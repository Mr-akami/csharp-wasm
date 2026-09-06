namespace CsWasm.Diagnostics;

/// <summary>
/// A CSWnnnn diagnostic identifier. Codes are allocated once and never reused, so a
/// code in a build log always means the same thing across cswasm versions.
/// </summary>
public readonly record struct DiagnosticCode(int Number)
{
    /// <summary>The band this code belongs to, derived from its leading digit.</summary>
    public DiagnosticCategory Category => (DiagnosticCategory)(Number / 1000);

    public string Id => $"CSW{Number:D4}";

    public override string ToString() => Id;

    // CSW0xxx - driver.
    /// <summary>The requested command exists in the plan but is not implemented in this step.</summary>
    public static DiagnosticCode NotImplementedYet => new(1);

    /// <summary>The command line could not be parsed.</summary>
    public static DiagnosticCode BadCommandLine => new(2);

    /// <summary>An input file named on the command line does not exist.</summary>
    public static DiagnosticCode InputNotFound => new(3);

    // CSW1xxx - frontend.
    /// <summary>A CIL instruction is outside the instruction set the frontend supports.</summary>
    public static DiagnosticCode UnsupportedOpcode => new(1001);

    /// <summary>A metadata construct is outside the subset the frontend models.</summary>
    public static DiagnosticCode UnsupportedMetadata => new(1002);

    // CSW5xxx - toolchain.
    /// <summary>A required MoonBit executable was not found on PATH or under MOON_HOME.</summary>
    public static DiagnosticCode ToolchainNotFound => new(5001);

    /// <summary>A MoonBit executable was found but reports a version other than the pin.</summary>
    public static DiagnosticCode ToolchainVersionMismatch => new(5002);

    /// <summary>A toolchain executable could not be run, or failed while being probed.</summary>
    public static DiagnosticCode ToolchainProbeFailed => new(5003);
}
