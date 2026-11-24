<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](cli.md) · [Deutsch](de/cli.md)

# Command reference

This page lists every command of `eqlmaker` with every option. What the steps do is described
in [pipeline](pipeline.md); each pipeline command is also an [MCP tool](mcp.md) with the same
result.

```text
eqlmaker <command> [arguments] [options]
eqlmaker help
```

## Common rules

| Option | Meaning |
|---|---|
| `--project <folder>` | the project folder (default: the current folder) |
| `--json` | print the result as JSON |
| `--lang en\|de` | language of the texts, see [configuration](configuration.md) |

- An option with a value is written `--name value` or `--name=value`; an option without a value
  is a switch. Everything after `--` is taken as arguments.
- A recording is named by its name inside the project folder `recordings`, or by the path of
  its folder.
- Without `--json` a result is printed as lines of `key value`.

| Exit code | Meaning |
|---|---|
| 0 | done |
| 1 | error; the reason is the line that starts with `eqlmaker:` |
| 2 | the step ran and its result is "not passed": `verify`, `sample`, `selftest` |

## Project

| Command | Does |
|---|---|
| `eqlmaker init [folder]` | creates the folder (default: the current one) with a starting `eqlmaker.json`; refuses when the file exists |
| `eqlmaker version` | prints the version |
| `eqlmaker help` | prints the short overview of the commands |

## Record

```text
eqlmaker record --scenario <file.json> [--name <n>] [--input real|background] [--no-video] [--capture-database <db>] [--quiet]
eqlmaker record --start [--name <n>] [--no-video] [--no-capture] [--region <r>] [--fps <n>]
eqlmaker record --stop
eqlmaker reset
```

| Option | Meaning |
|---|---|
| `--scenario <file>` | run the scenario file and record it; see [remote control](remote-control.md) |
| `--start` | start a recording in the background while a person works; returns when the capture runs |
| `--stop` | end the recording started with `--start` and write the recording folder |
| `--name <n>` | name of the recording (default: the name in the scenario, or `recording-<date>-<time>`) |
| `--input real\|background` | input mode, overrides scenario and configuration |
| `--no-video` | record no video |
| `--no-capture` | with `--start`: capture no database events |
| `--region <r>` | with `--start`: part of the screen for the video: `workarea` (default), `screen`, `x,y,w,h` or a window title pattern |
| `--fps <n>` | with `--start`: frames per second (default 15) |
| `--capture-database <db>` | capture exactly this database, whatever the configuration names |
| `--quiet` | with `--scenario`: do not print the steps and the result |

`eqlmaker reset` undoes what an aborted capture left on the database server: the logging
settings of roles and databases, and replication slots. See [event sources](sources.md).

## Time line, specification, manual

| Command | Options | Does |
|---|---|---|
| `eqlmaker correlate <recording>` | | writes `timeline.json` into the recording folder |
| `eqlmaker spec [recording ...]` | | writes `spec/spec.json` and `spec/spec.md` from the named recordings (default: all) |
| `eqlmaker manual <file>` | `--name <n>`: name in the project (default: file name) | takes a manual over into `manual/`; see [manual](manual.md) |

## Guided steps

See [guided steps](guided-steps.md).

| Command | Options | Does |
|---|---|---|
| `eqlmaker generate` | `--template <name>` (default `default`), `--force` | prepares `web/` and `GENERATE.md` |
| `eqlmaker clone` | `--renderer <name>` (default `backdrop`), `--force` | prepares `clone/` and `CLONE.md` |
| `eqlmaker enrich [<A> <B>]` | `--findings <name>` | prepares `enrich/<name>/` and `ENRICH.md`; with two recordings also the database comparison before |
| `eqlmaker enrich --decide <finding>` | `--status adopted\|rejected\|open`, `--note "<text>"`, `--files a,b`, `--findings <name>` | records the decision for one finding; status and note are required |
| `eqlmaker enrich --finish [<A> <B>]` | `--findings <name>` | writes the report; with two recordings also the database comparison after |
| `eqlmaker prompt <name>` | `--findings <name>` for `enrich` | prints the instructions of `generate`, `clone` or `enrich` |

## Video and comparison tests

