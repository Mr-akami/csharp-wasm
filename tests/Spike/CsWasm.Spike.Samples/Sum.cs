namespace CsWasm.Spike.Samples;

/// <summary>
/// The C# that issue #18 runs twice: once by .NET, once by compiling it with cswasm and
/// calling the module from Node.
/// </summary>
/// <remarks>
/// The arguments are <c>int</c> rather than the proof-of-concept sample's <c>Point[]</c> for
/// two reasons issue #18 states: a host has to be able to build the arguments and read the
/// result to compare them at all, and this step compares no floating point.
/// <para>
/// Nothing else belongs in this assembly. The frontend validates every method it finds
/// (src/Frontend.Cil/SupportedInstructions.cs), so an unrelated member here would make
/// <c>cswasm compile</c> refuse the whole assembly.
/// </para>
/// </remarks>
public static class Sample
{
    public static int Sum(int a, int b)
    {
        return a + b;
    }
}
