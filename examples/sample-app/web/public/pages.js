// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Invoice page and the dialog "New invoice" of the sample application. The element ids are
// listed in ../../CONTROLS.md; they do not change with the language.
(function () {
  'use strict';
  const App = window.App;
  const el = App.el;
  const t = App.t;

  function totalField(name, labelKey) {
    const id = 'invoice-' + name;
    const input = el('input', { type: 'text', class: 'in num', id, readonly: true, tabindex: '-1', 'data-value': '0.00' });
    return { name, input, nodes: [el('label', { class: 'lbl-strong', for: id, text: t(labelKey) }), input] };
  }

  function openInvoiceDialog(onSaved) {
    let lines = [];        // [{ sku, quantity }] as sent to the server
    let request = 0;       // only the answer to the newest preview is shown
    const customer = el('input', { type: 'text', class: 'in num', id: 'invoice-customer-no', inputmode: 'numeric' });
    const sku = el('input', { type: 'text', class: 'in', id: 'invoice-sku' });
    const quantity = el('input', { type: 'text', class: 'in num', id: 'invoice-quantity', inputmode: 'numeric' });
    const lineBody = el('tbody');
    const lineTable = el('table', { class: 'tbl', id: 'invoice-lines' },
      el('thead', null, el('tr', null,
        el('th', { scope: 'col', text: t('invoice.sku') }),
        el('th', { scope: 'col', text: t('invoice.article') }),
        el('th', { scope: 'col', class: 'num', text: t('invoice.quantity') }),
        el('th', { scope: 'col', class: 'num', text: t('invoice.price') }),
        el('th', { scope: 'col', class: 'num', text: t('invoice.lineNet') }))),
      lineBody);
    const totals = [totalField('net', 'invoice.net'), totalField('discount', 'invoice.discount'), totalField('tax', 'invoice.tax'), totalField('gross', 'invoice.gross')];
    let dialog = null;

    function show(preview) {
      lineBody.textContent = '';
      preview.lines.forEach((line, i) => {
        const cell = (value, text, num) => el('td', { class: num ? 'num' : null, 'data-value': String(value), text });
        lineBody.append(el('tr', { id: 'invoice-line-' + (i + 1) },
          cell(line.sku, line.sku), cell(line.name, line.name),
          cell(line.quantity, App.formatNumber(line.quantity, 0), true),
          cell(line.unit_price, App.formatNumber(line.unit_price, 2), true),
          cell(line.line_net, App.formatNumber(line.line_net, 2), true)));
      });
      for (const field of totals) {
        field.input.value = App.formatNumber(preview[field.name], 2);
        field.input.setAttribute('data-value', preview[field.name]);
      }
    }

    /** Asks the server for the totals of the given lines; returns false when it refused. */
    async function recalculate(nextLines) {
      const mine = ++request;
      try {
        const preview = await App.api('POST', '/api/invoices/preview', { customer_id: customer.value.trim(), lines: nextLines });
        if (mine === request) show(preview);
        return true;
      } catch (e) {
        if (mine === request) dialog.setStatus(e.message);
        return false;
      }
    }

    async function addLine() {
      const next = lines.concat([{ sku: sku.value.trim(), quantity: quantity.value.trim() }]);
      if (await recalculate(next)) {
        lines = next;
        sku.value = '';
        quantity.value = '';
        dialog.setStatus(t('invoice.lineAdded'), 'info');
        sku.focus();
      }
    }

    async function save() {
      try {
        const saved = await App.api('POST', '/api/invoices', { customer_id: customer.value.trim(), lines });
        dialog.close();
        onSaved(saved);
      } catch (e) { dialog.setStatus(e.message); }
    }

    customer.addEventListener('input', () => { recalculate(lines); });
    const addButton = App.button('btn-add-line', t('invoice.addLine'), 'plus', addLine);
    // Enter in the line fields adds the line instead of saving the invoice
    for (const input of [sku, quantity]) {
      input.addEventListener('keydown', (event) => { if (event.key === 'Enter') { event.preventDefault(); addLine(); } });
    }

    const content = [
      el('div', { class: 'toolbar' },
        el('div', { class: 'fld-inline' }, el('label', { for: 'invoice-customer-no', text: t('invoice.customerNo') }), customer)),
      el('div', { class: 'card' }, el('div', { class: 'table-wrap' }, lineTable)),
      el('div', { class: 'toolbar' },
        el('div', { class: 'fld-inline' }, el('label', { for: 'invoice-sku', text: t('invoice.sku') }), sku),
        el('div', { class: 'fld-inline' }, el('label', { for: 'invoice-quantity', text: t('invoice.quantity') }), quantity),
        addButton),
      el('div', { class: 'formgrid' }, totals.map((f) => f.nodes).reduce((all, nodes) => all.concat(nodes), [])),
    ];
    dialog = App.openDialog({
      name: 'invoice', title: t('invoice.new'), content,
      buttons: [
        { id: 'btn-cancel-invoice', label: t('action.cancel'), onClick: (d) => d.close() },
        { id: 'btn-save-invoice', label: t('invoice.save'), primary: true, onClick: save },
      ],
    });
    show({ lines: [], net: '0.00', discount: '0.00', tax: '0.00', gross: '0.00' });
  }

  App.registerPage({
    id: 'invoice',
    label: 'invoice.title',
    icon: 'file-text',
    render(container) {
      const statusChip = (row) => el('span', { class: 'chip ' + (row.status === 'paid' ? 'ok' : 'warn'), text: t(row.status === 'paid' ? 'invoice.status.paid' : 'invoice.status.open') });
      const list = App.createList({
        name: 'invoice', key: 'id',
        columns: [
          { name: 'id', label: 'list.id', type: 'integer' },
          { name: 'number', label: 'invoice.number', type: 'text' },
          { name: 'customer', label: 'invoice.customer', type: 'text' },
          { name: 'invoice_date', label: 'invoice.date', type: 'date' },
          { name: 'net', label: 'invoice.net', type: 'number', decimals: 2 },
          { name: 'discount', label: 'invoice.discount', type: 'number', decimals: 2 },
          { name: 'tax', label: 'invoice.tax', type: 'number', decimals: 2 },
          { name: 'gross', label: 'invoice.gross', type: 'number', decimals: 2 },
          { name: 'status', label: 'invoice.status', type: 'text', render: statusChip },
        ],
      });

      async function load(selectId, statusText) {
        try {
          const rows = await App.api('GET', '/api/invoices');
          list.setRows(rows);
          if (selectId !== undefined) list.select(selectId);
          App.setMeta(t('list.records', { count: App.formatNumber(rows.length, 0) }));
          if (statusText) App.setStatus(statusText);
        } catch (e) { App.setStatus(e.message, 'error'); }
      }
      async function markPaid() {
        const row = list.selectedRow();
        if (!row) { App.setStatus(t('invoice.noneSelected'), 'error'); return; }
        try {
          const paid = await App.api('POST', '/api/invoices/' + encodeURIComponent(row.id) + '/paid');
          await load(paid.id, t('invoice.markedPaid', { number: paid.number }));
        } catch (e) { App.setStatus(e.message, 'error'); }
      }

      container.append(el('div', { class: 'card' },
        el('div', { class: 'toolbar' },
          App.button('btn-new-invoice', t('invoice.new'), 'plus', () => openInvoiceDialog((saved) => load(saved.id, t('invoice.saved', { number: saved.number }))), 'primary'),
          App.button('btn-mark-paid', t('invoice.markPaid'), 'check', markPaid),
          App.button('btn-refresh-invoice', t('invoice.refresh'), 'refresh-cw', () => load(undefined, t('invoice.refreshed'))),
          el('span', { class: 'spacer' }),
          list.selectedField),
        list.element));
      return load();
    },
  });
})();
