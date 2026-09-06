namespace CsWasm.Spike.Differential;

/// <summary>
/// Where Node is, and where the harness it runs is. Issue #18 pins the host to Node 24; the
/// dev shell and CI put that Node on PATH, and <c>DifferentialTests</c> checks afterwards that
/// the engine which actually ran reported the pinned version.
/// </summary>
internal static class NodeHost
{
    public static string Executable { get; } = ToolOnPath.Find("node");

    /// <summary>The harness, copied next to the test binaries by the project file.</summary>
    public static string Harness { get; } = Path.Combine(AppContext.BaseDirectory, "host", "run-case.mjs");
}
