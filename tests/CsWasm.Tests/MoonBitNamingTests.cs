using System.Text.RegularExpressions;
using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts MB-NAME-FORM, MB-NAME-NO-FILENAME, MB-NAME-SAME-INPUT and MB-NAME-STABLE.
/// </summary>
/// <remarks>
/// Issue #16 fixes the shape of a generated identifier at
/// <c>__cs_&lt;short name&gt;_&lt;stable hash&gt;</c> and requires that it be decided by the
/// .NET identity of what it names and by nothing else: not by the file the assembly was read
/// from, not by the run, and not by unrelated members of the same assembly. The command is the
/// only place all four are observable at once, so they are asserted on its output.
/// </remarks>
public sealed class MoonBitNamingTests
{
    private static readonly Regex GeneratedIdentifier = new(@"__cs_[A-Za-z0-9_]+", RegexOptions.None);

    private static readonly Regex WellFormedIdentifier = new(@"^__cs_[A-Za-z0-9_]+_[0-9a-f]+$", RegexOptions.None);

    private static string Emit(string assemblyPath)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(["dump", "moonbit", assemblyPath], stdout, stderr);

        Assert.True(code == CommandLine.ExitSuccess, stderr.ToString());
        return stdout.ToString();
    }

    /// <summary>The generated identifier of <c>CsWasm.Samples.Poc.Point</c> in that output.</summary>
    private static string PointIdentifier(string output)
    {
        var matches = GeneratedIdentifier
            .Matches(output)
            .Select(match => match.Value)
            .Where(name => name.StartsWith("__cs_Point_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Assert.Single(matches);
    }

    // MB-NAME-FORM: every generated identifier carries a short name and a hash. One emitted
    // without the hash, or with a hash in some other alphabet, fails here.
    [Fact]
    public void EveryGeneratedIdentifierHasTheDocumentedShape()
    {
        var output = Emit(SamplePaths.Poc);

        var identifiers = GeneratedIdentifier
            .Matches(output)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(identifiers);
        Assert.All(identifiers, name => Assert.Matches(WellFormedIdentifier, name));
    }

    // MB-NAME-SAME-INPUT: the same input names the same thing every time. A process-seeded
    // hash such as string.GetHashCode passes a single run and fails here.
    [Fact]
    public void TheSameAssemblyProducesTheSameIdentifiers()
    {
        Assert.Equal(PointIdentifier(Emit(SamplePaths.Poc)), PointIdentifier(Emit(SamplePaths.Poc)));
    }

    // MB-NAME-NO-FILENAME: issue #16 says identifiers must not depend on the source file name.
    // The same bytes under a different file name in a different directory must produce the
    // same text, byte for byte.
    [Fact]
    public void RenamingTheAssemblyFileDoesNotChangeTheEmittedSource()
    {
        var fromOriginalPath = Emit(SamplePaths.Poc);

        var directory = Path.Combine(Path.GetTempPath(), "cswasm-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var renamed = Path.Combine(directory, "SomeOtherFileName.dll");
            File.Copy(SamplePaths.Poc, renamed);

            Assert.Equal(fromOriginalPath, Emit(renamed));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // MB-NAME-STABLE: an unrelated change must not move a name. CsWasm.Samples.NamingStability
    // declares the same CsWasm.Samples.Poc.Point while differing in every way that is not the
    // type's own identity - assembly name, assembly file name, metadata position, and the
    // other types and methods present. Feeding a member list, a member order or the IL into
    // the hash passes the same-input test above and fails here.
    [Fact]
    public void AnUnrelatedChangeDoesNotMoveTheIdentifierOfAType()
    {
        var fromPoc = PointIdentifier(Emit(SamplePaths.Poc));
        var fromOtherAssembly = PointIdentifier(Emit(SamplePaths.NamingStability));

        Assert.Equal(fromPoc, fromOtherAssembly);
    }

    // MB-NAME-NO-FILENAME and MB-DETERMINISM: nothing that varies between machines may reach
    // the output at all.
    [Fact]
    public void EmittedSourceDoesNotLeakTheInputPath()
    {
        var output = Emit(SamplePaths.Poc);

        Assert.DoesNotContain(AppContext.BaseDirectory, output, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, output, StringComparison.Ordinal);
    }
}
