// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// HTTP-level test against a real database; runs only when SAMPLE_DB is set (password from
// PGPASSWORD). Saves invoices through the API and checks the rows that were written.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const { calculateInvoice } = require('../app/invoice-calc');

const enabled = Boolean(process.env.SAMPLE_DB);

function request(port, method, url, body) {
  return new Promise((resolve, reject) => {
    const data = body === undefined ? null : Buffer.from(JSON.stringify(body));
    const headers = data ? { 'Content-Type': 'application/json', 'Content-Length': data.length } : {};
    const req = http.request({ host: '127.0.0.1', port, method, path: url, headers }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        let json = null;
        try { json = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch (e) { json = null; }
        resolve({ status: res.statusCode, json });
      });
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

async function start(flaw) {
  const config = Object.assign({}, require('../app/config'), { flaw });
  const app = require('../server').build({ config });
  await new Promise((resolve) => app.server.listen(0, '127.0.0.1', resolve));
  return {
    port: app.server.address().port,
    pool: app.pool,
    async stop() { await new Promise((resolve) => app.server.close(resolve)); await app.pool.end(); },
  };
}

async function snapshot(pool) {
  const one = async (sql) => (await pool.query(sql)).rows[0];
  return {
    counter: (await one("SELECT value FROM counter WHERE name = 'invoice'")).value,
    invoices: Number((await one('SELECT count(*) AS n FROM invoice')).n),
    lines: Number((await one('SELECT count(*) AS n FROM invoice_line')).n),
    movements: Number((await one('SELECT count(*) AS n FROM stock_movement')).n),
    stock: Object.fromEntries((await pool.query('SELECT sku, stock FROM article')).rows.map((r) => [r.sku, r.stock])),
  };
}

const LINES = [{ sku: 'TL-110', quantity: 20 }, { sku: 'FD-300', quantity: 10 }];

async function expectedTotals(pool, group) {
  const lines = [];
  for (const line of LINES) {
    const a = (await pool.query('SELECT id, unit_price, tax_class FROM article WHERE sku = $1', [line.sku])).rows[0];
    lines.push({ id: a.id, quantity: line.quantity, unit_price: a.unit_price, tax_class: a.tax_class });
  }
  return { lines, totals: calculateInvoice(group, lines) };
}

test('saving an invoice writes invoice, lines, stock movements, stock and counter', { skip: !enabled && 'SAMPLE_DB is not set' }, async () => {
  const app = await start(false);
  try {
    // a customer of group A whose limit allows exactly one of the test invoices
    let r = await request(app.port, 'POST', '/api/entity/customer', { name: 'Test customer', city: 'Testville', customer_group: 'A', credit_limit: '1000.00' });
    assert.equal(r.status, 201);
    const customerId = r.json.id;
    const expected = await expectedTotals(app.pool, 'A');
    const before = await snapshot(app.pool);

    r = await request(app.port, 'POST', '/api/invoices/preview', { customer_id: customerId, lines: LINES });
    assert.equal(r.status, 200);
    assert.equal(r.json.gross, expected.totals.gross);
    assert.deepEqual(await snapshot(app.pool), before, 'a preview writes nothing');

    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: LINES });
    assert.equal(r.status, 201);
    const invoiceId = r.json.id;
    const after = await snapshot(app.pool);

    assert.equal(after.counter, before.counter + 1);
    const year = (await app.pool.query("SELECT to_char(current_date, 'YYYY') AS y")).rows[0].y;
    assert.equal(r.json.number, 'INV-' + year + '-' + String(after.counter).padStart(5, '0'));

    const invoice = (await app.pool.query('SELECT number, customer_id, invoice_date = current_date AS today, net, discount_percent, discount, tax, gross, status FROM invoice WHERE id = $1', [invoiceId])).rows[0];
    assert.deepEqual(invoice, {
      number: r.json.number, customer_id: customerId, today: true, net: expected.totals.net, discount_percent: expected.totals.discount_percent,
      discount: expected.totals.discount, tax: expected.totals.tax, gross: expected.totals.gross, status: 'open',
    });

    const lines = (await app.pool.query('SELECT article_id, quantity, unit_price, line_net FROM invoice_line WHERE invoice_id = $1 ORDER BY id', [invoiceId])).rows;
    assert.deepEqual(lines, expected.lines.map((l, i) => ({ article_id: l.id, quantity: l.quantity, unit_price: l.unit_price, line_net: expected.totals.line_net[i] })));

    const movements = (await app.pool.query('SELECT article_id, quantity, reason FROM stock_movement WHERE invoice_id = $1 ORDER BY id', [invoiceId])).rows;
    assert.deepEqual(movements, expected.lines.map((l) => ({ article_id: l.id, quantity: -l.quantity, reason: 'invoice' })));

    assert.equal(after.stock['TL-110'], before.stock['TL-110'] - 20);
    assert.equal(after.stock['FD-300'], before.stock['FD-300'] - 10);
    assert.equal(after.invoices, before.invoices + 1);
    assert.equal(after.lines, before.lines + 2);
    assert.equal(after.movements, before.movements + 2);

    // the second invoice would exceed the credit limit: nothing is written
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: LINES });
    assert.equal(r.status, 409);
    assert.equal(r.json.error, 'Credit limit exceeded');
    assert.deepEqual(await snapshot(app.pool), after);

    // input errors write nothing either
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: [{ sku: 'NO-SUCH', quantity: 1 }] });
    assert.deepEqual([r.status, r.json.error], [400, 'Unknown SKU']);
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: [{ sku: 'TL-110', quantity: 0 }] });
    assert.equal(r.status, 400);
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: 999999, lines: LINES });
    assert.deepEqual([r.status, r.json.error], [400, 'Unknown customer']);
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: [] });
    assert.deepEqual([r.status, r.json.error], [400, 'No lines']);
    assert.deepEqual(await snapshot(app.pool), after);

    // paid invoices do not count against the limit
    r = await request(app.port, 'POST', '/api/invoices/' + invoiceId + '/paid');
    assert.deepEqual([r.status, r.json.number], [200, invoice.number]);
    assert.equal((await app.pool.query('SELECT status FROM invoice WHERE id = $1', [invoiceId])).rows[0].status, 'paid');
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: LINES });
    assert.equal(r.status, 201);

    r = await request(app.port, 'GET', '/api/invoices');
    assert.ok(r.json.some((row) => row.id === invoiceId && row.customer === 'Test customer' && row.status === 'paid'));
    const session = await app.pool.query("SELECT current_setting('application_name') AS name");
    assert.equal(session.rows[0].name, 'sample-web');
  } finally {
    await app.stop();
  }
});

