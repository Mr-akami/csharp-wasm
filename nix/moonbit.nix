# Pinned MoonBit toolchain (moon, moonc, moonrun, ...) plus the bundled core stdlib.
#
# Version and hashes come from tools/toolchain.json so the driver, CI and Nix all
# read the same pin. Upstream serves immutable versioned URLs of the form
#   https://cli.moonbitlang.com/binaries/<version>/moonbit-<target>.tar.gz
#   https://cli.moonbitlang.com/cores/core-<version>.tar.gz
# with `+` percent-encoded.
#
# We deliberately do not mirror the archives ourselves: redistributing MoonBit
# binaries is a licensing question that belongs to a release gate, not to the dev
# environment (docs/architecture.md, risk 5).
{ lib, stdenv, fetchurl, autoPatchelfHook, toolchain }:

let
  spec = toolchain.moonbit;
  system = stdenv.hostPlatform.system;
  binSpec =
    spec.binaries.${system}
      or (throw "MoonBit is not pinned for system '${system}'. Add its url/sha256 to tools/toolchain.json.");

  binTarball = fetchurl {
    name = "moonbit-${system}.tar.gz";
    url = binSpec.url;
    hash = binSpec.sha256;
  };

  coreTarball = fetchurl {
    name = "moonbit-core.tar.gz";
    url = spec.core.url;
    hash = spec.core.sha256;
  };
in
stdenv.mkDerivation {
  pname = "moonbit";
  version = spec.version;

  dontUnpack = true;
  dontConfigure = true;
  dontBuild = true;

  nativeBuildInputs = lib.optionals stdenv.hostPlatform.isLinux [ autoPatchelfHook ];
  buildInputs = [ stdenv.cc.cc.lib ];

  # The archives ship prebuilt object files and static archives whose symbols are
  # resolved only when a MoonBit program is finally linked.
  autoPatchelfIgnoreMissingDeps = true;
  dontStrip = true;

  installPhase = ''
    runHook preInstall
    mkdir -p "$out/lib"
    tar xf ${binTarball} -C "$out"
    tar xf ${coreTarball} -C "$out/lib"
    chmod -R u+w "$out"
    chmod +x "$out"/bin/* || true
    if [ -d "$out/bin/internal" ]; then chmod +x "$out"/bin/internal/* || true; fi
    runHook postInstall
  '';

  # `moon` cannot compile anything without a bundled core; the official installer
  # bundles it right after download. Do the same so the store path is self-contained.
  #
  # This must run after fixupPhase: autoPatchelfHook is registered as a postFixup
  # hook, and `moon` is not executable until it has rewritten the ELF interpreter.
  postPhases = [ "bundleCorePhase" ];

  bundleCorePhase = ''
    export HOME="$TMPDIR"
    export MOON_HOME="$out"
    export PATH="$out/bin:$PATH"
    "$out/bin/moon" -C "$out/lib/core" bundle --warn-list -a --all
    "$out/bin/moon" -C "$out/lib/core" bundle --warn-list -a --target wasm-gc --quiet
  '';

  passthru = { inherit (spec) version moonVersion; };

  meta = {
    description = "MoonBit toolchain pinned at ${spec.version}";
    homepage = "https://www.moonbitlang.com/";
    platforms = builtins.attrNames spec.binaries;
    sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
  };
}
