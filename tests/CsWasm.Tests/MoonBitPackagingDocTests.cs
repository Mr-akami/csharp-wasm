using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract PKG-LINKDOC. Issue #17 decides how a function is exported from a wasm-gc build -
/// the <c>moon.pkg.json</c> link settings - and requires that decision to be recorded in
/// <c>docs/moonbit-packaging.md</c> so that a MoonBit-side change has one place to be answered.
/// A packaging rule that exists only inside the emitter is gone as soon as MoonBit moves it.
/// </summary>
/// <remarks>
/// Only the two names the decision is about are checked, leaving the wording free to change:
/// this is the shape Step1DeviationReportTests already uses for docs/reports/step1.md.
/// </remarks>
public sealed class MoonBitPackagingDocTests
{
    private static readonly string DocumentPath =
        Path.Combine(TestPaths.RepositoryRoot, "docs", "moonbit-packaging.md");

    [Fact]
    public void ThePackagingDocumentExists()
    {
        Assert.True(File.Exists(DocumentPath), DocumentPath + " does not exist.");
    }

    [Theory]
    [InlineData("moon.pkg.json")]
    [InlineData("wasm-gc")]
    public void ThePackagingDocumentRecordsWhereTheExportDecisionLives(string subject)
    {
        var document = File.ReadAllText(DocumentPath);

        Assert.Contains(subject, document, StringComparison.Ordinal);
    }
}
