using CsWasm.Diagnostics;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract CIL-DOCS (requirement 19). The Allocated table in docs/diagnostics.md is the
/// registry of issued codes, not prose: a code that reaches a user without a row there cannot
/// be looked up. The code the frontend emits is the source of the expectation, so defining a
/// code without documenting it fails here. Only the row and its severity column are checked,
/// leaving the wording free to change.
/// </summary>
public sealed class DiagnosticsDocTests
{
    private static readonly string[] DiagnosticsDocLines =
        File.ReadAllLines(Path.Combine(TestPaths.RepositoryRoot, "docs", "diagnostics.md"));

    public static TheoryData<int> FrontendCodeNumbers => new()
    {
        DiagnosticCode.UnsupportedOpcode.Number,
        DiagnosticCode.UnsupportedMetadata.Number,
    };

    [Theory]
    [MemberData(nameof(FrontendCodeNumbers))]
    public void FrontendCodesAreListedInTheAllocatedTable(int number)
    {
        var id = new DiagnosticCode(number).Id;

        var row = Assert.Single(
            DiagnosticsDocLines,
            line => line.StartsWith($"| `{id}`", StringComparison.Ordinal));

        Assert.Contains("error", row, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCodesAreAllocatedInTheDocumentedBand()
    {
        Assert.Equal(1001, DiagnosticCode.UnsupportedOpcode.Number);
        Assert.Equal(1002, DiagnosticCode.UnsupportedMetadata.Number);
    }
}
