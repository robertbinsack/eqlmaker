// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.IO;

namespace EqlMaker.Tests {

public static class JsonTests {
    public static void RoundTripKeepsOrderAndTypes() {
        var o = Json.ParseObject("{\"b\":1,\"a\":[true,null,\"x\\n\\u00e4\",1.5,-7],\"c\":{\"z\":\"\\\"q\\\"\"}}");
        T.Eq("{\"b\":1,\"a\":[true,null,\"x\\nä\",1.5,-7],\"c\":{\"z\":\"\\\"q\\\"\"}}", Json.Write(o), "compact output");
        T.Eq(1L, o["b"], "integer"); T.Eq(1.5, o.Arr("a")[3], "double");
        T.Eq(o.Str("b"), "1", "Str of number");
    }
    public static void RejectsBrokenInput() {
        T.Throws(delegate { Json.Parse("{\"a\":1,}"); }, "key expected", "trailing comma");
        T.Throws(delegate { Json.Parse("[1 2]"); }, "expected", "missing comma");
        T.Throws(delegate { Json.Parse("{} x"); }, "unexpected", "text after value");
    }
    public static void JsonLinesFile() {
        string f = Path.Combine(T.TempDir("jsonl"), "a.jsonl");
        Json.WriteLines(f, new List<JO> { new JO("a", 1), new JO("a", "two\nlines") });
        var l = Json.ReadLines(f);
        T.Eq(2, l.Count, "lines"); T.Eq("two\nlines", l[1].Str("a"), "newline survives");
        T.Eq(2, File.ReadAllLines(f).Length, "one object per line");
    }
}

public static class ConnectionStringTests {
    public static void KeywordForm() {
        var c = PgConnInfo.Parse("host=localhost port=5433 user=app dbname='my db' password='p\\'w' application_name=x");
        T.Eq("127.0.0.1", c.Host, "localhost is IPv4 loopback"); T.Eq(5433, c.Port, "port"); T.Eq("app", c.User, "user");
        T.Eq("my db", c.Database, "quoted value"); T.Eq("p'w", c.Password, "escaped quote");
    }
    public static void UriForm() {
        // put together from parts, so that secret scanners do not take the made-up address for a real one
        var c = PgConnInfo.Parse("postgresql://" + "bob" + ":" + "s%40c" + "@" + "db.example:6000/shop?sslmode=require");
        T.Eq("db.example", c.Host, "host"); T.Eq(6000, c.Port, "port"); T.Eq("bob", c.User, "user"); T.Eq("s@c", c.Password, "password"); T.Eq("shop", c.Database, "db"); T.Eq("require", c.SslMode, "sslmode");
    }
    public static void DatabaseDefaultsToUser() { T.Eq("app", PgConnInfo.Parse("user=app").Database, "dbname default"); }
}

public static class LogParserTests {
    static bool All(LogEntry e, out string u, out string d, out string a) { u = e.User; d = e.Database; a = e.Application; return true; }

