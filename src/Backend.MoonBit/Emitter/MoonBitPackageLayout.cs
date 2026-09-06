using System.Text;
using System.Text.Json;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>One generated file: where it goes below the package root, and what is in it.</summary>
/// <remarks>
/// The path is always spelled with '/' so that the layout is one value on every platform; the
/// code that writes the files turns it into a platform path.
/// </remarks>
public sealed record MoonBitPackageFile(string RelativePath, string Text);

/// <summary>
/// <c>emit_package</c>: turns an emitted module into the files <c>moon</c> reads
/// (docs/architecture.md section 9.1).
/// </summary>
/// <remarks>
/// This is the only place in cswasm that knows how a MoonBit package is spelled - the manifest
/// keys, the directory names, the way a wasm-gc export is declared, and where <c>moon</c> puts
/// the module it builds. When MoonBit changes any of them, this file is the one that changes
/// (docs/architecture.md section 9.5, docs/moonbit-packaging.md).
/// <para>
/// Nothing here touches the file system: the layout is a value, so it can be checked without
/// running a build.
/// </para>
/// </remarks>
public static class MoonBitPackageLayout
{
    /// <summary>The module name in <c>moon.mod.json</c>. Generated code is never published.</summary>
    public const string ModuleName = "cswasm/generated";

    public const string ModuleVersion = "0.1.0";

    /// <summary>The directory <c>moon.mod.json</c> points at, and the one package below it.</summary>
    public const string SourceDirectory = "src";

    public const string PackageName = "gen";

    /// <summary>The <c>--target</c> the backend builds, and the name moon gives its directory.</summary>
    public const string Target = "wasm-gc";

    /// <summary>The build profile, and the name moon gives its directory.</summary>
    public const string Profile = "release";

    /// <summary>The files of the package, in a fixed order.</summary>
    public static IReadOnlyList<MoonBitPackageFile> Files(MoonBitModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var package = SourceDirectory + "/" + PackageName + "/";

        return
        [
            new MoonBitPackageFile("moon.mod.json", ModuleManifest()),
            new MoonBitPackageFile(package + "moon.pkg.json", PackageManifest(module.Exports)),
            new MoonBitPackageFile(package + PackageName + ".mbt", module.Source),
        ];
    }

    /// <summary>
    /// Where <c>moon</c> is expected to leave the module, below the package root. It is a
    /// starting point for the search, not a promise: the layout is not part of MoonBit's
    /// contract and has moved between releases (tools/moonbit-smoke.sh).
    /// </summary>
    public static string ArtifactRelativePath { get; } =
        Path.Combine("_build", Target, Profile, "build", PackageName, PackageName + ".wasm");

    private static string ModuleManifest()
    {
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("name", ModuleName);
            writer.WriteString("version", ModuleVersion);
            writer.WriteString("source", SourceDirectory);
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// The package manifest. A wasm-gc function leaves the module only when the package's
    /// <c>link</c> setting for that target names it, and only when the function itself is
    /// public; both halves are decided here and in the function emitter beside it
    /// (docs/moonbit-packaging.md).
    /// </summary>
    private static string PackageManifest(IReadOnlyList<string> exports)
    {
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("link");
            writer.WriteStartObject(Target);
            writer.WriteStartArray("exports");

            foreach (var export in exports)
            {
                writer.WriteStringValue(export);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// Renders JSON the way the rest of the generated package is rendered: written in the
    /// order this file states, indented, and ended with a line feed on every platform.
    /// </summary>
    private static string Write(Action<Utf8JsonWriter> content)
    {
        using var buffer = new MemoryStream();

        var options = new JsonWriterOptions { Indented = true, NewLine = "\n" };

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            content(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
