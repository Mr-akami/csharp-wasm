using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;
using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Driver;

/// <summary>
/// The <c>cswasm dump</c> commands: the debug views of what the CIL frontend read and of what
/// it normalised that into.
/// </summary>
/// <remarks>
/// Every failure the run produced - a missing input, unsupported metadata, an unsupported
/// opcode, a body that cannot be normalised - is collected into one list in decoding order,
/// and that single list decides both the exit code and what reaches stderr. If it holds
/// anything at all, stdout stays empty: a dump that describes part of an assembly would read
/// as if it described all of it (docs/diagnostics.md rule 1).
/// </remarks>
internal static class DumpCommand
{
    public static int Il(string path, TextWriter stdout, TextWriter stderr)
    {
        var diagnostics = new List<Diagnostic>();
        var assembly = Read(path, diagnostics);

        if (assembly is not null && diagnostics.Count == 0)
        {
            stdout.Write(IlDumpWriter.Write(assembly));
            return CommandLine.ExitSuccess;
        }

        return Report(diagnostics, stderr);
    }

    public static int Ssa(string path, TextWriter stdout, TextWriter stderr)
    {
        var diagnostics = new List<Diagnostic>();
        var assembly = Read(path, diagnostics);

        if (assembly is not null)
        {
            // Normalisation runs even when the opcode gate already refused something: an
            // address-taken local is a different failure from an unsupported opcode, and one
            // run reports both rather than making the user fix them one at a time.
            diagnostics.AddRange(SpikeSsaBuilder.Build(assembly, out var ssa));

            if (diagnostics.Count == 0)
            {
                stdout.Write(SpikeSsaDumpWriter.Write(ssa));
                return CommandLine.ExitSuccess;
            }
        }

        return Report(diagnostics, stderr);
    }

    /// <summary>
    /// Prints the MoonBit the backend generates for the assembly.
    /// </summary>
    /// <remarks>
    /// One list, one decision: the run fails if it holds an error, and the exit code, whether
    /// stdout is written and what reaches stderr all follow from that one answer. CSW1005 is a
    /// warning, so a run that only reports it still prints its source - the deviation it names
    /// is in the source, not instead of it.
    /// </remarks>
    public static int MoonBit(string path, TextWriter stdout, TextWriter stderr)
    {
        var diagnostics = new List<Diagnostic>();
        var assembly = Read(path, diagnostics);
        string? source = null;

        if (assembly is not null)
        {
            diagnostics.AddRange(SpikeSsaBuilder.Build(assembly, out var ssa));

            // The backend runs only over a model that is whole: lowering part of an assembly
            // would report failures of the part the frontend already refused.
            if (diagnostics.Count == 0)
            {
                diagnostics.AddRange(MoonBitEmitter.Emit(ssa, out source));
            }
        }

        var failed = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(diagnostic.Format());
        }

        if (failed || source is null)
        {
            return CommandLine.ExitFailure;
        }

        stdout.Write(source);
        return CommandLine.ExitSuccess;
    }

    /// <summary>
    /// Reads the assembly and applies the opcode gate, appending what either refused. Returns
    /// null when there was no file to read.
    /// </summary>
    private static AssemblyModel? Read(string path, List<Diagnostic> diagnostics)
    {
        if (!File.Exists(path))
        {
            diagnostics.Add(Diagnostic.Error(
                DiagnosticCode.InputNotFound,
                $"Input file '{path}' does not exist.",
                "Pass the path of a compiled assembly, for example bin/Debug/net9.0/Sample.dll."));
            return null;
        }

        diagnostics.AddRange(CilAssemblyReader.Read(path, out var assembly));
        diagnostics.AddRange(SupportedInstructions.Validate(assembly));
        return assembly;
    }

    private static int Report(IReadOnlyList<Diagnostic> diagnostics, TextWriter stderr)
    {
        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(diagnostic.Format());
        }

        return CommandLine.ExitFailure;
    }
}
