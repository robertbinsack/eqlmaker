// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Entities: from a plain description
//   { name, table, key, label, labelPlural, columns: [{ name, label, type, required, readOnly }], orderBy }
// this module serves list / read / create / update / delete as a JSON API. All values travel as
// parameters ($1, $2, ...), all identifiers are quoted, columns that are not described are refused.
// The front end renders the list and the edit dialog from the same description.
'use strict';

const { HttpError } = require('./core');

const TYPES = ['text', 'integer', 'number', 'date', 'boolean'];
const ACTIONS = ['create', 'update', 'delete'];

/** "name" with doubled quotes; an identifier can never end the quoting. */
function quoteIdent(name) {
  const text = String(name);
  if (text === '' || text.includes('\0')) throw new Error('invalid identifier');
  return '"' + text.replace(/"/g, '""') + '"';
}

/** Table name, optionally with a schema: public.customer becomes "public"."customer". */
function quoteTable(table) {
  return String(table).split('.').map(quoteIdent).join('.');
}

/** Checks a description and fills in the defaults. Throws when it is not usable. */
function normalizeEntity(description) {
  const d = description || {};
  if (!d.table || typeof d.table !== 'string') throw new Error('entity: table is missing');
  const name = d.name || d.table.split('.').pop();
  if (!/^[a-z][a-z0-9_]*$/.test(name)) throw new Error('entity ' + name + ': name must be lower case letters, digits, _');
  if (!Array.isArray(d.columns) || d.columns.length === 0) throw new Error('entity ' + name + ': columns are missing');
  const key = d.key || 'id';
  const seen = new Set();
  const columns = d.columns.map((c) => {
    if (!c || !c.name || typeof c.name !== 'string') throw new Error('entity ' + name + ': column without name');
    if (seen.has(c.name)) throw new Error('entity ' + name + ': column ' + c.name + ' twice');
    seen.add(c.name);
    const type = c.type || 'text';
    if (!TYPES.includes(type)) throw new Error('entity ' + name + ': column ' + c.name + ' has unknown type ' + type);
    const column = {
      name: c.name,
      label: c.label === undefined ? c.name : c.label,
      type,
      required: Boolean(c.required),
      readOnly: Boolean(c.readOnly) || c.name === key && d.keyGenerated !== false,
      inList: c.inList !== false,
      inForm: c.inForm !== false,
    };
    if (Array.isArray(c.options)) column.options = c.options.map(String);
    if (c.input) column.input = String(c.input);
    if (c.min !== undefined) column.min = Number(c.min);
    if (c.decimals !== undefined) column.decimals = Number(c.decimals);
    return column;
  });
  if (!seen.has(key)) {
    columns.unshift({ name: key, label: 'list.id', type: 'integer', required: false, readOnly: d.keyGenerated !== false, inList: true, inForm: false });
  }
  const orderBy = (Array.isArray(d.orderBy) ? d.orderBy : [d.orderBy || key]).map((entry) => {
    const m = /^(.+?)(?:\s+(asc|desc))?$/i.exec(String(entry).trim());
    if (!columns.some((c) => c.name === m[1])) throw new Error('entity ' + name + ': orderBy names unknown column ' + m[1]);
    return { column: m[1], descending: Boolean(m[2]) && m[2].toLowerCase() === 'desc' };
  });
  const actions = Array.isArray(d.actions) ? d.actions.filter((a) => ACTIONS.includes(a)) : ACTIONS.slice();
  return {
    name, table: d.table, key, columns, orderBy, actions,
    label: d.label === undefined ? name : d.label,
    labelPlural: d.labelPlural === undefined ? (d.label === undefined ? name : d.label) : d.labelPlural,
    texts: d.texts || {},
    icon: d.icon || 'table',
  };
}

/** What the front end gets: everything except the table name. */
function describeEntity(entity) {
  return {
    name: entity.name, key: entity.key, label: entity.label, labelPlural: entity.labelPlural,
    columns: entity.columns, actions: entity.actions, texts: entity.texts, icon: entity.icon,
  };
}

function columnOf(entity, name) {
  return entity.columns.find((c) => c.name === name) || null;
}

/** One value as text parameter (or null); throws HttpError 400 with a message key. */
function checkValue(column, raw) {
  const args = { label: column.label, column: column.name };
  let value = raw;
  if (typeof value === 'string') value = value.trim();
  if (value === undefined || value === null || value === '') {
    if (column.required) throw new HttpError(400, 'validation.required', args);
    return null;
  }
  if (typeof value === 'object') throw new HttpError(400, 'validation.text', args);
  let text = String(value);
  switch (column.type) {
    case 'integer':
      if (!/^-?\d{1,18}$/.test(text)) throw new HttpError(400, 'validation.integer', args);
      break;
    case 'number':
      if (!/^-?\d{1,18}(\.\d{1,10})?$/.test(text)) throw new HttpError(400, 'validation.number', args);
      break;
    case 'date': {
      const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
      const date = m ? new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]))) : null;
      if (!date || date.getUTCFullYear() !== Number(m[1]) || date.getUTCMonth() !== Number(m[2]) - 1 || date.getUTCDate() !== Number(m[3])) {
        throw new HttpError(400, 'validation.date', args);
      }
      break;
    }
    case 'boolean':
      if (value === true || text === 'true' || text === '1') text = 'true';
      else if (value === false || text === 'false' || text === '0') text = 'false';
      else throw new HttpError(400, 'validation.boolean', args);
      break;
    default:
      break;
  }
  if (column.options && !column.options.includes(text)) {
    throw new HttpError(400, 'validation.option', Object.assign({ options: column.options }, args));
  }
  if (column.min !== undefined && (column.type === 'integer' || column.type === 'number') && Number(text) < column.min) {
    throw new HttpError(400, 'validation.min', Object.assign({ min: column.min }, args));
  }
  return text;
}

