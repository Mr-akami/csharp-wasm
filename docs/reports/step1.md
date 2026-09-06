# Step 1 deviations from C# semantics

This step (issue #16) generates MoonBit source from the internal SSA. The generated code
departs from C# semantics in one respect, recorded here because a warning on stderr is gone
as soon as the run is over.

## Implicit exception checks are not inserted

C# guarantees that certain operations raise an exception rather than reading or writing
memory that does not belong to them. This step inserts no check for any of them, so the
generated MoonBit does not raise where C# would:

| Operation in C# | What C# guarantees | What this step generates |
| --------------- | ------------------ | ------------------------ |
| Reading or writing a field of a reference that holds no object | `NullReferenceException` | `o.Field`, with no check that `o` holds an object |
| Reading or writing an array element, or asking an array for its length, through a reference that holds no array | `NullReferenceException` | `a[i]` / `a.length()`, with no check that `a` holds an array |
| Reading or writing an array element outside the array | `IndexOutOfRangeException` | `a[i]`, with no bounds check |
| Integer division or remainder by zero | `DivideByZeroException` | Not reachable in this step; see below |

Inserting the explicit checks is Step 2 (issue #3).

### Division by zero

The SSA vocabulary this step lowers has no division operation. `div` and `rem` have no value
form in `src/Frontend.Cil/Ssa/SpikeSsaSteps.cs`, so a body containing one is refused as
`CSW1004` before the backend sees it. Nothing about division by zero can therefore be observed
through the generated code today; the row above records the guarantee that will have to be
honoured when a division operation is added.

## How a run reports it

A run of `cswasm dump moonbit <input.dll>` reports `CSW1005` once for every method that
performs one of the operations above. It is a **warning**: the source is generated and printed,
and the exit code stays zero. The warning names the C# method, so a reader knows which
generated functions carry the gap.

Constructs the backend cannot lower at all are a different matter and are refused as `CSW4001`
errors, which empty the output entirely (`docs/diagnostics.md` rule 1).
