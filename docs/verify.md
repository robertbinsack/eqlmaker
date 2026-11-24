<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](verify.md) · [Deutsch](de/verify.md)

# Comparison tests

`verify` compares the original with the rebuilt application. There are two tests: `screens`
compares pictures, `db` compares the measured database changes of two recordings. Both write a
report and end with exit code 0 when the test passed and 2 when it did not.

## verify screens

```text
eqlmaker verify screens <A> <B> [--tolerance 16] [--max-percent 0.5] [--ignore x,y,w,h;x,y,w,h] [--name <name>]
```

`<A>` is the reference, `<B>` what is checked. Both are picture files, or both are folders;
pictures in folders are paired by file name (`*.png`).

**Rules**

- Two pictures must have the same size. Pictures of different size fail.
- A pixel is different when one of its colour channels differs by more than the tolerance
  (default 16 of 255).
- A pair passes when at most `--max-percent` of the compared pixels are different (default 0.5).
- Regions given with `--ignore` are not compared. Each region is `x,y,w,h` in pixels; several
  are separated by `;`.
- The test passes when every pair passes, no picture is without partner, and there is at least
  one pair.

**Result** in `verify/<name>/` (default name `screens`):

| File | Content |
|---|---|
| `report.json` | `tolerance`, `max_percent`, `ignored`, `pairs`, `failed`, `unpaired`, `passed`, and per pair: `same_size`, `compared_pixels`, `different_pixels`, `percent`, `bounds_of_differences`, `passed` |
| `<picture>.diff.png` | for a pair with differences: the picture faded, with every different pixel in red |

Take pictures of a web page at the size of the reference with:

```text
eqlmaker web-shot <url> <file.png> --size 1280x800
```

## verify db

```text
eqlmaker verify db <A> <B> [--ignore table.column,...] [--keys table.column,...] [--references table.column=table,...] [--name <name>]
```

`<A>` is the recording of the original, `<B>` the recording of the rebuilt application. Both
must have been recorded with changed rows (`wal_level = logical`) and with the same chapter
captions, and both runs should start from the same database state.

**Rules**

1. The changed rows are compared per step. A step is a chapter; rows are matched to chapters by
   `correlate`.
2. Inside a step the rows of both recordings are compared as sets: the order does not count,
   the number does. A row is its table, its operation and its column values.
3. **Keys created during a recording** compare by their order of creation, not by value. The
   key columns are `*.id` by default: the first row inserted into `invoice` is `<new public.invoice #1>`
   on both sides, whatever number the database gave it. A column named `<table>_id` that holds
   such a key is treated the same way. Keys that existed before the recording compare literally.
4. Values that look like a time stamp (`2026-11-24 18:20:31...`) compare as "time of the run".
   The report notes each column where this happened.
5. Columns named with `--ignore` are left out.
6. Everything else compares literally, as text.
7. The result is `equal` only when no row is left over on either side, every step exists in
   both recordings, and at least one row was compared. Two recordings without changed rows are
   never equal.

**Options**

| Option | Meaning | Also in `eqlmaker.json` |
|---|---|---|
| `--ignore` | columns to leave out: `invoice.updated_at`, `*.changed` | `verify.ignoreColumns` |
| `--keys` | key columns whose new values compare by order of creation (default `*.id`) | `verify.keyColumns` |
| `--references` | columns that hold a key of another table when the name does not say so: `invoice_line.inv=invoice` | `verify.references` |
| `--name` | name of the result folder (default `db-<A>-<B>`) | |

A column pattern is `table.column`, `*.column` or `schema.table.column`.

**Result** in `verify/<name>/`: `report.json` and `report.txt`. The command prints the readable
report.

### Reading the report

```text
Database changes: desktop (A) against web-flawed (B)
RESULT: DIFFERENT (9 rows agree, 5 only in A, 2 only in B)

[ok] New customer: A 1 rows, B 1 rows

[ok] Change article price: A 1 rows, B 1 rows

[DIFF] New invoice: A 11 rows, B 8 rows
      only A: insert public.invoice customer_id=<new public.customer #1> discount=31.45 discount_percent=5.00 gross=705.79 id=<new public.invoice #1> invoice_date=2026-11-24 net=628.90 number=INV-2026-00004 status=open tax=108.34
      only A: insert public.stock_movement article_id=3 id=<new public.stock_movement #3> invoice_id=<new public.invoice #1> quantity=-3 reason=invoice
      only A: insert public.stock_movement article_id=6 id=<new public.stock_movement #1> invoice_id=<new public.invoice #1> quantity=-14 reason=invoice
      only A: insert public.stock_movement article_id=7 id=<new public.stock_movement #2> invoice_id=<new public.invoice #1> quantity=-7 reason=invoice
      only B: insert public.invoice customer_id=<new public.customer #1> discount=31.45 discount_percent=5.00 gross=705.78 id=<new public.invoice #1> invoice_date=2026-11-24 net=628.90 number=INV-2026-00004 status=open tax=108.33

[DIFF] Mark invoice paid: A 1 rows, B 1 rows
      only A: update public.invoice ... gross=705.79 ... status=paid tax=108.34
      only B: update public.invoice ... gross=705.78 ... status=paid tax=108.33
```

- `[ok]` and `[DIFF]` mark each step.
- `only A` is a change the original made and the rebuilt application did not; `only B` the
  reverse. A value that differs shows as one row on each side.
- `step missing in A` or `B` means the chapter caption exists on one side only.
- `<new public.invoice #1>` is a key created during the recording (rule 3); `<time of the run>` is a
  time stamp (rule 4).
- `note:` lines at the end name the columns compared as time of the run, and say when neither
  recording has changed rows.

The report above is the one of the sample with its deliberate defects, shortened in the last two lines.

## The deliberate defects of the sample

The browser variant of the sample application can be started with two built-in defects
(`SAMPLE_FLAW=1`): it rounds the tax per line instead of once per tax class, and it writes no
stock movements. Neither is visible on a screen.

```text
eqlmaker sample --flaw
```

`verify db` finds both: 5 rows only in the original (three stock movements, and the invoice
with tax 108.34 when it is written and when it is marked paid), 2 only in the rebuilt
application (the same invoice with tax 108.33). The run
counts as passed when the differences are found. Without `--flaw` the result is "equal
(14 changed rows agree)". See [sample application](sample-app.md).

## Limits

- `verify db` compares what was recorded. A step nobody recorded is not compared.
- The assignment of rows to steps follows `correlate`; without transaction ids in the server
  log it is an approximation by time and table. See [pipeline](pipeline.md).
- Rule 3 assumes that both applications create rows in the same order within a table.
- `verify screens` compares pixels. Other fonts, another window frame or other data give
  differences; use `--ignore` for regions that differ legitimately and say so in your report.
