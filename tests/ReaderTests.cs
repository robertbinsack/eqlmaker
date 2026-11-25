// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Readers: the built-in PowerBuilder reader with a synthetic library built here (no files of any real
// application), the reader plugin point and the part "static" of the specification.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EqlMaker.Tests {

public static class ReaderTests {
    static readonly Encoding Ansi = Encoding.GetEncoding(1252);

    class Obj { public string Name; public byte[] Data; }

    static Obj MakeObject(string name, string[] strings, int pad) {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 1, 2, 0, 7 }, 0, 4);
        foreach (var s in strings) { var b = Ansi.GetBytes(s); ms.Write(b, 0, b.Length); ms.Write(new byte[] { 0, 3, 0 }, 0, 3); }
        if (pad > 0) ms.Write(new byte[pad], 0, pad);
        return new Obj { Name = name, Data = ms.ToArray() };
    }

    /// <summary>A library of PowerBuilder 6 layout: header block, one node block with the entries, data blocks of
    /// 512 bytes (DAT*, next block, length, up to 502 bytes of data).</summary>
    static byte[] Library(List<Obj> objs, bool unicode) {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        var hdr = new byte[1024];
        byte[] h;
        if (unicode) { var a = Encoding.ASCII.GetBytes("HDR*"); var u = Encoding.Unicode.GetBytes("PowerBuilder"); h = new byte[a.Length + u.Length]; Array.Copy(a, h, a.Length); Array.Copy(u, 0, h, a.Length, u.Length); }
        else h = Encoding.ASCII.GetBytes("HDR*PowerBuilder\0\00600");
        Array.Copy(h, hdr, h.Length);
        w.Write(hdr);
        w.Write(new byte[3072]);
        var ents = new List<int[]>(); var names = new List<string>();
        foreach (var o in objs) {
            int first = 0, prevNextPos = -1, pos = 0;
            do {
                int len = Math.Min(502, o.Data.Length - pos), blk = (int)ms.Position;
                if (prevNextPos >= 0) { long cur = ms.Position; ms.Position = prevNextPos; w.Write(blk); ms.Position = cur; } else first = blk;
                w.Write(Encoding.ASCII.GetBytes("DAT*")); prevNextPos = (int)ms.Position; w.Write(0); w.Write((ushort)len);
                w.Write(o.Data, pos, len); w.Write(new byte[502 - len]);
                pos += len;
            } while (pos < o.Data.Length);
            ents.Add(new[] { first, o.Data.Length }); names.Add(o.Name);
        }
        ms.Position = 1024;
        w.Write(Encoding.ASCII.GetBytes("NOD*")); w.Write(new byte[28]);
        for (int i = 0; i < ents.Count; i++) {
            var nb = Ansi.GetBytes(names[i] + "\0");
            w.Write(Encoding.ASCII.GetBytes("ENT*0600")); w.Write(ents[i][0]); w.Write(ents[i][1]); w.Write((uint)1700000000); w.Write((ushort)0); w.Write((ushort)nb.Length); w.Write(nb);
        }
        return ms.ToArray();
    }

    static List<Obj> SampleObjects() {
        return new List<Obj> {
            MakeObject("w_customer.win", new[] { "w_customer", "d_customer", "f_error", "SELECT name FROM customer WHERE id = :id", "Der Kunde wurde gespeichert.", "Preis in €" }, 0),
            MakeObject("d_customer.dwo", new[] { "release 6;", "name_t", "Kundenname", "city_t", "Ort",
                "PBSELECT( VERSION(400) TABLE(NAME=\"customer\" ) COLUMN(NAME=\"customer.name\") COLUMN(NAME=\"customer.city\")WHERE(    EXP1 =\"customer.id\"   OP =\"=\"    EXP2 =\":id\" ) ) ARG(NAME = \"id\" TYPE = number)" }, 1200),
            MakeObject("f_error.fun", new[] { "f_error", "Ein Fehler ist aufgetreten" }, 0),
            MakeObject("m_main.men", new[] { "m_main", "&Datei", "&Kunden verwalten", "Be&enden", "w_customer", "Hilfe&themen\tF1" }, 0) };
    }

    static string Make(string name, List<Obj> objs, bool unicode) {
        string f = Path.Combine(T.TempDir(name), "app.pbd");
        File.WriteAllBytes(f, Library(objs, unicode));
        return f;
    }

    public static void PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences() {
        var objs = SampleObjects();
        string pbd = Make("pb-read", objs, false), outDir = Path.Combine(Path.GetDirectoryName(pbd), "out");
        var r = PowerBuilderReader.Run(new List<string> { pbd }, outDir, new List<string> { "w_customer" }, 3, null, false);
        T.Eq(4, r.Int("objects", 0), "four objects"); T.Eq(2, r.Int("sql", 0), "two SQL statements"); T.Eq(1, r.Int("menus", 0), "one menu"); T.Eq(1, r.Int("bundles", 0), "one bundle");
        var doc = Json.ReadFile(Path.Combine(outDir, "reader.json"));
        T.Eq(null, Readers.Check(doc), "reader.json has the format of a reader result");
        T.Eq("0600", doc.Arr("libraries")[0] is JO ? ((JO)doc.Arr("libraries")[0]).Str("version") : "", "library version");
        JO win = null, dw = null;
        foreach (JO o in doc.Arr("objects")) { if (o.Str("name") == "w_customer") win = o; if (o.Str("name") == "d_customer") dw = o; }
        T.True(win != null && dw != null, "window and data window found");
        T.Eq("d_customer,f_error", string.Join(",", win.Strings("refs").ToArray()), "references to other objects");
        T.True(win.Strings("texts").Contains("Der Kunde wurde gespeichert."), "message text");
        T.Eq("customer", string.Join(",", win.Strings("tables").ToArray()), "table of the embedded SQL");
        T.Eq("customer", string.Join(",", dw.Strings("tables").ToArray()), "table of the PBSELECT");
        T.Eq("Kundenname", dw.Obj("labels").Str("name"), "DataWindow label"); T.Eq("Ort", dw.Obj("labels").Str("city"), "DataWindow label");
        var raw = File.ReadAllBytes(Path.Combine(outDir, "objects", "app", "d_customer.dwo"));
        T.Eq(Convert.ToBase64String(objs[1].Data), Convert.ToBase64String(raw), "raw data over several blocks is byte-identical");
        T.True(File.ReadAllText(Path.Combine(outDir, "strings", "w_customer.win.txt"), Encoding.UTF8).Contains("Preis in €"), "Windows-1252 text is written as UTF-8");
        string sql = File.ReadAllText(Path.Combine(outDir, "sql.tsv"), Encoding.UTF8);
        T.Contains(sql, "FROM customer COLS customer.name, customer.city WHERE customer.id = :id ARGS id:number", "readable PBSELECT");
        string sum = File.ReadAllText(Path.Combine(outDir, "summary.md"), Encoding.UTF8);
        T.Contains(sum, "4 items: Datei | Kunden verwalten | Beenden | Hilfethemen", "menu captions without accelerator marks and key hints");
        string b = File.ReadAllText(Path.Combine(outDir, "bundles", "w_customer.md"), Encoding.UTF8);
        T.True(b.Contains("## d_customer (dwo, app)") && b.Contains("## f_error (fun, app)"), "the bundle follows the references");
    }

    public static void PowerBuilderReaderRefusesUnicodeLibrariesAndOtherFiles() {
        string uni = Make("pb-unicode", new List<Obj> { MakeObject("x.win", new[] { "x" }, 0) }, true);
        T.Throws(delegate { PowerBuilderReader.Run(new List<string> { uni }, Path.Combine(Path.GetDirectoryName(uni), "o"), null, 3, null, true); }, "Unicode", "PowerBuilder 10 and later");
        string txt = Path.Combine(Path.GetDirectoryName(uni), "not.pbd"); File.WriteAllText(txt, "this is not a library");
        T.Throws(delegate { PowerBuilderReader.Run(new List<string> { txt }, Path.Combine(Path.GetDirectoryName(uni), "o2"), null, 3, null, true); }, "not a PowerBuilder library", "another file");
        T.Throws(delegate { PowerBuilderReader.Run(new List<string> { Path.Combine(Path.GetDirectoryName(uni), "missing.pbd") }, Path.Combine(Path.GetDirectoryName(uni), "o3"), null, 3, null, true); }, "not found", "missing file");
    }

    public static void FirstLibraryWinsForAnObjectNameThatOccursTwice() {
        string d = T.TempDir("pb-order");
        File.WriteAllBytes(Path.Combine(d, "a.pbd"), Library(new List<Obj> { MakeObject("w_x.win", new[] { "w_x", "SELECT a FROM first_table" }, 0) }, false));
        File.WriteAllBytes(Path.Combine(d, "b.pbd"), Library(new List<Obj> { MakeObject("w_x.win", new[] { "w_x", "SELECT a FROM second_table" }, 0) }, false));
        var r = PowerBuilderReader.Run(new List<string> { Path.Combine(d, "a.pbd") + "," + Path.Combine(d, "b.pbd") }, Path.Combine(d, "o"), null, 3, null, true);
        T.Eq(1, r.Int("objects", 0), "one object");
        T.Contains(File.ReadAllText(Path.Combine(d, "o", "sql.tsv"), Encoding.UTF8), "first_table", "the first library wins");
        T.True(!Directory.Exists(Path.Combine(d, "o", "objects")), "no raw data with noRaw");
    }

    public static void ReadCommandPicksTheReaderByFileExtensionAndSpecTakesTheResultOver() {
        string d = T.TempDir("pb-spec");
        Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres"));
        string pbd = Path.Combine(d, "app.pbd"); File.WriteAllBytes(pbd, Library(SampleObjects(), false));
        string o;
        T.Eq(0, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "read \"" + pbd + "\" --project \"" + d + "\"", d, out o, 60000), "read: " + o);
        T.True(File.Exists(Path.Combine(d, "static", "powerbuilder", "reader.json")), "result in static\\powerbuilder");
        // spec with a time line that touched no table: the table of the application is an open point
        var cfg = Config.Load(d);
        var spec = new JO("tables", new List<object>());
        var open = new List<object>();
        var st = Readers.ForSpec(Readers.Load(cfg), spec, open);
        T.Eq(1, st.Arr("windows").Count, "one window"); var w = (JO)st.Arr("windows")[0];
        T.Eq("w_customer", w.Str("name"), "window name"); T.Eq("d_customer", string.Join(",", w.Strings("datawindows").ToArray()), "its data window"); T.Eq("Kundenname", w.Obj("labels").Str("name"), "its labels");
        T.Eq(1, st.Arr("menus").Count, "one menu");
        T.Eq(1, open.Count, "one open point"); T.Contains((string)open[0], "1 of 1 tables", "tables that no recording touched");
        // a recording that wrote the table: no open point, the table is marked as seen
        open.Clear();
        spec = new JO("tables", new List<object> { new JO("name", "public.customer") });
        st = Readers.ForSpec(Readers.Load(cfg), spec, open);
        T.Eq(0, open.Count, "no open point"); T.True(((JO)st.Arr("tables")[0]).Bool("seen_in_recordings", false), "table seen in a recording");
        var md = new StringBuilder(); Readers.Markdown(md, st);
        T.Contains(md.ToString(), "Static analysis", "section in spec.md"); T.Contains(md.ToString(), "Datei | Kunden verwalten", "menu in spec.md");
    }

    public static void ReaderPluginWritesTheSameFormatAndIsFoundByFileExtension() {
        string d = T.TempDir("reader-plugin");
        Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres", "plugins", new List<object> { Path.Combine(T.RepoRoot, "plugins", "examples") }));
        string scripts = Path.Combine(d, "scripts"); Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "orders.sql"), "INSERT INTO orders (id) VALUES (1);\nSELECT * FROM orders JOIN customer ON customer.id = orders.customer_id;\n");
        var cfg = Config.Load(d);
        T.Eq("sql-scripts", Readers.Detect(cfg, new List<string> { scripts }), "found by the extension .sql");
        var r = Readers.Run(cfg, null, new List<string> { scripts }, null, null, 3, null, false);
        T.Eq("sql-scripts", r.Str("reader"), "reader"); T.Eq(1, r.Int("objects", 0), "one object"); T.Eq(2, r.Int("sql", 0), "two statements");
        var doc = Json.ReadFile(Path.Combine(d, "static", "sql-scripts", "reader.json"));
        T.Eq(null, Readers.Check(doc), "format");
        var tables = ((JO)doc.Arr("objects")[0]).Strings("tables");
        T.True(tables.Contains("orders") && tables.Contains("customer"), "tables found: " + string.Join(",", tables.ToArray()));
        T.Throws(delegate { Readers.Run(cfg, "nope", new List<string> { scripts }, null, null, 3, null, false); }, "unknown reader", "unknown reader");
        T.Throws(delegate { Readers.Detect(cfg, new List<string> { Path.Combine(d, "eqlmaker.json") }); }, "no reader knows", "unknown file type");
    }

    public static void ReaderPluginNeedsACommandAndAValidResult() {
        string d = T.TempDir("reader-bad"); string p = Path.Combine(d, "nocmd"); Directory.CreateDirectory(p);
        File.WriteAllText(Path.Combine(p, "plugin.json"), "{\"type\":\"reader\",\"name\":\"x\"}");
        T.Throws(delegate { Plugin.Load(p); }, "needs \"command\"", "reader without command");
        T.Contains(Readers.Check(new JO("format", "other")), "eqlmaker-static", "wrong format named");
        T.Contains(Readers.Check(new JO("format", "eqlmaker-static", "reader", "x", "objects", new List<object> { new JO("name", "a") })), "without name or kind", "object without kind");
    }
}
}
