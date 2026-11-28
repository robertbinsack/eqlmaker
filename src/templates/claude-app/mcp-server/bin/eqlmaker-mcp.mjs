#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Start: node bin/eqlmaker-mcp.mjs --config config/{{APP_NAME}}.json   (stdio MCP server, no network listener)
import { loadConfig, createServer, serveStdio } from '../src/server.mjs';

const i = process.argv.indexOf('--config');
if (i < 0 || !process.argv[i + 1]) { process.stderr.write('usage: eqlmaker-mcp --config <file>\n'); process.exit(2); }
const cfg = loadConfig(process.argv[i + 1]);
// one process = one application = one database: every application has its own Desktop entry and its own config
const server = await createServer(cfg);
serveStdio(server, {
  name: cfg.name || 'eqlmaker',
  instructions: 'Reads and changes the data of one application on EQLServer. Read with query/get_record/lookups. Every change: plan_operation first (preview, nothing written), show the preview to the user, then run_operation or run_destructive_operation with the token.',
});
