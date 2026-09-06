using CsWasm.Backend.MoonBit;
using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;

namespace CsWasm.Driver;

/// <summary>
/// <c>cswasm compile &lt;input.dll&gt; -o &lt;output.wasm&gt;</c>: the whole run, from the
/// assembly to the module.
/// </summary>
/// <remarks>
/// This file owns paths, the life of the generated package and the exit code, and nothing
/// else. It spells no MoonBit: what a package looks like and how the toolchain is started
/// belong to src/Backend.MoonBit (docs/architecture.md section 9.5).
/// <para>
/// Every stage appends to one list of diagnostics, and that one list decides the exit code,
/// what reaches stderr and whether the output path is written at all. Nothing is written to it
/// until a module exists, so a failed run neither creates a file nor damages one an earlier run
/// left there (docs/diagnostics.md rule 1).
/// </para>
/// </remarks>
internal static class CompileCommand
{
    public static int Run(
        string input,
        string output,
        bool keepIntermediates,
        TextWriter stdout,
        TextWriter stderr)
    {
        var diagnostics = AssemblyPipeline.GenerateMoonBit(input, out var module);

        // The generated package is written only once the frontend and the backend agree there
        // is a whole assembly to write: a refused run leaves nothing behind anywhere.
        if (module is not null && !Failed(diagnostics))
        {
            Build(module, output, keepIntermediates, diagnostics, stdout);
        }

        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(diagnostic.Format());
        }

        return Failed(diagnostics) ? CommandLine.ExitFailure : CommandLine.ExitSuccess;
    }

    /// <summary>
    /// Builds <paramref name="module"/> in a temporary package directory and, if that produced
    /// a module, copies it to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// The directory is below the system temporary directory, so a default run leaves nothing
    /// in the directory the user invoked cswasm from (docs/architecture.md section 9.4). It is
    /// removed on every way out of this method - a module, a rejected build, an exception -
    /// unless the user asked to keep it, in which case the path is reported: kept intermediates
    /// a user cannot find are of no use for debugging. A kill signal is the one end no process
    /// can clean up after; that residue is left in the system temporary directory on purpose
    /// (docs/moonbit-packaging.md).
    /// </remarks>
    private static void Build(
        MoonBitModule module,
        string output,
        bool keepIntermediates,
        List<Diagnostic> diagnostics,
        TextWriter stdout)
    {
        var packageDirectory = Directory.CreateTempSubdirectory("cswasm-").FullName;

        try
        {
            diagnostics.AddRange(MoonBitBuild.Run(module, packageDirectory, out var wasm));

            if (wasm is not null && !Failed(diagnostics))
            {
                Copy(wasm, output);
            }
        }
        finally
        {
            if (keepIntermediates)
            {
                stdout.WriteLine("kept the generated MoonBit package in " + packageDirectory);
            }
            else
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }
    }

    private static void Copy(string wasm, string output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(wasm, output, overwrite: true);
    }

    private static bool Failed(List<Diagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
