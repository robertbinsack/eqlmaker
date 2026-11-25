// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Statement table of a recording (sql.tsv), conversion of scenario files of the earlier recorder, capture
// settings of a scenario.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EqlMaker.Tests {

public static class ExportTests {
    static List<DbEvent> Sample() {
        long t = 1795630000000000L;
        return new List<DbEvent> {
            new DbEvent { Ts = t, Offset = 0.5, Kind = "statement", Source = "postgres", Via = "log", Session = "4711", User = "app", Database = "shop", Application = "client", Sql = "SELECT 1\nFROM t\twhere x = 'a\\b'" },
            new DbEvent { Ts = t + 1000, Offset = 0.501, Kind = "execute", Source = "postgres", Via = "log", Session = "4711", User = "app", Database = "shop", Application = "client", Sql = "INSERT INTO t VALUES ($1)", Params = new List<string> { "1" } },
            new DbEvent { Ts = t + 2000, Offset = 1.25, Kind = "statement", Source = "eqlserver", Via = "trace", Session = "52", User = "tsql_user", Database = "shop", Application = "tds client", Sql = "UPDATE dbo.t SET a = 1" },
            new DbEvent { Ts = t + 3000, Offset = 1.3, Kind = "execute", Source = "eqlserver", Via = "trace", Session = "52", User = "tsql_user", Database = "shop", Application = "tds client", Sql = "exec sp_executesql N'SELECT @p', N'@p int'" },
            new DbEvent { Ts = t + 4000, Offset = 1.4, Kind = "error", Source = "postgres", Via = "log", Session = "4711", User = "app", Database = "shop", Error = "relation \"x\" does not exist", Sql = "SELECT * FROM x" },
            new DbEvent { Ts = t + 5000, Offset = 1.5, Kind = "transaction", Source = "postgres", Via = "replication", Tx = "9", Tables = new List<string> { "public.t" } } };
    }

    /// <summary>The columns and escapes that tools for replaying statements read.</summary>
    public static void SqlTsvHasTheColumnsKindsAndEscapesOfTheReplayFormat() {
        string tsv = SqlExport.Tsv(Sample());
        var lines = tsv.TrimEnd('\n').Split('\n');
        T.Eq("offset\ttime\tpid\tuser\tdatabase\tapplication\tkind\tstatement", lines[0], "header");
        T.Eq(6, lines.Length, "five statements and the header (transactions are not statements)");
        var c = lines[1].Split('\t');
        T.Eq(8, c.Length, "eight columns: tabs inside the statement are escaped"); T.Eq("0.500", c[0], "offset"); T.Eq("4711", c[2], "session"); T.Eq("app", c[3], "user"); T.Eq("shop", c[4], "database"); T.Eq("client", c[5], "application"); T.Eq("statement", c[6], "kind");
        T.Eq("SELECT 1\nFROM t\twhere x = 'a\\b'", SqlExport.Unescape(c[7]), "statement round trip with line break, tab and backslash");
        T.True(c[1].Length == 23 && c[1][4] == '-' && c[1][10] == ' ' && c[1][19] == '.', "time as yyyy-MM-dd HH:mm:ss.fff: " + c[1]);
        T.Eq("execute", lines[2].Split('\t')[6], "execute over the PostgreSQL protocol");
        T.Eq("tsql-batch", lines[3].Split('\t')[6], "batch seen in the statement trace of the server");
        T.Eq("tsql-rpc", lines[4].Split('\t')[6], "RPC call seen in the statement trace of the server");
        var e = lines[5].Split('\t'); T.Eq("error", e[6], "error"); T.Contains(SqlExport.Unescape(e[7]), "does not exist", "message of the error");
    }

    public static void ExportSqlCommandWritesTheTableOfAnExistingRecording() {
        string d = T.TempDir("export-sql");
        string rec = Path.Combine(d, "recordings", "r1"); Directory.CreateDirectory(rec);
        DbEvent.WriteFile(Path.Combine(rec, "events.jsonl"), Sample());
        Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres"));
        string o;
        T.Eq(0, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "export-sql r1 --project \"" + d + "\"", d, out o, 30000), "export-sql: " + o);
        string tsv = File.ReadAllText(Path.Combine(rec, "sql.tsv"), Encoding.UTF8);
        T.Eq(5, tsv.TrimEnd('\n').Split('\n').Length - 1, "five statements"); T.Contains(tsv, "tsql-rpc", "kinds"); T.True(tsv.IndexOf('\r') < 0, "line breaks are \\n");
        string custom = Path.Combine(d, "x", "order-entry.sql.tsv");
        T.Eq(0, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "export-sql r1 --out \"" + custom + "\" --project \"" + d + "\"", d, out o, 30000), "with --out: " + o);
        T.True(File.Exists(custom), "file written where asked");
        T.Eq(1, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "export-sql nothing --project \"" + d + "\"", d, out o, 30000), "unknown recording fails");
    }

    public static void ScenarioOfTheEarlierRecorderIsConverted() {
        string d = T.TempDir("convert-scenario"), f = Path.Combine(d, "new-owner.json"), o;
        File.WriteAllText(f, "{\"name\":\"new-owner\",\"region\":\"workarea\",\"fps\":15,\"sql\":{\"roles\":[\"app_user\"],\"databases\":\"shop,crm\"},\"steps\":[{\"caption\":\"Start\"},{\"webmove\":{\"css\":\"button\"}},{\"wait\":100}]}");
        T.Eq(0, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "convert-scenario \"" + f + "\" --json", d, out o, 30000), "convert: " + o);
        var nw = Json.ReadFile(Path.Combine(d, "new-owner.eqlmaker.json"));
        T.True(!nw.Has("sql") && nw.Obj("capture") != null, "sql became capture");
        T.Eq("app_user", string.Join(",", nw.Obj("capture").Strings("roles").ToArray()), "roles"); T.Eq("shop,crm", string.Join(",", nw.Obj("capture").Strings("databases").ToArray()), "databases (a comma separated text becomes a list)");
        T.Eq(3, nw.Arr("steps").Count, "steps unchanged"); T.Eq("new-owner", nw.Str("name"), "other keys kept"); T.Eq("name,region,fps,capture,steps", string.Join(",", new List<string>(nw.Keys).ToArray()), "order of the keys kept");
        // converting a file that needs nothing says so
        var again = SqlExport.ConvertScenario(Path.Combine(d, "new-owner.eqlmaker.json"), Path.Combine(d, "again.json"));
        T.Contains(again.Str("note"), "nothing to convert", "already converted");
    }

    public static void ScenarioCaptureObjectReplacesRolesAndDatabasesOfTheConfiguration() {
        var cfg = new Config(); cfg.Dir = T.TempDir("capture-override");
        cfg.Raw = new JO("source", "postgres", "connection", "host=x", "capture", new JO("roles", new List<object> { "configured" }, "databases", new List<object>(), "changes", true));
        var o = cfg.Capture();
        T.Eq("configured", string.Join(",", o.Roles.ToArray()), "from the configuration");
        o = cfg.Capture(new JO("roles", new List<object> { "a", "b" }, "databases", new List<object> { "d" }));
        T.Eq("a,b", string.Join(",", o.Roles.ToArray()), "roles of the scenario"); T.Eq("d", string.Join(",", o.Databases.ToArray()), "databases of the scenario"); T.True(o.Changes, "other settings stay");
        o = cfg.Capture(new JO());
        T.Eq("configured", string.Join(",", o.Roles.ToArray()), "an empty object changes nothing");
    }
}
}
