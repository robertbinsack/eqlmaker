// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Tests of the Claude app template without a database: tool contract, argument checks, the preview token rule,
// behaviour when EQLServer is not reachable, the stdio protocol, and the page (texts in both languages, only
// tools that capabilities.json grants). Run: node --test --test-concurrency=1 test/
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { loadConfig, createServer, TOOLS } from '../src/server.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
const appDir = path.join(root, '..', 'app');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'eqlmaker-claude-app-'));
let port; let cfgFile; let s;

const call = async (name, args) => { const r = await s.callTool(name, args); return r.isError ? { error: r.structuredContent.error } : r.structuredContent; };

before(async () => {
  // a port that nobody listens on: EQLServer "not reachable"
  port = await new Promise((res) => { const srv = net.createServer().listen(0, '127.0.0.1', () => { const p = srv.address().port; srv.close(() => res(p)); }); });
  const cfg = JSON.parse(fs.readFileSync(path.join(root, 'config', 'example.json'), 'utf8'));
  cfg.app = path.join(root, 'apps', 'example.json'); cfg.connection.port = port; cfg.connection.connect_timeout_ms = 1500; cfg.audit_log = path.join(tmp, 'audit.jsonl');
  cfgFile = path.join(tmp, 'example.json'); fs.writeFileSync(cfgFile, JSON.stringify(cfg));
  s = await createServer(loadConfig(cfgFile), { password: 'not-used' });
});
after(async () => { await s?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

test('tools: reads are read-only, writes need the token, deleting has its own destructive tool', () => {
  const by = Object.fromEntries(TOOLS.map((t) => [t.name, t]));
  assert.deepEqual(Object.keys(by).sort(), ['describe', 'get_record', 'lookups', 'plan_operation', 'query', 'run_destructive_operation', 'run_operation', 'status', 'write_outcome']);
  for (const n of ['status', 'describe', 'lookups', 'query', 'get_record', 'plan_operation', 'write_outcome']) assert.equal(by[n].annotations.readOnlyHint, true, n);
  assert.equal(by.run_operation.annotations.readOnlyHint, false); assert.equal(by.run_operation.annotations.destructiveHint, false);
  assert.equal(by.run_destructive_operation.annotations.destructiveHint, true);
  assert.deepEqual(by.run_operation.inputSchema.required, ['token']);
  for (const t of TOOLS) assert.equal(t.inputSchema.additionalProperties, false, `${t.name} rejects unknown arguments`);
});

test('describe: entities with create/update/delete and the list query come from the description', async () => {
  const d = await call('describe', {});
  assert.equal(d.app, 'example');
  const items = d.entities.find((e) => e.name === 'items');
  assert.deepEqual(items.ops, ['create', 'update', 'delete']);
  assert.ok(items.fields.find((f) => f.c === 'name').req);
  const q = d.queries.find((x) => x.name === 'items');
  assert.equal(q.kind, 'list'); assert.equal(q.search, true); assert.deepEqual(q.sort, ['name', 'price']);
  assert.ok(d.operations.find((o) => o.name === 'items.delete').destructive);
});

test('arguments: unknown arguments, unknown queries and unknown sort keys are refused before any SQL', async () => {
  assert.equal((await call('query', { name: 'items', evil: 1 })).error.code, 'validation');
  assert.equal((await call('query', { name: 'nope' })).error.code, 'validation');
  assert.equal((await call('plan_operation', { operation: 'items.drop', nonce: 'x' })).error.code, 'validation');
  assert.equal((await call('get_record', { entity: 'pg_authid', id: 1 })).error.code, 'validation');
});

test('writes: run_operation without a token from plan_operation writes nothing', async () => {
  const r = await call('run_operation', { token: 'made-up-token' });
  assert.equal(r.error.code, 'token_invalid');
});

test('EQLServer not reachable: status says so without failing, reads return unavailable', async () => {
  const st = await call('status', {});
  assert.equal(st.ok, false); assert.equal(st.port, port); assert.ok(st.message);
  const q = await call('query', { name: 'items' });
  assert.equal(q.error.code, 'unavailable');
});

test('stdio: one process serves exactly one application under its own name', async () => {
  const p = spawn(process.execPath, [path.join(root, 'bin', 'eqlmaker-mcp.mjs'), '--config', cfgFile], { stdio: ['pipe', 'pipe', 'pipe'] });
  const out = []; let buf = '';
  const got = new Promise((res) => p.stdout.on('data', (d) => { buf += d; const lines = buf.split('\n'); buf = lines.pop(); for (const l of lines) if (l.trim()) out.push(JSON.parse(l)); if (out.length >= 2) res(); }));
  p.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } }) + '\n');
  p.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 2, method: 'tools/list' }) + '\n');
  await got; p.stdin.end();
  await new Promise((res) => p.on('exit', res));
  assert.equal(out[0].result.serverInfo.name, 'eqlmaker-example');
  assert.match(out[0].result.instructions, /plan_operation first/);
  assert.equal(out[1].result.tools.length, TOOLS.length);
});

