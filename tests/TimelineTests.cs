// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.IO;

namespace EqlMaker.Tests {

public static class SqlTextTests {
    public static void VerbTablesAndShape() {
        T.Eq("select", SqlText.Verb("  -- c\n SELECT 1"), "verb after a comment");
        T.Eq("insert", SqlText.Verb("WITH x AS (SELECT 1) INSERT INTO t SELECT * FROM x"), "verb of a WITH statement");
        T.Eq("begin", SqlText.Verb("START TRANSACTION"), "start transaction");
        T.Eq("customer,public.invoice,Order Line", string.Join(",", SqlText.Tables("SELECT c.name FROM Customer c JOIN public.invoice i ON i.customer_id = c.id LEFT JOIN \"Order Line\" ol ON true WHERE c.note = 'FROM nowhere'").ToArray()), "tables of a join; text inside strings is ignored");
        T.Eq("invoice_line", string.Join(",", SqlText.Tables("INSERT INTO invoice_line (invoice_id, qty) VALUES ($1, $2)").ToArray()), "insert");
        T.Eq("article", string.Join(",", SqlText.Tables("UPDATE article SET stock = stock - $1 WHERE id = $2").ToArray()), "update");
        T.Eq("a,b", string.Join(",", SqlText.Tables("select * from a, b where a.x = b.x").ToArray()), "comma list");
        T.Eq("", string.Join(",", SqlText.Tables("SELECT * FROM generate_series(1, 3) g").ToArray()), "a function is not a table");
        T.Eq("dbo.kunden", string.Join(",", SqlText.Tables("SELECT TOP 5 * FROM [dbo].[Kunden] WHERE [Nr] = 7").ToArray()).ToLowerInvariant(), "bracket names of other dialects");
        string a = SqlText.Shape("SELECT * FROM customer WHERE id = 7 AND name = 'Müller ''x''' AND g IN (1, 2, 3)");
        string b = SqlText.Shape("select  *  from customer\n where id = 12345 and name = 'a' and g in (9,8)".Replace("select", "SELECT").Replace("from", "FROM").Replace("where", "WHERE").Replace(" and", " AND").Replace(" in", " IN"));
        T.Eq(a, b, "same shape for different values and spacing");
        T.Eq("SELECT * FROM customer WHERE id = ? AND name = ? AND g IN (?..)", a, "shape text");
        T.Eq("INSERT INTO t(a, b) VALUES ($1, $2)", SqlText.Shape("INSERT INTO t (a,b) VALUES ($1,$2)"), "parameters stay");
    }
}

public static class TimelineTests {
    /// <summary>A hand-written recording: two chapters, two sessions writing at the same time.</summary>
    public static string MakeRecording(string dir, bool withTxIds) {
        Directory.CreateDirectory(dir);
        long t0 = 1795630000000000L;
        Func<double, long> at = delegate (double s) { return t0 + (long)(s * 1000000); };
        var acts = new List<JO> {
            new JO("v", "0.1", "seq", 1, "ts", at(0.0), "offset", 0.0, "kind", "launch", "path", "app.exe"),
            new JO("v", "0.1", "seq", 2, "ts", at(1.0), "offset", 1.0, "kind", "chapter", "caption", "Customers"),
            new JO("v", "0.1", "seq", 3, "ts", at(1.5), "offset", 1.5, "kind", "click", "control", "New customer", "class", "Button", "window", "Shop", "shot", "shots/0001.png"),
            new JO("v", "0.1", "seq", 4, "ts", at(3.0), "offset", 3.0, "kind", "click", "control", "Save customer", "class", "Button", "window", "Customer 17", "shot", "shots/0002.png"),
            new JO("v", "0.1", "seq", 5, "ts", at(5.0), "offset", 5.0, "kind", "chapter", "caption", "New invoice"),
            new JO("v", "0.1", "seq", 6, "ts", at(6.0), "offset", 6.0, "kind", "click", "control", "Save invoice", "class", "Button", "window", "New invoice", "shot", "shots/0003.png") };
        Json.WriteLines(Path.Combine(dir, "actions.jsonl"), acts);
        var ev = new List<DbEvent>();
        Func<double, string, string, string, DbEvent> st = delegate (double s, string session, string sql, string tx) {
            return new DbEvent { Ts = at(s), Offset = s, Kind = sql.Contains("$") ? "execute" : "statement", Source = "postgres", Via = "log", Session = session, User = "app", Database = "shop", Application = session == "200" ? "other" : "sample", Sql = sql, Tx = withTxIds ? tx : null,
                Params = sql.Contains("$") ? new List<string> { "1" } : null };
        };
        Func<double, string, string, string, string, DbEvent> tx1 = delegate (double s, string id, string table, string op, string col) {
            return new DbEvent { Ts = at(s), Offset = s, Kind = "transaction", Source = "postgres", Via = "replication", Database = "shop", Tx = id, Tables = new List<string> { table },
                Rows = new List<JO> { new JO("table", table, "op", op, "new", new JO("id", "1", col, "x"), "old", null) } };
        };
        ev.Add(st(0.4, "100", "SELECT version()", null));                                   // start-up, tied to "launch"
        ev.Add(st(1.6, "100", "SELECT id, name FROM customer ORDER BY id", null));
        ev.Add(st(3.05, "100", "INSERT INTO customer (name) VALUES ($1)", "501"));           // autocommit
        ev.Add(st(3.051, "200", "UPDATE article SET stock = 5 WHERE id = 9", "502"));        // another session at the same time
        ev.Add(tx1(3.0512, "502", "public.article", "update", "stock"));
        ev.Add(tx1(3.0525, "501", "public.customer", "insert", "name"));
        ev.Add(st(6.1, "100", "BEGIN", null));
        ev.Add(st(6.11, "100", "UPDATE counter SET value = value + 1 WHERE name = 'invoice' RETURNING value", "503"));
        ev.Add(st(6.12, "100", "INSERT INTO invoice (number) VALUES ($1)", "503"));
        ev.Add(st(6.13, "100", "COMMIT", "503"));
        var big = tx1(6.1312, "503", "public.counter", "update", "value");
        big.Tables.Add("public.invoice"); big.Rows.Add(new JO("table", "public.invoice", "op", "insert", "new", new JO("id", "4", "number", "INV-1"), "old", null));
        ev.Add(big);
        ev.Add(new DbEvent { Ts = at(6.5), Offset = 6.5, Kind = "error", Source = "postgres", Via = "log", Session = "100", Database = "shop", Error = "duplicate key", Sql = "INSERT INTO x VALUES (1)" });
        DbEvent.WriteFile(Path.Combine(dir, "events.jsonl"), ev);
        Json.WriteFile(Path.Combine(dir, "recording.json"), new JO("format", "eqlmaker-recording", "version", "0.1", "name", Path.GetFileName(dir), "t0_us", t0, "duration", 8.0, "source", "postgres"));
        return dir;
    }

