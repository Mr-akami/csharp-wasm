> **Note (2026-09-05):** Original architecture draft, committed verbatim. Where later decisions override it, see `docs/adr/` and `CONTEXT.md`. Notably: ILLink is used as the reachability pre-pass (ADR-0001), real CoreLib IL is translated with a Substitution core (ADR-0002), ByRef storage classes are CsIR semantics not runtime representation (ADR-0003), compatibility levels are the four in CONTEXT.md (not A/B/C/D).

# C# → WebAssembly Compiler Architecture

**Status:** Architecture proposal / ADR-style design draft  
**Primary goal:** Preserve the C#/.NET development ecosystem while producing WebAssembly with MoonBit-class code size, startup cost, GC behavior, and host interoperability.  
**Initial backend:** MoonBit / WasmGC  
**Future backends:** Direct WasmGC and LLVM, only when they provide measurable value.  

---

## 1. Executive summary

The recommended architecture is:

```text
                 C# application source
                         |
              Roslyn semantic frontend
                         |
                         +-------------------+
                                             |
                    NuGet / DLL assemblies   |
                         |                   |
                   ECMA-335 reader           |
                         |                   |
                         +---------+---------+
                                   |
                                   v
                                 CsIR
                    C# managed semantic IR
                                   |
                     whole-program lowering
                                   |
                      BCL / runtime mapping
                                   |
                  +----------------+----------------+
                  |                |                |
                  v                v                v
            MoonBit emitter   Direct WasmGC      LLVM
              (initial)         (future)        (future)
                  |                |                |
                  v                |                |
            stock `moonc`          |                |
                  |                |                |
                  +----------------+----------------+
                                   |
                                   v
                                WebAssembly
```

The core architectural decision is **not** to create a large backend framework. Instead:

1. **CsIR is the single owned semantic boundary.**
2. **MoonBit is the first and default code generator.**
3. The first implementation may be very direct: IL/Roslyn → a small SSA-like representation → MoonBit.
4. CsIR grows only when a real C#/.NET semantic distinction must survive lowering.
5. A second backend is added only when a benchmark demonstrates that MoonBit cannot provide an important capability or target characteristic.
6. LLVM is treated primarily as a **low-level optimizer/backend for value, numeric, SIMD, unsafe, and linear-memory-oriented code**, not as the primary representation of C# semantics.
7. A direct WasmGC backend is the likely long-term route for managed objects, arrays, closures, host references, and other Wasm-native managed constructs if control over MoonBit code generation eventually becomes necessary.

This is intentionally similar in spirit to **mold**: solve the real common case directly, avoid designing an elaborate extensibility system for hypothetical users, and duplicate a small amount of straightforward backend code rather than introducing abstraction layers that make the main path harder to understand.

---

## 2. Product objective

The project is not intended to be another implementation of the complete .NET runtime.

The target product is better described as:

> **A Wasm-native C# implementation that consumes the existing C#/.NET ecosystem where practical.**

The desired user experience is approximately:

```bash
dotnet add package Some.AotFriendly.Package
cswasm build MyApp.csproj
```

producing:

```text
app.wasm
app.js          # only when host glue is required
app.d.ts        # optional TypeScript bindings
```

without shipping Mono, CoreCLR, an IL interpreter, a managed GC implementation, or the majority of `System.Private.CoreLib`.

The design optimizes for:

- small Wasm binaries;
- fast startup;
- WasmGC-managed objects instead of a second GC heap inside linear memory;
- good browser/JS interoperability;
- compatibility with useful portions of NuGet;
- normal C# language tooling;
- understandable compiler architecture;
- the ability to replace the initial MoonBit backend without rewriting the frontend.

It explicitly does **not** optimize initially for:

- 100% CLR behavioral compatibility;
- runtime assembly loading;
- `Reflection.Emit`;
- arbitrary P/Invoke;
- AppDomain-like functionality;
- dynamic code generation;
- every corner of unsafe/reflection-heavy .NET software.

---

## 3. Architectural principles

### 3.1 CsIR is a semantic firewall, not a framework

CsIR exists to preserve information that would otherwise be lost when converting C# or IL into MoonBit, LLVM IR, or raw Wasm.

It should **not** become a universal compiler framework.

A useful test for adding something to CsIR is:

> Will two plausible backends need to distinguish this concept, or do we need it for whole-program C#/.NET analysis?

If the answer is no, keep the concept out of CsIR.

Examples that belong in CsIR:

```text
ManagedRef<User>
ManagedArray<T>
ValueType<Vector3>
ByRef<T>
InterfaceCall
VirtualCall
Box / Unbox
GenericInstance
Delegate
HostRef
Exception region
```

Examples that normally should not:

```text
MoonBit-specific struct representation
Wasm type indices
LLVM basic-block metadata
Binaryen-specific nodes
browser-specific JS snippets
```

### 3.2 Prefer concrete implementations over premature backend abstraction

Do **not** start with this:

```csharp
interface IBackend
{
    void EmitClass(...);
    void EmitInterface(...);
    void EmitDelegate(...);
    void EmitAsync(...);
    void EmitException(...);
    // hundreds more
}
```

