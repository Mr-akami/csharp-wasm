using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts SSA-DUMP, SSA-DETERMINISM, SSA-SNAPSHOT, SSA-CSW1003 and KEEP-ALLORNOTHING at
/// the entry point the issue names: <c>cswasm dump ssa &lt;dll&gt;</c>.
/// </summary>
public sealed class DumpSsaCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // SSA-DUMP: a command that --help does not mention is not wired up, whatever exists
    // behind it.
    [Fact]
    public void UsageDisclosesTheDumpSsaCommand()
    {
        var (code, output, _) = Run("--help");

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Contains("dump ssa", output, StringComparison.Ordinal);
    }

    // SSA-DUMP: the proof-of-concept sample normalises end to end, with nothing on stderr.
    [Fact]
    public void DumpOfTheProofOfConceptSampleSucceeds()
    {
        var (code, output, error) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);
        Assert.Equal(string.Empty, error);
        Assert.NotEqual(string.Empty, output);
    }

    // SSA-SNAPSHOT. The snapshot is recorded once from an inspected run; until it exists this
    // fails with the output attached (tests/CsWasm.Tests/Snapshot.cs).
    [Fact]
    public void DumpOfTheProofOfConceptSampleMatchesTheSnapshot()
    {
        var (code, output, error) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);
        Snapshot.Matches("dump-ssa-poc.txt", output);
    }

    // SSA-SUM through the command: the completion condition is that Sum reaches the output,
    // and that the merge the loop closes on is named there. Asserted on the text so the
    // command's own path is observed, not only the builder's.
    [Fact]
    public void DumpNamesSumAndItsLoopHeaderBlock()
    {
        var (_, output, _) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.Contains("Sum", output, StringComparison.Ordinal);
        Assert.Contains("IL_001e", output, StringComparison.Ordinal);
    }

    // SSA-BLOCKARGS: merges are block parameters, so the word phi has no place in the output.
    [Fact]
    public void DumpNeverMentionsPhi()
    {
        var (_, output, _) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.DoesNotContain("phi", output, StringComparison.OrdinalIgnoreCase);
    }

    // SSA-DETERMINISM: the line separator belongs to the format, not to the platform.
    [Fact]
    public void DumpUsesLineFeedsOnly()
    {
        var (_, output, _) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.DoesNotContain("\r", output, StringComparison.Ordinal);
    }

    // SSA-DETERMINISM: nothing that varies between machines may reach the output.
    [Fact]
    public void DumpDoesNotLeakTheInputPath()
    {
        var (_, output, _) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.DoesNotContain(AppContext.BaseDirectory, output, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, output, StringComparison.Ordinal);
    }

    // SSA-DETERMINISM: two runs over the same file agree, so value numbering and block order
    // cannot depend on hash iteration order.
    [Fact]
    public void DumpOfTheSameAssemblyIsStableAcrossRuns()
    {
        var (_, first, _) = Run("dump", "ssa", SamplePaths.Poc);
        var (_, second, _) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.Equal(first, second);
    }

    // SSA-CSW1003 and KEEP-ALLORNOTHING through the driver: the refusal reaches stderr under
    // its own code, the exit code is non-zero, and no part of the assembly is printed
    // (docs/diagnostics.md rule 1).
    [Fact]
    public void DumpFailsOnAnAddressTakenLocalWithoutEmittingOutput()
    {
        var (code, output, error) = Run("dump", "ssa", SamplePaths.Unsupported);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW1003", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // KEEP-ALLORNOTHING: the input check the il dump already applies is the same one here.
    [Fact]
    public void DumpOfAMissingFileReportsInputNotFound()
    {
        Assert.False(File.Exists(SamplePaths.Missing));

        var (code, output, error) = Run("dump", "ssa", SamplePaths.Missing);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0003", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // SSA-DUMP: argument shapes the command does not accept are rejected by the parser rather
    // than throwing, exactly as for dump il.
    [Theory]
    [InlineData("dump", "ssa")]
    [InlineData("dump", "ssa", "Sample.dll", "extra")]
    public void MalformedDumpSsaInvocationsAreRejected(params string[] args)
    {
        var (code, output, error) = Run(args);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0002", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }
}
