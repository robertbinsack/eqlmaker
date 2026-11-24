// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The calculation of an invoice: line amounts, discount by customer group, tax by tax class.
// Pure function on whole cents; the desktop variant has the same function in C# and both are
// tested against ../../test-vectors.json.
'use strict';

const TAX_RATE = { S: 19, R: 7 };

/** '12.5' -> 1250 */
function parseCents(text) {
  const m = /^(\d{1,12})(?:\.(\d{1,2}))?$/.exec(String(text).trim());
  if (!m) throw new Error('not an amount: ' + text);
  return Number(m[1]) * 100 + Number(((m[2] || '') + '00').slice(0, 2));
}

/** 1250 -> '12.50' */
function formatCents(cents) {
  const digits = String(cents).padStart(3, '0');
  return digits.slice(0, -2) + '.' + digits.slice(-2);
}

/** cents * percent / 100, half a cent rounds up */
function percentOf(cents, percent) {
  const scaled = cents * percent + 50;
  return (scaled - (scaled % 100)) / 100;
}

function discountPercent(group, net) {
  if (group === 'A' && net >= 50000) return 5;
  if (group === 'B' && net >= 100000) return 3;
  return 0;
}

/**
 * group: 'A' | 'B' | 'C'; lines: [{ quantity, unit_price, tax_class }] with unit_price as text.
 * Returns texts: { line_net: [], net, discount_percent, discount, tax, gross }.
 * options.flaw switches the deliberate defect on: tax is then rounded per line.
 */
function calculateInvoice(group, lines, options) {
  const flaw = Boolean(options && options.flaw);
  const lineNet = [];
  const classNet = { S: 0, R: 0 };
  let net = 0;
  for (const line of lines) {
    if (!Object.prototype.hasOwnProperty.call(TAX_RATE, line.tax_class)) throw new Error('unknown tax class: ' + line.tax_class);
    const amount = Number(line.quantity) * parseCents(line.unit_price);
    lineNet.push(amount);
    classNet[line.tax_class] += amount;
    net += amount;
  }
  const percent = discountPercent(group, net);
  const discount = percentOf(net, percent);
  let tax = 0;
  if (flaw) {
    lines.forEach((line, i) => { tax += percentOf(lineNet[i] - percentOf(lineNet[i], percent), TAX_RATE[line.tax_class]); });
  } else {
    for (const taxClass of Object.keys(TAX_RATE)) {
      const share = classNet[taxClass] - percentOf(classNet[taxClass], percent);
      tax += percentOf(share, TAX_RATE[taxClass]);
    }
  }
  return {
    line_net: lineNet.map(formatCents),
    net: formatCents(net),
    discount_percent: formatCents(percent * 100),
    discount: formatCents(discount),
    tax: formatCents(tax),
    gross: formatCents(net - discount + tax),
  };
}

module.exports = { TAX_RATE, parseCents, formatCents, calculateInvoice };
