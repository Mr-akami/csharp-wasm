using System.Globalization;
using System.Text;

namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>
/// Renders <see cref="SpikeSsaAssembly"/> as the text <c>cswasm dump ssa</c> prints.
/// </summary>
/// <remarks>
/// The output is a fixed function of the model, on the same terms as
/// <see cref="IlDumpWriter"/>: line separators are "\n" on every platform, numbers use the
/// invariant culture, members appear in metadata table order and blocks in IL offset order.
/// Nothing that varies between machines or runs - a path, a timestamp, a version - is printed.
/// Block labels come from <see cref="MethodBodyDecoder.Label"/>, so a block in this dump and
/// an instruction in the IL dump can be lined up by name.
/// </remarks>
public static class SpikeSsaDumpWriter
{
    private const char LineFeed = '\n';

    public static string Write(SpikeSsaAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var text = new StringBuilder();
        text.Append("assembly ").Append(assembly.Name).Append(LineFeed);

        foreach (var type in assembly.Types)
        {
            text.Append("type ").Append(type.FullName).Append(LineFeed);

            foreach (var method in type.Methods)
            {
                AppendMethod(text, method);
            }
        }

        return text.ToString();
    }

    private static void AppendMethod(StringBuilder text, SpikeSsaMethod method)
    {
        text.Append("  method ").Append(method.Name).Append(LineFeed);

        foreach (var block in method.Blocks)
        {
            text.Append("    ").Append(MethodBodyDecoder.Label(block.IlOffset));

            if (block.Parameters.Count > 0)
            {
                text.Append('(')
                    .Append(string.Join(", ", block.Parameters.Select(Declare)))
                    .Append(')');
            }

            text.Append(':').Append(LineFeed);

            foreach (var instruction in block.Instructions)
            {
                text.Append("      ").Append(Render(instruction)).Append(LineFeed);
            }

            text.Append("      ").Append(Render(block.Terminator)).Append(LineFeed);
        }
    }

    private static string Render(SpikeSsaInstruction instruction)
    {
        var operands = new List<string>();

        if (instruction.Detail is not null)
        {
            operands.Add(instruction.Detail);
        }

        operands.AddRange(instruction.Operands.Select(Reference));

        var rendered = operands.Count == 0
            ? instruction.Op
            : instruction.Op + " " + string.Join(", ", operands);

        return instruction.Result is null ? rendered : Declare(instruction.Result) + " = " + rendered;
    }

    private static string Render(SpikeSsaTerminator terminator)
    {
        var operands = string.Join(", ", terminator.Operands.Select(Reference));

        switch (terminator.Kind)
        {
            case SpikeSsaTerminatorKind.Return:
                return terminator.Operands.Count == 0 ? "return" : "return " + operands;

            case SpikeSsaTerminatorKind.Branch:
                return "br " + Render(terminator.Successors[0]);

            default:
                return "br." + terminator.Condition + " " + operands
                    + " -> " + Render(terminator.Successors[0])
                    + " else " + Render(terminator.Successors[1]);
        }
    }

    private static string Render(SpikeSsaEdge edge)
    {
        var label = MethodBodyDecoder.Label(edge.TargetIlOffset);

        return edge.Arguments.Count == 0
            ? label
            : label + "(" + string.Join(", ", edge.Arguments.Select(Reference)) + ")";
    }

    /// <summary>Where a value is defined, its type is spelled out; where it is used, it is not.</summary>
    private static string Declare(SpikeSsaValue value) =>
        Reference(value) + " : " + SpikeSsaTypes.Render(value.Type);

    private static string Reference(SpikeSsaValue value) =>
        "%" + value.Id.ToString(CultureInfo.InvariantCulture);
}
