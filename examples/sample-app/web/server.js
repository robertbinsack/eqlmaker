// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Entry point of the web application. Everything that belongs to one application lives in app/
// (config.js, entities.js, routes.js) and in public/pages.js; this file stays as it is.
'use strict';

const path = require('node:path');
const core = require('./lib/core');
const entity = require('./lib/entity');

/** Builds the HTTP server. options.pool replaces the database pool (used by tests). */
function build(options) {
  const opts = options || {};
  const config = opts.config || require('./app/config');
  const publicDir = path.join(__dirname, 'public');
  const messages = core.loadMessages(publicDir);
  const pool = opts.pool || core.createPool(config.connection, { applicationName: config.applicationName });
  const entities = require('./app/entities').map(entity.normalizeEntity);
  const router = core.createRouter();

  router.get('/api/meta', () => ({
    appName: config.appName,
    languages: ['en', 'de'],
    entities: entities.map(entity.describeEntity),
  }));
  // routes of the application come first, so that they can replace a generic entity route
  require('./app/routes')(router, { pool, core, config, entities, messages });
  entity.registerEntities(router, pool, entities);

  const server = core.createServer({ router, publicDir, messages });
  return { server, pool, config, router };
}

if (require.main === module) {
  const app = build();
  app.pool.on('error', (e) => process.stderr.write('database: ' + e.message + '\n'));
  // local interface only; put a reverse proxy in front for anything else
  app.server.listen(app.config.port, '127.0.0.1', () => {
    process.stdout.write(app.config.appName + ' listening on http://127.0.0.1:' + app.server.address().port + '/\n');
  });
  const stop = () => { app.server.close(() => app.pool.end().then(() => process.exit(0))); };
  process.on('SIGINT', stop);
  process.on('SIGTERM', stop);
}

module.exports = { build };
