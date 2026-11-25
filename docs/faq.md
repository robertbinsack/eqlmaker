<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](faq.md) · [Deutsch](de/faq.md)

# FAQ

Short answers to frequent questions. Each answer names the page with the details.

## Is my data sent anywhere?

No. EQLMaker uses the network only for what you configure: the connection to your database
server, and the browser and programs it operates on the same machine. It has no telemetry, no
update check and no account. Speech recognition runs offline on your computer.

Two things are outside EQLMaker and in your hands. The setup of the Windows package downloads
components from pinned addresses, unless you run it with `-offline`. And when you connect an
assistant as MCP client, that assistant sees what the tools return: screenshots, the
specification, statements with their values. Where the assistant sends that is decided by the
assistant you chose, not by EQLMaker.

## What about personal data in recordings?

It is not masked in version 0.2. A recording contains the screen video, reference pictures,
typed text, SQL statements with their parameters and the changed rows; an imported video adds
sound and transcript. Treat a project folder like the data of the application itself: record on
test data where you can, do not publish recordings of real data, and delete what you no longer
need. Masking of personal data is planned, see the [roadmap](../ROADMAP.md).

## Do I need the source code of the old application?

No. EQLMaker watches the running application and the database. It does not read or change the
program files.

## Does EQLMaker write the new application?

No. `generate`, `clone` and `enrich` are guided steps: EQLMaker prepares a workspace and
instructions and checks the result with comparison tests. An assistant connected as MCP client,
or a person, does the work. See [guided steps](guided-steps.md).

## Which applications can it record?

Applications whose database is PostgreSQL 15 or later, on Windows: Windows programs and web
pages. For applications on other database servers, PostgreSQL can be replaced by EQLServer, a
commercial product by the same author that is not part of EQLMaker; see
[event sources](sources.md). Sources for other servers are planned.

## What does the capture change on my database server?

For the roles and databases you name, it switches statement logging on for the time of the
recording and resets it afterwards. With `wal_level = logical` it creates a replication slot per
captured database and drops it at the end. It changes no server-wide setting and never restarts
the server. After an aborted recording, `eqlmaker reset` cleans up. See
[event sources](sources.md).

## Why does my recording have no changed rows?

The server does not run with `wal_level = logical`. The recording says so in its warnings.
Statements are still captured, but `verify db` needs changed rows. Setting `wal_level` needs
the administrator of the server and one restart.

## Why are no statements captured at all?

Most often the application was already running when the recording started: the capture applies
to sessions that start afterwards. Other causes: the database or role is not named under
`capture` in `eqlmaker.json`, or the server log is not readable, see
[configuration](configuration.md).

## Can it find logic that I did not operate?

No. The specification contains only what was operated during a recording, and the comparison
tests compare only what was recorded. Record rarely used functions and error cases too. For a
migration, a person checks mask by mask.

## How exact is the link between a click and its database changes?

Statements carry the time of the server log, with millisecond precision. Which rows a statement
changed is exact when the server logs transaction ids (`%x` in `log_line_prefix`); otherwise it
is derived from time and table, and the method is written next to each link. See
[pipeline](pipeline.md).

## Does it take over my mouse and keyboard?

In `real` mode, yes, while it operates a program. In `background` mode, no: it sends window
messages, and the program may even run on a desktop that is never shown. `background` mode
works for programs that react to such messages. See [remote control](remote-control.md).

## Are my actions recorded when I work by hand?

No. With `record --start` a recording has video and database events. Actions with window and
control are recorded when EQLMaker operates the program, from a scenario file or for an MCP
client.

## Do I need admin rights?

Not for EQLMaker and not for the setup of the package. The capture needs a superuser of the
database server. See [setup](setup.md).

## Which languages are there?

English and German, for programs, documentation and the web template. See
[translating](translating.md) for adding one.

## What are the limits of version 0.2?

- Windows only; PostgreSQL only (plus the adapter named above).
- No masking of personal data.
- Words inside pictures and key frames are not read; the PDF reader gives no text for scans.
- The transcript is as good as the speech model.
- The web template makes no accessibility claim.
- Whether a release package exists is stated at the top of the [README](../README.en.md).
