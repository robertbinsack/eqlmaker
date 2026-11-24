# Sample Shop, browser variant

The five masks of the sample application in the browser: the pages Customers, Articles and
Invoices and the dialogs Customer, Article and New invoice. Same database, same behaviour and
the same rows written in the same transaction as the desktop program (see `../README.md`).

## Start

Node 18 or newer.

```
npm ci
node server.js
```

| Environment | Meaning | Default |
|---|---|---|
| `SAMPLE_DB` | connection string | `postgres://sample_app@127.0.0.1:5432/eqlmaker_sample` |
| `PGPASSWORD` | password of the database user | none |
| `PORT` | port | 8780 |
| `SAMPLE_FLAW` | `1` switches the deliberate defect on | off |

The server listens on 127.0.0.1 only and connects with the application name `sample-web`.

## Languages

English and German. Order of the choice: `?lang=en` or `?lang=de` in the address, then the
choice stored by the language switch, then the language of the browser, then English. Automated
runs open `http://127.0.0.1:8780/?lang=en`. Element ids, API field names and log output are the
same in both languages; they are listed in `../CONTROLS.md`.

The texts of the sample are in `public/i18n/app-en.json` and `public/i18n/app-de.json`.

## Deliberate defect

For demonstrating comparison tests the variant can be started with a known defect:

```
SAMPLE_FLAW=1 node server.js
```

Then it (a) rounds the tax per invoice line instead of once per tax class and (b) writes no
rows to `stock_movement`. Without the variable the behaviour is faithful to the desktop program.

## Files

Taken unchanged from the web application template (`src/templates/web` of this repository):

- `server.js`
- `lib/core.js`, `lib/entity.js`
- `public/app.js`, `public/style.css`, `public/favicon.svg`
- `public/i18n/en.json`, `public/i18n/de.json`
- `public/index.html` (the placeholder for the application name is filled in)

Own files of the sample:

- `app/config.js` - settings from the environment
- `app/entities.js` - customers and articles as entity descriptions
- `app/invoice-calc.js` - the calculation of an invoice
- `app/routes.js` - invoices: list, preview of the totals, saving, marking as paid
- `public/pages.js` - the invoice page and the dialog "New invoice"
- `public/i18n/app-en.json`, `public/i18n/app-de.json` - texts of the sample
- `test/` - tests

## Tests

```
node --test
```

Without a database: the calculation against `../test-vectors.json` (the faithful mode passes all
vectors, the defect differs in the rounding vectors), the language resources, and that the
template files are unchanged.

With `SAMPLE_DB` set (and `PGPASSWORD`), an HTTP-level test also saves invoices through the API
and checks the rows in `invoice`, `invoice_line`, `stock_movement`, the stock of the articles
and the counter. It adds test customers and invoices; rebuild the database afterwards with
`../db/setup.ps1 -Recreate`.

## API

| Route | Meaning |
|---|---|
| `GET /api/entity/customer`, `POST /api/entity/customer`, `PUT /api/entity/customer/<id>` | customers |
| `GET /api/entity/article`, `PUT /api/entity/article/<id>` | articles |
| `GET /api/invoices` | invoices |
| `POST /api/invoices/preview` | totals of `{ customer_id, lines: [{ sku, quantity }] }`, writes nothing |
| `POST /api/invoices` | saves an invoice; 409 with "Credit limit exceeded" when the limit is exceeded |
| `POST /api/invoices/<id>/paid` | marks an invoice as paid |

Errors are `{ "error": "<English text>", "code": "<key of the language resources>", "args": {} }`.
