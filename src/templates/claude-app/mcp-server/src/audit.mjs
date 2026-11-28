// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Audit log (JSON lines, flushed to disk before COMMIT) and the outcome of a write by request id.
// Without any table on the server: the begin record carries the transaction id (pg_current_xact_id) and the
// result; if the answer of a write got lost, pg_xact_status(xid) tells whether that transaction committed.
import fs from 'node:fs';
import path from 'node:path';

export class AuditLog {
  constructor(file) {
    this.file = file;
    this.req = new Map(); // request_id -> { xid, state, result }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    if (fs.existsSync(file)) {
      for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
        if (!line.trim()) continue;
        try { this.#apply(JSON.parse(line)); } catch { /* a torn last line after a crash */ }
      }
    }
  }
  #apply(r) {
    if (!r.request_id) return;
    const o = this.req.get(r.request_id) || {};
    if (r.t === 'begin') Object.assign(o, { xid: r.xid, state: 'pending', result: r.result });
    if (r.t === 'commit') o.state = 'committed';
    if (r.t === 'rollback' && o.state !== 'committed') o.state = 'none';
    this.req.set(r.request_id, o);
  }
  #write(r) {
    const line = JSON.stringify({ at: new Date().toISOString(), ...r }) + '\n';
    const fd = fs.openSync(this.file, 'a');
    try { fs.writeSync(fd, line); fs.fsyncSync(fd); } finally { fs.closeSync(fd); }
    this.#apply(r);
  }
  async begin(r) { this.#write({ t: 'begin', ...r }); }
  async commit(r) { this.#write({ t: 'commit', ...r }); }
  async rollback(r) { this.#write({ t: 'rollback', ...r }); }

  /** committed | none | unknown, with the result of the write when it committed */
  async outcome(requestId, db) {
    const o = this.req.get(requestId);
    if (!o || o.state === 'none') return { state: 'none' };
    if (o.state === 'committed') return { state: 'committed', result: o.result };
    try {
      const s = (await db.query('SELECT pg_xact_status($1::xid8) AS s', [o.xid])).rows[0].s;
      if (s === 'committed') { this.#write({ t: 'commit', request_id: requestId, xid: o.xid, recovered: true }); return { state: 'committed', result: o.result }; }
      if (s === 'aborted' || s === null) { this.#write({ t: 'rollback', request_id: requestId, xid: o.xid, recovered: true }); return { state: 'none' }; }
      return { state: 'unknown' };
    } catch { return { state: 'unknown' }; }
  }
}
