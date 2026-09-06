using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contract CIL-CSW1001 (requirement 17): an opcode outside the supported set fails loudly
/// instead of being skipped. Observed on the model, separately from the dump command, because
/// the decision is the frontend's and not the driver's.
/// </summary>
public sealed class SupportedInstructionsTests
{
    private static IReadOnlyList<Diagnostic> Validate(string assemblyPath)
    {
        CilAssemblyReader.Read(assemblyPath, out var assembly);
        return SupportedInstructions.Validate(assembly);
    }

    // The proof-of-concept sample uses the opcode set named in the issue plus conv.i4 (emitted
    // for points.Length) and call (emitted by Point's implicit constructor). If either were
    // missing from the supported set, the completion condition could not hold.
    [Fact]
    public void ProofOfConceptSampleUsesOnlySupportedOpcodes()
    {
        Assert.Empty(Validate(SamplePaths.Poc));
    }

    // Every unsupported instruction is reported, not just the first one: Multiply contributes
    // mul, Guarded contributes pop and two leave.s. Supported instructions in the same methods
    // (ldarg.0, ldelem.i4, ldc.i4.m1, stloc.0, ldloc.0, ret) must not be reported.
    [Fact]
    public void EveryUnsupportedInstructionIsReportedExactlyOnce()
    {
        var diagnostics = Validate(SamplePaths.Unsupported);

        Assert.Equal(4, diagnostics.Count);
    }

    [Fact]
    public void UnsupportedInstructionsAreReportedAcrossAllMethods()
    {
        var messages = Validate(SamplePaths.Unsupported).Select(diagnostic => diagnostic.Message).ToList();

        Assert.Contains(messages, message => message.Contains("Multiply", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Guarded", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedInstructionDiagnosticNamesTheOpcode()
    {
        var messages = Validate(SamplePaths.Unsupported).Select(diagnostic => diagnostic.Message).ToList();

        Assert.Contains(messages, message => message.Contains("mul", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("pop", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("leave.s", StringComparison.Ordinal));
    }

    // A method that is entirely supported must not be dragged in by its unsupported siblings.
    [Fact]
    public void FullySupportedMethodIsNotReported()
    {
        var messages = Validate(SamplePaths.Unsupported).Select(diagnostic => diagnostic.Message);

        Assert.DoesNotContain(messages, message => message.Contains("Identity", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedInstructionsUseTheFrontendOpcodeCodeAsErrors()
    {
        var diagnostics = Validate(SamplePaths.Unsupported);

        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("CSW1001", diagnostic.Code.Id);
            Assert.Equal(DiagnosticCategory.Frontend, diagnostic.Code.Category);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        });
    }

    // docs/diagnostics.md rule 2: a frontend failure must point at an IL location, otherwise
    // "which instruction" is unobservable and the report cannot be acted on.
    [Fact]
    public void UnsupportedInstructionDiagnosticCarriesAnIlLocation()
    {
        var diagnostics = Validate(SamplePaths.Unsupported);

        Assert.All(diagnostics, diagnostic =>
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Location)));
    }

    // Unsupported metadata is the reader's decision (CSW1002); the opcode gate must not
    // re-report it under its own code.
    [Fact]
    public void ValidationDoesNotReportUnsupportedMetadata()
    {
        Assert.DoesNotContain("CSW1002", Validate(SamplePaths.Unsupported).Select(diagnostic => diagnostic.Code.Id));
    }
}
