---
status: accepted
date: 2026-09-05
---
# ByRef<T> carries a Storage class in CsIR but is not always materialized at runtime

WasmGC has no addresses for fields or array elements, so C# `ref T`/`Span<T>` cannot be a raw pointer. CsIR models `ByRef<T>` with exactly four Storage classes — `Local(slot) | Field(object, fieldId) | ArrayElem(array, index) | Linear(address)` — from the first version. When the class is statically known at lowering, it becomes a direct local / struct.get,set / array.get,set / linear load-store. Only when it cannot be determined (control-flow merge, delegate/indirect passing) is a fat `RuntimeByRef<T>` tagged union emitted. `Span<T>` is `{ ByRef<T> start; nint length }` with `IsByRefLike` preserved; `stackalloc`/native allocation lowers to Linear.

Rationale: the fat-pointer idea is right as *semantics* and wrong as a *mandatory representation*. Keeping the distinction gives broad compatibility now (CoreLib Span-heavy code translates) and lets a future direct WasmGC backend consume CsIR unchanged.

## Consequences
- Unsafe/MemoryMarshal are judged per Storage class, not per API: `Unsafe.Add` on ArrayElem or `MemoryMarshal.Cast` between Linear spans are supported when semantics are preserved.
- `fixed` over managed objects, arbitrary bit reinterpretation, and exposing a managed address as a linear pointer are Unsupported (Platform-incompatible); a copy-in/copy-out fallback may be considered later.
