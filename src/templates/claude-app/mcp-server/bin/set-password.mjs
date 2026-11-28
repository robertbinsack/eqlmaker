#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Stores the password of the EQLServer login for a profile, protected with DPAPI (current Windows user).
//   node bin/set-password.mjs --profile {{APP_NAME}}            asks without echo
//   <command that prints the password> | node bin/set-password.mjs --profile {{APP_NAME}} --stdin
// The password is never shown, never passed as an argument and never written in clear text.
import { protectPassword } from '../src/credentials.mjs';

const arg = (n) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : undefined; };
const profile = arg('--profile');
if (!profile) { process.stderr.write('usage: set-password --profile <name> [--stdin]\n'); process.exit(2); }

async function fromStdin() {
  let s = ''; for await (const d of process.stdin) s += d;
  return s.replace(/\r?\n$/, '').split(/\r?\n/)[0];
}
function hidden() {
  return new Promise((resolve) => {
    process.stderr.write('Password: ');
    const inp = process.stdin; inp.setRawMode(true); inp.resume(); inp.setEncoding('utf8');
    let s = '';
    inp.on('data', (ch) => {
      if (ch === '\r' || ch === '\n') { inp.setRawMode(false); inp.pause(); process.stderr.write('\n'); resolve(s); }
      else if (ch === '\u0003') { process.stderr.write('\n'); process.exit(130); }
      else if (ch === '\b' || ch === '\u007f') s = s.slice(0, -1);
      else s += ch;
    });
  });
}

const pw = process.argv.includes('--stdin') || !process.stdin.isTTY ? await fromStdin() : await hidden();
if (!pw) { process.stderr.write('empty password, nothing stored\n'); process.exit(1); }
const file = await protectPassword(profile, pw);
process.stderr.write(`stored (DPAPI, current user): ${file}\n`);
