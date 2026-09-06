using CsWasm.Backend.MoonBit;
using CsWasm.Diagnostics;

namespace CsWasm.Driver;

/// <summary>
/// The <c>cswasm</c> entry point. Commands that later steps implement are present but
/// refuse with CSW0001 rather than silently doing something partial.
/// </summary>
public static class CommandLine
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;

    private const string Usage = """
        cswasm - a Wasm-native C# compiler targeting WasmGC through MoonBit

        Usage:
          cswasm --version            Print cswasm and pinned toolchain versions
          cswasm toolchain            Verify the local toolchain against the pin
          cswasm compile <input.dll> -o <output.wasm> [--keep-intermediates]
                                      Compile an assembly to WebAssembly; --keep-intermediates
                                      leaves the generated MoonBit package on disk
          cswasm check <input.dll>    Report package compatibility levels  (Step 3)
          cswasm dump il <input.dll>  Print the CIL the frontend read from an assembly
          cswasm dump ssa <input.dll> Print that CIL normalised into explicit values
          cswasm dump moonbit <input.dll>
                                      Print the MoonBit source the backend generates
          cswasm --help

        Documentation: docs/architecture.md, docs/diagnostics.md, CONTEXT.md
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stdout.WriteLine(Usage);
            return ExitSuccess;
        }

        return args[0] switch
        {
            "--help" or "-h" or "help" => Help(stdout),
            "--version" or "-v" or "version" => Version(stdout),
            "toolchain" => Toolchain(stdout, stderr),
            "compile" => Compile(args, stdout, stderr),
            "check" => NotImplementedYet("check", "Step 3 (issue #4)", stderr),
            "dump" => Dump(args, stdout, stderr),
            var unknown => Unknown(unknown, stderr),
        };
    }

    private static int Help(TextWriter stdout)
    {
        stdout.WriteLine(Usage);
        return ExitSuccess;
    }

    private static int Version(TextWriter stdout)
    {
        var pin = ToolchainPin.Current;
        stdout.WriteLine($"cswasm {ThisAssembly.Version}");
        stdout.WriteLine($"  moonbit    {pin.MoonBit.Version} (pinned {pin.MoonBit.PinnedOn})");
        stdout.WriteLine($"  moon       {pin.MoonBit.MoonVersion}");
        stdout.WriteLine($"  dotnet sdk {pin.Dotnet.Sdk}");
        stdout.WriteLine($"  node       {pin.Node.Version}");
        stdout.WriteLine($"  wasm-tools {pin.WasmTools.Version}");
        return ExitSuccess;
    }

    private static int Toolchain(TextWriter stdout, TextWriter stderr)
    {
        var pin = ToolchainPin.Current;
        var diagnostics = MoonBitToolchain.Verify(pin, out var found);

        foreach (var (tool, version) in found.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            stdout.WriteLine($"{tool,-6} {version}");
        }

        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(diagnostic.Format());
        }

        if (diagnostics.Count > 0)
        {
            return ExitFailure;
        }

        stdout.WriteLine($"toolchain matches the pin ({pin.MoonBit.Version})");
        return ExitSuccess;
    }

    /// <summary>
    /// The argument shapes <c>compile</c> accepts. The output path is required: issue #17 gives
    /// no default for it, and guessing one would put a module somewhere the user did not ask
    /// for.
    /// </summary>
    private static int Compile(string[] args, TextWriter stdout, TextWriter stderr) => args switch
    {
        ["compile", var input, "-o", var output] =>
            CompileCommand.Run(input, output, keepIntermediates: false, stdout, stderr),
        ["compile", var input, "-o", var output, "--keep-intermediates"] =>
            CompileCommand.Run(input, output, keepIntermediates: true, stdout, stderr),
        _ => Unknown(string.Join(' ', args), stderr),
    };

    private static int Dump(string[] args, TextWriter stdout, TextWriter stderr) => args switch
    {
        ["dump", "il", var input] => DumpCommand.Il(input, stdout, stderr),
        ["dump", "ssa", var input] => DumpCommand.Ssa(input, stdout, stderr),
        ["dump", "moonbit", var input] => DumpCommand.MoonBit(input, stdout, stderr),
        _ => Unknown(string.Join(' ', args), stderr),
    };

    private static int NotImplementedYet(string command, string step, TextWriter stderr)
    {
        stderr.WriteLine(Diagnostic.Error(
            DiagnosticCode.NotImplementedYet,
            $"'{command}' is not implemented yet; it lands in {step}.",
            "See the epic at https://github.com/Mr-akami/csharp-wasm/issues/12 for the step order.").Format());
        return ExitFailure;
    }

    private static int Unknown(string arg, TextWriter stderr)
    {
        stderr.WriteLine(Diagnostic.Error(
            DiagnosticCode.BadCommandLine,
            $"Unknown command or option '{arg}'.",
            "Run `cswasm --help`.").Format());
        return ExitFailure;
    }
}
