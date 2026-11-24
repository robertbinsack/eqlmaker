// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Server of the read-only archive "{{APP_NAME}}": static pages and one endpoint that runs named
// SELECT statements inside a READ ONLY transaction. Nothing here can change the database.
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');

const queries = require('./queries.js');   // name -> { sql, params: [names] }
const port = Number(process.env.PORT || 8790);
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml' };

let pool = null;
function getPool() {
  if (!pool) {
    const { Pool } = require('pg');
    pool = new Pool({ connectionString: process.env['{{DB_ENV}}'] || 'postgres://127.0.0.1:5432/postgres', application_name: 'archive' });
  }
  return pool;
}

/** Only a single statement that starts with SELECT or WITH is accepted. */
function isSelect(sql) { return /^\s*(select|with)\b/i.test(sql) && !/;\s*\S/.test(sql); }

async function runQuery(name, args) {
  const q = queries[name];
  if (!q) { const e = new Error('unknown query'); e.status = 404; throw e; }
  if (!isSelect(q.sql)) { const e = new Error('only SELECT statements are allowed in the archive'); e.status = 500; throw e; }
  const client = await getPool().connect();
  try {
    await client.query('BEGIN TRANSACTION READ ONLY');
    const r = await client.query(q.sql, (q.params || []).map((p) => (args[p] === undefined ? null : args[p])));
    return r.rows;
  } finally {
    try { await client.query('ROLLBACK'); } catch (e) { /* the connection is released below */ }
    client.release();
  }
}

function send(res, status, type, body) { res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' }); res.end(body); }

/** File below base for a URL path, or null when the path leaves base or names no file. */
function resolveFile(base, urlPath) {
  const file = path.normalize(path.join(base, urlPath === '/' ? 'index.html' : decodeURIComponent(urlPath)));
  if (!file.startsWith(base + path.sep)) return null;
  if (!fs.existsSync(file) || !fs.statSync(file).isFile()) return null;
  return file;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://127.0.0.1');
    if (req.method !== 'GET') return send(res, 405, 'text/plain', 'read-only');
    if (url.pathname.startsWith('/query/')) {
      const rows = await runQuery(decodeURIComponent(url.pathname.slice(7)), Object.fromEntries(url.searchParams));
      return send(res, 200, types['.json'], JSON.stringify(rows));
    }
    const base = url.pathname.startsWith('/reference/') ? __dirname : path.join(__dirname, 'public');
    const file = resolveFile(base, url.pathname);
    if (!file) return send(res, 404, 'text/plain', 'not found');
    send(res, 200, types[path.extname(file)] || 'application/octet-stream', fs.readFileSync(file));
  } catch (e) {
    send(res, e.status || 500, 'text/plain', e.message);
  }
});

if (require.main === module) server.listen(port, '127.0.0.1', () => console.log('archive on http://127.0.0.1:' + port));
module.exports = { server, isSelect, runQuery, resolveFile };
