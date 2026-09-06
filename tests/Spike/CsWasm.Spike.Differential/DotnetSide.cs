using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Side (a) of issue #18: the same C#, run by .NET.
/// </summary>
internal static class DotnetSide
{
    private const string HostAssemblyName = "CsWasm.Spike.DotnetHost";

    /// <summary>
    /// The .NET host's build output, copied next to the test binaries by the project file.
    /// It keeps its own directory because it brings its own runtimeconfig and dependencies.
    /// </summary>
    private static string HostAssembly { get; } =
        Path.Combine(AppContext.BaseDirectory, "dotnet-host", HostAssemblyName + ".dll");

    private static string Dotnet { get; } = ToolOnPath.Find("dotnet");

    public static Observation Run(SpikeCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if (!File.Exists(HostAssembly))
        {
            throw new InvalidOperationException(
                $"{HostAssembly} is missing. The differential project copies {HostAssemblyName}'s "
                    + "output next to the tests; the build did not.");
        }

        return HostProcess.Observe(
            "dotnet",
            Dotnet,
            ["exec", HostAssembly, "--method", testCase.MethodName, "--args", testCase.ArgumentsAsJson],
            testCase.Deadline);
    }
}
