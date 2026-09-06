using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract MB-NO-SYNTAX-LEAK. Issue #16 states that generated MoonBit is an output format,
/// not an IR, and that MoonBit's syntactic constraints stay inside the emitter instead of
/// leaking back into the passes in front of it (docs/architecture.md section 4.2).
/// </summary>
/// <remarks>
/// The tokens checked for are MoonBit surface syntax and the generated-identifier prefix, not
/// the word MoonBit: src/Common legitimately names the pinned MoonBit toolchain, and that is a
/// different concern from the frontend knowing how MoonBit spells an array.
/// </remarks>
public sealed class MoonBitSyntaxContainmentTests
{
    private static readonly string[] MoonBitSurfaceSyntax = ["Array[", "let mut", "__cs_"];

    public static TheoryData<string> FrontFacingProjects => new() { "Frontend.Cil", "Common" };

    [Theory]
    [MemberData(nameof(FrontFacingProjects))]
    public void ThePassesInFrontOfTheEmitterCarryNoMoonBitSyntax(string project)
    {
        var directory = Path.Combine(TestPaths.RepositoryRoot, "src", project);
        Assert.True(Directory.Exists(directory), directory + " does not exist.");

        var offenders = Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .SelectMany(file => MoonBitSurfaceSyntax
                .Where(token => file.Text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{file.Name}: {token}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