/**
 * Checks the values of a create or update. Unknown and read-only columns are refused.
 * Returns the columns to write, in the order of the description: [{ column, value }].
 */
function checkValues(entity, data, mode) {
  if (data === null || typeof data !== 'object' || Array.isArray(data)) throw new HttpError(400, 'error.badJson');
  for (const name of Object.keys(data)) {
    const column = columnOf(entity, name);
    if (!column) throw new HttpError(400, 'validation.unknownColumn', { column: name });
    if (column.readOnly) throw new HttpError(400, 'validation.readOnly', { column: name });
  }
  const out = [];
  for (const column of entity.columns) {
    if (column.readOnly) continue;
    const given = Object.prototype.hasOwnProperty.call(data, column.name);
    if (!given && mode === 'update') continue;
    if (!given && !column.required) continue;
    out.push({ column: column.name, value: checkValue(column, data[column.name]) });
  }
  if (out.length === 0) throw new HttpError(400, 'validation.noValues');
  return out;
}

function selectList(entity) {
  return entity.columns.map((c) => quoteIdent(c.name)).join(', ');
}

function buildListSql(entity) {
  const order = entity.orderBy.map((o) => quoteIdent(o.column) + (o.descending ? ' DESC' : '')).join(', ');
  return { text: 'SELECT ' + selectList(entity) + ' FROM ' + quoteTable(entity.table) + ' ORDER BY ' + order, values: [] };
}

function buildReadSql(entity, id) {
  return {
    text: 'SELECT ' + selectList(entity) + ' FROM ' + quoteTable(entity.table) + ' WHERE ' + quoteIdent(entity.key) + ' = $1',
    values: [String(id)],
  };
}

function buildInsertSql(entity, data) {
  const values = checkValues(entity, data, 'create');
  return {
    text: 'INSERT INTO ' + quoteTable(entity.table) + ' (' + values.map((v) => quoteIdent(v.column)).join(', ') + ') VALUES (' +
      values.map((v, i) => '$' + (i + 1)).join(', ') + ') RETURNING ' + selectList(entity),
    values: values.map((v) => v.value),
  };
}

function buildUpdateSql(entity, id, data) {
  const values = checkValues(entity, data, 'update');
  return {
    text: 'UPDATE ' + quoteTable(entity.table) + ' SET ' + values.map((v, i) => quoteIdent(v.column) + ' = $' + (i + 1)).join(', ') +
      ' WHERE ' + quoteIdent(entity.key) + ' = $' + (values.length + 1) + ' RETURNING ' + selectList(entity),
    values: values.map((v) => v.value).concat([String(id)]),
  };
}

function buildDeleteSql(entity, id) {
  return {
    text: 'DELETE FROM ' + quoteTable(entity.table) + ' WHERE ' + quoteIdent(entity.key) + ' = $1 RETURNING ' + quoteIdent(entity.key),
    values: [String(id)],
  };
}

/** The key travels in the URL; it must look like the key column says. */
function checkKey(entity, id) {
  const column = columnOf(entity, entity.key);
  if (column.type === 'integer' && !/^-?\d{1,18}$/.test(String(id))) throw new HttpError(404, 'error.notFound');
  return String(id);
}

/** Adds the JSON API of the entities to a router: /api/entity/:name and /api/entity/:name/:id. */
function registerEntities(router, pool, entities) {
  const byName = new Map(entities.map((e) => [e.name, e]));
  function entityOf(ctx, action) {
    const entity = byName.get(ctx.params.name);
    if (!entity) throw new HttpError(404, 'error.unknownEntity', { name: ctx.params.name });
    if (action && !entity.actions.includes(action)) throw new HttpError(405, 'error.notAllowed');
    return entity;
  }
  async function one(sql) {
    const result = await pool.query(sql.text, sql.values);
    if (result.rows.length === 0) throw new HttpError(404, 'error.notFound');
    return result.rows[0];
  }
  router.get('/api/entity/:name', async (ctx) => {
    const sql = buildListSql(entityOf(ctx));
    return (await pool.query(sql.text, sql.values)).rows;
  });
  router.get('/api/entity/:name/:id', async (ctx) => {
    const entity = entityOf(ctx);
    return one(buildReadSql(entity, checkKey(entity, ctx.params.id)));
  });
  router.post('/api/entity/:name', async (ctx) => {
    const entity = entityOf(ctx, 'create');
    const row = await one(buildInsertSql(entity, await ctx.body()));
    ctx.status = 201;
    return row;
  });
  router.put('/api/entity/:name/:id', async (ctx) => {
    const entity = entityOf(ctx, 'update');
    return one(buildUpdateSql(entity, checkKey(entity, ctx.params.id), await ctx.body()));
  });
  router.delete('/api/entity/:name/:id', async (ctx) => {
    const entity = entityOf(ctx, 'delete');
    return one(buildDeleteSql(entity, checkKey(entity, ctx.params.id)));
  });
}

module.exports = {
  TYPES, ACTIONS, quoteIdent, quoteTable, normalizeEntity, describeEntity,
  checkValue, checkValues, buildListSql, buildReadSql, buildInsertSql, buildUpdateSql, buildDeleteSql,
  registerEntities,
};
