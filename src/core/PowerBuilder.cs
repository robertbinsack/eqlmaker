// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Reader "powerbuilder": reads the compiled libraries (.pbd, .pbl) of PowerBuilder 5 to 9 applications
// without a PowerBuilder installation and without source code. The libraries are only read.
// It lists every object, the SQL statements, the readable texts (messages, captions), the menus, the
// DataWindow column labels and which object uses which, as a basis for the specification.
// Strings are decoded as Windows-1252. PowerBuilder 10 and later store Unicode libraries; these are
// recognised and refused.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker {

public class PbEntry {
    public string Library, Name, Kind; public int Size; public DateTime Compiled; public byte[] Data;
}

public class PbObject {
    public string Name, Kind, Library; public int Size; public DateTime Compiled;
    public List<string> Sql = new List<string>(), Texts = new List<string>(), Refs = new List<string>(), Strings = new List<string>(), Tables = new List<string>();
    public SortedDictionary<string, string> Labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public string Key { get { return Name + "." + Kind; } }
}

public static class PbLibrary {
    static readonly Encoding Ansi = Encoding.GetEncoding(1252);

    static string Ascii(byte[] b, int off, int len) {
        if (off < 0 || off + len > b.Length) return "";
        return Encoding.ASCII.GetString(b, off, len);
    }

    /// <summary>Version of the library ("0600"); throws for Unicode libraries and for files that are no library.</summary>
    public static string Version(byte[] buf, string file) {
        bool wide = buf.Length > 8 && ((buf[1] == 0 && buf[3] == 0) || (Ascii(buf, 0, 4) == "HDR*" && buf[5] == 0 && buf[7] == 0));
        if (wide) throw new InvalidDataException(file + ": PowerBuilder 10 or later (Unicode library) is not supported");
        if (Ascii(buf, 0, 4) != "HDR*") throw new InvalidDataException(file + ": not a PowerBuilder library (no HDR* header)");
        if (Ascii(buf, 4, 12) != "PowerBuilder") throw new InvalidDataException(file + ": unknown library header");
        return Ascii(buf, 18, 4);
    }

