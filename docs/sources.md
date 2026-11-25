<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](sources.md) · [Deutsch](de/sources.md)

# Event sources

An event source tells EQLMaker what an application did in the database while it was operated.
You choose it with one line of `eqlmaker.json`; nothing else changes when you swap it:

```json
{ "source": "postgres", "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres" }
```

| Source | For | Delivers |
|---|---|---|
| `postgres` | applications that talk to PostgreSQL 15 or later | statements, parameters, errors, sessions, and the rows each transaction changed |
| `eqlserver` | the same, plus applications that reach the server through other database protocols | the above, plus statements of the other protocols |
| a plugin | anything else | what the plugin writes, see [plugins](plugins.md) |

What each source fills in of the event format is listed in [event format](event-format.md). Applications that do not speak PostgreSQL: see [other databases](other-databases.md).

## `postgres`

The source uses only what PostgreSQL ships. It does not install anything on the server, changes
no server-wide setting and never restarts the server.

**What it does while a capture runs**

1. For each role and database named under `capture`, it sets `log_statement = 'all'` with
   `ALTER ROLE ... SET` or `ALTER DATABASE ... SET`. This applies to sessions that start
   afterwards: start the application after the recording has started.
2. If the messages of the server are not English, it also sets `lc_messages = 'C'` for the same
   roles and databases, so that it can read their log lines. The recording carries a warning,
   because error messages shown by the application are English meanwhile.
3. If `capture.changes` is true and the server runs with `wal_level = logical`, it creates one
   logical replication slot per captured database (name `eqlmaker_` plus random characters,
   output plugin `test_decoding`).
4. It asks `pg_stat_activity` several times per second which sessions exist, to name user,
   database and application of each session.
5. It measures the difference between the clock of the server and the clock of this machine.

**What it does when the capture ends**

1. It resets the settings of step 1 and 2.
2. It reads the part of the server log that was written during the capture, over the database
   connection (`pg_read_binary_file`), so the server may be another machine. When the server does
   not write its log through the logging collector, name the log file in `capture.logFile`; it
   must then be readable from this machine.
3. It reads the changes from the replication slots and drops the slots.

**What the server must provide**

| Requirement | Why | If it is missing |
|---|---|---|
| a superuser connection | changing settings of roles and reading the log | the capture does not start and says why |
| `log_destination` contains `stderr` | the plain-text log is read | the capture does not start |
| `log_line_prefix` contains a time (`%m`, `%n` or `%t`) and the process id (`%p` or `%c`) | to tell when and in which session a statement ran; the PostgreSQL default `'%m [%p] '` is fine | the capture does not start |
| `wal_level = logical` | changed rows | the recording has statements but no changed rows, and says so; `verify db` needs changed rows |
| `log_line_prefix` contains `%x` (optional) | exact tie between statements and transactions | the tie is made by time and table, see [pipeline](pipeline.md) |

`wal_level` can only be changed by the administrator of the server and needs one restart:

```sql
ALTER SYSTEM SET wal_level = logical;
```

**Changed rows without `test_decoding`**

`test_decoding` is an optional module of PostgreSQL; some servers do not have it. With
`capture.decoding` at `auto` (the default) the source asks the server whether the module is there. If
it is not, it reads the changed rows with `pgoutput`, which every server has: for each captured
database it creates a publication `FOR ALL TABLES` and the replication slot with the same name
(`eqlmaker_` plus random characters), reads the changes in binary form and drops both when the capture
ends. `eqlmaker reset` drops them after an aborted capture. The recording says in a warning which way was taken. Set
`capture.decoding` to `test_decoding` or `pgoutput` to force one.

**Configuration**

```json
{
  "source": "postgres",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": [], "databases": ["appdb"], "changes": true, "logFile": "" }
}
```

Name roles, databases or both. The password does not belong into this file: put it into the
environment variable `EQLMAKER_PGPASSWORD`, or name a password file in pgpass format with
`"passfile"`, see [configuration](configuration.md). EQLMaker takes `EQLMAKER_PGPASSWORD` out of
its own environment after reading it, so the application it starts does not inherit it.

**After an aborted capture**

If a recording was killed, the settings of step 1 and a replication slot may be left on the
server. A slot that nobody reads keeps the server from removing old write-ahead log. Run:

```text
eqlmaker reset
```

It resets the settings and drops the slots whose name starts with `eqlmaker_`.

**Limits**

- Statement times have millisecond precision; the server log has no finer time.
- A session that lasts less than about 100 ms may end before it is seen in `pg_stat_activity`;
  its application name is then unknown.
- Logical decoding does not tell which session made a change. `correlate` ties changes to
  statements; without `%x` that tie is an approximation.
- Changes of other sessions in a captured database are captured too.
- `pgaudit` is not read.

## `eqlserver`

EQLServer is a commercial database server of the author of EQLMaker that speaks the protocols of
several other database systems on top of PostgreSQL. This source reads the same things as
`postgres` and, when a statistics port is configured, the statement trace that the server offers
over HTTP for connections that do not use the PostgreSQL protocol.

```json
{
  "source": "eqlserver",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": ["app_login"], "databases": ["appdb"], "changes": true, "logFile": "C:\\Server\\eqlserver-server.log" },
  "eqlserver": { "statsPort": 6433 }
}
```

- All connection data comes from `eqlmaker.json`; the source reads no file of the server other
  than its log and the trace file the server names.
- The server does not use the logging collector: `capture.logFile` must name its log file.
- The trace is switched on with `/trace/start` and read after `/trace/stop` at the statistics
  port on 127.0.0.1, so this part works on the machine of the server.
- Statements of the trace carry time, session, user, application and text; parameters, tables,
  rows and transaction are `null`.
- Changed rows need `wal_level = logical` on the server, as with PostgreSQL.

How it is tested: the automated tests run this source against plain PostgreSQL and a simulated
statistics port. An optional end-to-end test runs it against the package of EQLServer 1.10.0: a
T-SQL client works through TDS, and the time line holds its batches, its calls, an error and the
changed rows, see [other databases](other-databases.md). That server has no `test_decoding`
module, so the changed rows come through `pgoutput`.