    static JO Find(JO tl, string chapter, int action) { foreach (JO c in tl.Arr("chapters")) if (c.Str("caption") == chapter) return (JO)c.Arr("actions")[action]; return null; }

    public static void EventsGoToTheActionBeforeThem() {
        var tl = Timeline.Build(RecordingData.Load(MakeRecording(Path.Combine(T.TempDir("tl1"), "rec"), false)));
        T.Eq(3, tl.Arr("chapters").Count, "chapters (one unnamed before the first mark)");
        var launch = (JO)((JO)tl.Arr("chapters")[0]).Arr("actions")[0];
        T.Eq("SELECT version()", ((JO)launch.Arr("statements")[0]).Str("sql"), "start-up statement tied to the launch");
        var newCust = Find(tl, "Customers", 1);
        T.Eq("New customer", newCust.Str("control"), "action"); T.Eq(1, newCust.Arr("statements").Count, "its statement");
        var save = Find(tl, "Customers", 2);
        T.Eq(2, save.Arr("statements").Count, "statements after Save customer (both sessions)"); T.Eq(2, save.Arr("transactions").Count, "transactions after Save customer");
        var inv = Find(tl, "New invoice", 1);
        T.Eq(5, inv.Arr("statements").Count, "statements of the invoice (with the error)"); T.Eq(1, inv.Arr("transactions").Count, "one transaction");
        var ch = (JO)tl.Arr("chapters")[2];
        T.Eq("public.counter,public.invoice", string.Join(",", ch.Obj("tables").Strings("written").ToArray()), "tables written (measured)");
        T.Eq(1, ch.Obj("counts").Int("errors", 0), "error counted");
        T.Eq(0, tl.Obj("counts").Int("before_first_action", -1), "nothing before the first action");
    }

    public static void TransactionsAreTiedByTimeAndTableWithoutIds() {
        var tl = Timeline.Build(RecordingData.Load(MakeRecording(Path.Combine(T.TempDir("tl2"), "rec"), false)));
        var att = tl.Arr("attribution");
        var a502 = (JO)att[0]; var a501 = (JO)att[1]; var a503 = (JO)att[2];
        T.Eq("time+table", a502.Str("method"), "method"); T.Eq("200", a502.Str("session"), "the other session wrote article");
        T.Eq("100", a501.Str("session"), "the customer insert belongs to session 100 although session 200 wrote later");
        T.Eq(1, a501.Arr("statements").Count, "autocommit: one statement");
        T.Eq(4, a503.Arr("statements").Count, "explicit block: BEGIN to COMMIT"); T.Eq("100", a503.Str("session"), "session of the block");
    }

