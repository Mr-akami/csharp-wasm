# Host profiles

A **Host profile** is a capability matrix, not an engine name. cswasm never decides what a
program may do by asking "is this Node?"; it asks which capabilities the profile declares.
This keeps browser, Node and WASI honest as they diverge, and stops assumptions about V8
leaking into the compiler.

## Capabilities

| Capability            | Meaning                                                                       |
| --------------------- | ----------------------------------------------------------------------------- |
| `wasm-gc`             | WasmGC `struct`/`array` reference types. Required; cswasm has no fallback.      |
| `js-string-builtins`  | The JS string builtins proposal. MoonBit strings cross FFI as `externref` only with this enabled. |
| `externref`           | Opaque host references, backing `HostRef`.                                     |
| `make-closure`        | Host-provided `moonbit:ffi.make_closure`, required for host-invoked callbacks.  |
| `dom`                 | Browser DOM APIs are reachable.                                                |
| `esm`                 | The module is loaded as an ES module.                                          |
| `wasi-p1` / `wasi-p2` | WASI imports are available.                                                    |
| `jspi`                | JavaScript Promise Integration. A future fast path for async, never the baseline. |

## Profiles

| Profile   | Status     | `wasm-gc` | `js-string-builtins` | `externref` | `make-closure` | `dom` | `esm` | `wasi` | `jspi` |
| --------- | ---------- | --------- | -------------------- | ----------- | -------------- | ----- | ----- | ------ | ------ |
| `node`    | Step 0     | yes       | probe                | yes         | yes            | no    | yes   | no     | probe  |
| `browser` | Step 6     | yes       | probe                | yes         | yes            | yes   | yes   | no     | probe  |
| `wasi`    | Step 8     | yes       | no                   | no          | no             | no    | no    | yes    | no     |

`probe` means the capability is detected at startup rather than assumed. Node and browser
versions differ in when they shipped the JS string builtins, and Safari has no JSPI, so a
permanent CLI flag or a hard-coded version check would be wrong in both directions.

## Consequences already decided

- **Strings.** `System.String` maps to a MoonBit `String`. That is a good fit wherever
  `js-string-builtins` is available. A host-neutral fallback representation must be designed
  before the WASI profile ships, even if it stays unimplemented (Step 8).
- **Async.** The single-thread cooperative Scheduler is the reference implementation on every
  profile. `jspi` may later shorten the Promise path; it never becomes a prerequisite.
- **Compatibility.** A .NET API that needs a host binding is `Compatible` on profiles that
  declare the capability and `Platform-incompatible` on those that do not. The Classifier
  reports the profile it judged against.
