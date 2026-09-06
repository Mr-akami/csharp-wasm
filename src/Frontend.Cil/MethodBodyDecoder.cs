using System.Buffers.Binary;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace CsWasm.Frontend.Cil;

/// <summary>
/// Turns a method body into <see cref="MethodBodyModel"/>: the instruction stream with the
/// offset each instruction starts at, the local variable signature, the maximum stack depth
/// and the exception regions.
/// </summary>
/// <remarks>
/// Decoding takes no view on which instructions cswasm supports; that decision belongs to
/// <see cref="SupportedInstructions"/>. Keeping them apart is what lets a method the opcode
/// gate rejects still be read - a method with a catch region always contains
/// <c>leave</c>, which the gate refuses.
/// </remarks>
internal static class MethodBodyDecoder
{
    public static MethodBodyModel Decode(
        MetadataReader reader,
        MethodBodyBlock body,
        CilSignatureFormatter formatter)
    {
        return new MethodBodyModel(
            body.MaxStack,
            DecodeLocals(reader, body, formatter),
            DecodeInstructions(reader, body.GetILBytes() ?? [], formatter),
            DecodeExceptionRegions(reader, body, formatter));
    }

    private static IReadOnlyList<LocalModel> DecodeLocals(
        MetadataReader reader,
        MethodBodyBlock body,
        CilSignatureFormatter formatter)
    {
        if (body.LocalSignature.IsNil)
        {
            return [];
        }

        var signature = reader.GetStandaloneSignature(body.LocalSignature);
        var types = signature.DecodeLocalSignature(formatter, null);

        var locals = new List<LocalModel>(types.Length);
        for (var index = 0; index < types.Length; index++)
        {
            locals.Add(new LocalModel(index, types[index]));
        }

        return locals;
    }

    private static IReadOnlyList<ExceptionRegionModel> DecodeExceptionRegions(
        MetadataReader reader,
        MethodBodyBlock body,
        CilSignatureFormatter formatter)
    {
        var regions = new List<ExceptionRegionModel>(body.ExceptionRegions.Length);

        foreach (var region in body.ExceptionRegions)
        {
            var kind = region.Kind switch
            {
                System.Reflection.Metadata.ExceptionRegionKind.Catch => ExceptionRegionKind.Catch,
                System.Reflection.Metadata.ExceptionRegionKind.Filter => ExceptionRegionKind.Filter,
                System.Reflection.Metadata.ExceptionRegionKind.Finally => ExceptionRegionKind.Finally,
                _ => ExceptionRegionKind.Fault,
            };

            regions.Add(new ExceptionRegionModel(
                kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                kind == ExceptionRegionKind.Catch ? formatter.TypeName(reader, region.CatchType) : null));
        }

        return regions;
    }

    private static IReadOnlyList<IlInstruction> DecodeInstructions(
        MetadataReader reader,
        byte[] il,
        CilSignatureFormatter formatter)
    {
        var instructions = new List<IlInstruction>();
        var position = 0;

        while (position < il.Length)
        {
            var offset = position;
            var first = il[position++];

            IlOpCodeInfo? info;
            if (first == IlOpCodes.Prefix && position < il.Length)
            {
                info = IlOpCodes.LookupPrefixed(il[position++]);
            }
            else
            {
                info = IlOpCodes.Lookup(first);
            }

            if (info is null)
            {
                // An undefined byte makes every following offset meaningless, so it is
                // surfaced as an instruction the supported-opcode gate cannot accept rather
                // than skipped.
                instructions.Add(new IlInstruction(
                    offset,
                    "unknown.0x" + first.ToString("x2", CultureInfo.InvariantCulture),
                    null));
                continue;
            }

            var operandStart = position;
            position += IlOpCodes.FixedOperandSize(info.Operand);

            if (info.Operand == IlOperandKind.InlineSwitch)
            {
                position += 4 * BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(operandStart, 4));
            }

            instructions.Add(new IlInstruction(
                offset,
                info.Name,
                FormatOperand(reader, il, info, operandStart, position, formatter),
                IntOperand(info, il, operandStart, position),
                CallOperand(reader, il, info, operandStart, formatter),
                TypeOperand(reader, il, info, operandStart, formatter),
                FieldOperand(reader, il, info, operandStart, formatter)));
        }