    public static void DefaultPrefixStatementsParametersErrors() {
        var rx = PgLog.LineRegex("%m [%p] ");
        string log =
            "2026-03-01 10:00:00.120 CET [4711] LOG:  statement: SELECT 1\n" +
            "2026-03-01 10:00:00.250 CET [4711] LOG:  execute <unnamed>: INSERT INTO customer (name, note)\n\tVALUES ($1, $2)\n" +
            "2026-03-01 10:00:00.250 CET [4711] DETAIL:  Parameters: $1 = 'O''Brien, Ltd', $2 = NULL\n" +
            "2026-03-01 10:00:00.400 CET [4712] ERROR:  relation \"nope\" does not exist at character 15\n" +
            "2026-03-01 10:00:00.400 CET [4712] STATEMENT:  SELECT * FROM nope\n" +
            "2026-03-01 10:00:00.500 CET [4711] LOG:  checkpoint starting: time\n";
        var ev = PgLog.ToEvents(PgLog.Parse(log, rx, 3600), "postgres", All);
        T.Eq(3, ev.Count, "events");
        T.Eq("statement", ev[0].Kind, "kind"); T.Eq("SELECT 1", ev[0].Sql, "sql"); T.Eq("4711", ev[0].Session, "session");
        T.Eq(Clock.ToUs(new DateTime(2026, 3, 1, 9, 0, 0, 120, DateTimeKind.Utc)), ev[0].Ts, "time in UTC microseconds");
        T.Eq("execute", ev[1].Kind, "kind"); T.Eq("INSERT INTO customer (name, note)\nVALUES ($1, $2)", ev[1].Sql, "multi-line statement");
        T.Eq(2, ev[1].Params.Count, "parameters"); T.Eq("O'Brien, Ltd", ev[1].Params[0], "quoted parameter with comma"); T.Eq(null, ev[1].Params[1], "NULL parameter");
        T.Eq("error", ev[2].Kind, "kind"); T.Eq("SELECT * FROM nope", ev[2].Sql, "statement of the error"); T.Contains(ev[2].Error, "does not exist", "message");
    }

    public static void RichPrefixGivesUserDatabaseApplicationTransaction() {
        var rx = PgLog.LineRegex("%n [%p] %q%u@%d app=%a tx=%x ");
        string log = "1772355600.500 [99] app@shop app=sample desktop tx=741 LOG:  statement: COMMIT\n" +
                     "1772355600.600 [99] app@shop app=sample desktop tx=0 LOG:  statement: SELECT 2\n";
        var ev = PgLog.ToEvents(PgLog.Parse(log, rx, 0), "postgres", All);
        T.Eq(2, ev.Count, "events"); T.Eq(1772355600500000L, ev[0].Ts, "epoch time");
        T.Eq("app", ev[0].User, "user"); T.Eq("shop", ev[0].Database, "database"); T.Eq("sample desktop", ev[0].Application, "application with blank");
        T.Eq("741", ev[0].Tx, "transaction id"); T.Eq(null, ev[1].Tx, "0 means none");
    }

    public static void PrefixWithoutTimeOrPidIsRefused() {
        T.True(PgLog.PrefixProblem("%m [%p] ") == null, "default prefix accepted");
        T.True(PgLog.PrefixProblem("%u ") != null, "prefix without time and pid refused");
        T.True(PgLog.PrefixProblem("%t %c ") == null, "session id counts as pid");
    }

