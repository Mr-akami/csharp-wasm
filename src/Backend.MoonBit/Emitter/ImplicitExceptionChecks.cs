using CsWasm.Diagnostics;
using CsWasm.Frontend.Cil.Ssa;

namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// Reports, per method, that the generated MoonBit carries none of the checks C# semantics
/// imply for the operations that method performs (CSW1005).
/// </summary>
/// <remarks>
/// Issue #16 decides that this step inserts no check for a missing reference, an array bound
/// or a division by zero, and that inserting them is Step 2 (issue #3). Silence would let the
/// generated code look like the C# it came from, so the departure is reported here as a
/// warning and written down in docs/reports/step1.md.
/// </remarks>
internal static class ImplicitExceptionChecks
{
    /// <summary>
    /// The operations whose C# form can throw without the program saying so: reading or
    /// writing a field or an element through a reference, and asking a reference for its
    /// length. Division is absent because the SSA vocabulary of this step has no division
    /// operation - <c>div</c> and <c>rem</c> are refused as CSW1004 before the backend sees
    /// them - which docs/reports/step1.md records.
    /// </summary>
    private static readonly HashSet<string> Unchecked = new(StringComparer.Ordinal)
    {
        "field.get", "field.set", "array.get", "array.set", "array.length",
    };

    /// <summary>
    /// One warning for <paramref name="method"/> when it performs at least one such operation,
    /// and none when it performs none. The unit is the method, so a run names the methods a
    /// reader has to check rather than the whole assembly.
    /// </summary>
    public static Diagnostic? For(string ownerFullName, SpikeSsaMethod method)
    {
        var performs = method.Blocks
            .SelectMany(block => block.Instructions)
            .Any(instruction => Unchecked.Contains(instruction.Op));

        if (!performs)
        {
            return null;
        }

        return new Diagnostic(
            DiagnosticCode.ImplicitExceptionChecksNotInserted,
            DiagnosticSeverity.Warning,
            $"'{ownerFullName}::{method.Name}' reads or writes through a reference, and the generated "
            + "MoonBit carries no check for the failures C# would raise there, so it departs from C# "
            + "semantics.",
            ownerFullName + "::" + method.Name,
            "Issue #3 (Step 2) inserts the explicit checks; docs/reports/step1.md records the gap.");
    }
}
