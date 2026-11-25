<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](pipeline.md) · [Deutsch](de/pipeline.md)

# Pipeline

EQLMaker works in steps. Each step reads files of the project folder and writes files into it;
nothing is kept anywhere else. Every step is a command and, with the same result, an MCP tool.

```text
record -> correlate -> spec -> generate / clone -> video -> verify
            manual ----^            ^---- enrich <- video-import
```

| Step | Command | Kind | Reads | Writes |
|---|---|---|---|---|
| Record | `record` | code | the running application, the database server | `recordings/<name>/` |
| Time line | `correlate <recording>` | code, fixed rules | a recording | `recordings/<name>/timeline.json` |
| Specification | `spec [recording ...]` | code, fixed rules | time lines, `manual/` | `spec/spec.json`, `spec/spec.md` |
| Manual | `manual <file>` | code | a manual | `manual/<name>.json`, `manual/<name>.txt` |
| Application files | `read <files>` | code | the files of the original application | `static/<reader>/` |
| Web application | `generate` | guided | `spec/`, `manual/`, a web template | `web/` with `GENERATE.md` |
| Archive | `clone` | guided | `spec/`, reference pictures | `clone/` with `CLONE.md` |
| Comparison video | `video <A> <B>` | code | two recordings | `video/<name>/` |
| Comparison tests | `verify screens`, `verify db` | code | pictures, or two recordings | `verify/<name>/` |
| Narrated video | `video-import <file.mp4>` | code | a video, recordings, `spec/` | `findings/<name>/` |
| Take over findings | `enrich` | guided | `findings/`, `web/` | `enrich/<name>/` |

"Code, fixed rules" means: the same input gives the same output, and no language model is
involved. "Guided" means: EQLMaker prepares and checks, somebody else builds. See
[guided steps](guided-steps.md).

## Record

A recording is made in one of three ways:

- `eqlmaker record --scenario <file.json>`: EQLMaker operates the application as the scenario
  file says and records meanwhile. See [remote control](remote-control.md).
- An MCP client calls `record_start`, operates the application with the remote-control tools,
  sets chapters with `record_caption` and ends with `record_stop`. See [MCP server](mcp.md).
- `eqlmaker record --start`, a person works, `eqlmaker record --stop`. Such a recording has
  video and database events, but no actions and no chapters.

The capture of database events applies to sessions that start after the recording started:
start the application after the recording. What the capture does on the server is described in
[event sources](sources.md).

**Chapters.** A chapter is a mark with a caption, one per mask or business step ("New
customer", "Save invoice"). Chapters become the steps of the specification, and `verify db` and
`video` pair two recordings by their chapter captions. Use the same captions in the recording
of the original and of the rebuilt application.

### The recording folder

| File | Content |
|---|---|
| `recording.json` | Manifest: `format`, `version`, `name`, `t0_us` (start in microseconds), `duration`, `input`, `video`, `fps`, `frames`, `region`, `source`, `capture` (roles, databases, whether changed rows were captured, what the source delivers, clock difference to the server, server version), `chapters`, `counts`, `warnings`, `files` |
| `actions.jsonl` | One action per line: `v`, `seq`, `ts`, `time`, `offset`, `kind` (`chapter`, `launch`, `window`, `click`, `fill`, `type`, `keys`, `scroll`, `web_open`, `web_click`, `web_fill`, `note`), and as far as they apply `window`, `control`, `class`, `x`, `y`, `text`, `shot` |
| `events.jsonl` | The database events in the [event format](event-format.md) |
| `video.mp4` | Screen video (H.264), when video was recorded |
| `sql.vtt` | The statements as subtitles of the video |
| `sql.tsv` | The statements as a table (offset, time, session, user, database, application, kind, statement), the format that tools for replaying statements read; see `eqlmaker export-sql` |
| `viewer.html` | Player page: video, actions and events side by side. Open it in a browser. |
| `shots/NNNN.png` | Reference picture of the window, taken with an action |
| `shots/NNNN.json` | The controls of the window in that picture: text, class, x, y, w, h, enabled |
| `timeline.json` | Written by `correlate` |

Read the `warnings` of `recording.json`. They say, for example, that no changed rows were
captured.

## Correlate

`eqlmaker correlate <recording>` ties what was operated to what the database did, by two fixed
rules:

1. **Events to actions.** Every event belongs to the last action that started at or before it,
   with a tolerance of 2 ms. Events before the first action are listed separately.
2. **Transactions to statements.** A transaction (the changed rows) is tied to statements by,
   in this order:
   - the same transaction id, when the server logs it (`%x` in `log_line_prefix`): exact;
   - else the session whose last writing statement before the commit names one of the changed
     tables: method `time+table`;
   - else the last statement before the commit: method `time`.

   The method is written next to every tie. For an explicit transaction block, the statements
   from `BEGIN` to `COMMIT` of that session are taken.

The SQL text is read by a lexer, not a parser: the verb, the tables after `FROM`, `JOIN`,
`INTO` and `UPDATE`, and the "shape" of the statement, which is its text with literals replaced
by `?`. Tables found this way are marked "according to SQL text"; tables of changed rows are
measured.

The result, `timeline.json`, is a tree: chapters → actions → statements → transactions → tables
and rows. The same recording always gives the same file.

Without transaction ids, the tie between a statement and its rows is an approximation by time
and table. It is reliable while one session writes at a time; with several sessions writing the
same tables at once it can be wrong, and the method shows where to look.

## Spec

`eqlmaker spec [recording ...]` derives the specification from one or more time lines (default:
all recordings of the project; a missing time line is built first). It contains:

- **Steps**: one per chapter, with its actions, statement shapes, writes (table, operation,
  number of rows, columns seen, one example), tables read and written, reference pictures, and
  the chapters of the manual that match.
- **Masks**: the windows that were seen, named by their title with digits neutralised
  ("Invoice 4711" and "Invoice 4712" are one mask).
- **Queries**: every statement shape with example parameters.
- **Tables**: which steps read and write each table, the operations, the columns seen.
- **Open points**: database errors that were seen, recordings without changed rows, events
  before the first action.

A chapter of the manual is linked to a step by the words they share: a word of the chapter
title counts 3, a word of the text 1; a link needs at least 3 points.

`spec/spec.json` is for programs, `spec/spec.md` for people. The specification contains only
what was operated during a recording. A function that nobody used is not in it.

## Manual

See [manual](manual.md). Run `spec` again after `manual` to get the links.

## Generate, clone, enrich

See [guided steps](guided-steps.md).

## Video

`eqlmaker video <A> <B>` puts two recordings of the same steps on one time axis. Chapters with
the same caption start together; the shorter one waits on its last picture. It writes into `video/<name>/`:

| File | Content |
|---|---|
| `player.html` | Player page with both videos, chapter list and the events of both sides |
| `pair.json` | The segments: which part of A runs next to which part of B |
| `pair.mp4` | One combined video, made with FFmpeg; left out with `--no-video-file` |

`--layout <name>` chooses the arrangement: `side-by-side` (built in) or a
[video-layout plugin](plugins.md).

## Verify

See [comparison tests](verify.md).

## Video import

See [video import](video-import.md) and [time stamp overlay](timestamp-overlay.md).

## Helpers

| Command | Use |
|---|---|
| `eqlmaker reset` | Undo what an aborted capture left on the server (settings, replication slots) |
| `eqlmaker clock` | Clock of the database server minus clock of this machine |
| `eqlmaker import-log <file>` | Turn a server log written earlier into a recording with events only |
| `eqlmaker web-shot <url> <png>` | Picture of a web page at a given size, for `verify screens` |

All commands and options: [command reference](cli.md).
