<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](configuration.md) · [Deutsch](de/configuration.md)

# Configuration

A project is a folder with the file `eqlmaker.json` and one sub-folder per pipeline step. This
page lists every key of that file, where passwords come from, where external tools are looked
for, and how the language is chosen.

## The project folder

`eqlmaker init [folder]` creates the folder and a starting `eqlmaker.json`. Every command works
in the current folder, or in the folder named with `--project <folder>`. The steps create these
sub-folders:

| Folder | Written by |
|---|---|
| `recordings/<name>/` | `record`, `import-log` |
| `manual/` | `manual` |
| `spec/` | `spec` |
| `web/` | `generate` |
| `clone/` | `clone` |
| `video/<name>/` | `video` |
| `verify/<name>/` | `verify` |
| `findings/<name>/` | `video-import` |
| `enrich/<name>/` | `enrich` |
| `.eqlmaker/` | internal state: a running recording, the browser profile |

## eqlmaker.json

```json
{
  "source": "postgres",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "passfile": "",
  "capture": { "roles": [], "databases": ["appdb"], "changes": true, "logFile": "" },
  "eqlserver": { "statsPort": 0 },
  "input": "real",
  "lang": "en",
  "tools": { "ffmpeg": "", "transcribe": "", "transcribeModel": "", "browser": "", "node": "" },
  "plugins": ["plugins"],
  "recordings": "recordings",
  "verify": { "ignoreColumns": [], "keyColumns": ["*.id"], "references": [] }
}
```

| Key | Meaning | Default |
|---|---|---|
| `source` | event source: `postgres`, `eqlserver`, or the name of a source plugin. See [event sources](sources.md). | `postgres` |
| `connection` | connection to the database server, as keywords (`host=... port=... user=... dbname=...`) or as a `postgres://` address. The user must be a superuser. `sslmode=require`, `verify-ca` or `verify-full` switch TLS on. | – |
| `passfile` | a password file in the pgpass format of PostgreSQL; a relative path is relative to the project folder | – |
| `capture.roles` | login roles whose statements are captured | none |
| `capture.databases` | databases whose statements and changed rows are captured. Name roles, databases or both. | none |
| `capture.changes` | capture changed rows (needs `wal_level = logical` on the server) | `true` |
| `capture.logFile` | path of the server log, when the server does not write it through the logging collector | – |
| `eqlserver.statsPort` | only for the source `eqlserver`: the statistics port for the statement trace; 0 = not used. A section named like the source holds the settings of that source; a source plugin gets its section the same way. | 0 |
| `input` | how programs are operated: `real` or `background`. See [remote control](remote-control.md). | `real` |
| `lang` | language of the program texts: `en` or `de` | see below |
| `tools.ffmpeg` | path of `ffmpeg.exe` | see below |
| `tools.transcribe` | path of the speech recogniser `whisper-cli.exe` | see below |
| `tools.transcribeModel` | path of the speech model | the model next to the recogniser |
| `tools.browser` | path of `msedge.exe` or `chrome.exe` | the usual installation folders |
| `tools.node` | path of `node.exe` | see below |
| `plugins` | folders with plugins, relative to the project folder or absolute. See [plugins](plugins.md). | the folder `plugins` of the project, when it exists |
| `recordings` | name of the folder for recordings | `recordings` |
| `verify.ignoreColumns` | columns that `verify db` leaves out | none |
| `verify.keyColumns` | key columns that `verify db` compares by order of creation | `*.id` |
| `verify.references` | columns that hold a key of another table, as `table.column=table` | none |

The options of `verify db` on the command line replace the values of `verify`. See
[comparison tests](verify.md).

## Passwords

Never write a password into `eqlmaker.json`. EQLMaker takes the password of the database
connection from the first of these that has one:

1. `password=` in the connection string (possible, not recommended);
2. the environment variable `EQLMAKER_PGPASSWORD`;
3. the file named by `passfile`;
4. the environment variable `PGPASSWORD`;
5. the password file of the user: the file named by `PGPASSFILE`, else
   `%APPDATA%\postgresql\pgpass.conf`.

`EQLMAKER_PGPASSWORD` is read once and then removed from the environment of the EQLMaker
process. A program that EQLMaker starts afterwards, such as the application that is recorded,
does not inherit it.

A password file has one line per server:

```text
127.0.0.1:5432:*:postgres:<password>
```

The Windows package writes such a file for its portable PostgreSQL (`data\pgpass.conf`) and
names it in the `eqlmaker.json` of its sample project.

## Where tools are looked for

EQLMaker itself needs no other program for `record`, `correlate`, `spec`, `manual` and
`verify`. FFmpeg is needed for `video-import` and for the combined file of `video`, the speech
recogniser for the transcript in `video-import`, Node.js for the web applications, a browser
for web pages.

For `ffmpeg`, `transcribe` and `node` the first hit counts:

1. `tools.<name>` in `eqlmaker.json`;
2. the environment variable `EQLMAKER_FFMPEG`, `EQLMAKER_TRANSCRIBE` or `EQLMAKER_NODE`;
3. the package folder: `tools\ffmpeg\bin\ffmpeg.exe`, `tools\whisper\whisper-cli.exe`,
   `tools\node\node.exe` next to the folder `bin`;
4. the `PATH` (not for the speech recogniser: without one, `video-import` works with
   `--transcript <file>` or `--no-transcript`).

The speech model is `tools.transcribeModel`, else the file named in `model.txt` next to the
recogniser, else the first `models\ggml-*.bin` there.

The browser is `tools.browser`, else Microsoft Edge, else Chrome in their usual folders.

## Language

The language of the program texts (help, messages, descriptions of the MCP tools) is the first
of: the option `--lang`, the key `lang` of `eqlmaker.json`, the environment variable
`EQLMAKER_LANG`, the language of Windows. English is used when there is no translation for the
language. Available: `en`, `de`.

File formats, identifiers, tool names, option names and the error messages of the engine stay
English in every language. See [translating](translating.md).

## Using EQLServer in place of PostgreSQL

The configuration stays the same; only `source`, `capture.logFile` and the section `eqlserver`
change. See [event sources](sources.md).
