using System.Text.RegularExpressions;
using CsWasm.Backend.MoonBit;
using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts BUILD-ARGS, BUILD-FIND, BUILD-STDERR, DIAG-5004, DIAG-PATH, DIAG-LINE,
/// DIAG-NOCSHARP, PKG-MOD, PKG-PKG, PKG-EXPORT and PKG-ONEPLACE.
/// </summary>
/// <remarks>
/// These are observed at the toolchain driver rather than at <c>cswasm compile</c>: the
/// generated MoonBit is decided by the emitter, so no C# input reaches a rejected build, and
/// the command line shows neither the arguments a build starts with nor where the module was
/// found. The entry points used here are the ones a run uses - the same argument list, the same
/// search, the same classification - so what a test observes is what a build does.
/// </remarks>
public sealed class MoonBitBuildTests
{
    /// <summary>A package directory that only holds what one test put in it.</summary>
    private sealed class Package : IDisposable
    {
        public Package() => Directory = System.IO.Directory.CreateTempSubdirectory("cswasm-build-test-").FullName;

        public string Directory { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    // BUILD-ARGS: issue #17 names the build `moon build --target wasm-gc --release`. A debug
    // build, or a build of the plain wasm target, is a different module.
    [Fact]
    public void TheBuildIsAReleaseBuildOfTheWasmGcTarget()
    {
        var arguments = MoonBitBuild.Arguments("/tmp/example");

        Assert.Equal(["-C", "/tmp/example", "build", "--target", "wasm-gc", "--release"], arguments);
    }

    // BUILD-FIND: where moon leaves the module is not part of MoonBit's contract, so a module
    // that is not at the expected path is still the module the run produced.
    [Fact]
    public void TheArtifactIsFoundEvenWhenItIsNotAtTheExpectedPath()
    {
        using var package = new Package();
        var elsewhere = Path.Combine(package.Directory, "some", "other", "layout", "gen.wasm");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllBytes(elsewhere, [0x00, 0x61, 0x73, 0x6d]);

        Assert.Equal(elsewhere, MoonBitBuild.FindArtifact(package.Directory));
    }

    // BUILD-FIND: a package that produced nothing is not a package whose module is somewhere
    // else. Returning some unrelated file here would report a half-success.
    [Fact]
    public void APackageWithNoModuleHasNoArtifact()
    {
        using var package = new Package();

        Assert.Null(MoonBitBuild.FindArtifact(package.Directory));
    }

    // DIAG-5004, DIAG-PATH, DIAG-LINE, DIAG-NOCSHARP and BUILD-STDERR: a package the pinned
    // compiler rejects is reported once, under its own code, pointing at the line of the
    // generated MoonBit the compiler pointed at, with what the compiler said attached.
    [Fact]
    public void ARejectedPackageIsReportedAsCsw5004AtTheGeneratedLine()
    {
        using var package = new Package();
        var module = new MoonBitModule("fn broken() -> Int {\n  this is not moonbit\n}\n", []);

        var diagnostic = Assert.Single(MoonBitBuild.Run(module, package.Directory, out var wasm));

        Assert.Null(wasm);
        Assert.Equal(DiagnosticCode.MoonBitBuildFailed, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // DIAG-PATH and DIAG-LINE: a location a reader can open, in the file that was rejected.
        Assert.NotNull(diagnostic.Location);
        Assert.Matches(@"\.mbt:\d+$", diagnostic.Location);
        Assert.True(
            File.Exists(diagnostic.Location[..diagnostic.Location.LastIndexOf(':')]),
            diagnostic.Location);

        // DIAG-NOCSHARP: writing the location back through to a C# position is Step 8.
        Assert.DoesNotContain("IL_", diagnostic.Location, StringComparison.Ordinal);

        // BUILD-STDERR: what the toolchain said reaches the user, not just that it failed.
        Assert.Contains("Parse error", diagnostic.Format(), StringComparison.Ordinal);
    }

    // KEEP-ALLORNOTHING at the toolchain end: a build that reports success without leaving a
    // module is a failure, not a module of zero bytes.
    [Fact]
    public void ABuildThatLeavesNoModuleIsReportedAsCsw5004()
    {
        using var package = new Package();

        // A package with no link setting: moonc compiles it and links nothing, which is the
        // one way to reach this end with the pinned toolchain (docs/moonbit-packaging.md).
        Write(package.Directory, "moon.mod.json", """{ "name": "cswasm/generated", "source": "src" }""");
        Write(package.Directory, "src/gen/moon.pkg.json", "{}");
        Write(package.Directory, "src/gen/gen.mbt", "pub fn kept() -> Int {\n  1\n}\n");

        var diagnostic = Assert.Single(MoonBitBuild.Build(package.Directory, out var wasm));

        Assert.Null(wasm);
        Assert.Equal(DiagnosticCode.MoonBitBuildFailed, diagnostic.Code);
    }

    // PKG-MOD, PKG-PKG: the files moon reads are generated, not asked of the user.
    [Fact]
    public void ThePackageCarriesItsTwoManifestsAndTheGeneratedSource()
    {
        var files = MoonBitPackageLayout.Files(new MoonBitModule("// source\n", []));

        Assert.Equal(
            ["moon.mod.json", "src/gen/moon.pkg.json", "src/gen/gen.mbt"],
            files.Select(file => file.RelativePath));

        Assert.Equal("// source\n", files.Single(file => file.RelativePath.EndsWith(".mbt", StringComparison.Ordinal)).Text);
    }

    // PKG-EXPORT: the exported names reach the manifest. An empty list, or a name of the
    // layout's own invention, fails here.
    [Fact]
    public void ThePackageManifestNamesTheExportedFunctions()
    {
        var files = MoonBitPackageLayout.Files(new MoonBitModule("// source\n", ["__cs_Sum_1234", "__cs_Other_5678"]));

        var manifest = files.Single(file => file.RelativePath.EndsWith("moon.pkg.json", StringComparison.Ordinal)).Text;

        Assert.Contains("__cs_Sum_1234", manifest, StringComparison.Ordinal);
        Assert.Contains("__cs_Other_5678", manifest, StringComparison.Ordinal);
    }

    // PKG-ONEPLACE: issue #17 requires the way a function is exported to be written down in one
    // place, so that a MoonBit-side change has one place to be answered. A second file that
    // spells the manifest keys is that requirement broken, whatever it is called.
    [Fact]
    public void TheLinkSettingIsSpelledInOneFile()
    {
        var sources = Directory.GetFiles(
            Path.Combine(TestPaths.RepositoryRoot, "src"),
            "*.cs",
            SearchOption.AllDirectories);

        var spelling = new Regex("\"link\"|\"exports\"", RegexOptions.None);

        var owners = sources
            .Where(path => spelling.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["MoonBitPackageLayout.cs"], owners);
    }

    private static void Write(string packageDirectory, string relativePath, string text)
    {
        var path = Path.Combine(packageDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
