# CsWasm

A Wasm-native C# compiler that consumes existing .NET assemblies and emits small WasmGC modules via a MoonBit backend.

## Language

### Pipeline

**CsIR**:
The single owned semantic IR: SSA-like, typed, preserves managed-language distinctions (managed ref, byref, box, interface call) that C#/IL would otherwise lose before reaching a backend.
_Avoid_: "the IR", internal SSA, lowered form (that is *Lowered CsIR*)

**Lowered CsIR**:
CsIR after whole-program passes have resolved .NET-semantic decisions (devirtualization, generic specialization, BCL intrinsic substitution). What a backend receives.

**Frontend**:
A reader that produces CsIR from an input form. Two exist: the CIL frontend (ECMA-335 DLLs) and the Roslyn frontend (C# source).

**Backend**:
A consumer of Lowered CsIR that produces a `.wasm`. Initially only the MoonBit backend.

**Emitter**:
The part of a backend that writes target source/binary from CsIR. The MoonBit emitter writes MoonBit source.

**Toolchain**:
The external, unmodified, version-pinned MoonBit compiler (`moon`/`moonc`) invoked by the MoonBit backend.

### Compatibility

**Classifier**:
The analysis that assigns a Compatibility Level to each API use and rolls it up per assembly. Exposed as `cswasm check`.

**Compatibility Level**:
One of four verdicts the Classifier assigns to a package or API use.

- **Compatible**: pure managed, closed-world safe, or host API that CsWasm already implements. No size cost beyond the code itself.
- **Compatible with fallback**: works only with extra generated metadata or a runtime shim (e.g. reflection metadata, Task shim), at explicit size cost.
- **AOT-incompatible**: requires runtime code generation or dynamic loading (Reflection.Emit, Assembly.Load(byte[])). Can never be supported.
- **Platform-incompatible**: depends on an OS/host facility that does not exist in Wasm or that CsWasm has no host binding for (Windows Registry, unsupported P/Invoke, unimplemented host API).

_Avoid_: the A/B/C/D letters from the original architecture draft; "host-dependent" as a level (it is folded into Compatible or Platform-incompatible depending on whether a host binding exists).

**Host binding**:
CsWasm-provided implementation of a .NET API on top of a host (JS/browser, Node, WASI) import.

**Profile**:
A named set of supported features and host bindings the compiler targets (Core, AOT NuGet, Compatibility). Distinct from Host profile (browser/Node/WASI).

**Risk signal**:
An intermediate classification the Classifier derives from ILLink/AOT warnings before deciding a Compatibility Level. `TrimRisk` (IL2xxx: member may be trimmed) and `AotRisk` (IL3xxx: needs runtime codegen or unsupported AOT feature). Warning codes never map 1:1 to a level; the Classifier combines Risk signals with CsWasm host-binding knowledge.

**Whole-program analysis**:
CsWasm's own thin closed-world pass, run after ILLink, that collects only what codegen needs: closed generic instances, virtual/interface call targets, delegate targets. Not a DCE.
_Avoid_: reachability (that is ILLink's job), trimmer

### Memory model

**Storage class**:
Where a `ByRef<T>` points. Exactly four: `Local(slot)`, `Field(object, fieldId)`, `ArrayElem(array, index)`, `Linear(address)`. A CsIR-level semantic notion, not a runtime representation.

**ByRef<T>**:
CsIR type for a C# `ref T`/interior reference, always carrying a Storage class. Lowered to a direct local/struct/array/linear access when the class is statically known.

**RuntimeByRef<T>**:
The fat runtime representation (tagged union over Storage classes) used only when the Storage class cannot be determined statically (control-flow merge, delegate/indirect passing).
_Avoid_: fat pointer (ambiguous between CsIR and runtime)

**Substitution**:
Replacing a .NET method or type with a CsWasm-provided implementation (MoonBit source or intrinsic) instead of translating its IL. Decided by a substitution table, keyed on .NET semantic identity.

**Substitution core**:
The set of CoreLib types always substituted regardless of measurement: String, Array, Span/ReadOnlySpan/Memory, Unsafe, MemoryMarshal, List<T>. Their IL is never translated.

**Intrinsic**:
A Substitution that disappears into a single CsIR/backend primitive (e.g. `Math.Sqrt` → `sqrt.f64`). Every Intrinsic is a Substitution; not every Substitution is an Intrinsic.

### Runtime

**Scheduler**:
The CsWasm single-thread cooperative event loop that queues async continuations. The reference implementation for Task semantics; JSPI suspend/resume is a possible future fast path, never the baseline.
_Avoid_: thread pool, event loop (ambiguous with the host's)

**Threads**:
Not supported. `Task.Run` queues to the Scheduler (Compatible with fallback, warned); blocking/real-thread APIs are Platform-incompatible; `lock`/Monitor implement single-thread monitor semantics (uncontended and recursive acquisition only).

### Host

**Host profile**:
A capability matrix (WasmGC, js-string builtins, externref, `moonbit:ffi.make_closure`, DOM, WASI imports) describing an execution environment. Named profiles: Node, Browser, WASI. Never an assumption keyed on engine name.

**HostRef**:
CsIR type for an opaque host object (`JSObject`). Carries identity, nullability, lifetime/rooting, disposal and callback re-entry rules; maps to `externref`.

**Interop signature**:
The unit at which `[JSImport]`/`[JSExport]` compatibility is judged: full parameter/return types plus `JSMarshalAs` mode. Attribute recognition alone never implies Compatible.
