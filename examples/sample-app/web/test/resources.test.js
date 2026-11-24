// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Language resources of the sample, and the files that are copies of the web template.
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
const read = (file) => fs.readFileSync(path.join(root, file), 'utf8');
const json = (file) => JSON.parse(read('public/i18n/' + file));
const has = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const used = (text, pattern) => Array.from(text.matchAll(pattern), (m) => m[1]);

test('languages: English and German resources have the same keys', () => {
  const holes = (text) => (String(text).match(/\{\w+\}/g) || []).sort();
  for (const pair of [['en.json', 'de.json'], ['app-en.json', 'app-de.json']]) {
    const en = json(pair[0]);
    const de = json(pair[1]);
    assert.deepEqual(Object.keys(de).sort(), Object.keys(en).sort(), pair.join(' / '));
    for (const key of Object.keys(en)) assert.deepEqual(holes(de[key]), holes(en[key]), 'placeholders of ' + key);
  }
});

test('languages: every key the sample uses exists in the English resources', () => {
  const all = Object.assign({}, json('en.json'), json('app-en.json'));
  const keys = []
    .concat(used(read('public/index.html'), /data-i18n(?:-[a-z-]+)?="([^"]+)"/g))
    .concat(used(read('public/app.js'), /\bt\(\s*'([^']+)'/g))
    .concat(used(read('public/pages.js'), /\bt\(\s*'([^']+)'/g))
    .concat(used(read('public/pages.js'), /'((?:invoice|list)\.[A-Za-z.]+)'/g))
    .concat(used(read('app/routes.js'), /'((?:invoice|error|validation)\.[A-Za-z.]+)'/g))
    .concat(used(read('app/entities.js'), /'((?:customer|article|list)\.[A-Za-z.]+)'/g));
  assert.ok(keys.length > 60);
  for (const key of keys) assert.ok(has(all, key), 'resources lack ' + key);
});

test('English texts of the sample match the desktop variant', () => {
  const en = json('app-en.json');
  assert.equal(en['invoice.creditLimit'], 'Credit limit exceeded');
  assert.equal(en['invoice.saved'], 'Invoice {number} saved');
  assert.equal(en['customer.new'], 'New customer');
});

test('files taken from the web template are unchanged', (t) => {
  const template = path.join(root, '..', '..', '..', 'src', 'templates', 'web');
  if (!fs.existsSync(template)) { t.skip('template folder not present'); return; }
  const same = ['server.js', 'lib/core.js', 'lib/entity.js', 'public/app.js', 'public/style.css', 'public/favicon.svg', 'public/i18n/en.json', 'public/i18n/de.json'];
  for (const file of same) assert.equal(read(file), fs.readFileSync(path.join(template, file), 'utf8'), file);
  const page = fs.readFileSync(path.join(template, 'public/index.html'), 'utf8').split('{{APP_NAME}}').join('Sample Shop');
  assert.equal(read('public/index.html'), page, 'public/index.html');
});
