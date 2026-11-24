// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Customers and articles of the sample application as entity descriptions. The texts are keys
// of public/i18n/app-en.json and app-de.json.
'use strict';

module.exports = [
  {
    name: 'customer',
    table: 'customer',
    key: 'id',
    label: 'customer.label',
    labelPlural: 'customer.labelPlural',
    icon: 'users',
    orderBy: 'id',
    actions: ['create', 'update'],
    texts: {
      new: 'customer.new', edit: 'customer.edit', refresh: 'customer.refresh', save: 'customer.save',
      noneSelected: 'customer.noneSelected',
    },
    columns: [
      { name: 'id', label: 'list.id', type: 'integer', readOnly: true, inForm: false },
      { name: 'name', label: 'customer.name', type: 'text', required: true },
      { name: 'city', label: 'customer.city', type: 'text', required: true },
      { name: 'customer_group', label: 'customer.group', type: 'text', required: true, options: ['A', 'B', 'C'], input: 'text' },
      { name: 'credit_limit', label: 'customer.creditLimit', type: 'number', required: true, min: 0, decimals: 2 },
    ],
  },
  {
    name: 'article',
    table: 'article',
    key: 'id',
    label: 'article.label',
    labelPlural: 'article.labelPlural',
    icon: 'package',
    orderBy: 'id',
    actions: ['update'],
    texts: { edit: 'article.edit', refresh: 'article.refresh', save: 'article.save', noneSelected: 'article.noneSelected' },
    columns: [
      { name: 'id', label: 'list.id', type: 'integer', readOnly: true, inForm: false },
      { name: 'sku', label: 'article.sku', type: 'text', readOnly: true, inForm: false },
      { name: 'name', label: 'article.name', type: 'text', required: true },
      { name: 'unit_price', label: 'article.price', type: 'number', required: true, min: 0, decimals: 2 },
      { name: 'stock', label: 'article.stock', type: 'integer', required: true },
      { name: 'tax_class', label: 'article.taxClass', type: 'text', readOnly: true, inForm: false },
    ],
  },
];
