using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CsWasm.Diagnostics;

namespace CsWasm.Frontend.Cil;

/// <summary>
/// Reads an assembly's metadata and method bodies into <see cref="AssemblyModel"/>.
/// </summary>
/// <remarks>
/// The file is read through <see cref="PEReader"/> over a file stream and is never loaded
/// into this process: inspecting an assembly must not run its module initialisers, must not
/// bind its references, and must not be limited to what the host runtime can already load
/// (docs/architecture.md section 5.2).
/// </remarks>
public static class CilAssemblyReader
{
    /// <summary>
    /// Reads <paramref name="path"/> into <paramref name="assembly"/> and returns one
    /// diagnostic per construct the model cannot represent. An empty list means every type,
    /// member and body in the file was modelled. A member that produced a diagnostic is left
    /// out of the model rather than approximated.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Read(string path, out AssemblyModel assembly)
    {
        var diagnostics = new List<Diagnostic>();
        var fallbackName = Path.GetFileNameWithoutExtension(path);

        using var stream = File.OpenRead(path);
        PEReader? peReader = null;
        try
        {
            peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                assembly = new AssemblyModel(fallbackName, []);
                diagnostics.Add(Unsupported($"'{path}' contains no ECMA-335 metadata.", fallbackName));
                return diagnostics;
            }

            assembly = ReadAssembly(peReader, peReader.GetMetadataReader(), fallbackName, diagnostics);
        }
        catch (BadImageFormatException ex)
        {
            assembly = new AssemblyModel(fallbackName, []);
            diagnostics.Add(Unsupported(
                $"'{path}' is not a portable executable cswasm can read: {ex.Message}",
                fallbackName));
        }
        finally
        {
            peReader?.Dispose();
        }

        return diagnostics;
    }

    private static AssemblyModel ReadAssembly(
        PEReader image,
        MetadataReader reader,
        string fallbackName,
        List<Diagnostic> diagnostics)
    {
        var formatter = new CilSignatureFormatter();

        // Operand rendering must not feed the metadata gate: an instruction may name a
        // construct this step does not model, and refusing that instruction is the
        // supported-opcode gate's decision, under its own code.
        var operandFormatter = new CilSignatureFormatter();

        var name = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : fallbackName;

        var types = new List<TypeModel>();

        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            var fullName = FullName(reader, definition);

            // <Module> is the container the runtime uses for global members, not a type the
            // source declared.
            if (string.Equals(fullName, "<Module>", StringComparison.Ordinal))
            {
                continue;
            }

            if (definition.GetGenericParameters().Count > 0)
            {
                diagnostics.Add(Unsupported($"Type '{fullName}' is a generic type definition.", fullName));
                continue;
            }

            var baseTypeName = definition.BaseType.IsNil
                ? null
                : formatter.TypeName(reader, definition.BaseType);
            var fields = ReadFields(reader, definition, fullName, formatter, diagnostics);
            var methods = ReadMethods(image, reader, definition, fullName, formatter, operandFormatter, diagnostics);

            types.Add(new TypeModel(fullName, baseTypeName, fields, methods));
        }

        return new AssemblyModel(name, types);
    }

    private static IReadOnlyList<FieldModel> ReadFields(
        MetadataReader reader,
        TypeDefinition definition,
        string typeFullName,
        CilSignatureFormatter formatter,
        List<Diagnostic> diagnostics)
    {
        var fields = new List<FieldModel>();

        foreach (var handle in definition.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            var name = reader.GetString(field.Name);

            formatter.ClearUnsupported();
            var typeName = field.DecodeSignature(formatter, null);

            if (formatter.Unsupported.Count > 0)
            {
                diagnostics.Add(Unsupported(
                    $"Field '{typeFullName}::{name}' has a signature using {Constructs(formatter)}.",
                    $"{typeFullName}::{name}"));
                continue;
            }

            fields.Add(new FieldModel(name, typeName));
        }

        return fields;
    }

    private static IReadOnlyList<MethodModel> ReadMethods(
        PEReader image,
        MetadataReader reader,
        TypeDefinition definition,
        string typeFullName,
        CilSignatureFormatter formatter,
        CilSignatureFormatter operandFormatter,
        List<Diagnostic> diagnostics)
    {
        var methods = new List<MethodModel>();

        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            var name = reader.GetString(method.Name);
            var location = $"{typeFullName}::{name}";

            if (method.GetGenericParameters().Count > 0)
            {
                diagnostics.Add(Unsupported($"Method '{location}' is a generic method definition.", location));
                continue;
            }

            if (!IsManagedIl(method))
            {
                diagnostics.Add(Unsupported($"Method '{location}' is not implemented in managed IL.", location));
                continue;
            }

            formatter.ClearUnsupported();
            var signature = method.DecodeSignature(formatter, null);

            if (formatter.Unsupported.Count > 0)
            {
                diagnostics.Add(Unsupported(
                    $"Method '{location}' has a signature using {Constructs(formatter)}.",
                    location));
                continue;
            }

            // A method definition without an RVA carries no IL of its own (abstract, or
            // supplied elsewhere); the model says so instead of inventing an empty body.
            var body = method.RelativeVirtualAddress == 0
                ? null
                : MethodBodyDecoder.Decode(
                    reader,
                    image.GetMethodBody(method.RelativeVirtualAddress),
                    operandFormatter);

            methods.Add(new MethodModel(
                name,
                (method.Attributes & MethodAttributes.Static) != 0,
                signature.ReturnType,
                signature.ParameterTypes,
                body));
        }

        return methods;
    }

    private static bool IsManagedIl(MethodDefinition method) =>
        (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.IL
        && (method.ImplAttributes & MethodImplAttributes.ManagedMask) == MethodImplAttributes.Managed
        && (method.ImplAttributes & MethodImplAttributes.InternalCall) == 0
        && (method.Attributes & MethodAttributes.PinvokeImpl) == 0;

    private static string Constructs(CilSignatureFormatter formatter) =>
        string.Join(", ", formatter.Unsupported.Distinct(StringComparer.Ordinal));

    private static Diagnostic Unsupported(string message, string location) =>
        new(
            DiagnosticCode.UnsupportedMetadata,
            DiagnosticSeverity.Error,
            message,
            location,
            "This step reads the proof-of-concept subset of ECMA-335 described in docs/architecture.md section 31.");

    private static string FullName(MetadataReader reader, TypeDefinition definition)
    {
        var name = reader.GetString(definition.Name);

        if (definition.IsNested)
        {
            return FullName(reader, reader.GetTypeDefinition(definition.GetDeclaringType())) + "/" + name;
        }

        var @namespace = reader.GetString(definition.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }
}
