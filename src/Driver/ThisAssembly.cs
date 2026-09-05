using System.Reflection;

namespace CsWasm.Driver;

internal static class ThisAssembly
{
    /// <summary>The informational version stamped into the driver assembly at build time.</summary>
    public static string Version { get; } =
        typeof(ThisAssembly).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ThisAssembly).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
