<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](claude-app.md) · [Deutsch](de/claude-app.md)

# Build Claude apps from legacy applications

A **Claude app** is a new user interface for a legacy application that runs **inside Claude Desktop** as an
installed app. It works on the live data of the original application in EQLServer, through a **local MCP
server** of its own. The original application keeps running unchanged, and its **source code is not needed**.

EQLMaker prepares the Claude app with one guided step, `eqlmaker claude-app`. It writes the MCP server, the
application description, a working generic page, the Claude Desktop entry and the instructions. A coding agent
or a person then completes the app screen by screen, and the comparison tests decide whether it behaves like the
original.

> **Status: proof of concept, beta.** Six Claude apps have been built with this method (see [examples](#examples)). The
> command is new in EQLMaker and prepares the app; it does not write the finished screens for you. "Install as
> app" is a beta feature of Claude Desktop. The app is private and needs Claude Desktop.

**Contents:** [What you need](#what-you-need) · [Architecture](#architecture) ·
[Step by step](#step-by-step) · [Block for a coding agent](#block-for-a-coding-agent) ·
[Troubleshooting](#troubleshooting) · [FAQ](#faq) · [Limits](#limits-and-status) · [Examples](#examples)

## What you need

- **Claude Desktop** on Windows (the password store uses Windows DPAPI) with a Claude plan that can publish artifacts and install them as apps.
- **EQLServer** with the data of the legacy application (Access, SQL Server, MySQL, Firebird, SQL Anywhere,
  Paradox and others), and the legacy application connected to it, unchanged. See
  [other databases](other-databases.md).
- **EQLMaker** with a project that has recordings and a specification of the application
  (see [quick start](quick-start.md)). Without a specification the command still works; it then lists no tables.
- **Node.js 20 or later** on the machine that runs Claude Desktop (the MCP server is a small Node program).
- A database login for the app. The password is never written to a file; it is stored with Windows DPAPI
  for the Windows user that runs Claude Desktop.

## Architecture

```
 Legacy application (unchanged) ──┐
                                  ├──> EQLServer: same database, same rows
 Claude Desktop                   │
   installed Claude app (page) ───┘ via host:eqlmaker-<app>
        │  window.claude.use("mcp").callTool(...)
        ▼
   local MCP server eqlmaker-<app> (Node, stdio, no network listener)
        reads mcp-server/apps/<app>.json (entities, queries, lookups, operations)
        writes only after preview + confirmation, audit log per app
```

- **One application = one database (or schema) = one MCP server.** There is no hub server. The server is
  called `eqlmaker-<app>`, has its own configuration, its own password profile and its own audit log
  (`%LOCALAPPDATA%\EQLMaker\audit\<app>.jsonl`).
- The MCP server is generic and has nine fixed tools: `status`, `describe`, `lookups`, `query`, `get_record`,
  `plan_operation`, `run_operation`, `run_destructive_operation`, `write_outcome`. What the app can do is
  defined by the **application description** (`mcp-server/apps/<app>.json`): entities with their fields, list
  queries with search, sorting and filters, selection lists, and domain operations of the original. All SQL text
  is in the description; every value from the page is a bind parameter.
- The **page** (`app/app.html`) is a single HTML file. It is published as a private artifact with the
  capability to call only `host:eqlmaker-<app>` and only the listed tools, and installed in Claude Desktop with
  "Install as app". It holds no data.
- **Preview before every write.** `plan_operation` runs the real statements in a transaction that is rolled
  back, and returns the rows before and after plus a one-time token (valid 10 minutes). The page shows this
  preview; only after the user confirms does `run_operation` write, in one transaction. Deleting needs
  `run_destructive_operation`. A request id protects against writing twice, `write_outcome` answers "did my
  write happen?" after a lost connection.
- The page speaks **German and English** (switcher at the bottom left), even if the original knows only one
  language. Data is not translated.

## Step by step

### 1. Capture the legacy application

Record the work in the original while EQLMaker measures what happens in the database, then build the
specification:

```
eqlmaker init my-app
cd my-app
eqlmaker record --start --name customers     # work in the original, then:
eqlmaker record --stop
eqlmaker correlate <recording>
eqlmaker spec
```

The manual, a narrated video or the files of the application can be added; see [pipeline](pipeline.md).

### 2. Data model

Decide which tables the app reads and which it writes (entities with a key). Give the app its own database or
schema on EQLServer: a copy of the application's tables for a demo, the production tables for real use. Create
a login with `SELECT` on the schema and `INSERT`/`UPDATE`/`DELETE` only on the tables the app writes. Do not
change the schema of the original application.

### 3. Prepare the Claude app

```
eqlmaker claude-app --name shop --schema shop --entity customers:id,orders:id
```

| Option | Meaning |
|---|---|
| `--name <app>` | name of the app (default: the project folder); the server is `eqlmaker-<app>` |
| `--title <text>` | title shown in the app (default: the name) |
| `--schema <s>` | database schema of the app (default `public`) |
| `--entity table:key,...` | tables the app may create, change and delete, with their key column |
| `--force` | copy the template over an existing `claude-app/` folder (your description and configuration are kept) |

It writes `claude-app/`:

| File | Content |
|---|---|
| `mcp-server/` | the MCP server with its tests (`npm test`) |
| `mcp-server/apps/<app>.json` | the application description: one list query per table of the specification, the entities with fields read from the database |
| `mcp-server/config/<app>.json` | host, port, database, schema, login, password profile; no password |
| `app/app.html`, `app/capabilities.json` | the page and the capabilities it is published with |
| `claude_desktop_config.json` | the `mcpServers` entry for Claude Desktop |
| `CLAUDE-APP.md` | the instructions for the coding agent or for you |

The connection comes from `database.connection` in `eqlmaker.json`. The same step is available to MCP clients
as the tool `claude_app` (see [MCP](mcp.md)).

Then store the password once:

```
cd claude-app/mcp-server
npm ci
node bin/set-password.mjs --profile shop
```

### 4. Generate the screens

The generic page already works: a left menu from the list queries, lists with search and paging, record forms
from the entity fields, and the preview dialog for every change. Now complete it from the specification and the
reference pictures of the recordings, screen by screen:

- complete the description: labels, required fields, lengths, selection lists, header and line tables
  (`children`), checks and guards, and the domain operations of the original (post, ship, close a period)
  with the same SQL the recording shows, so both write the same rows;
- build the screens of the original on the page: same functions, same order of work, a modern layout with a
  left menu.

This is the work of a coding agent; the [block below](#block-for-a-coding-agent) starts it.

### 5. Preview and confirm

Every write goes through the preview. Test it on purpose: change a record, cancel the preview (nothing is
written), confirm it (written once), confirm the same token again (refused). The template tests in
`mcp-server/test/` check exactly this; with `EQLMAKER_TEST_PG` set they run against a real PostgreSQL.

### 6. Multilingual

Every text of the page is in the `DE` dictionary (English key, German text). The switcher at the bottom left
changes the whole page; the template test fails if a text has no German entry. More languages: add a dictionary
and a button. See [translating](translating.md).

### 7. Install

1. Merge `claude-app/claude_desktop_config.json` into the `mcpServers` of Claude Desktop's own
   `claude_desktop_config.json` and restart Claude Desktop completely.
2. Publish `app/app.html` as a private artifact with the capabilities from `app/capabilities.json`
   (only `host:eqlmaker-<app>`, only the listed tools, plus `desktop`).
3. Open it in Claude Desktop and choose **Install as app** (beta).

### 8. Verify both directions

Record the same steps in the original and in the Claude app on the same data, and compare what both wrote:

```
eqlmaker verify db <recording-original> <recording-claude-app> --keys customers.id
```

Then change a record in the Claude app and read it back in the original, and the other way round. Both
directions are part of every example video.

## Block for a coding agent

Paste this into a coding agent that runs in the project folder (with EQLMaker as MCP server or on the command
line):

```text
Build a Claude app for the legacy application of this EQLMaker project.
1. Run `eqlmaker claude-app --name <app> --schema <schema> --entity <table:key,...>`
   (or the MCP tool claude_app) and read claude-app/CLAUDE-APP.md completely.
2. Keep the architecture: one database, one local MCP server eqlmaker-<app>, the nine fixed tools,
   preview and confirmation before every write. Never add a write path that skips the preview.
3. Complete mcp-server/apps/<app>.json screen by screen from spec/spec.md and the reference pictures:
   entities, list queries, lookups, and the domain operations with the SQL the recordings show.
4. Build the screens of the original in app/app.html (left menu, same functions, same order of work).
   Every text goes into the DE dictionary; the DE/EN switcher must cover the whole page.
5. Run `npm test` in claude-app/mcp-server and add tests for every operation you add.
6. Do not change the original application or its table structure. No passwords in any file.
7. Prove it: same steps in the original and in the app, `eqlmaker verify db <A> <B>`, and a change in each
   direction read back on the other side. Report what is built, what is missing and the test results.
```

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The page says "Please open it in the installed app in Claude Desktop." | The page runs outside Claude Desktop, or it was published without the `mcp` capability. Publish with `app/capabilities.json` and open the installed app. |
| `server_not_found` / the app shows no menu | Claude Desktop does not know `eqlmaker-<app>`. Check the entry in `claude_desktop_config.json` (absolute paths, `node` on the PATH) and restart Claude Desktop completely. |
| `status` reports `ok: false` | EQLServer not reachable, wrong host/port/database in `mcp-server/config/<app>.json`, or no password stored. Run `node bin/set-password.mjs --profile <app>` as the Windows user that runs Claude Desktop. |
| `token_invalid` or `token_expired` | A preview token is valid once and for 10 minutes. Open the preview again. |
| `conflict` on save | Someone (for example the original application) changed the record after it was loaded. Reload and repeat. |
| Deleting is refused with `validation` | Deletions need `run_destructive_operation`; the page does this when the preview is a deletion. |
| `claude-app/ exists already` | Run with `--force` to refresh the template; your description and configuration are kept. |
| The original does not show the change | Many legacy applications do not refresh on their own. Reload the list or reopen the form. |

## FAQ

**Is this an MCP App?** The app uses MCP: a local MCP server per application, called by the page through Claude
Desktop. The page itself is a private artifact installed with "Install as app"; it is not served as a `ui://`
resource by the server.

**Do I need the source code of the legacy application?** No. EQLMaker watches the running application and the
database. The original stays unchanged.

**Where is the data?** In your own EQLServer. The MCP server runs locally and connects only to it.

**Can several Claude apps share one MCP server?** No, on purpose: one application, one database, one server,
one password profile, one audit log.

**Can the app write data without me?** No. Every write shows a preview first and needs your confirmation in the
app.

**Is it a generator that builds the whole app?** No. `eqlmaker claude-app` prepares a working base; the screens
of the original are completed by a coding agent or a person and checked with comparison tests.

## Limits and status

- Proof of concept, beta. Six apps exist as examples; none is in production use.
- Needs Claude Desktop; "Install as app" is a beta feature there. The app is private to your account.
- The legacy application does not refresh on its own; changes from the Claude app show after a reload.
- The generic page covers lists, record forms and single-table changes. Header and line documents, domain
  operations and special screens are added per application.
- The Paradox example needs EQLServer 1.12.0, which is not yet released.

## Examples

Each example shows the unchanged original and the Claude app on the same data in EQLServer, with changes in
both directions and the installation as a Claude app.

| Original | Technology | Video (German) | Video (English) |
|---|---|---|---|
| Northwind (Access template) | Microsoft Access | [watch](https://eqlserver.de/poc-northwind-claude-app/) | [watch](https://eqlserver.de/poc-northwind-claude-app/en.html) |
| Contoso University | ASP.NET Core, SQL Server | [watch](https://eqlserver.de/poc-contoso-claude-app/) | [watch](https://eqlserver.de/poc-contoso-claude-app/en.html) |
| InvoicePlane | PHP, MySQL | [watch](https://eqlserver.de/poc-invoiceplane-claude-app/) | [watch](https://eqlserver.de/poc-invoiceplane-claude-app/en.html) |
| TurboCASH | Delphi, Firebird | [watch](https://eqlserver.de/poc-turbocash-claude-app/) | [watch](https://eqlserver.de/poc-turbocash-claude-app/en.html) |
| Property management | PowerBuilder 6.5, SQL Anywhere | [watch](https://eqlserver.de/poc-hausverwaltung-claude-app/) | [watch](https://eqlserver.de/poc-hausverwaltung-claude-app/en.html) |
| Invoicing application | Delphi, Paradox (BDE) | [watch](https://eqlserver.de/poc-fakturierung-claude-app/) | [watch](https://eqlserver.de/poc-fakturierung-claude-app/en.html) |

More on [eqlmaker.org](https://eqlmaker.org/#claude-app).
