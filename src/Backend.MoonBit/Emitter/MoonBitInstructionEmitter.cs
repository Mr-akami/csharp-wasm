using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// <c>emit_instruction</c>: turns one SSA operation into one line of MoonBit
/// (docs/architecture.md section 9.1).
/// </summary>
/// <remarks>
/// There is no case that shrugs. An operation this step has no lowering for is reported to the
/// caller, which refuses the whole run under CSW4001; emitting a comment in its place would
/// produce a program that is not the one in the assembly (docs/diagnostics.md rule 1).
/// <para>
/// No lowering here inserts a check for a missing reference, an array bound or a division by
/// zero. Issue #16 decides that this step inserts none of them and states the departure from
/// C# semantics in docs/reports/step1.md and under CSW1005 instead; inserting one "to be safe"
/// would make the generated code disagree with both.
/// </para>
/// </remarks>
internal static class MoonBitInstructionEmitter
{
    /// <summary>
    /// <c>System.Object</c>'s constructor, which every C# class constructor calls and which a
    /// MoonBit struct has no counterpart for.
    /// </summary>
    private const string ObjectConstructorOwner = "System.Object";

    private const string ObjectConstructorName = ".ctor";

    /// <summary>
    /// The MoonBit for <paramref name="instruction"/>, or null with <paramref name="reason"/>
    /// set when this step cannot lower it. An empty string is a lowering that writes nothing:
    /// the <c>arg</c> operations of the entry block are the function's parameters, which the
    /// signature has already named.
    /// </summary>
    public static string? Emit(SpikeSsaInstruction instruction, out string reason)
    {
        reason = string.Empty;

        return instruction.Op switch
        {
            "arg" => string.Empty,
            "const" => Constant(instruction, out reason),
            "add" => Binary(instruction, "+", out reason),
            "lt" => Binary(instruction, "<", out reason),
            "conv.i4" => Widen(instruction, out reason),
            "array.get" => Defining(instruction, 2, out reason, values => values[0] + "[" + values[1] + "]"),
            "array.set" => Writing(instruction, 3, out reason,
                values => values[0] + "[" + values[1] + "] = " + values[2]),
            "array.length" => Defining(instruction, 1, out reason, values => values[0] + ".length()"),
            "field.get" => FieldRead(instruction, out reason),
            "field.set" => FieldWrite(instruction, out reason),
            "call" => Call(instruction, out reason),
            _ => Unlowerable(instruction.Op, out reason),
        };
    }

    private static string? Unlowerable(string op, out string reason)
    {
        reason = $"'{op}' has no MoonBit form in this step.";
        return null;
    }

    private static string? Constant(SpikeSsaInstruction instruction, out string reason)
    {
        reason = string.Empty;

        if (instruction.Result is not { } result)
        {
            reason = "'const' defines no value.";
            return null;
        }

        // A reference or an array has no literal in the minimal type family, so the zero that
        // ".locals init" gives such a local cannot be written down here.
        if (result.Type.Kind is SpikeSsaTypeKind.Ref or SpikeSsaTypeKind.Array)
        {
            reason = "a constant of a reference type has no MoonBit literal in this step.";
            return null;
        }

        if (instruction.Detail is not { Length: > 0 } literal)
        {
            reason = "'const' names no value.";
            return null;
        }

        return Let(result, literal);
    }

    private static string? Binary(SpikeSsaInstruction instruction, string @operator, out string reason) =>
        Defining(instruction, 2, out reason, values => values[0] + " " + @operator + " " + values[1]);

    private static string? Widen(SpikeSsaInstruction instruction, out string reason)
    {
        reason = string.Empty;

        if (instruction.Operands.Count == 1 && instruction.Operands[0].Type.Kind != SpikeSsaTypeKind.I32)
        {
            reason = "'conv.i4' narrows a wider number, which this step does not model.";
            return null;
        }

        // Converting an i32 to an i32 is the identity. The binding is still written so that a
        // value the SSA dump numbers appears under that number in the generated source.
        return Defining(instruction, 1, out reason, values => values[0]);
    }

