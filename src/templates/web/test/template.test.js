// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Tests of the template that need no database: routing, static path protection, entity SQL,
// the HTTP handler with a stand-in pool, and the language resources.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const core = require('../lib/core');
const entity = require('../lib/entity');

const root = path.join(__dirname, '..');
const publicDir = path.join(root, 'public');
const readJson = (file) => JSON.parse(fs.readFileSync(path.join(publicDir, 'i18n', file), 'utf8'));
const has = (object, key) => Object.prototype.hasOwnProperty.call(object, key);

const customer = entity.normalizeEntity({
  table: 'shop.customer',
  label: { en: 'Customer', de: 'Kunde' },
  orderBy: ['name', 'id desc'],
  columns: [
    { name: 'id', type: 'integer', readOnly: true },
    { name: 'name', label: { en: 'Name', de: 'Name' }, required: true },
    { name: 'group', options: ['A', 'B'] },
    { name: 'limit', label: 'Limit', type: 'number', min: 0 },
    { name: 'odd"name', type: 'integer' },
  ],
});

test('router: parameters, first match, other method, no match', () => {
  const router = core.createRouter();
  router.get('/api/thing/:id', () => 'read');
  router.put('/api/thing/:id', () => 'write');
  router.get('/api/thing/:id/part/:part', () => 'part');
  const m = router.match('GET', '/api/thing/a%20b');
  assert.equal(m.handler(), 'read');
  assert.deepEqual(m.params, { id: 'a b' });
  assert.deepEqual(router.match('GET', '/api/thing/7/part/x').params, { id: '7', part: 'x' });
  assert.equal(router.match('PUT', '/api/thing/7').handler(), 'write');
  assert.deepEqual(router.match('DELETE', '/api/thing/7'), { methodNotAllowed: true });
  assert.equal(router.match('GET', '/api/thing'), null);
  assert.equal(router.match('GET', '/api/thing/'), null);
  assert.equal(router.match('GET', '/api/thing/%E0%A4%A'), null);
  assert.throws(() => router.get('no-slash', () => 1));
});

test('static files: paths stay inside the public folder', () => {
  const base = path.resolve(publicDir);
  assert.equal(core.resolveStaticPath(publicDir, '/'), path.join(base, 'index.html'));
  assert.equal(core.resolveStaticPath(publicDir, '/style.css'), path.join(base, 'style.css'));
  assert.equal(core.resolveStaticPath(publicDir, '/i18n/en.json'), path.join(base, 'i18n', 'en.json'));
  const bad = ['/../server.js', '/..%2fserver.js', '/%2e%2e/server.js', '/i18n/../../server.js', '/..\\server.js', '/%5c..%5cserver.js',
    '/.hidden', '/i18n/.hidden', '/c:/Windows/win.ini', '/a%00.css', '/%E0%A4%A', 'style.css', '//..//server.js'];
  for (const p of bad) assert.equal(core.resolveStaticPath(publicDir, p), null, p);
});

test('static files: MIME types', () => {
  assert.equal(core.mimeType('a.html'), 'text/html; charset=utf-8');
  assert.equal(core.mimeType('a.CSS'), 'text/css; charset=utf-8');
  assert.equal(core.mimeType('a.js'), 'text/javascript; charset=utf-8');
  assert.equal(core.mimeType('a.json'), 'application/json; charset=utf-8');
  assert.equal(core.mimeType('a.svg'), 'image/svg+xml');
  assert.equal(core.mimeType('a.unknown'), 'application/octet-stream');
});

test('entity: identifiers are quoted', () => {
  assert.equal(entity.quoteIdent('name'), '"name"');
  assert.equal(entity.quoteIdent('a"b'), '"a""b"');
  assert.equal(entity.quoteTable('shop.customer'), '"shop"."customer"');
  assert.throws(() => entity.quoteIdent(''));
  assert.throws(() => entity.quoteIdent('a\0b'));
});

