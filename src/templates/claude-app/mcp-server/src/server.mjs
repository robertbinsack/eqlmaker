// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// MCP server over stdio (JSON-RPC 2.0, one message per line). Tools are generic: they work for every
// application description; the description decides which queries, records and operations exist.
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import pg from 'pg';
import { Engine, AppError, mapPg } from './engine.mjs';
import { AuditLog } from './audit.mjs';
import { readPassword } from './credentials.mjs';

export const VERSION = '0.1.0';
const MAX_LINE = 1024 * 1024;

// timestamps and dates stay strings as EQLServer writes them; counts become numbers
for (const oid of [1082, 1114, 1184]) pg.types.setTypeParser(oid, (v) => v);
pg.types.setTypeParser(20, (v) => { const n = Number(v); return Number.isSafeInteger(n) ? n : v; });

const S = (props, req = []) => ({ type: 'object', properties: props, required: req, additionalProperties: false });
const str = (d, extra = {}) => ({ type: 'string', description: d, ...extra });
const RO = { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false };

export const TOOLS = [
  { name: 'status', title: 'Connection status', annotations: RO,
    description: 'Server, database, login, rights and tools of this connection. Never fails: when EQLServer is not reachable, ok is false and message says why.',
    inputSchema: S({}) },
  { name: 'describe', title: 'Describe the application', annotations: RO,
    description: 'Entities with fields, queries with parameters, filters and sort keys, lookups and operations of the application description.',
    inputSchema: S({}) },
  { name: 'lookups', title: 'Lookup lists', annotations: RO,
    description: 'Rows of the named lookup lists (id, text, ...); all lookups when names is empty.',
    inputSchema: S({ names: { type: 'array', items: { type: 'string' }, maxItems: 50 } }) },
  { name: 'query', title: 'Run a query', annotations: RO,
    description: 'Runs a named query of the description. Lists return {rows,total,page,size,pages[,sum]}; object queries return their parts.',
    inputSchema: S({
      name: str('query name (see describe)'), params: { type: 'object', description: 'query parameters (see describe)' },
      filter: str('named filter of the query'), q: str('search text', { maxLength: 200 }), sort: str('sort key of the query'),
      dir: { type: 'string', enum: ['asc', 'desc'] }, page: { type: 'integer', minimum: 1 }, size: { type: 'integer', minimum: 1, maximum: 1000 },
      actor_id: { type: ['integer', 'null'], description: 'id of the person working (for filters such as "mine")' },
    }, ['name']) },
  { name: 'get_record', title: 'Read a record', annotations: RO,
    description: 'One record of an entity with its child rows and extras, plus version (hash of the stored rows).',
    inputSchema: S({ entity: str('entity name'), id: { type: 'integer' } }, ['entity', 'id']) },
  { name: 'plan_operation', title: 'Preview a change', annotations: { ...RO, idempotentHint: false },
    description: 'Preview of an operation: runs it in a transaction that is rolled back and returns before/after rows, a summary and a one-time token (10 minutes). Nothing is written. The token is the only way to perform the change with run_operation or run_destructive_operation. expected = old values of the main record as the caller saw them; a difference returns a conflict with the current row. nonce makes every preview a fresh call.',
    inputSchema: S({
      operation: str('operation name (see describe), e.g. orders.update or orders.ship'), params: { type: 'object' },
      expected: { type: ['object', 'null'], description: 'old column values of the main record' },
      actor: { type: ['string', 'null'], maxLength: 100, description: 'name written to the audit columns' },
      nonce: str('any unique string per preview'),
    }, ['operation', 'nonce']) },
  { name: 'run_operation', title: 'Perform a previewed change', annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    description: 'Performs the change of a preview token from plan_operation in exactly one transaction (all or nothing). Rows read by the preview are compared again; a difference returns a conflict and writes nothing. The same request_id never writes twice.',
    inputSchema: S({ token: str('token of plan_operation'), request_id: str('unique id of this write', { maxLength: 100 }) }, ['token']) },
  { name: 'run_destructive_operation', title: 'Perform a previewed deletion', annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: true, openWorldHint: false },
    description: 'Like run_operation, for operations that delete data (preview says destructive: true).',
    inputSchema: S({ token: str('token of plan_operation'), request_id: str('unique id of this write', { maxLength: 100 }) }, ['token']) },
  { name: 'write_outcome', title: 'Outcome of a write', annotations: RO,
    description: 'Outcome of a write by its request_id: committed (with the result), none (nothing written) or unknown.',
    inputSchema: S({ request_id: str('request_id of the write') }, ['request_id']) },
];