    public static List<PbEntry> Read(string file, byte[] buf) {
        Version(buf, file);
        string lib = Path.GetFileNameWithoutExtension(file);
        var list = new List<PbEntry>();
        for (int off = 0; off + 4 <= buf.Length; off += 512) {
            if (Ascii(buf, off, 4) != "NOD*") continue;
            int p = off + 32, end = Math.Min(off + 3072, buf.Length);
            while (p + 24 < end && Ascii(buf, p, 4) == "ENT*") {
                int dat = BitConverter.ToInt32(buf, p + 8);
                int size = BitConverter.ToInt32(buf, p + 12);
                uint ts = BitConverter.ToUInt32(buf, p + 16);
                int nlen = BitConverter.ToUInt16(buf, p + 22);
                if (p + 24 + nlen > buf.Length) break;
                string name = Ansi.GetString(buf, p + 24, nlen).TrimEnd('\0');
                var e = new PbEntry();
                e.Library = lib; e.Size = size; e.Compiled = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts);
                int dot = name.LastIndexOf('.');
                e.Name = dot > 0 ? name.Substring(0, dot) : name;
                e.Kind = dot > 0 ? name.Substring(dot + 1) : "";
                e.Data = Chain(buf, dat);
                list.Add(e);
                p += 24 + nlen;
            }
            off += 3072 - 512;
        }
        return list;
    }

    // joins the DAT* blocks of an entry: DAT* | next (4) | length (2) | data
    static byte[] Chain(byte[] buf, int b) {
        var ms = new MemoryStream();
        int guard = 0;
        while (b > 0 && b + 10 <= buf.Length && guard++ < 1000000) {
            if (Ascii(buf, b, 4) != "DAT*") break;
            int next = BitConverter.ToInt32(buf, b + 4);
            int len = BitConverter.ToUInt16(buf, b + 8);
            if (b + 10 + len > buf.Length) len = buf.Length - b - 10;
            ms.Write(buf, b + 10, len);
            b = next;
        }
        return ms.ToArray();
    }

    static bool Printable(byte c) {
        return (c >= 0x20 && c < 0x7f) || c == 0x0d || c == 0x0a || c == 0x09 || c >= 0xc0 || c == 0xa7 || c == 0xb0 || c == 0x80;
    }

    // runtime names that occur in every object and carry no information
    static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.Ordinal) {
        "function_object", "structure", "mailrecipient", "environment", "mailfiledescription", "mailmessage",
        "datawindowchild", "listviewitem", "treeviewitem", "connectioninfo", "powerobject", "pointer",
        "transaction", "sqlca", "MS Sans Serif", "[general]", "IBeam!", "Arrow!", "Ibeam!", "UpArrow!", "pdw" };
    static readonly Regex Short = new Regex(@"^[\x20-\x7e]{1,2}$");
    static readonly Regex Marker = new Regex(@"^.@.@$|^PDW\d{4}$|^[^\p{L}\p{N}\x20-\x7e]+$");

    /// <summary>Readable strings (at least 3 characters) in their order, trimmed, without duplicates and noise.</summary>
    public static List<string> Strings(byte[] data) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int start = -1;
        for (int i = 0; i <= data.Length; i++) {
            bool ok = i < data.Length && Printable(data[i]);
            if (ok) { if (start < 0) start = i; continue; }
            if (start >= 0 && i - start >= 3) {
                string t = Ansi.GetString(data, start, i - start).Replace("\r\n", "\n").Trim();
                if (t.Length > 0 && !Noise.Contains(t) && !Short.IsMatch(t) && !Marker.IsMatch(t) && seen.Add(t)) result.Add(t);
            }
            start = -1;
        }
        return result;
    }

    static readonly Regex Sql = new Regex(@"^\s*(SELECT|INSERT|UPDATE|DELETE|DECLARE|PBSELECT|CREATE|ALTER|DROP|CALL|EXECUTE)\b", RegexOptions.IgnoreCase);
    static readonly Regex Text = new Regex(@"\p{L}{3,}\s+\p{L}{2,}");
    static readonly Regex Ident = new Regex(@"[A-Za-z_][A-Za-z0-9_$]*");
    static readonly Regex Label = new Regex(@"^[a-z0-9_]+_t$");
    static readonly Regex Plain = new Regex(@"^[a-z0-9_]+$|^\d+$");
    static readonly Regex Accel = new Regex(@"&\p{L}");

    public static bool IsSql(string s) { return Sql.IsMatch(s); }
    /// <summary>Menu item captions carry an accelerator ("&amp;File", "E&amp;xit"); "&amp;&amp;" is a literal ampersand.</summary>
    public static bool IsCaption(string s) { return Accel.IsMatch(s.Replace("&&", "")) && s.IndexOf('=') < 0 && s.IndexOf('~') < 0 && s.IndexOf('`') < 0 && !Sql.IsMatch(s); }
    /// <summary>Caption without accelerator mark and without the key hint after a tab.</summary>
    public static string CaptionText(string s) { int tab = s.IndexOf('\t'); if (tab >= 0) s = s.Substring(0, tab); return s.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim(); }
    public static bool IsText(string s) { return Text.IsMatch(s) && s.IndexOf('=') < 0 && s.IndexOf('~') < 0 && !Sql.IsMatch(s); }

    public static List<string> Refs(List<string> strings, Dictionary<string, string> names, string self) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = new List<string>();
        foreach (string s in strings)
            foreach (Match m in Ident.Matches(s)) {
                string n;
                if (names.TryGetValue(m.Value.ToLowerInvariant(), out n) && !string.Equals(n, self, StringComparison.OrdinalIgnoreCase) && seen.Add(n)) r.Add(n);
            }
        return r;
    }

    /// <summary>DataWindow header texts: "&lt;column&gt;_t" followed (within 3 strings) by its text.</summary>
    public static SortedDictionary<string, string> Labels(List<string> s) {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < s.Count - 1; i++) {
            if (!Label.IsMatch(s[i])) continue;
            for (int j = i + 1; j < Math.Min(s.Count, i + 4); j++) {
                string t = s[j];
                if (Regex.IsMatch(t, @"\p{L}") && !Plain.IsMatch(t)) { string k = s[i].Substring(0, s[i].Length - 2); if (!d.ContainsKey(k)) d[k] = t; break; }
            }
        }
        return d;
    }

    static List<string> All(string s, string rx, Func<Match, string> f) { var l = new List<string>(); foreach (Match m in Regex.Matches(s, rx)) l.Add(f(m)); return l; }

    /// <summary>Tables of a DataWindow PBSELECT.</summary>
    public static List<string> PbSelectTables(string s) { return All(s, "TABLE\\(NAME=\"([^\"]+)\"", delegate (Match m) { return m.Groups[1].Value; }); }

    /// <summary>Readable form of a DataWindow PBSELECT: FROM ... COLS ... JOIN ... WHERE ... ARGS ... ORDER ...</summary>
    public static string PbSelect(string s) {
        var t = PbSelectTables(s);
        var c = All(s, "(?:COLUMN|COMPUTE)\\(NAME=\"([^\"]+)\"", delegate (Match m) { return m.Groups[1].Value; });
        var j = All(s, "JOIN \\(LEFT=\"([^\"]+)\"\\s+OP =\"([^\"]+)\"RIGHT=\"([^\"]+)\"", delegate (Match m) { return m.Groups[1].Value + " " + m.Groups[2].Value + " " + m.Groups[3].Value; });
        var w = All(s, "EXP1 =\"([^\"]*)\"\\s+OP =\"([^\"]*)\"\\s+EXP2 =\"([^\"]*)\"", delegate (Match m) { return Regex.Replace((m.Groups[1].Value + " " + m.Groups[2].Value + " " + m.Groups[3].Value).Replace("~\"", "\""), @"\s+", " "); });
        var a = All(s, "ARG\\(NAME = \"([^\"]+)\" TYPE = (\\w+)\\)", delegate (Match m) { return m.Groups[1].Value + ":" + m.Groups[2].Value; });
        var o = All(s, "ORDER\\(NAME=\"([^\"]+)\" ASC=(\\w+)", delegate (Match m) { return m.Groups[1].Value + (m.Groups[2].Value == "no" ? " DESC" : ""); });
        var sb = new StringBuilder("FROM " + string.Join(", ", t.ToArray()) + " COLS " + string.Join(", ", c.ToArray()));
        if (j.Count > 0) sb.Append(" JOIN " + string.Join(" AND ", j.ToArray()));
        if (w.Count > 0) sb.Append(" WHERE " + string.Join(" | ", w.ToArray()));
        if (a.Count > 0) sb.Append(" ARGS " + string.Join(", ", a.ToArray()));
        if (o.Count > 0) sb.Append(" ORDER " + string.Join(", ", o.ToArray()));
        return sb.ToString();
    }

    public static string OneLine(string s) { return Regex.Replace(s, @"\s+", " ").Trim(); }
    public static string SqlLine(string q) { return Regex.IsMatch(q, @"^\s*PBSELECT") ? PbSelect(q) : OneLine(q); }
}

