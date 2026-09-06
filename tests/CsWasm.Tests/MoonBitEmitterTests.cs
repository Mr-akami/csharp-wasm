using CsWasm.Backend.MoonBit.Emitter;
using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil.Ssa;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts MB-EMIT-INSTR and MB-CSW4001 on the operations and the control-flow shapes the
/// proof-of-concept assembly does not contain.
/// </summary>
/// <remarks>
/// <c>Sum</c> reads fields and elements and never writes one, and its only merge is a loop, so
/// the command-level tests cannot observe the writing operations or the shapes the backend
/// refuses. The SSA is hand-built here for the same reason
/// <c>SpikeSsaBuilderTests</c> hand-builds CIL: the input needed does not exist as a sample.
/// </remarks>
public sealed class MoonBitEmitterTests
{
    private const string PointName = "HandBuilt.Point";

    private const string OwnerName = "HandBuilt.Shapes";

    private static readonly SpikeSsaType PointReference = SpikeSsaType.Reference(PointName);

    private static SpikeSsaAssembly Assembly(params SpikeSsaMethod[] methods) =>
        new(
            "HandBuilt",
            [
                new SpikeSsaTypeDefinition(
                    PointName,
                    [new SpikeSsaField("X", SpikeSsaType.Int32)],
                    []),
                new SpikeSsaTypeDefinition(OwnerName, [], methods),
            ]);

    private static IReadOnlyList<Diagnostic> Emit(SpikeSsaAssembly assembly, out string? source)
    {
        var diagnostics = MoonBitEmitter.Emit(assembly, out var module);
        source = module?.Source;
        return diagnostics;
    }