    public static void SessionIdGivesPid() {
        var rx = PgLog.LineRegex("%t %c ");
        var e = PgLog.Parse("2026-03-01 10:00:00 UTC 69a3f1c0.1267 LOG:  statement: SELECT 1\n", rx, 0);
        T.Eq(0x1267, e[0].Pid, "pid from %c");
    }
}

public static class TestDecodingTests {
    public static void InsertUpdateDeleteWithQuotingAndTypes() {
        var lines = new[] {
            "BEGIN 739",
            "table public.customer: INSERT: id[integer]:7 name[character varying]:'Müller ''& Söhne''' note[text]:null tags[text[]]:'{a,b}' created[timestamp without time zone]:'2026-03-01 10:00:00'",
            "table public.\"Order Line\": UPDATE: old-key: id[integer]:1 new-tuple: id[integer]:1 qty[numeric]:2.50",
            "table sales.invoice: DELETE: id[integer]:3",
            "table public.log: DELETE: (no-tuple-data)",
            "COMMIT 739 (at 2026-03-01 10:00:00.123456+01)",
            "BEGIN 740", "COMMIT 740 (at 2026-03-01 10:00:01+01)" };
        var ev = TestDecoding.ToEvents(lines, "postgres", "shop");
        T.Eq(1, ev.Count, "the empty transaction is dropped");
        var e = ev[0];
        T.Eq("transaction", e.Kind, "kind"); T.Eq("739", e.Tx, "tx"); T.Eq(4, e.Rows.Count, "rows");
        T.Eq(Clock.ToUs(new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)) + 123456, e.Ts, "commit time in microseconds");
        T.Eq("public.customer,public.Order Line,sales.invoice,public.log", string.Join(",", e.Tables.ToArray()), "tables");
        var ins = e.Rows[0]; T.Eq("insert", ins.Str("op"), "op");
        T.Eq("Müller '& Söhne'", ins.Obj("new").Str("name"), "quoted text"); T.True(ins.Obj("new").Has("note") && ins.Obj("new")["note"] == null, "null value");
        T.Eq("{a,b}", ins.Obj("new").Str("tags"), "array type"); T.Eq("2026-03-01 10:00:00", ins.Obj("new").Str("created"), "type with blanks");
        var upd = e.Rows[1]; T.Eq("1", upd.Obj("old").Str("id"), "old key"); T.Eq("2.50", upd.Obj("new").Str("qty"), "new value");
        T.Eq("3", e.Rows[2].Obj("old").Str("id"), "delete key"); T.True(e.Rows[3]["old"] == null, "delete without key");
    }
    public static void NegativeAndFractionalZoneOffsets() {
        long utc = Clock.ToUs(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        T.Eq(utc, TestDecoding.ParseTime("2026-03-01 07:00:00-05"), "negative offset");
        T.Eq(utc, TestDecoding.ParseTime("2026-03-01 17:30:00+05:30"), "half-hour offset");
    }
}

public static class EventFormatTests {
    public static void FileRoundTripWithAllFields() {
        var e = new DbEvent { Ts = 1772355600123456L, Kind = "execute", Source = "postgres", Via = "log", Session = "42", User = "app", Database = "shop", Application = "sample", Sql = "UPDATE t SET a = $1",
            Params = new List<string> { "x", null }, Tx = "9", Offset = 1.25 };
        var t = new DbEvent { Ts = 1772355600200000L, Kind = "transaction", Source = "postgres", Via = "replication", Database = "shop", Tx = "9", Tables = new List<string> { "public.t" },
            Rows = new List<JO> { new JO("table", "public.t", "op", "update", "new", new JO("a", "x"), "old", null) } };
        string f = Path.Combine(T.TempDir("events"), "events.jsonl");
        DbEvent.WriteFile(f, new List<DbEvent> { e, t });
        string first = File.ReadAllLines(f)[0];
        foreach (var k in new[] { "\"v\":\"0.1\"", "\"seq\":1", "\"ts\":1772355600123456", "\"time\":\"2026-03-01T09:00:00.123456Z\"", "\"session\":\"42\"", "\"application\":\"sample\"", "\"params\":[\"x\",null]", "\"tx\":\"9\"", "\"tables\":null", "\"rows\":null" })
            T.Contains(first, k, "field in the file");
        var back = DbEvent.ReadFile(f);
        T.Eq(2, back.Count, "events"); T.Eq(null, back[0].Params[1], "null parameter"); T.Eq(1.25, back[0].Offset.Value, "offset");
        T.Eq("update", back[1].Rows[0].Str("op"), "row"); T.Eq("public.t", back[1].Tables[0], "table");
    }
    public static void NewerMajorVersionIsRefused() {
        T.Throws(delegate { DbEvent.FromJson(new JO("v", "1.0", "ts", 1, "kind", "statement")); }, "not supported", "version check");
    }
    public static void SortIsStable() {
        var l = new List<DbEvent> { new DbEvent { Ts = 2, Sql = "b" }, new DbEvent { Ts = 1, Sql = "a" }, new DbEvent { Ts = 2, Sql = "c" } };
        DbEvent.SortByTime(l);
        T.Eq("abc", l[0].Sql + l[1].Sql + l[2].Sql, "order");
    }
}
}