public static class PowerBuilderReader {
    public const string Name = "powerbuilder";

    static string Safe(string s) { return Regex.Replace(s, @"[^\w.$-]", "_"); }
    static List<object> Objs(IEnumerable<string> l) { var r = new List<object>(); foreach (var s in l) r.Add(s); return r; }

    /// <summary>Reads libraries (.pbd/.pbl files or folders with them) and writes the result into outDir:
    /// reader.json (the format other readers write too), sql.tsv, summary.md, strings\, objects\ (raw data,
    /// unless noRaw) and bundles\ for the named start objects. Libraries are given in the order of the
    /// library list of the application: for a name in several libraries the first one wins.</summary>
    public static JO Run(List<string> paths, string outDir, List<string> bundles, int maxDepth, string exclude, bool noRaw) {
        var utf8 = new UTF8Encoding(false);
        var files = new List<string>();
        foreach (var raw in paths) {
            foreach (var p in raw.Split(',')) {
                if (p.Trim() == "") continue;
                if (Directory.Exists(p)) {
                    var fs = new List<string>(Directory.GetFiles(p)); fs.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in fs) { string x = Path.GetExtension(f).ToLowerInvariant(); if (x == ".pbd" || x == ".pbl") files.Add(f); }
                } else if (File.Exists(p)) files.Add(p);
                else throw new Exception("not found: " + p);
            }
        }
        if (files.Count == 0) throw new Exception("no .pbd or .pbl files found");
        Directory.CreateDirectory(outDir);
        string strDir = Path.Combine(outDir, "strings"); Directory.CreateDirectory(strDir);

