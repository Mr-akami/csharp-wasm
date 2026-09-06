using CsWasm.Spike.Observations;
using Xunit;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Contract DIFF-DIFFVIEW: issue #18 asks for "a diff display that puts the two sides' values
/// next to each other" when they disagree. A report that only says "mismatch", or that prints
/// one side, does not tell the reader which side is wrong.
/// </summary>
public sealed class ObservationDiffTests
{
    [Fact]
    public void RenderingADisagreementShowsBothSidesValueForEveryFieldThatDiffers()
    {
        var dotnet = TestObservations.Create(
            returnValue: "dotnet-return",
            stdout: "dotnet-stdout",
            stderr: "dotnet-stderr",
            exceptionType: "Dotnet.ExceptionType");
        var wasm = TestObservations.Create(
            returnValue: "wasm-return",
            stdout: "wasm-stdout",
            stderr: "wasm-stderr",
            exceptionType: "Wasm.ExceptionType");

        var rendered = ObservationDiff.Render(dotnet, wasm);

        Assert.Contains("dotnet-return", rendered, StringComparison.Ordinal);
        Assert.Contains("wasm-return", rendered, StringComparison.Ordinal);
        Assert.Contains("dotnet-stdout", rendered, StringComparison.Ordinal);
        Assert.Contains("wasm-stdout", rendered, StringComparison.Ordinal);
        Assert.Contains("dotnet-stderr", rendered, StringComparison.Ordinal);
        Assert.Contains("wasm-stderr", rendered, StringComparison.Ordinal);
        Assert.Contains("Dotnet.ExceptionType", rendered, StringComparison.Ordinal);
        Assert.Contains("Wasm.ExceptionType", rendered, StringComparison.Ordinal);
    }

    // The outcome is what separates "both sides failed" from an agreement, so a reader who is
    // shown only the values cannot tell why a run that produced no value disagreed.
    [Fact]
    public void RenderingADisagreementShowsBothSidesOutcome()
    {
        var dotnet = TestObservations.Create(returnValue: "3");
        var wasm = TestObservations.Create(Outcome.Trap, returnValue: null, detail: "unreachable");

        var rendered = ObservationDiff.Render(dotnet, wasm);

        Assert.Contains(nameof(Outcome.Completed), rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(Outcome.Trap), rendered, StringComparison.OrdinalIgnoreCase);
    }
}
