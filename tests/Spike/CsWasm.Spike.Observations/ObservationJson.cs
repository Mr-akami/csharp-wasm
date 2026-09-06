using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsWasm.Spike.Observations;

/// <summary>
/// The wire form of <see cref="Observation"/>: one line of JSON, written by whichever host ran
/// the case and read by the runner that compares the two.
/// </summary>
/// <remarks>
/// The Node harness writes the same shape by hand. This type is where the spelling of the
/// field names lives on the .NET side; the harness is the other half and changes with it.
/// </remarks>
public static class ObservationJson
{
    public static string Write(Observation observation) =>
        JsonSerializer.Serialize(observation, ObservationJsonContext.Default.Observation);

    /// <summary>
    /// Reads an observation, or returns <see langword="null"/> when the text is not one. A
    /// caller that got null has to say so as a host error rather than invent a result.
    /// </summary>
    public static Observation? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ObservationJsonContext.Default.Observation);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Observation))]
internal sealed partial class ObservationJsonContext : JsonSerializerContext;
