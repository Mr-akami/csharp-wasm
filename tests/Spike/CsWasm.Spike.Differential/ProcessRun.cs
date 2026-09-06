using System.Diagnostics;

namespace CsWasm.Spike.Differential;

/// <summary>What a child process did, as the runner observed it.</summary>
internal sealed record ProcessResult(bool TimedOut, int ExitCode, string Stdout, string Stderr);

/// <summary>
/// The one rule under which both sides of a differential run are started and observed.
/// </summary>
/// <remarks>
/// Issue #18 asks for culture, time zone and line endings to be normalised, and for a timeout
/// to be part of the oracle. All four are decided here, once, for whichever child is being
/// run: two sides normalised by two pieces of code would be two rules that can drift apart,
/// and a child that judged its own deadline would judge it differently on each side - Node
/// cannot interrupt a wasm call it is already inside.
/// </remarks>
internal static class ProcessRun
{
    /// <summary>
    /// The environment both children are given. Only these variables are overridden: the rest
    /// of the parent's environment, PATH above all, is what lets the child start at all.
    /// </summary>
    private static readonly (string Name, string Value)[] NormalisedEnvironment =
    [
        // Culture: whatever locale a developer or a CI runner happens to have must not reach
        // either side, or the two are no longer observed under one rule.
        ("LANG", "C"),
        ("LC_ALL", "C"),

        // The same statement to the .NET side, which reads its own variable rather than the
        // POSIX ones.
        ("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1"),

        // Time zone.
        ("TZ", "UTC"),
    ];

    public static ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in NormalisedEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");

        // Both streams are drained while the child runs: a child that fills a pipe would
        // otherwise block and be reported as a timeout it did not have.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        var timedOut = !process.WaitForExit((int)deadline.TotalMilliseconds);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new ProcessResult(
            timedOut,
            process.ExitCode,
            Normalise(stdout.GetAwaiter().GetResult()),
            Normalise(stderr.GetAwaiter().GetResult()));
    }

    /// <summary>
    /// Line feeds, and no trailing newline. A CRLF host and an LF host disagreeing over line
    /// endings alone is not a disagreement about the program.
    /// </summary>
    public static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
