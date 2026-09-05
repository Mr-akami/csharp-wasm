using System.Diagnostics;
using System.Runtime.InteropServices;
using CsWasm.Diagnostics;

namespace CsWasm.Backend.MoonBit;

/// <summary>
/// Locates the external MoonBit toolchain and checks it against the pin.
///
/// Everything that knows about MoonBit's CLI shape lives in this directory
/// (docs/architecture.md 9.5): when MoonBit changes its command line or its version
/// banner, only these files should need to change.
/// </summary>
public static class MoonBitToolchain
{
    /// <summary>Executables the compiler actually drives, and how to ask each for its version.</summary>
    private static readonly (string Tool, string[] Args, Func<ToolchainPin, string> Expected)[] Probes =
    [
        ("moonc", ["-v"], pin => pin.MoonBit.Version),
        ("moon", ["version"], pin => pin.MoonBit.MoonVersion),
    ];

    /// <summary>
    /// Resolves a MoonBit executable. <c>MOON_HOME</c> wins over <c>PATH</c> so that a dev
    /// shell pointing at the pinned copy is never shadowed by a globally installed MoonBit.
    /// </summary>
    public static string? FindExecutable(string tool)
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? tool + ".exe" : tool;

        var moonHome = Environment.GetEnvironmentVariable("MOON_HOME");
        if (!string.IsNullOrEmpty(moonHome))
        {
            var candidate = Path.Combine(moonHome, "bin", exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks every driven executable against <see cref="ToolchainPin.Current"/>.
    /// Returns one diagnostic per problem; an empty list means the environment matches the pin.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Verify(ToolchainPin pin, out IReadOnlyDictionary<string, string> found)
    {
        var diagnostics = new List<Diagnostic>();
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (tool, args, expected) in Probes)
        {
            var exe = FindExecutable(tool);
            if (exe is null)
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCode.ToolchainNotFound,
                    $"MoonBit executable '{tool}' was not found on PATH or under MOON_HOME.",
                    "Enter the pinned dev shell with `nix develop`, which materialises the pinned toolchain."));
                continue;
            }

            string banner;
            try
            {
                banner = RunAndCapture(exe, args);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCode.ToolchainProbeFailed,
                    $"Failed to run '{exe} {string.Join(' ', args)}': {ex.Message}"));
                continue;
            }

            var actual = ParseVersion(banner);
            versions[tool] = actual;

            var want = expected(pin);
            if (!string.Equals(actual, want, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Error(
                    DiagnosticCode.ToolchainVersionMismatch,
                    $"'{tool}' reports {actual}, but tools/toolchain.json pins {want}.",
                    "Upgrading MoonBit is a reviewed baseline change: update tools/toolchain.json and re-run the benchmarks."));
            }
        }

        found = versions;
        return diagnostics;
    }

    /// <summary>
    /// Extracts the version out of a MoonBit version banner. Both shapes seen so far
    /// carry the version as the first token that is not the tool name, optionally
    /// prefixed by <c>v</c>: "v0.10.11+6ff76a5f9 (2026-08-28)" and
    /// "moon 0.1.20260827 (d0aaa07 2026-08-27)".
    /// </summary>
    public static string ParseVersion(string banner)
    {
        var firstLine = banner.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        foreach (var token in firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.TrimStart('v');
            if (candidate.Length > 0 && char.IsDigit(candidate[0]))
            {
                return candidate;
            }
        }

        return firstLine;
    }

    private static string RunAndCapture(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start '{exe}'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"exited with code {process.ExitCode}: {(stderr.Length > 0 ? stderr.Trim() : stdout.Trim())}");
        }

        return stdout.Length > 0 ? stdout : stderr;
    }
}
