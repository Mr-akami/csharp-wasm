using CsWasm.Diagnostics;

namespace CsWasm.Frontend.Cil;

/// <summary>
/// The instruction set this step accepts, and the CSW1001 report for everything else.
/// </summary>
/// <remarks>
/// An opcode outside the set is reported, never skipped: silently dropping an instruction
/// would make the dump describe a program that is not the one in the file
/// (docs/diagnostics.md rule 1). Every occurrence is reported in one pass so a file with
/// several unsupported instructions does not need one run per instruction.
/// </remarks>
public static class SupportedInstructions
{
    /// <summary>
    /// The opcodes issue #14 names, with their short forms, plus two the proof-of-concept
    /// cannot avoid: <c>conv.i4</c>, which the C# compiler emits after <c>ldlen</c> because
    /// <c>ldlen</c> pushes a native int, and <c>call</c>, which the implicit constructor of
    /// <c>Point</c> uses to reach its base constructor.
    /// </summary>
    /// <remarks>
    /// <c>ldloca</c> is in the set even though no local can be promoted to an SSA value once
    /// its address is taken. Reading and printing the instruction is this gate's business;
    /// refusing the promotion is the SSA builder's, under CSW1003. Leaving it out here would
    /// stop such a body at CSW1001 and make CSW1003 unreachable.
    /// </remarks>
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "ldarg", "ldarg.s", "ldarg.0", "ldarg.1", "ldarg.2", "ldarg.3",
        "ldloc", "ldloc.s", "ldloc.0", "ldloc.1", "ldloc.2", "ldloc.3",
        "stloc", "stloc.s", "stloc.0", "stloc.1", "stloc.2", "stloc.3",
        "ldloca", "ldloca.s",
        "ldfld", "stfld",
        "ldc.i4", "ldc.i4.s", "ldc.i4.m1",
        "ldc.i4.0", "ldc.i4.1", "ldc.i4.2", "ldc.i4.3", "ldc.i4.4",
        "ldc.i4.5", "ldc.i4.6", "ldc.i4.7", "ldc.i4.8",
        "add",
        "ldelem", "ldelem.i", "ldelem.i1", "ldelem.u1", "ldelem.i2", "ldelem.u2",
        "ldelem.i4", "ldelem.u4", "ldelem.i8", "ldelem.r4", "ldelem.r8", "ldelem.ref",
        "stelem", "stelem.i", "stelem.i1", "stelem.i2", "stelem.i4", "stelem.i8",
        "stelem.r4", "stelem.r8", "stelem.ref",
        "ldlen",
        "newobj", "newarr",
        "br", "br.s", "brfalse", "brfalse.s", "brtrue", "brtrue.s",
        "blt", "blt.s", "blt.un", "blt.un.s",
        "clt",
        "ret",
        "conv.i4",
        "call",
    };

    /// <summary>
    /// Returns one diagnostic per instruction outside the supported set, in decoding order:
    /// types in metadata table order, methods in table order, instructions by IL offset.
    /// An empty list means every instruction in <paramref name="assembly"/> is supported.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Validate(AssemblyModel assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var diagnostics = new List<Diagnostic>();

        foreach (var type in assembly.Types)
        {
            foreach (var method in type.Methods)
            {
                if (method.Body is null)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (Supported.Contains(instruction.OpCodeName))
                    {
                        continue;
                    }

                    var location = $"{type.FullName}::{method.Name} {MethodBodyDecoder.Label(instruction.Offset)}";
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCode.UnsupportedOpcode,
                        DiagnosticSeverity.Error,
                        $"Instruction '{instruction.OpCodeName}' at "
                        + $"{MethodBodyDecoder.Label(instruction.Offset)} in '{type.FullName}::{method.Name}' "
                        + "is outside the instruction set this step supports.",
                        location,
                        "Issue #14 covers the proof-of-concept instruction set; later steps widen it."));
                }
            }
        }

        return diagnostics;
    }
}
