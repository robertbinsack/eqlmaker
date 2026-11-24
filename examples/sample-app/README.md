# Sample Shop

A deliberately small business application in the style of an older in-house program. It is the
demonstration object for recording an application and rebuilding it as a web application, and it
exists twice with the same behaviour:

- `desktop/` - a Windows program (Windows Forms), built as `bin\sample-desktop.exe`. It plays the
  original and is English only.
- `web/` - the same five masks in the browser, built on the web application template of this
  repository, in English and German.

Both work on the same database (`db/`). The data is invented.

## What the screens do not tell

The point of the sample is that the rules of saving an invoice are nowhere on the screen. When
an invoice is saved, in one transaction:

1. Every line gets `line_net = quantity * unit price of the article at that moment`; `net` is the
   sum of the lines.
2. Discount by customer group: group A gets 5 % from a net of 500, group B gets 3 % from a net
   of 1000, group C gets none. The discount is rounded half up to cents.
3. Tax is calculated per tax class on the discounted amount of that class (the class net minus
   the rounded discount of the class net): 19 % for class S, 7 % for class R. Each class amount
   is rounded half up to cents once, then the amounts are added. `gross = net - discount + tax`.
4. Credit limit: the gross amounts of the open invoices of the customer plus the new gross
   amount must not be higher than the credit limit. Otherwise nothing is written and the dialog
   says "Credit limit exceeded".
5. The counter `invoice` is incremented and the number becomes `INV-<year>-<5 digits>`.
6. For every line the stock of the article is decreased and a row with the negative quantity
   and the reason `invoice` is written to `stock_movement`.

"Mark paid" sets the status of an invoice to `paid`; paid invoices no longer count against the
credit limit.

Steps 1 to 3 are one small pure function per language (`desktop/InvoiceCalc.cs`,
`web/app/invoice-calc.js`). Both are tested against `test-vectors.json`.

## Database

PostgreSQL. `db/setup.ps1` creates the role `sample_app`, the database `eqlmaker_sample`, the
tables (`db/schema.sql`) and the data (`db/seed.sql`). Passwords are never written to a file:

```powershell
$env:PGPASSWORD = '<password of the admin user>'
$env:SAMPLE_APP_PASSWORD = '<password for sample_app>'
.\db\setup.ps1 -Psql 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -AdminPort 5432 -Recreate
```

With `-Recreate` the database is dropped and built again, so every run ends in the same state.

The script needs a `psql.exe` (15 or later). You do not need it to try the sample:
`eqlmaker sample` creates role and database itself, without psql. The PostgreSQL that the setup
of the release package installs comes without psql unless it is run with `-with-psql`.
Without it an existing database is left unchanged.

## Desktop program

Built by `build.ps1` in the root of the repository.

```powershell
$env:PGPASSWORD = '<password of sample_app>'
.\bin\sample-desktop.exe --connection "host=127.0.0.1 port=5432 user=sample_app dbname=eqlmaker_sample"
```

The password comes from `EQLMAKER_PGPASSWORD`, `PGPASSWORD` or the password file of PostgreSQL.
The program uses one database connection with the application name `sample-desktop`.

Self test of the calculation (no window, no database):

```powershell
.\bin\sample-desktop.exe --selftest examples\sample-app\test-vectors.json
```

It writes `selftest-result.json` next to the vectors file
(`{"passed": n, "failed": n, "failures": [...]}`) and ends with exit code 0 or 1.

## Browser variant

See `web/README.md`.

## Operating both by a tool

Window titles, control texts, positions and element ids are listed in `CONTROLS.md`.
