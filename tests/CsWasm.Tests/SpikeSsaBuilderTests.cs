using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;
using CsWasm.Frontend.Cil.Ssa;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts SSA-BLOCKS, SSA-STACKHEIGHT, SSA-STACKARGS, SSA-VALUES, SSA-LOCALS,
/// SSA-BLOCKARGS, SSA-TYPES, SSA-SUM, SSA-SUM-MERGE, SSA-CSW1003 and SSA-CSW1004: what
/// <see cref="SpikeSsaBuilder"/> makes of a body, observed on the model rather than on the
/// dump text, so the structure is asserted independently of the recorded snapshot.
/// </summary>
public sealed class SpikeSsaBuilderTests
{
    private const int Entry = 0x00;
    private const int LoopBody = 0x06;
    private const int LoopHeader = 0x1e;
    private const int Exit = 0x24;

    private static SpikeSsaAssembly BuildPoc()
    {
        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);
        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.True(
            diagnostics.Count == 0,
            "The proof-of-concept sample must normalise cleanly, but the builder reported:\n"
            + string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Format())));

        return ssa;
    }

    private static SpikeSsaTypeDefinition TypeOf(SpikeSsaAssembly ssa, string typeFullName) =>
        Assert.Single(ssa.Types,
            candidate => string.Equals(candidate.FullName, typeFullName, StringComparison.Ordinal));

    private static SpikeSsaMethod Method(SpikeSsaAssembly ssa, string typeFullName, string methodName)
    {
        var type = TypeOf(ssa, typeFullName);

        return Assert.Single(type.Methods,
            candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal));
    }

    private static SpikeSsaMethod Sum() => Method(BuildPoc(), "CsWasm.Samples.Poc.Sample", "Sum");

    private static SpikeSsaBlock BlockAt(SpikeSsaMethod method, int ilOffset)
    {
        var block = method.Blocks.SingleOrDefault(candidate => candidate.IlOffset == ilOffset);
        Assert.True(
            block is not null,
            $"No block at IL_{ilOffset:x4}. Blocks built: "
            + string.Join(", ", method.Blocks.Select(candidate => $"IL_{candidate.IlOffset:x4}")));
        return block!;
    }

    /// <summary>Every edge that names <paramref name="target"/>, paired with the block it leaves.</summary>
    private static IReadOnlyList<(SpikeSsaBlock From, SpikeSsaEdge Edge)> EdgesInto(
        SpikeSsaMethod method,
        int target) =>
        method.Blocks
            .SelectMany(block => block.Terminator.Successors.Select(edge => (From: block, Edge: edge)))
            .Where(entry => entry.Edge.TargetIlOffset == target)
            .ToList();

    // SSA-SUM: the completion condition needs Sum to reach the model at all, with a body.
    [Fact]
    public void SumIsNormalisedIntoBlocks()
    {
        var sum = Sum();

        Assert.NotEmpty(sum.Blocks);
        Assert.All(sum.Blocks, block => Assert.NotNull(block.Terminator));
    }

    // SSA-BLOCKS: leaders are offset 0, every branch target and the instruction after every
    // branch. Sum's only branches are br.s at IL_0004 and blt.s at IL_0022, which makes
    // IL_0006 both a target and a fall-through leader and IL_0024 a fall-through leader only.
    // A builder that keeps the stream in one block, or that misses the instruction after a
    // branch, produces a different set.
    [Fact]
    public void SumIsSplitAtBranchTargetsAndAfterBranches()
    {
        Assert.Equal(new[] { Entry, LoopBody, LoopHeader, Exit }, Sum().Blocks.Select(block => block.IlOffset));
    }

    // SSA-VALUES: every stack operation is gone and what remains is the value vocabulary of
    // docs/architecture.md section 6.3. Asserted per block so an op appearing in the wrong
    // place is not absorbed by a set comparison. IL_0000 is left to the assertions below
    // because its instruction count also depends on how locals are given their initial value.
    [Theory]
    [InlineData(LoopBody, new[] { "array.get", "field.get", "array.get", "field.get", "add", "add", "const", "add" })]
    [InlineData(LoopHeader, new[] { "array.length", "conv.i4" })]
    [InlineData(Exit, new string[0])]
    public void SumBlocksHoldValueOperationsOnly(int ilOffset, string[] expected)
    {
        Assert.Equal(expected, BlockAt(Sum(), ilOffset).Instructions.Select(instruction => instruction.Op));
    }

    // SSA-LOCALS: no ldloc/stloc survives, in any block. Stated over the whole method so a
    // builder that promotes locals in straight-line code but falls back to memory operations
    // across the merge is still caught.
    [Fact]
    public void NoStackOrLocalOperationSurvivesInSum()
    {
        var ops = Sum().Blocks.SelectMany(block => block.Instructions).Select(instruction => instruction.Op).ToList();

        Assert.DoesNotContain(ops, op => op.StartsWith("ldloc", StringComparison.Ordinal));
        Assert.DoesNotContain(ops, op => op.StartsWith("stloc", StringComparison.Ordinal));
        Assert.DoesNotContain(ops, op => op.StartsWith("ldarg", StringComparison.Ordinal));
        Assert.DoesNotContain(ops, op => string.Equals(op, "dup", StringComparison.Ordinal));
        Assert.DoesNotContain(ops, op => string.Equals(op, "pop", StringComparison.Ordinal));
    }

    // SSA-BLOCKARGS: the merge is expressed by block parameters, never by a phi instruction.
    [Fact]
    public void SumHasNoPhiInstruction()
    {
        Assert.DoesNotContain(
            Sum().Blocks.SelectMany(block => block.Instructions),
            instruction => instruction.Op.Contains("phi", StringComparison.Ordinal));
    }

    // SSA-SUM-MERGE, the completion condition: IL_001e is the only block with two
    // predecessors, and it is the only block that takes parameters. A builder that leaves the
    // loop-carried values implicit, or that hands parameters to every block, fails here.
    [Fact]
    public void OnlyTheLoopHeaderTakesBlockParameters()
    {
        var sum = Sum();

        Assert.Equal(2, EdgesInto(sum, LoopHeader).Count);
        Assert.Equal(2, BlockAt(sum, LoopHeader).Parameters.Count);

        foreach (var ilOffset in new[] { Entry, LoopBody, Exit })
        {
            Assert.Empty(BlockAt(sum, ilOffset).Parameters);
        }
    }

    // SSA-SUM-MERGE: the two loop-carried locals are int32 values, not an opaque slot count.
    [Fact]
    public void TheLoopHeaderParametersAreTheTwoInt32Locals()
    {
        Assert.All(
            BlockAt(Sum(), LoopHeader).Parameters,
            parameter => Assert.Equal(SpikeSsaTypeKind.I32, parameter.Type.Kind));
    }

    // SSA-SUM-MERGE: both edges into the merge carry arguments, and they carry different
    // values - the entry passes the two initialisers, the loop body passes the accumulated
    // sum and the incremented counter. Equal arguments on both edges would mean the loop
    // never fed its result back.
    [Fact]
    public void BothEdgesIntoTheLoopHeaderPassTheirOwnValues()
    {
        var sum = Sum();
        var body = BlockAt(sum, LoopBody);

        var fromEntry = Assert.Single(EdgesInto(sum, LoopHeader),
            entry => entry.From.IlOffset == Entry).Edge;
        var fromBody = Assert.Single(EdgesInto(sum, LoopHeader),
            entry => entry.From.IlOffset == LoopBody).Edge;

        Assert.Equal(2, fromEntry.Arguments.Count);
        Assert.Equal(2, fromBody.Arguments.Count);

        // The loop body's last two "add" results, in slot order: sum then counter.
        var adds = body.Instructions
            .Where(instruction => string.Equals(instruction.Op, "add", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, adds.Count);
        Assert.Equal(
            new SpikeSsaValue?[] { adds[1].Result, adds[2].Result },
            fromBody.Arguments.Cast<SpikeSsaValue?>());

        Assert.NotEqual(fromEntry.Arguments, fromBody.Arguments);
    }

    // SSA-SUM-MERGE: a merge target has parameters, a single-predecessor target does not, so
    // the edges leaving the merge carry nothing. Their successors are the loop body when the
    // comparison holds and the exit when it does not.
    [Fact]
    public void TheLoopHeaderEndsInATwoWayBranchThatPassesNoArguments()
    {
        var terminator = BlockAt(Sum(), LoopHeader).Terminator;

        Assert.Equal(SpikeSsaTerminatorKind.CondBranch, terminator.Kind);
        Assert.Equal("lt", terminator.Condition);
        Assert.Equal(new[] { LoopBody, Exit }, terminator.Successors.Select(edge => edge.TargetIlOffset));
        Assert.All(terminator.Successors, edge => Assert.Empty(edge.Arguments));
    }

    // SSA-VALUES: the comparison is between the loop counter, which arrives as the merge's
    // second parameter, and the converted array length computed in the same block. Asserting
    // the operand identities is what distinguishes a real value graph from a list of ops.
    [Fact]
    public void TheLoopConditionComparesTheCounterParameterWithTheArrayLength()
    {
        var header = BlockAt(Sum(), LoopHeader);
        var convert = Assert.Single(header.Instructions,
            instruction => string.Equals(instruction.Op, "conv.i4", StringComparison.Ordinal));

        Assert.Equal(
            new SpikeSsaValue?[] { header.Parameters[1], convert.Result },
            header.Terminator.Operands.Cast<SpikeSsaValue?>());
    }

    // SSA-LOCALS: the value the exit returns is the merge's first parameter, which is the
    // only way the accumulated sum can reach IL_0024 once locals are values.
    [Fact]
    public void TheExitReturnsTheAccumulatorParameterOfTheMerge()
    {
        var sum = Sum();
        var terminator = BlockAt(sum, Exit).Terminator;

        Assert.Equal(SpikeSsaTerminatorKind.Return, terminator.Kind);
        var returned = Assert.Single(terminator.Operands);
        Assert.Equal(BlockAt(sum, LoopHeader).Parameters[0], returned);
    }

    // SSA-VALUES: the array argument is defined once, in the entry block, and both the
    // element loads and the length read refer to that one value.
    [Fact]
    public void TheArrayArgumentIsDefinedOnceAndReusedAcrossBlocks()
    {
        var sum = Sum();
        var argument = Assert.Single(BlockAt(sum, Entry).Instructions,
            instruction => string.Equals(instruction.Op, "arg", StringComparison.Ordinal));

        var length = Assert.Single(BlockAt(sum, LoopHeader).Instructions,
            instruction => string.Equals(instruction.Op, "array.length", StringComparison.Ordinal));
        Assert.Equal(argument.Result, Assert.Single(length.Operands));

        var elementLoads = BlockAt(sum, LoopBody).Instructions
            .Where(instruction => string.Equals(instruction.Op, "array.get", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, elementLoads.Count);
        Assert.All(elementLoads, load => Assert.Equal(argument.Result, load.Operands[0]));
    }

    // SSA-TYPES: Point[] maps onto array<ref<Point>> and nothing wider.
    [Fact]
    public void TheArrayArgumentUsesTheMinimalArrayAndReferenceTypes()
    {
        var argument = Assert.Single(BlockAt(Sum(), Entry).Instructions,
            instruction => string.Equals(instruction.Op, "arg", StringComparison.Ordinal));

        Assert.NotNull(argument.Result);
        var type = argument.Result!.Type;
        Assert.Equal(SpikeSsaTypeKind.Array, type.Kind);
        Assert.NotNull(type.ElementType);
        Assert.Equal(SpikeSsaTypeKind.Ref, type.ElementType!.Kind);
        Assert.Equal("CsWasm.Samples.Poc.Point", type.ElementType!.RefTypeName);
    }

    // SSA-VALUES: the two field reads name different fields. Without this, a builder that
    // reads X twice produces the same shape and the same op sequence.
    [Fact]
    public void TheTwoFieldReadsNameXAndY()
    {
        var reads = BlockAt(Sum(), LoopBody).Instructions
            .Where(instruction => string.Equals(instruction.Op, "field.get", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, reads.Count);
        Assert.Contains("CsWasm.Samples.Poc.Point::X", reads[0].Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("CsWasm.Samples.Poc.Point::Y", reads[1].Detail ?? string.Empty, StringComparison.Ordinal);
    }

    // SSA-TYPES: the six types the issue names, and no seventh. The enum is the whole type
    // family, so growing it to carry string, char, float32 or native int fails here.
    [Fact]
    public void TheTypeFamilyIsExactlyTheSixTheIssueNames()
    {
        Assert.Equal(
            new[] { "Array", "Bool", "F64", "I32", "I64", "Ref" },
            Enum.GetNames<SpikeSsaTypeKind>().OrderBy(name => name, StringComparer.Ordinal));
    }

    // SSA-CSW1003: taking a local's address refuses, under its own code, with an IL location
    // (docs/diagnostics.md rule 2). CompareLocal is the only member that emits ldloca.s.
    [Fact]
    public void TakingTheAddressOfALocalIsRefusedUnderCsw1003()
    {
        CilAssemblyReader.Read(SamplePaths.Unsupported, out var assembly);
        var diagnostics = SpikeSsaBuilder.Build(assembly, out _);

        var refusal = Assert.Single(diagnostics,
            diagnostic => string.Equals(diagnostic.Code.Id, "CSW1003", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal(DiagnosticCategory.Frontend, refusal.Code.Category);
        Assert.Contains("CompareLocal", refusal.Location ?? string.Empty, StringComparison.Ordinal);
    }

    // SSA-CSW1003: a refused method is left out rather than emitted half-normalised
    // (docs/diagnostics.md rule 1).
    [Fact]
    public void ARefusedMethodIsNotPresentInTheModel()
    {
        CilAssemblyReader.Read(SamplePaths.Unsupported, out var assembly);
        SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.DoesNotContain(
            ssa.Types.SelectMany(type => type.Methods),
            method => string.Equals(method.Name, "CompareLocal", StringComparison.Ordinal));
    }

    // SSA-STACKHEIGHT and SSA-STACKARGS: a merge reached with one value already on the
    // evaluation stack. No local is involved, so the parameter can only come from the entry
    // stack height; a builder that only promotes locals produces a parameterless block here
    // and cannot say which of the two constants the return sees.
    private static SpikeSsaMethod ConditionalValue()
    {
        // IL_0000: ldarg.0
        // IL_0001: brtrue.s IL_0006
        // IL_0003: ldc.i4.2
        // IL_0004: br.s     IL_0007
        // IL_0006: ldc.i4.1
        // IL_0007: ret
        var body = new MethodBodyModel(
            2,
            [],
            [
                new IlInstruction(0x00, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(0x01, "brtrue.s", "IL_0006", IntOperand: 0x06),
                new IlInstruction(0x03, "ldc.i4.2", null, IntOperand: 2),
                new IlInstruction(0x04, "br.s", "IL_0007", IntOperand: 0x07),
                new IlInstruction(0x06, "ldc.i4.1", null, IntOperand: 1),
                new IlInstruction(0x07, "ret", null),
            ],
            []);

        var assembly = new AssemblyModel(
            "HandBuilt",
            [
                new TypeModel(
                    "HandBuilt.Shapes",
                    "System.Object",
                    [],
                    [new MethodModel("Choose", true, "int32", ["int32"], body)]),
            ]);

        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);
        Assert.True(
            diagnostics.Count == 0,
            "A conditional expression must normalise, but the builder reported:\n"
            + string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Format())));

        return Method(ssa, "HandBuilt.Shapes", "Choose");
    }

    [Fact]
    public void AMergeReachedWithAValueOnTheStackIsSplitIntoFourBlocks()
    {
        Assert.Equal(
            new[] { 0x00, 0x03, 0x06, 0x07 },
            ConditionalValue().Blocks.Select(block => block.IlOffset));
    }

    // SSA-STACKHEIGHT: the entry height of IL_0007 is one, and only that block has it.
    [Fact]
    public void OnlyTheMergeWithANonEmptyEntryStackTakesAParameter()
    {
        var method = ConditionalValue();

        var parameter = Assert.Single(BlockAt(method, 0x07).Parameters);
        Assert.Equal(SpikeSsaTypeKind.I32, parameter.Type.Kind);

        foreach (var ilOffset in new[] { 0x00, 0x03, 0x06 })
        {
            Assert.Empty(BlockAt(method, ilOffset).Parameters);
        }
    }

    // SSA-STACKARGS: each arm passes its own constant along its edge, and the merge returns
    // the parameter rather than either constant directly.
    [Fact]
    public void EachArmPassesItsOwnStackSlotAlongItsEdge()
    {
        var method = ConditionalValue();
        var merge = BlockAt(method, 0x07);

        var fromTwo = Assert.Single(EdgesInto(method, 0x07),
            entry => entry.From.IlOffset == 0x03);
        var fromOne = Assert.Single(EdgesInto(method, 0x07),
            entry => entry.From.IlOffset == 0x06);

        Assert.Equal(
            Assert.Single(fromTwo.From.Instructions).Result,
            Assert.Single(fromTwo.Edge.Arguments));
        Assert.Equal(
            Assert.Single(fromOne.From.Instructions).Result,
            Assert.Single(fromOne.Edge.Arguments));
        Assert.NotEqual(fromTwo.Edge.Arguments, fromOne.Edge.Arguments);

        Assert.Equal(merge.Parameters[0], Assert.Single(merge.Terminator.Operands));
    }

    // SSA-CSW1004: two predecessors that disagree about how deep the stack is cannot be given
    // a parameter list. Refusing is the only alternative to guessing (docs/diagnostics.md
    // rule 1).
    [Fact]
    public void AMergeWithDisagreeingStackHeightsIsRefusedUnderCsw1004()
    {
        // IL_0000: ldarg.0            ; height 1
        // IL_0001: brtrue.s IL_0006   ; pops, reaches IL_0006 at height 0
        // IL_0003: ldc.i4.1           ; height 1
        // IL_0004: br.s     IL_0006   ; reaches IL_0006 at height 1
        // IL_0006: ret
        var body = new MethodBodyModel(
            2,
            [],
            [
                new IlInstruction(0x00, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(0x01, "brtrue.s", "IL_0006", IntOperand: 0x06),
                new IlInstruction(0x03, "ldc.i4.1", null, IntOperand: 1),
                new IlInstruction(0x04, "br.s", "IL_0006", IntOperand: 0x06),
                new IlInstruction(0x06, "ret", null),
            ],
            []);

        var assembly = new AssemblyModel(
            "HandBuilt",
            [
                new TypeModel(
                    "HandBuilt.Shapes",
                    "System.Object",
                    [],
                    [new MethodModel("Mismatched", true, "int32", ["int32"], body)]),
            ]);

        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        // How many reports one unnormalisable body produces is not a contract; that it is
        // refused, under CSW1004, at an IL location, and left out of the model, is.
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("CSW1004", diagnostic.Code.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        });
        Assert.Contains(
            diagnostics,
            diagnostic => (diagnostic.Location ?? string.Empty).Contains("Mismatched", StringComparison.Ordinal));
        Assert.DoesNotContain(
            ssa.Types.SelectMany(type => type.Methods),
            method => string.Equals(method.Name, "Mismatched", StringComparison.Ordinal));
    }

    // SSA-TYPES and SSA-CSW1004: float32 is outside the six-type family. Refusing it is what
    // keeps the family from being widened by whatever the next sample happens to use, and
    // what stops an unknown type being rounded off to a reference.
    [Fact]
    public void ATypeOutsideTheMinimalFamilyIsRefusedUnderCsw1004()
    {
        var body = new MethodBodyModel(
            1,
            [],
            [
                new IlInstruction(0x00, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(0x01, "ret", null),
            ],
            []);

        var assembly = new AssemblyModel(
            "HandBuilt",
            [
                new TypeModel(
                    "HandBuilt.Shapes",
                    "System.Object",
                    [],
                    [new MethodModel("Identity", true, "float32", ["float32"], body)]),
            ]);

        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CSW1004", diagnostic.Code.Id));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Message.Contains("float32", StringComparison.Ordinal));
        Assert.DoesNotContain(
            ssa.Types.SelectMany(type => type.Methods),
            method => string.Equals(method.Name, "Identity", StringComparison.Ordinal));
    }

    private static AssemblyModel HandBuilt(MethodModel method) =>
        new("HandBuilt", [new TypeModel("HandBuilt.Shapes", "System.Object", [], [method])]);

    // SSA-VALUES: the operations the proof-of-concept never reaches. Sum uses none of
    // new.array, new.object, field.set or array.set, so without this the code that turns them
    // into values is never observed and could be refusing or mis-popping them.
    [Fact]
    public void TheValueOperationsSumDoesNotReachAreNormalisedToo()
    {
        // IL_0000: ldc.i4.1
        // IL_0001: newarr   HandBuilt.Point
        // IL_0006: stloc.0
        // IL_0007: newobj   instance void HandBuilt.Point::.ctor()
        // IL_000c: stloc.1
        // IL_000d: ldloc.1
        // IL_000e: ldarg.0
        // IL_000f: stfld    int32 HandBuilt.Point::X
        // IL_0014: ldloc.0
        // IL_0015: ldc.i4.0
        // IL_0016: ldloc.1
        // IL_0017: stelem.ref
        // IL_0018: ldloc.0
        // IL_0019: ret
        var body = new MethodBodyModel(
            3,
            [new LocalModel(0, "HandBuilt.Point[]"), new LocalModel(1, "HandBuilt.Point")],
            [
                new IlInstruction(0x00, "ldc.i4.1", null, IntOperand: 1),
                new IlInstruction(0x01, "newarr", "HandBuilt.Point", TypeOperand: "HandBuilt.Point"),
                new IlInstruction(0x06, "stloc.0", null, IntOperand: 0),
                new IlInstruction(
                    0x07,
                    "newobj",
                    "instance void HandBuilt.Point::.ctor()",
                    CallOperand: new IlCallOperand("HandBuilt.Point::.ctor", 0, HasThis: true, ReturnsVoid: true),
                    TypeOperand: "HandBuilt.Point"),
                new IlInstruction(0x0c, "stloc.1", null, IntOperand: 1),
                new IlInstruction(0x0d, "ldloc.1", null, IntOperand: 1),
                new IlInstruction(0x0e, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(0x0f, "stfld", "int32 HandBuilt.Point::X"),
                new IlInstruction(0x14, "ldloc.0", null, IntOperand: 0),
                new IlInstruction(0x15, "ldc.i4.0", null, IntOperand: 0),
                new IlInstruction(0x16, "ldloc.1", null, IntOperand: 1),
                new IlInstruction(0x17, "stelem.ref", null),
                new IlInstruction(0x18, "ldloc.0", null, IntOperand: 0),
                new IlInstruction(0x19, "ret", null),
            ],
            []);

        var assembly = HandBuilt(new MethodModel("Make", true, "HandBuilt.Point[]", ["int32"], body));
        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.True(
            diagnostics.Count == 0,
            string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Format())));

        var block = Assert.Single(Method(ssa, "HandBuilt.Shapes", "Make").Blocks);

        // The two "const" ops that open the block give the locals their initial value.
        Assert.Equal(
            new[] { "arg", "const", "const", "const", "new.array", "new.object", "field.set", "const", "array.set" },
            block.Instructions.Select(instruction => instruction.Op));

        var allocation = Assert.Single(block.Instructions,
            instruction => string.Equals(instruction.Op, "new.object", StringComparison.Ordinal));
        Assert.NotNull(allocation.Result);
        Assert.Equal(SpikeSsaTypeKind.Ref, allocation.Result!.Type.Kind);

        // array.set pops the array, the index and the value; field.set pops the object and the
        // value. A miscounted pop shows up as the wrong operand count here.
        Assert.Equal(
            3,
            Assert.Single(block.Instructions,
                instruction => string.Equals(instruction.Op, "array.set", StringComparison.Ordinal))
                .Operands.Count);
        Assert.Equal(
            2,
            Assert.Single(block.Instructions,
                instruction => string.Equals(instruction.Op, "field.set", StringComparison.Ordinal))
                .Operands.Count);
    }

    // SSA-VALUES: a call that returns something defines a value of the return type, where the
    // void call in Point's constructor defines none.
    [Fact]
    public void ACallThatReturnsAValueDefinesOne()
    {
        var body = new MethodBodyModel(
            1,
            [],
            [
                new IlInstruction(0x00, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(
                    0x01,
                    "call",
                    "int32 HandBuilt.Shapes::Identity(int32)",
                    CallOperand: new IlCallOperand(
                        "HandBuilt.Shapes::Identity",
                        1,
                        HasThis: false,
                        ReturnsVoid: false),
                    TypeOperand: "int32"),
                new IlInstruction(0x06, "ret", null),
            ],
            []);

        var assembly = HandBuilt(new MethodModel("Forward", true, "int32", ["int32"], body));
        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.True(
            diagnostics.Count == 0,
            string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Format())));

        var block = Assert.Single(Method(ssa, "HandBuilt.Shapes", "Forward").Blocks);
        var call = Assert.Single(block.Instructions,
            instruction => string.Equals(instruction.Op, "call", StringComparison.Ordinal));

        Assert.NotNull(call.Result);
        Assert.Equal(SpikeSsaTypeKind.I32, call.Result!.Type.Kind);
        Assert.Equal(call.Result, Assert.Single(block.Terminator.Operands));
    }

    // SSA-CSW1004: an instruction with no value form is reported, never shrugged off. mul is
    // inside no opcode set this step normalises, and dropping it would leave the SSA
    // describing a program that multiplies nothing.
    [Fact]
    public void AnInstructionWithNoValueFormIsRefusedUnderCsw1004()
    {
        var body = new MethodBodyModel(
            2,
            [],
            [
                new IlInstruction(0x00, "ldarg.0", null, IntOperand: 0),
                new IlInstruction(0x01, "ldarg.1", null, IntOperand: 1),
                new IlInstruction(0x02, "mul", null),
                new IlInstruction(0x03, "ret", null),
            ],
            []);

        var assembly = HandBuilt(new MethodModel("Multiply", true, "int32", ["int32", "int32"], body));
        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.All(diagnostics, diagnostic => Assert.Equal("CSW1004", diagnostic.Code.Id));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Message.Contains("mul", StringComparison.Ordinal));
        Assert.Empty(TypeOf(ssa, "HandBuilt.Shapes").Methods);
    }

    // SSA-CSW1004: a branch that names an offset no instruction starts at makes every block
    // boundary after it a guess, so the body is refused rather than split somewhere plausible.
    [Fact]
    public void ABranchIntoNoInstructionIsRefusedUnderCsw1004()
    {
        var body = new MethodBodyModel(
            1,
            [],
            [
                // IL_0003 is inside the ret, not the start of an instruction.
                new IlInstruction(0x00, "br.s", "IL_0003", IntOperand: 0x03),
                new IlInstruction(0x02, "ret", null),
            ],
            []);

        var assembly = HandBuilt(new MethodModel("Astray", true, "void", [], body));
        var diagnostics = SpikeSsaBuilder.Build(assembly, out var ssa);

        Assert.All(diagnostics, diagnostic => Assert.Equal("CSW1004", diagnostic.Code.Id));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Message.Contains("IL_0003", StringComparison.Ordinal));
        Assert.Empty(TypeOf(ssa, "HandBuilt.Shapes").Methods);
    }

    // A method definition with no IL contributes no blocks and no diagnostic: there is
    // nothing to normalise, which is not the same as a body that cannot be normalised.
    [Fact]
    public void AMethodWithoutABodyProducesNoDiagnostic()
    {
        var assembly = new AssemblyModel(
            "HandBuilt",
            [
                new TypeModel(
                    "HandBuilt.Shapes",
                    "System.Object",
                    [],
                    [new MethodModel("Abstract", false, "int32", [], null)]),
            ]);

        Assert.Empty(SpikeSsaBuilder.Build(assembly, out _));
    }
}