test('entity: description is checked and completed', () => {
  assert.equal(customer.name, 'customer');
  assert.equal(customer.key, 'id');
  assert.deepEqual(customer.actions, ['create', 'update', 'delete']);
  const implicit = entity.normalizeEntity({ table: 't', columns: [{ name: 'a' }] });
  assert.equal(implicit.columns[0].name, 'id');
  assert.equal(implicit.columns[0].readOnly, true);
  assert.throws(() => entity.normalizeEntity({ table: 't', columns: [] }));
  assert.throws(() => entity.normalizeEntity({ table: 't', columns: [{ name: 'a', type: 'money' }] }));
  assert.throws(() => entity.normalizeEntity({ table: 't', columns: [{ name: 'a' }], orderBy: 'b' }));
  assert.throws(() => entity.normalizeEntity({ table: 't', columns: [{ name: 'a' }, { name: 'a' }] }));
  assert.equal(entity.describeEntity(customer).table, undefined);
});

test('entity: SQL of list, read, create, update, delete', () => {
  const cols = '"id", "name", "group", "limit", "odd""name"';
  assert.deepEqual(entity.buildListSql(customer), { text: 'SELECT ' + cols + ' FROM "shop"."customer" ORDER BY "name", "id" DESC', values: [] });
  assert.deepEqual(entity.buildReadSql(customer, 7), { text: 'SELECT ' + cols + ' FROM "shop"."customer" WHERE "id" = $1', values: ['7'] });
  assert.deepEqual(entity.buildInsertSql(customer, { name: ' Ann ', limit: '12.50', 'odd"name': 3 }), {
    text: 'INSERT INTO "shop"."customer" ("name", "limit", "odd""name") VALUES ($1, $2, $3) RETURNING ' + cols,
    values: ['Ann', '12.50', '3'],
  });
  assert.deepEqual(entity.buildUpdateSql(customer, '7', { group: 'B', name: "x'); DROP TABLE customer; --" }), {
    text: 'UPDATE "shop"."customer" SET "name" = $1, "group" = $2 WHERE "id" = $3 RETURNING ' + cols,
    values: ["x'); DROP TABLE customer; --", 'B', '7'],
  });
  assert.deepEqual(entity.buildDeleteSql(customer, 7), { text: 'DELETE FROM "shop"."customer" WHERE "id" = $1 RETURNING "id"', values: ['7'] });
});

test('entity: unknown columns and wrong values are refused', () => {
  const code = (fn) => { try { fn(); } catch (e) { return e.status + ' ' + e.code; } return 'no error'; };
  assert.equal(code(() => entity.buildInsertSql(customer, { name: 'a', nope: 1 })), '400 validation.unknownColumn');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { 'name" = 1; --': 'x' })), '400 validation.unknownColumn');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { id: 5 })), '400 validation.readOnly');
  assert.equal(code(() => entity.buildInsertSql(customer, { limit: '1' })), '400 validation.required');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { name: '  ' })), '400 validation.required');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { limit: '1,5' })), '400 validation.number');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { limit: '-1' })), '400 validation.min');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { 'odd"name': '1.5' })), '400 validation.integer');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, { group: 'C' })), '400 validation.option');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, {})), '400 validation.noValues');
  assert.equal(code(() => entity.buildUpdateSql(customer, 1, [])), '400 error.badJson');
  const typed = entity.normalizeEntity({ table: 't', columns: [{ name: 'd', type: 'date' }, { name: 'b', type: 'boolean' }] });
  assert.deepEqual(entity.buildUpdateSql(typed, 1, { d: '2026-02-28', b: true }).values, ['2026-02-28', 'true', '1']);
  assert.equal(code(() => entity.buildUpdateSql(typed, 1, { d: '2026-02-30' })), '400 validation.date');
  assert.equal(code(() => entity.buildUpdateSql(typed, 1, { b: 'maybe' })), '400 validation.boolean');
  assert.deepEqual(entity.buildUpdateSql(typed, 1, { d: '' }).values, [null, '1']);
});

