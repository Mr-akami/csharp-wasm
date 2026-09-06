namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>How an instruction ends the block it sits in, if it ends one at all.</summary>
internal enum IlFlow
{
    /// <summary>Falls through to the next instruction.</summary>
    Normal,

    /// <summary>Transfers to one target unconditionally.</summary>
    Branch,

    /// <summary>Transfers to one target or falls through.</summary>
    ConditionalBranch,

    /// <summary>
    /// Ends the block without naming a single target to split on: <c>ret</c> leaves the
    /// method, and <c>switch</c>, <c>throw</c> and the exception machinery transfer control in
    /// ways the builder refuses. Splitting only has to know the block ends here.
    /// </summary>
    EndsBlock,
}

/// <summary>One basic block of IL: where it starts and the instructions it holds.</summary>
internal sealed record IlBlock(int IlOffset, IReadOnlyList<IlInstruction> Instructions);

/// <summary>
/// Splits an instruction stream into basic blocks. A leader is offset 0, every branch target,
/// and the instruction after anything that ends a block.
/// </summary>
internal static class SpikeSsaBlocks
{
    private static readonly HashSet<string> UnconditionalBranches = new(StringComparer.Ordinal)
    {
        "br", "br.s",
    };

    private static readonly HashSet<string> ConditionalBranches = new(StringComparer.Ordinal)
    {
        "brfalse", "brfalse.s", "brtrue", "brtrue.s",
        "beq", "beq.s", "bne.un", "bne.un.s",
        "bge", "bge.s", "bge.un", "bge.un.s",
        "bgt", "bgt.s", "bgt.un", "bgt.un.s",
        "ble", "ble.s", "ble.un", "ble.un.s",
        "blt", "blt.s", "blt.un", "blt.un.s",
    };

    private static readonly HashSet<string> BlockEnds = new(StringComparer.Ordinal)
    {
        "ret", "switch", "throw", "rethrow", "jmp",
        "leave", "leave.s", "endfinally", "endfilter",
    };

    private static IlFlow FlowOf(string opCodeName)
    {
        if (UnconditionalBranches.Contains(opCodeName))
        {
            return IlFlow.Branch;
        }

        if (ConditionalBranches.Contains(opCodeName))
        {
            return IlFlow.ConditionalBranch;
        }

        return BlockEnds.Contains(opCodeName) ? IlFlow.EndsBlock : IlFlow.Normal;
    }

    /// <summary>
    /// Splits <paramref name="instructions"/> into blocks in offset order. Returns null when a
    /// branch names a target that is not the offset of an instruction, which makes every block
    /// boundary after it a guess; <paramref name="invalidTarget"/> then holds that offset.
    /// </summary>
    public static IReadOnlyList<IlBlock>? Partition(
        IReadOnlyList<IlInstruction> instructions,
        out int invalidTarget)
    {
        invalidTarget = 0;

        if (instructions.Count == 0)
        {
            return [];
        }

        var offsets = new HashSet<int>(instructions.Select(instruction => instruction.Offset));
        var leaders = new HashSet<int> { instructions[0].Offset };

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            var flow = FlowOf(instruction.OpCodeName);

            if (flow is IlFlow.Branch or IlFlow.ConditionalBranch)
            {
                var target = instruction.IntOperand;
                if (target is null || !offsets.Contains(target.Value))
                {
                    invalidTarget = target ?? instruction.Offset;
                    return null;
                }

                leaders.Add(target.Value);
            }

            // Whatever follows a block-ending instruction starts a block of its own, even when
            // nothing branches to it: the exit of a loop is reached by falling out of the test.
            if (flow != IlFlow.Normal && index + 1 < instructions.Count)
            {
                leaders.Add(instructions[index + 1].Offset);
            }
        }

        var blocks = new List<IlBlock>();
        var start = 0;

        for (var index = 1; index <= instructions.Count; index++)
        {
            if (index < instructions.Count && !leaders.Contains(instructions[index].Offset))
            {
                continue;
            }

            blocks.Add(new IlBlock(
                instructions[start].Offset,
                instructions.Skip(start).Take(index - start).ToList()));
            start = index;
        }

        return blocks;
    }
}