export function loadConfig(file) {
  const cfg = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''));
  const dir = path.dirname(path.resolve(file));
  cfg.app = path.resolve(dir, cfg.app);
  cfg.audit_log = cfg.audit_log ? path.resolve(dir, cfg.audit_log.replace(/%LOCALAPPDATA%/gi, process.env.LOCALAPPDATA || '')) : path.join(process.env.LOCALAPPDATA || dir, 'EQLMaker', 'audit', `${path.basename(cfg.app, '.json')}.jsonl`);
  return cfg;
}

export async function createServer(cfg, { password, engineModule } = {}) {
  const { Engine: EngineC, mapPg: mapPgF } = engineModule || { Engine, mapPg };
  const desc = JSON.parse(fs.readFileSync(cfg.app, 'utf8'));
  const conn = cfg.connection;
  let pwError = null;
  let pw = password;
  if (pw === undefined) { try { pw = await readPassword(conn.credential_profile); } catch (e) { pwError = e.message; } }
  const pool = new pg.Pool({
    host: conn.host, port: conn.port, database: conn.database, user: conn.user, password: pw || '',
    options: `-c search_path=${conn.schema} -c statement_timeout=${conn.statement_timeout_ms || 15000}`,
    application_name: 'eqlmaker-mcp', max: 4, connectionTimeoutMillis: conn.connect_timeout_ms || 4000, idleTimeoutMillis: 30000,
  });
  pool.on('error', () => {}); // a dropped idle connection must not end the process
  const audit = new AuditLog(cfg.audit_log);
  const engine = new EngineC(desc, pool, { audit });

  const guard = () => { if (pwError) throw new AppError('forbidden', pwError); };
  const handlers = {
    async status() {
      const base = { app: desc.app, title: desc.title, server_version: VERSION, host: conn.host, port: conn.port, database: conn.database, schema: conn.schema, user: conn.user, audit_log: cfg.audit_log, tools: TOOLS.map((t) => t.name) };
      if (pwError) return { ok: false, ...base, message: pwError };
      try {
        const r = (await pool.query('SELECT version() AS v, current_user AS u, current_database() AS d, localtimestamp(0)::text AS now')).rows[0];
        const tables = [...new Set(Object.values(engine.entities).map((e) => e.table))];
        const rights = (await pool.query(`SELECT t AS table, has_table_privilege(current_user, $1 || '.' || t, 'SELECT') AS select,
            has_table_privilege(current_user, $1 || '.' || t, 'INSERT') AS insert, has_table_privilege(current_user, $1 || '.' || t, 'UPDATE') AS update,
            has_table_privilege(current_user, $1 || '.' || t, 'DELETE') AS delete FROM unnest($2::text[]) t`, [conn.schema, tables])).rows;
        return { ok: true, ...base, eqlserver: r.v, login: r.u, now: r.now, rights };
      } catch (e) { return { ok: false, ...base, message: mapPgF(e, engine).message }; }
    },
    async describe() { return engine.describe(); },
    async lookups(a) { guard(); return engine.lookups(a); },
    async query(a) { guard(); return engine.query(a); },
    async get_record(a) { guard(); return engine.getRecord(a); },
    async plan_operation(a) { guard(); return engine.plan(a); },
    async run_operation(a) { guard(); return engine.run(a, { destructive: false }); },
    async run_destructive_operation(a) { guard(); return engine.run(a, { destructive: true }); },
    async write_outcome(a) { guard(); return audit.outcome(a.request_id, pool); },
  };

  async function callTool(name, args) {
    const h = handlers[name];
    if (!h) return toolError(new AppError('not_found', `Unknown tool ${name}`));
    try {
      checkArgs(TOOLS.find((t) => t.name === name).inputSchema, args || {});
      const r = await h(args || {});
      return { content: [{ type: 'text', text: JSON.stringify(r) }], structuredContent: r };
    } catch (e) {
      return toolError(isAppError(e) ? e : mapPgF(e, engine));
    }
  }
  return { callTool, engine, pool, audit, close: () => pool.end() };
}

