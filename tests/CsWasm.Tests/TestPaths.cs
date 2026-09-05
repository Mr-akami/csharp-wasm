namespace CsWasm.Tests;

internal static class TestPaths
{
    /// <summary>Walks up from the test binaries to the repository root.</summary>
    public static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CsWasm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
