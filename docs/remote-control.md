<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](remote-control.md) · [Deutsch](de/remote-control.md)

# Remote control

EQLMaker operates Windows programs and web pages itself, so that every action is recorded with
its time, window and control. It does so from a scenario file or on behalf of an MCP client.
This page describes the two input modes, how controls are addressed, and the scenario file.

## Two input modes

| | `real` | `background` |
|---|---|---|
| Input | mouse and keyboard, as a person would use them | window messages sent to the target window |
| Pictures | from the screen | from the window itself |
| Browser | a visible browser window | a browser without a window, operated through its DevTools interface |
| Needs | a visible, unlocked desktop | no visible desktop; mouse and keyboard stay free |
| Works with | any program | programs that react to posted messages: classic Win32, Windows Forms and similar |
| Key combinations | yes (`^a`, `%{F4}`, `+{TAB}`) | combinations with Ctrl, Alt or Shift are refused |

Choose the mode with `"input"` in `eqlmaker.json` or in the scenario file, with `--input` of
`record`, or with the MCP tool `set_input_mode`. The default is `real`.

In `background` mode a program can also be started on a separate desktop that is never shown:

```text
eqlmaker background-run <desktop> <command...>
```

`eqlmaker sample` uses this: the sample program runs, is operated and recorded, and nothing
moves on the screen.

How it is tested: every end-to-end test of the repository uses `background` mode. The `real`
mode is covered by the automated tests only where no input is sent (the window list), because a
test must not take over mouse and keyboard.

## Finding windows and controls

```text
eqlmaker windows
eqlmaker controls "Sample Shop*"
```

`windows` lists the visible top-level windows with title, process and position. `controls`
lists the controls (child windows) of a window with text, window class and position.

A window is named by a title pattern with `*` and `?`, or by `pid:<n>` or `process:<name>`.
Coordinates are relative to the top-left corner of the current window.

A control is addressed by a query, in both modes:

| Key | Meaning |
|---|---|
| `text` | text of the control, with `*` and `?` |
| `class` | window class, for example `*EDIT*` or `*BUTTON*` |
| `index` | the n-th match, starting at 0 |
| `at` | `[x, y]` inside the control found, instead of its centre |

Addressing by text and class survives a moved window or another screen size; coordinates do
not. EQLMaker reads the child windows of a window. It does not read the UI Automation tree:
programs that draw their controls themselves (many newer toolkits) show no child windows and
are operated by coordinates. An external UI automation MCP server can be used next to EQLMaker
for such programs.

Elements of a web page are addressed by `css` (a CSS selector), `text` (exact visible text),
`contains` (part of the text) and `index`.

## Scenario file

A scenario is a JSON file with settings and a list of steps. EQLMaker runs the steps in order
and records meanwhile.

```json
{
  "name": "desktop",
  "input": "background",
  "video": true,
  "fps": 10,
  "closeAtEnd": true,
  "steps": [
    { "run": "${SAMPLE_BIN}/sample-desktop.exe", "args": "--connection \"${SAMPLE_CONNECTION}\"" },
    { "window": "Sample Shop", "timeout": 30000 },
    { "caption": "New customer" },
    { "control": { "text": "New customer" } },
    { "window": "Customer" },
    { "fill": { "class": "*EDIT*", "index": 0, "value": "Harbour Books" } },
    { "control": { "text": "Save customer" } }
  ]
}
```

`${NAME}` is replaced by the environment variable of that name; a missing variable stops the
scenario. Complete examples: `examples/sample-app/scenarios/desktop.json` and `web.json`.

### Settings

