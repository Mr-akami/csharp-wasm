# Diagnostics

Every message cswasm addresses to a user carries a `CSWnnnn` code. The code is the
contract: text may be reworded, codes are allocated once and never reused or renumbered.
CI, package authors and the compatibility Classifier all filter on codes.

## Bands

| Range     | Category         | Owns                                                                 |
| --------- | ---------------- | -------------------------------------------------------------------- |
| `CSW0xxx` | Driver           | CLI parsing, project loading, orchestration                          |
| `CSW1xxx` | Frontend         | C#/IL semantics we cannot represent, malformed IL                    |
| `CSW2xxx` | Compatibility    | NuGet and BCL compatibility, Classifier verdicts, Risk signals        |
| `CSW3xxx` | Whole-program    | Closed-world AOT violations, reflection roots, generic explosion      |
| `CSW4xxx` | Backend mapping  | CsIR constructs the MoonBit backend cannot lower                      |
| `CSW5xxx` | Toolchain        | The external MoonBit compiler: missing, wrong version, failed run     |
| `CSW6xxx` | Host ABI         | Interop signatures, host capability mismatches, JS marshalling        |

The band is the leading digit, so `DiagnosticCode.Category` is derived, never stored.

## Allocated

| Code      | Severity | Meaning                                                        |
| --------- | -------- | -------------------------------------------------------------- |
| `CSW0001` | error    | Command is planned but not implemented in this step             |
| `CSW0002` | error    | Unknown command or option                                       |
| `CSW0003` | error    | Input file named on the command line does not exist             |
| `CSW1001` | error    | A CIL instruction is outside the supported instruction set       |
| `CSW1002` | error    | A metadata construct is outside the subset the frontend models   |
| `CSW1003` | error    | A local has its address taken and cannot become an SSA value     |
| `CSW1004` | error    | A method body cannot be normalised into SSA                      |
| `CSW1005` | warning  | Implicit exception checks are not inserted for a method          |
| `CSW4001` | error    | An SSA construct the MoonBit backend cannot lower                |
| `CSW5001` | error    | A required MoonBit executable was not found                     |
| `CSW5002` | error    | A MoonBit executable does not match `tools/toolchain.json`      |
| `CSW5003` | error    | A toolchain executable could not be run while being probed      |
| `CSW5004` | error    | The MoonBit compiler rejected the generated package             |

## Rules

1. **Never fail silently and never half-succeed.** A command that a later step will
   implement reports `CSW0001` and exits non-zero rather than emitting partial output.
2. **Point at C#, not at generated MoonBit.** When a failure originates in C# semantics,
   the location must be a C# (or IL) location. Rewriting `moonc` diagnostics back through
   the MoonBit -> CsIR -> C# maps is Step 8 work; until then, backend errors are reported
   as `CSW5xxx` with the raw toolchain output attached.
3. **Say why a package is incompatible, not just that it is.** `CSW2xxx` messages name the
   API that forced the verdict.
4. **Help text is actionable.** The `help:` line tells the user what to do, not what happened.
