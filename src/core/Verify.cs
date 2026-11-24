// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// verify: comparison tests.
//   screens  two pictures or two folders of pictures, pixel by pixel with a tolerance
//   db       the measured database changes of two recordings of the same operations (the original
//            application and the rebuilt one), step by step
// Both write a report and say "equal" only when nothing differs beyond the stated rules.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker {

public static class VerifyScreens {
    static byte[] Pixels(Bitmap bmp) {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try {
            var raw = new byte[bmp.Width * bmp.Height * 3]; var row = new byte[data.Stride];
            for (int y = 0; y < bmp.Height; y++) { Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, data.Stride); Buffer.BlockCopy(row, 0, raw, y * bmp.Width * 3, bmp.Width * 3); }
            return raw;
        } finally { bmp.UnlockBits(data); }
    }

    /// <summary>Compares two pictures. tolerance: difference per colour channel (0..255) that still counts as equal.
    /// ignore: rectangles that are not compared. diffFile: picture with the differing pixels marked (may be null).</summary>
    public static JO Compare(string fileA, string fileB, int tolerance, List<Rectangle> ignore, string diffFile) {
        using (var a = new Bitmap(fileA)) using (var b = new Bitmap(fileB)) {
            var r = new JO("a", fileA, "b", fileB, "width_a", a.Width, "height_a", a.Height, "width_b", b.Width, "height_b", b.Height);
            if (a.Width != b.Width || a.Height != b.Height) { r["same_size"] = false; r["different_pixels"] = null; r["percent"] = 100.0; r["equal"] = false; return r; }
            byte[] pa = Pixels(a), pb = Pixels(b); int w = a.Width, h = a.Height; long diff = 0, compared = 0;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            byte[] mark = diffFile == null ? null : (byte[])pa.Clone();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                bool skip = false; if (ignore != null) foreach (var g in ignore) if (g.Contains(x, y)) { skip = true; break; }
                if (skip) continue;
                compared++; int i = (y * w + x) * 3;
                if (Math.Abs(pa[i] - pb[i]) > tolerance || Math.Abs(pa[i + 1] - pb[i + 1]) > tolerance || Math.Abs(pa[i + 2] - pb[i + 2]) > tolerance) {
                    diff++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (mark != null) { mark[i] = 0; mark[i + 1] = 0; mark[i + 2] = 255; }
                } else if (mark != null) { mark[i] = (byte)(200 + mark[i] / 5); mark[i + 1] = (byte)(200 + mark[i + 1] / 5); mark[i + 2] = (byte)(200 + mark[i + 2] / 5); }
            }
            r["same_size"] = true; r["compared_pixels"] = compared; r["different_pixels"] = diff; r["percent"] = compared == 0 ? 0 : Math.Round(diff * 100.0 / compared, 4);
            r["bounds_of_differences"] = diff == 0 ? null : new List<object> { minX, minY, maxX - minX + 1, maxY - minY + 1 };
            r["equal"] = diff == 0;
            if (mark != null && diff > 0) {
                using (var d = new Bitmap(w, h, PixelFormat.Format24bppRgb)) {
                    var data = d.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(mark, y * w * 3, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), w * 3);
                    d.UnlockBits(data);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diffFile)));
                    d.Save(diffFile, ImageFormat.Png);
                }
                r["diff_picture"] = diffFile;
            }
            return r;
        }
    }

    public static List<Rectangle> ParseIgnore(string s) {
        var l = new List<Rectangle>();
        if (string.IsNullOrEmpty(s)) return l;
        foreach (var part in s.Split(';')) {
            var m = Regex.Match(part.Trim(), @"^(\d+),(\d+),(\d+),(\d+)$");
            if (!m.Success) throw new Exception("--ignore expects x,y,w,h;x,y,w,h: " + part);
            l.Add(new Rectangle(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)));
        }
        return l;
    }

    /// <summary>Two files, or two folders whose pictures are paired by file name.</summary>
    public static JO Run(Config cfg, string a, string b, int tolerance, double maxPercent, string ignore, string name) {
        var pairs = new List<string[]>(); var missing = new List<object>();
        if (Directory.Exists(a) && Directory.Exists(b)) {
            var fa = new List<string>(Directory.GetFiles(a, "*.png")); fa.Sort(StringComparer.Ordinal);
            foreach (var f in fa) { string g = Path.Combine(b, Path.GetFileName(f)); if (File.Exists(g)) pairs.Add(new[] { f, g }); else missing.Add("only in A: " + Path.GetFileName(f)); }
            foreach (var g in Directory.GetFiles(b, "*.png")) if (!File.Exists(Path.Combine(a, Path.GetFileName(g)))) missing.Add("only in B: " + Path.GetFileName(g));
        } else if (File.Exists(a) && File.Exists(b)) pairs.Add(new[] { a, b });
        else throw new Exception("verify screens needs two picture files or two folders: " + a + ", " + b);
        if (string.IsNullOrEmpty(name)) name = "screens";
        string outDir = cfg.Sub("verify", name); Directory.CreateDirectory(outDir);
        var ign = ParseIgnore(ignore); var results = new List<object>(); int bad = 0;
        foreach (var p in pairs) {
            var r = Compare(p[0], p[1], tolerance, ign, Path.Combine(outDir, Path.GetFileNameWithoutExtension(p[0]) + ".diff.png"));
            bool ok = r.Bool("same_size", false) && r.Num("percent", 100) <= maxPercent;
            r["passed"] = ok; if (!ok) bad++;
            results.Add(r);
        }
        var rep = new JO("format", "eqlmaker-verify-screens", "version", DbEvent.FormatVersion, "tolerance", tolerance, "max_percent", maxPercent, "ignored", ignore, "pairs", pairs.Count, "failed", bad, "unpaired", missing,
            "passed", bad == 0 && missing.Count == 0 && pairs.Count > 0, "results", results);
        string file = Path.Combine(outDir, "report.json");
        Json.WriteFile(file, rep);
        return new JO("report", file, "pairs", pairs.Count, "failed", bad, "unpaired", missing.Count, "passed", rep.Bool("passed", false));
    }
}

