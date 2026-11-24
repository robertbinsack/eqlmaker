// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Custom pages of this application. A page is registered with an id, a label (a key of
// i18n/app-en.json and app-de.json) and a render function that fills the given container.
// window.App offers the building blocks: api, t, el, icon, button, createList, openDialog,
// setStatus, setMeta, formatNumber, formatDate.
(function () {
  'use strict';
  const App = window.App;

  App.registerPage({
    id: 'about',
    label: 'page.about.title',
    icon: 'info',
    async render(container) {
      const about = await App.api('GET', '/api/about');
      container.append(App.el('div', { class: 'card' },
        App.el('div', { class: 'card-b' },
          App.el('h2', { id: 'about-heading', text: App.t('page.about.heading') }),
          App.el('p', { id: 'about-text', text: App.t('page.about.text', { appName: about.appName, count: App.formatNumber(about.entities, 0) }) }))));
    },
  });
})();
