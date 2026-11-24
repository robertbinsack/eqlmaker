// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Settings of the sample application. The password is never written here: it comes from
// PGPASSWORD (or is part of SAMPLE_DB).
'use strict';

module.exports = {
  appName: 'Sample Shop',
  applicationName: 'sample-web',
  connection: process.env.SAMPLE_DB || 'postgres://sample_app@127.0.0.1:5432/eqlmaker_sample',
  port: process.env.PORT === undefined ? 8780 : Number(process.env.PORT),
  // deliberate defect for demonstrating comparison tests, see README.md
  flaw: process.env.SAMPLE_FLAW === '1',
};
