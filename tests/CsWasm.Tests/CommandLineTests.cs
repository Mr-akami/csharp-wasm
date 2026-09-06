using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

public sealed class CommandLineTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void VersionPrintsThePinnedToolchain()
    {
        var (code, output, _) = Run("--version");

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Contains(ToolchainPin.Current.MoonBit.Version, output, StringComparison.Ordinal);
        Assert.Contains(ToolchainPin.Current.Node.Version, output, StringComparison.Ordinal);
    }

    [Fact]
    public void NoArgumentsPrintsUsage()
    {
        var (code, output, _) = Run();

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    // KEEP-CHECK: issue #17 lifts the refusal for compile only. check keeps its own, and its
    // CSW0001 is asserted here rather than in CompileCommandTests because it is the contract
    // this step leaves alone.
    [Fact]
    public void CheckRefusesInsteadOfPretending()
    {
        var (code, _, error) = Run("check", "Sample.dll");

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0001", error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandIsRejected()
    {
        var (code, _, error) = Run("frobnicate");

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0002", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolchainCommandAgreesWithThePin()
    {
        var (code, output, error) = Run("toolchain");

        Assert.True(code == CommandLine.ExitSuccess, error);
        Assert.Contains(ToolchainPin.Current.MoonBit.Version, output, StringComparison.Ordinal);
    }
}
