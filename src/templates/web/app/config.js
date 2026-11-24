// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Settings of this application. The connection string comes from the environment; the password
// is never written here: it is part of that string or comes from PGPASSWORD.
'use strict';

module.exports = {
  appName: '{{APP_NAME}}',
  applicationName: 'web-app',
  connection: process.env['{{DB_ENV}}'] || 'postgres://127.0.0.1:5432/postgres',
  port: process.env.PORT === undefined ? 8700 : Number(process.env.PORT),
};
