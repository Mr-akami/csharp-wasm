---
status: accepted
date: 2026-09-05
---
# Translate real CoreLib IL, with a fixed Substitution core

The draft proposed a hand-written tiny managed library (List<T>, Dictionary, LINQ subset). We instead translate ILLink-trimmed `System.Private.CoreLib` and other BCL assemblies through the normal CsIR pipeline, and substitute only a fixed **Substitution core**: String, Array, Span/ReadOnlySpan/Memory, Unsafe, MemoryMarshal, List<T>, and the Task/async machinery (Task, ValueTask, AsyncTaskMethodBuilder, awaiters, SynchronizationContext, CancellationToken). Getting CoreLib *internals* to translate unmodified is explicitly not a compatibility goal.

Rationale: the project prioritizes breadth of .NET compatibility over size/perf; NativeAOT, Blazor and NativeAOT-LLVM all take this route with real-world results.

## Considered options
- A: hand-written tiny BCL — smallest output, but every API is permanent maintenance and breadth stays narrow.
- B (chosen): real IL + Substitution core.

## Consequences
- Output is larger and hot paths slower than option A.
- **Size-reduction headroom is retained by design:** the Substitution table can be widened later, moving hot or bloated CoreLib types to hand-written MoonBit one at a time, guided by size attribution from the benchmark system. Every widening must be justified by measurement, never by architecture preference.
