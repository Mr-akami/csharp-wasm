using System.Globalization;

namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>What one instruction does to the state vector.</summary>
internal enum StepKind
{
    /// <summary>Nothing: the instruction has no effect worth a value.</summary>
    None,

    /// <summary>Pushes the value of an argument, which the entry block defined once.</summary>
    PushArgument,

    /// <summary>Pushes the current value of a promoted local.</summary>
    PushLocal,

    /// <summary>Pops a value and makes it the current value of a promoted local.</summary>
    StoreLocal,

    /// <summary>Pops its operands and, unless <see cref="Step.ResultType"/> is null, defines a value.</summary>
    Value,

    Return,
    Branch,
    CondBranch,
}

/// <summary>
/// One instruction described as a stack effect, so that the pass over types and the pass over
/// values apply the same rule.
/// </summary>
internal sealed record Step(
    StepKind Kind,
    int Slot = 0,
    string Op = "",
    string? Detail = null,
    int PopCount = 0,
    SpikeSsaType? ResultType = null,
    string? Condition = null,
    SpikeSsaMemberReference? Member = null)
{
    public static Step Nothing { get; } = new(StepKind.None);
}

/// <summary>The types a body's arguments, locals and result have once mapped onto the family.</summary>
internal sealed record SpikeSsaSignature(
    IReadOnlyList<SpikeSsaType> Arguments,
    IReadOnlyList<SpikeSsaType> Locals,
    SpikeSsaType? ReturnType);

/// <summary>
/// The instruction set this step can turn into values, and the value operation each
/// instruction becomes.
/// </summary>
/// <remarks>
/// There is no default case that shrugs: an instruction with no description here is refused by
/// the caller under CSW1004, because a normalised body that quietly dropped an instruction
/// would describe a program that is not the one in the file (docs/diagnostics.md rule 1).
/// </remarks>
internal static class SpikeSsaSteps
{
    /// <summary>The instructions that make a local's address, which stops it becoming a value.</summary>
    public static readonly HashSet<string> AddressOfLocal = new(StringComparer.Ordinal)
    {
        "ldloca", "ldloca.s",
    };

    private static readonly HashSet<string> LoadArgument = new(StringComparer.Ordinal)
    {
        "ldarg", "ldarg.s", "ldarg.0", "ldarg.1", "ldarg.2", "ldarg.3",
    };

    private static readonly HashSet<string> LoadLocal = new(StringComparer.Ordinal)
    {
        "ldloc", "ldloc.s", "ldloc.0", "ldloc.1", "ldloc.2", "ldloc.3",
    };

    private static readonly HashSet<string> StoreLocal = new(StringComparer.Ordinal)
    {
        "stloc", "stloc.s", "stloc.0", "stloc.1", "stloc.2", "stloc.3",
    };

    private static readonly HashSet<string> LoadConstant = new(StringComparer.Ordinal)
    {
        "ldc.i4", "ldc.i4.s", "ldc.i4.m1",
        "ldc.i4.0", "ldc.i4.1", "ldc.i4.2", "ldc.i4.3", "ldc.i4.4",
        "ldc.i4.5", "ldc.i4.6", "ldc.i4.7", "ldc.i4.8",
    };

    private static readonly HashSet<string> LoadElement = new(StringComparer.Ordinal)
    {
        "ldelem", "ldelem.i", "ldelem.i1", "ldelem.u1", "ldelem.i2", "ldelem.u2",
        "ldelem.i4", "ldelem.u4", "ldelem.i8", "ldelem.r4", "ldelem.r8", "ldelem.ref",
    };

    private static readonly HashSet<string> StoreElement = new(StringComparer.Ordinal)
    {
        "stelem", "stelem.i", "stelem.i1", "stelem.i2", "stelem.i4", "stelem.i8",
        "stelem.r4", "stelem.r8", "stelem.ref",
    };

    /// <summary>The comparison each conditional branch makes, and how many values it compares.</summary>
    private static readonly Dictionary<string, (string Condition, int PopCount)> Conditions =
        new(StringComparer.Ordinal)
        {
            ["brtrue"] = ("true", 1),
            ["brtrue.s"] = ("true", 1),
            ["brfalse"] = ("false", 1),
            ["brfalse.s"] = ("false", 1),
            ["blt"] = ("lt", 2),
            ["blt.s"] = ("lt", 2),
            ["blt.un"] = ("lt.un", 2),
            ["blt.un.s"] = ("lt.un", 2),
        };

