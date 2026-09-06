using System.Text.RegularExpressions;
using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts CLI-COMPILE, CLI-NO0001, CLI-KEEP, PKG-MOD, PKG-PKG, PKG-EXPORT, PKG-TEMP,
/// BUILD-COPY, DONE-COMPILE, DONE-VALIDATE, DONE-STRUCT, DONE-ARRAY and KEEP-ALLORNOTHING,
/// observed at the entry point issue #17 names: <c>cswasm compile &lt;dll&gt; -o &lt;wasm&gt;</c>.
/// </summary>
/// <remarks>
/// Every test drives <see cref="CommandLine.Run"/>, the one process entry point, rather than
/// the classes behind it: issue #17 states its completion condition as what that command does,
/// and a value reached only by calling an internal helper is not wired up for a user.
/// </remarks>
public sealed class CompileCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>A directory that only holds what one test asked cswasm to write.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace() => Directory = System.IO.Directory.CreateTempSubdirectory("cswasm-compile-test-").FullName;

        public string Directory { get; }

        public string Output => Path.Combine(Directory, "Sample.wasm");

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private static string CompilePoc(Workspace work)
    {
        var (code, _, error) = Run("compile", SamplePaths.Poc, "-o", work.Output);

        Assert.True(code == CommandLine.ExitSuccess, error);
        return work.Output;
    }

    // CLI-COMPILE: a command whose options --help does not mention cannot be reached by a user
    // who has only the tool in front of them.
    [Fact]
    public void UsageDisclosesTheCompileCommandWithItsOutputAndKeepIntermediatesOptions()
    {
        var (code, output, _) = Run("--help");

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Matches(@"compile[^\r\n]*-o[^\r\n]*", output);
        Assert.Contains("--keep-intermediates", output, StringComparison.Ordinal);
    }

    // CLI-COMPILE, DONE-COMPILE, BUILD-COPY: the completion condition of issue #17 is that
    // `cswasm compile Sample.dll -o Sample.wasm` succeeds and leaves a module at -o. A run that
    // reports success without writing the file, or writes an empty one, fails here.
    [Fact]
    public void CompilingTheProofOfConceptSampleWritesTheRequestedModule()
    {
        using var work = new Workspace();

        var wasm = CompilePoc(work);

        Assert.True(File.Exists(wasm), wasm + " was not written.");
        Assert.NotEqual(0, new FileInfo(wasm).Length);
    }

    // CLI-NO0001: issue #17 lifts the CSW0001 refusal for compile. The code must not reach the
    // user from the command any more, whatever else the run reports.
    [Fact]
    public void CompileNoLongerRefusesWithCsw0001()
    {
        using var work = new Workspace();

        var (_, output, error) = Run("compile", SamplePaths.Poc, "-o", work.Output);

        Assert.DoesNotContain("CSW0001", error, StringComparison.Ordinal);
        Assert.DoesNotContain("CSW0001", output, StringComparison.Ordinal);
    }

    // DONE-VALIDATE: the completion condition names wasm-tools validate, so the test runs it.
    [Fact]
    public void TheCompiledModuleValidates()
    {
        using var work = new Workspace();
        var wasm = CompilePoc(work);

        var (code, _, error) = WasmTools.Run("validate", wasm);

        Assert.True(code == 0, error);
    }

    // DONE-STRUCT: WasmGC, not a linear-memory heap. The judgement is the one
    // tools/moonbit-smoke.sh already applies to the hand-written smoke module.
    [Fact]
    public void TheCompiledModuleDeclaresAWasmGcStructType()
    {
        using var work = new Workspace();
        var wasm = CompilePoc(work);

        var (code, printed, error) = WasmTools.Run("print", wasm);

        Assert.True(code == 0, error);
        Assert.Matches(@"\(type .*struct", printed);
    }

    // DONE-ARRAY: stated separately from the struct type - Point[] and Point are two mappings,
    // and a module that kept only one of them still fails the completion condition.
    [Fact]
    public void TheCompiledModuleDeclaresAWasmGcArrayType()
    {
        using var work = new Workspace();
        var wasm = CompilePoc(work);

        var (code, printed, error) = WasmTools.Run("print", wasm);

        Assert.True(code == 0, error);
        Assert.Matches(@"\(type .*array", printed);
    }

    // PKG-TEMP: issue #17 decides the generated package goes to a temporary or cache directory.
    // A default run must therefore leave the directory the user invoked cswasm from untouched -
    // no _build, no package files, no stray .mbt.
    [Fact]
    public void ADefaultCompileLeavesNothingBehindInTheWorkingDirectory()
    {
        using var work = new Workspace();
        var before = Directory
            .GetFileSystemEntries(Environment.CurrentDirectory)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        CompilePoc(work);

        var after = Directory
            .GetFileSystemEntries(Environment.CurrentDirectory)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(before, after);
    }

    // CLI-KEEP: keeping the generated MoonBit is only useful for debugging if the user is told
    // where it went, so the reported path is part of the contract, not just the files.
    [Fact]
    public void KeepIntermediatesReportsADirectoryThatHoldsTheGeneratedMoonBit()
    {
        using var work = new Workspace();
        var kept = KeepIntermediates(work);

        try
        {
            Assert.NotEmpty(Directory.GetFiles(kept, "*.mbt", SearchOption.AllDirectories));
        }
        finally
        {
            Remove(kept);
        }
    }

    // PKG-MOD: the module manifest is generated, not something the user is asked to write.
    [Fact]
    public void TheGeneratedPackageCarriesAMoonModJson()
    {
        using var work = new Workspace();
        var kept = KeepIntermediates(work);

        try
        {
            Assert.NotEmpty(Directory.GetFiles(kept, "moon.mod.json", SearchOption.AllDirectories));
        }
        finally
        {
            Remove(kept);
        }
    }

    // PKG-PKG.
    [Fact]
    public void TheGeneratedPackageCarriesAMoonPkgJson()
    {
        using var work = new Workspace();
        var kept = KeepIntermediates(work);

        try
        {
            Assert.NotEmpty(Directory.GetFiles(kept, "moon.pkg.json", SearchOption.AllDirectories));
        }
        finally
        {
            Remove(kept);
        }
    }

    // PKG-EXPORT: "including the functions to export". The expected name is read out of what
    // the backend generated for Sum rather than written down here, so the test observes that
    // the two agree instead of freezing an identifier spelling the backend owns.
    [Fact]
    public void TheGeneratedMoonPkgJsonNamesTheFunctionGeneratedForSum()
    {
        using var work = new Workspace();
        var generated = GeneratedFunctionNameForSum();
        var kept = KeepIntermediates(work);

        try
        {
            var manifests = Directory
                .GetFiles(kept, "moon.pkg.json", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .ToList();

            Assert.Contains(manifests, text => text.Contains(generated, StringComparison.Ordinal));
        }
        finally
        {
            Remove(kept);
        }
    }

    // KEEP-ALLORNOTHING (docs/diagnostics.md rule 1): a frontend refusal leaves no module. The
    // sample is the one whose local has its address taken, which dump moonbit already refuses.
    [Fact]
    public void AFrontendRefusalWritesNoModule()
    {
        using var work = new Workspace();

        var (code, _, error) = Run("compile", SamplePaths.Unsupported, "-o", work.Output);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW1003", error, StringComparison.Ordinal);
        Assert.False(File.Exists(work.Output), work.Output + " was written by a failed run.");
    }

    // KEEP-ALLORNOTHING: a backend refusal is the same answer at a different stage.
    [Fact]
    public void ABackendRefusalWritesNoModule()
    {
        using var work = new Workspace();

        var (code, _, error) = Run("compile", SamplePaths.BackendUnsupported, "-o", work.Output);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("error CSW4001", error, StringComparison.Ordinal);
        Assert.False(File.Exists(work.Output), work.Output + " was written by a failed run.");
    }

    // KEEP-ALLORNOTHING: the input check the dump commands apply is the same one here.
    [Fact]
    public void CompileOfAMissingFileReportsInputNotFoundAndWritesNoModule()
    {
        using var work = new Workspace();
        Assert.False(File.Exists(SamplePaths.Missing));

        var (code, _, error) = Run("compile", SamplePaths.Missing, "-o", work.Output);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0003", error, StringComparison.Ordinal);
        Assert.False(File.Exists(work.Output), work.Output + " was written by a failed run.");
    }

    // KEEP-ALLORNOTHING: half-succeeding includes destroying what was already at -o. Truncating
    // the file before the toolchain has produced anything would fail here.
    [Fact]
    public void AFailedCompileDoesNotDisturbAnExistingOutputFile()
    {
        using var work = new Workspace();
        const string Existing = "a module from an earlier run";
        File.WriteAllText(work.Output, Existing);

        var (code, _, _) = Run("compile", SamplePaths.Unsupported, "-o", work.Output);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Equal(Existing, File.ReadAllText(work.Output));
    }

    // CLI-COMPILE: argument shapes the command does not accept are rejected by the parser
    // rather than throwing, exactly as for the dump subcommands.
    [Fact]
    public void ACompileInvocationWithoutAnInputIsRejected()
    {
        var (code, output, error) = Run("compile");

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0002", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// Compiles the proof-of-concept sample with <c>--keep-intermediates</c> and returns the
    /// directory the run reported. The path is picked out of stdout by asking the file system
    /// which token is a directory, so the surrounding wording stays free to change.
    /// </summary>
    private static string KeepIntermediates(Workspace work)
    {
        var (code, output, error) = Run("compile", SamplePaths.Poc, "-o", work.Output, "--keep-intermediates");

        Assert.True(code == CommandLine.ExitSuccess, error);

        var directories = output
            .Split([' ', '\t', '\r', '\n', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1 && Directory.Exists(token))
            .ToList();

        Assert.True(
            directories.Count > 0,
            "--keep-intermediates reported no directory a user could open:\n" + output);

        return directories[0];
    }

    /// <summary>
    /// The name the backend gave <c>Sample::Sum</c>, read from the command that prints the
    /// generated MoonBit.
    /// </summary>
    private static string GeneratedFunctionNameForSum()
    {
        var (code, source, error) = Run("dump", "moonbit", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);

        var match = Regex.Match(source, @"fn\s+([A-Za-z_][A-Za-z0-9_]*Sum[A-Za-z0-9_]*)\s*\(");
        Assert.True(match.Success, "No generated function for Sum in:\n" + source);

        return match.Groups[1].Value;
    }

    private static void Remove(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