test('messages: labels and placeholders', () => {
  const messages = { 'validation.required': '{label} is required', 'x.name': 'Name' };
  assert.equal(core.formatMessage(messages, 'validation.required', { label: 'x.name' }), 'Name is required');
  assert.equal(core.formatMessage(messages, 'validation.required', { label: { en: 'City', de: 'Ort' } }), 'City is required');
  assert.equal(core.formatMessage(messages, 'unknown.key', {}), 'unknown.key');
  assert.equal(core.resolveLabel({ en: 'City', de: 'Ort' }, messages, 'de'), 'Ort');
});

function request(port, method, url, body) {
  return new Promise((resolve, reject) => {
    const data = body === undefined ? null : Buffer.from(typeof body === 'string' ? body : JSON.stringify(body));
    const headers = data ? { 'Content-Type': 'application/json', 'Content-Length': data.length } : {};
    const req = http.request({ host: '127.0.0.1', port, method, path: url, headers }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let json = null;
        try { json = JSON.parse(text); } catch (e) { json = null; }
        resolve({ status: res.statusCode, headers: res.headers, text, json });
      });
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

test('server: API and files through HTTP with a stand-in pool', async () => {
  const calls = [];
  const pool = {
    async query(text, values) {
      calls.push({ text, values });
      if (text.startsWith('SELECT') && values.length === 1 && values[0] === '404') return { rows: [] };
      return { rows: [{ id: 1, title: 'first' }] };
    },
  };
  const config = { appName: 'Test App', applicationName: 'test', connection: '', port: 0 };
  const app = require('../server').build({ pool, config });
  await new Promise((resolve) => app.server.listen(0, '127.0.0.1', resolve));
  const port = app.server.address().port;
  try {
    let r = await request(port, 'GET', '/api/meta');
    assert.equal(r.status, 200);
    assert.equal(r.json.appName, 'Test App');
    assert.equal(r.json.entities[0].name, 'item');
    assert.equal(r.headers['content-type'], 'application/json; charset=utf-8');

    r = await request(port, 'GET', '/api/entity/item');
    assert.deepEqual(r.json, [{ id: 1, title: 'first' }]);
    assert.match(calls[calls.length - 1].text, /^SELECT "id", "title", .* FROM "app_item" ORDER BY "id"$/);

    r = await request(port, 'POST', '/api/entity/item', { title: 'new', quantity: 2 });
    assert.equal(r.status, 201);
    assert.deepEqual(calls[calls.length - 1].values, ['new', '2']);

    r = await request(port, 'PUT', '/api/entity/item/1', { quantity: 'many' });
    assert.equal(r.status, 400);
    assert.deepEqual([r.json.code, r.json.error], ['validation.integer', 'Quantity must be a whole number']);

    r = await request(port, 'PUT', '/api/entity/item/1', { secret: 1 });
    assert.deepEqual([r.status, r.json.code], [400, 'validation.unknownColumn']);
    r = await request(port, 'POST', '/api/entity/item', '{broken');
    assert.deepEqual([r.status, r.json.code], [400, 'error.badJson']);
    r = await request(port, 'GET', '/api/entity/item/404');
    assert.deepEqual([r.status, r.json.code], [404, 'error.notFound']);
    r = await request(port, 'GET', '/api/entity/item/abc');
    assert.equal(r.status, 404);
    r = await request(port, 'GET', '/api/entity/nothing');
    assert.deepEqual([r.status, r.json.code], [404, 'error.unknownEntity']);
    r = await request(port, 'PATCH', '/api/entity/item/1');
    assert.equal(r.status, 405);
    r = await request(port, 'GET', '/api/about');
    assert.deepEqual(r.json, { appName: 'Test App', entities: 1 });

    r = await request(port, 'GET', '/');
    assert.equal(r.status, 200);
    assert.equal(r.headers['content-type'], 'text/html; charset=utf-8');
    assert.match(r.headers['content-security-policy'], /default-src 'self'/);
    r = await request(port, 'GET', '/style.css');
    assert.equal(r.headers['content-type'], 'text/css; charset=utf-8');
    r = await request(port, 'GET', '/i18n/de.json');
    assert.equal(r.json['action.save'], 'Speichern');
    r = await request(port, 'GET', '/..%2fserver.js');
    assert.equal(r.status, 404);
    r = await request(port, 'GET', '/%2e%2e/package.json');
    assert.equal(r.status, 404);
    r = await request(port, 'POST', '/style.css');
    assert.equal(r.status, 405);
  } finally {
    await new Promise((resolve) => app.server.close(resolve));
  }
});

test('transaction helper: commit, rollback, release', async () => {
  const log = [];
  const pool = { async connect() { return { async query(sql) { log.push(sql); }, release() { log.push('release'); } }; } };
  assert.equal(await core.withTransaction(pool, async (client) => { await client.query('work'); return 5; }), 5);
  await assert.rejects(core.withTransaction(pool, async () => { throw new Error('stop'); }), /stop/);
  assert.deepEqual(log, ['BEGIN', 'work', 'COMMIT', 'release', 'BEGIN', 'ROLLBACK', 'release']);
});

test('languages: English and German resources have the same keys', () => {
  const holes = (text) => (String(text).match(/\{\w+\}/g) || []).sort();
  for (const pair of [['en.json', 'de.json'], ['app-en.json', 'app-de.json']]) {
    const en = readJson(pair[0]);
    const de = readJson(pair[1]);
    assert.deepEqual(Object.keys(de).sort(), Object.keys(en).sort(), pair.join(' / '));
    for (const key of Object.keys(en)) {
      assert.deepEqual(holes(de[key]), holes(en[key]), 'placeholders of ' + key);
      assert.notEqual(String(de[key]).trim(), '', key);
    }
  }
});

test('languages: every key used in the pages and scripts exists in the English resources', () => {
  const template = readJson('en.json');
  const all = Object.assign({}, template, readJson('app-en.json'));
  const used = (text, pattern) => Array.from(text.matchAll(pattern), (m) => m[1]);
  const read = (file) => fs.readFileSync(path.join(root, file), 'utf8');

  const htmlKeys = [];
  for (const file of fs.readdirSync(publicDir).filter((f) => f.endsWith('.html'))) {
    htmlKeys.push(...used(read('public/' + file), /data-i18n(?:-[a-z-]+)?="([^"]+)"/g));
  }
  assert.ok(htmlKeys.length > 0);
  for (const key of htmlKeys) assert.ok(has(template, key), 'en.json lacks ' + key + ' (html)');

  const appKeys = used(read('public/app.js'), /\bt\(\s*'([^']+)'/g);
  assert.ok(appKeys.length > 10);
  for (const key of appKeys) assert.ok(has(template, key), 'en.json lacks ' + key + ' (app.js)');
  // keys that app.js passes on as fallback, and the message keys of the server
  for (const file of ['public/app.js', 'lib/core.js', 'lib/entity.js']) {
    for (const key of used(read(file), /'((?:action|entity|list|value|status|error|validation)\.[A-Za-z]+)'/g)) {
      assert.ok(has(template, key), 'en.json lacks ' + key + ' (' + file + ')');
    }
  }
  for (const key of used(read('public/pages.js'), /\bt\(\s*'([^']+)'/g)) assert.ok(has(all, key), 'resources lack ' + key + ' (pages.js)');
  // labels of the entity descriptions that are keys
  for (const e of require('../app/entities').map(entity.normalizeEntity)) {
    for (const spec of [e.label, e.labelPlural, ...e.columns.map((c) => c.label)]) {
      if (typeof spec === 'string' && /^[a-z]+\.[A-Za-z.]+$/.test(spec)) assert.ok(has(all, spec), 'resources lack ' + spec);
    }
  }
});