test('entity API of customers and articles', { skip: !enabled && 'SAMPLE_DB is not set' }, async () => {
  const app = await start(false);
  try {
    let r = await request(app.port, 'GET', '/api/entity/article');
    assert.ok(r.json.length >= 12);
    assert.equal(r.json[0].sku, 'TL-100');
    r = await request(app.port, 'POST', '/api/entity/article', { name: 'x' });
    assert.equal(r.status, 405);
    r = await request(app.port, 'PUT', '/api/entity/article/1', { sku: 'changed' });
    assert.deepEqual([r.status, r.json.code], [400, 'validation.readOnly']);
    r = await request(app.port, 'POST', '/api/entity/customer', { name: 'x', city: 'y', customer_group: 'D', credit_limit: '1' });
    assert.deepEqual([r.status, r.json.error], [400, 'Group must be one of A, B, C']);
    r = await request(app.port, 'POST', '/api/entity/customer', { name: '', city: 'y', customer_group: 'A', credit_limit: '1' });
    assert.deepEqual([r.status, r.json.error], [400, 'Name is required']);
    r = await request(app.port, 'POST', '/api/entity/customer', { name: 'x', city: 'y', customer_group: 'A', credit_limit: 'much' });
    assert.deepEqual([r.status, r.json.error], [400, 'Credit limit must be a number']);
    r = await request(app.port, 'DELETE', '/api/entity/customer/1');
    assert.equal(r.status, 405);
  } finally {
    await app.stop();
  }
});

test('with the deliberate defect no stock movement is written', { skip: !enabled && 'SAMPLE_DB is not set' }, async () => {
  const app = await start(true);
  try {
    let r = await request(app.port, 'POST', '/api/entity/customer', { name: 'Test customer', city: 'Testville', customer_group: 'C', credit_limit: '100.00' });
    const customerId = r.json.id;
    const before = await snapshot(app.pool);
    r = await request(app.port, 'POST', '/api/invoices', { customer_id: customerId, lines: [{ sku: 'OF-210', quantity: 1 }, { sku: 'OF-210', quantity: 1 }] });
    assert.equal(r.status, 201);
    const after = await snapshot(app.pool);
    assert.equal(after.movements, before.movements);
    assert.equal(after.lines, before.lines + 2);
    assert.equal(after.stock['OF-210'], before.stock['OF-210'] - 2);
  } finally {
    await app.stop();
  }
});