        return instructions;
    }

    /// <summary>
    /// The number the operand denotes - a branch target, a variable slot or an integer
    /// constant - for the encodings that carry one, and for the macro forms that fold it into
    /// the opcode. Null for every other instruction, so an operand is never invented.
    /// </summary>
    private static int? IntOperand(IlOpCodeInfo info, byte[] il, int operandStart, int next)
    {
        var span = il.AsSpan();

        return info.Operand switch
        {
            IlOperandKind.ShortInlineVar => il[operandStart],
            IlOperandKind.InlineVar => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(operandStart, 2)),
            IlOperandKind.ShortInlineBrTarget => next + (sbyte)il[operandStart],
            IlOperandKind.InlineBrTarget => next + BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4)),
            IlOperandKind.InlineI => BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4)),

            // Only ldc.i4.s carries a signed byte; the alignment prefix carries a count that
            // names no value, exactly as in FormatOperand.
            IlOperandKind.ShortInlineI => string.Equals(info.Name, "ldc.i4.s", StringComparison.Ordinal)
                ? (sbyte)il[operandStart]
                : null,

            _ => MacroOperands.TryGetValue(info.Name, out var folded) ? folded : null,
        };
    }

    /// <summary>The slot or constant the short-hand opcodes fold into the opcode itself.</summary>
    private static readonly Dictionary<string, int> MacroOperands = new(StringComparer.Ordinal)
    {
        ["ldarg.0"] = 0, ["ldarg.1"] = 1, ["ldarg.2"] = 2, ["ldarg.3"] = 3,
        ["ldloc.0"] = 0, ["ldloc.1"] = 1, ["ldloc.2"] = 2, ["ldloc.3"] = 3,
        ["stloc.0"] = 0, ["stloc.1"] = 1, ["stloc.2"] = 2, ["stloc.3"] = 3,
        ["ldc.i4.m1"] = -1,
        ["ldc.i4.0"] = 0, ["ldc.i4.1"] = 1, ["ldc.i4.2"] = 2, ["ldc.i4.3"] = 3, ["ldc.i4.4"] = 4,
        ["ldc.i4.5"] = 5, ["ldc.i4.6"] = 6, ["ldc.i4.7"] = 7, ["ldc.i4.8"] = 8,
    };

    private static IlCallOperand? CallOperand(
        MetadataReader reader,
        byte[] il,
        IlOpCodeInfo info,
        int operandStart,
        CilSignatureFormatter formatter)
    {
        if (info.Operand != IlOperandKind.InlineMethod)
        {
            return null;
        }

        var method = MethodToken(reader, il, operandStart, formatter);
        if (method is null)
        {
            return null;
        }

        return new IlCallOperand(
            method.OwnerName,
            method.Name,
            method.Signature.ParameterTypes.Length,
            method.Signature.Header.IsInstance,
            string.Equals(method.Signature.ReturnType, "void", StringComparison.Ordinal));
    }

    private static IlFieldOperand? FieldOperand(
        MetadataReader reader,
        byte[] il,
        IlOpCodeInfo info,
        int operandStart,
        CilSignatureFormatter formatter)
    {
        if (info.Operand != IlOperandKind.InlineField)
        {
            return null;
        }

        var field = formatter.FieldToken(reader, Token(il, operandStart));
        return field is null ? null : new IlFieldOperand(field.OwnerName, field.Name);
    }

    private static string? TypeOperand(
        MetadataReader reader,
        byte[] il,
        IlOpCodeInfo info,
        int operandStart,
        CilSignatureFormatter formatter)
    {
        switch (info.Operand)
        {
            case IlOperandKind.InlineField:
                return formatter.FieldToken(reader, Token(il, operandStart))?.TypeName;

            case IlOperandKind.InlineType:
                return formatter.TypeName(reader, Token(il, operandStart));

            case IlOperandKind.InlineMethod:
            {
                var method = MethodToken(reader, il, operandStart, formatter);

                // newobj is the one method token whose result is not the return type: it
                // allocates the type that declares the constructor.
                return method is null
                    ? null
                    : string.Equals(info.Name, "newobj", StringComparison.Ordinal)
                        ? method.OwnerName
                        : method.Signature.ReturnType;
            }

            default:
                return null;
        }
    }

    private static MethodTokenInfo? MethodToken(
        MetadataReader reader,
        byte[] il,
        int operandStart,
        CilSignatureFormatter formatter) =>
        formatter.MethodToken(reader, Token(il, operandStart));

    private static EntityHandle Token(byte[] il, int operandStart) =>
        MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(operandStart, 4)));

    private static string? FormatOperand(
        MetadataReader reader,
        byte[] il,
        IlOpCodeInfo info,
        int operandStart,
        int next,
        CilSignatureFormatter formatter)
    {
        var span = il.AsSpan();

        switch (info.Operand)
        {
            case IlOperandKind.InlineNone:
                return null;

            case IlOperandKind.ShortInlineVar:
                return il[operandStart].ToString(CultureInfo.InvariantCulture);

            case IlOperandKind.InlineVar:
                return BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(operandStart, 2))
                    .ToString(CultureInfo.InvariantCulture);

            case IlOperandKind.ShortInlineI:
                // Only ldc.i4.s carries a signed byte; the alignment prefix carries a count.
                return string.Equals(info.Name, "ldc.i4.s", StringComparison.Ordinal)
                    ? ((sbyte)il[operandStart]).ToString(CultureInfo.InvariantCulture)
                    : il[operandStart].ToString(CultureInfo.InvariantCulture);

            case IlOperandKind.InlineI:
                return BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4))
                    .ToString(CultureInfo.InvariantCulture);

            case IlOperandKind.InlineI8:
                return BinaryPrimitives.ReadInt64LittleEndian(span.Slice(operandStart, 8))
                    .ToString(CultureInfo.InvariantCulture);

            case IlOperandKind.ShortInlineR:
                return BinaryPrimitives.ReadSingleLittleEndian(span.Slice(operandStart, 4))
                    .ToString("R", CultureInfo.InvariantCulture);

            case IlOperandKind.InlineR:
                return BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(operandStart, 8))
                    .ToString("R", CultureInfo.InvariantCulture);

            case IlOperandKind.ShortInlineBrTarget:
                return Label(next + (sbyte)il[operandStart]);

            case IlOperandKind.InlineBrTarget:
                return Label(next + BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4)));

            case IlOperandKind.InlineSwitch:
            {
                var count = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4));
                var targets = new StringBuilder("(");
                for (var i = 0; i < count; i++)
                {
                    if (i > 0)
                    {
                        targets.Append(", ");
                    }

                    targets.Append(Label(
                        next + BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart + 4 + (4 * i), 4))));
                }

                return targets.Append(')').ToString();
            }

            case IlOperandKind.InlineString:
            {
                var handle = MetadataTokens.UserStringHandle(
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4)));
                return "\"" + reader.GetUserString(handle) + "\"";
            }

            case IlOperandKind.InlineSig:
                // A standalone signature names no member; the token is the only stable
                // identity it has, and calli is refused by the supported-opcode gate anyway.
                return "0x" + BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4))
                    .ToString("x8", CultureInfo.InvariantCulture);

            case IlOperandKind.InlineType:
                return formatter.TypeName(
                    reader,
                    MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4))));

            default:
                return formatter.MemberName(
                    reader,
                    MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(operandStart, 4))));
        }
    }

    /// <summary>The label form the dump and the branch operands share, so the two always agree.</summary>
    public static string Label(int offset) => "IL_" + offset.ToString("x4", CultureInfo.InvariantCulture);
}
