using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// Spells a value's type the way MoonBit does, following the mapping issue #16 fixes:
/// a C# class is a struct, and <c>T[]</c> is <c>Array[T]</c>.
/// </summary>
/// <remarks>
/// This is the only place in cswasm that knows how MoonBit writes a type. The passes in front
/// of the emitter carry the minimal type family and nothing about the syntax it ends up in
/// (docs/architecture.md section 4.2).
/// </remarks>
internal static class MoonBitTypeNames
{
    /// <summary>
    /// The MoonBit spelling of <paramref name="type"/>, or null when the type is not one this
    /// step can write down - a reference with no type name, or an array with no element type.
    /// </summary>
    public static string? Of(SpikeSsaType type) => type.Kind switch
    {
        SpikeSsaTypeKind.I32 => "Int",
        SpikeSsaTypeKind.I64 => "Int64",
        SpikeSsaTypeKind.F64 => "Double",
        SpikeSsaTypeKind.Bool => "Bool",
        SpikeSsaTypeKind.Ref when type.RefTypeName is not null => MoonBitNames.OfType(type.RefTypeName),
        SpikeSsaTypeKind.Array when type.ElementType is not null => Element(type.ElementType),
        _ => null,
    };

    /// <summary>The type a function with no return value has.</summary>
    public const string Unit = "Unit";

    private static string? Element(SpikeSsaType elementType)
    {
        var element = Of(elementType);
        return element is null ? null : "Array[" + element + "]";
    }
}
