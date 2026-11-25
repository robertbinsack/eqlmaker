<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](powerbuilder.md) · [Deutsch](de/powerbuilder.md)

# PowerBuilder libraries

The reader `powerbuilder` is built into EQLMaker. It reads the compiled libraries (`.pbd`, `.pbl`)
of a PowerBuilder 5 to 9 application and lists what is in them: menus, windows, DataWindow column
labels, message texts, every SQL statement and which object uses which. This is what a recording
cannot show: the functions that nobody used while it ran, the error messages, the statements of
branches that never ran.

The libraries are only read. No PowerBuilder installation and no source code are needed; the
compiled files that are delivered with the application are enough.

## Use

```text
eqlmaker read C:\App
eqlmaker read C:\App\app.pbd C:\App\app_uo.pbd --bundle w_customer,w_invoice
```

| Option | Meaning |
|---|---|
| `<files or folders>` | `.pbd` and `.pbl` files, or folders (all of them in the folder, not the sub folders). Give the libraries in the order of the library list of the application: for a name that occurs in several libraries, the first one wins. |
| `--reader powerbuilder` | name the reader; without it the file extension decides |
| `--out <folder>` | output folder (default `static/powerbuilder` in the project) |
| `--bundle <objects>` | start objects (window, function, menu): one Markdown file each with everything the object uses |
| `--max-depth <n>` | levels of references followed in a bundle (default 3) |
| `--exclude <regex>` | objects that are not followed in bundles |
| `--no-raw` | do not write the raw data of the objects |

The MCP tool is `read_application` (see [MCP server](mcp.md)).

## Result

| File | Content |
|---|---|
| `reader.json` | the result in the format that every reader writes, see [plugins](plugins.md); `spec` reads it |
| `summary.md` | libraries with their PowerBuilder version, objects and SQL statements per object kind, the items of every menu |
| `sql.tsv` | every SQL statement with its object; a DataWindow `PBSELECT` also in a readable form (`FROM ... COLS ... JOIN ... WHERE ... ARGS ... ORDER ...`) |
| `strings/<object>.<kind>.txt` | all readable strings of the object in their order |
| `objects/<library>/` | raw data of every entry and `_index.tsv` (name, size, compile time) |
| `bundles/<object>.md` | with `--bundle`: the object and the objects it uses, with SQL, labels, texts and references |

Object kinds: `win` window, `dwo` DataWindow, `fun` global function, `men` menu, `udo` user object,
`apl` application, `str` structure, `prj` project; in a `.pbl` also the source entries (`sr*`).

How the parts are recognised:

- **SQL:** strings that begin with `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `DECLARE`, `PBSELECT`,
  `CREATE`, `ALTER`, `DROP`, `CALL` or `EXECUTE`: embedded SQL of scripts and DataWindow sources.
  The tables are taken from them.
- **References:** identifiers in the strings of an object that are names of other objects.
- **Texts:** strings of at least two words: messages, window titles, help texts.
- **Menu items:** strings of a menu with an accelerator (`&Customers`); a tab ends the caption
  (`&Help topics<tab>F1`).
- **DataWindow labels:** the header text objects `<column>_t` and their text.
- **Bundles:** objects that many others use (2 % of all objects, at least 10: error handling, base
  classes) are named but not expanded, so that a bundle stays with the objects of one function.

Strings are read as Windows-1252 and written as UTF-8.

## In the specification

When `static/<reader>/reader.json` exists, `eqlmaker spec` adds a part `static` to `spec.json` and
a section "Static analysis" to `spec.md`:

- the menus with their items,
- the windows with their DataWindows, the column labels and the tables of their SQL,
- the tables of the SQL of the application, each marked as touched or not touched by a recording.

Tables that no recording touched become an open point of the specification. They tell you which
functions to operate and record next, or which parts of the rebuild have to come from the
libraries instead of from a recording. The windows are not matched to the masks of the
recordings: the title of a window in the library is not what the screen shows.

## Limits

- PowerBuilder 5 to 9 (ANSI libraries, header `HDR*PowerBuilder`, version `0500` to `0900`).
  PowerBuilder 10 and later store Unicode libraries; they are recognised and refused with a message.
- The reader finds strings. It does not decompile scripts and does not tell what a script does with
  a statement. Window titles and the positions of controls are not read; for the look of a
  screen use the recording.
- References are found by name. A name that is built at run time is not found.
- The raw data of the objects can contain texts of the application, among them names and
  addresses that were typed into a script. Keep the result folder as carefully as the
  libraries; `--no-raw` leaves out the raw data, but not the strings and the SQL.

## Tests

The tests build a synthetic library of PowerBuilder 6 layout themselves (objects over several data
blocks, SQL, `PBSELECT`, labels, a menu, Windows-1252 text) and check the result, the refusal of
Unicode libraries, the order of libraries and the part `static` of the specification:
`ReaderTests.PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences` and the tests next to it.
No library of a real application is part of the repository, and none was used in these tests.