        // 1. all libraries
        var entries = new List<PbEntry>(); var libs = new List<JO>();
        foreach (var f in files) {
            byte[] buf = File.ReadAllBytes(f);
            string ver = PbLibrary.Version(buf, f);
            var list = PbLibrary.Read(f, buf);
            libs.Add(new JO("file", Path.GetFileName(f), "library", Path.GetFileNameWithoutExtension(f), "version", ver, "entries", list.Count));
            if (!noRaw) {
                string rawDir = Path.Combine(outDir, "objects", Path.GetFileNameWithoutExtension(f)); Directory.CreateDirectory(rawDir);
                var idx = new StringBuilder();
                list.Sort(delegate (PbEntry a, PbEntry b) { int c = string.CompareOrdinal(a.Name, b.Name); return c != 0 ? c : string.CompareOrdinal(a.Kind, b.Kind); });
                foreach (var e in list) {
                    File.WriteAllBytes(Path.Combine(rawDir, Safe(e.Name + "." + e.Kind)), e.Data);
                    idx.Append(e.Name + "." + e.Kind + "\t" + e.Size + "\t" + e.Data.Length + "\t" + e.Compiled.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
                }
                File.WriteAllText(Path.Combine(rawDir, "_index.tsv"), idx.ToString(), utf8);
            }
            entries.AddRange(list);
        }

        // 2. objects: names first (for references), then strings, SQL, texts and labels of each object
        var names = new Dictionary<string, string>();
        foreach (var e in entries) { string k = e.Name.ToLowerInvariant(); if (e.Kind != "" && !names.ContainsKey(k)) names[k] = e.Name; }
        var objects = new List<PbObject>(); var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var sqlTsv = new StringBuilder("object\tkind\tstatement\n");
        foreach (var e in entries) {
            string key = e.Name + "." + e.Kind;
            if (e.Kind == "" || !seenKeys.Add(key)) continue;
            var s = PbLibrary.Strings(e.Data);
            File.WriteAllText(Path.Combine(strDir, Safe(key) + ".txt"), string.Join("\n", s.ToArray()), utf8);
            var o = new PbObject { Name = e.Name, Kind = e.Kind, Library = e.Library, Size = e.Size, Compiled = e.Compiled };
            foreach (var x in s) { if (PbLibrary.IsSql(x)) o.Sql.Add(x); if (PbLibrary.IsText(x)) o.Texts.Add(x); }
            o.Refs = PbLibrary.Refs(s, names, e.Name); o.Labels = PbLibrary.Labels(s);
            if (e.Kind == "men") o.Strings = s;
            var tables = new List<string>();
            foreach (var q in o.Sql) {
                var ts = Regex.IsMatch(q, @"^\s*PBSELECT") ? PbLibrary.PbSelectTables(q) : SqlText.Tables(q);
                foreach (var t in ts) if (!tables.Contains(t)) tables.Add(t);
                sqlTsv.Append(e.Name + "\t" + e.Kind + "\t" + PbLibrary.SqlLine(q).Replace("\t", " ") + "\n");
            }
            o.Tables = tables;
            objects.Add(o);
        }
        File.WriteAllText(Path.Combine(outDir, "sql.tsv"), sqlTsv.ToString(), utf8);

        // 3. menus: captions with an accelerator, as the menu shows them
        var menus = new List<JO>();
        foreach (var o in objects) {
            if (o.Kind != "men") continue;
            var items = new List<object>();
            foreach (var x in o.Strings) { if (!PbLibrary.IsCaption(x)) continue; string c = PbLibrary.CaptionText(x); if (Regex.IsMatch(c, @"\p{L}{2}")) items.Add(c); }
            menus.Add(new JO("name", o.Name, "library", o.Library, "items", items));
        }

        // 4. reader.json: the neutral format that "spec" reads
        int sqlCount = 0; var kinds = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var ol = new List<object>();
        foreach (var o in objects) {
            sqlCount += o.Sql.Count;
            int[] k; if (!kinds.TryGetValue(o.Kind, out k)) { k = new int[2]; kinds[o.Kind] = k; }
            k[0]++; k[1] += o.Sql.Count;
            var lab = new JO(); foreach (var kv in o.Labels) lab[kv.Key] = kv.Value;
            var sqlList = new List<object>(); foreach (var q in o.Sql) sqlList.Add(PbLibrary.SqlLine(q));
            ol.Add(new JO("name", o.Name, "kind", o.Kind, "library", o.Library, "compiled", o.Compiled.ToString("yyyy-MM-ddTHH:mm:ssZ"), "sql", sqlList, "tables", Objs(o.Tables), "refs", Objs(o.Refs), "texts", Objs(o.Texts), "labels", lab));
        }
        var ll = new List<object>(); foreach (var l in libs) ll.Add(l);
        var ml = new List<object>(); foreach (var m in menus) ml.Add(m);
        Json.WriteFile(Path.Combine(outDir, "reader.json"), new JO("format", "eqlmaker-static", "version", "0.1", "reader", Name, "libraries", ll, "menus", ml,
            "counts", new JO("objects", objects.Count, "sql", sqlCount, "menus", menus.Count), "objects", ol));

        // 5. summary.md
        var md = new StringBuilder("# PowerBuilder libraries\n\n| Library | Version | Entries | File |\n|---|---|---|---|\n");
        foreach (var l in libs) md.Append("| " + l.Str("library") + " | " + l.Str("version") + " | " + l.Int("entries", 0) + " | " + l.Str("file") + " |\n");
        md.Append("\n## Objects per kind\n\n| Kind | Objects | SQL statements |\n|---|---|---|\n");
        foreach (var kv in kinds) md.Append("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " |\n");
        md.Append("\nTotal: " + objects.Count + " objects, " + sqlCount + " SQL statements.\n");
        foreach (var m in menus) md.Append("\n## Menu " + m.Str("name") + "\n\n" + m.Arr("items").Count + " items: " + string.Join(" | ", m.Strings("items").ToArray()) + "\n");
        File.WriteAllText(Path.Combine(outDir, "summary.md"), md.ToString(), utf8);

        // 6. bundles: start object and the objects it uses, up to maxDepth levels
        int bundleCount = 0;
        if (bundles != null && bundles.Count > 0) {
            string bDir = Path.Combine(outDir, "bundles"); Directory.CreateDirectory(bDir);
            var byName = new Dictionary<string, List<PbObject>>();
            foreach (var o in objects) { string k = o.Name.ToLowerInvariant(); if (!byName.ContainsKey(k)) byName[k] = new List<PbObject>(); byName[k].Add(o); }
            // objects used by many others (error handling, base classes) are named but not expanded
            var inbound = new Dictionary<string, int>();
            foreach (var o in objects) foreach (var r in o.Refs) { string k = r.ToLowerInvariant(); int n; inbound.TryGetValue(k, out n); inbound[k] = n + 1; }
            int hubLimit = Math.Max(10, (int)(objects.Count * 0.02));
            Regex ex = string.IsNullOrEmpty(exclude) ? null : new Regex(exclude, RegexOptions.IgnoreCase);
            foreach (var start in bundles) {
                if (!byName.ContainsKey(start.ToLowerInvariant())) continue;
                var seen = new HashSet<string>(); var order = new List<PbObject>(); var shared = new List<string>();
                var queue = new Queue<KeyValuePair<string, int>>(); queue.Enqueue(new KeyValuePair<string, int>(start, 0));
                while (queue.Count > 0) {
                    var item = queue.Dequeue(); string n = item.Key.ToLowerInvariant(); int d = item.Value;
                    if (seen.Contains(n) || !byName.ContainsKey(n)) continue;
                    if (d > 0 && ex != null && ex.IsMatch(n)) continue;
                    int inb; inbound.TryGetValue(n, out inb);
                    if (d > 0 && inb >= hubLimit) { if (!shared.Contains(item.Key)) shared.Add(item.Key); continue; }
                    seen.Add(n);
                    foreach (var o in byName[n]) { order.Add(o); if (d < maxDepth) foreach (var r in o.Refs) queue.Enqueue(new KeyValuePair<string, int>(r, d + 1)); }
                }
                var b = new StringBuilder("# " + start + "\n\n" + order.Count + " objects (references up to " + maxDepth + " levels)\n");
                if (shared.Count > 0) b.Append("Shared objects (referenced by " + hubLimit + " or more objects, not expanded): " + string.Join(", ", shared.ToArray()) + "\n");
                foreach (var o in order) {
                    b.Append("\n## " + o.Name + " (" + o.Kind + ", " + o.Library + ")\n");
                    if (o.Labels.Count > 0) { var lp = new List<string>(); foreach (var kv in o.Labels) lp.Add(kv.Key + "=\"" + kv.Value + "\""); b.Append("labels: " + string.Join("; ", lp.ToArray()) + "\n"); }
                    foreach (var q in o.Sql) b.Append("- " + PbLibrary.SqlLine(q) + "\n");
                    var t = new List<string>(); foreach (var x in o.Texts) { if (t.Count >= 60) break; t.Add(PbLibrary.OneLine(x)); }
                    if (t.Count > 0) b.Append("texts: " + string.Join(" | ", t.ToArray()) + "\n");
                    if (o.Refs.Count > 0) b.Append("refs: " + string.Join(", ", o.Refs.ToArray()) + "\n");
                }
                File.WriteAllText(Path.Combine(bDir, Safe(start) + ".md"), b.ToString(), utf8);
                bundleCount++;
            }
        }
        return new JO("reader", Name, "libraries", libs.Count, "objects", objects.Count, "sql", sqlCount, "menus", menus.Count, "bundles", bundleCount, "out", outDir, "result", Path.Combine(outDir, "reader.json"));
    }
}
}
