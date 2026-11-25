// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Readers look at the files of the original application (not at its running) and write what they
// find in one neutral format, static/<name>/reader.json (format "eqlmaker-static"). The built-in
// reader "powerbuilder" is one; a plugin of the type "reader" is another (docs/plugins.md).
// "spec" takes every reader.json of the project into the specification.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace EqlMaker {

public static class Readers {
    public static readonly string[] Builtin = { PowerBuilderReader.Name };

    /// <summary>Name of the reader for these files: by file extension, among the built-in readers and the plugins.</summary>
    public static string Detect(Config cfg, List<string> paths) {
        foreach (var raw in paths) foreach (var p in raw.Split(',')) {
            var files = new List<string>();
            if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p)); else files.Add(p);
            foreach (var f in files) {
                string x = Path.GetExtension(f).ToLowerInvariant();
                if (x == ".pbd" || x == ".pbl") return PowerBuilderReader.Name;
                foreach (var pl in cfg.Plugins()) if (pl.Type == "reader" && pl.Manifest.Strings("extensions").Exists(delegate (string e) { return string.Equals(e, x, StringComparison.OrdinalIgnoreCase); })) return pl.Name;
            }
        }
        throw new Exception("no reader knows these files; name one with --reader (built in: " + string.Join(", ", Builtin) + ")");
    }

    public static JO Run(Config cfg, string reader, List<string> paths, string outDir, List<string> bundles, int maxDepth, string exclude, bool noRaw) {
        if (paths == null || paths.Count == 0) throw new Exception("read needs the files or folders of the application");
        if (string.IsNullOrEmpty(reader)) reader = Detect(cfg, paths);
        if (string.IsNullOrEmpty(outDir)) outDir = cfg.Sub("static", reader);
        outDir = Path.GetFullPath(outDir);
        if (string.Equals(reader, PowerBuilderReader.Name, StringComparison.OrdinalIgnoreCase)) return PowerBuilderReader.Run(paths, outDir, bundles, maxDepth, exclude, noRaw);
        var plugin = Plugin.Find(cfg.Plugins(), "reader", reader);
        if (plugin == null) throw new Exception("unknown reader '" + reader + "' (built in: " + string.Join(", ", Builtin) + "; plugins of the type reader are looked for in the plugin folders)");
        return RunPlugin(plugin, paths, outDir);
    }

    /// <summary>Reader plugin: gets {"action":"read","paths":[...],"out":"..."} as one line on standard input, writes
    /// reader.json into "out" and answers with one line {"result":{...}}.</summary>
    static JO RunPlugin(Plugin p, List<string> paths, string outDir) {
        Directory.CreateDirectory(outDir);
        var full = new List<object>(); foreach (var x in paths) full.Add(Path.GetFullPath(x));
        var req = new JO("action", "read", "paths", full, "out", outDir);
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + p.Manifest.Str("command") + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true; psi.WorkingDirectory = p.Dir;
        psi.StandardOutputEncoding = Encoding.UTF8;
        PluginIo.NoByteOrderMark();
        using (var proc = Process.Start(psi)) {
            var w = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)); w.Write(Json.Write(req) + "\n"); w.Flush(); proc.StandardInput.Close();
            string err = null; var t = new System.Threading.Thread(delegate () { err = proc.StandardError.ReadToEnd(); }); t.Start();
            string o = proc.StandardOutput.ReadToEnd(); proc.WaitForExit(600000); t.Join(2000);
            if (proc.ExitCode != 0) throw new Exception("reader '" + p.Name + "' failed: " + (err ?? "").Trim());
            JO result = null;
            foreach (var line in o.Split('\n')) { string l = line.Trim().TrimStart('﻿'); if (l.StartsWith("{")) { var r = Json.ParseObject(l); if (r.Obj("result") != null) result = r.Obj("result"); } }
            if (result == null) throw new Exception("reader '" + p.Name + "' did not answer with {\"result\": ...}");
            string f = Path.Combine(outDir, "reader.json");
            if (!File.Exists(f)) throw new Exception("reader '" + p.Name + "' wrote no reader.json into " + outDir);
            string problem = Check(Json.ReadFile(f));
            if (problem != null) throw new Exception("reader '" + p.Name + "': reader.json " + problem);
            result["reader"] = p.Name; result["out"] = outDir; result["result"] = f;
            return result;
        }
    }

    /// <summary>Null when the document has the format of a reader result, else what is wrong.</summary>
    public static string Check(JO doc) {
        if (doc.Str("format") != "eqlmaker-static") return "does not have \"format\": \"eqlmaker-static\"";
        if (doc.Str("reader", "") == "") return "has no \"reader\"";
        if (doc.Arr("objects") == null) return "has no \"objects\" list";
        foreach (JO o in doc.Arr("objects")) if (o.Str("name", "") == "" || o.Str("kind", "") == "") return "has an object without name or kind";
        return null;
    }

    /// <summary>The results of all readers of the project (static/*/reader.json).</summary>
    public static List<JO> Load(Config cfg) {
        var l = new List<JO>(); string root = cfg.Sub("static");
        if (!Directory.Exists(root)) return l;
        var dirs = new List<string>(Directory.GetDirectories(root)); dirs.Sort(StringComparer.Ordinal);
        foreach (var d in dirs) { string f = Path.Combine(d, "reader.json"); if (File.Exists(f)) { var j = Json.ReadFile(f); if (Check(j) == null) l.Add(j); } }
        return l;
    }

    static string Last(string table) { int i = table.LastIndexOf('.'); return (i >= 0 ? table.Substring(i + 1) : table).Trim('"').ToLowerInvariant(); }
    static List<object> Or(List<object> l) { return l ?? new List<object>(); }

    /// <summary>The part "static" of the specification: what the files of the application say, and which of its
    /// tables no recording has touched.</summary>
    public static JO ForSpec(List<JO> readers, JO spec, List<object> open) {
        var rl = new List<object>(); var menus = new List<object>(); var windows = new List<object>(); var tables = new JO();
        foreach (var r in readers) {
            var byName = new Dictionary<string, JO>(StringComparer.OrdinalIgnoreCase);
            foreach (JO o in r.Arr("objects")) byName[o.Str("name")] = o;
            int nsql = 0, nwin = 0;
            foreach (JO o in r.Arr("objects")) {
                var sql = Or(o.Arr("sql")); nsql += sql.Count;
                foreach (var t in o.Strings("tables")) {
                    JO e = tables.Obj(Last(t));
                    if (e == null) { e = new JO("name", t, "statements", 0, "objects", new List<object>()); tables[Last(t)] = e; }
                    e["statements"] = e.Int("statements", 0) + 1;
                    if (!e.Arr("objects").Contains(o.Str("name"))) e.Arr("objects").Add(o.Str("name"));
                }
                if (o.Str("kind") != "win") continue;
                nwin++;
                var dws = new List<object>(); var labels = new JO(); var wt = new List<object>();
                foreach (var rf in o.Strings("refs")) {
                    JO d; if (!byName.TryGetValue(rf, out d) || d.Str("kind") != "dwo") continue;
                    dws.Add(d.Str("name"));
                    if (d.Obj("labels") != null) foreach (var kv in d.Obj("labels")) if (!labels.Has(kv.Key)) labels[kv.Key] = kv.Value;
                    foreach (var t in d.Strings("tables")) if (!wt.Contains(t)) wt.Add(t);
                }
                foreach (var t in o.Strings("tables")) if (!wt.Contains(t)) wt.Add(t);
                var texts = Or(o.Arr("texts"));
                windows.Add(new JO("name", o.Str("name"), "reader", r.Str("reader"), "datawindows", dws, "labels", labels, "tables", wt, "sql", sql.Count, "texts", texts.GetRange(0, Math.Min(5, texts.Count))));
            }
            foreach (JO m in Or(r.Arr("menus"))) menus.Add(new JO("reader", r.Str("reader"), "name", m.Str("name"), "items", m["items"]));
            rl.Add(new JO("reader", r.Str("reader"), "objects", r.Arr("objects").Count, "sql", nsql, "windows", nwin, "menus", Or(r.Arr("menus")).Count));
        }
        // tables of the application that no recording has touched
        var seen = new HashSet<string>(); foreach (JO t in spec.Arr("tables")) seen.Add(Last(t.Str("name")));
        var tl = new List<object>(); var untouched = new List<string>();
        foreach (var kv in tables) { var e = (JO)kv.Value; bool s = seen.Contains(kv.Key); e["seen_in_recordings"] = s; tl.Add(e); if (!s) untouched.Add(e.Str("name")); }
        if (untouched.Count > 0) {
            untouched.Sort(StringComparer.OrdinalIgnoreCase);
            string list = string.Join(", ", untouched.GetRange(0, Math.Min(20, untouched.Count)).ToArray()) + (untouched.Count > 20 ? ", ..." : "");
            open.Add("static: " + untouched.Count + " of " + tables.Count + " tables in the SQL of the application were touched by no recording (" + list + "); operate the matching functions and record again, or take them from the files of the application");
        }
        return new JO("note", "What the files of the original application say. It is not what was operated: use it to see what the recordings do not cover.", "readers", rl, "menus", menus, "windows", windows, "tables", tl);
    }

    public static void Markdown(StringBuilder sb, JO st) {
        sb.Append("\n## Static analysis (files of the application)\n\n" + st.Str("note") + "\n\n");
        foreach (JO r in st.Arr("readers")) sb.Append("- reader " + r.Str("reader") + ": " + r.Int("objects", 0) + " objects, " + r.Int("sql", 0) + " SQL statements, " + r.Int("windows", 0) + " windows, " + r.Int("menus", 0) + " menus\n");
        foreach (JO m in st.Arr("menus")) sb.Append("\n### Menu " + m.Str("name") + "\n\n" + string.Join(" | ", m.Strings("items").ToArray()) + "\n");
        if (st.Arr("windows").Count > 0) {
            sb.Append("\n### Windows\n\n| window | data windows | tables | labels |\n|---|---|---|---|\n");
            foreach (JO w in st.Arr("windows")) {
                var lab = new List<string>(); if (w.Obj("labels") != null) foreach (var kv in w.Obj("labels")) lab.Add(Convert.ToString(kv.Value));
                sb.Append("| " + w.Str("name") + " | " + string.Join(" ", w.Strings("datawindows").ToArray()) + " | " + string.Join(", ", w.Strings("tables").ToArray()) + " | " + string.Join(", ", lab.ToArray()) + " |\n");
            }
        }
        if (st.Arr("tables").Count > 0) {
            sb.Append("\n### Tables in the SQL of the application\n\n| table | statements | in a recording |\n|---|---|---|\n");
            foreach (JO t in st.Arr("tables")) sb.Append("| " + t.Str("name") + " | " + t.Int("statements", 0) + " | " + (t.Bool("seen_in_recordings", false) ? "yes" : "no") + " |\n");
        }
    }
}
}