| Key | Meaning | Default |
|---|---|---|
| `name` | name of the recording | file name of the scenario |
| `input` | `real` or `background` | from the configuration |
| `video` | record video | `true` |
| `capture` | capture database events: `true`, `false`, or an object `{"roles": [...], "databases": [...]}` naming the logins and databases of this scenario instead of those of the configuration | `true` |
| `fps` | frames per second of the video | 15 |
| `region` | `real` mode: `workarea`, `screen`, `x,y,w,h` or a window title pattern | `workarea` |
| `shots` | reference pictures and control lists with the actions | `true` |
| `moveMs` | duration of a mouse movement in milliseconds (`real` mode) | 450 |
| `typeDelay` | pause between typed characters in milliseconds | 55 |
| `closeAtEnd` | close the programs the scenario started | `false` |
| `steps` | the steps | required |

A scenario file of the earlier recorder names them as `"sql": {"roles": [...], "databases": [...]}`;
`eqlmaker convert-scenario <file>` turns that into `capture`, everything else is the same in both.

Without a `record` step the whole scenario is recorded, and the capture starts before the first
step, so that a program started by the scenario is captured from its first statement.

### Steps

Each step is an object with one of these keys.

| Step | Value | More keys | Does |
|---|---|---|---|
| `run` | program | `args`, `cwd` | starts a program; a relative path is looked for next to the scenario file |
| `window` | title pattern | `maximize`, `timeout` (ms, default 30000) | waits for a window and makes it the current window |
| `screen` | `true` | | clears the current window; coordinates are screen coordinates again |
| `caption` | text | | sets a chapter mark |
| `control` | control query | `double` | clicks a control |
| `fill` | control query with `value` | | clicks an edit control and replaces its content |
| `click` | `[x, y]` | `button` (`left`, `right`, `middle`), `ms` | clicks at a position |
| `dclick` | `[x, y]` | `ms` | double click |
| `rclick` | `[x, y]` | `ms` | right click |
| `move` | `[x, y]` | `ms` | moves the mouse (`real` mode) |
| `type` | text | `secret` | types text into the focused control |
| `keys` | keys in SendKeys syntax | | `{ENTER}`, `{TAB}`, `{F5}`, `{DEL}`; in `real` mode also `^a`, `%{F4}`, `+{TAB}` |
| `scroll` | notches, positive is up | | turns the mouse wheel |
| `wait` | milliseconds | | waits |
| `webopen` | URL | `bounds` `[x, y, w, h]`, `maximize` | opens a URL in Microsoft Edge or Chrome under remote control |
| `webgo` | URL | | navigates the page |
| `webreload` | `true` | | reloads the page |
| `webclick` | element query | | clicks an element of the page |
| `webdclick` | element query | | double click |
| `webwait` | element query | `timeout` inside the query (ms, default 15000) | waits until the element exists |
| `webmove` | element query | `ms` | moves the mouse to the element without clicking (`real` mode) |
| `webfill` | element query with `value` | `clear` inside the query (default `true`) | clicks into a field and types a value |
| `webeval` | JavaScript expression | | evaluates it in the page |
| `screenshot` | file name | | writes a picture of the current window; a relative name lands in the recording folder |
| `record` | `start` or `stop` | `region` | records only a part of the scenario |

A control query or element query may be written as a plain text instead of an object; it then
means `text`.

`"secret": true` on a `type` step keeps the typed text out of `actions.jsonl`. The video still
shows what the application displays.

## Through an MCP client

The same operations are MCP tools: `list_windows`, `focus_window`, `list_controls`, `launch`,
`screenshot`, `click`, `click_control`, `fill_control`, `type_text`, `press_keys`, `web_open`,
`web_click`, `web_fill` and others. The usual order is: `record_start`, `launch`,
`focus_window`, `screenshot` or `list_controls`, then the actions with `record_caption` between
the business steps, and `record_stop`. See [MCP server](mcp.md).

## Limits

- `background` mode works only for programs that react to posted window messages. A program
  that reads the keyboard state directly, or draws its controls itself, needs `real` mode.
- `real` mode needs a visible, unlocked desktop and takes over mouse and keyboard while it runs.
- Key combinations with Ctrl, Alt or Shift are refused in `background` mode.
- While a person works by hand (`record --start`), actions are not recorded: the recording has
  video and database events only.
- Windows only.
