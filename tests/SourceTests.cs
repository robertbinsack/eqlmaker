// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Event sources against a real PostgreSQL (EQLMAKER_TEST_PG) and against a simulated trace endpoint.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace EqlMaker.Tests {

/// <summary>Temporary database and login role on the test server; dropped at the end.</summary>
public class TestDb : IDisposable {
    public string Admin, Database, Role, RolePassword; public PgConnInfo App;
    public TestDb() {
        Admin = T.Pg;
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        Database = "eqlm_t_" + id; Role = "eqlm_r_" + id; RolePassword = "P" + Guid.NewGuid().ToString("N");
        using (var c = new PgClient(Admin)) {
            c.Exec("CREATE ROLE " + Role + " LOGIN PASSWORD " + PgClient.Lit(RolePassword));
            c.Exec("CREATE DATABASE " + Database + " OWNER " + Role);
        }
        App = PgConnInfo.Parse(Admin).WithDatabase(Database); App.User = Role; App.Password = RolePassword; App.Application = "test-app";
    }
    public string AdminFor(string db) { var c = PgConnInfo.Parse(Admin); return "host=" + c.Host + " port=" + c.Port + " user=" + c.User + " dbname=" + db + (c.Password != null ? " password='" + c.Password.Replace("'", "\\'") + "'" : ""); }
    public void Dispose() {
        using (var c = new PgClient(Admin)) {
            foreach (var row in c.Query("SELECT slot_name FROM pg_replication_slots WHERE database = $1", Database).Rows) { try { c.Query("SELECT pg_drop_replication_slot($1)", row[0]); } catch { } }
            for (int i = 0; i < 20; i++) {
                try { c.Exec("DROP DATABASE IF EXISTS " + Database + " WITH (FORCE)"); break; } catch (PgException) { Thread.Sleep(250); }
            }
            c.Exec("DROP ROLE IF EXISTS " + Role);
        }
    }
}

public static class PostgresSourceTests {
    public static void ClientAuthenticatesAndRunsParameterisedQueries() {
        using (var c = new PgClient(T.Pg)) {
            T.Eq("3", c.Query("SELECT $1::int + $2::int", "1", "2").Scalar, "parameters");
            T.Eq(null, c.Query("SELECT $1::text", new string[] { null }).Scalar, "NULL parameter");
            T.Eq("ä€", c.Scalar("SELECT 'ä€'"), "UTF-8");
            var r = c.Exec("SELECT 1 AS a; SELECT 2 AS b, 3 AS c");
            T.Eq("b", r.Columns[0], "last result set"); T.Eq("SELECT 1", r.Tag, "tag");
            T.Throws(delegate { c.Exec("SELECT * FROM no_such_table_xyz"); }, "does not exist", "error");
            T.Eq("1", c.Scalar("SELECT 1"), "usable after an error");
            T.True(c.BackendPid > 0, "backend pid known");
        }
    }

    public static void WrongPasswordIsRefused() {
        var ci = PgConnInfo.Parse(T.Pg); ci.Password = "certainly-wrong";
        T.Throws(delegate { new PgClient(ci).Dispose(); }, "password", "authentication error");
    }

