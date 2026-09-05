using CsWasm.Backend.MoonBit;
using Xunit;

namespace CsWasm.Tests;

public sealed class MoonBitToolchainTests
{
    [Theory]
    [InlineData("v0.10.11+6ff76a5f9 (2026-08-28)", "0.10.11+6ff76a5f9")]
    [InlineData("moon 0.1.20260827 (d0aaa07 2026-08-27)", "0.1.20260827")]
    [InlineData("moonrun 0.1.20260827 (d0aaa07 2026-08-27)", "0.1.20260827")]
    [InlineData("0.10.11", "0.10.11")]
    public void ParsesVersionBanners(string banner, string expected) =>
        Assert.Equal(expected, MoonBitToolchain.ParseVersion(banner));

    [Fact]
    public void ParsesOnlyTheFirstLine() =>
        Assert.Equal(
            "0.10.11+6ff76a5f9",
            MoonBitToolchain.ParseVersion("v0.10.11+6ff76a5f9 (2026-08-28)\n\nFeature flags enabled: rr_moon_mod\n"));

    [Fact]
    public void PinnedToolchainIsOnPath()
    {
        // The dev shell and CI both put the pinned MoonBit in front of anything else.
        var diagnostics = MoonBitToolchain.Verify(ToolchainPin.Current, out var found);

        Assert.Empty(diagnostics.Select(d => d.Format()));
        Assert.Equal(ToolchainPin.Current.MoonBit.Version, found["moonc"]);
        Assert.Equal(ToolchainPin.Current.MoonBit.MoonVersion, found["moon"]);
    }
}
