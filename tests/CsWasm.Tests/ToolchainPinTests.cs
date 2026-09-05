using Xunit;

namespace CsWasm.Tests;

public sealed class ToolchainPinTests
{
    [Fact]
    public void PinIsEmbeddedAndParsed()
    {
        var pin = ToolchainPin.Current;

        Assert.False(string.IsNullOrWhiteSpace(pin.MoonBit.Version));
        Assert.False(string.IsNullOrWhiteSpace(pin.MoonBit.MoonVersion));
        Assert.False(string.IsNullOrWhiteSpace(pin.Dotnet.Sdk));
        Assert.False(string.IsNullOrWhiteSpace(pin.Node.Version));
        Assert.False(string.IsNullOrWhiteSpace(pin.WasmTools.Version));
    }

    [Fact]
    public void PinMatchesTheCheckedInToolchainFile()
    {
        // The embedded resource must not drift from the file the Nix flake reads.
        var repoRoot = TestPaths.RepositoryRoot;
        var json = File.ReadAllText(Path.Combine(repoRoot, "tools", "toolchain.json"));

        Assert.Contains($"\"{ToolchainPin.Current.MoonBit.Version}\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"{ToolchainPin.Current.Node.Version}\"", json, StringComparison.Ordinal);
    }
}
