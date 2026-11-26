<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](mcp.md) · [Deutsch](de/mcp.md)

# MCP server

EQLMaker is also a server of the Model Context Protocol (MCP). An MCP client, typically an
assistant, connects to it and gets the remote control and every pipeline step as tools. This is
how the [guided steps](guided-steps.md) are done by an assistant.

## Connecting a client

The server talks over standard input and output. Tell your MCP client to start this command:

```text
eqlmaker mcp --project <folder>
```

Most clients take a configuration of this form; the place of the file depends on the client:

```json
{
  "mcpServers": {
    "eqlmaker": {
      "command": "C:\\eqlmaker\\bin\\eqlmaker.exe",
      "args": ["mcp", "--project", "C:\\Projects\\my-project"],
      "env": { "EQLMAKER_PGPASSWORD": "..." }
    }
  }
}
```

- Give the full path of `eqlmaker.exe` (in the package: `bin\eqlmaker.exe`).
- `--project` names the project folder with `eqlmaker.json`. One server works on one project.
- The password of the database connection comes from the environment or a password file, see
  [configuration](configuration.md). If your client stores its configuration in a file that
  others can read, use `passfile` in `eqlmaker.json` instead of `env`.
- `--lang de` gives the descriptions of the tools in German. Tool names and parameter names
  stay the same.

The server implements `initialize`, `tools/list`, `tools/call`, `prompts/list`, `prompts/get`
and `ping`. It offers tools and prompts, no resources. A screenshot is returned as a picture
(`image/png`).

The server operates the computer on behalf of the client: it can start programs, click and
type. Connect only clients you trust.

## Tools

### Remote control

| Tool | Does |
|---|---|
| `list_windows` | lists the visible top-level windows |
| `focus_window` | makes a window the current window; later coordinates are relative to it |
| `use_screen` | clears the current window; coordinates are screen coordinates again |
| `set_input_mode` | chooses `real` or `background` |
| `list_controls` | lists the controls of the current window with text, class and position |
| `launch` | starts a program |
| `screenshot` | picture of the current window, the screen or the web page |
| `click` | clicks at a position |
| `click_control` | clicks a control found by text and/or window class |
| `fill_control` | replaces the content of an edit control |
| `move` | moves the mouse (`real` mode) |
| `type_text` | types text into the focused control; `secret` keeps it out of the recording |
| `press_keys` | sends keys in SendKeys syntax |
| `scroll` | turns the mouse wheel |
| `wait` | waits |
| `web_open` | opens a URL in Microsoft Edge or Chrome under remote control |
| `web_click` | finds an element of the web page and clicks it |
| `web_fill` | clicks into a field of the web page and types a value |
| `web_eval` | evaluates JavaScript in the web page |
| `web_reload` | reloads the web page |

Modes, addressing and limits: [remote control](remote-control.md).

### Recording

| Tool | Does |
|---|---|
| `record_start` | starts a recording: actions, database events, optional video and reference pictures |
| `record_caption` | sets a chapter mark; one chapter per mask or business step |
| `record_stop` | stops the recording and writes the recording folder |
| `run_scenario` | runs a scenario file and records it |

Call `record_start` before `launch`: the capture applies to database sessions that start
afterwards.

### Pipeline

| Tool | Command | Does |
|---|---|---|
| `correlate` | `correlate` | builds the time line of a recording |
| `spec` | `spec` | derives the specification |
| `manual_import` | `manual` | takes a manual over |
| `read_application` | `read` | reads the files of the original application into `static/<reader>/`; `spec` takes the result over |
| `export_sql` | `export-sql` | writes the statements of a recording as the table `sql.tsv` |
| `generate` | `generate` | guided step: prepares `web/`, returns the instructions |
| `clone` | `clone` | guided step: prepares `clone/`, returns the instructions |
| `video` | `video` | two recordings on one time axis |
| `verify_db` | `verify db` | compares the measured database changes; returns the report |
| `verify_screens` | `verify screens` | compares pictures |
| `web_shot` | `web-shot` | picture of a web page at a given size |
| `video_import` | `video-import` | imports a narrated video and writes findings |
| `narrate_draft` | `narrate-draft` | prepares a narration script from the caption timeline of a video |
| `narrate` | `narrate` | lays a spoken narration (local Fish Speech) and optional music over a video |
| `enrich` | `enrich` | guided step: starts taking findings over, returns the instructions |
| `enrich_decide` | `enrich --decide` | records the decision for one finding |
| `enrich_finish` | `enrich --finish` | writes the report |
| `clock_check` | `clock` | clock of the database server minus clock of this machine |
| `import_log` | `import-log` | turns a server log written earlier into a recording |

Each tool returns the same result object as its command. The parameters are described in the
tool list the server sends; they correspond to the options in the
[command reference](cli.md), with `_` in place of `-`.

`generate`, `clone` and `enrich` do not build anything. They prepare a workspace and return the
instructions; the client does the work, and `verify_db` or `verify_screens` checks it.

## Prompts

| Prompt | Argument | Content |
|---|---|---|
| `generate` | | instructions for building the web application from the specification |
| `clone` | | instructions for building the read-only 1:1 archive |
| `enrich` | `findings` (optional) | instructions for taking the findings of a narrated video over |

A prompt is the same text as the file `GENERATE.md`, `CLONE.md` or `ENRICH.md` in the
workspace, filled with the steps, masks and findings of the project.

## A typical session

1. `record_start`, `launch`, `focus_window`, then operate the original with `screenshot`,
   `list_controls`, `click_control`, `fill_control`; `record_caption` before each business
   step; `record_stop`.
2. `correlate`, `spec`.
3. `generate`, then build the application in `web/` as the instructions say.
4. Record the same steps in the new application (`web_open`, `web_click`, `web_fill`, with the
   same captions).
5. `verify_db` with both recordings; fix what differs; repeat from step 4.

How it is tested: `McpTests.ProtocolToolsAndPrompts` starts the server and checks the protocol
answers, the tool list and the prompts.
