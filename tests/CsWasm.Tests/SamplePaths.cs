namespace CsWasm.Tests;

/// <summary>
/// The sample assemblies the CIL frontend reads. They are built by the solution and copied
/// next to the test binaries as content, never referenced, so reading one is the only way a
/// test can observe it.
/// </summary>
internal static class SamplePaths
{
    public const string PocAssemblyName = "CsWasm.Samples.Poc";
    public const string UnsupportedAssemblyName = "CsWasm.Samples.Unsupported";

    /// <summary>docs/architecture.md section 31: <c>Point</c> and <c>Sum</c>.</summary>
    public static string Poc { get; } = Path.Combine(AppContext.BaseDirectory, PocAssemblyName + ".dll");

    /// <summary>Members outside the supported opcode set and outside supported metadata.</summary>
    public static string Unsupported { get; } = Path.Combine(AppContext.BaseDirectory, UnsupportedAssemblyName + ".dll");

    /// <summary>A path that is guaranteed not to exist, for the input-missing diagnostic.</summary>
    public static string Missing { get; } = Path.Combine(AppContext.BaseDirectory, "CsWasm.Samples.DoesNotExist.dll");
}