    public static void CapturesStatementsParametersErrorsAndChangedRows() {
        using (var db = new TestDb()) {
            using (var c = new PgClient(db.App)) c.Exec("CREATE TABLE item (id serial PRIMARY KEY, name text NOT NULL, qty integer); CREATE TABLE \"Audit Log\" (id serial PRIMARY KEY, note text)");
            var src = new PostgresSource();
            string state = Path.Combine(T.TempDir("capture"), "capture-active.json");
            var opt = new CaptureOptions { Connection = T.Pg, Databases = new List<string> { db.Database }, Changes = true, StateFile = state };
            long before = Clock.NowUs();
            src.Start(opt);
            try {
                T.True(File.Exists(state), "state file while the capture runs");
                using (var admin = new PgClient(T.Pg))
                    T.Eq("log_statement=all", admin.Query("SELECT array_to_string(setconfig, ',') FROM pg_db_role_setting s JOIN pg_database d ON d.oid = s.setdatabase WHERE d.datname = $1", db.Database).Scalar, "log_statement is set for the database only");
                using (var c = new PgClient(db.App)) {
                    c.Exec("SELECT 'plain'");
                    c.Exec("SELECT pg_sleep(1.0)");   // long enough to be seen in pg_stat_activity, also on a busy machine
                    c.Query("INSERT INTO item (name, qty) VALUES ($1, $2)", "bolt, 'M8'", "10");
                    c.Query("INSERT INTO item (name, qty) VALUES ($1, $2)", "nut", null);
                    try { c.Exec("SELECT * FROM missing_table"); } catch (PgException) { }
                    c.Exec("BEGIN");
                    c.Query("UPDATE item SET qty = qty - $1 WHERE name = $2", "3", "bolt, 'M8'");
                    c.Exec("INSERT INTO \"Audit Log\" (note) VALUES ('two\nlines')");
                    c.Exec("DELETE FROM item WHERE name = 'nut'");
                    c.Exec("COMMIT");
                    c.Exec("BEGIN; INSERT INTO item (name) VALUES ('rolled back'); ROLLBACK");
                }
                using (var c = new PgClient(db.App.WithApplication("short-session"))) c.Exec("SELECT 'short'");
            } finally { }
            var ev = src.Stop();
            long after = Clock.NowUs();
            var info = src.Info;

            var stm = ev.FindAll(delegate (DbEvent e) { return e.Kind == "statement" || e.Kind == "execute"; });
            var sqls = stm.ConvertAll(delegate (DbEvent e) { return e.Sql; });
            T.Eq("SELECT 'plain'", sqls[0], "first statement");
            T.True(sqls.Contains("INSERT INTO item (name, qty) VALUES ($1, $2)") && sqls.Contains("COMMIT") && sqls.Contains("SELECT 'short'"), "statements of both sessions: " + string.Join(" | ", sqls.ToArray()));
            T.True(sqls.Contains("INSERT INTO \"Audit Log\" (note) VALUES ('two\nlines')"), "statement with a line break");
            var ins = stm.Find(delegate (DbEvent e) { return e.Sql.StartsWith("INSERT INTO item (name, qty)"); });
            T.Eq("execute", ins.Kind, "extended protocol"); T.Eq("bolt, 'M8'", ins.Params[0], "parameter 1"); T.Eq("10", ins.Params[1], "parameter 2");
            var ins2 = stm.FindAll(delegate (DbEvent e) { return e.Sql.StartsWith("INSERT INTO item (name, qty)"); })[1];
            T.Eq(null, ins2.Params[1], "NULL parameter");
            T.Eq(db.Role, ins.User, "user"); T.Eq(db.Database, ins.Database, "database"); T.Eq("test-app", ins.Application, "application");
            T.True(ins.Session != null && ins.Session != stm.Find(delegate (DbEvent e) { return e.Sql == "SELECT 'short'"; }).Session, "sessions differ");
            var shortOne = stm.Find(delegate (DbEvent e) { return e.Sql == "SELECT 'short'"; });
            T.Eq(db.Database, shortOne.Database, "database of a session that lasted a few milliseconds");
            T.True(shortOne.User == null || shortOne.User == db.Role, "user of the short session is known or null");
            T.True(shortOne.Application == null || shortOne.Application == "short-session", "application of the short session is known or null");
            var err = ev.Find(delegate (DbEvent e) { return e.Kind == "error"; });
            T.Contains(err.Error, "missing_table", "error message"); T.Eq("SELECT * FROM missing_table", err.Sql, "statement of the error");
            T.True(!sqls.Exists(delegate (string s) { return s.Contains("pg_stat_activity") || s.Contains("pg_logical"); }), "own statements are not in the result");

            var tx = ev.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; });
            T.Eq(3, tx.Count, "committed transactions with changes (two inserts, one block; the rolled back one is absent)");
            T.Eq("insert", tx[0].Rows[0].Str("op"), "op"); T.Eq("bolt, 'M8'", tx[0].Rows[0].Obj("new").Str("name"), "inserted value");
            var block = tx[2];
            T.Eq(3, block.Rows.Count, "rows of the block"); T.Eq("public.item,public.Audit Log", string.Join(",", block.Tables.ToArray()), "tables changed");
            T.Eq("update", block.Rows[0].Str("op"), "update"); T.Eq("7", block.Rows[0].Obj("new").Str("qty"), "new value");
            T.Eq("two\nlines", block.Rows[1].Obj("new").Str("note"), "value with a line break");
            T.Eq("delete", block.Rows[2].Str("op"), "delete"); T.True(block.Rows[2].Obj("old").Str("id") != null, "key of the deleted row");
            T.True(block.Tx != null && block.Tx != tx[0].Tx, "transaction ids");

