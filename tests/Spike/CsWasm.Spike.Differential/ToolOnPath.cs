using System.Runtime.InteropServices;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Finds a pinned tool on PATH.
/// </summary>
/// <remarks>
/// Resolution is deliberately PATH-only and unconditional, the same judgement
/// <c>tests/CsWasm.Tests/WasmTools.cs</c> makes: the dev shell and CI both put the pinned
/// toolchain in front of anything else, so a missing tool is a fault in the environment, not
/// a reason to pass a differential test that never ran either side.
/// </remarks>
internal static class ToolOnPath
{
    public static string Find(string name)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"'{name}' was not found on PATH. Enter the pinned dev shell with `nix develop`.");
    }
}
