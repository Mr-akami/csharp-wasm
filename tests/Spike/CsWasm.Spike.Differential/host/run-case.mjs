// Side (b) of issue #18: instantiate a module cswasm produced, call one export, and say what
// happened in the record both hosts share (tests/Spike/CsWasm.Spike.Observations/Observation.cs).
//
//   node run-case.mjs --wasm <module> --export <name> --args <json array of integers>
//
// Two rules this file follows and the runner depends on:
//   * The observation is the last line of stdout, and nothing is ever written to stderr. The
//     sample's own output is compared, so a diagnostic of this harness's on stderr would be
//     read as the program having printed it.
//   * The exit code says nothing. Issue #18 rules out the exit code as the oracle, so a
//     failure is reported inside the observation and the process still exits 0.

import { readFileSync } from 'node:fs';

const SCHEMA = 1;

// (module (import "wasm:js-string" "length" (func (param externref) (result i32))))
// assembled by wasm-tools 1.258.0. The smallest module that needs the JS string builtins.
const JS_STRING_PROBE = new Uint8Array([
  0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
  0x01, 0x06, 0x01, 0x60, 0x01, 0x6f, 0x01, 0x7f,
  0x02, 0x19, 0x01, 0x0e, 0x77, 0x61, 0x73, 0x6d, 0x3a, 0x6a, 0x73, 0x2d, 0x73, 0x74, 0x72,
  0x69, 0x6e, 0x67, 0x06, 0x6c, 0x65, 0x6e, 0x67, 0x74, 0x68, 0x00, 0x00,
]);

/**
 * Whether this engine has the JS string builtins, established by asking it to link a module
 * that needs them and no import object that could satisfy them otherwise.
 *
 * docs/host-profiles.md marks the capability `probe` for the node profile and says why:
 * engines shipped it at different times, so a version check or a permanent flag is wrong in
 * both directions. `WebAssembly.validate` does not answer this - it accepts the module either
 * way, because imports are not resolved during validation - so the probe links it.
 */
function probeJsStringBuiltins() {
  try {
    const module = new WebAssembly.Module(JS_STRING_PROBE, { builtins: ['js-string'] });
    new WebAssembly.Instance(module, {});
    return true;
  } catch {
    return false;
  }
}

const host = {
  runtime: 'node',
  version: process.versions.node,
  capabilities: { jsStringBuiltins: probeJsStringBuiltins() },
};

function completed(returnValue) {
  return {
    outcome: 'completed',
    returnValue,
    // The module imports nothing, so it has no way to write to either stream; a module that
    // did import something is refused below rather than run with a stubbed host.
    stdout: '',
    stderr: '',
    exceptionType: null,
    detail: null,
  };
}

function failed(outcome, detail) {
  return { outcome, returnValue: null, stdout: '', stderr: '', exceptionType: null, detail };
}

function parseArguments(argv) {
  const named = new Map();
  for (let index = 0; index < argv.length; index += 2) {
    named.set(argv[index], argv[index + 1]);
  }

  const wasm = named.get('--wasm');
  const exported = named.get('--export');
  const args = named.get('--args');

  if (wasm === undefined || exported === undefined || args === undefined) {
    throw new Error(
      'Usage: run-case.mjs --wasm <module> --export <name> --args <json array of integers>. '
        + `Got: ${argv.join(' ')}`);
  }

  const parsed = JSON.parse(args);
  if (!Array.isArray(parsed) || parsed.some((value) => !Number.isInteger(value))) {
    throw new Error(`'${args}' is not a JSON array of integers.`);
  }

  return { wasm, exported, args: parsed };
}

function run() {
  let request;
  try {
    request = parseArguments(process.argv.slice(2));
  } catch (error) {
    return failed('hostError', String(error && error.message ? error.message : error));
  }

  let module;
  let instance;
  try {
    module = new WebAssembly.Module(readFileSync(request.wasm));

    // An unknown import is reported rather than stubbed: a stub would be this harness
    // answering for the program, and the two sides would no longer be running the same thing.
    const imports = WebAssembly.Module.imports(module);
    if (imports.length > 0) {
      const named = imports.map((entry) => `${entry.module}.${entry.name}`).join(', ');
      return failed('hostError', `The module imports ${named}; this harness provides no imports.`);
    }

    instance = new WebAssembly.Instance(module, {});
  } catch (error) {
    return failed('hostError', `Could not instantiate ${request.wasm}: ${error}`);
  }

  const exported = instance.exports[request.exported];
  if (typeof exported !== 'function') {
    const available = Object.keys(instance.exports).join(', ') || '(none)';
    return failed(
      'hostError',
      `The module exports no function named '${request.exported}'. It exports: ${available}`);
  }

  let returned;
  try {
    returned = exported(...request.args);
  } catch (error) {
    if (error instanceof WebAssembly.RuntimeError) {
      // A trap is the program failing and is kept apart from this harness failing; folding
      // the two together is what would let a trap be read as a managed exception.
      return failed('trap', String(error.message));
    }

    return failed('hostError', `Calling '${request.exported}' threw ${error}`);
  }

  // A decimal string, so that neither side's number type reaches the comparison.
  return completed(returned === undefined ? null : String(returned));
}

process.stdout.write(JSON.stringify({ schema: SCHEMA, ...run(), host }) + '\n');
