<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Build the Claude app "{{APP_TITLE}}"

You are a coding agent (or a person following this by hand). EQLMaker has prepared the workspace
`{{WORKSPACE}}` for a Claude app: an application that runs inside Claude Desktop as an installed app and works
on the live data of the original application in EQLServer. EQLMaker does **not** write the app for you. You
complete it here, and the checks at the end decide whether it behaves like the original.

## Architecture (do not change it)

- The original application stays unchanged and keeps running on EQLServer. Its source code is not needed.
- The Claude app has its **own database** on EQLServer (or its own schema `{{SCHEMA}}`) and its **own local MCP
  server** `{{SERVER_NAME}}`. There is no hub server: one application = one database = one MCP server.
- `mcp-server/` is a generic MCP server (Node, stdio, no network listener, one dependency `pg`). It reads the
  application description `mcp-server/apps/{{APP_NAME}}.json` and serves it with nine fixed tools: `status`,
  `describe`, `lookups`, `query`, `get_record`, `plan_operation`, `run_operation`, `run_destructive_operation`,
  `write_outcome`. You change the description, not the tools.
- `app/app.html` is the page. It runs inside Claude Desktop and reaches the server only as `host:{{SERVER_NAME}}`
  (see `app/capabilities.json`). It holds no data.
- Every write is a preview first: `plan_operation` runs the real statements in a transaction that is rolled
  back and returns before/after rows and a one-time token. The page shows that preview, and only after the user
  confirmed it does `run_operation` (or `run_destructive_operation` for deletions) write, in exactly one
  transaction. Never add a write path that skips the preview.

## What you have

- {{SPEC}}
- Tables of the application:

{{TABLES}}
- `mcp-server/config/{{APP_NAME}}.json` - host, port, database, schema `{{SCHEMA}}`, login, password profile
  `{{APP_NAME}}`. There is no password in any file.
- `claude_desktop_config.json` - the entry for Claude Desktop.

## Steps

1. **Database and login.** Give the app its own database or schema on EQLServer (a copy of the application's
   tables for a demo, the production tables for real use) and its own login with `SELECT` on the schema and
   `INSERT`/`UPDATE`/`DELETE` only on the tables the app writes. Do not change the schema of the original
   application.
2. **Password.** `cd mcp-server`, `npm ci`, then `node bin/set-password.mjs --profile {{APP_NAME}}` (asks without
   echo; stored with Windows DPAPI for the current user).
3. **Description.** Complete `mcp-server/apps/{{APP_NAME}}.json` screen by screen from the specification and the
   reference pictures of the recordings:
   - `entities`: table, key, `identity`, `fields` (`c` column, `l` label, `t` text|memo|int|number|money|date|bool,
     `req`, `len`, `ro`, `min`, `max`), `children` for lines that belong to a header, `checks`, `guards`.
     Every entity gets `<entity>.create/update/delete`.
   - `queries`: `sql` + `params` + `where` + `filters` + `search` + `sort` (whitelist) + `sum`, or `parts` for
     dashboards. Name the list query of an entity like the entity, then the page links rows to the record form.
   - `lookups`: named SELECT statements for selection lists.
   - `operations`: domain operations of the original (post, ship, close a period ...) as steps (`lock`, `check`,
     `update`, `insert`, `delete`, `sql`, `if`, `repeat`, `foreach`, `result`). Use the same SQL that the
     recording shows for the original, so both write the same rows.
   - SQL text lives only in the description; every caller value is a bind parameter.
4. **Page.** Start from the generic `app/app.html` (menu, lists, record forms and the preview dialog come from
   `describe`). Then build the screens of the original one by one: same functions, same order of work, a modern
   layout with a left menu. Keep every text in the `DE` dictionary of the page (English key, German text) so the
   DE/EN switcher at the bottom left covers the whole page.
5. **Tests.** `npm test` in `mcp-server` (add tests for your own operations next to `test/template.test.mjs`;
   with `EQLMAKER_TEST_PG` set they run against a real server).
6. **Claude Desktop.** Merge `claude_desktop_config.json` into the `mcpServers` of Claude Desktop's
   `claude_desktop_config.json` and restart Claude Desktop completely. The server must run as the Windows user
   that stored the password.
7. **Publish and install.** Publish `app/app.html` as a private artifact with the capabilities of
   `app/capabilities.json` (only `host:{{SERVER_NAME}}` and the listed tools, plus `desktop`). Open it in Claude
   Desktop and choose "Install as app" (a beta feature of Claude Desktop).
8. **Prove it in both directions.** Record the same steps in the original and in the Claude app on the same
   data and compare the database changes: `eqlmaker verify db <original> <claude-app>`. Then change a record in
   the Claude app and read it back in the original, and the other way round.

## Rules

- No passwords, connection secrets or personal data in the description, the page or the repository.
- Do not change the original application or its tables' structure.
- A function that the app cannot offer yet is listed as missing, not hidden.
- Report what you built, what is missing and the results of step 5 and step 8.
