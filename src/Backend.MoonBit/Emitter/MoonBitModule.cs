namespace CsWasm.Backend.MoonBit.Emitter;

/// <summary>
/// What the emitter made of one assembly: the MoonBit source, and the names of the functions
/// the generated package exports.
/// </summary>
/// <remarks>
/// The exported names travel beside the source rather than being read back out of it. The
/// generated text is an output format, not a representation anything parses
/// (docs/architecture.md section 4.2), and the package manifest has to name the exports before
/// a single byte of it has been written down.
/// </remarks>
public sealed record MoonBitModule(string Source, IReadOnlyList<string> Exports);
