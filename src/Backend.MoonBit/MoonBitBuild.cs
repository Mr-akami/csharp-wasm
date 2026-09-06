using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;

namespace CsWasm.Backend.MoonBit;

/// <summary>
/// Drives the external MoonBit toolchain over a generated package: writes it out, runs the
/// pinned <c>moon</c>, collects what it said, and finds the module it produced
/// (docs/architecture.md section 9.4).
/// </summary>
/// <remarks>
/// Only the pinned toolchain is ever started. The version check is the one
/// <c>cswasm toolchain</c> already makes, so a mismatched or missing executable is reported as
/// CSW5001, CSW5002 or CSW5003 here too rather than under a second set of codes.
/// <para>
/// A failed build is classified once. The first location the toolchain reported decides the
/// diagnostic's location, and that one diagnostic decides the exit code, what reaches stderr
/// and whether anything is written to the output path: the caller never picks one field out of
/// one error and another field out of another.
/// </para>
/// <para>
/// The location is inside the generated MoonBit, with its raw output attached. Mapping it back
/// to a C# position is Step 8 (docs/diagnostics.md rule 2).
/// </para>
/// </remarks>
public static class MoonBitBuild
{
    /// <summary>The tool cswasm starts. <c>moon</c> is what drives <c>moonc</c>.</summary>
    private const string Tool = "moon";

    /// <summary>
    /// The first location a MoonBit diagnostic reports, which it draws as
    /// <c>╭─[ &lt;path&gt;:&lt;line&gt;:&lt;column&gt; ]</c> above the offending source line.
    /// </summary>
    private static readonly Regex ReportedLocation =
        new(@"╭─\[\s*(?<path>[^\r\n\]]+?):(?<line>\d+):(?<column>\d+)\s*\]", RegexOptions.None);

    /// <summary>
    /// The command line cswasm runs, exactly as it is passed to the process. It is one value so
    /// that what a test observes is what a build starts.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string packageDirectory) =>
    [
        // -C keeps the build inside the generated package: moon is never asked to guess a
        // project from the directory the user happened to invoke cswasm in.
        "-C",
        packageDirectory,
        "build",
        "--target",
        MoonBitPackageLayout.Target,
        "--release",
    ];

    /// <summary>
    /// Writes <paramref name="module"/> below <paramref name="packageDirectory"/> and builds
    /// it. <paramref name="wasm"/> is the module that came out, or null when the run produced
    /// none; the returned list is empty exactly when it is not null.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Run(
        MoonBitModule module,
        string packageDirectory,
        out string? wasm)
    {
        Write(module, packageDirectory);
        return Build(packageDirectory, out wasm);
    }

    /// <summary>Writes the files of the generated package below <paramref name="packageDirectory"/>.</summary>
    public static void Write(MoonBitModule module, string packageDirectory)
    {
        foreach (var file in MoonBitPackageLayout.Files(module))
        {
            var path = Path.Combine(packageDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Text);
        }
    }

    /// <summary>
    /// Builds the package already written below <paramref name="packageDirectory"/>.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Build(string packageDirectory, out string? wasm)
    {
        wasm = null;

        var pin = MoonBitToolchain.Verify(ToolchainPin.Current, out _);
        if (pin.Count > 0)
        {
            return pin;
        }

        var exe = MoonBitToolchain.FindExecutable(Tool);
        if (exe is null)
        {
            return
            [
                Diagnostic.Error(
                    DiagnosticCode.ToolchainNotFound,
                    $"MoonBit executable '{Tool}' was not found on PATH or under MOON_HOME.",
                    "Enter the pinned dev shell with `nix develop`, which materialises the pinned toolchain."),
            ];
        }

        int exitCode;
        string output;
        try
        {
            exitCode = Start(exe, packageDirectory, out output);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return
            [
                Diagnostic.Error(
                    DiagnosticCode.ToolchainProbeFailed,
                    $"Failed to run '{exe} {string.Join(' ', Arguments(packageDirectory))}': {ex.Message}"),
            ];
        }

        if (exitCode != 0)
        {
            return [Rejected(packageDirectory, output)];
        }

        wasm = FindArtifact(packageDirectory);
        if (wasm is null)
        {
            return [NoArtifact(packageDirectory, output)];
        }

        return [];
    }

    /// <summary>
    /// The module <c>moon</c> left below <paramref name="packageDirectory"/>, or null if there
    /// is none.
    /// </summary>
    /// <remarks>
    /// The expected path is tried first and a search is the fallback, which is the way
    /// tools/moonbit-smoke.sh already treats this: where moon puts its output is not part of
    /// MoonBit's contract and has moved between releases, so a build that produced a module
    /// somewhere else is a success, not a failure.
    /// </remarks>
    public static string? FindArtifact(string packageDirectory)
    {
        var expected = Path.Combine(packageDirectory, MoonBitPackageLayout.ArtifactRelativePath);
        if (File.Exists(expected))
        {
            return expected;
        }

        return Directory
            .EnumerateFiles(packageDirectory, "*.wasm", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Runs the tool, collecting both of its streams into one transcript.</summary>
    private static int Start(string exe, string packageDirectory, out string output)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in Arguments(packageDirectory))
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start '{exe}'.");

        // Both streams are read before waiting: moon writes its diagnostics to one and its
        // progress to the other, and a full pipe on either would deadlock the wait.
        var readStandardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = readStandardError.GetAwaiter().GetResult();
        process.WaitForExit();

        var transcript = new StringBuilder();
        transcript.Append(standardOutput);
        if (standardOutput.Length > 0 && standardError.Length > 0 && !standardOutput.EndsWith('\n'))
        {
            transcript.Append('\n');
        }

        transcript.Append(standardError);
        output = transcript.ToString();

        return process.ExitCode;
    }

    private static Diagnostic Rejected(string packageDirectory, string output) =>
        new(
            DiagnosticCode.MoonBitBuildFailed,
            DiagnosticSeverity.Error,
            "the pinned MoonBit compiler rejected the generated package:"
            + Environment.NewLine + output.TrimEnd(),
            Location(packageDirectory, output),
            "The location is in the generated MoonBit; keep it with --keep-intermediates to read it. "
            + "Mapping it back to the C# it came from is Step 8.");

    private static Diagnostic NoArtifact(string packageDirectory, string output) =>
        new(
            DiagnosticCode.MoonBitBuildFailed,
            DiagnosticSeverity.Error,
            "the pinned MoonBit compiler reported success but produced no .wasm module:"
            + Environment.NewLine + output.TrimEnd(),
            Location(packageDirectory, output),
            "Keep the generated package with --keep-intermediates and run the build by hand to see "
            + "what it left behind.");

    /// <summary>
    /// Where the failure is, in the generated MoonBit. The first position the toolchain
    /// reported wins; when it reported none, or reported one outside the generated source, the
    /// generated file itself is named, because a location a reader cannot open is worse than a
    /// coarse one.
    /// </summary>
    private static string Location(string packageDirectory, string output)
    {
        var generated = Path.Combine(
            packageDirectory,
            MoonBitPackageLayout.SourceDirectory,
            MoonBitPackageLayout.PackageName,
            MoonBitPackageLayout.PackageName + ".mbt");

        var match = ReportedLocation.Match(output);
        if (!match.Success)
        {
            return generated;
        }

        var path = match.Groups["path"].Value.Trim();
        return path.EndsWith(".mbt", StringComparison.Ordinal)
            ? path + ":" + match.Groups["line"].Value
            : generated;
    }
}
