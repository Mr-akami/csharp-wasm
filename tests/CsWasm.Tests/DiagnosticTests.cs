using CsWasm.Diagnostics;
using Xunit;

namespace CsWasm.Tests;

public sealed class DiagnosticTests
{
    [Theory]
    [InlineData(1, "CSW0001", DiagnosticCategory.Driver)]
    [InlineData(1203, "CSW1203", DiagnosticCategory.Frontend)]
    [InlineData(2001, "CSW2001", DiagnosticCategory.Compatibility)]
    [InlineData(3010, "CSW3010", DiagnosticCategory.WholeProgram)]
    [InlineData(4100, "CSW4100", DiagnosticCategory.BackendMapping)]
    [InlineData(5002, "CSW5002", DiagnosticCategory.Toolchain)]
    [InlineData(6001, "CSW6001", DiagnosticCategory.HostAbi)]
    public void CodesRenderAndClassify(int number, string id, DiagnosticCategory category)
    {
        var code = new DiagnosticCode(number);

        Assert.Equal(id, code.Id);
        Assert.Equal(category, code.Category);
    }

    [Fact]
    public void ErrorFormatsWithCodeAndHelp()
    {
        var text = Diagnostic.Error(DiagnosticCode.ToolchainVersionMismatch, "moonc is old.", "Update the pin.").Format();

        Assert.Contains("error CSW5002: moonc is old.", text, StringComparison.Ordinal);
        Assert.Contains("help: Update the pin.", text, StringComparison.Ordinal);
    }
}