    /// <summary>
    /// The name the emitter gave the field <c>HandBuilt.Point::X</c>, read out of the struct it
    /// declared. MoonBit rejects a field name that starts with a capital, so the C# name cannot
    /// be carried over verbatim and the test does not write the generated spelling down.
    /// </summary>
    private static string FieldNameForX(string source)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            source,
            @"mut\s+([A-Za-z_][A-Za-z0-9_]*X[A-Za-z0-9_]*)\s*:");

        Assert.True(match.Success, "No field generated for X in:\n" + source);
        return match.Groups[1].Value;
    }

    private static string Refusal(SpikeSsaAssembly assembly)
    {
        var diagnostics = Emit(assembly, out var source);

        Assert.Null(source);

        var refusal = Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.BackendCannotLower);

        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        return refusal.Format();
    }

    // MB-EMIT-INSTR: the writing halves of the field and element operations. A lowering that
    // only handles the reading halves passes every test Sum drives and fails here.
    [Fact]
    public void WritingAFieldAndAnElementLowersToAssignments()
    {
        var array = new SpikeSsaValue(0, SpikeSsaType.ArrayOf(PointReference));
        var point = new SpikeSsaValue(1, PointReference);
        var index = new SpikeSsaValue(2, SpikeSsaType.Int32);

        var method = new SpikeSsaMethod(
            "Write",
            IsStatic: true,
            ReturnType: null,
            [
                new SpikeSsaBlock(
                    0x00,
                    [],
                    [
                        new SpikeSsaInstruction(array, "arg", [], "0"),
                        new SpikeSsaInstruction(point, "arg", [], "1"),
                        new SpikeSsaInstruction(index, "const", [], "0"),
                        new SpikeSsaInstruction(
                            null,
                            "field.set",
                            [point, index],
                            "int32 HandBuilt.Point::X",
                            new SpikeSsaMemberReference(PointName, "X")),
                        new SpikeSsaInstruction(null, "array.set", [array, index, point]),
                    ],
                    new SpikeSsaTerminator(SpikeSsaTerminatorKind.Return, null, [], [])),
            ]);

        var diagnostics = Emit(Assembly(method), out var source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(source);
        Assert.Contains("v1." + FieldNameForX(source) + " = v2", source, StringComparison.Ordinal);
        Assert.Contains("v0[v2] = v1", source, StringComparison.Ordinal);
    }

    // MB-CSW1005-EMIT: the same run reports the deviation for that method, because writing
    // through a reference is one of the operations C# checks and this step does not.
    [Fact]
    public void WritingThroughAReferenceIsReportedUnderCsw1005()
    {
        var array = new SpikeSsaValue(0, SpikeSsaType.ArrayOf(PointReference));
        var point = new SpikeSsaValue(1, PointReference);
        var index = new SpikeSsaValue(2, SpikeSsaType.Int32);

        var method = new SpikeSsaMethod(
            "Write",
            IsStatic: true,
            ReturnType: null,
            [
                new SpikeSsaBlock(
                    0x00,
                    [],
                    [
                        new SpikeSsaInstruction(array, "arg", [], "0"),
                        new SpikeSsaInstruction(point, "arg", [], "1"),
                        new SpikeSsaInstruction(index, "const", [], "0"),
                        new SpikeSsaInstruction(null, "array.set", [array, index, point]),
                    ],
                    new SpikeSsaTerminator(SpikeSsaTerminatorKind.Return, null, [], [])),
            ]);

        var warning = Assert.Single(
            Emit(Assembly(method), out _),
            diagnostic => diagnostic.Code == DiagnosticCode.ImplicitExceptionChecksNotInserted);

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Write", warning.Format(), StringComparison.Ordinal);
    }

    // MB-CSW4001: a merge that is not a loop is a control-flow shape issue #16 does not map,
    // and it is refused rather than written as something that drops the merged values.
    [Fact]
    public void AMergeThatIsNotALoopIsRefused()
    {
        var left = new SpikeSsaValue(0, SpikeSsaType.Int32);
        var right = new SpikeSsaValue(1, SpikeSsaType.Int32);
        var merged = new SpikeSsaValue(2, SpikeSsaType.Int32);

        var method = new SpikeSsaMethod(
            "Pick",
            IsStatic: true,
            ReturnType: SpikeSsaType.Int32,
            [
                new SpikeSsaBlock(
                    0x00,
                    [],
                    [
                        new SpikeSsaInstruction(left, "const", [], "0"),
                        new SpikeSsaInstruction(right, "const", [], "1"),
                    ],
                    new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.CondBranch,
                        "lt",
                        [left, right],
                        [new SpikeSsaEdge(0x05, [left]), new SpikeSsaEdge(0x0a, [right])])),
                new SpikeSsaBlock(
                    0x05,
                    [],
                    [],
                    new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.Branch,
                        null,
                        [],
                        [new SpikeSsaEdge(0x0f, [left])])),
                new SpikeSsaBlock(
                    0x0a,
                    [],
                    [],
                    new SpikeSsaTerminator(
                        SpikeSsaTerminatorKind.Branch,
                        null,
                        [],
                        [new SpikeSsaEdge(0x0f, [right])])),
                new SpikeSsaBlock(
                    0x0f,
                    [merged],
                    [],
                    new SpikeSsaTerminator(SpikeSsaTerminatorKind.Return, null, [merged], [])),
            ]);

        Assert.Contains("Pick", Refusal(Assembly(method)), StringComparison.Ordinal);
    }

    // MB-CSW4001: a field operation whose token carried no field cannot name the field it
    // touches. Normalisation accepts it (KEEP-SSA-ACCEPT), so the refusal belongs here.
    [Fact]
    public void AFieldOperationThatNamesNoFieldIsRefused()
    {
        var point = new SpikeSsaValue(0, PointReference);
        var read = new SpikeSsaValue(1, SpikeSsaType.Int32);

        var method = new SpikeSsaMethod(
            "Read",
            IsStatic: true,
            ReturnType: SpikeSsaType.Int32,
            [
                new SpikeSsaBlock(
                    0x00,
                    [],
                    [
                        new SpikeSsaInstruction(point, "arg", [], "0"),
                        new SpikeSsaInstruction(read, "field.get", [point], "int32 HandBuilt.Point::X"),
                    ],
                    new SpikeSsaTerminator(SpikeSsaTerminatorKind.Return, null, [read], [])),
            ]);

        Assert.Contains("field.get", Refusal(Assembly(method)), StringComparison.Ordinal);
    }

    // MB-CSW4001 and MB-NO-NULL-CHECK: the zero a reference local starts at has no literal in
    // this step, and inventing one would put a value in the source that C# never had.
    [Fact]
    public void TheInitialValueOfAReferenceLocalIsRefused()
    {
        var zero = new SpikeSsaValue(0, PointReference);

        var method = new SpikeSsaMethod(
            "Start",
            IsStatic: true,
            ReturnType: null,
            [
                new SpikeSsaBlock(
                    0x00,
                    [],
                    [new SpikeSsaInstruction(zero, "const", [], "null")],
                    new SpikeSsaTerminator(SpikeSsaTerminatorKind.Return, null, [], [])),
            ]);

        Assert.Contains("Start", Refusal(Assembly(method)), StringComparison.Ordinal);
    }

    // MB-CSW4001: a field whose declared type is outside the minimal family is listed by the
    // model with no type, and the struct that would hold it cannot be written down.
    [Fact]
    public void AFieldWithNoTypeIsRefused()
    {
        var assembly = new SpikeSsaAssembly(
            "HandBuilt",
            [new SpikeSsaTypeDefinition(PointName, [new SpikeSsaField("Label", null)], [])]);

        Assert.Contains("Label", Refusal(assembly), StringComparison.Ordinal);
    }
}
