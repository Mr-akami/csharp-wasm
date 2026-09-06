using CsWasm.Frontend.Cil;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract CIL-BODY (requirements 10 to 13): instruction stream with offsets, local variable
/// signature, max stack and exception regions, observed on the model so that methods the
/// supported-opcode gate rejects are still reachable.
/// </summary>
public sealed class MethodBodyDecodingTests
{
    private static MethodBodyModel Body(string assemblyPath, string typeFullName, string methodName)
    {
        CilAssemblyReader.Read(assemblyPath, out var assembly);
        var method = CilAssemblyReaderTests.FindMethod(assembly, typeFullName, methodName);
        Assert.NotNull(method.Body);
        return method.Body!;
    }

    private static MethodBodyModel SumBody() => Body(SamplePaths.Poc, "CsWasm.Samples.Poc.Sample", "Sum");

    // Requirement 10: every instruction carries the offset it starts at, and offsets advance by
    // the real encoded length. The anchors are instructions whose predecessors carry operands,
    // so a decoder that ignores operand widths lands on different offsets.
    [Fact]
    public void InstructionOffsetsFollowTheEncodedLength()
    {
        var instructions = SumBody().Instructions;

        Assert.Equal(0, instructions[0].Offset);
        Assert.Equal("ldc.i4.0", instructions[0].OpCodeName);

        Assert.Equal("ldfld", OpCodeAt(instructions, 0x0a));
        Assert.Equal("conv.i4", OpCodeAt(instructions, 0x21));

        var last = instructions[^1];
        Assert.Equal(0x25, last.Offset);
        Assert.Equal("ret", last.OpCodeName);
    }

    [Fact]
    public void InstructionOffsetsAreStrictlyIncreasing()
    {
        var offsets = SumBody().Instructions.Select(instruction => instruction.Offset).ToList();

        Assert.Equal(offsets.OrderBy(offset => offset).Distinct().ToList(), offsets);
    }

    // Requirement 10: a branch operand is the absolute target offset, not the encoded delta.
    [Fact]
    public void BranchOperandsNameTheAbsoluteTarget()
    {
        var instructions = SumBody().Instructions;

        Assert.Equal("IL_001e", OperandAt(instructions, 0x04));
        Assert.Equal("IL_0006", OperandAt(instructions, 0x22));
    }

    // Requirement 10: a field token operand is resolved to the member it names.
    [Fact]
    public void FieldOperandsAreResolvedToTheMember()
    {
        var instructions = SumBody().Instructions;

        Assert.Contains("CsWasm.Samples.Poc.Point::X", OperandAt(instructions, 0x0a), StringComparison.Ordinal);
        Assert.Contains("CsWasm.Samples.Poc.Point::Y", OperandAt(instructions, 0x12), StringComparison.Ordinal);
    }

    // Requirement 11.
    [Fact]
    public void LocalVariableSignatureIsDecoded()
    {
        var locals = SumBody().Locals;

        Assert.Equal(2, locals.Count);
        Assert.Equal(new[] { 0, 1 }, locals.Select(local => local.Index));
        Assert.All(locals, local => Assert.Equal("int32", local.TypeName));
    }

    [Fact]
    public void MethodWithoutLocalsHasNone()
    {
        var constructor = Body(SamplePaths.Poc, "CsWasm.Samples.Poc.Point", ".ctor");

        Assert.Empty(constructor.Locals);
    }

    // Requirement 12: the header value, not a constant. Two methods with different max stacks
    // are checked so a hard-coded value cannot satisfy both.
    [Fact]
    public void MaxStackComesFromTheMethodHeader()
    {
        Assert.Equal(4, SumBody().MaxStack);
        Assert.Equal(8, Body(SamplePaths.Poc, "CsWasm.Samples.Poc.Point", ".ctor").MaxStack);
    }

    // Requirement 13.
    [Fact]
    public void CatchRegionIsDecodedWithItsKindTypeAndExtent()
    {
        var guarded = Body(SamplePaths.Unsupported, "CsWasm.Samples.Unsupported.UnsupportedShapes", "Guarded");

        var region = Assert.Single(guarded.ExceptionRegions);

        Assert.Equal(ExceptionRegionKind.Catch, region.Kind);
        Assert.Equal("System.IndexOutOfRangeException", region.CatchTypeName);
        Assert.Equal(0, region.TryOffset);
        Assert.True(region.TryLength > 0, "the protected block must not be empty");
        Assert.Equal(region.TryOffset + region.TryLength, region.HandlerOffset);
        Assert.True(region.HandlerLength > 0, "the handler must not be empty");
    }

    // Requirement 13: the region bounds must line up with real instruction boundaries, which a
    // set of fixed offsets would not.
    [Fact]
    public void CatchRegionBoundsLandOnInstructionBoundaries()
    {
        var guarded = Body(SamplePaths.Unsupported, "CsWasm.Samples.Unsupported.UnsupportedShapes", "Guarded");
        var region = Assert.Single(guarded.ExceptionRegions);
        var offsets = guarded.Instructions.Select(instruction => instruction.Offset).ToList();

        Assert.Contains(region.TryOffset, offsets);
        Assert.Contains(region.HandlerOffset, offsets);
    }

    [Fact]
    public void MethodWithoutHandlersHasNoExceptionRegions()
    {
        Assert.Empty(SumBody().ExceptionRegions);
    }

    private static string OpCodeAt(IReadOnlyList<IlInstruction> instructions, int offset) =>
        InstructionAt(instructions, offset).OpCodeName;

    private static string OperandAt(IReadOnlyList<IlInstruction> instructions, int offset)
    {
        var operand = InstructionAt(instructions, offset).Operand;
        Assert.True(operand is not null, $"IL_{offset:x4} carries an operand but the model has none.");
        return operand!;
    }

    private static IlInstruction InstructionAt(IReadOnlyList<IlInstruction> instructions, int offset)
    {
        var instruction = instructions.SingleOrDefault(candidate => candidate.Offset == offset);
        Assert.True(
            instruction is not null,
            $"No instruction at IL_{offset:x4}. Offsets decoded: "
            + string.Join(", ", instructions.Select(candidate => $"IL_{candidate.Offset:x4}")));
        return instruction!;
    }
}
