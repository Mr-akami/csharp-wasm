using System.Text.Json.Serialization;

namespace CsWasm.Spike.Observations;

/// <summary>
/// What one side of a differential run did, classified once by the host that ran it.
/// </summary>
/// <remarks>
/// Issue #18 rules out the exit code as the oracle, so nothing here records one. A host says
/// what happened in exactly one word, and the comparison is over the values below rather
/// over how the process happened to end.
/// </remarks>
public enum Outcome
{
    /// <summary>The exported function returned.</summary>
    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>The module trapped (on Node, a <c>WebAssembly.RuntimeError</c>).</summary>
    [JsonStringEnumMemberName("trap")]
    Trap,

    /// <summary>The sample threw a managed exception; <see cref="Observation.ExceptionType"/> names it.</summary>
    [JsonStringEnumMemberName("exception")]
    Exception,

    /// <summary>The run outlived the deadline. Only the parent that owns the deadline says this.</summary>
    [JsonStringEnumMemberName("timeout")]
    Timeout,

    /// <summary>
    /// The host could not run the case at all - a missing export, an import it will not
    /// provide, an unreadable module. Distinct from <see cref="Trap"/> and
    /// <see cref="Exception"/>: those are the sample failing, this is the harness failing.
    /// </summary>
    [JsonStringEnumMemberName("hostError")]
    HostError,
}

/// <summary>
/// The capabilities the host detected at startup. docs/host-profiles.md marks
/// <c>js-string-builtins</c> as <c>probe</c> for the node profile and rules out version
/// checks and permanent flags, so this is what a probe found, not what a table said.
/// </summary>
/// <param name="JsStringBuiltins">
/// <see langword="null"/> when the capability does not apply to the host that reported it.
/// A host on which it does apply always answers <see langword="true"/> or
/// <see langword="false"/>, so "not probed" and "probed and absent" stay distinguishable.
/// </param>
public sealed record HostCapabilities(bool? JsStringBuiltins);

/// <summary>Which engine produced an observation. Reported, never compared.</summary>
public sealed record HostInfo(string Runtime, string Version, HostCapabilities Capabilities);

/// <summary>
/// One run of one case on one host. Both hosts write this record and the runner reads it, so
/// the two sides are described in the same words rather than in two shapes that have to be
/// reconciled at the comparison.
/// </summary>
/// <param name="Schema">
/// The version of this contract. A host that writes another number is not speaking it.
/// </param>
/// <param name="ReturnValue">
/// The value the exported function returned, as a decimal string. Kept textual on purpose:
/// a JS number and a .NET <c>int</c> would otherwise be compared through whichever of the two
/// the record happened to be typed as.
/// </param>
/// <param name="ExceptionType">The assembly-qualified-free full name of the exception, or null.</param>
/// <param name="Detail">
/// Why a failed run failed. Outside the compared set - it is engine wording - and shown only
/// when the two sides disagree.
/// </param>
public sealed record Observation(
    int Schema,
    Outcome Outcome,
    string? ReturnValue,
    string Stdout,
    string Stderr,
    string? ExceptionType,
    string? Detail,
    HostInfo Host)
{
    /// <summary>The only schema version this repository speaks.</summary>
    public const int CurrentSchema = 1;
}
