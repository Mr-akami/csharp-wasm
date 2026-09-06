namespace CsWasm.Samples.Poc;

// The same type identity as tests/Samples/CsWasm.Samples.Poc/Sample.cs: the same namespace,
// the same type name, the same fields. Everything around it differs - the assembly name, the
// assembly file name, the other types in the assembly, the methods those types declare, and
// the metadata order the type is written in. Issue #16 requires that none of those move the
// generated identifier of Point, so this sample is what makes "an unrelated change does not
// move the name" observable rather than asserted about one assembly on its own.

// Declared before Point so that Point is not the first type in the metadata table here,
// unlike in CsWasm.Samples.Poc.
public static class Unrelated
{
    public static int Twice(int a)
    {
        return a + a;
    }
}

public class Point
{
    public int X;
    public int Y;
}
