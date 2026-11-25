// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Statement table of a recording as tab-separated text (<name>.sql.tsv), the format that tools for
// replaying recorded statements read, and the conversion of scenario files of the earlier recorder.
//
// sql.tsv: a header line, then one line per statement:
//   offset in the video (seconds), time (local time of this machine), session, user, database, application,
//   kind (statement, execute, error, tsql-batch, tsql-rpc), statement.
// Backslash, tab and line break in the statement are written as \\, \t and \n.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EqlMaker {

public static class SqlExport {
    public const string Header = "offset\ttime\tpid\tuser\tdatabase\tapplication\tkind\tstatement\n";

    public static string Escape(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n"); }
    public static string Unescape(string s) {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++) {
            if (s[i] == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n); }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>Kind column: events seen in the statement trace of the server (connections that do not speak the
    /// PostgreSQL protocol) are tsql-batch or tsql-rpc, the others statement or execute, failures error.</summary>
    public static string KindOf(DbEvent e) {
        if (e.Kind == "error") return "error";
        if (e.Via == "trace") return e.Kind == "execute" ? "tsql-rpc" : "tsql-batch";
        return e.Kind;
    }

    public static string Tsv(List<DbEvent> events) {
        var sb = new StringBuilder(Header);
        foreach (var e in events) {
            if (e.Kind == "transaction") continue;
            string text = e.Kind == "error" ? (e.Error ?? "") + (e.Sql != null ? " -- statement: " + e.Sql : "") : e.Sql;
            DateTime local = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(e.Ts * 10).ToLocalTime();
            sb.Append((e.Offset ?? 0).ToString("0.000", CultureInfo.InvariantCulture) + "\t" + local.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "\t" + (e.Session ?? "") + "\t" + Escape(e.User) + "\t" +
                Escape(e.Database) + "\t" + Escape(e.Application) + "\t" + KindOf(e) + "\t" + Escape(text) + "\n");
        }
        return sb.ToString();
    }

    /// <summary>Writes the table of a recording; default file: &lt;recording&gt;/sql.tsv.</summary>
    public static JO Run(Config cfg, string recording, string outFile) {
        string dir = cfg.RecordingDir(recording);
        string f = Path.Combine(dir, "events.jsonl");
        if (!File.Exists(f)) throw new Exception("no events.jsonl in " + dir);
        var events = DbEvent.ReadFile(f);
        if (string.IsNullOrEmpty(outFile)) outFile = Path.Combine(dir, "sql.tsv");
        outFile = Path.GetFullPath(outFile);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        File.WriteAllText(outFile, Tsv(events), new UTF8Encoding(false));
        int n = 0; foreach (var e in events) if (e.Kind != "transaction") n++;
        return new JO("file", outFile, "statements", n);
    }

    /// <summary>Converts a scenario file of the earlier recorder: "sql": {"roles", "databases"} becomes
    /// "capture": {"roles", "databases"}; everything else is the same in both. Returns what changed.</summary>
    public static JO ConvertScenario(string file, string outFile) {
        var sc = Json.ReadFile(file);
        var res = new JO(); var changes = new List<object>();
        var nw = new JO();
        foreach (var kv in sc) {
            if (kv.Key == "sql" && !sc.Has("capture")) {
                var sql = kv.Value as JO ?? new JO();
                nw["capture"] = new JO("roles", sql.Has("roles") ? ToList(sql["roles"]) : new List<object>(), "databases", sql.Has("databases") ? ToList(sql["databases"]) : new List<object>());
                changes.Add("\"sql\" -> \"capture\" (roles and databases to capture)");
            } else nw[kv.Key] = kv.Value;
        }
        if (string.IsNullOrEmpty(outFile)) outFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFileNameWithoutExtension(file) + ".eqlmaker.json");
        outFile = Path.GetFullPath(outFile);
        Json.WriteFile(outFile, nw);
        res["file"] = outFile; res["changes"] = changes;
        if (changes.Count == 0) res["note"] = "nothing to convert: the format is the same";
        return res;
    }
    static List<object> ToList(object v) {
        var l = new List<object>(); if (v == null) return l;
        var s = v as string;
        if (s != null) { foreach (var p in s.Split(',')) if (p.Trim() != "") l.Add(p.Trim()); return l; }
        var a = v as List<object>; if (a != null) l.AddRange(a);
        return l;
    }
}
}
