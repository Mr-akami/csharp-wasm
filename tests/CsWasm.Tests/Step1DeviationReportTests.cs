using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract MB-REPORT. Issue #16 decides that this step inserts no implicit exception checks
/// and requires the resulting departure from C# semantics to be stated in
/// <c>docs/reports/step1.md</c> as well as in a diagnostic. A deviation recorded only in a
/// warning on stderr is gone as soon as the run is over.
/// </summary>
public sealed class Step1DeviationReportTests
{
    private static readonly string ReportPath =
        Path.Combine(TestPaths.RepositoryRoot, "docs", "reports", "step1.md");

    [Fact]
    public void TheStepOneReportExists()
    {
        Assert.True(File.Exists(ReportPath), ReportPath + " does not exist.");
    }

    // The three check kinds issue #16 names, each stated rather than summarised as "checks".
    [Theory]
    [InlineData("null")]
    [InlineData("bounds")]
    [InlineData("zero")]
    public void TheReportNamesTheCheckKindThatIsNotInserted(string kind)
    {
        var report = File.ReadAllText(ReportPath);

        Assert.Contains(kind, report, StringComparison.OrdinalIgnoreCase);
    }

    // The report is where a reader finds the code to look up, so it carries it.
    [Fact]
    public void TheReportPointsAtTheDiagnosticThatReportsTheDeviation()
    {
        var report = File.ReadAllText(ReportPath);

        Assert.Contains("CSW1005", report, StringComparison.Ordinal);
    }
}
