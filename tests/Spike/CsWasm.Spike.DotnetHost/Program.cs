using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CsWasm.Spike.Observations;
using CsWasm.Spike.Samples;

namespace CsWasm.Spike.DotnetHost;

/// <summary>
/// Side (a) of the differential run: the sample executed by .NET, reported in the same record
/// the Node harness writes.
/// </summary>
/// <remarks>
/// This is a separate process rather than a call inside the test for the reason issue #18
/// states its oracle: stdout, stderr and the deadline have to be observed under one rule on
/// both sides, and a side that ran in the test host would be observed under another.
/// <para>
/// The exit code is deliberately always success. The observation is the result; an exit code
/// as the oracle is what issue #18 rules out.
/// </para>
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        var observation = Observe(args);
        Console.Out.Write(ObservationJson.Write(observation));
        Console.Out.Write('\n');
        return 0;
    }

    private static Observation Observe(string[] args)
    {
        if (args is not ["--method", var methodName, "--args", var argumentsJson])
        {
            return Failed(
                "Usage: CsWasm.Spike.DotnetHost --method <name> --args <json array of integers>. Got: "
                    + string.Join(' ', args));
        }

        int[] arguments;
        try
        {
            arguments = ParseArguments(argumentsJson);
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            return Failed($"'{argumentsJson}' is not a JSON array of integers: {error.Message}");
        }

        var method = typeof(Sample).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [.. arguments.Select(_ => typeof(int))],
            modifiers: null);

        if (method is null)
        {
            return Failed(
                $"{typeof(Sample).FullName} declares no public static '{methodName}' taking {arguments.Length} integers.");
        }

        return Invoke(method, arguments);
    }

    /// <summary>
    /// Runs the method with the sample's own output captured, so that what the sample printed
    /// ends up in the observation rather than mixed into the line the runner reads.
    /// </summary>
    private static Observation Invoke(MethodInfo method, int[] arguments)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var sampleOut = new StringWriter(CultureInfo.InvariantCulture);
        var sampleError = new StringWriter(CultureInfo.InvariantCulture);

        try
        {
            Console.SetOut(sampleOut);
            Console.SetError(sampleError);

            object? returned;
            try
            {
                returned = method.Invoke(null, [.. arguments.Select(argument => (object)argument)]);
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
            {
                var thrown = invocation.InnerException;
                return new Observation(
                    Observation.CurrentSchema,
                    Outcome.Exception,
                    ReturnValue: null,
                    Text(sampleOut),
                    Text(sampleError),
                    thrown.GetType().FullName,
                    thrown.Message,
                    Host);
            }

            return new Observation(
                Observation.CurrentSchema,
                Outcome.Completed,
                Format(returned),
                Text(sampleOut),
                Text(sampleError),
                ExceptionType: null,
                Detail: null,
                Host);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static int[] ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The arguments are not a JSON array.");
        }

        return [.. document.RootElement.EnumerateArray().Select(element => element.GetInt32())];
    }

    /// <summary>
    /// The return value as a decimal string, in the invariant culture: the two sides compare
    /// text, so neither side's number formatting reaches the comparison.
    /// </summary>
    private static string? Format(object? returned) => returned switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString(),
    };

    /// <summary>
    /// What the sample printed, verbatim. Line endings are not normalised here: the runner
    /// applies that rule to both sides' observations at one place, so that the two sides
    /// cannot end up normalised by two rules that drift apart.
    /// </summary>
    private static string Text(StringWriter writer) => writer.ToString();

    private static HostInfo Host { get; } =
        new("dotnet", Environment.Version.ToString(), new HostCapabilities(JsStringBuiltins: null));

    /// <summary>
    /// This host could not run the case at all. Kept apart from the sample throwing: that is
    /// <see cref="Outcome.Exception"/> and carries a type name the two sides compare.
    /// </summary>
    private static Observation Failed(string detail) =>
        new(Observation.CurrentSchema, Outcome.HostError, null, string.Empty, string.Empty, null, detail, Host);
}
