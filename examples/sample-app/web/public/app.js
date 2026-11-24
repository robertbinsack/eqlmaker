// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Front end of the web application template, without build step: language resources, app shell
// (navigation, page head, status line), list pages and edit dialogs rendered from the entity
// descriptions of /api/meta, and the building blocks for custom pages (window.App).
(function () {
  'use strict';

  // Icons: inline SVG paths of the Lucide icon set (ISC licence), see ICONS.md.
  const ICONS = {
    'panel-left': '<rect width="18" height="18" x="3" y="3" rx="2"/><path d="M9 3v18"/>',
    'table': '<path d="M12 3v18"/><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 9h18"/><path d="M3 15h18"/>',
    'users': '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
    'package': '<path d="m7.5 4.27 9 5.15"/><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
    'file-text': '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/>',
    'info': '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
    'plus': '<path d="M5 12h14"/><path d="M12 5v14"/>',
    'pencil': '<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/>',
    'trash-2': '<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/><line x1="10" x2="10" y1="11" y2="17"/><line x1="14" x2="14" y1="11" y2="17"/>',
    'refresh-cw': '<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
    'check': '<path d="M20 6 9 17l-5-5"/>',
    'x': '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
  };
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const LANGUAGES = ['en', 'de'];

  let lang = 'en';
  const resources = { en: {}, de: {} };
  let meta = { appName: '', entities: [] };
  const pages = [];
  let currentPage = null;
  let cleanup = null;
  const openDialogs = [];

  // ---------------------------------------------------------------- small helpers

  function icon(name, size) {
    const svg = document.createElementNS(SVG_NS, 'svg');
    const px = String(size || 16);
    const attrs = {
      viewBox: '0 0 24 24', width: px, height: px, fill: 'none', stroke: 'currentColor', 'stroke-width': '2',
      'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true', focusable: 'false', class: 'icon',
    };
    for (const k of Object.keys(attrs)) svg.setAttribute(k, attrs[k]);
    svg.innerHTML = ICONS[name] || ICONS.table;
    return svg;
  }

  /** el('button', { class: 'btn', id: 'x', onclick: fn }, child, 'text', ...) */
  function el(tag, attrs) {
    const node = document.createElement(tag);
    const a = attrs || {};
    for (const key of Object.keys(a)) {
      const value = a[key];
      if (value === null || value === undefined || value === false) continue;
      if (key === 'text') node.textContent = String(value);
      else if (key.startsWith('on') && typeof value === 'function') node.addEventListener(key.slice(2), value);
      else node.setAttribute(key, value === true ? '' : String(value));
    }
    for (let i = 2; i < arguments.length; i++) {
      const child = arguments[i];
      if (child === null || child === undefined || child === false) continue;
      if (Array.isArray(child)) child.forEach((c) => { if (c) node.append(c); });
      else node.append(child);
    }
    return node;
  }

  function store(key, value) {
    try {
      if (value === undefined) return window.localStorage.getItem(key);
      window.localStorage.setItem(key, value);
    } catch (e) { /* storage may be switched off */ }
    return null;
  }

  // ---------------------------------------------------------------- language

  /** Order: ?lang= in the address, stored choice, language of the browser, English. */
  function chooseLanguage() {
    const fromQuery = (new URLSearchParams(window.location.search).get('lang') || '').toLowerCase();
    if (LANGUAGES.includes(fromQuery)) return fromQuery;
    const stored = store('lang');
    if (LANGUAGES.includes(stored)) return stored;
    const wanted = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || ''];
    for (const entry of wanted) {
      const short = String(entry).slice(0, 2).toLowerCase();
      if (LANGUAGES.includes(short)) return short;
    }
    return 'en';
  }

  function hasKey(key) {
    return Object.prototype.hasOwnProperty.call(resources[lang], key) || Object.prototype.hasOwnProperty.call(resources.en, key);
  }

  /** A label is a text, a key of the resources or an object with one text per language. */
  function label(spec) {
    if (spec === null || spec === undefined) return '';
    if (typeof spec === 'object') return String(spec[lang] || spec.en || '');
    const key = String(spec);
    if (Object.prototype.hasOwnProperty.call(resources[lang], key)) return resources[lang][key];
    if (Object.prototype.hasOwnProperty.call(resources.en, key)) return resources.en[key];
    return key;
  }

  /** Text of a resource key in the chosen language; {name} is filled from args. */
  function t(key, args) {
    return label(key).replace(/\{(\w+)\}/g, (all, name) => {
      if (!args || !Object.prototype.hasOwnProperty.call(args, name)) return all;
      const value = args[name];
      if (Array.isArray(value)) return value.join(', ');
      return name === 'label' || name === 'labelPlural' ? label(value) : String(value);
    });
  }

  function applyStaticTexts() {
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach((node) => { node.textContent = t(node.getAttribute('data-i18n')); });
    for (const attr of ['aria-label', 'title', 'placeholder']) {
      document.querySelectorAll('[data-i18n-' + attr + ']').forEach((node) => {
        node.setAttribute(attr, t(node.getAttribute('data-i18n-' + attr)));
      });
    }
    document.querySelectorAll('#lang-switch button').forEach((button) => {
      const on = button.getAttribute('data-lang') === lang;
      button.classList.toggle('on', on);
      button.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
  }

  function setLanguage(next) {
    if (!LANGUAGES.includes(next) || next === lang) return;
    lang = next;
    store('lang', lang);
    const url = new URL(window.location.href);
    if (url.searchParams.has('lang')) {
      url.searchParams.set('lang', lang);
      window.history.replaceState(null, '', url.toString());
    }
    openDialogs.slice().forEach((d) => d.close());
    applyStaticTexts();
    renderNav();
    showPage();
  }

  // ---------------------------------------------------------------- values by locale

  function formatNumber(value, decimals) {
    if (value === null || value === undefined || value === '') return '';
    const text = String(value);
    const digits = decimals === undefined || decimals === null ? (text.split('.')[1] || '').length : decimals;
    return new Intl.NumberFormat(lang, { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(Number(text));
  }

  function formatDate(value) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value || ''));
    if (!m) return value === null || value === undefined ? '' : String(value);
    const date = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])));
    return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeZone: 'UTC' }).format(date);
  }

  /** Text of a value in a list, by the type of its column. */
  function formatValue(column, value, isKey) {
    if (value === null || value === undefined) return '';
    switch (column.type) {
      case 'boolean': return t(value === true || value === 'true' ? 'value.yes' : 'value.no');
      case 'integer': return isKey ? String(value) : formatNumber(value, 0);
      case 'number': return formatNumber(value, column.decimals);
      case 'date': return formatDate(value);
      default: return String(value);
    }
  }

  /** Text of a value in an input field: neutral, only the decimal sign follows the language. */
  function toInput(column, value) {
    if (value === null || value === undefined) return '';
    const text = String(value);
    return column.type === 'number' && lang === 'de' ? text.replace('.', ',') : text;
  }

  function fromInput(column, text) {
    const value = String(text).trim();
    return column.type === 'number' && lang === 'de' && /^-?\d+,\d+$/.test(value) ? value.replace(',', '.') : value;
  }

  // ---------------------------------------------------------------- server

  function errorText(data) {
    if (data && data.code && hasKey(data.code)) return t(data.code, data.args);
    return data && data.error ? String(data.error) : t('error.request');
  }

  async function api(method, url, body) {
    const options = { method, headers: { Accept: 'application/json' } };
    if (body !== undefined) {
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(body);
    }
    const response = await fetch(url, options);
    let data = null;
    try { data = await response.json(); } catch (e) { data = null; }
    if (!response.ok) {
      const error = new Error(errorText(data));
      error.status = response.status;
      error.code = data && data.code;
      throw error;
    }
    return data;
  }

  // ---------------------------------------------------------------- shell

  function setStatus(text, kind) {
    const node = document.getElementById('status');
    node.removeAttribute('data-i18n');
    node.textContent = text;
    node.className = 'statusline' + (kind ? ' ' + kind : '');
  }

  function setMeta(text) { document.getElementById('page-meta').textContent = text || ''; }

  function renderNav() {
    const nav = document.getElementById('nav');
    nav.textContent = '';
    for (const page of pages) {
      const on = currentPage === page;
      nav.append(el('a', { id: 'nav-' + page.id, href: '#/' + page.id, class: on ? 'on' : null, 'aria-current': on ? 'page' : null },
        icon(page.icon, 18), el('span', { text: label(page.label) })));
    }
  }

  function pageFromHash() {
    const id = window.location.hash.replace(/^#\/?/, '');
    return pages.find((p) => p.id === id) || pages[0] || null;
  }

  function showPage() {
    const page = pageFromHash();
    const container = document.getElementById('page');
    if (typeof cleanup === 'function') { try { cleanup(); } catch (e) { /* ignore */ } }
    cleanup = null;
    currentPage = page;
    container.textContent = '';
    setMeta('');
    setStatus(t('status.ready'));
    renderNav();
    if (!page) { document.getElementById('page-title').textContent = ''; return; }
    document.getElementById('page-title').textContent = label(page.label);
    document.title = label(page.label) + ' - ' + meta.appName;
    try {
      const result = page.render(container, App);
      if (result && typeof result.then === 'function') result.then((c) => { if (currentPage === page) cleanup = c; }, (e) => setStatus(e.message, 'error'));
      else cleanup = result;
    } catch (e) { setStatus(e.message, 'error'); }
  }

  /** Adds a page to the navigation: { id, label, icon, render(container, App) }. */
  function registerPage(page) {
    if (!page || !page.id || typeof page.render !== 'function') throw new Error('registerPage: id and render are needed');
    pages.push({ id: page.id, label: page.label === undefined ? page.id : page.label, icon: page.icon || 'table', render: page.render });
  }

  // ---------------------------------------------------------------- dialog

  /**
   * Modal dialog. options: { name, title, content, narrow, buttons: [{ id, label, primary, danger, onClick }] }.
   * The primary button is also the Enter key; Escape and the close button close the dialog.
   * Returns { element, close(), setStatus(text, kind) }; the status line has the id <name>-status.
   */
  function openDialog(options) {
    const name = options.name;
    const before = document.activeElement;
    const status = el('p', { id: name + '-status', class: 'dialog-status', role: 'alert' });
    const footer = el('div', { class: 'dialog-f' }, status);
    let primary = null;
    for (const b of options.buttons || []) {
      const button = el('button', {
        type: b.primary ? 'submit' : 'button', id: b.id, text: b.label,
        class: 'btn' + (b.primary ? ' primary' : '') + (b.danger ? ' danger' : ''),
      });
      if (b.primary) primary = b; else button.addEventListener('click', () => b.onClick(dialog));
      footer.append(button);
    }
    const closeButton = el('button', { type: 'button', class: 'ibtn outline', id: 'btn-close-' + name, 'aria-label': t('action.close'), title: t('action.close') }, icon('x', 16));
    const form = el('form', { novalidate: true, autocomplete: 'off' },
      el('div', { class: 'dialog-h' }, el('h2', { id: 'dlg-' + name + '-title', text: options.title }), closeButton),
      el('div', { class: 'dialog-b' }, options.content),
      footer);
    const box = el('div', { class: 'dialog' + (options.narrow ? ' narrow' : ''), id: 'dlg-' + name, role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'dlg-' + name + '-title' }, form);
    const overlay = el('div', { class: 'overlay' }, box);

    const dialog = {
      element: box,
      close() {
        const at = openDialogs.indexOf(dialog);
        if (at < 0) return;
        openDialogs.splice(at, 1);
        overlay.remove();
        if (typeof options.onClose === 'function') options.onClose();
        if (before && typeof before.focus === 'function' && document.contains(before)) before.focus();
      },
      setStatus(text, kind) {
        status.textContent = text || '';
        status.className = 'dialog-status' + (kind ? ' ' + kind : '');
      },
    };
    form.addEventListener('submit', (event) => { event.preventDefault(); if (primary) primary.onClick(dialog); });
    closeButton.addEventListener('click', () => dialog.close());
    overlay.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); dialog.close(); return; }
      if (event.key !== 'Tab') return;
      // keep the focus inside the dialog
      const items = Array.from(box.querySelectorAll('button, input, select, textarea, [tabindex]')).filter((n) => !n.disabled && n.tabIndex >= 0);
      if (items.length === 0) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    });
    document.getElementById('dialog-root').append(overlay);
    openDialogs.push(dialog);
    const firstField = box.querySelector('.dialog-b input:not([readonly]):not([disabled]), .dialog-b select, .dialog-b textarea') || box.querySelector('.dialog-f button');
    if (firstField) firstField.focus();
    return dialog;
  }

  // ---------------------------------------------------------------- list

  /**
   * Table with one selectable row and the "Selected id" field that belongs to it.
   * options: { name, key, columns: [{ name, label, type, decimals, render(row) }], onOpen(row) }.
   * Ids: table <name>-list, rows <name>-row-<key>, field <name>-selected-id. Every cell carries
   * the untranslated value in data-value.
   */
  function createList(options) {
    const name = options.name;
    const key = options.key || 'id';
    let rows = [];
    let selected = null;
    const body = el('tbody');
    const head = el('tr');
    for (const c of options.columns) {
      head.append(el('th', { scope: 'col', class: c.type === 'integer' || c.type === 'number' ? 'num' : null, text: label(c.label) }));
    }
    const table = el('table', { class: 'tbl', id: name + '-list' }, el('thead', null, head), body);
    const input = el('input', { class: 'in num', id: name + '-selected-id', type: 'text', inputmode: 'numeric', autocomplete: 'off' });
    const field = el('div', { class: 'fld-inline' }, el('label', { for: name + '-selected-id', text: t('list.selectedId') }), input);

    function mark() {
      body.querySelectorAll('tr[data-id]').forEach((tr) => {
        const on = tr.getAttribute('data-id') === selected;
        tr.classList.toggle('sel', on);
        tr.setAttribute('aria-selected', on ? 'true' : 'false');
      });
    }
    function select(id, keepInput) {
      const wanted = id === null || id === undefined ? '' : String(id);
      selected = rows.some((r) => String(r[key]) === wanted) ? wanted : null;
      if (!keepInput) input.value = selected === null ? '' : selected;
      mark();
    }
    input.addEventListener('input', () => select(input.value.trim(), true));

    function setRows(next) {
      rows = next || [];
      body.textContent = '';
      if (rows.length === 0) {
        body.append(el('tr', null, el('td', { class: 'empty', colspan: options.columns.length, text: t('list.empty') })));
      }
      for (const row of rows) {
        const id = String(row[key]);
        const tr = el('tr', { id: name + '-row-' + id, 'data-id': id, tabindex: '0' });
        for (const c of options.columns) {
          const raw = row[c.name];
          const td = el('td', { class: c.type === 'integer' || c.type === 'number' ? 'num' : null, 'data-column': c.name, 'data-value': raw === null || raw === undefined ? '' : String(raw) });
          td.append(typeof c.render === 'function' ? c.render(row) : formatValue(c, raw, c.name === key));
          tr.append(td);
        }
        tr.addEventListener('click', () => select(id));
        tr.addEventListener('dblclick', () => { select(id); if (options.onOpen) options.onOpen(row); });
        tr.addEventListener('keydown', (event) => {
          if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            select(id);
            if (event.key === 'Enter' && options.onOpen) options.onOpen(row);
          }
        });
        body.append(tr);
      }
      select(selected);
    }

    return {
      element: el('div', { class: 'table-wrap' }, table),
      selectedField: field,
      setRows,
      select,
      selectedId() { return selected; },
      selectedRow() { return rows.find((r) => String(r[key]) === selected) || null; },
      count() { return rows.length; },
    };
  }

  function button(id, text, iconName, onClick, kind) {
    return el('button', { type: 'button', class: 'btn' + (kind ? ' ' + kind : ''), id, onclick: onClick }, iconName ? icon(iconName, 15) : null, el('span', { text }));
  }

  // ---------------------------------------------------------------- entity pages

  function entityText(entity, name, fallbackKey) {
    return entity.texts && entity.texts[name] !== undefined ? label(entity.texts[name]) : t(fallbackKey);
  }

  /** Edit dialog of an entity; row is null for a new record. Field ids: <entity>-<column>. */
  function openEntityDialog(entity, row, onSaved) {
    const grid = el('div', { class: 'formgrid' });
    const fields = [];
    for (const column of entity.columns) {
      if (!column.inForm) continue;
      if (column.readOnly && !row) continue;
      const id = entity.name + '-' + column.name;
      const value = row ? row[column.name] : null;
      let input;
      if (column.type === 'boolean') {
        input = el('input', { type: 'checkbox', class: 'check', id });
        input.checked = value === true || value === 'true';
      } else if (column.options && column.input !== 'text') {
        input = el('select', { class: 'in', id }, column.required ? null : el('option', { value: '', text: '' }),
          column.options.map((o) => el('option', { value: o, text: o })));
        input.value = value === null || value === undefined ? (column.required ? column.options[0] : '') : String(value);
      } else {
        const numeric = column.type === 'integer' || column.type === 'number';
        input = el('input', { type: 'text', class: 'in' + (numeric ? ' num' : ''), id, inputmode: column.type === 'integer' ? 'numeric' : numeric ? 'decimal' : null });
        input.value = toInput(column, value);
      }
      if (column.readOnly) { input.disabled = true; } else {
        if (column.required) input.setAttribute('aria-required', 'true');
        fields.push({ column, input });
      }
      grid.append(el('label', { class: 'lbl-strong', for: id, text: label(column.label) }), input);
    }
    const save = async (dialog) => {
      const data = {};
      for (const f of fields) data[f.column.name] = f.column.type === 'boolean' ? f.input.checked : fromInput(f.column, f.input.value);
      try {
        const saved = row
          ? await api('PUT', '/api/entity/' + entity.name + '/' + encodeURIComponent(row[entity.key]), data)
          : await api('POST', '/api/entity/' + entity.name, data);
        dialog.close();
        onSaved(saved);
      } catch (e) { dialog.setStatus(e.message); }
    };
    return openDialog({
      name: entity.name, title: label(entity.label), content: grid, narrow: true,
      buttons: [
        { id: 'btn-cancel-' + entity.name, label: t('action.cancel'), onClick: (d) => d.close() },
        { id: 'btn-save-' + entity.name, label: entityText(entity, 'save', 'action.save'), primary: true, onClick: save },
      ],
    });
  }

  /** List page of an entity. Button ids: btn-new-<entity>, btn-edit-<entity>, btn-delete-<entity>, btn-refresh-<entity>. */
  function renderEntityPage(entity, container) {
    const can = (action) => entity.actions.includes(action);
    const name = entity.name;
    let list = null;

    async function load(selectId, statusText) {
      try {
        const rows = await api('GET', '/api/entity/' + name);
        list.setRows(rows);
        if (selectId !== undefined) list.select(selectId);
        setMeta(t('list.records', { count: formatNumber(rows.length, 0) }));
        if (statusText) setStatus(statusText);
      } catch (e) { setStatus(e.message, 'error'); }
    }
    const saved = (row) => load(row[entity.key], t('entity.saved', { label: entity.label, id: row[entity.key] }));
    function edit() {
      const row = list.selectedRow();
      if (!row) { setStatus(entityText(entity, 'noneSelected', 'entity.noneSelected'), 'error'); return; }
      openEntityDialog(entity, row, saved);
    }
    function remove() {
      const row = list.selectedRow();
      if (!row) { setStatus(entityText(entity, 'noneSelected', 'entity.noneSelected'), 'error'); return; }
      const id = row[entity.key];
      openDialog({
        name: name + '-delete', title: label(entity.label), narrow: true,
        content: el('p', { text: t('entity.confirmDelete', { label: entity.label, id }) }),
        buttons: [
          { id: 'btn-cancel-delete-' + name, label: t('action.cancel'), onClick: (d) => d.close() },
          {
            id: 'btn-confirm-delete-' + name, label: t('action.delete'), primary: true, onClick: async (d) => {
              try {
                await api('DELETE', '/api/entity/' + name + '/' + encodeURIComponent(id));
                d.close();
                load(null, t('entity.deleted', { label: entity.label, id }));
              } catch (e) { d.setStatus(e.message); }
            },
          },
        ],
      });
    }

    list = createList({
      name, key: entity.key, columns: entity.columns.filter((c) => c.inList),
      onOpen: can('update') ? edit : null,
    });
    const toolbar = el('div', { class: 'toolbar' },
      can('create') ? button('btn-new-' + name, entityText(entity, 'new', 'action.new'), 'plus', () => openEntityDialog(entity, null, saved), 'primary') : null,
      can('update') ? button('btn-edit-' + name, entityText(entity, 'edit', 'action.edit'), 'pencil', edit) : null,
      can('delete') ? button('btn-delete-' + name, entityText(entity, 'delete', 'action.delete'), 'trash-2', remove, 'danger') : null,
      button('btn-refresh-' + name, entityText(entity, 'refresh', 'action.refresh'), 'refresh-cw',
        () => load(undefined, t('entity.refreshed', { labelPlural: entity.labelPlural }))),
      el('span', { class: 'spacer' }),
      list.selectedField);
    container.append(el('div', { class: 'card' }, toolbar, list.element));
    return load();
  }

  // ---------------------------------------------------------------- start

  const App = {
    registerPage, api, t, label, hasKey, el, icon, button, createList, openDialog, setStatus, setMeta,
    formatNumber, formatDate, formatValue, toInput, fromInput,
    get lang() { return lang; },
    get meta() { return meta; },
    icons: ICONS,
  };
  window.App = App;

  async function loadResources() {
    const files = [['en', 'en.json'], ['de', 'de.json'], ['en', 'app-en.json'], ['de', 'app-de.json']];
    await Promise.all(files.map(async (entry) => {
      try {
        const response = await fetch('i18n/' + entry[1]);
        if (response.ok) Object.assign(resources[entry[0]], await response.json());
      } catch (e) { /* a missing file leaves the keys visible */ }
    }));
  }

  async function start() {
    lang = chooseLanguage();
    const toggle = document.getElementById('btn-toggle-nav');
    const app = document.getElementById('app');
    toggle.append(icon('panel-left', 18));
    const setHidden = (hidden) => {
      app.classList.toggle('sb-hidden', hidden);
      toggle.setAttribute('aria-expanded', hidden ? 'false' : 'true');
      store('nav-hidden', hidden ? '1' : '0');
    };
    setHidden(store('nav-hidden') === '1');
    toggle.addEventListener('click', () => setHidden(!app.classList.contains('sb-hidden')));
    document.querySelectorAll('#lang-switch button').forEach((b) => b.addEventListener('click', () => setLanguage(b.getAttribute('data-lang'))));

    await loadResources();
    applyStaticTexts();
    try { meta = await api('GET', '/api/meta'); } catch (e) { setStatus(e.message, 'error'); return; }
    document.getElementById('app-name').textContent = meta.appName;
    // entity pages first, then the pages that pages.js registered
    const custom = pages.splice(0, pages.length);
    for (const entity of meta.entities) {
      registerPage({ id: entity.name, label: entity.labelPlural, icon: entity.icon, render: (container) => renderEntityPage(entity, container) });
    }
    custom.forEach((p) => pages.push(p));
    window.addEventListener('hashchange', showPage);
    showPage();
  }

  // deferred scripts run before DOMContentLoaded, so pages.js has registered its pages by then
  if (document.readyState === 'complete') start();
  else document.addEventListener('DOMContentLoaded', start);
})();
