// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Helpers for archive pages: read data through /query/<name>, never write.
'use strict';
const archive = {
  async query(name, args) {
    const q = new URLSearchParams(args || {}).toString();
    const r = await fetch('/query/' + encodeURIComponent(name) + (q ? '?' + q : ''));
    if (!r.ok) throw new Error(await r.text());
    return r.json();
  },
  control(mask, text) { return document.querySelector('#' + CSS.escape(mask) + ' .ctl[data-text="' + CSS.escape(text) + '"]'); },
  go(page) { location.href = page; },
};
document.addEventListener('click', (e) => {
  const off = e.target.closest('.archive-off');
  if (off) { e.preventDefault(); e.stopPropagation(); }
}, true);
