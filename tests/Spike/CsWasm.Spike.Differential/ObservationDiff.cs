using System.Globalization;
using System.Text;
using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Puts the two sides next to each other so a reader can see which one is wrong.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ObservationComparer"/>: what counts as agreement and
/// how a disagreement is shown change for different reasons, and a display that decided
/// agreement would be a second answer to a question that already has one.
/// <para>
/// Every field is shown, not only the differing ones. A reader who is told just what differs
/// cannot see that the other side produced nothing either.
/// </para>
/// </remarks>
internal static class ObservationDiff
{
    private const string DotnetHeading = ".NET";
    private const string WasmHeading = "wasm on Node";

    public static string Render(Observation dotnet, Observation wasm)
    {
        ArgumentNullException.ThrowIfNull(dotnet);
        ArgumentNullException.ThrowIfNull(wasm);

        var rows = new List<(string Field, string Dotnet, string Wasm)>
        {
            ("outcome", dotnet.Outcome.ToString(), wasm.Outcome.ToString()),
            ("returnValue", Show(dotnet.ReturnValue), Show(wasm.ReturnValue)),
            ("stdout", Show(dotnet.Stdout), Show(wasm.Stdout)),
            ("stderr", Show(dotnet.Stderr), Show(wasm.Stderr)),
            ("exceptionType", Show(dotnet.ExceptionType), Show(wasm.ExceptionType)),
            ("detail", Show(dotnet.Detail), Show(wasm.Detail)),
            ("host", Show(dotnet.Host), Show(wasm.Host)),
        };

        var field = Math.Max(rows.Max(row => row.Field.Length), "field".Length);
        var left = Math.Max(rows.Max(row => row.Dotnet.Length), DotnetHeading.Length);

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"The two sides of the differential run disagree.{Environment.NewLine}");
        Append(text, ("field", DotnetHeading, WasmHeading), field, left);

        foreach (var row in rows)
        {
            Append(text, row, field, left);
        }

        return text.ToString();
    }

    private static void Append(
        StringBuilder text,
        (string Field, string Dotnet, string Wasm) row,
        int field,
        int left)
    {
        text.Append("  ")
            .Append(row.Field.PadRight(field))
            .Append("  ")
            .Append(row.Dotnet.PadRight(left))
            .Append("  ")
            .Append(row.Wasm)
            .Append(Environment.NewLine);
    }

    /// <summary>
    /// Quoted, so that an empty string and a missing value are told apart on sight - the two
    /// that a reader would otherwise most easily confuse.
    /// </summary>
    private static string Show(string? value) =>
        value is null ? "<none>" : "\"" + value.Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static string Show(HostInfo host)
    {
        var capability = host.Capabilities.JsStringBuiltins switch
        {
            null => "not probed",
            var probed => probed.Value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
        };

        return $"{host.Runtime} {host.Version} (js-string-builtins: {capability})";
    }
}
