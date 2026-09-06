using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// Decides the identifier the emitter gives a generated type, function or field:
/// <c>__cs_&lt;short name&gt;_&lt;stable hash&gt;</c> (docs/architecture.md section 9.2).
/// </summary>
/// <remarks>
/// The hash is decided by the .NET identity of what is being named and by nothing else - not
/// by the file the assembly was read from, not by the run, and not by the other members of the
/// same assembly - so a rebuild that did not change a type does not change the name of that
/// type, which is what makes incremental builds and size diffs readable.
/// <para>
/// The identity string this hashes is spelled here rather than borrowed from the SSA dump
/// writer on purpose: the dump is a debug view whose wording is free to change, and a change
/// to it must not move every generated identifier in every build.
/// </para>
/// </remarks>
internal static class MoonBitNames
{
    private const string Prefix = "__cs_";

    /// <summary>
    /// The identifiers MoonBit reserves. A C# field whose name is one of them cannot be
    /// carried over verbatim, so it is given a generated name like everything else.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "as", "async", "await", "break", "const", "continue", "derive", "else", "enum",
        "extern", "fn", "for", "guard", "if", "impl", "in", "is", "let", "loop", "match",
        "mut", "pub", "priv", "raise", "return", "self", "struct", "test", "trait", "try",
        "type", "typealias", "using", "while", "with",
    };

    /// <summary>The identifier of a type, decided by its fully qualified .NET name alone.</summary>
    public static string OfType(string fullName) => Compose(ShortName(fullName), "type " + fullName);

    /// <summary>
    /// The identifier of a method, decided by its owner, its name and its declared signature.
    /// The instance an instance method receives in slot 0 is not part of the signature, so
    /// making a method static or instance is the only thing that can move its name through it.
    /// </summary>
    public static string OfMethod(
        string ownerFullName,
        SpikeSsaMethod method,
        IReadOnlyList<SpikeSsaType> declaredParameterTypes)
    {
        var parameters = string.Join(",", declaredParameterTypes.Select(Canonical));
        var identity = "method " + ownerFullName + "::" + method.Name
            + "(" + parameters + ")->"
            + (method.ReturnType is null ? "void" : Canonical(method.ReturnType))
            + (method.IsStatic ? " static" : " instance");

        return Compose(ShortName(method.Name), identity);
    }

    /// <summary>
    /// The identifier of a struct field. A C# name that is already a MoonBit identifier is
    /// carried over as it stands, because the generated source has to be readable back to the
    /// C# it came from; anything else is given a generated name.
    /// </summary>
    public static string OfField(string ownerFullName, string fieldName) =>
        IsPlainIdentifier(fieldName) && !Reserved.Contains(fieldName)
            ? fieldName
            : Compose(ShortName(fieldName), "field " + ownerFullName + "::" + fieldName);

    /// <summary>The name of an SSA value, which is the number the SSA dump gives it.</summary>
    public static string OfValue(SpikeSsaValue value) =>
        "v" + value.Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The spelling of a type inside a hash input. It is deliberately not MoonBit's spelling
    /// and not the SSA dump's: it belongs to this file, and changing it changes every name.
    /// </summary>
    private static string Canonical(SpikeSsaType type) => type.Kind switch
    {
        SpikeSsaTypeKind.I32 => "i32",
        SpikeSsaTypeKind.I64 => "i64",
        SpikeSsaTypeKind.F64 => "f64",
        SpikeSsaTypeKind.Bool => "bool",
        SpikeSsaTypeKind.Ref => "ref<" + type.RefTypeName + ">",
        _ => "array<" + Canonical(type.ElementType!) + ">",
    };

    private static string Compose(string shortName, string identity) =>
        Prefix + shortName + "_" + StableHash(identity);

    /// <summary>
    /// The first eight bytes of the SHA-256 of the identity, in lower-case hexadecimal.
    /// <c>string.GetHashCode</c> is seeded per process and would give a different answer on
    /// every run, so it is not used here or anywhere else in the emitter.
    /// </summary>
    private static string StableHash(string identity)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));

        var text = new StringBuilder(16);
        for (var index = 0; index < 8; index++)
        {
            text.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>
    /// The last segment of a .NET name, with everything MoonBit would not accept inside an
    /// identifier replaced: <c>CsWasm.Samples.Poc.Point</c> reads as <c>Point</c> and
    /// <c>.ctor</c> as <c>_ctor</c>.
    /// </summary>
    private static string ShortName(string name)
    {
        var lastSeparator = name.LastIndexOfAny(['.', '/', '+']);
        var segment = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;

        if (segment.Length == 0)
        {
            segment = name;
        }

        var text = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            text.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return text.Length == 0 ? "_" : text.ToString();
    }

    private static bool IsPlainIdentifier(string name) =>
        name.Length > 0
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
