<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](sample-app.md) · [Deutsch](de/sample-app.md)

# Sample application

"Sample Shop" is a small business application made for this repository: customers, articles and
invoices in five masks, on PostgreSQL. It exists twice with the same behaviour, as a Windows
program and in the browser, so that the whole pipeline can be shown and tested without any
third-party software. The data is invented.

| Part | Where | What |
|---|---|---|
| Windows program | `examples/sample-app/desktop`, built as `bin\sample-desktop.exe` | plays the original; Windows Forms, English |
| Browser variant | `examples/sample-app/web` | plays the rebuilt application; built on the web template, English and German |
| Database | `examples/sample-app/db` | tables and invented data; role `sample_app`, database `eqlmaker_sample` |
| Scenarios | `examples/sample-app/scenarios` | the same business steps for both variants |
| Handbook | `examples/sample-app/manual/handbook.md` | a short manual that explains the masks and leaves out most rules |
| Calculation cases | `examples/sample-app/test-vectors.json` | invoices with their expected amounts, used by both variants |

## The hidden rules

The point of the sample is that the rules of saving an invoice are nowhere on the screen. When
an invoice is saved, in one transaction:

1. Every line gets its net amount from quantity and the price of the article at that moment.
2. Discount by customer group: group A gets 5 % from a net of 500, group B gets 3 % from a net
   of 1000, group C gets none.
3. Tax per tax class (19 % and 7 %) on the discounted amount of the class, rounded once per
   class, then added.
4. Credit limit: the open invoices of the customer plus the new one must not exceed the limit;
   otherwise nothing is written.
5. The invoice number comes from a counter: `INV-<year>-<5 digits>`.
6. For every line the stock of the article is decreased and a stock movement row is written.

The handbook mentions none of the numbers. A rebuild that looks the same can still round
differently or forget the stock movements. That is what the comparison of measured database
changes is for.

## eqlmaker sample

```text
eqlmaker sample [--flaw] [--no-video] [--visible]
```

The command runs the whole pipeline against the database server of the project configuration.
It needs a superuser connection, `wal_level = logical` on the server, Node.js, and the one
dependency of the browser variant installed (`npm ci` in `examples/sample-app/web`; the setup
of the package does that).

What it does, in order:

1. Creates the role `sample_app` and the database `eqlmaker_sample` with tables and data. An
   existing sample database is replaced. The password of the role is made up at random for the
   run, unless `SAMPLE_APP_PASSWORD` is set.
2. Records the Windows program: the scenario `desktop.json` operates it in `background` mode on
   a separate desktop that is never shown. Recording `desktop`.
3. Loads the same data again, starts the browser variant on a free port of 127.0.0.1 and
   records the scenario `web.json` in a browser without a window. Recording `web`.
4. `correlate` for both, the handbook through `manual`, and `spec` from the recording `desktop`.
5. `verify db desktop web`.
6. `video desktop web`: the player page.

Every line of the output starts with `ok` or `FAIL`; the exit code is 0 when everything was as
expected and 2 when not. Nothing moves on the screen, and mouse and keyboard stay free.

| Option | Meaning |
|---|---|
| `--flaw` | starts the browser variant with its deliberate defects; the recording is named `web-flawed`, the run passes when `verify db` finds differences; specification and player page are left out |
| `--no-video` | records no video and writes no player page |
| `--visible` | operates both variants with the real mouse on the visible desktop, for a demonstration; do not touch mouse or keyboard meanwhile |

The business steps of both scenarios, each one chapter: "New customer", "Change article price",
"New invoice" (three lines), "Mark invoice paid".

## What the numbers are

Measured in the end-to-end test of the repository
(`PipelineTests.SampleEndToEndOnPlainPostgres`) on PostgreSQL 18:

| Number | Meaning |
|---|---|
| 38 actions | what the scenario did in the Windows program: clicks, filled fields, chapter marks |
| 43 statements | SQL statements the Windows program sent meanwhile |
| 4 transactions, 14 changed rows | one transaction per business step; the invoice alone changes 11 rows |
| "equal (14 changed rows agree)" | result of `verify db` for the faithful browser variant |
| 5 rows only in the original, 2 only in the rebuilt one | result with `--flaw`: three stock movements are missing, and the invoice carries tax 108.33 instead of 108.34 |

The 11 rows of the invoice are what the screen does not show: the invoice, its three lines, the
counter, the three changed stocks and the three stock movements.

## The deliberate defects

Started with `SAMPLE_FLAW=1`, the browser variant

- rounds the tax per invoice line instead of once per tax class, and
- writes no stock movements.

Both pages look the same as before. `verify db` reports both, row by row; the report is shown
in [comparison tests](verify.md).

## Running the parts by hand

How to create the database, start the Windows program and start the browser variant yourself is
described in `examples/sample-app/README.md` and `examples/sample-app/web/README.md`. Window
titles, control texts and element ids for automation are listed in
`examples/sample-app/CONTROLS.md`.

## Tests

| Test | Checks |
|---|---|
| `PipelineTests.SampleEndToEndOnPlainPostgres` | the run above, faithful and with defects |
| `node --test` in `examples/sample-app/web` | calculation cases, the HTTP interface with a database, language resources, the files taken from the web template |
| `bin\sample-desktop.exe --selftest examples\sample-app\test-vectors.json` | the calculation of the Windows program against the same cases |
