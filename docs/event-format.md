<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](event-format.md) · [Deutsch](de/event-format.md)

# Event format v0.1

EQLMaker writes what an application did in the database as events, one JSON object per line
(JSON Lines, UTF-8, no byte order mark). The file of a recording is `events.jsonl`. Every event
source writes this format, and plugins that bring their own source write it too.

This is version 0.1. It describes what EQLMaker reads and writes today. A standalone, versioned
specification for other tools (v1.0) is planned and not part of this release; expect changes
until then.

## Example

```json
{"v":"0.1","seq":17,"ts":1795630831123000,"time":"2026-11-25T18:20:31.123000Z","offset":12.481,"kind":"execute","source":"postgres","via":"log","session":"4711","user":"sample_app","database":"eqlmaker_sample","application":"sample-desktop","sql":"INSERT INTO customer (name, city) VALUES ($1, $2)","params":["Harbour Books","Rostock"],"tx":null,"tables":null,"rows":null,"error":null}
{"v":"0.1","seq":18,"ts":1795630831124377,"time":"2026-11-25T18:20:31.124377Z","offset":12.482377,"kind":"transaction","source":"postgres","via":"replication","session":null,"user":null,"database":"eqlmaker_sample","application":null,"sql":null,"params":null,"tx":"739","tables":["public.customer"],"rows":[{"table":"public.customer","op":"insert","new":{"id":"9","name":"Harbour Books","city":"Rostock"},"old":null}],"error":null}
```

## Fields

Every event has every field. A field that a source cannot deliver is `null`; it is never left
out and never filled with a guess.

| Field | Type | Meaning |
|---|---|---|
| `v` | string | Version of the format, `"0.1"`. |
| `seq` | integer | Position of the event in its file, starting at 1. |
| `ts` | integer | Time in microseconds since 1970-01-01 00:00:00 UTC, on the clock of the database server. |
| `time` | string | The same time as ISO 8601 text in UTC, for people. `ts` is the value to compute with. |
| `offset` | number or null | Seconds since the start of the recording, on the clock of the recording machine. The recorder computes it from `ts` and the measured difference between the two clocks. `null` outside a recording. |
| `kind` | string | `statement` (a statement sent as text), `execute` (a statement run with parameters), `error` (the server refused a statement), `transaction` (a committed transaction that changed rows). |
| `source` | string | Name of the event source: `postgres`, `eqlserver`, or the name of a plugin. |
| `via` | string | How the source saw the event: `log` (server log), `replication` (logical decoding), `trace` (statement trace of the server). |
| `session` | string or null | Session of the application on the server (PostgreSQL: process id of the backend). |
| `user` | string or null | Login name of the session. |
| `database` | string or null | Database of the session. |
| `application` | string or null | Name the application gave to the server (`application_name`). |
| `sql` | string or null | Text of the statement. For `error`: the statement that failed, when the server reports it. |
| `params` | array or null | Values of the parameters as text, in order; a `null` entry is SQL NULL. `null` when the statement had no parameters or the source cannot see them. |
| `tx` | string or null | Transaction id of the server. |
| `tables` | array or null | For `transaction`: the tables that were changed, as `schema.table`. Measured, not derived from the SQL text. |
| `rows` | array or null | For `transaction`: the changed rows in the order of the changes. Each row: `table`, `op` (`insert`, `update`, `delete`, `truncate`), `new` (column: value after the change) and `old` (column: value before the change, as far as the server provides it). Values are text; SQL NULL is `null`. |
| `error` | string or null | For `error`: the message of the server. |

Order: events are sorted by `ts`; events with the same `ts` keep the order in which the source
delivered them. A statement stands before the transaction it belongs to, because the server logs
a statement when it starts and reports a transaction when it commits.

## Which source delivers what

| Field | `postgres` | `eqlserver`, trace part | Log file imported later (`import-log`) |
|---|---|---|---|
| `ts` | statements: millisecond precision (the server log has no finer time); transactions: microseconds (commit time) | milliseconds | as the log has it |
| `session` | yes | yes | yes |
| `user`, `database`, `application` | yes. For a session that lasted less than about 100 ms: `database` or `user` only when exactly one database or role is captured, `application` is `null` | `user` and `application` yes; `database` as far as the server reports it | only when `log_line_prefix` contains `%u`, `%d`, `%a` |
| `sql` | yes | yes | yes |
| `params` | for statements sent with parameters | `null` | when the log has the parameter lines |
| `tx` | transactions: yes. Statements: only when `log_line_prefix` contains `%x` | `null` | only with `%x` |
| `tables`, `rows` | when the server runs with `wal_level = logical`; otherwise no `transaction` events at all, and the recording says so | `null` | `null` |
| `error` | yes | `null` | yes |

The `eqlserver` source delivers everything the `postgres` source delivers for connections that
speak the PostgreSQL protocol, and adds the trace part for the other connections. See
[sources](sources.md).

`old` values of `update` and `delete` rows follow the `REPLICA IDENTITY` of the table: by default
the key columns, with `REPLICA IDENTITY FULL` the whole row.

## Reading the format

- Read line by line; skip empty lines.
- Refuse a file whose `v` has a major version you do not know. EQLMaker 0.1 reads `0.x`.
- Ignore fields you do not know.
- Do not rely on `seq` being without gaps across files that were merged.

## Related files of a recording

`actions.jsonl` holds what was operated (clicks, typed text, chapter marks) with the same `ts`
and `offset` scale but on the clock of the recording machine. `timeline.json`, written by
`eqlmaker correlate`, ties actions, statements and transactions together. Both are described in
[pipeline](pipeline.md).
