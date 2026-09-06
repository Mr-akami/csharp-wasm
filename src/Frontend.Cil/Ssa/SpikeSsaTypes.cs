namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>
/// Maps the type names the CIL model uses onto the minimal type family, and refuses the rest.
/// </summary>
/// <remarks>
/// Issue #15 fixes the family at <c>i32 / i64 / f64 / bool / ref&lt;TypeId&gt; / array&lt;T&gt;</c>
/// and says not to widen it here. The names this refuses are the ones that would have to be
/// widened onto - <c>float32</c>, <c>char</c>, the narrow integers, <c>string</c> - so a body
/// using one is reported rather than rounded off to whichever member is nearest.
/// </remarks>
internal static class SpikeSsaTypes
{
    /// <summary>
    /// The ILAsm spellings that name a type outside the family. They are listed rather than
    /// inferred so that a name this step has never seen is a reference type, not a silent
    /// approximation of one of these.
    /// </summary>
    private static readonly HashSet<string> OutsideTheFamily = new(StringComparer.Ordinal)
    {
        "void", "char", "string", "object", "typedref",
        "int8", "uint8", "int16", "uint16", "uint32", "uint64",
        "float32", "native int", "native uint",
    };

    /// <summary>Returns null for a name outside the family, and for a null name.</summary>
    public static SpikeSsaType? Map(string? ilTypeName)
    {
        if (ilTypeName is null)
        {
            return null;
        }

        if (ilTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = Map(ilTypeName[..^2]);
            return element is null ? null : SpikeSsaType.ArrayOf(element);
        }

        switch (ilTypeName)
        {
            case "int32":
                return SpikeSsaType.Int32;
            case "int64":
                return SpikeSsaType.Int64;
            case "float64":
                return SpikeSsaType.Float64;
            case "bool":
                return SpikeSsaType.Boolean;
            default:
                return OutsideTheFamily.Contains(ilTypeName) ? null : SpikeSsaType.Reference(ilTypeName);
        }
    }

    /// <summary>
    /// The value <c>.locals init</c> gives a local of this type before anything assigns it.
    /// </summary>
    public static string ZeroLiteral(SpikeSsaType type) => type.Kind switch
    {
        SpikeSsaTypeKind.Bool => "false",
        SpikeSsaTypeKind.I32 or SpikeSsaTypeKind.I64 or SpikeSsaTypeKind.F64 => "0",
        _ => "null",
    };

    /// <summary>
    /// Renders a type the way the issue spells it. A <see cref="SpikeSsaTypeKind.Ref"/>
    /// without a name or a <see cref="SpikeSsaTypeKind.Array"/> without an element type is not
    /// a type, and is reported as the construction error it is rather than printed as
    /// something that looks like one.
    /// </summary>
    public static string Render(SpikeSsaType type) => type.Kind switch
    {
        SpikeSsaTypeKind.I32 => "i32",
        SpikeSsaTypeKind.I64 => "i64",
        SpikeSsaTypeKind.F64 => "f64",
        SpikeSsaTypeKind.Bool => "bool",
        SpikeSsaTypeKind.Ref when type.RefTypeName is not null => "ref<" + type.RefTypeName + ">",
        SpikeSsaTypeKind.Array when type.ElementType is not null => "array<" + Render(type.ElementType) + ">",
        _ => throw new ArgumentException(
            $"A {type.Kind} type is missing the name or the element type it is made of.",
            nameof(type)),
    };
}
