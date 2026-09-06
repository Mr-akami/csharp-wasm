using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;

namespace CsWasm.Driver;

/// <summary>
/// <c>cswasm dump il &lt;input.dll&gt;</c>: the debug view of what the CIL frontend read.
/// </summary>
/// <remarks>
/// Every failure the run produced - a missing input, unsupported metadata, an unsupported
/// opcode - is collected into one list in decoding order, and that single list decides both
/// the exit code and what reaches stderr. If it holds anything at all, stdout stays empty:
/// a dump that describes part of an assembly would read as if it described all of it
/// (docs/diagnostics.md rule 1).
/// </remarks>
internal static class DumpCommand
{
    public static int Il(string path, TextWriter stdout, TextWriter stderr)
    {
        var diagnostics = new List<Diagnostic>();

        if (!File.Exists(path))
        {
            diagnostics.Add(Diagnostic.Error(
                DiagnosticCode.InputNotFound,
                $"Input file '{path}' does not exist.",
                "Pass the path of a compiled assembly, for example bin/Debug/net9.0/Sample.dll."));
        }
        else
        {
            diagnostics.AddRange(CilAssemblyReader.Read(path, out var assembly));
            diagnostics.AddRange(SupportedInstructions.Validate(assembly));

            if (diagnostics.Count == 0)
            {
                stdout.Write(IlDumpWriter.Write(assembly));
                return CommandLine.ExitSuccess;
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(diagnostic.Format());
        }

        return CommandLine.ExitFailure;
    }
}
