using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsWasm;

/// <summary>
/// The pinned toolchain versions, read from the embedded copy of tools/toolchain.json.
/// A build never picks up whatever MoonBit happens to be installed: it either finds the
/// pinned version or fails with a CSW5xxx diagnostic.
/// </summary>
public sealed record ToolchainPin(
    MoonBitPin MoonBit,
    DotnetPin Dotnet,
    NodePin Node,
    WasmToolsPin WasmTools)
{
    private const string ResourceName = "CsWasm.toolchain.json";

    private static readonly Lazy<ToolchainPin> Cached = new(LoadCore, isThreadSafe: true);

    /// <summary>The pin this build of cswasm was compiled against.</summary>
    public static ToolchainPin Current => Cached.Value;

    private static ToolchainPin LoadCore()
    {
        var assembly = typeof(ToolchainPin).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing. The build did not embed tools/toolchain.json.");

        return JsonSerializer.Deserialize(stream, ToolchainJsonContext.Default.ToolchainPin)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is empty.");
    }
}

public sealed record MoonBitPin(
    string Version,
    string MoonVersion,
    string MoonrunVersion,
    string PinnedOn);

public sealed record DotnetPin(string Sdk, string Channel);

public sealed record NodePin(string Version);

public sealed record WasmToolsPin(string Version);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ToolchainPin))]
internal sealed partial class ToolchainJsonContext : JsonSerializerContext;
