using System.Globalization;

namespace CsWasm.Spike.Differential;

/// <summary>
/// One case of the differential run: which method of the sample to call, with which arguments,
/// and how long either side may take.
/// </summary>
/// <remarks>
/// The arguments are <c>int</c>, which is how issue #18's "no floating point in this step" is
/// stated in code rather than in a comment. The deadline belongs to the case rather than to
/// either host, because both sides are held to the same one.
/// </remarks>
internal sealed record SpikeCase(string MethodName, IReadOnlyList<int> Arguments, TimeSpan Deadline)
{
    /// <summary>
    /// The arguments in the one spelling both hosts read: a JSON array of integers. Written
    /// here rather than by each side, so the two sides cannot be handed different arguments.
    /// </summary>
    public string ArgumentsAsJson =>
        "[" + string.Join(',', Arguments.Select(argument => argument.ToString(CultureInfo.InvariantCulture))) + "]";
}

internal static class SpikeCases
{
    /// <summary>
    /// The case issue #18 names by hand: <c>Sum</c> agreeing on both sides.
    /// </summary>
    /// <remarks>
    /// The deadline is generous because it bounds a child process that is expected to finish
    /// in milliseconds - it is there to turn a hang into a reported timeout, not to measure
    /// anything.
    /// </remarks>
    public static SpikeCase Sum { get; } = new("Sum", [17, 25], TimeSpan.FromSeconds(60));
}
