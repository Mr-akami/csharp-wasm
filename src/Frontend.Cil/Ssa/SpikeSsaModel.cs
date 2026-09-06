namespace CsWasm.Frontend.Cil.Ssa;

/// <summary>
/// The whole type family this step has, and the whole it is meant to have: issue #15 names
/// <c>i32 / i64 / f64 / bool / ref&lt;TypeId&gt; / array&lt;T&gt;</c> and says not to widen it here.
/// A body using anything else is refused (CSW1004) rather than rounded off to the nearest
/// member.
/// </summary>
public enum SpikeSsaTypeKind
{
    I32,
    I64,
    F64,
    Bool,
    Ref,
    Array,
}

/// <summary>
/// One value's type. <paramref name="RefTypeName"/> is set only for
/// <see cref="SpikeSsaTypeKind.Ref"/> and <paramref name="ElementType"/> only for
/// <see cref="SpikeSsaTypeKind.Array"/>.
/// </summary>
public sealed record SpikeSsaType(
    SpikeSsaTypeKind Kind,
    string? RefTypeName = null,
    SpikeSsaType? ElementType = null)
{
    public static SpikeSsaType Int32 { get; } = new(SpikeSsaTypeKind.I32);

    public static SpikeSsaType Int64 { get; } = new(SpikeSsaTypeKind.I64);

    public static SpikeSsaType Float64 { get; } = new(SpikeSsaTypeKind.F64);

    public static SpikeSsaType Boolean { get; } = new(SpikeSsaTypeKind.Bool);

    public static SpikeSsaType Reference(string typeName) => new(SpikeSsaTypeKind.Ref, RefTypeName: typeName);

    public static SpikeSsaType ArrayOf(SpikeSsaType elementType) =>
        new(SpikeSsaTypeKind.Array, ElementType: elementType);
}

/// <summary>
/// One value, defined exactly once - by a block parameter or by an instruction - and referred
/// to by that identity everywhere else. <paramref name="Id"/> is unique inside its method.
/// </summary>
public sealed record SpikeSsaValue(int Id, SpikeSsaType Type);

/// <summary>
/// One operation on values. <paramref name="Result"/> is null for an operation that defines
/// nothing, and <paramref name="Detail"/> carries what the operation names - the constant, the
/// field, the callee - for the operations that name something.
/// </summary>
/// <remarks>
/// <paramref name="Op"/> uses the vocabulary of docs/architecture.md section 6.3:
/// <c>arg / const / add / lt / conv.i4 / field.get / field.set / array.get / array.set /
/// array.length / new.array / new.object / call</c>. Nothing that only moves a value between
/// the evaluation stack and a local appears here; that is what normalising the stack means.
/// </remarks>
/// <param name="Member">
/// The member the operation names, read as data rather than as the display text
/// <paramref name="Detail"/> carries: the field of a <c>field.get</c> or <c>field.set</c>, the
/// callee of a <c>call</c> or <c>new.object</c>. Null when the operation names no member, and
/// null for a hand-built instruction whose token carried none.
/// </param>
public sealed record SpikeSsaInstruction(
    SpikeSsaValue? Result,
    string Op,
    IReadOnlyList<SpikeSsaValue> Operands,
    string? Detail = null,
    SpikeSsaMemberReference? Member = null);

/// <summary>
/// A member an instruction names, as the owner's fully qualified name and the member's own
/// name. This is .NET identity, not display text: a consumer reads it instead of recovering a
/// name from <see cref="SpikeSsaInstruction.Detail"/>.
/// </summary>
public sealed record SpikeSsaMemberReference(string OwnerTypeName, string MemberName);

public enum SpikeSsaTerminatorKind
{
    Branch,
    CondBranch,
    Return,
}

/// <summary>
/// One edge out of a block. <paramref name="Arguments"/> holds the values the edge passes to
/// the target's parameters, and is empty for a target that takes none.
/// </summary>
public sealed record SpikeSsaEdge(int TargetIlOffset, IReadOnlyList<SpikeSsaValue> Arguments);

/// <summary>
/// How a block ends. <paramref name="Condition"/> names the comparison of a
/// <see cref="SpikeSsaTerminatorKind.CondBranch"/> and is null otherwise;
/// <paramref name="Operands"/> holds the compared values, or the returned value for a
/// <see cref="SpikeSsaTerminatorKind.Return"/> that returns one.
/// </summary>
/// <remarks>
/// The successors of a <see cref="SpikeSsaTerminatorKind.CondBranch"/> are ordered
/// [taken, not taken].
/// </remarks>
public sealed record SpikeSsaTerminator(
    SpikeSsaTerminatorKind Kind,
    string? Condition,
    IReadOnlyList<SpikeSsaValue> Operands,
    IReadOnlyList<SpikeSsaEdge> Successors);

/// <summary>
/// One basic block, identified by the IL offset it starts at.
/// </summary>
/// <remarks>
/// A merge is expressed by <paramref name="Parameters"/> and by the arguments its incoming
/// edges pass, never by a phi instruction (issue #15: block arguments, no phi nodes). Only a
/// block with more than one predecessor takes parameters; a block with one predecessor sees
/// its predecessor's values directly.
/// </remarks>
public sealed record SpikeSsaBlock(
    int IlOffset,
    IReadOnlyList<SpikeSsaValue> Parameters,
    IReadOnlyList<SpikeSsaInstruction> Instructions,
    SpikeSsaTerminator Terminator);

/// <summary><paramref name="Blocks"/> is ordered by <see cref="SpikeSsaBlock.IlOffset"/>.</summary>
/// <remarks>
/// The entry block - the first of <paramref name="Blocks"/> in IL offset order - defines one
/// <c>arg</c> instruction per argument slot, in slot order and before anything else, so a
/// consumer reads the parameter list off those instructions rather than out of their
/// <see cref="SpikeSsaInstruction.Detail"/>. Slot 0 of an instance method is the instance
/// itself, which <paramref name="IsStatic"/> distinguishes.
/// </remarks>
/// <param name="ReturnType">Null for a method declared to return nothing.</param>
public sealed record SpikeSsaMethod(
    string Name,
    bool IsStatic,
    SpikeSsaType? ReturnType,
    IReadOnlyList<SpikeSsaBlock> Blocks);

/// <summary>
/// One instance field. <paramref name="Type"/> is null for a field whose declared type is
/// outside the minimal type family; the field is still listed, because leaving it out would
/// describe a type that is not the one in the file.
/// </summary>
public sealed record SpikeSsaField(string Name, SpikeSsaType? Type);

public sealed record SpikeSsaTypeDefinition(
    string FullName,
    IReadOnlyList<SpikeSsaField> Fields,
    IReadOnlyList<SpikeSsaMethod> Methods);

/// <summary>
/// One assembly normalised out of its evaluation stack, in metadata table order. A method that
/// could not be normalised is absent rather than approximated (docs/diagnostics.md rule 1).
/// </summary>
public sealed record SpikeSsaAssembly(string Name, IReadOnlyList<SpikeSsaTypeDefinition> Types);
