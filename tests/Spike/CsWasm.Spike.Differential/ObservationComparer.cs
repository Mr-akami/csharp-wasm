using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// What it means for the two sides of a differential run to agree.
/// </summary>
/// <remarks>
/// Issue #18 states the oracle as a structured comparison of return value, stdout, stderr,
/// exception type, timeout and trap, and not as an exit code - so no exit code is read here,
/// and none is carried in the record either.
/// <para>
/// Agreement requires both sides to have completed. Two failures are not an agreement: that is
/// the false positive issue #18 names, and it is why the condition is not "the two outcomes
/// are equal". In this step the sample cannot fail on both sides for the same reason - the
/// wasm side inserts no implicit exception checks (CSW1005) - so a failure that matched would
/// be a run nobody actually compared.
/// </para>
/// <para>
/// Host metadata and the failure detail are outside the compared set: they are the engine
/// describing itself, not the program's behaviour. The split is stated here and nowhere else.
/// </para>
/// </remarks>
internal static class ObservationComparer
{
    public static bool Matches(Observation dotnet, Observation wasm)
    {
        ArgumentNullException.ThrowIfNull(dotnet);
        ArgumentNullException.ThrowIfNull(wasm);

        return dotnet.Outcome == Outcome.Completed
            && wasm.Outcome == Outcome.Completed
            && string.Equals(dotnet.ReturnValue, wasm.ReturnValue, StringComparison.Ordinal)
            && string.Equals(dotnet.Stdout, wasm.Stdout, StringComparison.Ordinal)
            && string.Equals(dotnet.Stderr, wasm.Stderr, StringComparison.Ordinal)
            && string.Equals(dotnet.ExceptionType, wasm.ExceptionType, StringComparison.Ordinal);
    }
}
