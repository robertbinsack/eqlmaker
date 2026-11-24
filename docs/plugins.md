<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](plugins.md) · [Deutsch](de/plugins.md)

# Plugins

EQLMaker can be extended in four places without changing its code:

| Type | Extends | Built in | Example in the repository |
|---|---|---|---|
| `source` | where database events come from | `postgres` and the other one listed in [event sources](sources.md) | `plugins/examples/source-jsonl-file` |
| `web-template` | the starting point that `generate` copies | `default` | `plugins/examples/web-template-minimal` |
| `clone-renderer` | the page that `clone` makes for each mask | `backdrop` | `plugins/examples/clone-renderer-outline` |
| `video-layout` | how `video` arranges the two recordings | `side-by-side` | `plugins/examples/video-layout-stacked` |

A plugin is a folder with a file `plugin.json`. EQLMaker looks for plugins in the folders listed
under `"plugins"` in `eqlmaker.json` (default: the folder `plugins` of the project). Each listed
folder may be one plugin or may contain several plugin folders.

```json
{ "plugins": ["plugins", "C:\\Shared\\eqlmaker-plugins"] }
```

Plugins of the types `source` and `clone-renderer` are separate programs. EQLMaker starts them
and talks to them over standard input and standard output with JSON, one object per line, in UTF-8
without a byte order mark, whatever the code page of the console is. Read and write the two streams
as bytes in UTF-8 rather than through the console encoding of your language; a reader should accept
a byte order mark in front of the first line.
They can be written in any language. No plugin code is loaded into the EQLMaker process.
What a plugin program writes to its error output is shown when it fails.

Every plugin has these keys:

| Key | Meaning |
|---|---|
| `type` | `source`, `web-template`, `clone-renderer` or `video-layout` |
| `name` | the name used in the configuration and on the command line; no blanks or slashes |
| `description` | one sentence for people (optional) |

## Source

```json
{ "type": "source", "name": "jsonl-file", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File source.ps1" }
```

`command` is run in the folder of the plugin. Select the source with `"source": "jsonl-file"` in
`eqlmaker.json`; a section with the same name holds its settings:

```json
{ "source": "jsonl-file", "jsonl-file": { "file": "C:\\Logs\\events.jsonl" } }
```

The program is started when a capture begins:

1. It reads one line from standard input, the capture options:
   `{"action":"start","connection":"...","roles":[...],"databases":[...],"changes":true,"logFile":"","settings":{...}}`.
   `settings` is the section named like the source.
2. As soon as it is capturing, it writes one line `{"info":{...}}` (`{"info":{}}` is enough).
   EQLMaker waits for this line before the recording counts as started, so that nothing the
   application does right afterwards is missed; without it the start fails after 60 seconds
   (`"startTimeoutSeconds"` in `plugin.json` changes that). Keys EQLMaker understands: `fields`
   (what the source delivers, free form), `server_minus_local_us` (clock of the database server
   minus the clock of this machine, in microseconds), `warnings` (list of texts), `changes`
   (`"on"` or `"off"`).
3. It writes events, one per line, in the [event format](event-format.md), at any time while it
   runs. `source` may be left out; EQLMaker fills in the plugin name.
4. When it reads the line `stop`, it writes the events that are left and ends with exit code 0.
   An exit code other than 0 fails the recording.

With `"action":"reset"` the program undoes what an aborted capture left behind on the server and
ends; it is called by `eqlmaker reset`.

## Web template

```json
{ "type": "web-template", "name": "minimal", "files": "files", "variables": ["APP_NAME", "DB_ENV"] }
```

`files` is the folder that `eqlmaker generate --template minimal` copies into the project folder
`web`. In text files, `{{APP_NAME}}` is replaced by the name of the project and `{{DB_ENV}}` by
the name of the environment variable that holds the database connection of the application.
`generate` then puts the specification into `web/spec` and the instructions into
`web/GENERATE.md`. A template decides language, framework and look of the application; it does
not have to use Node.

## Clone renderer

```json
{ "type": "clone-renderer", "name": "outline", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File render.ps1" }
```

`eqlmaker clone --renderer outline` starts the program once per mask. It reads one line:

```json
{"mask":{"id":"m-customer","title":"Customer #"},"reference":"../reference/m-customer.png","width":396,"height":249,"controls":[{"text":"Save customer","class":"...BUTTON...","x":162,"y":201,"w":110,"h":26,"enabled":true,"writes":true}],"queries":["q4"],"tables_read":["customer"],"out":"C:\\project\\clone\\public"}
```

- `reference` is the reference picture of the mask, relative to the page.
- `controls` are the controls recorded for the mask, with positions inside the picture; `writes`
  is true for a control that changed data when it was used.
- `out` is the folder for the page and anything else the renderer writes.

The program writes its files and answers with one line: `{"page":"m-customer.html"}`.
A renderer must keep the archive read-only: controls with `writes` stay visible and inoperable.

## Video layout

```json
{ "type": "video-layout", "name": "stacked", "height": 540, "filter": "[a][b]vstack=inputs=2[v]", "player": "column" }
```

- `filter` is an FFmpeg filter that makes the output `[v]` from the inputs `[a]` (first recording)
  and `[b]` (second recording). Both inputs are already scaled to `height` and have the same length.
- `player` arranges the two videos on the player page: `row` or `column`.

Use it with `eqlmaker video <A> <B> --layout stacked`.

## Checking a plugin

`eqlmaker` reports a broken `plugin.json` with the reason when the plugin is used. The examples
are covered by the tests in `tests/PipelineTests.cs` (`PluginTests`); they are a good starting
point for tests of your own plugin.