    /// <summary>
    /// Describes <paramref name="instruction"/> against the evaluation stack it sees, or
    /// returns null and says in <paramref name="reason"/> why this step cannot normalise it.
    /// </summary>
    public static Step? Describe(
        IlInstruction instruction,
        IReadOnlyList<SpikeSsaType> stack,
        SpikeSsaSignature signature,
        out string reason)
    {
        reason = string.Empty;
        var name = instruction.OpCodeName;

        if (string.Equals(name, "nop", StringComparison.Ordinal))
        {
            return Step.Nothing;
        }

        if (LoadArgument.Contains(name))
        {
            return TrySlot(instruction, signature.Arguments.Count, "argument", out var slot, out reason)
                ? new Step(StepKind.PushArgument, Slot: slot)
                : null;
        }

        if (LoadLocal.Contains(name))
        {
            return TrySlot(instruction, signature.Locals.Count, "local", out var slot, out reason)
                ? new Step(StepKind.PushLocal, Slot: slot)
                : null;
        }

        if (StoreLocal.Contains(name))
        {
            if (!TrySlot(instruction, signature.Locals.Count, "local", out var slot, out reason)
                || !TryPeek(stack, 0, instruction, out _, out reason))
            {
                return null;
            }

            return new Step(StepKind.StoreLocal, Slot: slot, PopCount: 1);
        }

        if (LoadConstant.Contains(name))
        {
            if (instruction.IntOperand is not int value)
            {
                reason = $"'{name}' at {MethodBodyDecoder.Label(instruction.Offset)} carries no constant.";
                return null;
            }

            return new Step(
                StepKind.Value,
                Op: "const",
                Detail: value.ToString(CultureInfo.InvariantCulture),
                ResultType: SpikeSsaType.Int32);
        }

        if (LoadElement.Contains(name))
        {
            if (!TryPeekArray(stack, 1, instruction, out var array, out reason))
            {
                return null;
            }

            return new Step(StepKind.Value, Op: "array.get", PopCount: 2, ResultType: array.ElementType);
        }

        if (StoreElement.Contains(name))
        {
            return TryPeekArray(stack, 2, instruction, out _, out reason)
                ? new Step(StepKind.Value, Op: "array.set", PopCount: 3)
                : null;
        }

        if (Conditions.TryGetValue(name, out var condition))
        {
            if (!TryPeek(stack, condition.PopCount - 1, instruction, out _, out reason))
            {
                return null;
            }

            return new Step(StepKind.CondBranch, PopCount: condition.PopCount, Condition: condition.Condition);
        }

        return name switch
        {
            "add" => Arithmetic(instruction, stack, "add", out reason),
            "clt" => Comparison(instruction, stack, "lt", out reason),
            "conv.i4" => Convert(instruction, stack, out reason),
            "ldlen" => Length(instruction, stack, out reason),
            "ldfld" => LoadField(instruction, stack, out reason),
            "stfld" => StoreField(instruction, stack, out reason),
            "newarr" => NewArray(instruction, stack, out reason),
            "newobj" => NewObject(instruction, stack, out reason),
            "call" => Call(instruction, stack, out reason),
            "br" or "br.s" => new Step(StepKind.Branch),
            "ret" => ReturnStep(instruction, stack, signature, out reason),
            _ => Unmodelled(instruction, out reason),
        };
    }

