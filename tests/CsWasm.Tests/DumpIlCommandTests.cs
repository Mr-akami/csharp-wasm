using System.Text.RegularExpressions;
using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts CIL-DUMP, CIL-CSW1001 and CIL-INPUT-MISSING at the entry point the issue names:
/// <c>cswasm dump il &lt;dll&gt;</c>.
/// </summary>
public sealed class DumpIlCommandTests
{
    private static readonly Regex InstructionLine =
        new(@"^\s*IL_(?<offset>[0-9a-f]{4}): (?<opcode>\S+)", RegexOptions.ExplicitCapture);

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // Requirement 14 plus the reachability contract: a command the user cannot discover from
    // --help is not wired up.
    [Fact]
    public void UsageDisclosesTheDumpCommand()
    {
        var (code, output, _) = Run("--help");

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Contains("dump il", output, StringComparison.Ordinal);
    }

    // CIL-DUMP (requirements 15, 16, 22, 23).
    [Fact]
    public void DumpOfTheProofOfConceptSampleMatchesTheSnapshot()
    {
        var (code, output, error) = Run("dump", "il", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);
        Assert.Equal(string.Empty, error);
        Snapshot.Matches("dump-il-poc.txt", output);
    }

    // Requirement 16: the line separator is part of the format, not the platform's.
    [Fact]
    public void DumpUsesLineFeedsOnly()
    {
        var (_, output, _) = Run("dump", "il", SamplePaths.Poc);

        Assert.DoesNotContain("\r", output, StringComparison.Ordinal);
    }

    // Requirement 16: nothing that varies between machines or runs may reach the output.
    [Fact]
    public void DumpDoesNotLeakTheInputPath()
    {
        var (_, output, _) = Run("dump", "il", SamplePaths.Poc);

        Assert.DoesNotContain(AppContext.BaseDirectory, output, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, output, StringComparison.Ordinal);
    }

    // Requirement 22, the completion condition: Sum's instruction stream, offsets and opcodes,
    // asserted independently of the recorded snapshot.
    [Fact]
    public void DumpEmitsTheInstructionStreamOfSum()
    {
        var (_, output, _) = Run("dump", "il", SamplePaths.Poc);

        var expected = new[]
        {
            "IL_0000: ldc.i4.0",
            "IL_0001: stloc.0",
            "IL_0002: ldc.i4.0",
            "IL_0003: stloc.1",
            "IL_0004: br.s",
            "IL_0006: ldloc.0",
            "IL_0007: ldarg.0",
            "IL_0008: ldloc.1",
            "IL_0009: ldelem.ref",
            "IL_000a: ldfld",
            "IL_000f: ldarg.0",
            "IL_0010: ldloc.1",
            "IL_0011: ldelem.ref",
            "IL_0012: ldfld",
            "IL_0017: add",
            "IL_0018: add",
            "IL_0019: stloc.0",
            "IL_001a: ldloc.1",
            "IL_001b: ldc.i4.1",
            "IL_001c: add",
            "IL_001d: stloc.1",
            "IL_001e: ldloc.1",
            "IL_001f: ldarg.0",
            "IL_0020: ldlen",
            "IL_0021: conv.i4",
            "IL_0022: blt.s",
            "IL_0024: ldloc.0",
            "IL_0025: ret",
        };

        Assert.Equal(expected, InstructionsOfMethod(output, "Sum"));
    }

    // CIL-CSW1001 through the driver (requirement 17): non-zero exit, the code on stderr, and
    // no partial dump on stdout (docs/diagnostics.md rule 1).
    [Fact]
    public void DumpFailsOnUnsupportedOpcodesWithoutEmittingOutput()
    {
        var (code, output, error) = Run("dump", "il", SamplePaths.Unsupported);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW1001", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // CIL-CSW1002 through the driver (requirement 18): reported alongside CSW1001 from the same
    // run, under its own code.
    [Fact]
    public void DumpReportsUnsupportedMetadataUnderItsOwnCode()
    {
        var (_, _, error) = Run("dump", "il", SamplePaths.Unsupported);

        Assert.Contains("CSW1002", error, StringComparison.Ordinal);
        Assert.Contains("Identity", error, StringComparison.Ordinal);
    }

    // CIL-INPUT-MISSING (requirement 24).
    [Fact]
    public void DumpOfAMissingFileReportsInputNotFound()
    {
        Assert.False(File.Exists(SamplePaths.Missing));

        var (code, output, error) = Run("dump", "il", SamplePaths.Missing);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0003", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // Requirement 14: the argument shapes the command does not accept are rejected by the
    // command line parser rather than throwing.
    [Theory]
    [InlineData("dump")]
    [InlineData("dump", "il")]
    [InlineData("dump", "wasm", "Sample.dll")]
    public void MalformedDumpInvocationsAreRejected(params string[] args)
    {
        var (code, output, error) = Run(args);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0002", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// Extracts the <c>IL_xxxx: opcode</c> lines that belong to one method, so an assertion
    /// observes that method's stream rather than the whole document.
    /// </summary>
    private static IReadOnlyList<string> InstructionsOfMethod(string dump, string methodName)
    {
        var lines = dump.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains($" {methodName}(", StringComparison.Ordinal));
        Assert.True(start >= 0, $"The dump has no method line for '{methodName}':\n{dump}");

        var instructions = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var match = InstructionLine.Match(lines[i]);
            if (match.Success)
            {
                instructions.Add($"IL_{match.Groups["offset"].Value}: {match.Groups["opcode"].Value}");
            }
            else if (instructions.Count > 0)
            {
                break;
            }
        }

        return instructions;
    }
}
