// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The entities of this application. Each description gives a list page with an edit dialog and
// the JSON API /api/entity/<name>. Labels are keys of public/i18n/app-en.json and app-de.json
// (or objects like { en: 'Item', de: 'Posten' }). The table of the example is in app/schema.sql.
'use strict';

module.exports = [
  {
    name: 'item',
    table: 'app_item',
    key: 'id',
    label: 'entity.item.label',
    labelPlural: 'entity.item.labelPlural',
    icon: 'table',
    orderBy: 'id',
    columns: [
      { name: 'id', label: 'list.id', type: 'integer', readOnly: true },
      { name: 'title', label: 'entity.item.title', type: 'text', required: true },
      { name: 'quantity', label: 'entity.item.quantity', type: 'integer', required: true, min: 0 },
      { name: 'price', label: 'entity.item.price', type: 'number', decimals: 2, min: 0 },
      { name: 'due', label: 'entity.item.due', type: 'date' },
      { name: 'done', label: 'entity.item.done', type: 'boolean' },
    ],
  },
];
