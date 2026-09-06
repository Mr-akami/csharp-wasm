using Xunit;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Contracts DIFF-NORM-CULTURE, DIFF-NORM-TZ, DIFF-NORM-EOL and DIFF-ORACLE-TIMEOUT at the one
/// place issue #18 lets own them: both sides are child processes started the same way, so the
/// culture, the time zone, the line endings and the deadline are decided once, by the parent,
/// rather than twice with two chances to disagree.
/// </summary>
/// <remarks>
/// The child here is the pinned Node itself rather than either side of the differential run:
/// what is under test is how a child is started and how its output is captured, and a child
/// that only reports its own environment cannot pass by accident.
/// </remarks>
public sealed class ProcessRunTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static ProcessResult RunNode(string script, TimeSpan deadline) =>
        ProcessRun.Run(NodeHost.Executable, ["-e", script], deadline);

    // DIFF-NORM-CULTURE: whatever locale the developer or the CI runner happens to have is not
    // allowed to reach either side, or the two sides stop being observed under one rule.
    [Fact]
    public void AChildProcessSeesTheInvariantCulture()
    {
        var result = RunNode(
            "process.stdout.write([process.env.LANG, process.env.LC_ALL].join('|'))",
            Generous);

        Assert.False(result.TimedOut);
        Assert.Equal("C|C", result.Stdout);
    }

    // DIFF-NORM-TZ: observed through the child's resolved time zone rather than the variable
    // that sets it, because it is the resolution that the compared output would depend on.
    [Fact]
    public void AChildProcessResolvesTimesInUtc()
    {
        var result = RunNode(
            "process.stdout.write(Intl.DateTimeFormat().resolvedOptions().timeZone)",
            Generous);

        Assert.False(result.TimedOut);
        Assert.Equal("UTC", result.Stdout);
    }

    // DIFF-NORM-EOL: a CRLF host and an LF host must not disagree over line endings alone.
    [Fact]
    public void CapturedOutputIsNormalisedToLineFeeds()
    {
        var result = RunNode(@"process.stdout.write('first\r\nsecond\r\n')", Generous);

        Assert.False(result.TimedOut);
        Assert.Equal("first\nsecond", result.Stdout);
    }

    // DIFF-ORACLE-TIMEOUT: a child that never finishes is a timeout, decided by the parent.
    // Node cannot interrupt a wasm call it is already inside, so a child that reported its own
    // timeout would report it under a different rule on each side.
    [Fact]
    public void AChildThatOutlivesItsDeadlineIsReportedAsTimedOut()
    {
        var result = RunNode("while (true) { }", TimeSpan.FromSeconds(2));

        Assert.True(result.TimedOut, "A child that never returns was not reported as timed out.");
    }

    // The same deadline must not turn a child that does finish into a timeout.
    [Fact]
    public void AChildThatFinishesWithinItsDeadlineIsNotReportedAsTimedOut()
    {
        var result = RunNode("process.stdout.write('done')", TimeSpan.FromSeconds(2));

        Assert.False(result.TimedOut);
        Assert.Equal("done", result.Stdout);
    }
}
