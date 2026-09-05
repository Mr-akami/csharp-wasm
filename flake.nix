{
  description = "CsWasm - a Wasm-native C# compiler targeting WasmGC through MoonBit";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    # Only systems for which tools/toolchain.json pins a MoonBit build.
    flake-utils.lib.eachSystem [ "x86_64-linux" "aarch64-darwin" ] (system:
      let
        pkgs = import nixpkgs { inherit system; config.allowUnfree = true; };
        toolchain = builtins.fromJSON (builtins.readFile ./tools/toolchain.json);

        moonbit = pkgs.callPackage ./nix/moonbit.nix { inherit toolchain; };

        dotnet = pkgs.dotnet-sdk_9;
        nodejs = pkgs.nodejs_24;
        wasmTools = pkgs.wasm-tools;

        # A nixpkgs bump that moves any pinned tool must be a reviewed baseline
        # change, not a silent upgrade. Fail evaluation loudly instead.
        checkPin = name: actual: expected:
          if actual == expected then true
          else throw ''
            Pinned toolchain drift: ${name} is ${actual} in this nixpkgs, but tools/toolchain.json pins ${expected}.
            Either pin nixpkgs back (flake.lock) or update tools/toolchain.json deliberately and re-baseline the benchmarks.
          '';

        pinsOk =
          checkPin "dotnet-sdk" dotnet.version toolchain.dotnet.sdk
          && checkPin "nodejs" nodejs.version toolchain.node.version
          && checkPin "wasm-tools" wasmTools.version toolchain.wasmTools.version;

        nativeTools = [ moonbit dotnet nodejs wasmTools ]
          ++ (with pkgs; [ brotli jq git ]);
      in
      assert pinsOk; {
        packages = {
          inherit moonbit;
          default = moonbit;
        };

        devShells.default = pkgs.mkShell {
          packages = nativeTools;

          # `moon` writes caches and a registry index below MOON_HOME, so it cannot
          # point straight at the read-only store path. Keep a writable copy in the
          # working tree instead; it is reproducible from the pinned store path and
          # is refreshed whenever the pin changes.
          shellHook = ''
            export DOTNET_ROOT="${dotnet}/share/dotnet"
            export DOTNET_CLI_TELEMETRY_OPTOUT=1
            export DOTNET_NOLOGO=1

            export MOON_HOME="''${MOON_HOME:-$PWD/.moon-home}"
            export MOONBIT_STORE="${moonbit}"
            if [ ! -e "$MOON_HOME/.pin" ] || [ "$(cat "$MOON_HOME/.pin" 2>/dev/null)" != "${moonbit}" ]; then
              echo "cswasm: materialising pinned MoonBit ${toolchain.moonbit.version} into $MOON_HOME"
              rm -rf "$MOON_HOME"
              mkdir -p "$MOON_HOME"
              cp -r "${moonbit}"/. "$MOON_HOME"/
              chmod -R u+w "$MOON_HOME"
              printf '%s' "${moonbit}" > "$MOON_HOME/.pin"
            fi
            export PATH="$MOON_HOME/bin:$PATH"

            echo "cswasm dev shell"
            echo "  dotnet     ${dotnet.version}"
            echo "  node       ${nodejs.version}"
            echo "  moonbit    ${toolchain.moonbit.version} (moon ${toolchain.moonbit.moonVersion})"
            echo "  wasm-tools ${wasmTools.version}"
          '';
        };

        checks.toolchain = pkgs.runCommand "cswasm-toolchain-check"
          { nativeBuildInputs = [ moonbit nodejs dotnet wasmTools ]; }
          ''
            export HOME="$TMPDIR"
            export MOON_HOME="${moonbit}"
            moonc -v | tee moonc.txt
            grep -q '${toolchain.moonbit.version}' moonc.txt
            node --version | grep -q '${toolchain.node.version}'
            wasm-tools --version
            touch $out
          '';
      });
}
