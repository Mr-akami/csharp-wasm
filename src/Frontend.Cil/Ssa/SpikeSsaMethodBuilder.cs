using System.Globalization;
using CsWasm.Diagnostics;

namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>
/// Normalises one method body. The evaluation stack and the promoted locals are carried as a
/// state vector: values flow along the edges between blocks, and a merge turns that vector
/// into block parameters.
/// </summary>
/// <remarks>
/// The stack effect of an instruction is described once, by <see cref="TryDescribe"/>, and
/// used twice: first over types, to find the entry stack of every block and so the parameter
/// list of every merge, then over values, to build the instructions themselves. Describing it
/// twice would let the two passes disagree about the same body.
/// </remarks>
internal sealed class SpikeSsaMethodBuilder
{
    private readonly TypeModel type;
    private readonly MethodModel method;
    private readonly MethodBodyModel body;
    private readonly List<Diagnostic> diagnostics = [];

    private readonly Dictionary<int, IlBlock> blocksByOffset = [];
    private readonly Dictionary<int, int> indexByOffset = [];
    private readonly Dictionary<int, IReadOnlyList<int>> successorsByOffset = [];
    private readonly Dictionary<int, IReadOnlyList<SpikeSsaType>> entryStacks = [];
    private readonly HashSet<int> parameterisedBlocks = [];
    private readonly Dictionary<int, StateVector> inherited = [];

    private IReadOnlyList<IlBlock> blocks = [];
    private SpikeSsaType[] argumentTypes = [];
    private SpikeSsaType[] localTypes = [];
    private SpikeSsaValue[] argumentValues = [];
    private SpikeSsaSignature signature = new([], [], null);
    private int nextValueId;

    public SpikeSsaMethodBuilder(TypeModel type, MethodModel method, MethodBodyModel body)
    {
        this.type = type;
        this.method = method;
        this.body = body;
    }

    /// <summary>What the body was refused for, when <see cref="Build"/> returned null.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    /// <summary>Returns null when the body was refused; the reason is in <see cref="Diagnostics"/>.</summary>
    public SpikeSsaMethod? Build()
    {
        if (!TryRefuseAddressTakenLocal())
        {
            return null;
        }

        var partitioned = SpikeSsaBlocks.Partition(body.Instructions, out var invalidTarget);
        if (partitioned is null)
        {
            Refuse(
                0,
                $"a branch names {MethodBodyDecoder.Label(invalidTarget)}, "
                + "which is not where an instruction starts.");
            return null;
        }

        if (partitioned.Count == 0)
        {
            Refuse(0, "the body carries no instructions.");
            return null;
        }

        blocks = partitioned;
        for (var index = 0; index < blocks.Count; index++)
        {
            blocksByOffset[blocks[index].IlOffset] = blocks[index];
            indexByOffset[blocks[index].IlOffset] = index;
        }

        var entryOffset = blocks[0].IlOffset;
        if (!TryMapSignature(entryOffset) || !TryComputeEntryStacks(entryOffset) || !TryFindMerges(entryOffset))
        {
            return null;
        }

        return BuildBlocks(entryOffset);
    }

    // A local whose address is taken is not a value: something else may write through the
    // pointer, so promoting it would describe a program that is not the one in the file.
    private bool TryRefuseAddressTakenLocal()
    {
        foreach (var instruction in body.Instructions)
        {
            if (!SpikeSsaSteps.AddressOfLocal.Contains(instruction.OpCodeName))
            {
                continue;
            }

            diagnostics.Add(new Diagnostic(
                DiagnosticCode.LocalAddressTaken,
                DiagnosticSeverity.Error,
                $"'{instruction.OpCodeName}' at {MethodBodyDecoder.Label(instruction.Offset)} in "
                + $"'{type.FullName}::{method.Name}' takes the address of a local, so the locals of that "
                + "method cannot be promoted to SSA values.",
                Location(instruction.Offset),
                "Issue #15 promotes only locals whose address is never taken."));
            return false;
        }

        return true;
    }

