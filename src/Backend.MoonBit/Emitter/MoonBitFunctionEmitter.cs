using System.Globalization;
using System.Text;
using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// <c>emit_function</c>: turns one SSA method into one MoonBit <c>fn</c>
/// (docs/architecture.md section 9.1).
/// </summary>
/// <remarks>
/// The mapping issue #16 fixes decides the shape: a static C# method is a plain <c>fn</c>, a
/// loop is a MoonBit <c>loop</c> whose continuation arguments carry the values that change
/// round it, and a block reached along a single edge is written where that edge is rather than
/// as a function of its own. A control-flow shape outside that set is refused under CSW4001
/// instead of being approximated.
/// </remarks>
internal sealed class MoonBitFunctionEmitter
{
    private const char LineFeed = '\n';

    private const string Indent = "  ";

    private readonly string ownerFullName;
    private readonly SpikeSsaMethod method;
    private readonly List<Diagnostic> diagnostics;

    private readonly Dictionary<int, SpikeSsaBlock> blocksByOffset = [];
    private readonly HashSet<int> written = [];

    private int? loopHeader;
    private bool insideLoop;
    private bool refused;

    public MoonBitFunctionEmitter(string ownerFullName, SpikeSsaMethod method, List<Diagnostic> diagnostics)
    {
        this.ownerFullName = ownerFullName;
        this.method = method;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// The <c>fn</c> for the method, or null when this step cannot lower it. The reason is
    /// appended to the diagnostic list the emitter was given.
    /// </summary>
    public string? Emit()
    {
        if (method.Blocks.Count == 0)
        {
            Refuse(null, "the method has no blocks.");
            return null;
        }

        foreach (var block in method.Blocks)
        {
            blocksByOffset[block.IlOffset] = block;
        }

        var entry = method.Blocks[0];
        if (!TryFindLoopHeader(entry.IlOffset) || !TrySignature(entry, out var signature))
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append("// ").Append(ownerFullName).Append("::").Append(method.Name).Append(LineFeed);
        text.Append(signature).Append(LineFeed);

        WriteBlock(entry.IlOffset, 1, text);

        if (refused)
        {
            return null;
        }

        text.Append('}').Append(LineFeed);
        return text.ToString();
    }

    /// <summary>
    /// Finds the one block an edge runs backwards into, which is the loop this step writes as
    /// a MoonBit <c>loop</c>. A method with more than one, or with a merge that is not a loop,
    /// is outside the shape issue #16 names and is refused.
    /// </summary>
    private bool TryFindLoopHeader(int entryOffset)
    {
        var headers = new List<int>();
        var onPath = new HashSet<int>();
        var finished = new HashSet<int>();

        void Walk(int offset)
        {
            onPath.Add(offset);

            foreach (var successor in Successors(offset))
            {
                if (onPath.Contains(successor))
                {
                    if (!headers.Contains(successor))
                    {
                        headers.Add(successor);
                    }
                }
                else if (finished.Add(successor))
                {
                    Walk(successor);
                }
            }

            onPath.Remove(offset);
        }

        finished.Add(entryOffset);
        Walk(entryOffset);

        if (headers.Count > 1)
        {
            Refuse(null, "the method has more than one loop, and this step writes one.");
            return false;
        }

        loopHeader = headers.Count == 1 ? headers[0] : null;

        foreach (var block in method.Blocks)
        {
            if (block.Parameters.Count > 0 && block.IlOffset != loopHeader)
            {
                Refuse(
                    block.IlOffset,
                    "a merge that is not a loop takes block arguments, and this step writes none.");
                return false;
            }
        }

        if (loopHeader is int header && blocksByOffset[header].Parameters.Count == 0)
        {
            Refuse(header, "a loop carries no values round it, and this step writes a loop that does.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The <c>fn</c> line. The parameters are the <c>arg</c> operations the entry block opens
    /// with, in the order it defines them, which is argument slot order; their
    /// <see cref="SpikeSsaInstruction.Detail"/> is display text and is not read back.
    /// </summary>
    private bool TrySignature(SpikeSsaBlock entry, out string signature)
    {
        signature = string.Empty;
        var parameters = new List<string>();

        foreach (var instruction in entry.Instructions)
        {
            if (!string.Equals(instruction.Op, "arg", StringComparison.Ordinal))
            {
                break;
            }

            if (instruction.Result is not { } argument)
            {
                Refuse(entry.IlOffset, "an argument defines no value.");
                return false;
            }

            var argumentType = MoonBitTypeNames.Of(argument.Type);
            if (argumentType is null)
            {
                Refuse(entry.IlOffset, $"argument '{MoonBitNames.OfValue(argument)}' has a type this step "
                    + "cannot write down.");
                return false;
            }

            parameters.Add(MoonBitNames.OfValue(argument) + " : " + argumentType);
        }

        var returnType = MoonBitTypeNames.Unit;
        if (method.ReturnType is { } declared)
        {
            var mapped = MoonBitTypeNames.Of(declared);
            if (mapped is null)
            {
                Refuse(null, "the return type is one this step cannot write down.");
                return false;
            }

            returnType = mapped;
        }

        signature = "fn " + MoonBitNames.OfMethod(ownerFullName, method, DeclaredParameterTypes(entry))
            + "(" + string.Join(", ", parameters) + ") -> " + returnType + " {";
        return true;
    }

    /// <summary>
    /// The types the method declares, which is every argument except the instance an instance
    /// method receives in slot 0.
    /// </summary>
    private IReadOnlyList<SpikeSsaType> DeclaredParameterTypes(SpikeSsaBlock entry)
    {
        var arguments = entry.Instructions
            .TakeWhile(instruction => string.Equals(instruction.Op, "arg", StringComparison.Ordinal))
            .Select(instruction => instruction.Result!.Type)
            .ToList();

        return method.IsStatic ? arguments : arguments.Skip(1).ToList();
    }

    private void WriteBlock(int offset, int depth, StringBuilder text)
    {
        if (refused)
        {
            return;
        }

        if (!written.Add(offset))
        {
            Refuse(offset, "the block is reached from more than one place without being a loop.");
            return;
        }

        var block = blocksByOffset[offset];
        Line(text, depth, "// " + Label(offset));

        foreach (var instruction in block.Instructions)
        {
            var lowered = MoonBitInstructionEmitter.Emit(instruction, out var reason);

            if (lowered is null)
            {
                Refuse(offset, reason);
                return;
            }

            if (lowered.Length > 0)
            {
                Line(text, depth, lowered);
            }
        }

        WriteTerminator(block, depth, text);
    }

    private void WriteTerminator(SpikeSsaBlock block, int depth, StringBuilder text)
    {
        var terminator = block.Terminator;

        switch (terminator.Kind)
        {
            case SpikeSsaTerminatorKind.Return:
                // The returned value is the tail expression of the block it is written in,
                // which is how a MoonBit function and a MoonBit loop both give their value.
                Line(
                    text,
                    depth,
                    terminator.Operands.Count == 0
                        ? "()"
                        : MoonBitNames.OfValue(terminator.Operands[0]));
                return;

            case SpikeSsaTerminatorKind.Branch:
                WriteEdge(terminator.Successors[0], depth, text);
                return;

            default:
            {
                var condition = Condition(terminator);
                if (condition is null)
                {
                    Refuse(
                        block.IlOffset,
                        $"the condition '{terminator.Condition}' has no MoonBit form in this step.");
                    return;
                }

                Line(text, depth, "if " + condition + " {");
                WriteEdge(terminator.Successors[0], depth + 1, text);
                Line(text, depth, "} else {");
                WriteEdge(terminator.Successors[1], depth + 1, text);
                Line(text, depth, "}");
                return;
            }
        }
    }

    private void WriteEdge(SpikeSsaEdge edge, int depth, StringBuilder text)
    {
        if (refused)
        {
            return;
        }

        if (edge.TargetIlOffset != loopHeader)
        {
            WriteBlock(edge.TargetIlOffset, depth, text);
            return;
        }

        var arguments = string.Join(", ", edge.Arguments.Select(MoonBitNames.OfValue));

        if (insideLoop)
        {
            Line(text, depth, "continue " + arguments);
            return;
        }

        WriteLoop(edge.TargetIlOffset, arguments, depth, text);
    }

    private void WriteLoop(int header, string arguments, int depth, StringBuilder text)
    {
        var parameters = string.Join(", ", blocksByOffset[header].Parameters.Select(MoonBitNames.OfValue));

        // The header's own IL label is written by the block itself, inside the arm.
        Line(text, depth, "loop " + arguments + " {");
        Line(text, depth + 1, parameters + " => {");

        insideLoop = true;
        WriteBlock(header, depth + 2, text);
        insideLoop = false;

        Line(text, depth + 1, "}");
        Line(text, depth, "}");
    }

    /// <summary>
    /// The comparison a conditional branch makes. Only the forms this step has a MoonBit
    /// operator for are written; the rest are refused by the caller.
    /// </summary>
    private static string? Condition(SpikeSsaTerminator terminator)
    {
        var operands = terminator.Operands;

        return terminator.Condition switch
        {
            "lt" when operands.Count == 2 =>
                MoonBitNames.OfValue(operands[0]) + " < " + MoonBitNames.OfValue(operands[1]),
            "true" when operands is [{ Type.Kind: SpikeSsaTypeKind.Bool }] =>
                MoonBitNames.OfValue(operands[0]),
            "false" when operands is [{ Type.Kind: SpikeSsaTypeKind.Bool }] =>
                "not(" + MoonBitNames.OfValue(operands[0]) + ")",
            _ => null,
        };
    }

    private IReadOnlyList<int> Successors(int offset) =>
        [.. blocksByOffset[offset].Terminator.Successors.Select(edge => edge.TargetIlOffset)];

    private static void Line(StringBuilder text, int depth, string content)
    {
        for (var level = 0; level < depth; level++)
        {
            text.Append(Indent);
        }

        text.Append(content).Append(LineFeed);
    }

    /// <summary>
    /// The IL offset the way every other cswasm view spells it, so a reader can line the
    /// generated source up with <c>dump il</c> and <c>dump ssa</c>.
    /// </summary>
    private static string Label(int ilOffset) =>
        "IL_" + ilOffset.ToString("x4", CultureInfo.InvariantCulture);

    private void Refuse(int? ilOffset, string reason)
    {
        refused = true;

        var location = ownerFullName + "::" + method.Name
            + (ilOffset is int offset ? " " + Label(offset) : string.Empty);

        diagnostics.Add(new Diagnostic(
            DiagnosticCode.BackendCannotLower,
            DiagnosticSeverity.Error,
            $"'{ownerFullName}::{method.Name}' cannot be lowered to MoonBit: {reason}",
            location,
            "Issue #16 maps the proof-of-concept subset; later steps widen it."));
    }
}