// With a database (CI sets EQLMAKER_TEST_PG, a libpq keyword string of a plain PostgreSQL): the whole write path of
// the template against a real server, in a schema of its own that is dropped at the end.
const PG = process.env.EQLMAKER_TEST_PG;
test('database: preview writes nothing, the confirmed token writes once, deleting needs the destructive tool', { skip: !PG && 'EQLMAKER_TEST_PG not set' }, async () => {
  const kv = Object.fromEntries([...PG.matchAll(/(\w+)=('(?:[^'\\]|\\.)*'|\S+)/g)].map((m) => [m[1], m[2].replace(/^'|'$/g, '').replace(/\\(.)/g, '$1')]));
  const { default: pg } = await import('pg');
  const admin = new pg.Client({ host: kv.host, port: +kv.port, user: kv.user, password: kv.password, database: kv.dbname });
  await admin.connect();
  const schema = 'claudeapp_t' + process.pid;
  await admin.query(`CREATE SCHEMA ${schema}; CREATE TABLE ${schema}.items (id serial PRIMARY KEY, name varchar(80) NOT NULL, price numeric(10,2), note text)`);
  const cfg = loadConfig(cfgFile);
  Object.assign(cfg.connection, { host: kv.host, port: +kv.port, user: kv.user, database: kv.dbname, schema });
  const db = await createServer(cfg, { password: kv.password });
  const c = async (n, a) => { const r = await db.callTool(n, a); return r.isError ? { error: r.structuredContent.error } : r.structuredContent; };
  try {
    const p = await c('plan_operation', { operation: 'items.create', params: { values: { name: 'Lamp', price: '12,50' } }, nonce: 'n1' });
    assert.equal(p.tool, 'run_operation'); assert.match(p.summary, /items: insert 1/);
    assert.equal((await admin.query(`SELECT count(*)::int n FROM ${schema}.items`)).rows[0].n, 0, 'the preview wrote nothing');
    const w = await c('run_operation', { token: p.token, request_id: 'r1' });
    assert.ok(!w.error, JSON.stringify(w.error));
    assert.equal((await c('run_operation', { token: p.token, request_id: 'r2' })).error.code, 'token_invalid', 'a token works once');
    const row = (await admin.query(`SELECT id, name, price::text FROM ${schema}.items`)).rows[0];
    assert.deepEqual([row.name, row.price], ['Lamp', '12.50']);
    const list = await c('query', { name: 'items', q: 'lam' });
    assert.equal(list.total, 1);
    const stale = await c('plan_operation', { operation: 'items.update', params: { id: row.id, values: { name: 'Desk lamp' } }, expected: { name: 'Other' }, nonce: 'n2' });
    assert.equal(stale.error.code, 'conflict', 'old values that differ give a conflict');
    const bad = await c('plan_operation', { operation: 'items.create', params: { values: { name: '' } }, nonce: 'n3' });
    assert.equal(bad.error.code, 'validation'); assert.ok(bad.error.fields);
    const d = await c('plan_operation', { operation: 'items.delete', params: { id: row.id }, nonce: 'n4' });
    assert.equal(d.destructive, true);
    const wrong = await c('run_operation', { token: d.token });
    assert.equal(wrong.error.code, 'validation', 'a deleting token does not work in run_operation'); assert.match(wrong.error.message, /run_destructive_operation/);
    assert.equal((await admin.query(`SELECT count(*)::int n FROM ${schema}.items`)).rows[0].n, 1, 'still there');
    const d2 = await c('plan_operation', { operation: 'items.delete', params: { id: row.id }, nonce: 'n5' });
    assert.ok(!(await c('run_destructive_operation', { token: d2.token })).error);
    assert.equal((await admin.query(`SELECT count(*)::int n FROM ${schema}.items`)).rows[0].n, 0);
  } finally { await db.close(); await admin.query(`DROP SCHEMA ${schema} CASCADE`); await admin.end(); }
});

test('page: every text has a German entry, only granted tools are called, no data inside', () => {
  const page = fs.readFileSync(path.join(appDir, 'app.html'), 'utf8');
  assert.doesNotThrow(() => new Function(page.match(/<script>([\s\S]*)<\/script>/)[1]), 'the script of the page parses');
  const de = Function(`return (${page.match(/const DE = (\{[\s\S]*?\n\});/)[1]})`)();
  const used = [...page.matchAll(/\bt\("((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]);
  assert.ok(used.length > 20);
  for (const k of used) assert.ok(de[k], `German text for "${k}"`);
  const caps = JSON.parse(fs.readFileSync(path.join(appDir, 'capabilities.json'), 'utf8'));
  const granted = new Set(caps.mcp.servers[0].tools);
  const calls = [...page.matchAll(/\b(?:read|write)\("(\w+)"/g)].map((m) => m[1]);
  for (const c of new Set([...calls, 'run_operation', 'run_destructive_operation'])) assert.ok(granted.has(c), `${c} is granted`);
  assert.equal(caps.mcp.servers[0].server, 'host:{{SERVER_NAME}}');
  assert.match(page, /const SERVER = "host:\{\{SERVER_NAME\}\}"/);
  assert.match(page, /"de", "en"/, 'language switcher DE/EN');
  assert.doesNotMatch(page, /password|passwort/i);
});