That interface would merely freeze assumptions before enough is known.

Instead start with:

```text
MoonBitEmitter
```

that directly walks CsIR.

If and when DirectWasmEmitter appears, factor out only the truly shared pieces discovered from those two implementations.

If LLVM becomes a third backend, repeat the same process.

A little duplication across two or three emitters is cheaper than maintaining a complicated abstraction hierarchy that all backends continuously work around.

### 3.3 Closed-world compilation is the default

The compiler should assume that the final set of reachable assemblies and types is known at build time.

That enables:

- dead-code elimination;
- generic specialization;
- devirtualization;
- interface implementation analysis;
- reflection metadata elimination;
- static BCL substitution;
- static interop binding;
- removal of unused runtime helpers.

This is one of the fundamental reasons the output can be much smaller than general-purpose .NET runtime deployment.

### 3.4 Backend selection is primarily project-level, not per-expression

The architecture should allow multiple backends, but the default should **not** dynamically split every method among MoonBit, LLVM, and direct Wasm.

That would introduce ABI, optimization, debugging, linking, and ownership complexity far too early.

Initial model:

```text
one compilation -> one primary backend
```

Later, a narrowly scoped secondary route may be added for clearly valuable cases such as:

```text
[WasmNative]
unsafe numeric kernel -> LLVM / direct linear-memory Wasm
```

but only after profiling proves the need.

---

## 4. Why MoonBit is the initial backend

MoonBit currently has explicit WebAssembly and WebAssembly GC backends. Its WasmGC model represents managed data with WebAssembly GC reference types such as `struct` and `array`, and its documentation states that linear memory is not used by default for those data structures. Its WasmGC backend reuses the host/runtime GC rather than implementing an independent managed heap.

Those properties line up almost exactly with this project's goals.

MoonBit also already handles compiler responsibilities we otherwise would need to build early:

- WasmGC type generation;
- closure representation;
- aggregate layout;
- generic lowering/specialization within its own language model;
- dead-code elimination and other optimization passes;
- Wasm binary emission;
- host-reference/FFI mechanics;
- JS string builtin integration where enabled.

Therefore MoonBit offers something more valuable than merely “another source language”:

> It is a pre-existing, managed-language-oriented WasmGC code generator and optimizer.

### 4.1 Why use MoonBit source rather than its internal IR initially

Directly targeting MoonBit's internal compiler IR looks attractive, but it creates several problems:

- internal IR is not a stable public compiler ABI;
- MoonBit states that the compiler is evolving rapidly;
- important passes happen before its final Wasm-oriented IR;
- generic specialization, layout work, async lowering, and other language passes may be bypassed if we inject too late;
- source generation works with the official compiler without maintaining a compiler fork;
- the MoonBit compiler license deserves careful separation from a commercial compiler implementation.

Therefore the initial integration boundary should be:

```text
CsIR
  |
  v
Generated MoonBit source/package
  |
  v
Unmodified official MoonBit toolchain
```

This makes MoonBit a replaceable toolchain dependency rather than part of our compiler's internal object model.

### 4.2 Generated source is not the permanent IR

Generated MoonBit code is an **output form**, not the canonical representation.

Never let MoonBit syntax leak backward into analysis passes.

Bad:

```text
CsIR optimizer checks MoonBit syntax constraints
```

Good:

```text
CsIR optimizer knows C# semantics
MoonBitEmitter handles MoonBit constraints
```

That separation is what preserves a future direct-Wasm or LLVM route.

---

## 5. Frontends

There are two input paths because application source and NuGet binaries contain different information.

### 5.1 Source frontend: Roslyn

When source is available, consume Roslyn semantic information before lowering everything to ordinary IL.

This is particularly useful for constructs that become mechanically complicated after Roslyn lowering:

- `async` / `await`;
- lambdas and captures;
- pattern matching;
- nullable annotations;
- source-level generic constraints;
- syntax-associated diagnostics;
- source mapping.

The Roslyn frontend should not duplicate the C# compiler. Roslyn remains responsible for parsing, binding, overload resolution, inference, and language legality.

The frontend's job is approximately:

```text
Roslyn IOperation / symbols
        |
        v
normalized CsIR
```

### 5.2 Binary frontend: ECMA-335 IL + metadata

NuGet compatibility requires consuming compiled assemblies.

The binary frontend should read:

- CIL method bodies;
- metadata tables;
- type definitions;
- generic signatures;
- custom attributes;
- assembly references;
- exception regions;
- method specifications;
- interface implementation metadata.

Candidate implementation technologies include `System.Reflection.Metadata` or another small ECMA-335 reader. Avoid loading arbitrary assemblies into the host runtime merely to inspect them.

The IL stack machine should be normalized early into explicit values.

IL:

```text
ldarg.0
ldfld int32 User::Age
ldc.i4.1
add
ret
```

becomes something like:

```text
%0 : ManagedRef<User> = arg 0
%1 : i32 = field.get %0, User.Age
%2 : i32 = const 1
%3 : i32 = add %1, %2
return %3
```

This makes data flow and optimization much easier than continuously simulating the CIL evaluation stack.

