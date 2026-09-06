using CsWasm.Spike.Observations;
using Xunit;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Contracts DIFF-ORACLE-RET, DIFF-ORACLE-OUT, DIFF-ORACLE-ERR, DIFF-ORACLE-EXC,
/// DIFF-ORACLE-TIMEOUT, DIFF-ORACLE-TRAP, DIFF-NOEXITONLY and DIFF-NOFALSEMATCH: the
/// definition of "the two sides agree", which issue #18 states as a structured comparison
/// of return value / stdout / stderr / exception type / timeout / trap rather than an
/// exit code, and which must not call two failures an agreement.
/// </summary>
public sealed class ObservationComparerTests
{
    // DIFF-MATCH, and the baseline the field tests below are perturbations of: without it,
    // a comparer that answers "mismatch" to everything would satisfy every other test here.
    [Fact]
    public void TwoCompletedRunsThatAgreeOnEveryComparedFieldMatch()
    {
        var dotnet = TestObservations.Create(returnValue: "3", stdout: "out", stderr: "err");
        var wasm = TestObservations.Create(returnValue: "3", stdout: "out", stderr: "err");

        Assert.True(ObservationComparer.Matches(dotnet, wasm));
    }

    // DIFF-ORACLE-RET, and DIFF-NOEXITONLY: the record the two hosts exchange carries no exit
    // code at all, so a comparer that agreed because both processes ended the same way would
    // have to ignore the return value - which this rejects.
    [Fact]
    public void RunsThatDifferOnlyInTheReturnValueDoNotMatch()
    {
        var dotnet = TestObservations.Create(returnValue: "3");
        var wasm = TestObservations.Create(returnValue: "4");

        Assert.False(ObservationComparer.Matches(dotnet, wasm));
    }

    // DIFF-ORACLE-OUT.
    [Fact]
    public void RunsThatDifferOnlyInStdoutDoNotMatch()
    {
        var dotnet = TestObservations.Create(stdout: "written by the sample");
        var wasm = TestObservations.Create(stdout: string.Empty);

        Assert.False(ObservationComparer.Matches(dotnet, wasm));
    }

    // DIFF-ORACLE-ERR: stated separately from stdout - a comparer that folded the two streams
    // together would still pass the stdout test.
    [Fact]
    public void RunsThatDifferOnlyInStderrDoNotMatch()
    {
        var dotnet = TestObservations.Create(stderr: "written by the sample");
        var wasm = TestObservations.Create(stderr: string.Empty);

        Assert.False(ObservationComparer.Matches(dotnet, wasm));
    }

    // DIFF-ORACLE-EXC: issue #18 names the exception type, not merely whether an exception
    // happened. The two sides here are otherwise identical, so only a comparer that reads
    // the type name can tell them apart.
    [Fact]
    public void RunsThatDifferOnlyInTheExceptionTypeDoNotMatch()
    {
        var dotnet = TestObservations.Create(exceptionType: "System.OverflowException");
        var wasm = TestObservations.Create(exceptionType: "System.InvalidOperationException");

        Assert.False(ObservationComparer.Matches(dotnet, wasm));
    }

    // DIFF-NOFALSEMATCH, DIFF-ORACLE-TIMEOUT, DIFF-ORACLE-TRAP: "both sides failed" is the
    // false positive issue #18 calls out. Two identical failures are not an agreement, and a
    // failure never agrees with a completed run either.
    [Theory]
    [InlineData(Outcome.Trap)]
    [InlineData(Outcome.Exception)]
    [InlineData(Outcome.Timeout)]
    [InlineData(Outcome.HostError)]
    public void AFailedRunMatchesNothing(Outcome failure)
    {
        var failed = TestObservations.Create(failure, returnValue: null, detail: "the same detail");
        var identicallyFailed = TestObservations.Create(failure, returnValue: null, detail: "the same detail");
        var completed = TestObservations.Create(returnValue: null);

        Assert.False(
            ObservationComparer.Matches(failed, identicallyFailed),
            $"Two {failure} runs were reported as agreeing.");
        Assert.False(
            ObservationComparer.Matches(completed, failed),
            $"A completed run and a {failure} run were reported as agreeing.");
    }
}
