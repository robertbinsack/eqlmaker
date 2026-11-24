<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Build the web application for project "{{PROJECT}}"

You are connected to EQLMaker as an MCP client (or you are a person following this by hand).
EQLMaker has recorded the original application and derived a specification. It does **not** write
the new application: you do, in the folder `web/` of the project `{{PROJECT_DIR}}`. Afterwards the
comparison tests decide whether the result behaves like the original.

## What you have

- `web/` - the web template (server, generic list/edit pages, style). Read `web/README.md` first.
- `web/spec/spec.md` and `web/spec/spec.json` - what was observed: {{STEP_COUNT}} steps, {{MASK_COUNT}} masks,
  every statement shape with example parameters, every table written with the columns seen.
- `web/spec/manual-*.txt` - the manual of the original, when one was handed over. The specification
  names the chapters that belong to a mask.
- `recordings/<name>/` - the recordings themselves: `viewer.html`, reference pictures in `shots/`,
  `timeline.json` (which action caused which statements and row changes).

Steps recorded:

{{STEPS}}
Masks recorded:

{{MASKS}}
Open points of the specification:

{{OPEN_POINTS}}

## Rules

1. Rebuild every step of the list above. For each step the new application must change the same
   tables, rows and values as the original did (`timeline.json` shows them row by row).
2. The specification contains only what was operated during a recording. Do not invent functions.
   Where behaviour is unclear (a calculation whose rule is not visible, a branch that was never
   taken), write it into `web/OPEN-POINTS.md` instead of guessing silently. Rounding, numbering,
   discounts and side effects on other tables are typical hidden logic: derive them from the
   recorded values, and ask for another recording when one example is not enough.
3. Keep the data model. The application works on the existing database; do not change the schema.
4. Use parameterised statements only. Keep one database transaction where the original used one.
5. Give every element that a test must operate a stable `id`.
6. Do not copy texts, pictures or logos of the original beyond field labels needed to operate it.

## How to work

1. Read the specification and look at the reference pictures of each mask.
2. Describe each table as an entity (`web/app/entities.js`) for the generic list/edit pages; add
   custom routes and pages for steps with their own logic.
3. Start the application and operate it yourself with the remote-control tools (`web_open`,
   `web_click`, `web_fill`, `screenshot`).
4. Prove it: reset the database to the state the original recording started from, record the same
   steps in the new application with the same chapter captions (`record_start`, `record_caption`,
   ..., `record_stop`, or a scenario file), then run `verify_db` with the original recording and
   yours. Fix what differs and repeat until the report says "equal", or list the remaining
   differences with their reason in `web/OPEN-POINTS.md`.

When you are done, report: which steps are rebuilt, the result of `verify_db` per step, and the
open points.