### 5.3 Both paths converge quickly

The two frontends should converge before most optimization and backend work:

```text
         App source                    NuGet DLL
             |                            |
           Roslyn                       CIL reader
             |                            |
             +-------------+--------------+
                           |
                           v
                          CsIR
```

Do not maintain parallel compiler pipelines beyond what is required to recover source-only semantic information.

---

## 6. CsIR design

### 6.1 What CsIR is

CsIR is an SSA-like, typed, managed-language IR.

It is lower level than C#, but higher level than LLVM IR and Wasm.

It should preserve the distinctions required to map CLR/C# semantics efficiently onto a Wasm-native runtime model.

A useful mental model is:

```text
        C# AST / Roslyn
               |
          source details
               |
               v
             CsIR
     managed semantic layer
               |
      target-independent lowering
               |
               v
   MoonBit / WasmGC / LLVM
```

### 6.2 Type system

Start small.

Suggested initial type families:

```text
CsType
├── Void
├── Primitive
│   ├── I32
│   ├── I64
│   ├── F32
│   ├── F64
│   └── Bool
├── ManagedRef(TypeId, Nullability)
├── ManagedArray(ElementType)
├── String
├── ValueType(TypeId)
├── InterfaceRef(TypeId)
├── Delegate(SignatureId)
├── GenericParameter(Id)
├── GenericInstance(TypeId, Args)
├── ByRef(T)
├── Pointer(T)
├── Span(T)
└── HostRef(HostTypeId)
```

Do not introduce more categories until a real semantic distinction requires one.

### 6.3 Core operations

A reasonable initial operation set:

```text
Control flow
  branch
  cond_branch
  return
  phi/block parameters

Values
  const
  convert
  compare
  arithmetic

Managed objects
  new.object
  field.get
  field.set
  type.test
  cast

Arrays
  new.array
  array.get
  array.set
  array.length

Calls
  call.direct
  call.virtual
  call.interface
  call.indirect

Managed semantics
  box
  unbox
  delegate.new
  delegate.invoke
  throw

Memory semantics
  byref.field
  byref.array
  load
  store

Host
  host.import
  host.call
  host.export
```

### 6.4 What not to model initially

Do not initially create dedicated high-level CsIR nodes for every C# construct.

For example, ordinary `foreach`, `using`, switch expressions, records, pattern matching, and many lambda forms can be normalized by Roslyn/frontend lowering into smaller primitives.

Keep high-level constructs only when preserving them creates material backend value.

`async` is a likely example, but it should be added based on prototype results rather than assumed upfront.

### 6.5 Async strategy

Source async should initially remain recognizable long enough to choose an efficient lowering.

Two options should be benchmarked:

**A. Emit high-level MoonBit async**

```text
Roslyn async operation
        |
        v
CsIR async region
        |
        v
MoonBit async
        |
        v
MoonBit's own lowering
```

**B. Lower in CsIR to a minimal continuation/state machine**

```text
Roslyn async operation
        |
        v
CsIR state machine
        |
        v
ordinary MoonBit functions/closures
```

For NuGet binaries, async IL may already be a generated .NET state machine. Recognize common Roslyn patterns using attributes and generated-type metadata. Recovery to high-level async is optional; correctness fallback is to compile the state machine semantics with a small Task compatibility layer.

Do not require perfect async decompilation for NuGet support.

---

## 7. Whole-program layer

The compiler should perform one explicit whole-program stage after loading application code and reachable NuGet assemblies.

This is one of the most important layers in the architecture.

Responsibilities:

### Reachability

Find reachable:

- methods;
- types;
- static constructors;
- generic instantiations;
- runtime helpers;
- reflection roots;
- exported entry points.

### Generic specialization

Prefer monomorphization for the Wasm-native profile:

```text
List<int>  -> specialization A
List<User> -> specialization B
```

Avoid recreating the CLR's fully general runtime generic machinery unless measurements show it is necessary.

### Devirtualization

Given:

```text
IFoo.Run
```

and the closed world contains only one reachable implementation:

```text
Foo.Run
```

replace interface dispatch with a direct call.

For small multiple implementation sets, retain enough information for the backend to choose a compact dispatch scheme.

### Reflection rooting

Reflection metadata is opt-in or statically inferred.

The default build should not preserve complete CLR metadata.

Possible policy:

```csharp
[WasmReflect]
public sealed class User { ... }
```

or compiler-analyzed static reflection use.

### BCL substitution

Calls should be recognized semantically, not based on generated MoonBit code.

Examples:

```text
System.Math.Sqrt
    -> CsIntrinsic.SqrtF64

System.String.Length
    -> CsIntrinsic.StringLength

System.Array.Length
    -> CsIntrinsic.ArrayLength
```

The backend then chooses the target implementation.

---

## 8. Runtime / BCL compatibility strategy

NuGet compatibility is a core product requirement, but NuGet compatibility does not imply shipping the CLR.

The project needs a small **compatibility surface** consisting of three categories.

### 8.1 Intrinsics

Operations that map naturally to Wasm or backend primitives:

