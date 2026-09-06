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

    public const string BackendUnsupportedAssemblyName = "CsWasm.Samples.BackendUnsupported";

    public const string NamingStabilityAssemblyName = "CsWasm.Samples.NamingStability";

    /// <summary>
    /// A body the frontend normalises without complaint and the MoonBit backend of this step
    /// cannot lower: allocation is not in the mapping issue #16 lists.
    /// </summary>
    public static string BackendUnsupported { get; } =
        Path.Combine(AppContext.BaseDirectory, BackendUnsupportedAssemblyName + ".dll");

    /// <summary>
    /// Declares the same <c>CsWasm.Samples.Poc.Point</c> as <see cref="Poc"/> inside a
    /// differently named assembly, in a different metadata position, alongside different
    /// members: everything an identifier must not depend on.
    /// </summary>
    public static string NamingStability { get; } =
        Path.Combine(AppContext.BaseDirectory, NamingStabilityAssemblyName + ".dll");

    /// <summary>A path that is guaranteed not to exist, for the input-missing diagnostic.</summary>
    public static string Missing { get; } = Path.Combine(AppContext.BaseDirectory, "CsWasm.Samples.DoesNotExist.dll");
}
