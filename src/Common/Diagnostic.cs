namespace CsWasm.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One message addressed to the user. Diagnostics always carry a <see cref="DiagnosticCode"/>
/// so that they can be suppressed, filtered and documented; free-form messages are not
/// part of the contract, the code is.
/// </summary>
public sealed record Diagnostic(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Location = null,
    string? Help = null)
{
    public static Diagnostic Error(DiagnosticCode code, string message, string? help = null) =>
        new(code, DiagnosticSeverity.Error, message, Location: null, Help: help);

    public static Diagnostic Warning(DiagnosticCode code, string message, string? help = null) =>
        new(code, DiagnosticSeverity.Warning, message, Location: null, Help: help);

    public static Diagnostic Info(DiagnosticCode code, string message) =>
        new(code, DiagnosticSeverity.Info, message);

    /// <summary>Renders in the shape editors and CI log parsers expect.</summary>
    public string Format()
    {
        var severity = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        var prefix = Location is null ? string.Empty : Location + ": ";
        var text = $"{prefix}{severity} {Code.Id}: {Message}";
        return Help is null ? text : text + Environment.NewLine + "  help: " + Help;
    }
}