```text
System.Math
primitive conversions
array length
selected string operations
bit operations
some Interlocked operations where supported
```

These should disappear during compilation.

### 8.2 Tiny managed library

Data structures and helpers where a real implementation is useful:

```text
List<T>
Dictionary<TKey,TValue>
HashSet<T>
Task<T> compatibility primitives
StringBuilder subset
selected LINQ operators
```

Implement these against the CsIR/Wasm-native object model, not by copying CoreCLR internals.

### 8.3 Unsupported/runtime-dynamic APIs

Initially reject or gate APIs such as:

```text
Reflection.Emit
Assembly.Load from arbitrary bytes
runtime code generation
AppDomain-style loading
unsupported native P/Invoke
```

Diagnostics should clearly explain why a package is incompatible.

### 8.4 Package compatibility classification

Expose compatibility levels to users and CI:

```text
A: Wasm-native compatible
   pure managed, closed-world safe, no unsupported runtime features

B: Compatible with metadata/runtime helpers
   reflection or Task/BCL shims required

C: Host-dependent
   browser/Node/WASI-specific binding required

D: Unsupported
   runtime code generation, unsupported native dependency, etc.
```

Leverage existing `.NET` AOT/trimming annotations where possible rather than inventing parallel ecosystem metadata. Packages already designed for Native AOT and trimming are natural early targets.

---

## 9. MoonBit backend design

### 9.1 Keep it boring

The first backend should be one module/package that walks CsIR and writes MoonBit source plus minimal package metadata.

Conceptually:

```text
MoonBitEmitter
├── emit_type
├── emit_function
├── emit_instruction
├── emit_runtime_helper
└── emit_package
```

No generic backend visitor hierarchy is needed.

### 9.2 Stable naming

Generated identifiers should be deterministic and independent of source filenames where possible.

Example:

```text
Cs type: System.Collections.Generic.List<System.Int32>
Generated: __cs_List_i32_<stable-hash>
```

Stable naming helps:

- incremental compilation;
- reproducible output;
- diagnostics;
- source maps;
- binary-size diffing.

### 9.3 Generated code is disposable

Generated MoonBit should be readable enough for debugging, but do not optimize the architecture for hand-editing it.

A build cache may persist it, but it is always reproducible from CsIR.

### 9.4 Toolchain invocation

Treat MoonBit as an external compiler executable/toolchain.

```text
CsWasm compiler
    |
    +-> generated MoonBit package in temp/cache directory
    |
    +-> execute pinned official moon/moonc version
    |
    +-> collect wasm + diagnostics
```

Pin the supported MoonBit version in the toolchain manifest. Do not silently use an arbitrary globally installed version.

### 9.5 Version adapter

Because MoonBit is evolving quickly, isolate MoonBit-specific compatibility in a tiny area:

```text
backend/moonbit/
  emitter.*
  toolchain.*
  version.*
```

When syntax or CLI behavior changes, only this directory should normally change.

---

## 10. Direct WasmGC backend: future role

A direct backend should be considered only when measurements show one or more of these:

- generated MoonBit introduces unavoidable size overhead;
- source translation prevents important C# semantic optimizations;
- host interop needs finer ABI control;
- debugging/source maps require a direct mapping;
- MoonBit toolchain startup dominates incremental builds;
- required Wasm features are inaccessible from MoonBit;
- MoonBit licensing/toolchain distribution creates unacceptable product constraints.

The direct backend would naturally own:

```text
ManagedRef<T>       -> WasmGC `(ref $T)`
ManagedArray<T>     -> WasmGC arrays
HostRef<T>          -> externref
Delegate            -> funcref + environment representation
ValueType           -> locals / multivalue / GC struct depending on escape
String              -> configured string representation
```

This backend should directly consume **lowered CsIR**.

Do not force MoonBit's runtime representation into CsIR merely to make migration easy.

---

## 11. LLVM backend: future role

LLVM is valuable, but it should not become the semantic foundation of the managed compiler.

LLVM is especially attractive for:

- numeric kernels;
- vectorization;
- SIMD;
- linear-memory code;
- unsafe code;
- `Span<T>`-heavy algorithms;
- DSP/image/compression/crypto kernels;
- native targets if they are added later.

LLVM IR is less suitable as the earliest universal IR because C# concepts such as managed references, interfaces, boxing, delegates, and GC object identity should not be prematurely collapsed into generic pointers and structs.

The likely future path is:

```text
CsIR
  |
  +-> managed/WasmGC path
  |
  +-> LLVM lowering for selected low-level functions or whole target
```

But function-level mixing should **not** be implemented until a real benchmark justifies the additional ABI/linking complexity.

MoonBit itself now documents an experimental LLVM backend, which reinforces the general usefulness of LLVM but does not remove the need for a managed semantic layer.

---

## 12. Memory model

The architecture should explicitly distinguish two conceptual worlds.

### Managed world

```text
class
managed object
managed arrays
strings
closures/delegates
interfaces
host references
```

Preferred implementation:

```text
WasmGC / reference types
```

### Linear-memory world

