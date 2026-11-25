<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](other-databases.md) · [Deutsch](de/other-databases.md)

# Applications on other databases

## The principle

A rebuild can only be proved when what a person does on the screen and what the application
does in the database are seen on **one time line**. "This click wrote these rows" is the sentence
that the comparison tests of EQLMaker check for the original and for the rebuild
([comparison tests](verify.md)). EQLMaker measures the database side on PostgreSQL, so the
question for every application is: does its data live in a PostgreSQL that EQLMaker can watch?

| The application uses | What to do | What EQLMaker sees |
|---|---|---|
| PostgreSQL | nothing: plain PostgreSQL, see [event sources](sources.md) | statements, parameters, errors, sessions, changed rows |
| SQL Server, MySQL or MariaDB, Oracle, SQLite, Firebird or InterBase, SAP SQL Anywhere, Advantage, Informix, Access, SAP or Sybase ASE | replace the database by a server that accepts the application unchanged and stores in PostgreSQL, for example EQLServer | the same, as far as the table below says |
| a database that cannot be replaced, or no access to it | work with screen, video and manual only | no database events; see "Without database access" |

[EQLServer](https://eqlserver.com) is a commercial database server of the author of EQLMaker. It is
a separate product, optional, and **EQLMaker does not need it**: everything in this repository
works with a plain PostgreSQL. What other databases need is a server that stores in PostgreSQL,
so that EQLMaker can see the transactions. EQLServer is one such server; EQLMaker reads it with the
source `eqlserver`, and a server of another maker could be read with a [source plugin](plugins.md).

## Steps with EQLServer

1. Start EQLServer on the machine that runs the application, or on another machine. For a test on
   one machine set `ListenAddresses` to `localhost` in the configuration file of the server.
2. Point the application at it as its manual describes: the same driver, the same kind of
   connection string, the port of the protocol of the original database. The application is not
   changed.
3. Changed rows need logical decoding, which EQLServer inherits from PostgreSQL. Once:
   `ALTER SYSTEM SET wal_level = logical;` as the administrator, then restart the server
   (`eqlserverctl restart`). Without it EQLMaker records statements and says that changed rows are
   missing; `verify db` needs them.
4. Write the project configuration. The login is the PostgreSQL superuser of the server (EQLMaker
   changes `log_statement` of the captured roles and reads the log).

```json
{
  "source": "eqlserver",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": ["app_login"], "databases": ["eqlserver"], "changes": true,
               "logFile": "C:/EQLServer/eqlserver-server.log" },
  "eqlserver": { "statsPort": 7434 }
}
```

   - `roles` are the logins the application uses, `databases` the PostgreSQL database that holds
     the data (name it as `psql -l` shows it).
   - `logFile` is the log of the server: EQLServer does not use the logging collector, it writes
     `eqlserver-server.log` into its folder.
   - `statsPort` is the statistics port of the connection pool; the manual of the server says
     where to find it. The statement trace of the T-SQL connections is read there.
   - The password of the connection goes into the environment variable `EQLMAKER_PGPASSWORD`,
     see [configuration](configuration.md).
5. `eqlmaker record --start`, start the application, work, `eqlmaker record --stop`, then
   `eqlmaker correlate <recording>` and `eqlmaker spec`. Start the application after the
   recording has started: the capture applies to sessions that begin afterwards.

## What EQLMaker sees

| | T-SQL (TDS) | PostgreSQL protocol | other protocols |
|---|---|---|---|
| statement text | batches as `statement`, calls with parameters (RPC) as `execute`, from the statement trace of the server | from the server log | depends on the server; not tested here |
| parameters of a call | not visible (`params` is `null`) | visible for statements sent with parameters | not tested here |
| user, application, session | yes | yes | not tested here |
| changed rows | yes, from logical decoding of the database in which the server stores | yes | yes, whatever the protocol: the rows are stored in PostgreSQL |

Things to know when you read the result:

- A statement names the T-SQL database of the application (`shopdb`), a changed row the physical
  one. In multi-database mode of the server the physical table is `<database>_<schema>.<table>`
  (`shopdb_dbo.customer`). `correlate` knows this and ties a transaction to the statement of the same
  database and table.
- Statement times of the trace have millisecond precision. When two statements of one session
  run within the same millisecond, a transaction is tied to the last statement before its commit that writes to
  the table (method `time+table`, see [pipeline](pipeline.md)); that need not be the right one of the two.
- `eqlmaker export-sql <recording>` writes the statements as `sql.tsv`; batches and calls of the trace have the
  kinds `tsql-batch` and `tsql-rpc`.

## What is tested and what is not

Tested by `tests/eqlserver/run-eqlserver-e2e.ps1`, optional, against a running EQLServer 1.10.0
(it was run on 127.0.0.1 with ports of its own):

- a T-SQL client (`System.Data.SqlClient`) does inserts, an update, a delete and a failing
  statement through TDS while `eqlmaker record --scenario` captures;
- the recording holds the batches and the calls with their user, database and application, the
  error, and the changed rows with their values;
- `correlate` builds three chapters and ties every transaction to its statement by time and
  table; `export-sql` writes `tsql-batch`, `tsql-rpc` and `error` rows; `spec` names the table;
- `eqlmaker reset` leaves no replication slot, no publication and no `log_statement` setting;
The server of the test has no `test_decoding` module. EQLMaker then reads the changed rows with
the output plugin `pgoutput`, which every PostgreSQL contains; see [event sources](sources.md).

The script installs, starts and stops no server. It needs a running one: the statement trace on
its statistics port, `wal_level = logical`, a T-SQL login that may create a database and a
PostgreSQL superuser. Give it the ports and, only through the environment, the two passwords
(`EQLMAKER_E2E_TDS_PORT`, `EQLMAKER_E2E_PG_PORT`, `EQLMAKER_E2E_STATS_PORT`,
`EQLMAKER_E2E_TDS_PASSWORD`, `EQLMAKER_E2E_PG_PASSWORD`; optional `EQLMAKER_E2E_HOST`,
`EQLMAKER_E2E_DATABASE`, `EQLMAKER_E2E_TDS_USER`, `EQLMAKER_E2E_PG_USER`, `EQLMAKER_E2E_PSQL`,
`EQLMAKER_E2E_LOGFILE`; the parameters of the script have the same names without the prefix).
`bin\eqlmaker-tests.exe` runs the script when `EQLMAKER_E2E_TDS_PORT` is set. Without the ports
the test is skipped.

**Not tested:** the protocols of MySQL and MariaDB, Oracle, SQLite, Firebird and InterBase, SAP
SQL Anywhere, Advantage, Informix, Access and SAP or Sybase ASE against the capture of EQLMaker;
an application with a screen (the test client is a script); a server on another machine; TLS
between application and server; versions of EQLServer other than 1.10.0; EQLServer as a Windows
service. What EQLServer itself tests for those protocols is described by its own documentation.

## Without database access

EQLMaker still works with screen, video and manual: recording of the operation, the manual, the
narrated video, the specification of the screens and their controls, the rebuild as a guided
step and the picture comparison `verify screens`. What is not possible then is the comparison of
the data: `verify db` compares the measured changes of two recordings, and without them there
is nothing to compare. The rebuild is then checked by looking at it, not by measuring it.
