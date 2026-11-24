// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Routes of this application beyond the generic entity API. A handler gets a context
// { params, query, body() } and returns the value that is sent as JSON; errors are thrown as
// core.HttpError(status, messageKey, args). Use core.withTransaction(pool, fn) for writes that
// belong together.
'use strict';

module.exports = function routes(router, app) {
  router.get('/api/about', () => ({ appName: app.config.appName, entities: app.entities.length }));
};
