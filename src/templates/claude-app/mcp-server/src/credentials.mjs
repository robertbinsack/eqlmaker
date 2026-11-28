// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Password of the EQLServer login, protected with DPAPI (Windows, current user). The clear text exists only
// in memory and in the pipe to powershell.exe; it is never written to a file, an argument or the output.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export function credentialFile(profile) {
  if (!/^[\w.-]{1,64}$/.test(profile || '')) throw new Error('bad credential profile name');
  const base = process.env.EQLMAKER_CREDENTIAL_DIR || path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'EQLMaker', 'credentials');
  return path.join(base, profile + '.dpapi');
}

// stdin: line 1 = file path, line 2 = entropy, line 3 = password (protect only)
const PROTECT = `$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Security
$f=[Console]::In.ReadLine(); $en=[Text.Encoding]::UTF8.GetBytes([Console]::In.ReadLine()); $pw=[Console]::In.ReadLine()
$b=[Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($pw),$en,'CurrentUser')
[IO.File]::WriteAllText($f,[Convert]::ToBase64String($b)); 'ok'`;
const UNPROTECT = `$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Security
$f=[Console]::In.ReadLine(); $en=[Text.Encoding]::UTF8.GetBytes([Console]::In.ReadLine())
$b=[Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String([IO.File]::ReadAllText($f)),$en,'CurrentUser')
[Console]::Out.Write([Convert]::ToBase64String($b))`;

function ps(script, input) {
  return new Promise((resolve, reject) => {
    const p = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let out = ''; let err = '';
    p.stdout.on('data', (d) => { out += d; });
    p.stderr.on('data', (d) => { err += d; });
    p.on('error', reject);
    p.on('close', (code) => (code === 0 ? resolve(out.trim()) : reject(new Error('DPAPI failed: ' + err.split('\n')[0]))));
    p.stdin.end(input);
  });
}

export async function protectPassword(profile, password) {
  if (process.platform !== 'win32') throw new Error('DPAPI is available on Windows only');
  if (/[\r\n]/.test(password)) throw new Error('password must be one line');
  const f = credentialFile(profile);
  fs.mkdirSync(path.dirname(f), { recursive: true });
  await ps(PROTECT, `${f}\nEQLMaker-v1:${profile}\n${password}\n`);
  return f;
}

export async function readPassword(profile) {
  const f = credentialFile(profile);
  if (!fs.existsSync(f)) throw new Error(`no stored password for profile "${profile}" (run: node bin/set-password.mjs --profile ${profile})`);
  const b64 = await ps(UNPROTECT, `${f}\nEQLMaker-v1:${profile}\n`);
  return Buffer.from(b64, 'base64').toString('utf8');
}
