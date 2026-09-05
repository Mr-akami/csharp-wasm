#!/usr/bin/env bash
# Re-pin the MoonBit toolchain to a new upstream version.
#
# Upgrading MoonBit is a reviewed baseline change, not a silent bump: it can move
# generated code size and startup, which every benchmark budget is measured against.
# This script only computes the new hashes and rewrites tools/toolchain.json; running
# the benchmarks and re-baselining budgets is a separate, deliberate step.
#
# Usage: tools/update-toolchain.sh <version>
#   e.g. tools/update-toolchain.sh 0.10.12+abcdef123
set -euo pipefail

version="${1:-}"
if [ -z "$version" ]; then
  echo "usage: $0 <moonbit-version>   (see https://cli.moonbitlang.com/version.json)" >&2
  exit 2
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
json="$root/tools/toolchain.json"
encoded="${version//+/%2B}"
base="https://cli.moonbitlang.com"

hash_of() {
  local url="$1" name="$2"
  nix-prefetch-url --type sha256 --name "$name" "$url" 2>/dev/null | tail -1
}

sri() { nix hash convert --hash-algo sha256 --to sri "$1"; }

echo "fetching moonbit $version ..."
core_hash="$(sri "$(hash_of "$base/cores/core-$encoded.tar.gz" "moonbit-core.tar.gz")")"
linux_hash="$(sri "$(hash_of "$base/binaries/$encoded/moonbit-linux-x86_64.tar.gz" "moonbit-linux-x86_64.tar.gz")")"
darwin_hash="$(sri "$(hash_of "$base/binaries/$encoded/moonbit-darwin-aarch64.tar.gz" "moonbit-darwin-aarch64.tar.gz")")"

python3 - "$json" "$version" "$encoded" "$core_hash" "$linux_hash" "$darwin_hash" <<'PY'
import json, sys, datetime

path, version, encoded, core_hash, linux_hash, darwin_hash = sys.argv[1:7]
base = "https://cli.moonbitlang.com"

with open(path) as f:
    doc = json.load(f)

mb = doc["moonbit"]
mb["version"] = version
mb["pinnedOn"] = datetime.date.today().isoformat()
mb["core"] = {"url": f"{base}/cores/core-{encoded}.tar.gz", "sha256": core_hash}
mb["binaries"]["x86_64-linux"] = {
    "url": f"{base}/binaries/{encoded}/moonbit-linux-x86_64.tar.gz", "sha256": linux_hash}
mb["binaries"]["aarch64-darwin"] = {
    "url": f"{base}/binaries/{encoded}/moonbit-darwin-aarch64.tar.gz", "sha256": darwin_hash}

with open(path, "w") as f:
    json.dump(doc, f, indent=2, ensure_ascii=False)
    f.write("\n")
PY

cat <<MSG

tools/toolchain.json now pins moonbit $version.

Still to do, by hand:
  1. moonVersion / moonrunVersion may have moved too - check `moon version` in the new shell.
  2. nix develop  (rebuilds the toolchain, re-materialises .moon-home)
  3. nix flake check && dotnet test && tools/moonbit-smoke.sh
  4. Re-run the benchmarks and re-baseline budgets before merging (see docs/bench.md, Step 5).
MSG