```text
eqlmaker video <A> <B> [--layout <name>] [--name <n>] [--no-video-file]
eqlmaker verify screens <A> <B> [--tolerance 16] [--max-percent 0.5] [--ignore x,y,w,h;x,y,w,h] [--name <n>]
eqlmaker verify db <A> <B> [--ignore table.column,...] [--keys table.column,...] [--references table.column=table,...] [--name <n>]
eqlmaker web-shot <url> <file.png> [--size 1280x800] [--wait 300]
```

| Option | Meaning |
|---|---|
| `--layout <name>` | `video`: `side-by-side` (default) or a video-layout plugin |
| `--no-video-file` | `video`: only the player page, no combined file |
| `--name <n>` | name of the result folder in `video/` or `verify/` |
| `--tolerance <n>` | `verify screens`: difference per colour channel that still counts as equal (default 16) |
| `--max-percent <n>` | `verify screens`: share of differing pixels that still passes (default 0.5) |
| `--ignore` | `verify screens`: regions not compared; `verify db`: columns left out |
| `--keys` | `verify db`: key columns compared by order of creation (default `*.id`) |
| `--references` | `verify db`: columns that hold a key of another table |
| `--size <w>x<h>` | `web-shot`: size of the picture (default `1280x800`) |
| `--wait <ms>` | `web-shot`: pause after the page is loaded (default 300) |

The rules are in [comparison tests](verify.md).

## Narrated video

```text
eqlmaker video-import <file.mp4> [options]
eqlmaker clock
eqlmaker import-log <file> [--name <n>] [--prefix "%m [%p] "] [--utc-offset-minutes <n>] [--database <db>] [--application <name>]
```

| Option of `video-import` | Meaning |
|---|---|
| `--recordings a,b` | recordings whose events belong to the video (default: all of the project) |
| `--transcript <file>` | use this transcript (WebVTT, SRT or JSON) instead of recognising the speech |
| `--language <code>` | language of the speech (default: detected) |
| `--no-transcript` | skip the sound track |
| `--clock-offset-ms <n>` | clock of the database server minus clock of the recording machine |
| `--estimate-offset` | estimate that difference from the data even when a measured value exists |
| `--max-shift <seconds>` | largest clock difference considered when it is estimated (default 900) |
| `--sample-fps <n>` | frames per second read for the time mapping (default 2) |
| `--max-keyframes <n>` | upper limit of key frames kept (default 120) |
| `--attach-seconds <n>` | how far a sentence or key frame may be from an operation to be tied to it (default 8) |
| `--name <n>` | name of the result folder (default: file name of the video) |

| Option of `import-log` | Meaning |
|---|---|
| `--name <n>` | name of the recording (default: `log-<file name>`) |
| `--prefix` | `log_line_prefix` of the server (default `%m [%p] `) |
| `--utc-offset-minutes <n>` | UTC offset of the times in the log (default: this machine) |
| `--database <db>` | take only statements of this database (needs `%d` in the prefix) |
| `--application <name>` | take only statements of this application (needs `%a` in the prefix) |

`eqlmaker clock` prints the clock of the database server minus the clock of this machine. See
[video import](video-import.md). The time stamp overlay is its own program with its own
options, see [time stamp overlay](timestamp-overlay.md).

## Remote control

| Command | Does |
|---|---|
| `eqlmaker windows` | lists the visible top-level windows |
| `eqlmaker controls <title>` | lists the controls of the window whose title matches the pattern |
| `eqlmaker background-run <desktop> <command...>` | runs a command on a separate desktop that is never shown; `--timeout <seconds>` ends it after that time |

## MCP server

| Command | Does |
|---|---|
| `eqlmaker mcp` | runs the MCP server on standard input and output; see [MCP server](mcp.md) |

## Sample and self test

| Command | Options | Does |
|---|---|---|
| `eqlmaker sample` | `--flaw`: start the browser variant with its deliberate defects; `--no-video`: record no video; `--visible`: operate both variants with the real mouse on the visible desktop | runs the whole pipeline with the sample application; see [sample application](sample-app.md) |
| `eqlmaker selftest` | `--quick`: skip the second sample run with the deliberate defects; `--audio <file>` with `--expect "word,word"`: check the speech recognition with this audio file | checks resources, FFmpeg, Node.js, the database connection and, with `--audio`, the speech recognition, then runs the sample faithful and with the defects; used by the setup of the package |

## For translators

| Command | Does |
|---|---|
| `eqlmaker i18n-export <file>` | writes the English program texts with their keys; see [translating](translating.md) |
