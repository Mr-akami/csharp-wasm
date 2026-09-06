namespace CsWasm.Samples.Poc;

// Verbatim from docs/architecture.md section 31, the proof-of-concept target.
// Point is public rather than the internal default of the document because Sum's
// signature exposes it; C# rejects a public member with an internal parameter type.
public class Point
{
    public int X;
    public int Y;
}

public static class Sample
{
    public static int Sum(Point[] points)
    {
        int n = 0;
        for (int i = 0; i < points.Length; i++)
            n += points[i].X + points[i].Y;
        return n;
    }
}
