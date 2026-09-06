using CsWasm.Diagnostics;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts MB-DOC-4001, MB-DOC-1005 and KEEP-CODES. Issue #16 asks for both codes to be
/// added to the Allocated table in docs/diagnostics.md; a code that reaches a user without a
/// row there cannot be looked up.
/// </summary>
/// <remarks>
/// The severity column is part of the row, and the two codes differ in it: CSW4001 refuses a
/// construct and CSW1005 only reports a deviation, which is why the run it appears in still
/// prints its output. Documenting CSW1005 as an error would contradict that.
/// </remarks>
public sealed class MoonBitBackendDiagnosticsDocTests
{
    private const int ImplicitExceptionChecksNotInserted = 1005;

    private const int BackendCannotLower = 4001;

    private static readonly string[] DiagnosticsDocLines =
        File.ReadAllLines(Path.Combine(TestPaths.RepositoryRoot, "docs", "diagnostics.md"));

    private static string Row(int number)
    {
        var id = new DiagnosticCode(number).Id;

        return Assert.Single(
            DiagnosticsDocLines,
            line => line.StartsWith($"| `{id}`", StringComparison.Ordinal));
    }

    // MB-DOC-4001.
    [Fact]
    public void Csw4001IsListedAsAnErrorInTheAllocatedTable()
    {
        Assert.Contains("error", Row(BackendCannotLower), StringComparison.Ordinal);
    }

    // MB-DOC-1005: the row records that this one is a warning.
    [Fact]
    public void Csw1005IsListedAsAWarningInTheAllocatedTable()
    {
        var row = Row(ImplicitExceptionChecksNotInserted);

        Assert.Contains("warning", row, StringComparison.Ordinal);
        Assert.DoesNotContain("error", row, StringComparison.Ordinal);
    }

    // The band is the leading digit (docs/diagnostics.md): CSW1005 is a C# semantics we cannot
    // represent, CSW4001 is a construct the MoonBit backend cannot lower.
    [Fact]
    public void TheNewCodesFallInTheDocumentedBands()
    {
        Assert.Equal(
            DiagnosticCategory.Frontend,
            new DiagnosticCode(ImplicitExceptionChecksNotInserted).Category);

        Assert.Equal(
            DiagnosticCategory.BackendMapping,
            new DiagnosticCode(BackendCannotLower).Category);
    }

    // KEEP-CODES: codes are allocated once and never reused, so the two this step introduces
    // must not take a number the earlier steps already issued.
    [Fact]
    public void TheNewCodesDoNotCollideWithTheAllocatedOnes()
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
            DiagnosticCode.ToolchainNotFound.Number,
            DiagnosticCode.ToolchainVersionMismatch.Number,
            DiagnosticCode.ToolchainProbeFailed.Number,
        };

        Assert.DoesNotContain(ImplicitExceptionChecksNotInserted, existing);
        Assert.DoesNotContain(BackendCannotLower, existing);
    }
}
