<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](guided-steps.md) · [Deutsch](de/guided-steps.md)

# Guided steps: generate, clone, enrich

Three steps of the pipeline are guided steps. EQLMaker does not write the new application in
them. It prepares a workspace, writes the instructions, records decisions and checks the result
with comparison tests. The work itself is done by an assistant that is connected as MCP client,
or by a person who follows the same instructions.

| | EQLMaker prepares | The MCP client or person does | Checked by |
|---|---|---|---|
| `generate` | folder `web/` from a web template, the specification next to it, `GENERATE.md` | builds the web application | `verify db` |
| `clone` | folder `clone/` with a reference picture and a scaffold page per mask, a read-only server, `CLONE.md` | fills the pages with live data | `verify screens` |
| `enrich` | folder `enrich/<name>/` with the state before and `ENRICH.md` listing every finding | decides every finding and changes the web application | the report of `enrich --finish`, `verify db` |

The instructions are the same text in three places: the file in the workspace, the MCP prompt
of the same name, and `eqlmaker prompt generate` (or `clone`, `enrich`) on the command line.

All three need a specification: run `spec` first.

## generate

```text
eqlmaker generate [--template <name>] [--force]
```

**What EQLMaker prepares**

- `web/`: a copy of the web template. Built in is `default`: a Node.js server without
  framework, the driver `pg` as only dependency, list pages with edit dialogs rendered from
  plain entity descriptions, English and German resources. Other templates are
  [plugins](plugins.md). The placeholders `{{APP_NAME}}` (name of the project folder) and
  `{{DB_ENV}}` (`APP_DB`, the environment variable with the database connection) are filled in.
- `web/spec/spec.json`, `web/spec/spec.md`, and `web/spec/manual-<name>.txt` for each manual.
- `web/GENERATE.md`: the instructions, with the list of steps and masks and the open points of
  the specification.

An existing `web/` is refused; `--force` copies the template over it and leaves files with
other names alone.

**What the MCP client or person does**

Reads the specification and the reference pictures, describes the tables as entities, adds
routes and pages for steps with their own logic, starts the application and operates it. The
rules of `GENERATE.md` in short: rebuild every recorded step; invent nothing; write unclear
behaviour into `web/OPEN-POINTS.md` instead of guessing; keep the data model; parameterised
statements only; one transaction where the original used one; a stable `id` for every element
a test must operate.

**How it is checked**

Reset the database to the state the original recording started from. Record the same steps in
the new application with the same chapter captions. Then:

```text
eqlmaker verify db <original> <web>
```

The report says per step whether the rebuilt application changed the same rows with the same
values. See [comparison tests](verify.md). What still differs is fixed, or listed with its
reason in `web/OPEN-POINTS.md`.

## clone

```text
eqlmaker clone [--renderer <name>] [--force]
```

The clone is the read-only 1:1 archive: the masks of the original as they looked, filled with
live data, without any way to change data.

**What EQLMaker prepares**

- `clone/reference/<mask>.png`: the reference picture of each mask, from the recording.
- `clone/public/<mask>.html`: one page per mask, made by the clone renderer. The built-in
  renderer `backdrop` puts the reference picture behind the page and one positioned element per
  recorded control on top. Until data is bound, the page shows exactly the reference picture.
  Controls that changed data when they were used in the recording are greyed out and cannot be
  operated. Other renderers are [plugins](plugins.md).
- `clone/masks.json`: the list of masks and pages.
- `clone/server.js`, `clone/queries.js`: a small server that runs every query in a `READ ONLY`
  transaction and accepts only single statements that start with `SELECT` or `WITH`.
- `clone/CLONE.md`: the instructions.

A mask without a reference picture gets no page; `masks.json` says so.

**What the MCP client or person does**

Mask by mask: binds lists and fields to the statements of the specification, covers the
matching region of the backdrop, matches font and spacing, makes the navigation between masks
work, and greys out further writing controls that the recording did not exercise.

**How it is checked**

```text
eqlmaker web-shot <url of the page> shots\<mask>.png --size <width>x<height>
eqlmaker verify screens clone\reference shots
```

Data that changed since the recording differs legitimately: compare on the database state of
the recording, or leave the region out with `--ignore` and say so. To check that nothing can be
written, operate every greyed control while a recording runs: the recording must contain no
transaction.

## enrich

`enrich` takes the findings of a narrated video over into the web application. Findings come
from [`video-import`](video-import.md): per operation what was said, what was seen and what the
database did that the specification does not contain. They are candidates. EQLMaker does not
judge them.

```text
eqlmaker enrich [<A> <B>] [--findings <name>]
eqlmaker enrich --decide <finding> --status adopted|rejected|open --note "..." [--files a,b] [--findings <name>]
eqlmaker enrich --finish [<A> <B>] [--findings <name>]
```

**What EQLMaker prepares**

`eqlmaker enrich` writes `enrich/<name>/`:

- `state.json`: a checksum of every file of `web/` before the work, the decisions, and, when
  two recordings `<A>` (original) and `<B>` (web application) are named, the result of
  `verify db` before.
- `ENRICH.md`: the instructions with every finding and its id (`f1`, `f2`, ...).

`--findings <name>` is needed only when the project has more than one set of findings.

**What the MCP client or person does**

Decides every finding and records the decision with a reason:

| Status | Meaning | The note says |
|---|---|---|
| `adopted` | the web application must have it and did not: `web/` was changed | what was changed and where |
| `rejected` | not relevant for the application | why |
| `open` | relevant, but cannot be built from what is known | what is missing |

A spoken sentence is evidence of intent, not proof of behaviour. Where it contradicts the
recorded database changes, the recorded behaviour stays and the finding is `open`.

**The report**

After the changes, record the steps in the changed application again under a new name and end
the step:

```text
eqlmaker enrich --finish <original> <new recording>
```

It writes `enrich/<name>/report.json` and `report.md`:

- every finding as adopted, rejected or open with its reason; a finding without a decision is
  reported as open with "no decision was recorded";
- the files of `web/` that were changed, added or removed since the start;
- the database comparison before and after;
- warnings: findings marked as adopted although no file changed; a comparison that was equal
  before and differs now; a comparison after that used the same recording as before.

## What guided steps cannot do

- The quality of the rebuilt application is the quality of the work of the assistant or person.
  EQLMaker shows where the result differs from the original on what was recorded; it does not
  repair it.
- Logic that no recording exercised is not in the specification and not in the comparison.
- The web template makes no accessibility claim.