    private bool TryMapSignature(int offset)
    {
        var arguments = new List<SpikeSsaType>();

        // Argument 0 of an instance method is the instance itself, which no signature lists.
        if (!method.IsStatic)
        {
            arguments.Add(SpikeSsaType.Reference(type.FullName));
        }

        foreach (var parameterTypeName in method.ParameterTypeNames)
        {
            if (!TryMapType(parameterTypeName, offset, out var mapped))
            {
                return false;
            }

            arguments.Add(mapped);
        }

        var locals = new List<SpikeSsaType>();
        foreach (var local in body.Locals)
        {
            if (!TryMapType(local.TypeName, offset, out var mapped))
            {
                return false;
            }

            locals.Add(mapped);
        }

        SpikeSsaType? returnType = null;
        if (!string.Equals(method.ReturnTypeName, "void", StringComparison.Ordinal))
        {
            if (!TryMapType(method.ReturnTypeName, offset, out var mapped))
            {
                return false;
            }

            returnType = mapped;
        }

        argumentTypes = [.. arguments];
        localTypes = [.. locals];
        signature = new SpikeSsaSignature(argumentTypes, localTypes, returnType);
        return true;
    }

    /// <summary>
    /// Walks the reachable blocks from the entry, applying every instruction to a stack of
    /// types. The keys of <see cref="entryStacks"/> afterwards are exactly the reachable
    /// blocks; a block only the exception machinery can enter is not one of them.
    /// </summary>
    private bool TryComputeEntryStacks(int entryOffset)
    {
        entryStacks[entryOffset] = [];
        var pending = new Queue<int>();
        pending.Enqueue(entryOffset);

        while (pending.Count > 0)
        {
            var offset = pending.Dequeue();
            var block = blocksByOffset[offset];
            var stack = new List<SpikeSsaType>(entryStacks[offset]);

            var last = Step.Nothing;
            foreach (var instruction in block.Instructions)
            {
                if (!TryDescribe(instruction, stack, out var step) || !TryApplyTypes(instruction, step, stack))
                {
                    return false;
                }

                last = step;
            }

            var successors = SuccessorsOf(offset, block.Instructions[^1], last);
            if (successors is null)
            {
                return false;
            }

            foreach (var successor in successors)
            {
                if (!entryStacks.TryGetValue(successor, out var recorded))
                {
                    entryStacks[successor] = [.. stack];
                    pending.Enqueue(successor);
                    continue;
                }

                if (recorded.Count != stack.Count)
                {
                    Refuse(
                        block.Instructions[^1].Offset,
                        $"{MethodBodyDecoder.Label(successor)} is reached with {stack.Count} value(s) on the "
                        + $"evaluation stack here and {recorded.Count} on another path, so its parameters "
                        + "cannot be decided.");
                    return false;
                }

                if (!recorded.SequenceEqual(stack))
                {
                    Refuse(
                        block.Instructions[^1].Offset,
                        $"{MethodBodyDecoder.Label(successor)} is reached with evaluation stack values of "
                        + "different types on different paths.");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the blocks a value can reach along more than one edge. Those are the merges, and
    /// only they take parameters: a block with one predecessor is dominated by it and sees its
    /// values directly.
    /// </summary>
    private bool TryFindMerges(int entryOffset)
    {
        var predecessors = new Dictionary<int, int>();

        foreach (var block in blocks)
        {
            if (!entryStacks.ContainsKey(block.IlOffset))
            {
                continue;
            }

            foreach (var successor in successorsByOffset[block.IlOffset])
            {
                predecessors[successor] = predecessors.GetValueOrDefault(successor) + 1;
            }
        }

        if (predecessors.GetValueOrDefault(entryOffset) > 0)
        {
            Refuse(
                entryOffset,
                "the entry block is a branch target, so the arguments and the initial value of every local "
                + "would have to be defined in a block that can be re-entered.");
            return false;
        }

        foreach (var (offset, count) in predecessors)
        {
            if (count >= 2)
            {
                parameterisedBlocks.Add(offset);
            }
        }

        return true;
    }

    private SpikeSsaMethod? BuildBlocks(int entryOffset)
    {
        var built = new List<SpikeSsaBlock>();

        foreach (var offset in ReversePostOrder(entryOffset))
        {
            var block = BuildBlock(offset, entryOffset);
            if (block is null)
            {
                return null;
            }

            built.Add(block);
        }

        return Renumber(built.OrderBy(block => block.IlOffset).ToList());
    }

    private SpikeSsaBlock? BuildBlock(int offset, int entryOffset)
    {
        var instructions = new List<SpikeSsaInstruction>();
        var parameters = new List<SpikeSsaValue>();
        List<SpikeSsaValue> stack;
        SpikeSsaValue[] locals;

        if (parameterisedBlocks.Contains(offset))
        {
            // The merge's parameters are the entry stack slots, bottom to top, then the
            // promoted locals in slot order. Every edge into it passes its own values in that
            // order, which is what makes the merge explicit instead of implied.
            var entryStack = entryStacks[offset];
            foreach (var slotType in entryStack)
            {
                parameters.Add(NewValue(slotType));
            }

            foreach (var localType in localTypes)
            {
                parameters.Add(NewValue(localType));
            }

            stack = parameters.Take(entryStack.Count).ToList();
            locals = parameters.Skip(entryStack.Count).ToArray();
        }
        else if (offset == entryOffset)
        {
            stack = [];
            locals = new SpikeSsaValue[localTypes.Length];
            argumentValues = new SpikeSsaValue[argumentTypes.Length];

            for (var slot = 0; slot < argumentTypes.Length; slot++)
            {
                argumentValues[slot] = NewValue(argumentTypes[slot]);
                instructions.Add(new SpikeSsaInstruction(
                    argumentValues[slot],
                    "arg",
                    [],
                    slot.ToString(CultureInfo.InvariantCulture)));
            }

            // ".locals init" zeroes every local before the body runs, and the state vector
            // needs a value for each of them from the first edge onwards.
            for (var slot = 0; slot < localTypes.Length; slot++)
            {
                locals[slot] = NewValue(localTypes[slot]);
                instructions.Add(new SpikeSsaInstruction(
                    locals[slot],
                    "const",
                    [],
                    SpikeSsaTypes.ZeroLiteral(localTypes[slot])));
            }
        }
        else if (inherited.TryGetValue(offset, out var state))
        {
            stack = [.. state.Stack];
            locals = [.. state.Locals];
        }
        else
        {
            Refuse(
                offset,
                $"{MethodBodyDecoder.Label(offset)} has one predecessor but is reached before it, so the "
                + "values it sees are not decided.");
            return null;
        }

        SpikeSsaTerminator? terminator = null;

        foreach (var instruction in blocksByOffset[offset].Instructions)
        {
            if (!TryDescribe(instruction, [.. stack.Select(value => value.Type)], out var step))
            {
                return null;
            }

            switch (step.Kind)
            {
                case StepKind.None:
                    break;

                case StepKind.PushArgument:
                    stack.Add(argumentValues[step.Slot]);
                    break;

                case StepKind.PushLocal:
                    stack.Add(locals[step.Slot]);
                    break;

                case StepKind.StoreLocal:
                    locals[step.Slot] = Pop(stack, 1)[0];
                    break;

                case StepKind.Value:
                {
                    var operands = Pop(stack, step.PopCount);
                    var result = step.ResultType is null ? null : NewValue(step.ResultType);
                    instructions.Add(new SpikeSsaInstruction(result, step.Op, operands, step.Detail, step.Member));

                    if (result is not null)
                    {
                        stack.Add(result);
                    }

                    break;
                }

                case StepKind.Return:
                    terminator = new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.Return,
                        null,
                        Pop(stack, step.PopCount),
                        []);
                    break;

                case StepKind.Branch:
                    terminator = new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.Branch,
                        null,
                        [],
                        [MakeEdge(successorsByOffset[offset][0], stack, locals)]);
                    break;

                default:
                {
                    var operands = Pop(stack, step.PopCount);
                    var successors = successorsByOffset[offset];
                    terminator = new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.CondBranch,
                        step.Condition,
                        operands,
                        [MakeEdge(successors[0], stack, locals), MakeEdge(successors[1], stack, locals)]);
                    break;
                }
            }
        }

        // A block that ends because the next instruction starts one leaves along a branch of
        // its own, so the merge it falls into is reached the same way every other merge is.
        terminator ??= new SpikeSsaTerminator(
            SpikeSsaTerminatorKind.Branch,
            null,
            [],
            [MakeEdge(successorsByOffset[offset][0], stack, locals)]);

        return new SpikeSsaBlock(offset, parameters, instructions, terminator);
    }

    /// <summary>
    /// Builds the edge to <paramref name="target"/>. A merge is passed the whole state vector;
    /// a block with a single predecessor is handed it directly, which is why it needs no
    /// parameters.
    /// </summary>
    private SpikeSsaEdge MakeEdge(int target, IReadOnlyList<SpikeSsaValue> stack, SpikeSsaValue[] locals)
    {
        if (parameterisedBlocks.Contains(target))
        {
            return new SpikeSsaEdge(target, [.. stack, .. locals]);
        }

        inherited[target] = new StateVector([.. stack], [.. locals]);
        return new SpikeSsaEdge(target, []);
    }

    /// <summary>
    /// Reverse post-order, so a block with one predecessor is always built after it: the only
    /// edge that can run backwards is one into a block with two predecessors, and that block
    /// takes parameters instead of inheriting anything.
    /// </summary>
    private List<int> ReversePostOrder(int entryOffset)
    {
        var order = new List<int>();
        var visited = new HashSet<int> { entryOffset };
        var pending = new Stack<(int Offset, int Next)>();
        pending.Push((entryOffset, 0));

        while (pending.Count > 0)
        {
            var (offset, next) = pending.Pop();
            var successors = successorsByOffset[offset];

            if (next == successors.Count)
            {
                order.Add(offset);
                continue;
            }

            pending.Push((offset, next + 1));

            if (visited.Add(successors[next]))
            {
                pending.Push((successors[next], 0));
            }
        }

        order.Reverse();
        return order;
    }

    /// <summary>
    /// Numbers the values in reading order - blocks by IL offset, parameters before
    /// instructions - so the dump reads top to bottom and does not expose the order the
    /// builder happened to walk the graph in.
    /// </summary>
    private SpikeSsaMethod Renumber(IReadOnlyList<SpikeSsaBlock> ordered)
    {
        var renumbered = new Dictionary<int, int>();

        foreach (var block in ordered)
        {
            foreach (var parameter in block.Parameters)
            {
                renumbered[parameter.Id] = renumbered.Count;
            }

            foreach (var instruction in block.Instructions)
            {
                if (instruction.Result is not null)
                {
                    renumbered[instruction.Result.Id] = renumbered.Count;
                }
            }
        }

        SpikeSsaValue Remap(SpikeSsaValue value) => value with { Id = renumbered[value.Id] };

        IReadOnlyList<SpikeSsaValue> RemapAll(IReadOnlyList<SpikeSsaValue> values) =>
            [.. values.Select(Remap)];

        return new SpikeSsaMethod(
            method.Name,
            method.IsStatic,
            signature.ReturnType,
            [.. ordered.Select(block => new SpikeSsaBlock(
                block.IlOffset,
                RemapAll(block.Parameters),
                [.. block.Instructions.Select(instruction => new SpikeSsaInstruction(
                    instruction.Result is null ? null : Remap(instruction.Result),
                    instruction.Op,
                    RemapAll(instruction.Operands),
                    instruction.Detail,
                    instruction.Member))],
                new SpikeSsaTerminator(
                    block.Terminator.Kind,
                    block.Terminator.Condition,
                    RemapAll(block.Terminator.Operands),
                    [.. block.Terminator.Successors.Select(edge =>
                        new SpikeSsaEdge(edge.TargetIlOffset, RemapAll(edge.Arguments)))])))]);
    }

    /// <summary>
    /// Where a block goes, decided by what its last instruction turned out to be rather than by
    /// a second reading of the opcode. An instruction that transfers control in a way this step
    /// does not model has already been refused by <see cref="TryDescribe"/>, so the cases here
    /// are the ones that survive it.
    /// </summary>
    private IReadOnlyList<int>? SuccessorsOf(int offset, IlInstruction last, Step step)
    {
        var index = indexByOffset[offset];
        var fallThrough = index + 1 < blocks.Count ? blocks[index + 1].IlOffset : (int?)null;

        // Partition has already checked that a branch names an offset an instruction starts at.
        var target = last.IntOperand ?? 0;
        IReadOnlyList<int> successors;

        switch (step.Kind)
        {
            case StepKind.Return:
                successors = [];
                break;

            case StepKind.Branch:
                successors = [target];
                break;

            case StepKind.CondBranch when fallThrough is not null:
                successors = [target, fallThrough.Value];
                break;

            case StepKind.CondBranch:
                Refuse(last.Offset, $"'{last.OpCodeName}' has nothing to fall through to.");
                return null;

            default:
                if (fallThrough is null)
                {
                    Refuse(
                        last.Offset,
                        $"'{last.OpCodeName}' runs off the end of the body instead of leaving it.");
                    return null;
                }

                successors = [fallThrough.Value];
                break;
        }

        successorsByOffset[offset] = successors;
        return successors;
    }

    /// <summary>
    /// Describes one instruction, or refuses the body with the reason the description failed
    /// for. Both passes go through here, so neither can accept what the other rejects.
    /// </summary>
    private bool TryDescribe(IlInstruction instruction, IReadOnlyList<SpikeSsaType> stack, out Step step)
    {
        var described = SpikeSsaSteps.Describe(instruction, stack, signature, out var reason);

        if (described is null)
        {
            Refuse(instruction.Offset, reason);
            step = Step.Nothing;
            return false;
        }

        step = described;
        return true;
    }

    private bool TryApplyTypes(IlInstruction instruction, Step step, List<SpikeSsaType> stack)
    {
        switch (step.Kind)
        {
            case StepKind.None:
                return true;

            case StepKind.PushArgument:
                stack.Add(argumentTypes[step.Slot]);
                return true;

            case StepKind.PushLocal:
                stack.Add(localTypes[step.Slot]);
                return true;

            case StepKind.StoreLocal:
            {
                var stored = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                if (stored != localTypes[step.Slot])
                {
                    Refuse(
                        instruction.Offset,
                        $"a {SpikeSsaTypes.Render(stored)} is stored into local {step.Slot}, which is "
                        + $"declared {SpikeSsaTypes.Render(localTypes[step.Slot])}.");
                    return false;
                }

                return true;
            }

            default:
                stack.RemoveRange(stack.Count - step.PopCount, step.PopCount);

                if (step.ResultType is not null)
                {
                    stack.Add(step.ResultType);
                }

                return true;
        }
    }

    private static IReadOnlyList<SpikeSsaValue> Pop(List<SpikeSsaValue> stack, int count)
    {
        var values = stack.GetRange(stack.Count - count, count);
        stack.RemoveRange(stack.Count - count, count);
        return values;
    }

    private SpikeSsaValue NewValue(SpikeSsaType valueType) => new(nextValueId++, valueType);

    private bool TryMapType(string? ilTypeName, int offset, out SpikeSsaType mapped)
    {
        var candidate = SpikeSsaTypes.Map(ilTypeName);

        if (candidate is null)
        {
            Refuse(
                offset,
                $"'{ilTypeName ?? "an unnamed type"}' is outside the type family this step models "
                + "(i32, i64, f64, bool, ref<T>, array<T>).");
            mapped = SpikeSsaType.Int32;
            return false;
        }

        mapped = candidate;
        return true;
    }

    private void Refuse(int ilOffset, string reason)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticCode.BodyNotNormalizable,
            DiagnosticSeverity.Error,
            $"'{type.FullName}::{method.Name}' cannot be normalised into SSA: {reason}",
            Location(ilOffset),
            "Issue #15 normalises the proof-of-concept subset; later steps widen it."));
    }

    private string Location(int ilOffset) =>
        $"{type.FullName}::{method.Name} {MethodBodyDecoder.Label(ilOffset)}";

    /// <summary>The evaluation stack and the promoted locals as one block sees them.</summary>
    private sealed record StateVector(IReadOnlyList<SpikeSsaValue> Stack, IReadOnlyList<SpikeSsaValue> Locals);
}
