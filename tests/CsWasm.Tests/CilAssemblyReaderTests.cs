using System.Runtime.Loader;
using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil;
using Xunit;

namespace CsWasm.Tests;

/// <summary>
/// Contracts CIL-SAMPLE-BUILD, CIL-NO-HOST-LOAD, CIL-MODEL and CIL-CSW1002:
/// what <see cref="CilAssemblyReader"/> observes in an assembly and how it reads it.
/// </summary>
public sealed class CilAssemblyReaderTests
{
    // CIL-SAMPLE-BUILD (requirement 20): the sample DLLs are produced by the solution build,
    // not committed, so every other contract here depends on them being generated.
    [Theory]
    [InlineData(SamplePaths.PocAssemblyName)]
    [InlineData(SamplePaths.UnsupportedAssemblyName)]
    public void SampleAssembliesAreBuiltNextToTheTests(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

        Assert.True(File.Exists(path), $"{assemblyName}.dll was not produced into {AppContext.BaseDirectory}.");
    }

    // CIL-NO-HOST-LOAD (requirements 3 and 4): reading metadata must not put the input
    // assembly into this process. Observed on the runtime's own load contexts rather than on
    // the frontend's source, so an Assembly.Load added later still fails the test.
    [Fact]
    public void ReadingAnAssemblyDoesNotLoadItIntoTheHostRuntime()
    {
        Assert.DoesNotContain(SamplePaths.PocAssemblyName, LoadedAssemblyNames());

        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);

        Assert.NotNull(assembly);
        Assert.DoesNotContain(SamplePaths.PocAssemblyName, LoadedAssemblyNames());
    }

    [Fact]
    public void ReadingReportsNoDiagnosticsForTheSupportedSample()
    {
        var diagnostics = CilAssemblyReader.Read(SamplePaths.Poc, out _);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AssemblyModelIsNamedAfterTheAssembly()
    {
        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);

        Assert.Equal(SamplePaths.PocAssemblyName, assembly.Name);
    }

    // CIL-MODEL (requirements 5, 6, 8, 9).
    [Fact]
    public void PointIsModelledWithItsBaseTypeAndFieldSignatures()
    {
        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);

        var point = FindType(assembly, "CsWasm.Samples.Poc.Point");

        Assert.Equal("System.Object", point.BaseTypeName);
        Assert.Equal(new[] { "X", "Y" }, point.Fields.Select(field => field.Name));
        Assert.All(point.Fields, field => Assert.Equal("int32", field.TypeName));
    }

    // CIL-MODEL (requirements 7 and 8).
    [Fact]
    public void SumIsModelledWithItsFullSignature()
    {
        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);

        var sum = FindMethod(assembly, "CsWasm.Samples.Poc.Sample", "Sum");

        Assert.True(sum.IsStatic);
        Assert.Equal("int32", sum.ReturnTypeName);
        Assert.Equal(new[] { "CsWasm.Samples.Poc.Point[]" }, sum.ParameterTypeNames);
    }

    // CIL-MODEL: an instance method must be distinguishable from a static one, otherwise the
    // static flag could be hard-coded and still satisfy the test above.
    [Fact]
    public void InstanceConstructorIsNotReportedAsStatic()
    {
        CilAssemblyReader.Read(SamplePaths.Poc, out var assembly);

        var constructor = FindMethod(assembly, "CsWasm.Samples.Poc.Point", ".ctor");

        Assert.False(constructor.IsStatic);
        Assert.Empty(constructor.ParameterTypeNames);
    }

    // CIL-CSW1002 (requirement 18): unsupported metadata is reported by the reader, names the
    // member that caused it, and does not borrow the unsupported-opcode code.
    [Fact]
    public void GenericMethodDefinitionIsReportedAsUnsupportedMetadata()
    {
        var diagnostics = CilAssemblyReader.Read(SamplePaths.Unsupported, out _);

        var unsupportedMetadata = Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Message.Contains("Identity", StringComparison.Ordinal));

        Assert.Equal("CSW1002", unsupportedMetadata.Code.Id);
        Assert.Equal(DiagnosticSeverity.Error, unsupportedMetadata.Severity);
    }

    // CIL-CSW1001 / CIL-CSW1002 own different failures. The reader classifies metadata only;
    // opcodes belong to SupportedInstructions.
    [Fact]
    public void ReaderDoesNotReportUnsupportedOpcodes()
    {
        var diagnostics = CilAssemblyReader.Read(SamplePaths.Unsupported, out _);

        Assert.DoesNotContain("CSW1001", diagnostics.Select(diagnostic => diagnostic.Code.Id));
    }

    internal static TypeModel FindType(AssemblyModel assembly, string fullName)
    {
        var type = assembly.Types.SingleOrDefault(candidate => candidate.FullName == fullName);
        Assert.True(
            type is not null,
            $"'{fullName}' is missing. Types read: {string.Join(", ", assembly.Types.Select(t => t.FullName))}");
        return type!;
    }

    internal static MethodModel FindMethod(AssemblyModel assembly, string typeFullName, string methodName)
    {
        var type = FindType(assembly, typeFullName);
        var method = type.Methods.SingleOrDefault(candidate => candidate.Name == methodName);
        Assert.True(
            method is not null,
            $"'{typeFullName}::{methodName}' is missing. Methods read: {string.Join(", ", type.Methods.Select(m => m.Name))}");
        return method!;
    }

    private static IReadOnlyCollection<string> LoadedAssemblyNames() =>
        AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .Select(loaded => loaded.GetName().Name)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
}
