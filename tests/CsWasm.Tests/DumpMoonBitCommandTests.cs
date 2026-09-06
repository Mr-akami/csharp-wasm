using CsWasm.Driver;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts MB-CMD, MB-CMD-ARGS, MB-SUM, MB-TRACEABLE, MB-EMIT-TYPE, MB-EMIT-FN,
/// MB-EMIT-INSTR, MB-EMIT-ARRAY, MB-EMIT-LOOP, MB-EMIT-LOCAL, MB-SNAPSHOT, MB-DETERMINISM,
/// MB-CSW1005-EMIT, MB-CSW1005-WARNING, MB-CSW4001, MB-NO-NULL-CHECK, MB-NO-BOUNDS-CHECK,
/// MB-NO-DIV-CHECK and KEEP-ALLORNOTHING, observed at the entry point issue #16 names:
/// <c>cswasm dump moonbit &lt;dll&gt;</c>.
/// </summary>
public sealed class DumpMoonBitCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// The name the struct declares for the field the C# one called <paramref name="cSharpName"/>.
    /// It is read out of the declaration rather than written down here, so that the reads can
    /// be checked against the declaration without this file owning the generated spelling.
    /// </summary>
    private static string DeclaredField(string output, string cSharpName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            output,
            @"mut\s+([A-Za-z_][A-Za-z0-9_]*" + cSharpName + @"[A-Za-z0-9_]*)\s*:");

        Assert.True(match.Success, $"No field declared for {cSharpName} in:\n{output}");
        return match.Groups[1].Value;
    }

    private static string EmitPoc()
    {
        var (code, output, error) = Run("dump", "moonbit", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);
        return output;
    }

    // MB-CMD: a command --help does not mention is not wired up, whatever exists behind it.
    [Fact]
    public void UsageDisclosesTheDumpMoonBitCommand()
    {
        var (code, output, _) = Run("--help");

        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.Contains("dump moonbit", output, StringComparison.Ordinal);
    }

    // MB-CMD: the proof-of-concept sample emits end to end. stderr is not required to be
    // empty here - CSW1005 belongs on it - but the run must succeed and print something.
    [Fact]
    public void DumpOfTheProofOfConceptSampleSucceeds()
    {
        var (code, output, error) = Run("dump", "moonbit", SamplePaths.Poc);

        Assert.True(code == CommandLine.ExitSuccess, error);
        Assert.NotEqual(string.Empty, output);
    }

    // MB-SUM: the completion condition of issue #16 is that Sum reaches the output.
    [Fact]
    public void EmittedSourceContainsAFunctionForSum()
    {
        var output = EmitPoc();

        Assert.Contains("Sum", output, StringComparison.Ordinal);
        Assert.Contains("fn ", output, StringComparison.Ordinal);
    }

    // MB-TRACEABLE: "a human can follow it back to the original C#" means the C# member the
    // function came from and the IL offsets of its blocks are both in the text. IL_001e is
    // the loop header the SSA dump already names (tests/CsWasm.Tests/Snapshots/dump-ssa-poc.txt).
    [Fact]
    public void EmittedSourceNamesTheOriginalCSharpMemberAndIlOffsets()
    {
        var output = EmitPoc();

        Assert.Contains("CsWasm.Samples.Poc.Sample::Sum", output, StringComparison.Ordinal);
        Assert.Contains("IL_001e", output, StringComparison.Ordinal);
    }

    // MB-EMIT-TYPE: a C# class becomes a struct with mutable fields, and the C# field name is
    // still readable in the generated one (issue #16 mapping table, completion condition).
    // MoonBit rejects a field name that starts with a capital, so Point's X and Y cannot be
    // carried over verbatim; what has to survive is that each field is mutable and still names
    // the C# field it came from (docs/moonbit-packaging.md).
    [Fact]
    public void EmittedSourceDeclaresPointAsAStructWithMutableFields()
    {
        var output = EmitPoc();

        Assert.Contains("struct", output, StringComparison.Ordinal);
        Assert.Matches(@"mut\s+[A-Za-z_][A-Za-z0-9_]*X[A-Za-z0-9_]*\s*:", output);
        Assert.Matches(@"mut\s+[A-Za-z_][A-Za-z0-9_]*Y[A-Za-z0-9_]*\s*:", output);
    }

    // MB-EMIT-ARRAY: T[] becomes Array[T], element type included. Losing the element type or
    // printing the raw IL spelling both fail here.
    [Fact]
    public void EmittedSourceSpellsTheArrayParameterAsAnArrayOfTheStruct()
    {
        var output = EmitPoc();

        Assert.Matches(@"Array\[\s*Cs_Point_[0-9a-f]+\s*\]", output);
    }

    // MB-EMIT-FN: Sum is a static C# method, so it becomes a plain fn with a parameter and a
    // return type. A body emitted without a signature fails here.
    [Fact]
    public void EmittedSourceGivesSumAFunctionSignatureWithAReturnType()
    {
        var output = EmitPoc();

        Assert.Matches(@"fn\s+__cs_Sum_[0-9a-f]+\s*\([^)]*Array\[[^\]]*\][^)]*\)\s*->\s*Int", output);
    }

    // MB-EMIT-LOOP: the mapping table says loops become `loop` with continuation arguments,
    // not an unrolled or rejected body.
    [Fact]
    public void EmittedSourceExpressesTheLoopAsALoop()
    {
        var output = EmitPoc();

        Assert.Contains("loop", output, StringComparison.Ordinal);
        Assert.Contains("continue", output, StringComparison.Ordinal);
    }

    // MB-EMIT-LOCAL: the mapping table allows either `let mut` or block arguments for locals.
    // The test holds the allowed set rather than picking one of the two for the implementer.
    [Fact]
    public void EmittedSourceCarriesLocalsAsLetMutOrAsLoopArguments()
    {
        var output = EmitPoc();

        var hasLetMut = output.Contains("let mut", StringComparison.Ordinal);
        var hasLoopArguments = System.Text.RegularExpressions.Regex.IsMatch(output, @"loop\s+[^\r\n{]+\{");

        Assert.True(
            hasLetMut || hasLoopArguments,
            "Sum's locals are carried by neither `let mut` nor loop arguments:\n" + output);
    }

    // MB-EMIT-INSTR: the operations Sum's SSA is made of - array.get, array.length, field.get,
    // add, lt - all reach the text. field.get in particular must read the fields the C# X and
    // Y became, through the same names the struct declares them under: MoonBit rejects a field
    // called X, so what has to hold is that the read and the declaration agree.
    [Fact]
    public void EmittedSourceLowersSumsOperations()
    {
        var output = EmitPoc();

        Assert.Contains("." + DeclaredField(output, "X"), output, StringComparison.Ordinal);
        Assert.Contains("." + DeclaredField(output, "Y"), output, StringComparison.Ordinal);
        Assert.Contains("length()", output, StringComparison.Ordinal);
        Assert.Contains("+", output, StringComparison.Ordinal);
        Assert.Contains("<", output, StringComparison.Ordinal);
    }

    // MB-NO-NULL-CHECK, MB-NO-BOUNDS-CHECK, MB-NO-DIV-CHECK: issue #16 decides that this step
    // inserts none of the three implicit checks and reports the deviation instead. A check
    // added "to be safe" would introduce the vocabulary below.
    [Theory]
    [InlineData("null")]
    [InlineData("abort")]
    [InlineData("panic")]
    [InlineData("NullReference")]
    [InlineData("IndexOutOfRange")]
    [InlineData("DivideByZero")]
    public void EmittedSourceInsertsNoImplicitExceptionChecks(string forbidden)
    {
        var output = EmitPoc();

        Assert.DoesNotContain(forbidden, output, StringComparison.OrdinalIgnoreCase);
    }

    // MB-CSW1005-EMIT: the deviation is reported per method that contains one of the
    // operations, so Sum is warned about and Point::.ctor - which has none - is not.
    [Fact]
    public void SumIsReportedUnderCsw1005AndTheConstructorIsNot()
    {
        var (_, _, error) = Run("dump", "moonbit", SamplePaths.Poc);

        var warned = error
            .Split('\n')
            .Where(line => line.Contains("CSW1005", StringComparison.Ordinal))
            .ToList();

        var single = Assert.Single(warned);
        Assert.Contains("Sum", single, StringComparison.Ordinal);
        Assert.DoesNotContain(".ctor", single, StringComparison.Ordinal);
    }

    // MB-CSW1005-WARNING and KEEP-ALLORNOTHING: CSW1005 is a warning, so it is reported as one
    // and the output still reaches stdout with a zero exit code. Treating it as an error would
    // empty stdout (docs/diagnostics.md rule 1) and never reach the completion condition.
    [Fact]
    public void Csw1005DoesNotSuppressTheOutput()
    {
        var (code, output, error) = Run("dump", "moonbit", SamplePaths.Poc);

        Assert.Contains("CSW1005", error, StringComparison.Ordinal);
        Assert.Contains("warning CSW1005", error, StringComparison.Ordinal);
        Assert.Equal(CommandLine.ExitSuccess, code);
        Assert.NotEqual(string.Empty, output);
    }

    // MB-CSW4001 and KEEP-ALLORNOTHING: an SSA construct the backend cannot lower is refused
    // under its own code, and no part of the assembly is printed.
    [Fact]
    public void AnSsaConstructTheBackendCannotLowerIsRefusedWithoutOutput()
    {
        var (code, output, error) = Run("dump", "moonbit", SamplePaths.BackendUnsupported);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("error CSW4001", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // MB-CSW4001: the refusal names the C# member it came from, not the generated MoonBit
    // (docs/diagnostics.md rule 2).
    [Fact]
    public void TheCsw4001RefusalNamesTheCSharpMember()
    {
        var (_, _, error) = Run("dump", "moonbit", SamplePaths.BackendUnsupported);

        Assert.Contains("Make", error, StringComparison.Ordinal);
    }

    // KEEP-ALLORNOTHING: a frontend refusal stops the backend as well, and stdout stays empty.
    [Fact]
    public void DumpFailsOnAnAddressTakenLocalWithoutEmittingOutput()
    {
        var (code, output, error) = Run("dump", "moonbit", SamplePaths.Unsupported);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW1003", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // KEEP-ALLORNOTHING: the input check the other dumps apply is the same one here.
    [Fact]
    public void DumpOfAMissingFileReportsInputNotFound()
    {
        Assert.False(File.Exists(SamplePaths.Missing));

        var (code, output, error) = Run("dump", "moonbit", SamplePaths.Missing);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0003", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // MB-CMD-ARGS: argument shapes the command does not accept are rejected by the parser
    // rather than throwing, exactly as for dump il and dump ssa.
    [Theory]
    [InlineData("dump", "moonbit")]
    [InlineData("dump", "moonbit", "Sample.dll", "extra")]
    public void MalformedDumpMoonBitInvocationsAreRejected(params string[] args)
    {
        var (code, output, error) = Run(args);

        Assert.Equal(CommandLine.ExitFailure, code);
        Assert.Contains("CSW0002", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    // MB-DETERMINISM: the line separator belongs to the format, not to the platform.
    [Fact]
    public void EmittedSourceUsesLineFeedsOnly()
    {
        var output = EmitPoc();

        Assert.DoesNotContain("\r", output, StringComparison.Ordinal);
    }

    // MB-DETERMINISM: two runs over the same file agree byte for byte, so nothing in the
    // emitter may depend on hash iteration order.
    [Fact]
    public void EmittingTheSameAssemblyTwiceProducesIdenticalBytes()
    {
        var first = EmitPoc();
        var second = EmitPoc();

        Assert.Equal(first, second);
    }

    // MB-SNAPSHOT. The snapshot is recorded once from an inspected run; until it exists this
    // fails with the output attached (tests/CsWasm.Tests/Snapshot.cs).
    [Fact]
    public void EmittedSourceMatchesTheSnapshot()
    {
        Snapshot.Matches("dump-moonbit-poc.txt", EmitPoc());
    }

    // KEEP-DUMP-IL and KEEP-DUMP-SSA at the command level: adding a third dump does not change
    // what the two existing ones print. Their own snapshot tests pin the text; this pins that
    // the new subcommand did not take their place in the parser.
    [Fact]
    public void TheExistingDumpSubcommandsStillProduceTheirOwnOutput()
    {
        var (ilCode, il, ilError) = Run("dump", "il", SamplePaths.Poc);
        var (ssaCode, ssa, ssaError) = Run("dump", "ssa", SamplePaths.Poc);

        Assert.True(ilCode == CommandLine.ExitSuccess, ilError);
        Assert.True(ssaCode == CommandLine.ExitSuccess, ssaError);
        Assert.Equal(string.Empty, ilError);
        Assert.Equal(string.Empty, ssaError);
        Assert.NotEqual(il, ssa);
    }
}