    private static string? FieldRead(SpikeSsaInstruction instruction, out string reason)
    {
        if (!TryField(instruction, 1, out var field, out reason))
        {
            return null;
        }

        return Defining(instruction, 1, out reason, values => values[0] + "." + field);
    }

    private static string? FieldWrite(SpikeSsaInstruction instruction, out string reason)
    {
        if (!TryField(instruction, 2, out var field, out reason))
        {
            return null;
        }

        return Writing(instruction, 2, out reason, values => values[0] + "." + field + " = " + values[1]);
    }

    private static string? Call(SpikeSsaInstruction instruction, out string reason)
    {
        reason = string.Empty;

        if (instruction is
            {
                Result: null,
                Member: { OwnerTypeName: ObjectConstructorOwner, MemberName: ObjectConstructorName },
            })
        {
            // A MoonBit struct has no base to initialise, so this call has no lowering. The
            // line records that it was read and dropped, rather than leaving a silent gap.
            return "// " + ObjectConstructorOwner + "::" + ObjectConstructorName
                + " initialises a base that a struct does not have";
        }

        reason = instruction.Member is { } member
            ? $"a call to '{member.OwnerTypeName}::{member.MemberName}' has no MoonBit form in this step."
            : "a call has no MoonBit form in this step.";
        return null;
    }

    /// <summary>
    /// The generated name of the field the instruction reads or writes, or false with
    /// <paramref name="reason"/> set when the instruction does not name one.
    /// </summary>
    private static bool TryField(
        SpikeSsaInstruction instruction,
        int operandCount,
        out string field,
        out string reason)
    {
        field = string.Empty;
        reason = string.Empty;

        if (instruction.Member is not { } member)
        {
            reason = $"'{instruction.Op}' names no field, so the field it touches cannot be written down.";
            return false;
        }

        if (instruction.Operands.Count != operandCount)
        {
            reason = $"'{instruction.Op}' has "
                + $"{instruction.Operands.Count} operand(s) where it needs {operandCount}.";
            return false;
        }

        field = MoonBitNames.OfField(member.OwnerTypeName, member.MemberName);
        return true;
    }

    /// <summary>An operation that defines a value: <c>let vN = &lt;expression&gt;</c>.</summary>
    private static string? Defining(
        SpikeSsaInstruction instruction,
        int operandCount,
        out string reason,
        Func<IReadOnlyList<string>, string> expression)
    {
        if (!TryOperands(instruction, operandCount, out var values, out reason))
        {
            return null;
        }

        if (instruction.Result is not { } result)
        {
            reason = $"'{instruction.Op}' defines no value.";
            return null;
        }

        return Let(result, expression(values));
    }

    /// <summary>An operation that defines nothing and only writes.</summary>
    private static string? Writing(
        SpikeSsaInstruction instruction,
        int operandCount,
        out string reason,
        Func<IReadOnlyList<string>, string> statement)
    {
        if (!TryOperands(instruction, operandCount, out var values, out reason))
        {
            return null;
        }

        if (instruction.Result is not null)
        {
            reason = $"'{instruction.Op}' defines a value, and this step writes none for it.";
            return null;
        }

        return statement(values);
    }

    private static bool TryOperands(
        SpikeSsaInstruction instruction,
        int operandCount,
        out IReadOnlyList<string> values,
        out string reason)
    {
        reason = string.Empty;

        if (instruction.Operands.Count != operandCount)
        {
            values = [];
            reason = $"'{instruction.Op}' has "
                + $"{instruction.Operands.Count} operand(s) where it needs {operandCount}.";
            return false;
        }

        values = [.. instruction.Operands.Select(MoonBitNames.OfValue)];
        return true;
    }

    private static string Let(SpikeSsaValue result, string expression) =>
        "let " + MoonBitNames.OfValue(result) + " = " + expression;
}
