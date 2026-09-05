namespace CsWasm.Diagnostics;

/// <summary>
/// The band a <see cref="DiagnosticCode"/> falls into. The numeric ranges are fixed
/// and documented in docs/diagnostics.md; they are part of the user-visible contract
/// because CI and package authors filter on them.
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>CSW0xxx - driver, CLI and project loading.</summary>
    Driver = 0,

    /// <summary>CSW1xxx - frontend, and C# or IL semantics we cannot represent.</summary>
    Frontend = 1,

    /// <summary>CSW2xxx - NuGet and BCL compatibility, including classifier verdicts.</summary>
    Compatibility = 2,

    /// <summary>CSW3xxx - whole-program and closed-world AOT violations.</summary>
    WholeProgram = 3,

    /// <summary>CSW4xxx - MoonBit backend mapping.</summary>
    BackendMapping = 4,

    /// <summary>CSW5xxx - the external MoonBit toolchain.</summary>
    Toolchain = 5,

    /// <summary>CSW6xxx - host ABI and interop.</summary>
    HostAbi = 6,
}
