#!/usr/bin/env bash
# Proves the pinned MoonBit toolchain can compile and validate a WasmGC module.
# Run inside the dev shell: `nix develop --command tools/moonbit-smoke.sh`.
set -euo pipefail

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/src/main"
cat > "$work/moon.mod.json" <<'JSON'
{ "name": "cswasm/smoke", "version": "0.1.0", "source": "src" }
JSON
cat > "$work/src/main/moon.pkg.json" <<'JSON'
{ "is-main": true }
JSON
cat > "$work/src/main/main.mbt" <<'MBT'
fn main {
  println("cswasm moonbit smoke ok")
}
MBT

moon -C "$work" build --target wasm-gc --release

wasm="$work/_build/wasm-gc/release/build/main/main.wasm"
if [ ! -f "$wasm" ]; then
  # Older and newer moon releases have moved this directory before; fall back to a search
  # rather than pinning a layout that is not part of MoonBit's contract.
  wasm="$(find "$work" -name 'main.wasm' -print -quit)"
fi

[ -n "$wasm" ] && [ -f "$wasm" ] || { echo "no wasm produced" >&2; exit 1; }

wasm-tools validate "$wasm"

# WasmGC, not a linear-memory heap: the module must declare GC types.
if ! wasm-tools print "$wasm" | grep -qE '\(type .*(struct|array)'; then
  echo "expected WasmGC struct/array types in the module" >&2
  exit 1
fi

printf 'ok: %s (%s bytes)\n' "$(basename "$wasm")" "$(stat -c %s "$wasm" 2>/dev/null || stat -f %z "$wasm")"
moon -C "$work" run src/main --target wasm-gc
