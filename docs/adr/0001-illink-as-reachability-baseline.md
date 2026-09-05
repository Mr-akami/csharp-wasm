---
status: accepted
date: 2026-09-05
---
# ILLink is the reference implementation of .NET reachability; CsWasm does not reimplement DCE

The architecture draft assumed CsWasm would own whole-program reachability, generic-instantiation discovery, devirtualization and reflection rooting. We instead run ILLink (the .NET trimmer) as a pre-pass and feed its trimmed assemblies into the CIL frontend. CsWasm keeps only a thin whole-program analysis that collects what codegen needs: closed generic instances, virtual/interface call targets, delegate targets.

Rationale: ILLink already understands .NET semantics (trimming attributes, `DynamicallyAccessedMembers`, reflection rooting, IL2xxx/IL3xxx warnings). Reusing it buys most of NuGet compatibility for free and its warnings become Classifier input as Risk signals (never mapped 1:1 to a Compatibility Level). Double optimization with MoonBit's own DCE is accepted.

Principle: **ILLink understands .NET; CsIR understands the Wasm backend.**

## Consequences
- Original (untrimmed) metadata stays available to the frontend when needed.
- Own reachability is introduced only if ILLink becomes a bottleneck, constraint, or precision problem, and then incrementally.