            foreach (var e in ev) T.True(e.Ts > before - 2000000 && e.Ts < after + 2000000, "event time within the capture (server clock): " + e.Kind);
            var commit = stm.Find(delegate (DbEvent e) { return e.Sql == "COMMIT"; });
            T.True(Math.Abs(commit.Ts - block.Ts) < 500000, "COMMIT statement and commit time are close: " + (block.Ts - commit.Ts) + " us");
            T.True(Math.Abs(info.Long("server_minus_local_us", long.MaxValue)) < 2000000, "server clock offset measured: " + info.Long("server_minus_local_us", 0));
            T.Eq("on", info.Str("changes"), "changes captured");
            T.Note("server_minus_local_us", info.Long("server_minus_local_us", 0));
            T.Note("clock_round_trip_us", info.Long("clock_round_trip_us", 0));

            using (var admin = new PgClient(T.Pg)) {
                T.Eq("0", admin.Query("SELECT count(*) FROM pg_db_role_setting s JOIN pg_database d ON d.oid = s.setdatabase WHERE d.datname = $1", db.Database).Scalar, "log_statement reset after the capture");
                T.Eq("0", admin.Query("SELECT count(*) FROM pg_replication_slots WHERE database = $1", db.Database).Scalar, "replication slot dropped");
            }
            T.True(!File.Exists(state), "state file removed");
        }
    }

    public static void RoleCaptureAndResetOfAnAbortedCapture() {
        using (var db = new TestDb()) {
            string state = Path.Combine(T.TempDir("capture2"), "capture-active.json");
            var opt = new CaptureOptions { Connection = db.AdminFor(db.Database), Roles = new List<string> { db.Role }, Changes = true, StateFile = state };
            var src = new PostgresSource();
            src.Start(opt);
            // the capture is "aborted": nobody calls Stop
            using (var admin = new PgClient(T.Pg)) {
                T.Eq("log_statement=all", admin.Query("SELECT array_to_string(rolconfig, ',') FROM pg_roles WHERE rolname = $1", db.Role).Scalar, "role setting");
                T.Eq("1", admin.Query("SELECT count(*) FROM pg_replication_slots WHERE database = $1", db.Database).Scalar, "slot exists");
            }
            new PostgresSource().Reset(new CaptureOptions { Connection = db.AdminFor(db.Database), StateFile = state });
            using (var admin = new PgClient(T.Pg)) {
                T.Eq("", admin.Query("SELECT coalesce(array_to_string(rolconfig, ','), '') FROM pg_roles WHERE rolname = $1", db.Role).Scalar, "role setting reset");
                T.Eq("0", admin.Query("SELECT count(*) FROM pg_replication_slots WHERE database = $1", db.Database).Scalar, "slot dropped by reset");
            }
            try { src.Stop(); } catch { }
        }
    }

    public static void NeedsRolesOrDatabases() {
        T.Throws(delegate { new PostgresSource().Start(new CaptureOptions { Connection = T.Pg }); }, "roles or databases", "empty capture");
    }
}

