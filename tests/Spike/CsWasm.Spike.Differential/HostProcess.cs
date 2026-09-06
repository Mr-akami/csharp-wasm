using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Runs one side's host process and turns that run into exactly one observation.
/// </summary>
/// <remarks>
/// Both sides pass through here so that the parent's share of the classification - the
/// deadline, and a child that did not speak the record - is decided once and in the same words
/// for each. What happened inside a case is classified by the host that ran it; this only
/// classifies what happened to the process.
/// </remarks>
internal static class HostProcess
{
    public static Observation Observe(
        string runtime,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan deadline)
    {
        var result = ProcessRun.Run(executable, arguments, deadline);

        if (result.TimedOut)
        {
            return Failed(
                runtime,
                Outcome.Timeout,
                $"The {runtime} host did not finish within {deadline.TotalSeconds:0.###}s and was killed."
                    + Trailing(result));
        }

        var line = LastNonEmptyLine(result.Stdout);
        if (line is null)
        {
            return Failed(
                runtime,
                Outcome.HostError,
                $"The {runtime} host wrote no observation." + Trailing(result));
        }

        var observation = ObservationJson.Read(line);
        if (observation is null)
        {
            return Failed(
                runtime,
                Outcome.HostError,
                $"The {runtime} host's last line of output is not an observation: {line}" + Trailing(result));
        }

        if (observation.Schema != Observation.CurrentSchema)
        {
            return Failed(
                runtime,
                Outcome.HostError,
                $"The {runtime} host wrote schema {observation.Schema}; this runner speaks schema "
                    + $"{Observation.CurrentSchema}.");
        }

        // Line endings are normalised here rather than by each host: what the sample printed
        // travels inside the record, so it never passed through the stream normalisation, and
        // both sides have to be held to the one rule ProcessRun states.
        return observation with
        {
            Stdout = ProcessRun.Normalise(observation.Stdout),
            Stderr = ProcessRun.Normalise(observation.Stderr),
        };
    }

    /// <summary>
    /// The observation is the last line the host wrote; anything before it is the host's own
    /// noise, kept for the diff rather than compared.
    /// </summary>
    private static string? LastNonEmptyLine(string stdout)
    {
        var lines = stdout.Split('\n');

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (lines[index].Trim().Length > 0)
            {
                return lines[index];
            }
        }

        return null;
    }

    private static string Trailing(ProcessResult result)
    {
        var text = $" Exit code {result.ExitCode}.";

        if (result.Stdout.Length > 0)
        {
            text += "\nstdout: " + result.Stdout;
        }

        if (result.Stderr.Length > 0)
        {
            text += "\nstderr: " + result.Stderr;
        }

        return text;
    }

    /// <summary>
    /// A failure the parent decided. The compared fields carry nothing invented: only a host
    /// that ran the case reports a value, and a failed run never agrees with anything anyway.
    /// </summary>
    private static Observation Failed(string runtime, Outcome outcome, string detail) =>
        new(
            Observation.CurrentSchema,
            outcome,
            ReturnValue: null,
            Stdout: string.Empty,
            Stderr: string.Empty,
            ExceptionType: null,
            detail,
            new HostInfo(runtime, string.Empty, new HostCapabilities(JsStringBuiltins: null)));
}