    public static void TransactionIdsGiveExactTies() {
        var tl = Timeline.Build(RecordingData.Load(MakeRecording(Path.Combine(T.TempDir("tl3"), "rec"), true)));
        foreach (JO a in tl.Arr("attribution")) T.Eq("tx-id", a.Str("method"), "method with ids");
        T.Eq(3, ((JO)tl.Arr("attribution")[2]).Arr("statements").Count, "statements that carry the id (BEGIN has none yet)");
    }

    public static void SameInputGivesSameOutput() {
        string d = MakeRecording(Path.Combine(T.TempDir("tl4"), "rec"), false);
        T.Eq(Json.Write(Timeline.Build(RecordingData.Load(d))), Json.Write(Timeline.Build(RecordingData.Load(d))), "deterministic");
    }
}

public static class SpecTests {
    public static void StepsMasksQueriesTablesAndManualLinks() {
        string dir = T.TempDir("spec1");
        var cfg = Config.Load(dir);
        TimelineTests.MakeRecording(Path.Combine(cfg.RecordingsDir, "rec"), false);
        Directory.CreateDirectory(cfg.Sub("manual"));
        Json.WriteFile(cfg.Sub("manual", "handbook.json"), new JO("format", "eqlmaker-manual", "version", "0.1", "name", "handbook", "title", "Handbook",
            "chapters", new List<object> {
                new JO("id", "c1", "level", 1, "title", "Customers", "text", "A customer has a name and a credit limit."),
                new JO("id", "c2", "level", 1, "title", "Writing an invoice", "text", "Press Save invoice. Group A customers get a discount."),
                new JO("id", "c3", "level", 1, "title", "Backup", "text", "Nothing about masks here.") }));
        var r = Spec.Run(cfg, new List<string>());
        T.Eq(3, r.Int("steps", 0), "steps"); T.Eq(1, r.Int("manuals", 0), "manuals");
        var spec = Json.ReadFile(cfg.Sub("spec", "spec.json"));
        var inv = (JO)spec.Arr("steps")[2];
        T.Eq("New invoice", inv.Str("caption"), "step"); T.Eq(2, inv.Arr("writes").Count, "writes of the step");
        T.Eq("update", ((JO)inv.Arr("writes")[0]).Str("op"), "op"); T.Eq("id,value", string.Join(",", ((JO)inv.Arr("writes")[0]).Strings("columns").ToArray()), "columns seen");
        T.Eq("c2", ((JO)inv.Arr("manual")[0]).Str("chapter"), "manual chapter linked to the step");
        var masks = spec.Arr("masks");
        T.Eq("Shop,Customer #,New invoice", string.Join(",", masks.ConvertAll(delegate (object m) { return ((JO)m).Str("title"); }).ToArray()), "masks; numbers in window titles are neutral");
        var custMask = (JO)masks[1];
        T.Eq("Save customer", ((JO)custMask.Arr("controls")[0]).Str("text"), "control used"); T.True(custMask.Strings("tables_written").Contains("public.customer"), "table written through the mask");
        T.Eq("c1", ((JO)custMask.Arr("manual")[0]).Str("chapter"), "manual chapter linked to the mask");
        var q = spec.Arr("queries").Find(delegate (object x) { return ((JO)x).Str("verb") == "insert" && ((JO)x).Strings("tables").Contains("customer"); }) as JO;
        T.Eq("INSERT INTO customer(name) VALUES ($1)", q.Str("shape"), "statement shape"); T.Eq(1, q.Int("count", 0), "count");
        T.True(!spec.Arr("queries").Exists(delegate (object x) { string v = ((JO)x).Str("verb"); return v == "begin" || v == "commit"; }), "transaction control is not a query of the specification");
        var tbl = spec.Arr("tables").Find(delegate (object x) { return ((JO)x).Str("name") == "public.customer"; }) as JO;
        T.True(tbl != null && tbl.Strings("read_by").Count == 1 && tbl.Strings("written_by").Count == 1, "customer and public.customer are one table");
        T.True(spec.Arr("open_points").Exists(delegate (object o) { return ((string)o).Contains("duplicate key"); }), "database error listed as open point");
        string md = File.ReadAllText(cfg.Sub("spec", "spec.md"));
        T.Contains(md, "### s3 New invoice", "readable form"); T.Contains(md, "| public.customer |", "table list");
        string again = Json.Write(Spec.Build(new List<JO> { Json.ReadFile(Path.Combine(cfg.RecordingsDir, "rec", "timeline.json")) }, new List<JO> { Json.ReadFile(cfg.Sub("manual", "handbook.json")) }, Path.GetFileName(dir)));
        T.Eq(Json.Write(spec), again, "deterministic");
    }
}
}