public static class EqlServerSourceTests {
    public static void TraceFileIsReadIntoEvents() {
        string f = Path.Combine(T.TempDir("trace"), "trace.tsv");
        File.WriteAllText(f, "time\tsession\tuser\tdatabase\tapplication\tkind\tstatement\n" +
            "2026-03-01 10:00:00.250\t17\tapp\tshop\tOrder Entry\tbatch\tSELECT a\\n  FROM t\\tWHERE x = 'b\\\\c'\n" +
            "2026-03-01 10:00:01.500\t17\tapp\tshop\tOrder Entry\trpc\tUPDATE t SET a = @p\n" +
            "short line\n", new UTF8Encoding(false));
        var ev = EqlServerSource.ReadTrace(f, "eqlserver");
        T.Eq(2, ev.Count, "events"); T.Eq("statement", ev[0].Kind, "batch"); T.Eq("execute", ev[1].Kind, "rpc");
        T.Eq("SELECT a\n  FROM t\tWHERE x = 'b\\c'", ev[0].Sql, "escapes"); T.Eq("trace", ev[0].Via, "via"); T.Eq("17", ev[0].Session, "session");
        T.Eq(Clock.ToUs(new DateTime(2026, 3, 1, 10, 0, 0, 250, DateTimeKind.Local).ToUniversalTime()), ev[0].Ts, "local time of the server");
        T.Eq(1250000L, ev[1].Ts - ev[0].Ts, "distance");
        T.True(ev[0].Params == null && ev[0].Tables == null && ev[0].Rows == null && ev[0].Tx == null, "fields the trace cannot deliver stay null");
    }

    /// <summary>Simulated statistics port: answers /trace/start and /trace/stop like the documented interface.</summary>
    public static void TraceEndpointIsUsedWhenConfigured() {
        using (var db = new TestDb()) {
            string dir = T.TempDir("trace2"), f = Path.Combine(dir, "t.tsv");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var seen = new List<string>(); bool run = true;
            var th = new Thread(delegate () {
                while (run) {
                    TcpClient cl;
                    try { cl = listener.AcceptTcpClient(); } catch { break; }
                    using (cl) using (var st = cl.GetStream()) {
                        var rd = new StreamReader(st, Encoding.ASCII);
                        string first = rd.ReadLine() ?? ""; string l; while (!string.IsNullOrEmpty(l = rd.ReadLine())) { }
                        lock (seen) seen.Add(first);
                        if (first.Contains("/trace/stop")) File.WriteAllText(f, "time\tsession\tuser\tdatabase\tapplication\tkind\tstatement\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\t5\tsa\t" + db.Database + "\tlegacy app\tbatch\tSELECT 42\n");
                        string body = "{\"file\":" + Json.Write(f) + "}";
                        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n");
                        st.Write(bytes, 0, bytes.Length); var bb = Encoding.UTF8.GetBytes(body); st.Write(bb, 0, bb.Length);
                    }
                }
            });
            th.IsBackground = true; th.Start();
            try {
                var src = new EqlServerSource();
                var opt = new CaptureOptions { Connection = T.Pg, Databases = new List<string> { db.Database }, Changes = false, SourceSettings = new JO("statsPort", port) };
                src.Start(opt);
                T.Eq("on", src.Info.Str("trace"), "trace started");
                using (var c = new PgClient(db.App)) c.Exec("SELECT 'over the PostgreSQL protocol'");
                var ev = src.Stop();
                T.True(ev.Exists(delegate (DbEvent e) { return e.Via == "log" && e.Sql == "SELECT 'over the PostgreSQL protocol'"; }), "statement from the server log");
                var tr = ev.Find(delegate (DbEvent e) { return e.Via == "trace"; });
                T.True(tr != null && tr.Sql == "SELECT 42" && tr.Application == "legacy app" && tr.Source == "eqlserver", "statement from the trace");
                lock (seen) {
                    T.True(seen.Exists(delegate (string s) { return s.Contains("/trace/start?logins=&databases=" + db.Database); }), "start request: " + string.Join(" | ", seen.ToArray()));
                    T.True(seen.Exists(delegate (string s) { return s.Contains("/trace/stop"); }), "stop request");
                }
                T.True(!File.Exists(f), "trace file removed after reading");
            } finally { run = false; listener.Stop(); }
        }
    }
}
}
