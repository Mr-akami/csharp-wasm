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
/// <remarks>
/// <paramref name="Operand"/> is display text and must never be read back as data: it is
/// formatted for a human, and recovering a number or an arity from it would make the dump's
/// wording part of the compiler's semantics. A consumer that has to reason about an operand
/// reads the structured fields instead.
/// </remarks>
/// <param name="IntOperand">
/// The number the operand denotes, or null when the instruction denotes none: the absolute
/// target offset of a branch, the slot of an argument or local, or the value of an
/// <c>ldc.i4</c>. The short and macro forms carry it too, so <c>stloc.0</c> reports slot 0
/// and <c>ldc.i4.1</c> reports 1.
/// </param>
/// <param name="CallOperand">The called method, for the opcodes that take a method token.</param>
/// <param name="TypeOperand">
/// The type the operand token implies, in the same spelling <see cref="FieldModel.TypeName"/>
/// and <see cref="MethodModel.ReturnTypeName"/> use, or null when the opcode names no type:
/// the field type for a field token, the type itself for a type token, the constructed type
/// for <c>newobj</c> and the return type for the other method tokens.
/// </param>
public sealed record IlInstruction(
    int Offset,
    string OpCodeName,
    string? Operand,
    int? IntOperand = null,
    IlCallOperand? CallOperand = null,
    string? TypeOperand = null);

/// <summary>
/// The called method of a method token, as data rather than as display text.
/// </summary>
/// <param name="MemberName">The owner and name of the method, as <c>Owner::Name</c>.</param>
/// <param name="ArgumentCount">
/// The number of declared parameters, which does not include the instance itself. The number
/// of stack slots a <c>call</c> pops is this plus one when <paramref name="HasThis"/> holds;
/// <c>newobj</c> allocates the instance instead of popping it and pops only the parameters.
/// </param>
public sealed record IlCallOperand(string MemberName, int ArgumentCount, bool HasThis, bool ReturnsVoid);

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
