# MoonBit packaging

How cswasm turns generated MoonBit into a `.wasm`, and what the pinned MoonBit toolchain
requires of the source it is given.

MoonBit is a replaceable external toolchain (`docs/architecture.md` section 9.4): the
generated package is written to a temporary directory, only the pinned `moon` is started, and
nothing here is meant to be hand-edited. Everything on this page is stated once, and the code
that implements it lives in one place per item, so that a MoonBit release that changes any of
it changes one file (`docs/architecture.md` section 9.5).

Verified against the pin in `tools/toolchain.json`: `moon 0.1.20260827`,
`moonc v0.10.11+6ff76a5f9`.

## The generated package

```
<temp>/
  moon.mod.json          { "name": "cswasm/generated", "version": …, "source": "src" }
  src/gen/moon.pkg.json  the link settings below
  src/gen/gen.mbt        the emitted source
```

Owner: `src/Backend.MoonBit/Emitter/MoonBitPackageLayout.cs`. It is a pure value - a list of
relative paths and their text - so the layout can be checked without running a build.

## Exporting a function to wasm-gc

Two things are needed, and a function that has only one of them is silently dropped from the
module by dead-code elimination:

1. the package names it in the `link` setting of the target it is built for:

   ```json
   { "link": { "wasm-gc": { "exports": ["__cs_Sum_9bb3700577fffb43"] } } }
   ```

2. the function is `pub` in the generated source.

Both are decided from the same list of exported names: the manifest is written by
`MoonBitPackageLayout` and the `pub` by `MoonBitFunctionEmitter`, from the
`MoonBitModule.Exports` the emitter produced.

cswasm exports the static methods it lowered. An instance method needs a receiver a host has
no way to construct in this step, so it is generated but not exported.

Observed with the pin: an export whose parameter is `Array[<struct>]` is accepted, and the
WasmGC struct and array types it uses survive into the module. Without the export, moonc
emits a module with no types and no functions at all (144 bytes for the proof-of-concept
sample), which is why the export is what makes the completion condition of issue #17
reachable.

## Building

```
moon -C <package> build --target wasm-gc --release
```

Owner: `src/Backend.MoonBit/MoonBitBuild.cs`, which exposes the argument list as one value so
that what a test observes is what a build starts. Both output streams are collected; a
non-zero exit is reported as `CSW5004` with the raw transcript attached and a location inside
the generated MoonBit (`docs/diagnostics.md` rule 2).

`moon` leaves the module at `_build/wasm-gc/release/build/gen/gen.wasm`. That path is not part
of MoonBit's contract and has moved between releases, so it is a starting point: if it is not
there, the package is searched for a `.wasm`, the same way `tools/moonbit-smoke.sh` does it.

## What the pinned toolchain requires of generated identifiers

These are parse-time rules of the pinned `moonc`. They are why the generated spelling is what
it is (`src/Backend.MoonBit/Emitter/MoonBitNames.cs`).

| Rule | Rejected as | Generated spelling |
| --- | --- | --- |
| A type name starts with an upper-case ASCII letter | `[3002] Expected upper case identifier for type name` | `Cs_<short name>_<hash>` |
| A struct field name starts with a lower-case letter or `_` | `[3002] Expected lower case identifier for struct field name` | the C# name when it already qualifies, otherwise `__cs_<short name>_<hash>` |
| The values a `loop` carries are one parenthesised group | `[3002] unexpected token ','` | `loop (a, b) { (x, y) => … continue (p, q) … }` |

A C# field named `X` therefore cannot be carried over verbatim. Lower-casing its first letter
instead would map the C# pair `X` and `x` onto one field, so the generated name is used: it is
decided by the field's own identity, cannot collide, and still reads back to the C# name.

The pinned `moonc` reports unused values, unused types, never-constructed structs and the
deprecated `loop` syntax as **warnings**, including under `--release`; they do not fail the
build. The generated source produces several of them for the proof-of-concept sample.

## Diagnostic locations

A `CSW5004` location is `<generated .mbt>:<line>`, taken from the first position the toolchain
reported. When it reported none, or one outside the generated source, the generated file is
named without a line: a location a reader cannot open is worse than a coarse one. Rewriting
these back to a C# position is Step 8.

## Intermediates

The package is removed on every way out of a run - a module, a rejected build, an exception -
unless `--keep-intermediates` was passed, in which case the path is printed so the user can
open it.

A kill signal is the one end no process can clean up after. The package is created below the
system temporary directory precisely so that residue is where the operating system already
cleans up; cswasm grows no separate reaper for it.
