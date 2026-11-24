// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Core of the web application template: routing, JSON bodies, static files, error handling,
// message resources, the database pool and a transaction helper. No framework; only `pg`.
'use strict';

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

/** Error with an HTTP status and a message key of the resource files (public/i18n). */
class HttpError extends Error {
  constructor(status, code, args) {
    super(code);
    this.name = 'HttpError';
    this.status = status;
    this.code = code;
    this.args = args || {};
  }
}

// ---------------------------------------------------------------- messages

/** Reads the English resource files (template keys and application keys) of a public folder. */
function loadMessages(publicDir) {
  const messages = {};
  for (const name of ['en.json', 'app-en.json']) {
    const file = path.join(publicDir, 'i18n', name);
    if (fs.existsSync(file)) Object.assign(messages, JSON.parse(fs.readFileSync(file, 'utf8')));
  }
  return messages;
}

/** A label is a plain text, a key of the resources, or an object with one text per language. */
function resolveLabel(label, messages, lang) {
  if (label === null || label === undefined) return '';
  if (typeof label === 'object') return String(label[lang || 'en'] || label.en || '');
  const key = String(label);
  return messages && Object.prototype.hasOwnProperty.call(messages, key) ? messages[key] : key;
}

/** Text of a message key; placeholders {name} are filled from args, labels are resolved. */
function formatMessage(messages, code, args) {
  const has = messages && Object.prototype.hasOwnProperty.call(messages, code);
  const text = has ? messages[code] : String(code);
  return text.replace(/\{(\w+)\}/g, (all, name) => {
    if (!args || !Object.prototype.hasOwnProperty.call(args, name)) return all;
    const value = args[name];
    if (Array.isArray(value)) return value.join(', ');
    return name === 'label' || name === 'labelPlural' ? resolveLabel(value, messages, 'en') : String(value);
  });
}

// ---------------------------------------------------------------- routing

function compilePattern(pattern) {
  if (typeof pattern !== 'string' || !pattern.startsWith('/')) throw new Error('route pattern must start with /');
  return pattern.split('/').slice(1);
}

function matchParts(parts, segments) {
  if (parts.length !== segments.length) return null;
  const params = {};
  for (let i = 0; i < parts.length; i++) {
    if (parts[i].startsWith(':')) {
      if (segments[i] === '') return null;
      try { params[parts[i].slice(1)] = decodeURIComponent(segments[i]); } catch (e) { return null; }
    } else if (parts[i] !== segments[i]) return null;
  }
  return params;
}

/**
 * Router with patterns like '/api/entity/:name/:id'. The first route added wins.
 * match() returns { handler, params }, { methodNotAllowed: true } or null.
 */
function createRouter() {
  const routes = [];
  const router = {
    add(method, pattern, handler) {
      if (typeof handler !== 'function') throw new Error('route handler must be a function');
      routes.push({ method: String(method).toUpperCase(), parts: compilePattern(pattern), handler });
      return router;
    },
    get(pattern, handler) { return router.add('GET', pattern, handler); },
    post(pattern, handler) { return router.add('POST', pattern, handler); },
    put(pattern, handler) { return router.add('PUT', pattern, handler); },
    delete(pattern, handler) { return router.add('DELETE', pattern, handler); },
    match(method, pathname) {
      const segments = String(pathname).split('/').slice(1);
      let otherMethod = false;
      for (const route of routes) {
        const params = matchParts(route.parts, segments);
        if (!params) continue;
        if (route.method === String(method).toUpperCase()) return { handler: route.handler, params };
        otherMethod = true;
      }
      return otherMethod ? { methodNotAllowed: true } : null;
    },
  };
  return router;
}

// ---------------------------------------------------------------- bodies and answers

/** Reads a JSON object from the request body; an empty body is an empty object. */
function readJsonBody(req, limit) {
  const max = limit || 1024 * 1024;
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    let failed = false;
    req.on('data', (chunk) => {
      if (failed) return;
      size += chunk.length;
      if (size > max) { failed = true; reject(new HttpError(413, 'error.tooLarge')); return; }
      chunks.push(chunk);
    });
    req.on('error', (e) => { if (!failed) { failed = true; reject(e); } });
    req.on('end', () => {
      if (failed) return;
      const text = Buffer.concat(chunks).toString('utf8').trim();
      if (text === '') { resolve({}); return; }
      let value;
      try { value = JSON.parse(text); } catch (e) { reject(new HttpError(400, 'error.badJson')); return; }
      if (value === null || typeof value !== 'object' || Array.isArray(value)) { reject(new HttpError(400, 'error.badJson')); return; }
      resolve(value);
    });
  });
}

function sendJson(res, status, value) {
  const body = Buffer.from(JSON.stringify(value === undefined ? null : value), 'utf8');
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': body.length,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(body);
}

// ---------------------------------------------------------------- static files

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
  '.woff2': 'font/woff2',
  '.pdf': 'application/pdf',
};

function mimeType(file) {
  return MIME_TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream';
}

/**
 * Maps the path of a URL to a file below root. Returns null for everything that would leave
 * the folder or name a hidden file: '..', backslashes, drive letters, NUL, broken escapes.
 */