public static class VerifyDb {
    static readonly Regex TimeLike = new Regex(@"^\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d");

    class StepChanges { public string Caption; public List<JO> Rows = new List<JO>(); }

    static List<StepChanges> Load(RecordingData rec) {
        var tl = Timeline.Build(rec); var list = new List<StepChanges>();
        foreach (JO ch in tl.Arr("chapters")) {
            var s = new StepChanges { Caption = ch.Str("caption") ?? "(before the first chapter)" };
            foreach (JO a in ch.Arr("actions")) foreach (JO t in a.Arr("transactions")) if (t.Arr("rows") != null) foreach (JO r in t.Arr("rows")) s.Rows.Add(r);
            list.Add(s);
        }
        foreach (JO t in tl.Arr("before_first_action")) if (t.Arr("rows") != null) { if (list.Count == 0 || list[0].Caption != "(before the first chapter)") list.Insert(0, new StepChanges { Caption = "(before the first chapter)" }); foreach (JO r in t.Arr("rows")) list[0].Rows.Add(r); }
        return list;
    }

    static bool Match(string pattern, string table, string column) {
        // "table.column", "*.column", "schema.table.column"; table names compare without schema when the pattern has none
        int dot = pattern.LastIndexOf('.');
        string pc = dot < 0 ? pattern : pattern.Substring(dot + 1), pt = dot < 0 ? "*" : pattern.Substring(0, dot);
        if (!string.Equals(pc, column, StringComparison.OrdinalIgnoreCase) && pc != "*") return false;
        if (pt == "*") return true;
        string bare = table.IndexOf('.') >= 0 ? table.Substring(table.IndexOf('.') + 1) : table;
        return string.Equals(pt, table, StringComparison.OrdinalIgnoreCase) || string.Equals(pt, bare, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Text form of a changed row in which values generated during the recording (keys of inserted
    /// rows and references to them) are replaced by the order of their appearance, ignored columns are
    /// left out, and run times are replaced by a marker.</summary>
    static string Normal(JO row, Dictionary<string, string> generated, List<string> ignore, List<string> identity, List<object> notes, string side) {
        string table = row.Str("table"), op = row.Str("op");
        var cols = row.Obj("new") ?? row.Obj("old");
        var sb = new StringBuilder(op + " " + table);
        if (cols == null) return sb.ToString();
        var keys = new List<string>(cols.Keys); keys.Sort(StringComparer.Ordinal);
        foreach (var k in keys) {
            if (ignore.Exists(delegate (string p) { return Match(p, table, k); })) continue;
            string v = cols.Str(k);
            if (v != null) {
                bool idLike = identity.Exists(delegate (string p) { return Match(p, table, k); });
                if (idLike && generated.ContainsKey(v)) v = generated[v];
                else if (TimeLike.IsMatch(v) && v.Length > 10) { v = "<time of the run>"; string n = table + "." + k + ": time stamps are compared as 'time of the run'"; if (!notes.Contains(n)) notes.Add(n); }
            }
            sb.Append(" " + k + "=" + (v == null ? "NULL" : v));
        }
        return sb.ToString();
    }

    /// <summary>Keys that were created during the recording: value of an identity column of an inserted row,
    /// mapped to "#n" by order of first appearance (per recording).</summary>
    static Dictionary<string, string> Generated(List<StepChanges> steps, List<string> keyColumns) {
        var map = new Dictionary<string, string>(); var counter = new Dictionary<string, int>();
        foreach (var s in steps) foreach (var r in s.Rows) {
            if (r.Str("op") != "insert" || r.Obj("new") == null) continue;
            string table = r.Str("table");
            foreach (var k in r.Obj("new").Keys) {
                if (!keyColumns.Exists(delegate (string p) { return Match(p, table, k); })) continue;
                string v = r.Obj("new").Str(k); if (v == null) continue;
                string key = table + "\u0001" + v;
                if (map.ContainsKey(key)) continue;
                int n; counter.TryGetValue(table, out n); counter[table] = ++n;
                map[key] = "<new " + table + " #" + n + ">";
            }
        }
        return map;
    }

    public static JO Compare(RecordingData a, RecordingData b, List<string> ignore, List<string> keyColumns, List<string> references) {
        var sa = Load(a); var sb = Load(b);
        var notes = new List<object>();
        if (a.Events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count == 0 && b.Events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count == 0)
            notes.Add("neither recording contains changed rows: the event source did not deliver them (PostgreSQL: wal_level = logical), so there is nothing to compare");
        // generated keys: "table\u0001value" -> token; references ("invoice_line.invoice_id=invoice") look the value up in the referenced table
        var ga = Generated(sa, keyColumns); var gb = Generated(sb, keyColumns);
        var steps = new List<object>(); int onlyA = 0, onlyB = 0, same = 0;
        var captions = new List<string>(); foreach (var s in sa) if (!captions.Contains(s.Caption)) captions.Add(s.Caption); foreach (var s in sb) if (!captions.Contains(s.Caption)) captions.Add(s.Caption);
        foreach (var cap in captions) {
            var la = Rows(sa, cap, ga, ignore, keyColumns, references, notes, "A"); var lb = Rows(sb, cap, gb, ignore, keyColumns, references, notes, "B");
            var inA = sa.Exists(delegate (StepChanges s) { return s.Caption == cap; }); var inB = sb.Exists(delegate (StepChanges s) { return s.Caption == cap; });
            var oa = new List<object>(); var ob = new List<object>(); var rest = new List<string>(lb); int eq = 0;
            foreach (var r in la) { int i = rest.IndexOf(r); if (i >= 0) { rest.RemoveAt(i); eq++; } else oa.Add(r); }
            foreach (var r in rest) ob.Add(r);
            onlyA += oa.Count; onlyB += ob.Count; same += eq;
            steps.Add(new JO("step", cap, "in_a", inA, "in_b", inB, "rows_a", la.Count, "rows_b", lb.Count, "equal_rows", eq, "only_in_a", oa, "only_in_b", ob, "equal", oa.Count == 0 && ob.Count == 0 && inA && inB));
        }
        bool equal = onlyA == 0 && onlyB == 0 && steps.TrueForAll(delegate (object s) { return ((JO)s).Bool("in_a", false) && ((JO)s).Bool("in_b", false); }) && same > 0;
        return new JO("format", "eqlmaker-verify-db", "version", DbEvent.FormatVersion, "a", a.Name, "b", b.Name,
            "rules", new JO("ignored_columns", ignore, "key_columns", keyColumns, "references", references,
                "explanation", "rows are compared per step as sets; keys created during a recording are compared by their order of creation; time stamps as 'time of the run'; everything else literally"),
            "equal", equal, "equal_rows", same, "only_in_a", onlyA, "only_in_b", onlyB, "steps", steps, "notes", notes);
    }

    static List<string> Rows(List<StepChanges> steps, string caption, Dictionary<string, string> gen, List<string> ignore, List<string> keyColumns, List<string> references, List<object> notes, string side) {
        var l = new List<string>();
        foreach (var s in steps) {
            if (s.Caption != caption) continue;
            foreach (var r in s.Rows) {
                string table = r.Str("table");
                // per row: which values are generated keys? own key columns look up the own table, references the named table
                var local = new Dictionary<string, string>(); var idCols = new List<string>();
                var cols = r.Obj("new") ?? r.Obj("old");
                if (cols != null) foreach (var k in cols.Keys) {
                    string v = cols.Str(k); if (v == null) continue;
                    string target = null;
                    if (keyColumns.Exists(delegate (string p) { return Match(p, table, k); })) target = table;
                    foreach (var rf in references) { var p = rf.Split('='); if (p.Length == 2 && Match(p[0], table, k)) target = Resolve(p[1], gen); }
                    if (target == null && k.EndsWith("_id", StringComparison.OrdinalIgnoreCase)) target = Resolve(k.Substring(0, k.Length - 3), gen);
                    if (target == null) continue;
                    string tok; if (gen.TryGetValue(target + "\u0001" + v, out tok)) { local[v] = tok; idCols.Add("*." + k); }
                }
                l.Add(Normal(r, local, ignore, idCols, notes, side));
            }
        }
        l.Sort(StringComparer.Ordinal);
        return l;
    }
    // "invoice" -> "public.invoice" when a generated key of that table exists
    static string Resolve(string table, Dictionary<string, string> gen) {
        foreach (var k in gen.Keys) { string t = k.Substring(0, k.IndexOf('\u0001')); if (string.Equals(t, table, StringComparison.OrdinalIgnoreCase) || t.EndsWith("." + table, StringComparison.OrdinalIgnoreCase)) return t; }
        return null;
    }

    public static string Text(JO rep) {
        var sb = new StringBuilder();
        sb.Append("Database changes: " + rep.Str("a") + " (A) against " + rep.Str("b") + " (B)\n");
        sb.Append(rep.Bool("equal", false) ? "RESULT: equal (" + rep.Int("equal_rows", 0) + " changed rows agree)\n" : "RESULT: DIFFERENT (" + rep.Int("equal_rows", 0) + " rows agree, " + rep.Int("only_in_a", 0) + " only in A, " + rep.Int("only_in_b", 0) + " only in B)\n");
        foreach (JO s in rep.Arr("steps")) {
            sb.Append("\n[" + (s.Bool("equal", false) ? "ok" : "DIFF") + "] " + s.Str("step") + ": A " + s.Int("rows_a", 0) + " rows, B " + s.Int("rows_b", 0) + " rows\n");
            if (!s.Bool("in_a", false)) sb.Append("      step missing in A\n");
            if (!s.Bool("in_b", false)) sb.Append("      step missing in B\n");
            foreach (var r in s.Arr("only_in_a")) sb.Append("      only A: " + r + "\n");
            foreach (var r in s.Arr("only_in_b")) sb.Append("      only B: " + r + "\n");
        }
        foreach (var n in rep.Arr("notes")) sb.Append("\nnote: " + n + "\n");
        return sb.ToString();
    }

    public static JO Run(Config cfg, string recA, string recB, List<string> ignore, List<string> keyColumns, List<string> references, string name) {
        var a = RecordingData.Load(cfg.RecordingDir(recA)); var b = RecordingData.Load(cfg.RecordingDir(recB));
        var v = cfg.Raw.Obj("verify") ?? new JO();
        if (ignore.Count == 0) ignore = v.Strings("ignoreColumns");
        if (keyColumns.Count == 0) keyColumns = v.Strings("keyColumns");
        if (keyColumns.Count == 0) keyColumns.Add("*.id");
        if (references.Count == 0) references = v.Strings("references");
        var rep = Compare(a, b, ignore, keyColumns, references);
        if (string.IsNullOrEmpty(name)) name = "db-" + a.Name + "-" + b.Name;
        string dir = cfg.Sub("verify", name); Directory.CreateDirectory(dir);
        Json.WriteFile(Path.Combine(dir, "report.json"), rep);
        File.WriteAllText(Path.Combine(dir, "report.txt"), Text(rep), new UTF8Encoding(false));
        return new JO("report", Path.Combine(dir, "report.json"), "readable", Path.Combine(dir, "report.txt"), "equal", rep.Bool("equal", false), "equal_rows", rep.Int("equal_rows", 0), "only_in_a", rep.Int("only_in_a", 0), "only_in_b", rep.Int("only_in_b", 0));
    }
}
}