```text
Span<T>
unsafe pointers
stackalloc
binary buffers requiring byte-address semantics
native/WASI ABIs
SIMD-oriented memory
```

Preferred implementation:

```text
Wasm linear memory
```

This avoids forcing every C# object into a manually managed linear-memory heap while retaining the byte-addressable semantics needed by C# low-level APIs.

The crossing rules must be explicit. In particular, avoid pretending that a WasmGC field address is an ordinary stable `T*`.

Possible policies for `ref`/`Span` over managed storage must be tested carefully; this is one of the hardest semantic areas and should not be hidden behind a vague abstraction.

---

## 13. Exceptions

Exceptions require a deliberately small design.

CsIR should preserve:

```text
throw value
try region
catch type
finally region
```

but should not encode a specific Wasm exception ABI.

The MoonBit backend maps these to the most natural MoonBit exception/error representation available for the pinned compiler version.

A future direct backend can choose Wasm exception handling where broadly usable.

Performance policy:

- exceptions are exceptional;
- no expensive runtime tables for unused exception types;
- remove unreachable handlers during whole-program analysis;
- do not use exceptions as a general control-flow mechanism in compatibility libraries.

---

## 14. JavaScript and host interoperability

Interop is a first-class design concern, not an afterthought.

Recommended source surface:

```csharp
[JSImport("document.querySelector")]
static extern JSObject QuerySelector(string selector);

[JSExport]
static int Add(int a, int b) => a + b;
```

CsIR should represent host calls abstractly:

```text
HostImport(module, name, signature)
HostExport(name, function)
HostRef<T>
```

The MoonBit backend translates these to MoonBit's FFI constructs.

A direct WasmGC backend can later map them to `externref`, `funcref`, component-model bindings, or host glue without changing frontend semantics.

Avoid copying host objects into managed wrapper graphs unless C# semantics genuinely require a wrapper object.

---

## 15. Debugging and diagnostics

A transpiling backend makes diagnostics especially important.

The user should see C# locations, not generated MoonBit locations, whenever the failure originated in C# semantics.

Pipeline diagnostics should be classified:

```text
CSW1xxx frontend / unsupported C# semantic feature
CSW2xxx NuGet / BCL compatibility
CSW3xxx whole-program/AOT violation
CSW4xxx MoonBit backend mapping
CSW5xxx external MoonBit compiler/toolchain
CSW6xxx host ABI
```

Maintain source mapping:

```text
CsIR instruction -> C# source span
Generated MoonBit range -> CsIR node
```

This also makes it possible to rewrite MoonBit compiler errors back to useful C# diagnostics.

---

## 16. Incremental build strategy

Developer experience requires builds to remain fast.

Borrow the mold mindset: the common edit-build-run path should be treated as a performance feature, not only final runtime execution.

Cache at coarse, understandable boundaries:

```text
assembly fingerprint
    -> parsed metadata/IL

source compilation fingerprint
    -> CsIR module

whole-program graph fingerprint
    -> lowered CsIR

backend fingerprint + MoonBit toolchain version
    -> generated MoonBit package
```

Do not begin with a complicated dependency graph engine. Measure first.

Possible first implementation:

```text
content hash + directory cache
```

is sufficient.

---

## 17. Repository layout

A deliberately simple initial layout:

```text
/src
  /Driver
      CLI, project loading, orchestration

  /Frontend.Roslyn
      source -> CsIR

  /Frontend.Cil
      ECMA-335 metadata + CIL -> CsIR

  /IR
      CsIR types, functions, blocks, operations

  /Analysis
      reachability
      generic instantiations
      virtual/interface targets
      metadata roots

  /Lowering
      BCL intrinsics
      runtime substitutions
      representation decisions

  /Runtime
      compatibility definitions / manifests

  /Backend.MoonBit
      CsIR -> MoonBit source
      MoonBit toolchain invocation

  /Backend.Wasm       # future, create only when implemented

  /Backend.Llvm       # future, create only when implemented

/tests
  /Conformance
  /NuGet
  /Size
  /Performance
  /Interop
```

Do not create empty future-backend projects just to demonstrate extensibility.

---

## 18. Backend API philosophy

The project should avoid a broad backend interface initially.

If a tiny seam is useful for the driver, keep it operational rather than semantic:

```csharp
public readonly record struct BackendInput(
    CsProgram Program,
    BuildOptions Options,
    string OutputDirectory);

public readonly record struct BackendResult(
    string WasmPath,
    IReadOnlyList<Diagnostic> Diagnostics);
```

Then concrete functions/classes can simply expose:

```text
MoonBitBackend.Compile(input)
DirectWasmBackend.Compile(input)   // future
LlvmBackend.Compile(input)         // future
```

The backend is free to walk CsIR however it wants.

Do not make every CsIR node call virtual backend methods.

This is the mold-like choice: a narrow top-level boundary, concrete internals, minimal policy abstraction.

---

## 19. Representation lowering

A key distinction should exist between **semantic CsIR** and **lowered CsIR**.

This need not be two unrelated IR class hierarchies. It may simply be annotations or replacement operations in the same structure.

Example:

Before lowering:

```text
%r = call.interface IFoo.Run(%obj)
```

