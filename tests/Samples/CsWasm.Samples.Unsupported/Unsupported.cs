using System;

namespace CsWasm.Samples.Unsupported;

/// <summary>
/// Constructs the CIL frontend must refuse or must model, one per member so a test can
/// name the member that produced a diagnostic.
/// </summary>
public static class UnsupportedShapes
{
    /// <summary>Emits <c>mul</c>, which is outside the supported opcode set (CSW1001).</summary>
    public static int Multiply(int left, int right) => left * right;

    /// <summary>A generic method definition: unsupported metadata (CSW1002).</summary>
    public static T Identity<T>(T value) => value;

    /// <summary>
    /// Takes the address of a local, so the local cannot be promoted to an SSA value
    /// (CSW1003). The opcode itself decodes and prints; only the promotion refuses.
    /// </summary>
    public static int CompareLocal(int value)
    {
        int local = value;
        return local.CompareTo(2);
    }

    /// <summary>
    /// Carries one catch region so the exception-region decoding can be observed from the
    /// model. Its <c>leave</c> instructions are outside the supported opcode set, so this
    /// member is only reachable through the model, never through a successful dump.
    /// </summary>
    public static int Guarded(int[] values, int index)
    {
        try
        {
            return values[index];
        }
        catch (IndexOutOfRangeException)
        {
            return -1;
        }
    }
}
