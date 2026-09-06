using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsWasm.Tests;

/// <summary>
/// Runs the pinned <c>wasm-tools</c>. The completion condition of issue #17 is stated in terms
/// of what <c>wasm-tools validate</c> and <c>wasm-tools print</c> say about the module cswasm
/// produced, so the tests that observe it have to run the tool rather than describe it.
/// </summary>
/// <remarks>
/// Resolution is deliberately PATH-only and unconditional: the dev shell and CI both put the
/// pinned toolchain in front of anything else (README.md, .github/workflows/ci.yml), and
/// <c>MoonBitToolchainTests.PinnedToolchainIsOnPath</c> already fails loudly rather than
/// skipping when it is absent. A missing wasm-tools is an environment fault, not a reason to
/// pass a test that never checked anything.
/// </remarks>
internal static class WasmTools
{
    private static string Executable { get; } = Find();

    public static (int ExitCode, string Out, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Executable)
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
            ?? throw new InvalidOperationException($"Could not start '{Executable}'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }

    private static string Find()
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "wasm-tools.exe" : "wasm-tools";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "wasm-tools was not found on PATH. Enter the pinned dev shell with `nix develop`.");
    }
}
