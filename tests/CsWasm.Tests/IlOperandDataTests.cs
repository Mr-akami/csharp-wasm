using CsWasm.Frontend.Cil;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract SSA-OPERAND-DATA (requirement 17): the branch target, the variable slot, the
/// integer constant and the callee's arity are carried as data on <see cref="IlInstruction"/>.
/// </summary>
/// <remarks>
/// <see cref="IlInstruction.Operand"/> is documented as already rendered for display, so a
/// consumer that needs a number has to be given one. These assertions pin the structured
/// fields and, alongside them, that the display string is unchanged - the dump's format is a
/// contract this step does not touch (contract KEEP-DUMPIL).
/// </remarks>
public sealed class IlOperandDataTests
{
    private static MethodBodyModel Body(string assemblyPath, string typeFullName, string methodName)
    {
        CilAssemblyReader.Read(assemblyPath, out var assembly);
        var method = CilAssemblyReaderTests.FindMethod(assembly, typeFullName, methodName);
        Assert.NotNull(method.Body);
        return method.Body!;
    }

    private static MethodBodyModel SumBody() => Body(SamplePaths.Poc, "CsWasm.Samples.Poc.Sample", "Sum");

    private static IlInstruction At(MethodBodyModel body, int offset)
    {
        var instruction = body.Instructions.SingleOrDefault(candidate => candidate.Offset == offset);
        Assert.True(
            instruction is not null,
            $"No instruction at IL_{offset:x4}. Offsets decoded: "
            + string.Join(", ", body.Instructions.Select(candidate => $"IL_{candidate.Offset:x4}")));
        return instruction!;
    }

    // Two branches with different deltas and different encodings of the same short form, so a
    // decoder that stored the delta rather than the target fails on at least one of them.
    [Fact]
    public void BranchTargetIsCarriedAsAnAbsoluteOffset()
    {
        var sum = SumBody();

        Assert.Equal(0x1e, At(sum, 0x04).IntOperand);
        Assert.Equal(0x06, At(sum, 0x22).IntOperand);
    }

    // The display string stays what dump il prints; the structured field is what a consumer
    // reads. Both must be present, so neither replaces the other.
    [Fact]
    public void BranchStillRendersTheDisplayLabel()
    {
        var sum = SumBody();

        Assert.Equal("IL_001e", At(sum, 0x04).Operand);
        Assert.Equal("IL_0006", At(sum, 0x22).Operand);
    }

    // Slot 0 and slot 1 in both directions: a decoder that returns a constant, or that only
    // fills the explicit-operand forms and leaves the macro forms null, fails here.
    [Theory]
    [InlineData(0x01, "stloc.0", 0)]
    [InlineData(0x03, "stloc.1", 1)]
    [InlineData(0x06, "ldloc.0", 0)]
    [InlineData(0x1a, "ldloc.1", 1)]
    [InlineData(0x07, "ldarg.0", 0)]
    public void VariableSlotIsCarriedAsANumber(int offset, string opCodeName, int expectedSlot)
    {
        var instruction = At(SumBody(), offset);

        Assert.Equal(opCodeName, instruction.OpCodeName);
        Assert.Equal(expectedSlot, instruction.IntOperand);
    }

    // Two different constants, both in macro form, so the value cannot come from the opcode
    // being recognised as "some ldc.i4".
    [Theory]
    [InlineData(0x00, "ldc.i4.0", 0)]
    [InlineData(0x1b, "ldc.i4.1", 1)]
    public void IntegerConstantIsCarriedAsANumber(int offset, string opCodeName, int expectedValue)
    {
        var instruction = At(SumBody(), offset);

        Assert.Equal(opCodeName, instruction.OpCodeName);
        Assert.Equal(expectedValue, instruction.IntOperand);
    }

    // An instruction with no operand of its own must not be given an invented one.
    [Theory]
    [InlineData(0x17, "add")]
    [InlineData(0x20, "ldlen")]
    [InlineData(0x25, "ret")]
    public void InstructionsWithoutAnOperandCarryNoNumber(int offset, string opCodeName)
    {
        var instruction = At(SumBody(), offset);

        Assert.Equal(opCodeName, instruction.OpCodeName);
        Assert.Null(instruction.IntOperand);
    }

    // A parameterless instance call: the pops are "this" alone.
    [Fact]
    public void ParameterlessInstanceCallReportsItsArity()
    {
        var call = At(Body(SamplePaths.Poc, "CsWasm.Samples.Poc.Point", ".ctor"), 0x01);

        Assert.Equal("call", call.OpCodeName);
        Assert.NotNull(call.CallOperand);
        Assert.Equal(0, call.CallOperand!.ArgumentCount);
        Assert.True(call.CallOperand!.HasThis);
        Assert.True(call.CallOperand!.ReturnsVoid);
    }

    // A one-argument instance call that returns a value: every field of the call operand
    // differs from the parameterless void case above, so a hard-coded record fails one of them.
    // Read from the model because the method it lives in never survives a dump.
    [Fact]
    public void InstanceCallWithAnArgumentReportsItsArity()
    {
        var body = Body(
            SamplePaths.Unsupported,
            "CsWasm.Samples.Unsupported.UnsupportedShapes",
            "CompareLocal");

        var call = Assert.Single(body.Instructions,
            instruction => string.Equals(instruction.OpCodeName, "call", StringComparison.Ordinal));

        Assert.NotNull(call.CallOperand);
        Assert.Equal(1, call.CallOperand!.ArgumentCount);
        Assert.True(call.CallOperand!.HasThis);
        Assert.False(call.CallOperand!.ReturnsVoid);
    }

    // The arity belongs to calls only; a field token must not be given one.
    [Fact]
    public void FieldAccessCarriesNoCallOperand()
    {
        var ldfld = At(SumBody(), 0x0a);

        Assert.Equal("ldfld", ldfld.OpCodeName);
        Assert.Null(ldfld.CallOperand);
    }
}
