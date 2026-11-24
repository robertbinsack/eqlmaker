// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// spec: a specification derived from time lines by fixed rules. It states what was observed:
// steps (chapters), masks (windows), statement shapes, tables read and written with the columns
// seen, and where the manual talks about a mask. It does not invent behaviour that was not recorded.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker {

public static class Spec {
    /// <summary>Window title without the parts that change with the data (numbers).</summary>
    public static string MaskName(string windowTitle) {
        if (string.IsNullOrEmpty(windowTitle)) return "(no window)";
        return Regex.Replace(windowTitle, @"\d+", "#").Trim();
    }
    public static string Slug(string s) {
        string t = Regex.Replace((s ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return t == "" ? "x" : t;
    }

    public static JO Build(List<JO> timelines, List<JO> manuals, string projectName) {
        var steps = new List<object>(); var masks = new JO(); var queries = new JO(); var tables = new JO(); var open = new List<object>(); var sources = new List<object>();
        int qn = 0;
        foreach (var tl in timelines) {
            string recName = tl.Str("recording");
            sources.Add(new JO("recording", recName, "source", tl.Str("source"), "counts", tl["counts"]));
            if (tl.Obj("counts").Int("transactions", 0) == 0 && tl.Obj("counts").Int("statements", 0) > 0)
                open.Add("recording '" + recName + "': no changed rows were captured (the source did not deliver them); written tables are taken from the SQL text only");
            if (tl.Obj("counts").Int("before_first_action", 0) > 0)
                open.Add("recording '" + recName + "': " + tl.Obj("counts").Int("before_first_action", 0) + " database events before the first action are not tied to a step");
            foreach (JO ch in tl.Arr("chapters")) {
                string caption = ch.Str("caption") ?? "(recording " + recName + ")";
                var step = new JO("id", "s" + (steps.Count + 1), "caption", caption, "recording", recName, "start", ch["start"], "end", ch["end"]);
                var stepMasks = new List<string>(); var stepActs = new List<object>(); var stepQueries = new List<string>(); var writes = new JO(); var stepShots = new List<object>();
                foreach (JO a in ch.Arr("actions")) {
                    string kind = a.Str("kind", "");
                    string mask = MaskName(a.Str("window"));
                    if (a.Str("window") != null && !stepMasks.Contains(mask)) stepMasks.Add(mask);
                    JO m = masks.Obj(mask);
                    if (a.Str("window") != null && m == null) { m = new JO("id", "m-" + Slug(mask), "title", mask, "windows", new List<object>(), "controls", new List<object>(), "shots", new List<object>(), "steps", new List<object>(), "queries", new List<object>(), "tables_read", new List<object>(), "tables_written", new List<object>(), "manual", new List<object>()); masks[mask] = m; }
                    if (m != null) {
                        AddOnce(m.Arr("windows"), a.Str("window")); AddOnce(m.Arr("steps"), step.Str("id"));
                        if (!string.IsNullOrEmpty(a.Str("control"))) { bool has = false; foreach (JO c in m.Arr("controls")) if (c.Str("text") == a.Str("control") && c.Str("class") == a.Str("class")) has = true; if (!has) m.Arr("controls").Add(new JO("text", a.Str("control"), "class", a.Str("class"), "used_as", kind)); }
                        if (a.Str("shot") != null && m.Arr("shots").Count < 12) m.Arr("shots").Add(recName + "/" + a.Str("shot"));
                    }
                    if (a.Str("shot") != null) stepShots.Add(recName + "/" + a.Str("shot"));
                    var actQueries = new List<object>();
                    foreach (JO s in a.Arr("statements")) {
                        if (s.Str("kind") == "error") { open.Add("step '" + caption + "': the application received a database error: " + s.Str("error")); continue; }
                        string verb = s.Str("verb", "");
                        if (SqlText.IsTransactionControl(verb) || verb == "set" || verb == "show") continue;
                        string shape = s.Str("shape");
                        JO q = queries.Obj(shape);
                        if (q == null) { q = new JO("id", "q" + (++qn), "verb", verb, "shape", shape, "tables", s["tables_referenced"], "count", 0, "example_params", s["params"], "example_sql", s.Str("sql"), "steps", new List<object>(), "applications", new List<object>()); queries[shape] = q; }
                        q["count"] = q.Int("count", 0) + 1; AddOnce(q.Arr("steps"), step.Str("id")); AddOnce(q.Arr("applications"), s.Str("application"));
                        if (!stepQueries.Contains(q.Str("id"))) stepQueries.Add(q.Str("id"));
                        actQueries.Add(q.Str("id"));
                        if (m != null) {
                            AddOnce(m.Arr("queries"), q.Str("id"));
                            foreach (var t in s.Strings("tables_referenced")) { if (verb == "select") AddOnce(m.Arr("tables_read"), t); Table(tables, t, verb == "select" ? "read_by" : SqlText.IsWrite(verb) ? "written_by_sql_text" : "read_by", step.Str("id")); }
                        }
                    }
                    foreach (JO t in a.Arr("transactions")) {
                        if (t.Arr("rows") == null) continue;
                        foreach (JO r in t.Arr("rows")) {
                            string tn = r.Str("table"), op = r.Str("op");
                            JO w = writes.Obj(tn + " " + op);
                            if (w == null) { w = new JO("table", tn, "op", op, "rows", 0, "columns", new List<object>(), "example", r["new"] ?? r["old"]); writes[tn + " " + op] = w; }
                            w["rows"] = w.Int("rows", 0) + 1;
                            var cols = r.Obj("new") ?? r.Obj("old");
                            if (cols != null) foreach (var k in cols.Keys) AddOnce(w.Arr("columns"), k);
                            if (m != null) AddOnce(m.Arr("tables_written"), tn);
                            var tj = Table(tables, tn, "written_by", step.Str("id"));
                            if (cols != null) foreach (var k in cols.Keys) AddOnce(tj.Arr("columns_seen"), k);
                            AddOnce(tj.Arr("operations"), op);
                        }
                    }
                    if (kind != "chapter") stepActs.Add(new JO("text", a.Str("text"), "mask", a.Str("window") != null ? mask : null, "queries", actQueries, "transactions", a.Arr("transactions").Count));
                }
                step["masks"] = stepMasks.ConvertAll(delegate (string x) { return (object)x; });
                step["actions"] = stepActs; step["queries"] = stepQueries.ConvertAll(delegate (string x) { return (object)x; });
                var wl = new List<object>(); foreach (var kv in writes) wl.Add(kv.Value);
                step["writes"] = wl; step["tables"] = ch["tables"]; step["shots"] = stepShots; step["manual"] = new List<object>();
                steps.Add(step);
            }
        }
        // --- manual: chapters whose words overlap with a mask or step
        var manualList = new List<object>();
        foreach (var man in manuals) {
            manualList.Add(new JO("name", man.Str("name"), "title", man.Str("title"), "chapters", man.Arr("chapters").Count));
            foreach (var kv in masks) { var m = (JO)kv.Value; var words = Words(m.Str("title")); foreach (JO c in m.Arr("controls")) words.UnionWith(Words(c.Str("text"))); Link(m.Arr("manual"), man, words); }
            foreach (JO s in steps) { var words = Words(s.Str("caption")); foreach (var mk in s.Strings("masks")) words.UnionWith(Words(mk)); Link(s.Arr("manual"), man, words); }
        }
        var spec = new JO();
        spec["format"] = "eqlmaker-spec"; spec["version"] = DbEvent.FormatVersion; spec["project"] = projectName;
        spec["note"] = "Derived by fixed rules from what was recorded. Behaviour that was never operated during a recording is not in here.";
        spec["sources"] = sources; spec["manuals"] = manualList; spec["steps"] = steps;
        var ml = new List<object>(); foreach (var kv in masks) ml.Add(kv.Value); spec["masks"] = ml;
        var ql = new List<object>(); foreach (var kv in queries) ql.Add(kv.Value); spec["queries"] = ql;
        var tlst = new List<object>(); foreach (var kv in tables) tlst.Add(kv.Value); spec["tables"] = tlst;
        spec["open_points"] = open;
        return spec;
    }

    static void AddOnce(List<object> l, string v) { if (v != null && !l.Contains(v)) l.Add(v); }
    static JO Table(JO tables, string name, string list, string stepId) {
        // "customer" and "public.customer" are the same table when only one schema was seen
        string key = name;
        foreach (var k in tables.Keys) { if (k == name) break; if (k.EndsWith("." + name, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + k, StringComparison.OrdinalIgnoreCase)) { key = k; break; } }
        JO t = tables.Obj(key);
        if (t == null) { t = new JO("name", name, "read_by", new List<object>(), "written_by", new List<object>(), "written_by_sql_text", new List<object>(), "operations", new List<object>(), "columns_seen", new List<object>()); tables[key] = t; }
        else if (name.IndexOf('.') >= 0 && t.Str("name").IndexOf('.') < 0) t["name"] = name;
        AddOnce(t.Arr(list), stepId);
        return t;
    }

    static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "the", "and", "for", "with", "from", "that", "this", "are", "was", "not", "you", "can", "new", "edit", "save", "cancel", "refresh", "window", "button",
        "der", "die", "das", "und", "mit", "von", "ist", "ein", "eine", "nicht", "auf", "den", "dem", "des", "wird", "sie", "neu" };
    public static HashSet<string> Words(string s) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(s ?? "", @"[\p{L}][\p{L}\p{Nd}]{2,}")) if (!Stop.Contains(m.Value)) { string w = m.Value.ToLowerInvariant(); set.Add(w.Length > 3 && w.EndsWith("s") ? w.Substring(0, w.Length - 1) : w); }   // "customers" = "customer"
        return set;
    }
    static void Link(List<object> target, JO manual, HashSet<string> words) {
        if (words.Count == 0) return;
        var hits = new List<JO>();
        foreach (JO c in manual.Arr("chapters")) {
            var title = Words(c.Str("title")); var text = Words(c.Str("text"));
            int score = 0; var matched = new List<object>();
            foreach (var w in words) { if (title.Contains(w)) { score += 3; matched.Add(w); } else if (text.Contains(w)) { score += 1; matched.Add(w); } }
            if (score >= 3) hits.Add(new JO("manual", manual.Str("name"), "chapter", c.Str("id"), "title", c.Str("title"), "score", score, "matched_words", matched));
        }
        hits.Sort(delegate (JO a, JO b) { int c = b.Int("score", 0).CompareTo(a.Int("score", 0)); return c != 0 ? c : string.CompareOrdinal(a.Str("chapter"), b.Str("chapter")); });
        for (int i = 0; i < hits.Count && i < 3; i++) target.Add(hits[i]);
    }

    public static string Markdown(JO spec) {
        var sb = new StringBuilder();
        sb.Append("# Specification: " + spec.Str("project") + "\n\n" + spec.Str("note") + "\n\n");
        sb.Append("## Steps\n\n");
        foreach (JO s in spec.Arr("steps")) {
            sb.Append("### " + s.Str("id") + " " + s.Str("caption") + "\n\n");
            sb.Append("- recording: " + s.Str("recording") + ", masks: " + string.Join(", ", s.Strings("masks").ToArray()) + "\n");
            foreach (JO a in s.Arr("actions")) sb.Append("- " + a.Str("text") + (a.Arr("queries").Count > 0 ? "  -> " + string.Join(", ", a.Strings("queries").ToArray()) : "") + (a.Int("transactions", 0) > 0 ? "  (changes rows)" : "") + "\n");
            foreach (JO w in s.Arr("writes")) sb.Append("- writes: " + w.Str("op") + " " + w.Str("table") + " x " + w.Int("rows", 0) + " (" + string.Join(", ", w.Strings("columns").ToArray()) + ")\n");
            foreach (JO m in s.Arr("manual")) sb.Append("- manual: " + m.Str("title") + " (" + m.Str("manual") + " " + m.Str("chapter") + ")\n");
            sb.Append("\n");
        }
        sb.Append("## Masks\n\n");
        foreach (JO m in spec.Arr("masks")) {
            sb.Append("### " + m.Str("title") + "\n\n");
            var cs = new List<string>(); foreach (JO c in m.Arr("controls")) cs.Add("\"" + c.Str("text") + "\" (" + c.Str("class") + ")");
            if (cs.Count > 0) sb.Append("- controls used: " + string.Join(", ", cs.ToArray()) + "\n");
            sb.Append("- reads: " + string.Join(", ", m.Strings("tables_read").ToArray()) + "\n- writes: " + string.Join(", ", m.Strings("tables_written").ToArray()) + "\n");
            if (m.Arr("shots").Count > 0) sb.Append("- reference picture: " + m.Strings("shots")[m.Arr("shots").Count - 1] + "\n");
            foreach (JO x in m.Arr("manual")) sb.Append("- manual: " + x.Str("title") + " (" + x.Str("manual") + " " + x.Str("chapter") + ")\n");
            sb.Append("\n");
        }
        sb.Append("## Statements\n\n");
        foreach (JO q in spec.Arr("queries")) sb.Append("- " + q.Str("id") + " (" + q.Int("count", 0) + " x, steps " + string.Join(", ", q.Strings("steps").ToArray()) + "): `" + q.Str("shape").Replace("`", "'") + "`\n");
        sb.Append("\n## Tables\n\n| table | read by | written by | operations | columns seen |\n|---|---|---|---|---|\n");
        foreach (JO t in spec.Arr("tables")) sb.Append("| " + t.Str("name") + " | " + string.Join(" ", t.Strings("read_by").ToArray()) + " | " + string.Join(" ", t.Strings("written_by").ToArray()) + " | " + string.Join(" ", t.Strings("operations").ToArray()) + " | " + string.Join(", ", t.Strings("columns_seen").ToArray()) + " |\n");
        if (spec.Arr("open_points").Count > 0) { sb.Append("\n## Open points\n\n"); foreach (var o in spec.Arr("open_points")) sb.Append("- " + o + "\n"); }
        return sb.ToString();
    }

    /// <summary>Builds the specification of a project from the named recordings (all with a time line when none is named).</summary>
    public static JO Run(Config cfg, List<string> recordings) {
        var tls = new List<JO>();
        var dirs = new List<string>();
        if (recordings.Count > 0) foreach (var r in recordings) dirs.Add(cfg.RecordingDir(r));
        else if (Directory.Exists(cfg.RecordingsDir)) { foreach (var d in Directory.GetDirectories(cfg.RecordingsDir)) if (File.Exists(Path.Combine(d, "recording.json"))) dirs.Add(d); dirs.Sort(StringComparer.Ordinal); }
        if (dirs.Count == 0) throw new Exception("no recordings found in " + cfg.RecordingsDir);
        foreach (var d in dirs) {
            string f = Path.Combine(d, "timeline.json");
            if (!File.Exists(f)) Timeline.Run(d);
            tls.Add(Json.ReadFile(f));
        }
        var manuals = new List<JO>();
        if (Directory.Exists(cfg.Sub("manual"))) { var files = new List<string>(Directory.GetFiles(cfg.Sub("manual"), "*.json")); files.Sort(StringComparer.Ordinal); foreach (var f in files) { var m = Json.ReadFile(f); if (m.Str("format") == "eqlmaker-manual") manuals.Add(m); } }
        var spec = Build(tls, manuals, Path.GetFileName(cfg.Dir));
        Directory.CreateDirectory(cfg.Sub("spec"));
        Json.WriteFile(cfg.Sub("spec", "spec.json"), spec);
        File.WriteAllText(cfg.Sub("spec", "spec.md"), Markdown(spec), new UTF8Encoding(false));
        return new JO("spec", cfg.Sub("spec", "spec.json"), "readable", cfg.Sub("spec", "spec.md"), "steps", spec.Arr("steps").Count, "masks", spec.Arr("masks").Count, "queries", spec.Arr("queries").Count, "tables", spec.Arr("tables").Count,
            "manuals", manuals.Count, "open_points", spec.Arr("open_points").Count);
    }
}
}
