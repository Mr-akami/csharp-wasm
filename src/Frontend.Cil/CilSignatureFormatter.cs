using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;

namespace CsWasm.Frontend.Cil;

/// <summary>
/// Renders ECMA-335 signatures and member tokens as display text: primitives in ILAsm
/// spelling, everything else as its fully qualified metadata name without assembly
/// qualification.
/// </summary>
/// <remarks>
/// A construct outside the proof-of-concept scope is recorded in <see cref="Unsupported"/>
/// instead of being approximated, so the reader can report it as CSW1002 rather than let a
/// signature it cannot represent look as if it had been understood.
/// </remarks>
internal sealed class CilSignatureFormatter : ISignatureTypeProvider<string, object?>
{
    private readonly List<string> unsupported = [];

    public IReadOnlyList<string> Unsupported => unsupported;

    public void ClearUnsupported() => unsupported.Clear();

    private string Refuse(string construct, string rendered)
    {
        unsupported.Add(construct);
        return rendered;
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.SByte => "int8",
        PrimitiveTypeCode.Byte => "uint8",
        PrimitiveTypeCode.Int16 => "int16",
        PrimitiveTypeCode.UInt16 => "uint16",
        PrimitiveTypeCode.Int32 => "int32",
        PrimitiveTypeCode.UInt32 => "uint32",
        PrimitiveTypeCode.Int64 => "int64",
        PrimitiveTypeCode.UInt64 => "uint64",
        PrimitiveTypeCode.Single => "float32",
        PrimitiveTypeCode.Double => "float64",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "native int",
        PrimitiveTypeCode.UIntPtr => "native uint",
        PrimitiveTypeCode.TypedReference => Refuse("typedref", "typedref"),
        _ => Refuse("primitive type " + typeCode.ToString(), typeCode.ToString()),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        DefinitionName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        ReferenceName(reader, handle);

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) =>
        Refuse("multi-dimensional array", elementType + "[" + new string(',', Math.Max(shape.Rank - 1, 0)) + "]");

    public string GetByReferenceType(string elementType) => Refuse("byref", elementType + "&");

    public string GetPointerType(string elementType) => Refuse("pointer", elementType + "*");

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        Refuse("function pointer", "method*");

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        Refuse("generic instantiation", genericType + "<" + string.Join(", ", typeArguments) + ">");

    public string GetGenericMethodParameter(object? genericContext, int index) =>
        Refuse("generic method parameter", "!!" + index.ToString(CultureInfo.InvariantCulture));

    public string GetGenericTypeParameter(object? genericContext, int index) =>
        Refuse("generic type parameter", "!" + index.ToString(CultureInfo.InvariantCulture));

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        Refuse(isRequired ? "modreq" : "modopt", unmodifiedType);

    public string GetPinnedType(string elementType) => Refuse("pinned", elementType + " pinned");

    /// <summary>Renders any handle that names a type: a definition, a reference or a specification.</summary>
    public string TypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => DefinitionName(reader, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => ReferenceName(reader, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification =>
            reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        _ => Refuse("type handle " + handle.Kind.ToString(), handle.Kind.ToString()),
    };

    /// <summary>Renders a field or method token the way ILAsm names it, signature included.</summary>
    public string MemberName(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                return field.DecodeSignature(this, null)
                    + " " + DefinitionName(reader, field.GetDeclaringType())
                    + "::" + reader.GetString(field.Name);
            }

            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return MethodDisplay(
                    method.DecodeSignature(this, null),
                    DefinitionName(reader, method.GetDeclaringType()),
                    reader.GetString(method.Name));
            }

            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var owner = TypeName(reader, member.Parent);
                var name = reader.GetString(member.Name);
                return member.GetKind() == MemberReferenceKind.Field
                    ? member.DecodeFieldSignature(this, null) + " " + owner + "::" + name
                    : MethodDisplay(member.DecodeMethodSignature(this, null), owner, name);
            }

            case HandleKind.MethodSpecification:
            {
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return MemberName(reader, specification.Method);
            }

            default:
                return TypeName(reader, handle);
        }
    }

    /// <summary>
    /// Renders a method the way ILAsm names one:
    /// <c>[instance ]&lt;return&gt; &lt;owner&gt;::&lt;name&gt;(&lt;parameters&gt;)</c>.
    /// </summary>
    private static string MethodDisplay(MethodSignature<string> signature, string owner, string name) =>
        (signature.Header.IsInstance ? "instance " : string.Empty)
        + signature.ReturnType + " " + owner + "::" + name
        + "(" + string.Join(", ", signature.ParameterTypes) + ")";

    private static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);

        if (definition.IsNested)
        {
            return DefinitionName(reader, definition.GetDeclaringType()) + "/" + name;
        }

        var @namespace = reader.GetString(definition.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }

    private static string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return ReferenceName(reader, (TypeReferenceHandle)reference.ResolutionScope) + "/" + name;
        }

        var @namespace = reader.GetString(reference.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }
}
