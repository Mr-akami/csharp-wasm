namespace CsWasm.Frontend.Cil;

/// <summary>
/// One assembly as the CIL frontend read it. Nothing here is interpreted: the model says
/// what the metadata and the method bodies contain, in metadata table order, and leaves
/// every semantic decision to later steps.
/// </summary>
public sealed record AssemblyModel(string Name, IReadOnlyList<TypeModel> Types);

/// <summary>A type definition. <paramref name="BaseTypeName"/> is null for a type with no base.</summary>
public sealed record TypeModel(
    string FullName,
    string? BaseTypeName,
    IReadOnlyList<FieldModel> Fields,
    IReadOnlyList<MethodModel> Methods);

public sealed record FieldModel(string Name, string TypeName);

/// <summary><paramref name="Body"/> is null for a method definition that carries no IL.</summary>
public sealed record MethodModel(
    string Name,
    bool IsStatic,
    string ReturnTypeName,
    IReadOnlyList<string> ParameterTypeNames,
    MethodBodyModel? Body);

public sealed record MethodBodyModel(
    int MaxStack,
    IReadOnlyList<LocalModel> Locals,
    IReadOnlyList<IlInstruction> Instructions,
    IReadOnlyList<ExceptionRegionModel> ExceptionRegions);

public sealed record LocalModel(int Index, string TypeName);

/// <summary>
/// One decoded instruction. <paramref name="Offset"/> is the byte offset of the opcode
/// inside the method body, and <paramref name="Operand"/> is the operand already rendered
/// for display - branch targets as absolute offsets, tokens as the member they name -
/// or null when the instruction takes no operand.
/// </summary>
public sealed record IlInstruction(int Offset, string OpCodeName, string? Operand);

public enum ExceptionRegionKind
{
    Catch,
    Filter,
    Finally,
    Fault,
}

/// <summary><paramref name="CatchTypeName"/> is set only for <see cref="ExceptionRegionKind.Catch"/>.</summary>
public sealed record ExceptionRegionModel(
    ExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    string? CatchTypeName);