After whole-program analysis:

```text
%r = call.direct Foo.Run(%obj)
```

Before:

```text
%x = call System.Math.Sqrt(%v)
```

After:

```text
%x = intrinsic.sqrt.f64(%v)
```

Before:

```text
new GenericInstance(List, User)
```

After:

```text
new SpecializedType(List_User)
```

The backend should receive a program where most decisions that depend on .NET semantics are already resolved.

---

## 20. What MoonBit should and should not decide

### Let MoonBit decide

Initially, MoonBit should decide low-level Wasm details such as:

- WasmGC concrete aggregate encoding;
- Wasm instruction selection;
- MoonBit closure representation;
- its internal optimizations;
- low-level layout details that do not affect C# observable behavior;
- binary emission.

### Do not let MoonBit decide

The C# compiler must decide:

- .NET method/type resolution;
- NuGet reachability;
- C# generic identity;
- virtual/interface semantic targets;
- boxing semantics;
- reflection roots;
- API compatibility;
- C# exception behavior;
- `ref` / `Span` correctness;
- which BCL operation an IL call means.

This boundary avoids making MoonBit an accidental implementation of CLR compatibility.

---

## 21. Compatibility target

The project should define compatibility in layers rather than claim “.NET compatible.”

Suggested profiles:

### Core profile

Goal: tiny Wasm and deterministic compilation.

Supports:

- primitives;
- classes/value types;
- arrays;
- interfaces;
- generics;
- delegates/lambdas;
- exceptions;
- async subset;
- collections subset;
- JS/WASI imports;
- selected BCL.

### AOT NuGet profile

Adds packages compatible with closed-world AOT assumptions.

Targets libraries that are already friendly to:

- trimming;
- Native AOT;
- source generation;
- static reflection analysis.

### Compatibility profile

Adds progressively more metadata and shim behavior at explicit size/runtime cost.

Never silently turn every build into the compatibility profile.

---

## 22. Major technical risks

### Risk 1: C# `ref` semantics do not map cleanly to WasmGC

This is likely the hardest semantic issue.

Mitigation:

- keep `ByRef<T>` explicit in CsIR;
- implement a restricted first version;
- prioritize refs into linear memory/value storage;
- reject unsupported escaping managed interior references initially;
- build semantic tests before optimization.

### Risk 2: NuGet libraries depend on more BCL behavior than expected

Mitigation:

- begin with real representative packages early;
- build package compatibility tooling;
- reuse AOT/trimming metadata;
- measure API demand before implementing large BCL areas.

### Risk 3: MoonBit source translation changes semantics or creates overhead

Mitigation:

- differential tests against .NET;
- inspect emitted Wasm;
- compare generated size/performance with hand-written MoonBit equivalents;
- set explicit thresholds for graduating to a direct backend.

### Risk 4: MoonBit changes quickly

The MoonBit compiler repository explicitly says it is evolving extremely fast.

Mitigation:

- pin a toolchain version;
- keep all MoonBit syntax/tool invocation code localized;
- test next MoonBit versions in CI before upgrading;
- never expose MoonBit details in CsIR APIs.

### Risk 5: MoonBit licensing affects distribution/commercial architecture

The current compiler repository describes the MoonBit Public License as a relaxed SSPL-style license; generated artifacts may use a user-chosen license, while modifications to the compiler have different restrictions.

Mitigation:

- use the unmodified official MoonBit compiler as an external tool initially;
- do not copy MoonBit compiler internals into the project without legal review;
- treat license review as a release gate, not an afterthought.

### Risk 6: Debugging through generated source is painful

Mitigation:

- retain C# source locations on every relevant CsIR node;
- emit deterministic generated MoonBit;
- create a MoonBit-location -> CsIR -> C# map;
- rewrite toolchain diagnostics.

---

## 23. Testing strategy

### Semantic differential tests

For every supported feature:

```text
same C# program
   |            |
 .NET          CsWasm
   |            |
 result A      result B
```

Require equivalent observable behavior.

### Wasm structural tests

Verify properties such as:

- managed classes result in WasmGC structures under the MoonBit backend;
- arrays use GC arrays where expected;
- no accidental large linear-memory GC heap appears;
- unused classes disappear;
- `System.Math` calls become Wasm operations/intrinsics where appropriate.

### Size tests

Track both raw and compressed sizes:

```text
raw wasm
brotli wasm
JS glue
runtime/helper contribution
```

Never optimize only “Hello World.”

Representative benchmarks should include:

1. numeric function;
2. class allocation;
3. interface dispatch;
4. generic collection;
5. delegate/lambda;
6. exception;
7. async host call;
8. JSON-like object graph;
9. JS interop;
10. one real NuGet library.

### Performance tests

Measure:

- startup;
- steady-state execution;
- allocation throughput;
- GC behavior;
- host-call overhead;
- compile time;
- incremental build time.

---

## 24. Success criteria for the MoonBit backend

Do not build a native backend merely because it is architecturally attractive.

Keep MoonBit as the production backend if it meets practical thresholds such as:

