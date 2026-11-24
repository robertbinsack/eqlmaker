<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Build the read-only 1:1 clone (archive) for project "{{PROJECT}}"

The archive shows the masks of the original exactly as they looked, filled with live data from the
database, and cannot change anything. EQLMaker has prepared the folder `clone/` of
`{{PROJECT_DIR}}`; you fill it. The picture comparison decides whether a page matches.

## What you have

- `clone/reference/<mask>.png` - the reference picture of each mask from the recording.
- `clone/public/<mask>.html` - one page per mask: the reference picture as backdrop and one
  positioned element per recorded control (`data-control`, `data-text`). Until you bind data, the
  page shows exactly the reference picture.
- `clone/masks.json` - the list of masks and pages.
- `clone/server.js` - a small server that answers queries inside a read-only transaction
  (see `clone/README.md`).
- `spec/spec.md` - which statements each mask sends and which tables it reads.

Masks:

{{MASKS}}

## Rules

1. Read-only. The server opens every transaction as `READ ONLY`; do not weaken that. No statement
   other than `SELECT`.
2. Controls that changed data in the original stay visible and are greyed out (class
   `archive-off`); EQLMaker has marked the ones it saw writing. Mark the others you recognise
   (save, delete, new, book ...).
3. Same size, same positions, same fonts as the reference picture. Replace regions of the backdrop
   by live elements only where data is shown (lists, fields); keep everything else as picture.
4. Navigation between masks works like in the original (buttons, tabs, double click on a row).
5. Use the statements of the specification for the data; do not invent other queries.

## How to work

1. Mask by mask: bind the lists and fields to the queries, cover the corresponding region of the
   backdrop, match font and spacing.
2. Take a picture of the page at the size of the reference (`web_shot`), compare with
   `verify_screens` (reference against your picture), look at the difference picture, correct.
   Data that changed since the recording differs legitimately: compare on the database state the
   recording was made on, or exclude the region with `ignore` and say so.
3. Check that nothing can be written: operate every greyed control, and record the session
   (`record_start` ... `record_stop`): the recording must contain no transaction.

When you are done, report per mask: difference in percent, regions ignored and why, and what is
not rebuilt.