function resolveStaticPath(root, urlPath) {
  let decoded;
  try { decoded = decodeURIComponent(String(urlPath)); } catch (e) { return null; }
  if (!decoded.startsWith('/') || decoded.includes('\0') || decoded.includes('\\')) return null;
  const segments = decoded.split('/').filter((s) => s !== '');
  for (const s of segments) {
    if (s === '..' || s.startsWith('.') || s.includes(':')) return null;
  }
  if (segments.length === 0 || decoded.endsWith('/')) segments.push('index.html');
  const base = path.resolve(root);
  const full = path.resolve(base, ...segments);
  const relative = path.relative(base, full);
  if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) return null;
  return full;
}

const HTML_SECURITY_POLICY = "default-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";

/** Sends a file of the public folder; resolves to false when there is no such file. */
function serveStatic(root, req, res, pathname) {
  return new Promise((resolve) => {
    const file = resolveStaticPath(root, pathname);
    if (!file) { resolve(false); return; }
    fs.stat(file, (err, stat) => {
      if (err || !stat.isFile()) { resolve(false); return; }
      const type = mimeType(file);
      const headers = {
        'Content-Type': type,
        'Content-Length': stat.size,
        'Cache-Control': 'no-cache',
        'X-Content-Type-Options': 'nosniff',
      };
      if (type.startsWith('text/html')) headers['Content-Security-Policy'] = HTML_SECURITY_POLICY;
      res.writeHead(200, headers);
      if (req.method === 'HEAD') { res.end(); resolve(true); return; }
      const stream = fs.createReadStream(file);
      stream.on('error', () => { res.destroy(); resolve(true); });
      stream.on('end', () => resolve(true));
      stream.pipe(res);
    });
  });
}

// ---------------------------------------------------------------- database

/**
 * Pool of the `pg` driver. The password comes from the connection string or from PGPASSWORD.
 * Dates stay text (YYYY-MM-DD) so that no time zone can shift them; numeric stays text as well.
 */
function createPool(connectionString, options) {
  const pg = require('pg');
  pg.types.setTypeParser(1082, (value) => value);
  const opts = options || {};
  return new pg.Pool({
    connectionString,
    application_name: opts.applicationName || 'web-app',
    max: opts.max || 5,
  });
}

/** Runs fn(client) between BEGIN and COMMIT; any error rolls the transaction back. */
async function withTransaction(pool, fn) {
  const client = await pool.connect();
  try {
    await client.query('BEGIN');
    const result = await fn(client);
    await client.query('COMMIT');
    return result;
  } catch (e) {
    try { await client.query('ROLLBACK'); } catch (ignored) { /* the first error is the one to report */ }
    throw e;
  } finally {
    client.release();
  }
}

// ---------------------------------------------------------------- application

function errorAnswer(err, messages) {
  if (err instanceof HttpError) {
    return { status: err.status, body: { error: formatMessage(messages, err.code, err.args), code: err.code, args: err.args } };
  }
  // integrity constraint violations (class 23) and data exceptions (class 22) are input errors
  if (err && typeof err.code === 'string' && /^(22|23)/.test(err.code) && err.severity) {
    return { status: 400, body: { error: formatMessage(messages, 'error.database', {}), code: 'error.database', args: {} } };
  }
  return null;
}

/**
 * Request handler of an application. Routes below /api answer JSON, everything else is a file
 * of the public folder. A route handler gets a context { req, res, params, query, body() } and
 * returns the value to send (status 200, or ctx.status).
 */
function createHandler(options) {
  const router = options.router;
  const publicDir = options.publicDir;
  const messages = options.messages || {};
  const log = options.log || ((text) => process.stderr.write(text + '\n'));

  return async function handler(req, res) {
    let pathname = '/';
    try {
      const url = new URL(req.url, 'http://127.0.0.1');
      pathname = url.pathname;
      if (pathname === '/api' || pathname.startsWith('/api/')) {
        const found = router.match(req.method, pathname);
        if (!found) throw new HttpError(404, 'error.notFound');
        if (found.methodNotAllowed) throw new HttpError(405, 'error.methodNotAllowed');
        let cached;
        const ctx = {
          req, res, params: found.params, query: url.searchParams, status: 200,
          body() { if (!cached) cached = readJsonBody(req); return cached; },
        };
        const result = await found.handler(ctx);
        if (!res.headersSent) sendJson(res, ctx.status, result);
        return;
      }
      if (req.method !== 'GET' && req.method !== 'HEAD') throw new HttpError(405, 'error.methodNotAllowed');
      if (!(await serveStatic(publicDir, req, res, pathname))) throw new HttpError(404, 'error.notFound');
    } catch (err) {
      if (res.headersSent) { res.destroy(); return; }
      const answer = errorAnswer(err, messages);
      if (answer) { sendJson(res, answer.status, answer.body); return; }
      log('error ' + req.method + ' ' + pathname + ': ' + (err && err.stack ? err.stack : err));
      sendJson(res, 500, { error: formatMessage(messages, 'error.internal', {}), code: 'error.internal', args: {} });
    }
  };
}

function createServer(options) {
  return http.createServer(createHandler(options));
}

module.exports = {
  HttpError, loadMessages, resolveLabel, formatMessage,
  createRouter, readJsonBody, sendJson,
  MIME_TYPES, mimeType, resolveStaticPath, serveStatic,
  createPool, withTransaction, createHandler, createServer,
};
