using Xunit;
using Xunit.Sdk;

namespace CsWasm.Tests;

internal static class Snapshot
{
    private const string UpdateVariable = "CSWASM_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Compares <paramref name="actual"/> against a checked-in snapshot byte for byte.
    /// Set <c>CSWASM_UPDATE_SNAPSHOTS=1</c> to record the file from a run whose output has
    /// been inspected; the snapshot is never written implicitly.
    /// </summary>
    public static void Matches(string fileName, string actual)
    {
        var path = Path.Combine(TestPaths.RepositoryRoot, "tests", "CsWasm.Tests", "Snapshots", fileName);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        if (!File.Exists(path))
        {
            throw new XunitException(
                $"Snapshot '{fileName}' does not exist. Inspect the output below and, if it is correct, "
                + $"re-run with {UpdateVariable}=1 to record it.{Environment.NewLine}{actual}");
        }

        // Normalise only the expected side: a checkout may store CRLF, but the dump itself
        // must emit "\n" regardless of platform.
        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(expected, actual, ignoreLineEndingDifferences: false);
    }
}
