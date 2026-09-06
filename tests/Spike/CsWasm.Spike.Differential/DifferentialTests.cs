using System.Globalization;
using CsWasm.Spike.Observations;
using Xunit;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Contracts DIFF-RUNNER, DIFF-SAMPLE, DIFF-HOST, DIFF-PROBE, DIFF-NODEPIN, DIFF-MATCH and
/// DIFF-NEGATIVE: the same C# is run twice - once by .NET, once by compiling it with cswasm
/// and calling the module from Node - and the two runs are compared.
/// </summary>
/// <remarks>
/// Every test here drives the real path on both sides. A fixed expected observation on one
/// side would leave the completion condition of issue #18 - that CI checks the two agree -
/// unobserved, because a compiler change could then only ever break one of them.
/// </remarks>
public sealed class DifferentialTests
{
    // DIFF-RUNNER, DIFF-SAMPLE, DIFF-HOST, DIFF-MATCH: the case issue #18 names by hand.
    [Fact]
    public void SumAgreesBetweenDotnetAndTheModuleRunOnNode()
    {
        var testCase = SpikeCases.Sum;

        var dotnet = DotnetSide.Run(testCase);
        var wasm = WasmSide.Run(testCase);

        Assert.True(
            ObservationComparer.Matches(dotnet, wasm),
            ObservationDiff.Render(dotnet, wasm));
    }

    // Stated separately from the agreement: two sides that both produced nothing would agree
    // on every compared field, so the agreement alone does not show either side ran the sample.
    // DIFF-NOFLOAT is observed here too - the value both sides carry is a decimal integer.
    [Fact]
    public void BothSidesReturnTheIntegerSumOfTheCaseArguments()
    {
        var testCase = SpikeCases.Sum;
        var expected = testCase.Arguments.Sum();

        var dotnet = DotnetSide.Run(testCase);
        var wasm = WasmSide.Run(testCase);

        Assert.Equal(Outcome.Completed, dotnet.Outcome);
        Assert.Equal(Outcome.Completed, wasm.Outcome);
        Assert.Equal(expected, ParseInteger(dotnet.ReturnValue, "the .NET side"));
        Assert.Equal(expected, ParseInteger(wasm.ReturnValue, "the Node side"));
    }

    // DIFF-PROBE: docs/host-profiles.md marks js-string-builtins as `probe` for the node
    // profile and rules out version checks and permanent flags, so the harness has to detect
    // it and say what it found. The value is deliberately not asserted - freezing true or
    // false here would put the answer back into the source it is supposed to be probed from.
    [Fact]
    public void TheNodeHarnessReportsWhetherJsStringBuiltinsArePresent()
    {
        var wasm = WasmSide.Run(SpikeCases.Sum);

        Assert.NotNull(wasm.Host.Capabilities.JsStringBuiltins);
    }

    // DIFF-NODEPIN: issue #18 pins the host to Node 24. A run on some other Node would be
    // comparing against an engine nobody reviewed.
    [Fact]
    public void TheNodeHarnessRunsOnThePinnedNodeVersion()
    {
        var wasm = WasmSide.Run(SpikeCases.Sum);

        Assert.Equal(ToolchainPin.Current.Node.Version, wasm.Host.Version);
    }

    // DIFF-NEGATIVE: "check that it fails when the expected value is deliberately broken".
    // Perturbing a real observation rather than a constructed one keeps the check on the same
    // values the passing test compared.
    [Fact]
    public void ADeliberatelyAlteredReturnValueIsReportedAsADisagreement()
    {
        var testCase = SpikeCases.Sum;
        var dotnet = DotnetSide.Run(testCase);
        var wasm = WasmSide.Run(testCase);
        var altered = dotnet with { ReturnValue = dotnet.ReturnValue + "0" };

        Assert.False(
            ObservationComparer.Matches(altered, wasm),
            ObservationDiff.Render(altered, wasm));
    }

    // DIFF-NEGATIVE and DIFF-NOFALSEMATCH on the real path: a case neither side can run must
    // not be reported as agreement. This is the failure a comparer that only asks whether the
    // two sides ended the same way would let through.
    [Fact]
    public void ACaseNeitherSideCanRunFailsOnBothSidesAndIsStillADisagreement()
    {
        var missing = SpikeCases.Sum with { MethodName = "AMethodTheSampleDoesNotDeclare" };

        var dotnet = DotnetSide.Run(missing);
        var wasm = WasmSide.Run(missing);

        Assert.NotEqual(Outcome.Completed, dotnet.Outcome);
        Assert.NotEqual(Outcome.Completed, wasm.Outcome);
        Assert.False(
            ObservationComparer.Matches(dotnet, wasm),
            ObservationDiff.Render(dotnet, wasm));
    }

    private static int ParseInteger(string? returnValue, string side)
    {
        Assert.True(returnValue is not null, side + " reported no return value.");

        Assert.True(
            int.TryParse(returnValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value),
            $"{side} reported '{returnValue}', which is not a decimal integer.");

        return value;
    }
}
