using CsWasm.Diagnostics;

namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>
/// Normalises the CIL evaluation stack into explicit values: the tiny internal SSA of
/// docs/architecture.md section 25 phase 0, so that later passes do not have to keep
/// simulating a stack (section 5.2).
/// </summary>
/// <remarks>
/// This is deliberately not CsIR. It models one method body at a time, merges with block
/// arguments rather than phi nodes, and refuses anything it cannot represent: a body that
/// takes the address of a local (CSW1003) or that this step cannot normalise (CSW1004) is
/// left out of the model instead of being half-built (docs/diagnostics.md rule 1).
/// </remarks>
public static class SpikeSsaBuilder
{
    /// <summary>
    /// Normalises <paramref name="assembly"/> into <paramref name="ssa"/> and returns one
    /// diagnostic list for the whole run, in decoding order. An empty list means every method
    /// with a body was normalised. A method definition that carries no IL is not a failure and
    /// simply contributes nothing.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Build(AssemblyModel assembly, out SpikeSsaAssembly ssa)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var diagnostics = new List<Diagnostic>();
        var types = new List<SpikeSsaTypeDefinition>();

        foreach (var type in assembly.Types)
        {
            var methods = new List<SpikeSsaMethod>();

            foreach (var method in type.Methods)
            {
                if (method.Body is null)
                {
                    continue;
                }

                var builder = new SpikeSsaMethodBuilder(type, method, method.Body);
                var built = builder.Build();

                if (built is null)
                {
                    diagnostics.AddRange(builder.Diagnostics);
                }
                else
                {
                    methods.Add(built);
                }
            }

            // A field whose type is outside the family is recorded with no type rather than
            // refused: issue #15 decides which bodies normalise, and a type declaration that
            // no normalised body reads is not a body.
            var fields = type.Fields
                .Select(field => new SpikeSsaField(field.Name, SpikeSsaTypes.Map(field.TypeName)))
                .ToList();

            types.Add(new SpikeSsaTypeDefinition(type.FullName, fields, methods));
        }

        ssa = new SpikeSsaAssembly(assembly.Name, types);
        return diagnostics;
    }
}
