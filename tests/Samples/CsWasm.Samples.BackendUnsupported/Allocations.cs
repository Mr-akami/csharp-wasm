namespace CsWasm.Samples.BackendUnsupported;

public class Node
{
    public int Value;
}

/// <summary>
/// A body the frontend accepts and the MoonBit backend of this step cannot lower.
/// </summary>
/// <remarks>
/// <c>newobj</c> is inside the supported opcode set (src/Frontend.Cil/SupportedInstructions.cs)
/// and normalises into the <c>new.object</c> SSA operation
/// (src/Frontend.Cil/Ssa/SpikeSsaSteps.cs), so this assembly reaches the backend with no
/// frontend diagnostic. Allocation is not in the mapping issue #16 lists, which makes this the
/// input that observes CSW4001 through the real command path rather than through a hand-built
/// model.
/// </remarks>
public static class Allocations
{
    public static Node Make()
    {
        return new Node();
    }
}
