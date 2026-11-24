// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Invoices of the sample application: list, preview of the totals, saving in one transaction,
// marking as paid. The statements and their order are the same as in the desktop variant.
'use strict';

const { calculateInvoice, parseCents } = require('./invoice-calc');

module.exports = function routes(router, app) {
  const { pool, core, config } = app;
  const HttpError = core.HttpError;
  const flaw = Boolean(config.flaw);

  /** [{ sku, quantity }] from the request body */
  function readLines(body) {
    const lines = Array.isArray(body.lines) ? body.lines : [];
    return lines.map((line) => {
      const sku = String(line && line.sku !== undefined && line.sku !== null ? line.sku : '').trim();
      const quantity = String(line && line.quantity !== undefined && line.quantity !== null ? line.quantity : '').trim();
      if (!/^[1-9]\d{0,4}$/.test(quantity)) throw new HttpError(400, 'invoice.badQuantity');
      if (sku === '') throw new HttpError(400, 'invoice.unknownSku');
      return { sku, quantity: Number(quantity) };
    });
  }

  function customerNo(body) {
    const text = String(body.customer_id === undefined || body.customer_id === null ? '' : body.customer_id).trim();
    return /^\d{1,9}$/.test(text) ? text : null;
  }

  router.get('/api/invoices', async () => {
    const result = await pool.query(
      "SELECT i.id, i.number, c.name AS customer, to_char(i.invoice_date, 'YYYY-MM-DD') AS invoice_date, " +
      'i.net, i.discount, i.tax, i.gross, i.status FROM invoice i JOIN customer c ON c.id = i.customer_id ORDER BY i.id');
    return result.rows;
  });

  // totals of the lines entered so far; an unknown customer counts as group C
  router.post('/api/invoices/preview', async (ctx) => {
    const body = await ctx.body();
    const lines = readLines(body);
    const customerId = customerNo(body);
    let group = 'C';
    if (customerId) {
      const customer = await pool.query('SELECT customer_group FROM customer WHERE id = $1', [customerId]);
      if (customer.rows.length > 0) group = customer.rows[0].customer_group;
    }
    const shown = [];
    for (const line of lines) {
      const article = await pool.query('SELECT id, sku, name, unit_price, tax_class FROM article WHERE sku = $1', [line.sku]);
      if (article.rows.length === 0) throw new HttpError(400, 'invoice.unknownSku');
      const a = article.rows[0];
      shown.push({ sku: a.sku, name: a.name, quantity: line.quantity, unit_price: a.unit_price, tax_class: a.tax_class });
    }
    const totals = calculateInvoice(group, shown, { flaw });
    return {
      lines: shown.map((l, i) => ({ sku: l.sku, name: l.name, quantity: l.quantity, unit_price: l.unit_price, line_net: totals.line_net[i] })),
      net: totals.net, discount_percent: totals.discount_percent, discount: totals.discount, tax: totals.tax, gross: totals.gross,
    };
  });

  router.post('/api/invoices', async (ctx) => {
    const body = await ctx.body();
    const lines = readLines(body);
    const customerId = customerNo(body);
    if (!customerId) throw new HttpError(400, 'invoice.unknownCustomer');
    if (lines.length === 0) throw new HttpError(400, 'invoice.noLines');

    const saved = await core.withTransaction(pool, async (db) => {
      const customer = await db.query('SELECT id, customer_group, credit_limit FROM customer WHERE id = $1 FOR UPDATE', [customerId]);
      if (customer.rows.length === 0) throw new HttpError(400, 'invoice.unknownCustomer');
      const articles = [];
      for (const line of lines) {
        const article = await db.query('SELECT id, unit_price, tax_class FROM article WHERE sku = $1 FOR UPDATE', [line.sku]);
        if (article.rows.length === 0) throw new HttpError(400, 'invoice.unknownSku');
        articles.push({ id: article.rows[0].id, quantity: line.quantity, unit_price: article.rows[0].unit_price, tax_class: article.rows[0].tax_class });
      }
      const totals = calculateInvoice(customer.rows[0].customer_group, articles, { flaw });

      const open = await db.query("SELECT coalesce(sum(gross), 0) AS total FROM invoice WHERE customer_id = $1 AND status = 'open'", [customerId]);
      if (parseCents(open.rows[0].total) + parseCents(totals.gross) > parseCents(customer.rows[0].credit_limit)) {
        throw new HttpError(409, 'invoice.creditLimit');
      }

      const counter = await db.query("UPDATE counter SET value = value + 1 WHERE name = 'invoice' RETURNING value, to_char(current_date, 'YYYY') AS year");
      const number = 'INV-' + counter.rows[0].year + '-' + String(counter.rows[0].value).padStart(5, '0');
      const invoice = await db.query(
        'INSERT INTO invoice (number, customer_id, invoice_date, net, discount_percent, discount, tax, gross, status) ' +
        "VALUES ($1, $2, current_date, $3, $4, $5, $6, $7, 'open') RETURNING id",
        [number, customerId, totals.net, totals.discount_percent, totals.discount, totals.tax, totals.gross]);
      const invoiceId = invoice.rows[0].id;
      for (let i = 0; i < articles.length; i++) {
        const a = articles[i];
        await db.query('INSERT INTO invoice_line (invoice_id, article_id, quantity, unit_price, line_net) VALUES ($1, $2, $3, $4, $5)',
          [invoiceId, a.id, a.quantity, a.unit_price, totals.line_net[i]]);
        await db.query('UPDATE article SET stock = stock - $1 WHERE id = $2', [a.quantity, a.id]);
        if (!flaw) {
          await db.query("INSERT INTO stock_movement (article_id, quantity, reason, invoice_id) VALUES ($1, $2, 'invoice', $3)",
            [a.id, -a.quantity, invoiceId]);
        }
      }
      return { id: invoiceId, number, net: totals.net, discount: totals.discount, tax: totals.tax, gross: totals.gross };
    });
    ctx.status = 201;
    return saved;
  });

  router.post('/api/invoices/:id/paid', async (ctx) => {
    if (!/^\d{1,9}$/.test(ctx.params.id)) throw new HttpError(404, 'invoice.unknownInvoice');
    const result = await pool.query("UPDATE invoice SET status = 'paid' WHERE id = $1 RETURNING id, number", [ctx.params.id]);
    if (result.rows.length === 0) throw new HttpError(404, 'invoice.unknownInvoice');
    return result.rows[0];
  });
};