- generated Wasm size within roughly 10–20% of equivalent hand-written MoonBit for representative managed workloads;
- no large fixed runtime cost;
- startup appropriate for web use;
- no pathological allocation introduced by translation;
- sufficient host interop;
- acceptable compiler build latency;
- stable enough toolchain pinning.

The exact percentages should be set from benchmark data, not architecture preference.

A direct backend should be justified by concrete evidence such as:

```text
"This feature costs 180 KB because of unavoidable generated MoonBit abstraction"
```

rather than:

```text
"Owning our backend feels cleaner"
```

---

## 25. Development plan

### Phase 0 — Feasibility spike

No polished CsIR framework.

Support:

- primitives;
- functions;
- local variables;
- branches/loops;
- classes/fields;
- arrays.

Pipeline:

```text
CIL
 -> stack normalization
 -> tiny internal SSA
 -> MoonBit source
 -> moonc wasm-gc
```

Compare against equivalent hand-written MoonBit.

Deliverable: hard data proving or disproving that translation preserves MoonBit's Wasm advantages.

### Phase 1 — Formalize minimal CsIR

Promote the tiny internal SSA into explicit CsIR only after the first translation works.

Add:

- managed reference types;
- value types;
- direct calls;
- virtual/interface calls;
- exception regions;
- generics;
- delegates.

### Phase 2 — Real NuGet experiment

Pick several packages across categories:

- algorithmic pure-managed;
- collection-heavy;
- JSON/source-generated;
- async API;
- reflection-light.

Implement the minimum BCL/intrinsics required by those packages.

The goal is learning the compatibility surface, not maximizing package count.

### Phase 3 — Source-aware frontend

Add Roslyn semantic lowering for application source.

Preserve high-value source constructs such as async where doing so improves output.

### Phase 4 — Productize MoonBit backend

Add:

- pinned toolchain distribution/installation;
- cache;
- source mapping;
- diagnostic rewriting;
- TypeScript bindings;
- browser/Node/WASI host profiles.

### Phase 5 — Backend decision checkpoint

Only now decide whether a second backend is economically justified.

Evaluate:

```text
MoonBit limitations
vs
Direct WasmGC development cost
vs
LLVM value
```

### Phase 6 — Optional second backend

Preferred order based on likely project needs:

1. **Direct WasmGC** if managed code size/control is the limitation.
2. **LLVM** if numeric/unsafe/SIMD performance is the limitation.

Do not implement both unless both solve demonstrated problems.

---

## 26. Why not use LLVM as the primary IR

LLVM is mature and extremely valuable, but using LLVM IR as the first universal IR would force managed semantics to be lowered too early.

C# distinguishes:

```text
managed ref
interior byref
unmanaged pointer
interface reference
boxed value
array reference
host reference
```

A useful C# compiler must know those differences during whole-program analysis.

CsIR therefore owns them.

LLVM should receive a form where the compiler has already made the important managed-language decisions.

This also keeps open a direct WasmGC route because WebAssembly GC has explicit managed `struct` and `array` reference types whose semantics do not need to be mediated through a linear-memory pointer model.

---

## 27. Why not compile directly from IL forever

Direct IL translation is appropriate for Phase 0 and some parts of Phase 1.

But IL is a stack machine and contains already-lowered patterns that are inconvenient for optimization and alternate backends.

A normalized CsIR provides:

- explicit data flow;
- stable managed type distinctions;
- backend independence;
- place for reachability/devirtualization results;
- generic specialization representation;
- a common convergence point for Roslyn source and NuGet IL.

The rule should be:

> Create CsIR because compiler passes need it, not because architecture diagrams look cleaner with an IR box.

That keeps the design grounded.

---

## 28. Why not directly target MoonBit internal IR

Direct MoonBit-IR integration could eventually eliminate parsing and expose lower-level control, but it currently has poor tradeoffs:

- internal interfaces are not promised stable;
- compiler development is rapid;
- injecting late risks bypassing valuable MoonBit passes;
- it creates a tighter license and maintenance relationship;
- it makes upgrades more expensive;
- it weakens the clean separation needed for future native backends.

Internal MoonBit IR is still extremely valuable as an **oracle**.

Use compiler dumps and generated Wasm to study:

```text
MoonBit construct
  -> internal representation
  -> final WasmGC shape
```

and use that knowledge to improve the emitter.

---

## 29. Mold-inspired implementation rules

The following rules capture the intended engineering style.

### Rule 1: Optimize the normal path

The primary path is:

```text
C#/.NET -> CsIR -> MoonBit -> WasmGC
```

It should be easy to read from top to bottom.

### Rule 2: Do not implement generality nobody currently needs

Examples:

- no plugin backend API;
- no arbitrary pass registration framework;
- no general linker-script equivalent;
- no user-defined IR dialect system;
- no backend capability negotiation graph.

### Rule 3: Prefer tables and switches to object hierarchies

For a compiler with 2–3 backends, this is often clearer:

```text
switch backend:
  MoonBit -> compile_moonbit(program)
  Wasm    -> compile_wasm(program)
  LLVM    -> compile_llvm(program)
```

than a complex service container and backend object model.

