# csharp-wasm

**CsWasm** compiles C#/.NET assemblies to WebAssembly with a WasmGC-native object model,
by lowering them to an owned semantic IR (**CsIR**) and emitting MoonBit source for the
unmodified, version-pinned official MoonBit compiler.

```
C# / NuGet assemblies -> ILLink -> CIL frontend -> CsIR -> whole-program passes
                                                            -> MoonBit source -> moonc --target wasm-gc -> app.wasm
```

The project prioritises **breadth of .NET compatibility over raw performance**. It is not a
port of the CLR: there is no Mono, no CoreCLR, no IL interpreter and no second GC heap in
linear memory.

- Architecture: [`docs/architecture.md`](docs/architecture.md)
- Vocabulary: [`CONTEXT.md`](CONTEXT.md)
- Decisions: [`docs/adr/`](docs/adr/)
- Diagnostics: [`docs/diagnostics.md`](docs/diagnostics.md)
- Host profiles: [`docs/host-profiles.md`](docs/host-profiles.md)
- Plan: [issue #12](https://github.com/Mr-akami/csharp-wasm/issues/12) (Steps 0-10)

## Status

Step 0 (foundations) is in place. `cswasm compile <input.dll> -o <output.wasm>` compiles the
proof-of-concept subset end to end, through the pinned MoonBit toolchain
([`docs/moonbit-packaging.md`](docs/moonbit-packaging.md)). `check` refuses with `CSW0001`
until Step 3 lands.

## Getting started

Everything is pinned through Nix, so no toolchain is installed system-wide.

```bash
nix develop                 # dotnet 9.0.317, node 24.19.0, moonbit 0.10.11, wasm-tools
dotnet build
dotnet test
dotnet run --project src/Driver -- --version
dotnet run --project src/Driver -- toolchain      # verify the local toolchain against the pin
tools/moonbit-smoke.sh                            # pinned moonc really emits a WasmGC module
```

`direnv allow` enters the shell automatically if you use direnv.

The first `nix develop` downloads the ~94 MB MoonBit archive and copies a writable
toolchain into `.moon-home/`, because `moon` needs to write caches below `MOON_HOME`.
Both are reproducible from the pin and are git-ignored.

## Toolchain pinning

[`tools/toolchain.json`](tools/toolchain.json) is the single source of truth. `flake.nix`
reads it, the driver embeds it, and CI checks both. Three things fail loudly rather than
drifting:

- a nixpkgs bump that moves .NET, Node or wasm-tools fails flake evaluation;
- an upstream MoonBit archive whose contents changed fails the fixed-output hash;
- a `moonc` on `PATH` that is not the pinned build fails `cswasm toolchain` with `CSW5002`.

Upgrading MoonBit is a reviewed baseline change: run `tools/update-toolchain.sh <version>`,
then re-run the benchmarks before merging.

## Layout

```
src/Driver             CLI, orchestration
src/Common             diagnostics, the embedded toolchain pin
src/Backend.MoonBit    MoonBit toolchain integration; all MoonBit-specific code lives here
tests/CsWasm.Tests     unit tests
tests/Spike            .NET vs wasm differential run on the pinned Node host
tools/                 toolchain pin, smoke and re-pin scripts
nix/                   the pinned MoonBit derivation
```

`src/Frontend.Cil`, `src/IR`, `src/Analysis`, `src/Lowering` and `src/Runtime` arrive with
the steps that need them; empty placeholder projects are deliberately not created.

## Licence

MIT, see [LICENSE](LICENSE). The MoonBit toolchain is fetched from upstream at its own
licence and is never redistributed by this repository.