    private static Step? Unmodelled(IlInstruction instruction, out string reason)
    {
        reason = $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} "
            + "has no value form in this step.";
        return null;
    }

    private static Step? Arithmetic(
        IlInstruction instruction,
        IReadOnlyList<SpikeSsaType> stack,
        string op,
        out string reason)
    {
        if (!TryPeek(stack, 0, instruction, out var right, out reason)
            || !TryPeek(stack, 1, instruction, out var left, out reason))
        {
            return null;
        }

        if (left != right)
        {
            reason = $"'{instruction.OpCodeName}' combines a {SpikeSsaTypes.Render(left)} with a "
                + $"{SpikeSsaTypes.Render(right)}.";
            return null;
        }

        return new Step(StepKind.Value, Op: op, PopCount: 2, ResultType: left);
    }

    private static Step? Comparison(
        IlInstruction instruction,
        IReadOnlyList<SpikeSsaType> stack,
        string op,
        out string reason)
    {
        if (!TryPeek(stack, 0, instruction, out var right, out reason)
            || !TryPeek(stack, 1, instruction, out var left, out reason))
        {
            return null;
        }

        if (left != right)
        {
            reason = $"'{instruction.OpCodeName}' compares a {SpikeSsaTypes.Render(left)} with a "
                + $"{SpikeSsaTypes.Render(right)}.";
            return null;
        }

        return new Step(StepKind.Value, Op: op, PopCount: 2, ResultType: SpikeSsaType.Boolean);
    }

    private static Step? Convert(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason)
    {
        if (!TryPeek(stack, 0, instruction, out var source, out reason))
        {
            return null;
        }

        if (source.Kind is not (SpikeSsaTypeKind.I32 or SpikeSsaTypeKind.I64 or SpikeSsaTypeKind.F64))
        {
            reason = $"'conv.i4' converts a {SpikeSsaTypes.Render(source)}, which is not a number.";
            return null;
        }

        return new Step(StepKind.Value, Op: "conv.i4", PopCount: 1, ResultType: SpikeSsaType.Int32);
    }

    // ldlen pushes a native int in ECMA-335, and the type family has none. The length is
    // modelled as i32, which is the type the conv.i4 the C# compiler emits right after it
    // would give anyway.
    private static Step? Length(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason) =>
        TryPeekArray(stack, 0, instruction, out _, out reason)
            ? new Step(StepKind.Value, Op: "array.length", PopCount: 1, ResultType: SpikeSsaType.Int32)
            : null;

    private static Step? LoadField(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason)
    {
        if (!TryPeek(stack, 0, instruction, out _, out reason))
        {
            return null;
        }

        var fieldType = SpikeSsaTypes.Map(instruction.TypeOperand);
        if (fieldType is null)
        {
            reason = $"the field '{instruction.Operand}' has a type outside the family this step models.";
            return null;
        }

        return new Step(
            StepKind.Value,
            Op: "field.get",
            Detail: instruction.Operand,
            PopCount: 1,
            ResultType: fieldType,
            Member: FieldReference(instruction));
    }

    private static Step? StoreField(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason) =>
        TryPeek(stack, 1, instruction, out _, out reason)
            ? new Step(
                StepKind.Value,
                Op: "field.set",
                Detail: instruction.Operand,
                PopCount: 2,
                Member: FieldReference(instruction))
            : null;

    /// <summary>
    /// The field the token names, or null when the instruction carries no field token. A
    /// missing token is not refused here: it costs a consumer the field's identity, which is
    /// that consumer's decision to report, and refusing it would narrow what normalises.
    /// </summary>
    private static SpikeSsaMemberReference? FieldReference(IlInstruction instruction) =>
        instruction.FieldOperand is { } field
            ? new SpikeSsaMemberReference(field.OwnerName, field.Name)
            : null;

    private static Step? NewArray(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason)
    {
        if (!TryPeek(stack, 0, instruction, out _, out reason))
        {
            return null;
        }

        var elementType = SpikeSsaTypes.Map(instruction.TypeOperand);
        if (elementType is null)
        {
            reason = $"'newarr' makes an array of '{instruction.TypeOperand}', which is outside the family "
                + "this step models.";
            return null;
        }

        return new Step(
            StepKind.Value,
            Op: "new.array",
            Detail: instruction.TypeOperand,
            PopCount: 1,
            ResultType: SpikeSsaType.ArrayOf(elementType));
    }

    private static Step? NewObject(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason)
    {
        if (instruction.CallOperand is not { } callee)
        {
            reason = "'newobj' names no constructor.";
            return null;
        }

        // newobj allocates the instance rather than popping it, so only the declared
        // parameters come off the stack.
        if (!TryDepth(stack, callee.ArgumentCount, instruction, out reason))
        {
            return null;
        }

        var constructed = SpikeSsaTypes.Map(instruction.TypeOperand);
        if (constructed is null)
        {
            reason = $"'newobj' makes a '{instruction.TypeOperand}', which is outside the family this step "
                + "models.";
            return null;
        }

        return new Step(
            StepKind.Value,
            Op: "new.object",
            Detail: callee.MemberName,
            PopCount: callee.ArgumentCount,
            ResultType: constructed,
            Member: new SpikeSsaMemberReference(callee.OwnerName, callee.Name));
    }

    private static Step? Call(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out string reason)
    {
        if (instruction.CallOperand is not { } callee)
        {
            reason = "'call' names no method.";
            return null;
        }

        var popCount = callee.ArgumentCount + (callee.HasThis ? 1 : 0);
        if (!TryDepth(stack, popCount, instruction, out reason))
        {
            return null;
        }

        SpikeSsaType? resultType = null;
        if (!callee.ReturnsVoid)
        {
            resultType = SpikeSsaTypes.Map(instruction.TypeOperand);
            if (resultType is null)
            {
                reason = $"'{callee.MemberName}' returns '{instruction.TypeOperand}', which is outside the "
                    + "family this step models.";
                return null;
            }
        }

        return new Step(
            StepKind.Value,
            Op: "call",
            Detail: callee.MemberName,
            PopCount: popCount,
            ResultType: resultType,
            Member: new SpikeSsaMemberReference(callee.OwnerName, callee.Name));
    }

    private static Step? ReturnStep(
        IlInstruction instruction,
        IReadOnlyList<SpikeSsaType> stack,
        SpikeSsaSignature signature,
        out string reason)
    {
        reason = string.Empty;

        if (signature.ReturnType is null)
        {
            return new Step(StepKind.Return);
        }

        if (!TryPeek(stack, 0, instruction, out var returned, out reason))
        {
            return null;
        }

        if (returned != signature.ReturnType)
        {
            reason = $"'ret' returns a {SpikeSsaTypes.Render(returned)} from a method declared to return "
                + $"{SpikeSsaTypes.Render(signature.ReturnType)}.";
            return null;
        }

        return new Step(StepKind.Return, PopCount: 1);
    }

    private static bool TrySlot(
        IlInstruction instruction,
        int count,
        string what,
        out int slot,
        out string reason)
    {
        slot = 0;
        reason = string.Empty;

        if (instruction.IntOperand is not int value)
        {
            reason = $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} names no "
                + what + " slot.";
            return false;
        }

        if (value < 0 || value >= count)
        {
            reason = $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} names "
                + $"{what} slot {value.ToString(CultureInfo.InvariantCulture)}, and the method has "
                + $"{count.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        slot = value;
        return true;
    }

    private static bool TryDepth(
        IReadOnlyList<SpikeSsaType> stack,
        int needed,
        IlInstruction instruction,
        out string reason)
    {
        reason = string.Empty;

        if (stack.Count >= needed)
        {
            return true;
        }

        reason = $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} needs "
            + $"{needed.ToString(CultureInfo.InvariantCulture)} value(s) on the evaluation stack, and "
            + $"{stack.Count.ToString(CultureInfo.InvariantCulture)} are there.";
        return false;
    }

    private static bool TryPeek(
        IReadOnlyList<SpikeSsaType> stack,
        int fromTop,
        IlInstruction instruction,
        out SpikeSsaType type,
        out string reason)
    {
        type = SpikeSsaType.Int32;

        if (!TryDepth(stack, fromTop + 1, instruction, out reason))
        {
            return false;
        }

        type = stack[stack.Count - 1 - fromTop];
        return true;
    }

    private static bool TryPeekArray(
        IReadOnlyList<SpikeSsaType> stack,
        int fromTop,
        IlInstruction instruction,
        out SpikeSsaType array,
        out string reason)
    {
        if (!TryPeek(stack, fromTop, instruction, out array, out reason))
        {
            return false;
        }

        if (array is { Kind: SpikeSsaTypeKind.Array, ElementType: not null })
        {
            return true;
        }

        reason = $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} works on a "
            + $"{SpikeSsaTypes.Render(array)}, which is not an array.";
        return false;
    }
}