const isAppError = (e) => e instanceof AppError || (e?.constructor?.name === 'AppError' && typeof e.code === 'string');

function checkArgs(schema, args) {
  if (typeof args !== 'object' || Array.isArray(args)) throw new AppError('validation', 'arguments must be an object');
  const bad = Object.keys(args).filter((k) => !schema.properties[k]);
  const missing = schema.required.filter((k) => args[k] === undefined || args[k] === null);
  if (bad.length || missing.length) throw new AppError('validation', bad.length ? `Unknown argument ${bad[0]}` : `Missing argument ${missing[0]}`, { fields: Object.fromEntries([...bad, ...missing].map((k) => [k, bad.includes(k) ? 'unknown' : 'required'])) });
  for (const [k, p] of Object.entries(schema.properties)) {
    const v = args[k]; if (v === undefined || v === null) continue;
    const types = [].concat(p.type);
    const ok = types.some((t) => (t === 'integer' ? Number.isInteger(v) : t === 'array' ? Array.isArray(v) : t === 'object' ? typeof v === 'object' && !Array.isArray(v) : typeof v === t));
    if (!ok || (p.enum && !p.enum.includes(v)) || (p.maxLength && v.length > p.maxLength)) throw new AppError('validation', `Bad argument ${k}`, { fields: { [k]: 'type' } });
  }
}

export function toolError(e) {
  const error = { code: e.code || 'error', message: e.message, ...(e.fields ? { fields: e.fields } : {}), ...(e.current !== undefined ? { current: e.current } : {}), ...(e.retryable ? { retryable: true } : {}) };
  return { isError: true, content: [{ type: 'text', text: JSON.stringify({ error }) }], structuredContent: { error } };
}

/** stdio transport: newline-delimited JSON-RPC; logs go to stderr only */
export function serveStdio(server, { name = 'eqlmaker', instructions } = {}) {
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  const send = (m) => process.stdout.write(JSON.stringify(m) + '\n');
  rl.on('line', async (line) => {
    if (!line.trim()) return;
    if (line.length > MAX_LINE) { send({ jsonrpc: '2.0', id: null, error: { code: -32600, message: 'message larger than 1 MiB' } }); return; }
    let m; try { m = JSON.parse(line); } catch { send({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'parse error' } }); return; }
    const reply = (result) => m.id !== undefined && send({ jsonrpc: '2.0', id: m.id, result });
    try {
      if (m.method === 'initialize') reply({ protocolVersion: m.params?.protocolVersion || '2025-06-18', capabilities: { tools: { listChanged: false } }, serverInfo: { name, title: name, version: VERSION }, instructions });
      else if (m.method === 'ping') reply({});
      else if (m.method === 'tools/list') reply({ tools: TOOLS });
      else if (m.method === 'tools/call') reply(await server.callTool(m.params?.name, m.params?.arguments));
      else if (m.method?.startsWith('notifications/')) { /* initialized, cancelled: nothing to do */ }
      else if (m.id !== undefined) send({ jsonrpc: '2.0', id: m.id, error: { code: -32601, message: `method not found: ${m.method}` } });
    } catch (e) { if (m.id !== undefined) send({ jsonrpc: '2.0', id: m.id, error: { code: -32603, message: e.message } }); }
  });
  rl.on('close', () => { server.close().finally(() => process.exit(0)); });
}
