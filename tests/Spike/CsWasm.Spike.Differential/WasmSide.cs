using System.Text.RegularExpressions;
using CsWasm.Driver;
using CsWasm.Spike.Observations;

namespace CsWasm.Spike.Differential;

/// <summary>
/// Side (b) of issue #18: the same C# compiled by <c>cswasm compile</c> and called from Node.
/// </summary>
/// <remarks>
/// The compiler is driven through <see cref="CommandLine.Run"/>, the entry point a user has,
/// rather than through the classes behind it - the same judgement
/// <c>tests/CsWasm.Tests/CompileCommandTests.cs</c> makes.
/// </remarks>
internal static class WasmSide
{
    /// <summary>The sample assembly, copied next to the test binaries as content.</summary>
    private static string SampleAssembly { get; } =
        Path.Combine(AppContext.BaseDirectory, "CsWasm.Spike.Samples.dll");

    public static Observation Run(SpikeCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        var workspace = Directory.CreateTempSubdirectory("cswasm-spike-").FullName;
        try
        {
            var (dumped, source, dumpError) = Cswasm("dump", "moonbit", SampleAssembly);
            if (dumped != CommandLine.ExitSuccess)
            {
                return Failed($"`cswasm dump moonbit` failed with exit code {dumped}: {dumpError}");
            }

            var module = Path.Combine(workspace, "Sample.wasm");
            var (compiled, _, compileError) = Cswasm("compile", SampleAssembly, "-o", module);
            if (compiled != CommandLine.ExitSuccess)
            {
                return Failed($"`cswasm compile` failed with exit code {compiled}: {compileError}");
            }

            return HostProcess.Observe(
                "node",
                NodeHost.Executable,
                [
                    NodeHost.Harness,
                    "--wasm", module,
                    "--export", ExportNameFor(source, testCase.MethodName),
                    "--args", testCase.ArgumentsAsJson,
                ],
                testCase.Deadline);
        }
        finally
        {
            // Every ending - agreement, disagreement, exception - leaves nothing behind.
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// The name the backend gave the case's method, read from the MoonBit it generates.
    /// </summary>
    /// <remarks>
    /// The name is resolved on this side because the backend owns how it is spelt
    /// (<c>src/Backend.MoonBit/Emitter/MoonBitNames.cs</c>); a harness that rebuilt the
    /// mangling in JavaScript would be a second copy of that rule.
    /// <para>
    /// When the sample declares no such method there is no generated function, and the case's
    /// own method name is passed through: the module does not export it either, so the
    /// question "can this module run this case" is still answered where every other answer to
    /// it comes from - the harness that looks the export up and reports a host error.
    /// </para>
    /// </remarks>
    private static string ExportNameFor(string generatedSource, string methodName)
    {
        var match = Regex.Match(
            generatedSource,
            @"fn\s+([A-Za-z_][A-Za-z0-9_]*" + Regex.Escape(methodName) + @"[A-Za-z0-9_]*)\s*\(");

        return match.Success ? match.Groups[1].Value : methodName;
    }

    private static (int Code, string Out, string Err) Cswasm(params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CommandLine.Run(arguments, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static Observation Failed(string detail) =>
        new(
            Observation.CurrentSchema,
            Outcome.HostError,
            ReturnValue: null,
            Stdout: string.Empty,
            Stderr: string.Empty,
            ExceptionType: null,
            detail,
            new HostInfo("node", string.Empty, new HostCapabilities(JsStringBuiltins: null)));
}
