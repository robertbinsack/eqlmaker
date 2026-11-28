// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Generic application engine: reads an application description (entities, queries, lookups, operations)
// and runs it against EQLServer over the PostgreSQL port. Nothing in here knows a concrete application.
//
// Safety rules built in:
//   - SQL text comes only from the description; every value of the caller is a bind parameter ($n).
//   - Identifiers (tables, columns, sort keys) are checked against the description, never taken from input.
//   - A write is planned first (preview: the real statements in a transaction that is rolled back), the plan
//     returns a one-time token; only that token can start the write, which is again exactly one transaction.
//   - Optimistic concurrency: expected old values of the caller and the rows read by the plan are compared
//     again inside the write transaction; a difference aborts it with a conflict and the current row.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export class AppError extends Error {
  constructor(code, message, more = {}) { super(message); this.code = code; Object.assign(this, more); }
}

const IDENT = /^[A-Za-z_][A-Za-z0-9_]*$/;
const PLACEHOLDER = /(?<![:\w]):([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)/g;
const TOKEN_TTL_MS = 10 * 60 * 1000;
const MAX_CHANGES = 60;

const DEFAULT_TEXTS = {
  required: 'Please enter "{label}".',
  too_long: 'At most {len} characters.',
  not_int: 'Not a whole number.',
  not_number: 'Not a number.',
  not_date: 'Not a date (YYYY-MM-DD).',
  min: 'At least {min}.',
  max: 'At most {max}.',
  unique: '"{label}" {value} is already taken.',
  unknown_field: 'Unknown field.',
  duplicate_child: 'Duplicate entry.',
  not_found: '{single} not found.',
  conflict: 'The record was changed in the meantime. Nothing was written.',
  token_invalid: 'The preview has expired or was already used. Please preview the change again.',
  wrong_tool: 'This operation needs the tool {tool}.',
  no_insert: 'Creating is not available here.',
  no_delete: 'Deleting is not available here.',
  unavailable: 'EQLServer not reachable: {detail}',
  op_create: 'Create {single}', op_update: 'Change {single}', op_delete: 'Delete {single}',
};

const stable = (v) => JSON.stringify(v, (k, x) => (x && typeof x === 'object' && !Array.isArray(x) ? Object.fromEntries(Object.keys(x).sort().map((y) => [y, x[y]])) : x));
export const hash = (v) => crypto.createHash('sha256').update(stable(v)).digest('hex').slice(0, 32);
const same = (a, b) => String(a ?? '') === String(b ?? '');

/** One SQL statement with numbered parameters; placeholders :name or :a.b resolve against a scope. */
class Sql {
  constructor() { this.values = []; this.index = new Map(); }
  param(v) { this.values.push(toParam(v)); return '$' + this.values.length; }
  fragment(text, scope) {
    return text.replace(PLACEHOLDER, (m, name) => {
      const key = 'n:' + name;
      if (!this.index.has(key)) this.index.set(key, this.param(resolve(scope, name)));
      return this.index.get(key);
    });
  }
}
function toParam(v) {
  if (v === undefined) return null;
  if (v === null || typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean') return v;
  if (v instanceof Date) return v.toISOString();
  return JSON.stringify(v);
}
function resolve(scope, name) {
  const parts = name.split('.');
  if (!(parts[0] in scope)) throw new AppError('config', `description error: unknown placeholder :${name}`);
  let v = scope;
  for (const p of parts) { if (v == null) return null; v = v[p]; }
  return v;
}
const compile = (text, scope) => { const s = new Sql(); const t = s.fragment(text, scope); return [t, s.values]; };
/** value of the description: ":path" = from the scope, {sql} = SQL expression, anything else = literal */
function valueSql(s, v, scope) {
  if (typeof v === 'string' && v.startsWith(':') && IDENT.test(v.slice(1).split('.')[0])) return s.param(resolve(scope, v.slice(1)));
  if (v && typeof v === 'object' && typeof v.sql === 'string') return '(' + s.fragment(v.sql, scope) + ')';
  return s.param(v);
}
const valueOf = (v, scope) => (typeof v === 'string' && v.startsWith(':') ? resolve(scope, v.slice(1)) : v);
// entity keys: one column, or a composite key (array of columns, e.g. a link table); a composite key value is the
// text 'v1|v2' of its columns
const keyExpr = (e) => (Array.isArray(e.key) ? '(' + e.key.map((k) => `${k}::text`).join(" || '|' || ") + ')' : e.key);
const keyOf = (e, row) => (Array.isArray(e.key) ? e.key.map((k) => String(row[k])).join('|') : row[e.key]);
const keyNum = (e, row) => (Array.isArray(e.key) ? keyOf(e, row) : Number(row[e.key]));
const auditOf = (e, desc) => (e.audit && typeof e.audit === 'object' ? e.audit : e.audit ? desc.audit : null);
const truthy = (v) => !(v === null || v === undefined || v === false || v === 0 || v === '0' || v === 'f' || v === 'false' || v === '');

export class Engine {
  /** @param desc application description  @param db { query(client-less) , connect() } a pg Pool  @param opts { audit } */
  constructor(desc, db, opts = {}) {
    this.desc = desc; this.db = db; this.audit = opts.audit; this.texts = { ...DEFAULT_TEXTS, ...(desc.texts || {}) };
    this.tokens = new Map();
    this.entities = {}; this.queries = desc.queries || {}; this.lookupsDef = desc.lookups || {}; this.operations = {};
    for (const [name, e] of Object.entries(desc.entities || {})) this.entities[name] = this.#entity(name, e);
    for (const e of Object.values(this.entities)) this.#entityOps(e);
    for (const [name, op] of Object.entries(desc.operations || {})) this.operations[name] = { name, ...op, steps: op.steps || [] };
    // tables: further tables that hook code (step "call") may write; hidden entities used only to track and preview
    // the changes (no own operations, not in describe)
    for (const [t, d] of Object.entries(desc.tables || {})) {
      if (Object.values(this.entities).some((e) => e.table === t)) continue;
      this.entities[t] = { ...this.#entity(t, { table: t, key: d.key, identity: d.identity !== false }), ops: [], hidden: true };
    }
    this.byTable = {};
    for (const e of Object.values(this.entities)) this.byTable[e.table] ||= e;
    this.redact = {};
    for (const [t, d] of Object.entries(desc.tables || {})) if (d.redact?.length) this.redact[t] = new Set(d.redact);
    this.fragments = desc.fragments || {};
    this.colCache = new Map();
    for (const [name, qd] of Object.entries(this.queries)) this.#checkQuery(name, qd);
  }

  t(key, vars = {}) { return (this.texts[key] || key).replace(/\{(\w+)\}/g, (m, k) => vars[k] ?? ''); }

  // ---- description ----------------------------------------------------------------------------------------
  #entity(name, e) {
    if (!IDENT.test(e.table) || ![].concat(e.key).every((k) => IDENT.test(k))) throw new AppError('config', `entity ${name}: bad table or key`);
    const fields = {};
    for (const f of e.fields || []) { if (!IDENT.test(f.c)) throw new AppError('config', `entity ${name}: bad column ${f.c}`); fields[f.c] = { t: 'text', l: f.c, ...f }; }
    for (const [cn, ch] of Object.entries(e.children || {})) {
      if (!IDENT.test(ch.fk) || !IDENT.test(cn)) throw new AppError('config', `entity ${name}: bad child ${cn}`);
      ch.fields = Object.fromEntries((ch.fields || []).map((f) => [f.c, { t: 'text', l: f.c, ...f }]));
    }
    return { name, ops: Array.isArray(e.key) ? [] : ['create', 'update', 'delete'], single: name, ...e, fields };
  }
  #entityOps(e) {
    const t = e.titles || {};
    if (e.ops.includes('create')) this.operations[`${e.name}.create`] = { name: `${e.name}.create`, title: t.create || this.t('op_create', { single: e.single }), entity: e.name, kind: 'create', params: { values: { t: 'object', req: true } }, steps: [{ entity_create: e.name }] };
    if (e.ops.includes('update')) this.operations[`${e.name}.update`] = { name: `${e.name}.update`, title: t.update || this.t('op_update', { single: e.single }), entity: e.name, kind: 'update', params: { id: { t: 'int', req: true }, values: { t: 'object', req: true } }, steps: [{ entity_update: e.name }] };
    if (e.ops.includes('delete')) this.operations[`${e.name}.delete`] = { name: `${e.name}.delete`, title: t.delete || this.t('op_delete', { single: e.single }), entity: e.name, kind: 'delete', destructive: true, params: { id: { t: 'int', req: true } }, steps: [{ entity_delete: e.name }] };
  }
  #checkQuery(name, qd) {
    for (const k of Object.keys(qd.sort || {})) if (!IDENT.test(k)) throw new AppError('config', `query ${name}: bad sort key ${k}`);
  }
  entity(name) { const e = this.entities[name]; if (!e || e.hidden) throw new AppError('validation', `Unknown entity ${name}`, { fields: { entity: 'unknown' } }); return e; }
  describe() {
    const fl = (fs) => Object.values(fs).map(({ c, l, t, req, len, uniq, ro, min, max }) => ({ c, l, t, req: !!req, len, uniq: !!uniq, ro: !!ro, min, max }));
    return {
      app: this.desc.app, title: this.desc.title, version: this.desc.version,
      entities: Object.values(this.entities).filter((e) => !e.hidden).map((e) => ({ name: e.name, single: e.single, key: e.key, ops: e.ops, fields: fl(e.fields), children: Object.fromEntries(Object.entries(e.children || {}).map(([n, c]) => [n, { entity: c.entity, fields: fl(c.fields) }])) })),
      queries: Object.entries(this.queries).map(([n, qd]) => ({ name: n, title: qd.title, kind: qd.parts ? 'object' : 'list', params: qd.params || {}, filters: Object.keys(qd.filters || {}), sort: Object.keys(qd.sort || {}), search: !!qd.search })),
      lookups: Object.keys(this.lookupsDef),
      operations: Object.values(this.operations).map((o) => ({ name: o.name, title: o.title, destructive: !!o.destructive, params: o.params || {} })),
    };
  }

  // ---- values ---------------------------------------------------------------------------------------------
  /** input value -> database value of a field; errors are collected per field */
  conv(f, v, errs, key) {
    if (v === undefined || v === null || (typeof v === 'string' && v.trim() === '')) {
      if (f.req) errs[key] = this.t('required', { label: f.l });
      return null;
    }
    let out = v;
    if (f.t === 'int' || f.t === 'money' || f.t === 'number') {
      const n = typeof v === 'number' ? v : Number(String(v).trim().replace(',', '.'));
      if (!Number.isFinite(n)) { errs[key] = this.t('not_number'); return null; }
      if (f.t === 'int' && !Number.isInteger(n)) { errs[key] = this.t('not_int'); return null; }
      if (f.min !== undefined && n < f.min) errs[key] = this.t('min', { min: f.min });
      if (f.max !== undefined && n > f.max) errs[key] = this.t('max', { max: f.max });
      out = n;
    } else if (f.t === 'date') {
      const s = String(v).trim();
      if (!/^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?$/.test(s) || Number.isNaN(Date.parse(s.slice(0, 10)))) { errs[key] = this.t('not_date'); return null; }
      out = s.replace('T', ' ');
    } else if (f.t === 'bool') {
      out = v === true || v === 1 || v === '1' || v === 'true';
    } else if (f.t === 'array') {
      // list of objects (e.g. order lines); every item is checked against f.items, errors as key.i.field
      if (!Array.isArray(v)) { errs[key] = this.t('unknown_field'); return null; }
      if (f.min_items && v.length < f.min_items) errs[key] = this.t('min', { min: f.min_items });
      if (f.max_items && v.length > f.max_items) errs[key] = this.t('max', { max: f.max_items });
      out = v.map((item, i) => {
        const o = {};
        if (!item || typeof item !== 'object' || Array.isArray(item)) { errs[`${key}.${i}`] = this.t('unknown_field'); return o; }
        for (const k of Object.keys(item)) if (!(f.items || {})[k]) errs[`${key}.${i}.${k}`] = this.t('unknown_field');
        for (const [k, fi] of Object.entries(f.items || {})) o[k] = this.conv({ l: k, ...fi }, item[k], errs, `${key}.${i}.${k}`);
        return o;
      });
    } else if (f.t === 'object') {
      if (typeof v !== 'object' || Array.isArray(v)) { errs[key] = this.t('unknown_field'); return null; }
    } else {
      out = String(v);
      if (f.t !== 'memo') out = out.trim();
      if (f.len && out.length > f.len) errs[key] = this.t('too_long', { len: f.len });
    }
    return out;
  }
  params(op, input) {
    const errs = {}; const out = {};
    for (const k of Object.keys(input || {})) if (!(op.params || {})[k]) errs[k] = this.t('unknown_field');
    for (const [k, f] of Object.entries(op.params || {})) out[k] = this.conv({ l: k, ...f }, input?.[k], errs, k);
    if (Object.keys(errs).length) throw new AppError('validation', Object.values(errs)[0], { fields: errs });
    return out;
  }

  // ---- reads ----------------------------------------------------------------------------------------------
  async q(c, text, scope, values) { const [t, v] = values ? [text, values] : compile(text, scope); return (await c.query(t, v)).rows; }

  async query(input) {
    const name = input?.name; const qd = this.queries[name];
    if (!qd) throw new AppError('validation', `Unknown query ${name}`, { fields: { name: 'unknown' } });
    const params = this.params({ params: qd.params || {} }, input.params || {});
    const scope = { params, ...params, actor: input.actor ?? null, actor_id: input.actor_id ?? null };
    if (qd.call) return this.#callQuery(qd, params, input);
    if (qd.parts) {
      const out = {};
      for (const [k, p] of Object.entries(qd.parts)) {
        const rows = await this.q(this.db, p.sql, scope);
        out[k] = p.one ? rows[0] ?? null : rows;
      }
      return out;
    }
    const s = new Sql(); const cond = [];
    for (const [k, clause] of Object.entries(qd.where || {})) if (params[k] !== null && params[k] !== undefined) cond.push(s.fragment(clause, scope));
    if (input.filter) {
      const fc = (qd.filters || {})[input.filter];
      if (fc === undefined) throw new AppError('validation', `Unknown filter ${input.filter}`, { fields: { filter: 'unknown' } });
      if (fc) cond.push(s.fragment(fc, scope));
    }
    if (input.q && String(input.q).trim() && qd.search) {
      const like = s.param('%' + String(input.q).trim().replace(/[\\%_]/g, (m) => '\\' + m) + '%');
      const exact = qd.search.exact?.length ? s.param(String(input.q).trim()) : null;
      const coll = /^[\w-]+$/.test(this.desc.search_collation || '') ? ` COLLATE "${this.desc.search_collation}"` : '';
      cond.push('(' + [...(qd.search.like || []).map((c) => `${c}::text${coll} ILIKE ${like}`), ...(qd.search.exact || []).map((c) => `${c}::text = ${exact}`)].join(' OR ') + ')');
    }
    const base = s.fragment(qd.sql, scope);
    const where = cond.length ? ' WHERE ' + cond.join(' AND ') : '';
    const sortKey = input.sort || qd.default_sort;
    if (sortKey && !(qd.sort || {})[sortKey]) throw new AppError('validation', `Unknown sort ${sortKey}`, { fields: { sort: 'unknown' } });
    const dir = (input.dir || qd.default_dir || 'asc').toLowerCase() === 'desc' ? 'DESC' : 'ASC';
    const order = sortKey ? ` ORDER BY ${qd.sort[sortKey]} ${dir} NULLS LAST${qd.tiebreak ? ', ' + qd.tiebreak : ''}` : qd.tiebreak ? ` ORDER BY ${qd.tiebreak}` : '';
    const size = Math.max(1, Math.min(+input.size || qd.size || 12, qd.max_size || 1000));
    const head = (await this.db.query(`SELECT count(*)::int AS n${qd.sum ? `, coalesce(sum(${qd.sum}), 0) AS s` : ''} FROM (${base}) t${where}`, s.values)).rows[0];
    const pages = Math.max(1, Math.ceil(head.n / size));
    const page = Math.max(1, Math.min(Math.floor(+input.page || 1), pages));
    const rows = (await this.db.query(`SELECT * FROM (${base}) t${where}${order} LIMIT ${size} OFFSET ${(page - 1) * size}`, s.values)).rows;
    return { rows, total: head.n, page, size, pages, ...(qd.sum ? { sum: head.s } : {}) };
  }

  // ---- hooks: application code of the description (desc.hooks = module file, relative to the apps folder) ------
  // reloaded when the file changes; a hook gets { client, params, actor, preview, step|query, input, AppError }
  async hooks() {
    if (!this.desc.hooks) throw new AppError('config', 'description error: no hooks module');
    const dir = this.desc.hooks_dir || path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'apps');
    const file = path.isAbsolute(this.desc.hooks) ? this.desc.hooks : path.join(dir, this.desc.hooks);
    const m = fs.statSync(file).mtimeMs;
    if (!this.hooksMod || this.hooksM !== m) {
      const mod = await import(pathToFileURL(file).href + '?v=' + m);
      this.hooksMod = mod.default || mod; this.hooksM = m;
    }
    return this.hooksMod;
  }
  async #hook(name) {
    const h = await this.hooks();
    if (typeof h[name] !== 'function') throw new AppError('config', `description error: unknown hook ${name}`);
    return h[name].bind(h);
  }
  /** query answered by a hook, inside a read-only transaction (a hook can never write from a query) */
  async #callQuery(qd, params, input) {
    const fn = await this.#hook(qd.call);
    let c;
    try { c = await this.db.connect(); } catch (e) { throw mapPg(e, this); }
    try {
      await c.query('BEGIN READ ONLY');
      return await fn({ client: c, params, actor: input.actor ?? null, preview: false, readonly: true, query: qd, input, AppError });
    } catch (e) { throw e instanceof AppError ? e : mapPg(e, this); } finally { await c.query('ROLLBACK').catch(() => {}); c.release(); }
  }
  /** client for hook code in a write: INSERT/UPDATE/DELETE of declared tables are tracked like steps (rows locked and
   *  kept for the conflict check, provisional keys in the preview); reads pass through */
  #tracked(x) {
    // WHERE clause of the statement itself: the first WHERE outside parentheses and quotes (SET a = (SELECT .. WHERE ..)
    // keeps its subquery); m[1] = table, m[2] = condition
    const topWhere = (head, sql) => {
      const h = head.exec(sql);
      if (!h) return null;
      let depth = 0; let quote = null;
      for (let i = h[0].length - 1; i < sql.length; i++) {
        const ch = sql[i];
        if (quote) { if (ch === quote) quote = null; continue; }
        if (ch === "'" || ch === '"') quote = ch;
        else if (ch === '(') depth++;
        else if (ch === ')') depth--;
        else if (!depth && /\s/.test(ch)) {
          const w = /^\sWHERE\s+/i.exec(sql.slice(i, i + 12));
          if (w) return [sql, h[1], sql.slice(i + w[0].length)];
        }
      }
      return null;
    };
    const eng = this; const c = x.c;
    const ent = (t) => { const e = eng.byTable[t]; if (!e) throw new AppError('config', `description error: table ${t} is not declared`); return e; };
    const lockRows = async (t, where, args) => {
      const e = ent(t);
      const used = [...new Set([...where.matchAll(/\$(\d+)/g)].map((m) => +m[1]))];
      const map = new Map(used.map((n, i) => [n, i + 1]));
      const w = where.replace(/\$(\d+)/g, (m, n) => '$' + map.get(+n));
      const rows = (await c.query(`SELECT * FROM ${t} WHERE ${w} ORDER BY ${keyExpr(e)} FOR UPDATE`, used.map((n) => args[n - 1]))).rows;
      for (const r of rows) if (!x.changes.has(`${e.table}:${keyOf(e, r)}`)) { x.before.push(r); eng.#track(x, e, keyOf(e, r), r); }
    };
    return {
      async query(text, args = []) {
        const sql = String(typeof text === 'string' ? text : text?.text).trim();
        let m;
        if ((m = /^INSERT\s+INTO\s+(\w+)\s*\(([^)]*)\)\s*VALUES\s*\(([\s\S]*)\)\s*(?:RETURNING\s+[\w\s,*]+)?$/i.exec(sql))) {
          const e = ent(m[1]); const cols = m[2].split(',').map((v) => v.trim()); const a = [...args]; let vals = m[3]; let over = '';
          if (x.preview && e.identity && !Array.isArray(e.key) && !cols.includes(e.key)) { a.push(-(++x.prov)); cols.push(e.key); vals += ', $' + a.length; over = ' OVERRIDING SYSTEM VALUE'; }
          const r = await c.query(`INSERT INTO ${m[1]} (${cols.join(', ')})${over} VALUES (${vals}) RETURNING *`, a);
          for (const row of r.rows) eng.#track(x, e, keyOf(e, row), null);
          return r;
        }
        if ((m = topWhere(/^UPDATE\s+(\w+)\s+SET\s/i, sql)) || (m = /^DELETE\s+FROM\s+(\w+)\s+WHERE\s+([\s\S]*)$/i.exec(sql))) {
          await lockRows(m[1], m[2], args);
          return c.query(text, args);
        }
        if (/^(UPDATE|DELETE|INSERT|MERGE|TRUNCATE|COPY|CREATE|ALTER|DROP|GRANT|REVOKE|COMMIT|ROLLBACK|BEGIN|START|SET|RESET|CALL|DO)\b/i.test(sql)) throw new AppError('config', 'description error: statement not allowed in hook code: ' + sql.slice(0, 40));
        return c.query(text, args);
      },
    };
  }
  #mask(table, row) {
    const r = this.redact[table];
    if (!r || !row) return row;
    return Object.fromEntries(Object.entries(row).map(([k, v]) => [k, r.has(k) && v != null ? '•••' : v]));
  }
  /** parameters for the audit log: secret parameters and secret keys inside object parameters are masked */
  safeParams(op, params) {
    const keys = new Set(this.desc.secret_keys || []);
    const deep = (v) => (Array.isArray(v) ? v.map(deep) : v && typeof v === 'object' ? Object.fromEntries(Object.entries(v).map(([k, y]) => [k, keys.has(k) && y != null ? '•••' : deep(y)])) : v);
    return Object.fromEntries(Object.entries(params || {}).map(([k, v]) => [k, op.params?.[k]?.secret && v != null ? '•••' : deep(v)]));
  }

  async lookups(input) {
    const names = input?.names?.length ? input.names : Object.keys(this.lookupsDef);
    const out = {};
    for (const n of names) {
      if (!this.lookupsDef[n]) throw new AppError('validation', `Unknown lookup ${n}`, { fields: { names: n } });
      out[n] = await this.q(this.db, this.lookupsDef[n], {});
    }
    return out;
  }

  /** select list of an entity: * or, with e.omit (e.g. picture or blob columns), every other column */
  async cols(c, e) {
    if (!e.omit?.length) return '*';
    if (!this.colCache.has(e.table)) {
      const r = await c.query('SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = $1 ORDER BY ordinal_position', [e.table]);
      if (!r.rows.length) throw new AppError('config', `entity ${e.name}: table ${e.table} not found`);
      this.colCache.set(e.table, r.rows.map((x) => x.column_name).filter((n) => !e.omit.includes(n)).map((n) => '"' + n.replace(/"/g, '""') + '"').join(', '));
    }
    return this.colCache.get(e.table);
  }
  async baseRow(c, e, id, lock) {
    const rows = await this.q(c, `SELECT ${await this.cols(c, e)} FROM ${e.table} WHERE ${keyExpr(e)} = $1${lock ? ' FOR UPDATE' : ''}`, null, [Array.isArray(e.key) ? String(id) : id]);
    return rows[0] ?? null;
  }
  async getRecord(input) {
    const e = this.entity(input?.entity);
    const id = this.params({ params: { id: { t: 'int', req: true } } }, { id: input.id }).id;
    const base = await this.baseRow(this.db, e, id);
    if (!base) throw new AppError('not_found', this.t('not_found', { single: e.single }));
    const scope = { id };
    const row = e.record?.sql ? (await this.q(this.db, e.record.sql, scope))[0] : base;
    const children = {};
    for (const [n, sql] of Object.entries(e.record?.children || {})) children[n] = await this.q(this.db, sql, scope);
    const extras = {};
    for (const [n, p] of Object.entries(e.record?.extras || {})) { const r = await this.q(this.db, p.sql, scope); extras[n] = p.one ? r[0] ?? null : r; }
    return { entity: e.name, id, row, version: this.version(base, children), children, extras };
  }
  /** version of a record = hash of the table row and its child rows */
  version(base, children) { return hash({ base, children: children || {} }); }

  // ---- writes ---------------------------------------------------------------------------------------------
  op(name) { const o = this.operations[name]; if (!o) throw new AppError('validation', `Unknown operation ${name}`, { fields: { operation: 'unknown' } }); return o; }

  /** preview: run the operation in a transaction, collect before/after rows, roll back, hand out a token */
  async plan(input) {
    const op = this.op(input?.operation);
    const params = this.params(op, input.params || {});
    const x = await this.#execute(op, params, { preview: true, expected: input.expected, actor: input.actor ?? null, frozen: [] });
    const token = crypto.randomBytes(24).toString('base64url');
    const now = Date.now();
    for (const [k, v] of this.tokens) if (v.expires < now) this.tokens.delete(k);
    this.tokens.set(token, { op: op.name, params, expected: input.expected ?? null, actor: input.actor ?? null, frozen: x.frozen, beforeHash: hash(x.before), expires: now + TOKEN_TTL_MS, destructive: !!op.destructive });
    const changes = x.changes;
    const counts = {};
    for (const ch of changes) { const k = `${ch.table}.${ch.action}`; counts[k] = (counts[k] || 0) + 1; }
    return {
      token, operation: op.name, title: op.title, destructive: !!op.destructive, expires_at: new Date(now + TOKEN_TTL_MS).toISOString(),
      tool: op.destructive ? 'run_destructive_operation' : 'run_operation',
      summary: Object.entries(counts).map(([k, n]) => `${k.replace('.', ': ')} ${n}`).join(', ') || 'no change',
      counts, changes: changes.slice(0, MAX_CHANGES), changes_total: changes.length, result: x.result,
    };
  }

  /** the write: exactly one transaction, only with a valid unused token of a plan */
  async run(input, { destructive }) {
    const rid = input?.request_id ?? null;
    if (rid && this.audit) {
      const prev = await this.audit.outcome(rid, this.db);
      if (prev.state === 'committed') return { ...prev.result, replayed: true };
    }
    const tk = this.tokens.get(input?.token);
    if (!tk || tk.used || tk.expires < Date.now()) throw new AppError('token_invalid', this.t('token_invalid'));
    if (tk.destructive !== destructive) throw new AppError('validation', this.t('wrong_tool', { tool: tk.destructive ? 'run_destructive_operation' : 'run_operation' }), { fields: { token: 'tool' } });
    tk.used = true; // one-time, also when the write fails
    const op = this.op(tk.op);
    const x = await this.#execute(op, tk.params, { preview: false, expected: tk.expected, actor: tk.actor, frozen: tk.frozen, beforeHash: tk.beforeHash, request_id: rid });
    return x.result;
  }

  async #execute(op, params, o) {
    let c;
    try { c = await this.db.connect(); } catch (e) { throw mapPg(e, this); }
    const x = { c, preview: o.preview, actor: o.actor, expected: o.expected, frozen: o.frozen, fi: 0, before: [], seen: new Set(), changes: new Map(), prov: 0, result: {}, collect: {} };
    const scope = { params, ...params, actor: o.actor, result: x.result, collect: x.collect };
    let began = false;
    try {
      await c.query('BEGIN'); began = true;
      const xid = o.preview ? null : (await c.query('SELECT pg_current_xact_id()::text AS x')).rows[0].x;
      await this.#steps(op.steps, scope, x);
      for (const ch of x.changes.values()) ch.after = await this.baseRow(c, this.entities[ch.entity], ch.key);
      x.changes = [...x.changes.values()].filter((ch) => stable(ch.before) !== stable(ch.after)).map((ch) => ({
        table: ch.table, entity: ch.entity, key: ch.key, action: !ch.before ? 'insert' : !ch.after ? 'delete' : 'update',
        changed: ch.before && ch.after ? Object.keys(ch.after).filter((k) => !same(ch.before[k], ch.after[k])) : undefined, before: this.#mask(ch.table, ch.before), after: this.#mask(ch.table, ch.after),
      }));
      if (!o.preview) {
        if (hash(x.before) !== o.beforeHash) throw new AppError('conflict', this.t('conflict'), { current: x.before[0] ?? null });
        const result = { ok: true, operation: op.name, ...x.result, changes: x.changes.length };
        if (this.audit) await this.audit.begin({ request_id: o.request_id, xid, operation: op.name, actor: o.actor, params: this.safeParams(op, params), result, changes: x.changes.map((ch) => ({ table: ch.table, key: ch.key, action: ch.action })) });
        await c.query('COMMIT'); began = false;
        if (this.audit) await this.audit.commit({ request_id: o.request_id, xid });
        x.result = result;
      } else {
        await c.query('ROLLBACK'); began = false;
      }
      return x;
    } catch (e) {
      if (began) await c.query('ROLLBACK').catch(() => {});
      if (!o.preview && this.audit) await this.audit.rollback({ request_id: o.request_id, operation: op.name, error: e.message }).catch(() => {});
      throw e instanceof AppError ? e : mapPg(e, this);
    } finally { c.release(); }
  }

  /** a row read before the change binds the token (x.before); rows created or already read in this operation do not,
   *  because their values (keys of new rows) differ between preview and write */
  #read(x, e, row) {
    const k = `${e.table}:${keyOf(e, row)}`;
    if (x.seen.has(k)) return;
    x.seen.add(k); x.before.push(row);
  }
  #track(x, e, key, before) {
    const k = `${e.table}:${key}`;
    if (!x.changes.has(k)) x.changes.set(k, { table: e.table, entity: e.name, key, before });
  }
  #freeze(x, compute) {
    if (x.preview) return compute().then((v) => { x.frozen.push(v); return v; });
    if (x.fi >= x.frozen.length) throw new AppError('conflict', this.t('conflict'));
    return Promise.resolve(x.frozen[x.fi++]);
  }

  async #steps(steps, scope, x) { for (const s of steps) await this.#step(s, scope, x); }

  async #step(s, scope, x) {
    const c = x.c;
    if (s.lock) {
      const e = this.entity(s.lock); const id = valueOf(s.id, scope);
      const row = await this.baseRow(c, e, id, true);
      if (!row) throw new AppError('not_found', s.message ? this.#msg(s.message, scope) : this.t('not_found', { single: e.single }));
      this.#read(x, e, row);
      if (s.primary) this.#expect(row, x.expected);
      scope[s.as || 'row'] = row;
    } else if (s.call) {
      // application code: the hook writes through the tracked client; its result object goes into the result
      const fn = await this.#hook(s.call);
      const r = await fn({ client: this.#tracked(x), params: scope.params, actor: x.actor, preview: x.preview, step: s, AppError, expected: x.expected, expect: (row) => this.#expect(row, x.expected) });
      if (r && typeof r === 'object' && !Array.isArray(r) && s.result !== false) Object.assign(x.result, r);
      if (s.as) scope[s.as] = r;
    } else if (s.check) {
      const r = await this.q(c, s.check, scope);
      const v = r[0] ? Object.values(r[0])[0] : null;
      if (truthy(v)) { const m = this.#msg(s.message, scope); throw new AppError(s.code || 'validation', m, { fields: s.fields ? Object.fromEntries(s.fields.map((f) => [f, m])) : undefined }); }
    } else if (s.sql) {
      const run = async () => {
        if (s.touches) {
          const e = this.entity(s.touches);
          const rows = await this.q(c, `SELECT ${await this.cols(c, e)} FROM ${e.table} WHERE ${s.where} ORDER BY ${keyExpr(e)} FOR UPDATE`, scope);
          for (const r of rows) { this.#read(x, e, r); this.#track(x, e, keyOf(e, r), r); }
        }
        const r = await this.q(c, s.sql, scope);
        return s.one ? r[0] ?? null : r;
      };
      const v = s.freeze ? await this.#freeze(x, run) : await run();
      if (s.as) scope[s.as] = v;
    } else if (s.if) {
      const r = await this.q(c, s.if, scope);
      await this.#steps(truthy(r[0] ? Object.values(r[0])[0] : null) ? s.then || [] : s.else || [], scope, x);
    } else if (s.repeat) {
      const n = Number(valueOf(s.repeat, scope)) || 0;
      for (let i = 0; i < n; i++) { scope.i = i; await this.#steps(s.do, scope, x); }
    } else if (s.foreach) {
      for (const item of valueOf(s.foreach, scope) || []) { scope[s.as || 'item'] = item; await this.#steps(s.do, scope, x); }
    } else if (s.insert) {
      const e = this.entity(s.insert);
      const key = await this.#insert(c, e, s.values, scope, x, s.audit !== false);
      if (s.as) scope[s.as] = { [e.key]: key };
      if (s.collect) (x.collect[s.collect] ||= []).push(key);
      if (s.collect && s.result !== false) x.result[s.collect] = x.collect[s.collect];
    } else if (s.update) {
      const e = this.entity(s.update); const id = valueOf(s.id, scope);
      const before = await this.baseRow(c, e, id, true);
      if (!before) throw new AppError('not_found', this.t('not_found', { single: e.single }));
      this.#read(x, e, before); this.#track(x, e, id, before);
      await this.#updateSql(c, e, id, s.set, scope, s.audit !== false);
    } else if (s.delete) {
      const e = this.entity(s.delete); const id = valueOf(s.id, scope);
      const before = await this.baseRow(c, e, id, true);
      if (!before) throw new AppError('not_found', this.t('not_found', { single: e.single }));
      this.#read(x, e, before); this.#track(x, e, id, before);
      await c.query(`DELETE FROM ${e.table} WHERE ${keyExpr(e)} = $1`, [id]);
    } else if (s.entity_create) await this.#entityCreate(this.entity(s.entity_create), scope, x);
    else if (s.entity_update) await this.#entityUpdate(this.entity(s.entity_update), scope, x);
    else if (s.entity_delete) await this.#entityDelete(this.entity(s.entity_delete), scope, x);
    else if (s.use) {
      const f = this.fragments[s.use];
      if (!f) throw new AppError('config', 'description error: unknown fragment ' + s.use);
      await this.#steps(f, scope, x);
    } else if (s.result) { for (const [k, v] of Object.entries(s.result)) x.result[k] = valueOf(v, scope); }
    else throw new AppError('config', 'description error: unknown step ' + Object.keys(s).join(','));
  }

  /** message of the description; {a.b} is replaced by the value of :a.b in the scope */
  #msg(m, scope) { return String(m ?? '').replace(/\{([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\}/g, (t, n) => { try { return String(resolve(scope, n) ?? ''); } catch { return t; } }); }

  #expect(row, expected) {
    if (!expected) return;
    const diff = Object.entries(expected).filter(([k, v]) => k in row && !same(row[k], v)).map(([k]) => k);
    const unknown = Object.keys(expected).filter((k) => !(k in row));
    if (unknown.length) throw new AppError('validation', this.t('unknown_field'), { fields: Object.fromEntries(unknown.map((k) => [k, this.t('unknown_field')])) });
    if (diff.length) throw new AppError('conflict', this.t('conflict'), { current: row, fields: Object.fromEntries(diff.map((k) => [k, String(row[k] ?? '')])) });
  }

  async #insert(c, e, values, scope, x, withAudit) {
    const s = new Sql(); const cols = []; const vals = [];
    for (const [k, v] of Object.entries(values || {})) { if (!IDENT.test(k)) throw new AppError('config', 'bad column ' + k); cols.push(k); vals.push(valueSql(s, v, scope)); }
    if (withAudit) for (const [k, v] of Object.entries(auditOf(e, this.desc)?.insert || {})) if (!cols.includes(k)) { cols.push(k); vals.push(valueSql(s, v, scope)); }
    let over = '';
    // preview: negative keys instead of sequence values (identity key, or the columns of e.preview_keys)
    const pk = e.preview_keys || (e.identity ? [e.key] : []);
    if (x.preview && pk.length) { const n = -(++x.prov); for (const k of pk) if (!cols.includes(k)) { cols.unshift(k); vals.unshift(s.param(n)); } over = ' OVERRIDING SYSTEM VALUE'; }
    const r = await c.query(`INSERT INTO ${e.table} (${cols.join(', ')})${over} VALUES (${vals.join(', ')}) RETURNING ${[].concat(e.key).join(', ')}`, s.values);
    const key = keyOf(e, r.rows[0]);
    x.seen.add(`${e.table}:${key}`);
    this.#track(x, e, key, null);
    return key;
  }
  async #updateSql(c, e, id, set, scope, withAudit) {
    const s = new Sql(); const parts = [];
    for (const [k, v] of Object.entries(set || {})) { if (!IDENT.test(k)) throw new AppError('config', 'bad column ' + k); parts.push(`${k} = ${valueSql(s, v, scope)}`); }
    if (!parts.length) return;
    if (withAudit) for (const [k, v] of Object.entries(auditOf(e, this.desc)?.update || {})) if (!(k in (set || {}))) parts.push(`${k} = ${valueSql(s, v, scope)}`);
    await c.query(`UPDATE ${e.table} SET ${parts.join(', ')} WHERE ${keyExpr(e)} = ${s.param(Array.isArray(e.key) ? String(id) : id)}`, s.values);
  }

  /** input values of an entity -> converted columns; unknown and read-only columns are refused */
  #values(e, input, errs, isNew) {
    const v = {};
    for (const k of Object.keys(input || {})) {
      if (e.children?.[k]) continue;
      const f = e.fields[k];
      if (!f || f.ro || (k === e.key && !(isNew && e.manual_key))) { errs[k] = this.t('unknown_field'); continue; }
      v[k] = this.conv(f, input[k], errs, k);
    }
    if (isNew) for (const f of Object.values(e.fields)) if (f.req && !(f.c in v) && e.defaults?.[f.c] === undefined && !f.ro && (f.c !== e.key || e.manual_key)) errs[f.c] = this.t('required', { label: f.l });
    return v;
  }
  async #unique(c, e, merged, id, errs) {
    for (const f of Object.values(e.fields)) {
      if (!f.uniq || merged[f.c] == null || errs[f.c]) continue;
      const r = await c.query(`SELECT count(*)::int AS n FROM ${e.table} WHERE ${f.c} = $1 AND ${e.key} IS DISTINCT FROM $2`, [merged[f.c], id ?? null]);
      if (r.rows[0].n) errs[f.c] = this.t('unique', { label: f.l, value: merged[f.c] });
    }
  }
  #fail(errs) { if (Object.keys(errs).length) throw new AppError('validation', Object.values(errs)[0], { fields: errs }); }

  async #entityCreate(e, scope, x) {
    const errs = {}; const v = this.#values(e, scope.params.values, errs, true);
    const lines = await this.#childInput(e, scope.params.values, errs);
    const merged = { ...v };
    for (const [k, d] of Object.entries(e.defaults || {})) if (merged[k] == null && typeof d !== 'object') merged[k] = d;
    await this.#unique(x.c, e, merged, null, errs);
    this.#fail(errs);
    Object.assign(scope, { values: merged, row: null, id: null });
    await this.#steps(e.checks || [], scope, x);
    const vals = {};
    for (const [k, d] of Object.entries(e.defaults || {})) if (v[k] == null) vals[k] = d;
    for (const [k, val] of Object.entries(v)) if (val !== null) vals[k] = ':values.' + k;
    const key = await this.#insert(x.c, e, vals, scope, x, true);
    scope.new = { [e.key]: key }; x.result.id = key;
    await this.#syncChildren(e, key, lines, scope, x);
  }
  async #entityUpdate(e, scope, x) {
    const id = scope.params.id;
    const row = await this.baseRow(x.c, e, id, true);
    if (!row) throw new AppError('not_found', this.t('not_found', { single: e.single }));
    this.#read(x, e, row); this.#track(x, e, id, row);
    this.#expect(row, x.expected);
    const errs = {}; const v = this.#values(e, scope.params.values, errs, false);
    const lines = await this.#childInput(e, scope.params.values, errs);
    const merged = { ...row, ...v };
    for (const f of Object.values(e.fields)) if (f.req && f.c in v && v[f.c] == null) errs[f.c] = this.t('required', { label: f.l });
    await this.#unique(x.c, e, merged, id, errs);
    this.#fail(errs);
    Object.assign(scope, { values: merged, row, id });
    await this.#steps(e.guards?.update || [], scope, x);
    await this.#steps(e.checks || [], scope, x);
    const changed = Object.keys(v).filter((k) => !same(v[k], row[k]));
    await this.#updateSql(x.c, e, id, Object.fromEntries(changed.map((k) => [k, ':values.' + k])), scope, true);
    x.result.id = id;
    await this.#syncChildren(e, id, lines, scope, x);
  }
  async #entityDelete(e, scope, x) {
    const id = scope.params.id;
    const row = await this.baseRow(x.c, e, id, true);
    if (!row) throw new AppError('not_found', this.t('not_found', { single: e.single }));
    this.#read(x, e, row); this.#track(x, e, id, row);
    this.#expect(row, x.expected);
    Object.assign(scope, { row, id });
    await this.#steps(e.guards?.delete || [], scope, x);
    for (const [cn, ch] of Object.entries(e.children || {})) {
      const ce = this.entity(ch.entity);
      const rows = await this.q(x.c, `SELECT ${await this.cols(x.c, ce)} FROM ${ce.table} WHERE ${ch.fk} = $1 ORDER BY ${keyExpr(ce)} FOR UPDATE`, null, [id]);
      for (const r of rows) { this.#read(x, ce, r); this.#track(x, ce, keyOf(ce, r), r); }
      await x.c.query(`DELETE FROM ${ce.table} WHERE ${ch.fk} = $1`, [id]);
      void cn;
    }
    await x.c.query(`DELETE FROM ${e.table} WHERE ${e.key} = $1`, [id]);
    x.result.id = id;
  }

  async #childInput(e, input, errs) {
    const out = {};
    for (const [cn, ch] of Object.entries(e.children || {})) {
      if (!(cn in (input || {}))) continue;
      const rows = input[cn];
      if (!Array.isArray(rows)) { errs[cn] = this.t('unknown_field'); continue; }
      const ce = this.entity(ch.entity); const seen = new Set();
      out[cn] = rows.map((r, i) => {
        const v = {}; const fe = {};
        for (const k of Object.keys(r || {})) if (k !== ce.key && !ch.fields[k]) fe[k] = this.t('unknown_field');
        for (const f of Object.values(ch.fields)) v[f.c] = this.conv(f, r?.[f.c], fe, f.c);
        const sig = (ch.unique || []).map((k) => String(v[k])).join('|');
        if (ch.unique?.length && seen.has(sig)) fe[ch.unique[0]] = ch.duplicate || this.t('duplicate_child');
        seen.add(sig);
        for (const [k, m] of Object.entries(fe)) errs[`${cn}.${i}.${k}`] = m;
        return { key: !Array.isArray(ce.key) && r?.[ce.key] != null ? Number(r[ce.key]) : null, v };
      });
    }
    return out;
  }
  async #syncChildren(e, parentKey, input, scope, x) {
    for (const [cn, rows] of Object.entries(input)) {
      const ch = e.children[cn]; const ce = this.entity(ch.entity);
      const existing = await this.q(x.c, `SELECT ${await this.cols(x.c, ce)} FROM ${ce.table} WHERE ${ch.fk} = $1 ORDER BY ${keyExpr(ce)} FOR UPDATE`, null, [parentKey]);
      const byKey = new Map(existing.map((r) => [keyNum(ce, r), r]));
      for (const r of existing) this.#read(x, ce, r);
      // a line without key but with the unique values of a stored line is that line (e.g. the same product)
      const sig = (r) => (ch.unique || []).map((k) => String(r[k] ?? '')).join('|');
      const bySig = new Map(ch.unique?.length ? existing.map((r) => [sig(r), r]) : []);
      const plan = rows.map(({ key, v }) => ({ v, old: key != null ? byKey.get(key) : bySig.get(sig(v)) }));
      const keep = new Set(plan.filter((p) => p.old).map((p) => keyNum(ce, p.old)));
      // order: delete, then update, then insert (unique indexes over the lines stay valid on the way)
      for (const r of existing) if (!keep.has(keyNum(ce, r))) { this.#track(x, ce, keyOf(ce, r), r); await x.c.query(`DELETE FROM ${ce.table} WHERE ${keyExpr(ce)} = $1`, [keyOf(ce, r)]); }
      for (const { v, old } of plan) {
        if (!old) continue;
        const key = keyNum(ce, old);
        const changed = Object.keys(v).filter((k) => !same(v[k], old[k]) && !(typeof v[k] === 'number' && Number(old[k]) === v[k]));
        if (changed.length) { this.#track(x, ce, key, old); await this.#updateSql(x.c, ce, key, Object.fromEntries(changed.map((k) => [k, v[k]])), scope, true); }
      }
      for (const { v, old } of plan) if (!old) await this.#insert(x.c, ce, { ...v, [ch.fk]: parentKey }, scope, x, true);
    }
  }
}

/** PostgreSQL / network error -> error of the tool (code + message of EQLServer in its own words) */
export function mapPg(e, eng) {
  const t = (k, v) => (eng ? eng.t(k, v) : k);
  const net = ['ECONNREFUSED', 'ECONNRESET', 'ETIMEDOUT', 'EHOSTUNREACH', 'ENOTFOUND', 'EPIPE'];
  if (net.includes(e?.code) || /timeout exceeded when trying to connect|Connection terminated/i.test(e?.message || '')) return new AppError('unavailable', t('unavailable', { detail: e.code || e.message }), { retryable: true });
  if (e?.code === '57P01' || e?.code === '57P03' || e?.code === '08006' || e?.code === '08001') return new AppError('unavailable', t('unavailable', { detail: e.message }), { retryable: true });
  if (e?.code === '28P01' || e?.code === '28000' || e?.code === '42501') return new AppError('forbidden', e.message);
  if (e?.code === '40001' || e?.code === '40P01' || e?.code === '55P03') return new AppError('conflict', e.message);
  if (/^2[23]/.test(e?.code || '')) return new AppError('validation', e.message, { fields: e.column ? { [e.column]: e.message } : undefined });
  return new AppError('db_error', e?.message || String(e));
}