### Rule 4: Make performance properties visible

Compiler phases should be measurable independently:

```text
frontend
whole-program analysis
lowering
MoonBit emission
moonc execution
final wasm size
```

### Rule 5: Compatibility is selective and intentional

Like mold declining to reproduce every linker-script capability, this project should not reproduce obscure CLR behavior merely because it exists.

Implement features that unlock important real workloads. Reject the rest clearly.

### Rule 6: A small amount of duplication is acceptable

If MoonBitEmitter and DirectWasmEmitter each have a 30-line mapping for primitive types, keep both until a shared abstraction is obviously simpler.

---

## 30. Open questions that require experiments

Architecture alone cannot answer these reliably.

1. **How efficiently can generated MoonBit represent C# inheritance/interface patterns?**
2. **How much of Roslyn-generated async IL can be compiled directly without importing heavyweight Task semantics?**
3. **Which AOT-friendly NuGet packages work after only a small BCL layer?**
4. **What exact semantics can be supported for `ref T` into managed objects under WasmGC?**
5. **Should C# strings use MoonBit strings, JS built-in strings, or a project-owned representation by host profile?**
6. **What is the cost of monomorphizing common .NET generic libraries?**
7. **Can generated MoonBit preserve source-level debugging sufficiently well?**
8. **At what application size does invoking moonc become a meaningful incremental-build cost?**
9. **Would method-level LLVM splitting ever beat a simpler all-MoonBit or all-direct-Wasm compilation enough to justify its ABI complexity?**

These should become benchmark projects, not speculative abstraction work.

---

## 31. Recommended first proof of concept

Build one command:

```bash
cswasm compile Sample.dll --out Sample.wasm
```

Support exactly:

```csharp
class Point
{
    public int X;
    public int Y;
}

static int Sum(Point[] points)
{
    int n = 0;
    for (int i = 0; i < points.Length; i++)
        n += points[i].X + points[i].Y;
    return n;
}
```

Pipeline:

```text
Sample.dll
 -> ECMA-335 parser
 -> stack-to-SSA
 -> minimal CsIR
 -> generated MoonBit
 -> official moonc --target wasm-gc
 -> Sample.wasm
```

Then add, one at a time:

```text
interface
closed generic
lambda/delegate
exception
async host call
one AOT-friendly NuGet package
```

For each step compare:

```text
C# translated version
vs
hand-written equivalent MoonBit
```

on:

```text
raw wasm size
brotli size
startup
execution
allocation behavior
compile time
```

That experiment will provide more architectural information than implementing a general backend framework first.

---

## 32. Final recommendation

The strongest architecture after reconsidering the problem from compiler design, Wasm semantics, .NET compatibility, toolchain maturity, build performance, maintainability, and licensing perspectives is:

```text
                +----------------------+
                |   C# / NuGet world   |
                +----------+-----------+
                           |
             Roslyn + ECMA-335 frontend
                           |
                           v
                +----------------------+
                |        CsIR          |
                | managed semantics    |
                | explicit data flow   |
                | target independent   |
                +----------+-----------+
                           |
                  whole-program passes
                           |
                           v
                +----------------------+
                |    Lowered CsIR      |
                +----------+-----------+
                           |
              initially exactly one path
                           |
                           v
                +----------------------+
                |   MoonBitEmitter     |
                +----------+-----------+
                           |
                 unmodified official
                      MoonBit compiler
                           |
                           v
                         WasmGC
```

Future evolution should be additive and evidence-driven:

```text
Lowered CsIR
    |
    +----> MoonBit              default, while it remains good enough
    |
    +----> Direct WasmGC        when control/size/debugging justifies it
    |
    +----> LLVM                 when low-level numeric/linear-memory work justifies it
```

The most important constraint is:

> **Do not design the compiler around the possibility of three backends. Design a good CsIR and one good MoonBit backend. If a second backend appears, let the real differences between the two implementations determine the abstraction.**

That provides backend independence without paying for speculative framework complexity, and it closely matches the engineering philosophy that makes tools such as mold effective: a narrow contract, concrete implementation, aggressive focus on the common path, and refusal to inherit unnecessary historical complexity.

---

## 33. External references reviewed

- MoonBit compiler repository and current project/license notes:  
  https://github.com/moonbitlang/moonbit-compiler
- MoonBit FFI/backend documentation, including WasmGC and experimental LLVM backend:  
  https://github.com/moonbitlang/moonbit-docs/blob/main/next/language/ffi.md
- MoonBit package/backend options:  
  https://github.com/moonbitlang/moonbit-docs/blob/main/next/toolchain/moon/package.md
- WebAssembly GC proposal overview (`struct`, `array`, managed references):  
  https://github.com/WebAssembly/gc/blob/main/proposals/gc/Overview.md
- mold design document:  
  https://github.com/rui314/mold/blob/main/docs/design.md
- mold user documentation and compatibility philosophy:  
  https://github.com/rui314/mold/blob/main/docs/mold.md
- LLVM project / WebAssembly backend issue tracker, used to sanity-check current backend maturity and ongoing work:  
  https://github.com/llvm/llvm-project/issues

