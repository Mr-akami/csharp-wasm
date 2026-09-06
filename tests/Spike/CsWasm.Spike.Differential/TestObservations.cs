using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Builds the observation record the two hosts share, filling everything a given test does
/// not speak about. Only the fields a test names are meant to influence its result.
/// </summary>
internal static class TestObservations
{
    private const int CurrentSchema = 1;

    /// <summary>
    /// Host metadata is reported by each side but is deliberately outside the compared set
    /// (issue #18 names return value / stdout / stderr / exception type / timeout / trap),
    /// so the factory gives both sides the same placeholder rather than a realistic one.
    /// </summary>
    private static HostInfo Host { get; } = new("fixture", "0.0.0", new HostCapabilities(true));

    public static Observation Create(
        Outcome outcome = Outcome.Completed,
        string? returnValue = "0",
        string stdout = "",
        string stderr = "",
        string? exceptionType = null,
        string? detail = null) =>
        new(CurrentSchema, outcome, returnValue, stdout, stderr, exceptionType, detail, Host);
}
