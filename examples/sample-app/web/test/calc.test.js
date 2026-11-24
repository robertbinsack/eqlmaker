// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The invoice calculation against the shared test vectors.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { calculateInvoice, parseCents, formatCents } = require('../app/invoice-calc');
const vectors = require('../../test-vectors.json').vectors;

test('there are at least 8 test vectors', () => {
  assert.ok(vectors.length >= 8);
});

for (const vector of vectors) {
  test('calculation: ' + vector.name, () => {
    assert.deepEqual(calculateInvoice(vector.group, vector.lines), vector.expected);
  });
}

test('the deliberate defect changes at least one rounding vector, and only the tax', () => {
  let different = 0;
  for (const vector of vectors) {
    const flawed = calculateInvoice(vector.group, vector.lines, { flaw: true });
    assert.deepEqual(flawed.line_net, vector.expected.line_net);
    assert.equal(flawed.net, vector.expected.net);
    assert.equal(flawed.discount, vector.expected.discount);
    if (flawed.tax !== vector.expected.tax) different++;
  }
  assert.ok(different >= 1, 'no vector shows the defect');
});

test('amounts as texts', () => {
  assert.equal(parseCents('12.5'), 1250);
  assert.equal(parseCents('0.05'), 5);
  assert.equal(parseCents('1000'), 100000);
  assert.equal(formatCents(5), '0.05');
  assert.equal(formatCents(123456), '1234.56');
  assert.throws(() => parseCents('1,5'));
  assert.throws(() => calculateInvoice('A', [{ quantity: 1, unit_price: '1.00', tax_class: 'X' }]));
});
