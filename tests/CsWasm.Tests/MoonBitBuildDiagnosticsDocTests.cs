using CsWasm.Diagnostics;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts DIAG-DOC and KEEP-CODES. Issue #17 reports a failed <c>moonc</c> run as CSW5004;
/// the Allocated table in docs/diagnostics.md is the registry of issued codes, so a code that
/// reaches a user without a row there cannot be looked up.
/// </summary>
/// <remarks>
/// The number is written down here rather than read from <c>DiagnosticCode</c> so that the
/// registry, not the definition, is what this file asserts about - the same shape
/// MoonBitBackendDiagnosticsDocTests uses for CSW4001 and CSW1005.
/// </remarks>
public sealed class MoonBitBuildDiagnosticsDocTests
{
    private const int MoonBitBuildFailed = 5004;

    private static readonly string[] DiagnosticsDocLines =
        File.ReadAllLines(Path.Combine(TestPaths.RepositoryRoot, "docs", "diagnostics.md"));

    // DIAG-DOC.
    [Fact]
    public void Csw5004IsListedAsAnErrorInTheAllocatedTable()
    {
        var id = new DiagnosticCode(MoonBitBuildFailed).Id;

        var row = Assert.Single(
            DiagnosticsDocLines,
            line => line.StartsWith($"| `{id}`", StringComparison.Ordinal));

        Assert.Contains("error", row, StringComparison.Ordinal);
    }

    // The band is the leading digit (docs/diagnostics.md): a failed run of the external MoonBit
    // compiler is a toolchain failure, not a backend mapping one.
    [Fact]
    public void Csw5004FallsInTheToolchainBand()
    {
        Assert.Equal(DiagnosticCategory.Toolchain, new DiagnosticCode(MoonBitBuildFailed).Category);
    }

    // KEEP-CODES: codes are allocated once and never reused, so the code this step introduces
    // must not take a number an earlier step already issued.
    [Fact]
    public void Csw5004DoesNotCollideWithTheAllocatedCodes()
    {
        var existing = new[]
        {
            DiagnosticCode.NotImplementedYet.Number,
            DiagnosticCode.BadCommandLine.Number,
            DiagnosticCode.InputNotFound.Number,
            DiagnosticCode.UnsupportedOpcode.Number,
            DiagnosticCode.UnsupportedMetadata.Number,
            DiagnosticCode.LocalAddressTaken.Number,
            DiagnosticCode.BodyNotNormalizable.Number,
            DiagnosticCode.ImplicitExceptionChecksNotInserted.Number,
            DiagnosticCode.BackendCannotLower.Number,
            DiagnosticCode.ToolchainNotFound.Number,
            DiagnosticCode.ToolchainVersionMismatch.Number,
            DiagnosticCode.ToolchainProbeFailed.Number,
        };

        Assert.DoesNotContain(MoonBitBuildFailed, existing);
    }
}
