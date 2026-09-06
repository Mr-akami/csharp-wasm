using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;
using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Driver;

/// <summary>
/// The run every command that needs generated MoonBit makes: read the assembly, apply the
/// opcode gate, normalise into SSA, emit.
/// </summary>
/// <remarks>
/// It is one owner because <c>dump moonbit</c> and <c>compile</c> are the same run up to what
/// they do with the result, and two copies of it would drift apart at the first stage anyone
/// added.
/// <para>
/// Every failure of the run - a missing input, unsupported metadata, an unsupported opcode, a
/// body that cannot be normalised, a construct the backend cannot lower - is collected into
/// one list in decoding order. The caller makes one decision from that one list
/// (docs/diagnostics.md rule 1).
/// </para>
/// </remarks>
internal static class AssemblyPipeline
{
    /// <summary>
    /// Reads the assembly and applies the opcode gate, appending what either refused. Returns
    /// null when there was no file to read.
    /// </summary>
    public static AssemblyModel? Read(string path, List<Diagnostic> diagnostics)
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

    /// <summary>
    /// Generates the MoonBit for the assembly at <paramref name="path"/>.
    /// <paramref name="module"/> is null when any stage reported an error.
    /// </summary>
    public static List<Diagnostic> GenerateMoonBit(string path, out MoonBitModule? module)
    {
        var diagnostics = new List<Diagnostic>();
        var assembly = Read(path, diagnostics);
        module = null;

        if (assembly is null)
        {
            return diagnostics;
        }

        diagnostics.AddRange(SpikeSsaBuilder.Build(assembly, out var ssa));

        // The backend runs only over a model that is whole: lowering part of an assembly
        // would report failures of the part the frontend already refused.
        if (diagnostics.Count == 0)
        {
            diagnostics.AddRange(MoonBitEmitter.Emit(ssa, out module));
        }

        return diagnostics;
    }
}
