// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Guided steps: generate (new web application), clone (read-only 1:1 copy), enrich (take findings of
// a narrated video over into the web application). EQLMaker does not write the application itself.
// Each step prepares a workspace (template, specification, reference pictures), writes instructions
// for the connected MCP client (or a person), records what was decided, and leaves the proof to
// "verify". Nothing here is an automatic generator.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EqlMaker {

public static class Guided {
    static readonly string[] TextExt = { ".js", ".mjs", ".json", ".html", ".css", ".md", ".txt", ".cmd", ".ps1" };

    static void CopyTemplate(string from, string to, JO vars) {
        Share.CopyDir(from, to, true);
        foreach (var f in Directory.GetFiles(to, "*", SearchOption.AllDirectories)) {
            if (Array.IndexOf(TextExt, Path.GetExtension(f).ToLowerInvariant()) < 0 || f.Contains("node_modules")) continue;
            string s = File.ReadAllText(f, Encoding.UTF8), t = s;
            foreach (var kv in vars) t = t.Replace("{{" + kv.Key + "}}", Convert.ToString(kv.Value));
            if (t != s) File.WriteAllText(f, t, new UTF8Encoding(false));
        }
    }

    static JO NeedSpec(Config cfg) {
        string f = cfg.Sub("spec", "spec.json");
        if (!File.Exists(f)) throw new Exception("no specification yet: run 'eqlmaker spec' first");
        return Json.ReadFile(f);
    }

    static string Fill(string promptName, JO vars) {
        string s = File.ReadAllText(Share.File("prompts", promptName + ".md"), Encoding.UTF8);
        foreach (var kv in vars) s = s.Replace("{{" + kv.Key + "}}", Convert.ToString(kv.Value));
        return s;
    }

    static string StepList(JO spec) {
        var sb = new StringBuilder();
        foreach (JO s in spec.Arr("steps")) {
            sb.Append("- " + s.Str("id") + " \"" + s.Str("caption") + "\": masks " + string.Join(", ", s.Strings("masks").ToArray()));
            var w = new List<string>(); foreach (JO x in s.Arr("writes")) w.Add(x.Str("op") + " " + x.Str("table"));
            sb.Append(w.Count > 0 ? "; writes " + string.Join(", ", w.ToArray()) : "; reads only");
            sb.Append("\n");
        }
        return sb.ToString();
    }
    static string MaskList(JO spec) {
        var sb = new StringBuilder();
        foreach (JO m in spec.Arr("masks")) {
            var c = new List<string>(); foreach (JO x in m.Arr("controls")) c.Add("\"" + x.Str("text") + "\"");
            sb.Append("- " + m.Str("id") + " \"" + m.Str("title") + "\": controls used " + (c.Count > 0 ? string.Join(", ", c.ToArray()) : "(none recorded)") + "; reads " + string.Join(", ", m.Strings("tables_read").ToArray()) + "; writes " + string.Join(", ", m.Strings("tables_written").ToArray()) + "\n");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- generate
    public static JO Generate(Config cfg, string template, bool force) {
        var spec = NeedSpec(cfg);
        string web = cfg.Sub("web");
        if (Directory.Exists(web) && Directory.GetFileSystemEntries(web).Length > 0 && !force) throw new Exception("the folder 'web' exists already; use --force to copy the template over it (your files with other names stay)");
        string from; string tname = string.IsNullOrEmpty(template) ? "default" : template;
        if (tname == "default") from = Share.File("templates", "web");
        else {
            var p = Plugin.Find(cfg.Plugins(), "web-template", tname);
            if (p == null) throw new Exception("web template '" + tname + "' not found (built-in: default; others are plugins of type web-template)");
            from = Path.Combine(p.Dir, p.Manifest.Str("files", "files"));
        }
        var vars = new JO("APP_NAME", Path.GetFileName(cfg.Dir), "DB_ENV", "APP_DB");
        CopyTemplate(from, web, vars);
        Directory.CreateDirectory(Path.Combine(web, "spec"));
        File.Copy(cfg.Sub("spec", "spec.json"), Path.Combine(web, "spec", "spec.json"), true);
        File.Copy(cfg.Sub("spec", "spec.md"), Path.Combine(web, "spec", "spec.md"), true);
        if (Directory.Exists(cfg.Sub("manual"))) foreach (var f in Directory.GetFiles(cfg.Sub("manual"), "*.txt")) File.Copy(f, Path.Combine(web, "spec", "manual-" + Path.GetFileName(f)), true);
        string text = Prompt(cfg, "generate");
        string guide = Path.Combine(web, "GENERATE.md");
        File.WriteAllText(guide, text, new UTF8Encoding(false));
        return new JO("workspace", web, "template", tname, "instructions", guide, "steps", spec.Arr("steps").Count, "masks", spec.Arr("masks").Count,
            "next", "build the application as GENERATE.md says (MCP prompt 'generate'), record the same steps in it, then: eqlmaker verify db <original> <web>");
    }

    // ---------------------------------------------------------------- claude-app
    static readonly System.Text.RegularExpressions.Regex Ident = new System.Text.RegularExpressions.Regex("^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>libpq connection string (keywords or postgres:// URI) -> host, port, database, user. The password
    /// and the passfile are never taken over: the Claude app keeps its password in DPAPI (set-password).</summary>
    public static JO ParseConnection(string c) {
        var o = new JO("host", "127.0.0.1", "port", 5432, "database", "", "user", "");
        c = (c ?? "").Trim();
        if (c.StartsWith("postgres://") || c.StartsWith("postgresql://")) {
            var u = new Uri(c);
            if (u.Host != "") o["host"] = u.Host; if (u.Port > 0) o["port"] = u.Port;
            o["database"] = Uri.UnescapeDataString(u.AbsolutePath.Trim('/'));
            if (u.UserInfo != "") o["user"] = Uri.UnescapeDataString(u.UserInfo.Split(':')[0]);
            return o;
        }
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(c, @"(\w+)\s*=\s*('(?:[^'\\]|\\.)*'|\S+)")) {
            string k = m.Groups[1].Value, v = m.Groups[2].Value;
            if (v.StartsWith("'")) v = System.Text.RegularExpressions.Regex.Replace(v.Substring(1, v.Length - 2), @"\\(.)", "$1");
            if (k == "host" || k == "hostaddr") o["host"] = v; else if (k == "port") { int p; if (int.TryParse(v, out p)) o["port"] = p; }
            else if (k == "dbname") o["database"] = v; else if (k == "user") o["user"] = v;
        }
        return o;
    }

    /// <summary>Guided step for a Claude app: copies the template (generic local MCP server + page for Claude Desktop),
    /// writes the application description (one list query per table of the specification, one entity with
    /// create/update/delete per --entity table:key), the server configuration without password, the Claude Desktop
    /// entry and the instructions. One application = one database = one MCP server eqlmaker-&lt;app&gt;.</summary>
    public static JO ClaudeApp(Config cfg, string name, string title, string schema, string entities, bool force) {
        string app = Spec.Slug(string.IsNullOrEmpty(name) ? Path.GetFileName(cfg.Dir) : name);
        string server = "eqlmaker-" + app;
        string ttl = string.IsNullOrEmpty(title) ? (string.IsNullOrEmpty(name) ? Path.GetFileName(cfg.Dir) : name) : title;
        string dir = cfg.Sub("claude-app");
        if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0 && !force) throw new Exception("the folder 'claude-app' exists already; use --force to copy the template over it (your files with other names, the description and the configuration stay)");
        CopyTemplate(Share.File("templates", "claude-app"), dir, new JO("APP_NAME", app, "APP_TITLE", ttl.Replace("\"", "'"), "SERVER_NAME", server));

        // tables: the specification knows which tables the original read and wrote, and the columns it wrote
        JO spec = File.Exists(cfg.Sub("spec", "spec.json")) ? Json.ReadFile(cfg.Sub("spec", "spec.json")) : null;
        var tables = new List<JO>();
        if (spec != null) foreach (JO t in spec.Arr("tables")) { string n = t.Str("name", ""); int dot = n.LastIndexOf('.'); if (dot >= 0) n = n.Substring(dot + 1); if (Ident.IsMatch(n) && !tables.Exists(delegate (JO x) { return x.Str("name") == n; })) tables.Add(new JO("name", n, "columns", t["columns_seen"] ?? new List<object>(), "written", t.Arr("written_by").Count > 0)); }
        var keys = new JO();
        foreach (var part in (entities ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
            var tk = part.Split(':');
            if (tk.Length != 2 || !Ident.IsMatch(tk[0]) || !Ident.IsMatch(tk[1])) throw new Exception("--entity expects table:key, for example customer:customer_id (got '" + part + "')");
            keys[tk[0]] = tk[1];
            if (!tables.Exists(delegate (JO x) { return x.Str("name") == tk[0]; })) tables.Add(new JO("name", tk[0], "columns", new List<object>(), "written", true));
        }
        // columns of the entity tables from the database itself (types, required, length), when it is reachable
        var dbCols = new JO(); string dbNote = "";
        if (keys.Count > 0 && cfg.Raw.Str("connection", "") != "") {
            try {
                using (var pc = new PgClient(PgConnInfo.Parse(cfg.Connection).WithApplication(PostgresSource.AppName))) {
                    foreach (var kv in keys) {
                        var res = pc.Query("SELECT column_name, data_type, is_nullable, column_default IS NOT NULL OR is_identity = 'YES' AS has_default, character_maximum_length FROM information_schema.columns WHERE table_schema = $1 AND table_name = $2 ORDER BY ordinal_position", string.IsNullOrEmpty(schema) ? "public" : schema, kv.Key);
                        var list = new List<object>();
                        foreach (var row in res.Rows) {
                            if (row[0] == Convert.ToString(kv.Value) || !Ident.IsMatch(row[0])) continue;
                            string dt = row[1], ty = dt.Contains("int") ? "int" : dt == "numeric" || dt == "money" || dt.Contains("double") || dt == "real" ? "number" : dt.Contains("date") || dt.Contains("timestamp") ? "date" : dt == "boolean" ? "bool" : dt == "text" ? "memo" : "text";
                            var fo = new JO("c", row[0], "l", row[0], "t", ty);
                            if (row[2] == "NO" && row[3] != "t") fo["req"] = true;
                            int len; if (int.TryParse(row[4], out len) && len > 0) fo["len"] = len;
                            list.Add(fo);
                        }
                        if (list.Count > 0) dbCols[kv.Key] = list;
                    }
                }
                dbNote = "columns of the entities read from the database";
            } catch (Exception e) { dbNote = "database not reachable (" + e.Message + "): fields from the specification only"; }
        }
        var queries = new JO(); var ents = new JO();
        foreach (var t in tables) {
            string n = t.Str("name");
            queries[n] = new JO("title", n, "sql", "SELECT * FROM " + n);
            if (keys.Has(n)) {
                var fields = new List<object>();
                if (dbCols.Has(n)) fields = dbCols.Arr(n);
                else foreach (var c in t.Strings("columns")) if (Ident.IsMatch(c) && c != keys.Str(n)) fields.Add(new JO("c", c, "l", c, "t", "text"));
                ents[n] = new JO("table", n, "key", keys.Str(n), "identity", true, "single", n, "fields", fields);
                ((JO)queries[n])["tiebreak"] = keys.Str(n);
            }
        }
        string descFile = Path.Combine(dir, "mcp-server", "apps", app + ".json"), cfgFile = Path.Combine(dir, "mcp-server", "config", app + ".json");
        bool keptDesc = File.Exists(descFile), keptCfg = File.Exists(cfgFile);
        if (!keptDesc) Json.WriteFile(descFile, new JO("app", app, "title", ttl, "version", "0.1.0",
            "source", "written by eqlmaker claude-app from " + (spec != null ? "spec/spec.json" : "the command line") + "; complete fields (types t, labels l, req, len), queries and operations as the instructions say",
            "entities", ents, "queries", queries));
        var conn = ParseConnection(cfg.Raw.Str("connection", ""));
        if (!keptCfg) Json.WriteFile(cfgFile, new JO("name", server, "app", "../apps/" + app + ".json",
            "connection", new JO("host", conn["host"], "port", conn["port"], "database", conn.Str("database"), "schema", string.IsNullOrEmpty(schema) ? "public" : schema,
                "user", conn.Str("user"), "credential_profile", app),
            "audit_log", "%LOCALAPPDATA%/EQLMaker/audit/" + app + ".jsonl"));
        string fwd = dir.Replace('\\', '/');
        string desktop = Path.Combine(dir, "claude_desktop_config.json");
        Json.WriteFile(desktop, new JO("mcpServers", new JO(server, new JO("command", "node", "args", new List<object> { fwd + "/mcp-server/bin/eqlmaker-mcp.mjs", "--config", fwd + "/mcp-server/config/" + app + ".json" }))));
        if (spec != null) {
            Directory.CreateDirectory(Path.Combine(dir, "spec"));
            File.Copy(cfg.Sub("spec", "spec.json"), Path.Combine(dir, "spec", "spec.json"), true);
            if (File.Exists(cfg.Sub("spec", "spec.md"))) File.Copy(cfg.Sub("spec", "spec.md"), Path.Combine(dir, "spec", "spec.md"), true);
        }
        var tl = new StringBuilder();
        foreach (var t in tables) tl.Append("- `" + t.Str("name") + "`: " + (keys.Has(t.Str("name")) ? "entity with key `" + keys.Str(t.Str("name")) + "` and list query" : "list query only" + (t.Bool("written", false) ? " (the original writes this table: add `--entity " + t.Str("name") + ":<key>` or write the entity by hand)" : "")) + (t.Strings("columns").Count > 0 ? "; columns seen: " + string.Join(", ", t.Strings("columns").ToArray()) : "") + "\n");
        string text = File.ReadAllText(Share.File("prompts", "claude-app.md"), Encoding.UTF8);
        foreach (var kv in new JO("APP_NAME", app, "APP_TITLE", ttl, "SERVER_NAME", server, "WORKSPACE", fwd, "PROJECT_DIR", cfg.Dir, "SCHEMA", string.IsNullOrEmpty(schema) ? "public" : schema,
            "TABLES", tables.Count == 0 ? "(none yet: no specification and no --entity; describe the tables by hand)\n" : tl.ToString(),
            "SPEC", spec != null ? "`spec/spec.json` and `spec/spec.md` (copied from the project)" : "no specification yet (run `eqlmaker record` and `eqlmaker spec` first, or describe the screens by hand)"))
            text = text.Replace("{{" + kv.Key + "}}", Convert.ToString(kv.Value));
        string guide = Path.Combine(dir, "CLAUDE-APP.md");
        File.WriteAllText(guide, text, new UTF8Encoding(false));
        return new JO("workspace", dir, "app", app, "server", server, "page", Path.Combine(dir, "app", "app.html"), "capabilities", Path.Combine(dir, "app", "capabilities.json"),
            "description", descFile, "description_kept", keptDesc, "config", cfgFile, "config_kept", keptCfg, "desktop_config", desktop, "instructions", guide,
            "tables", tables.Count, "entities", ents.Count, "columns", dbNote,
            "next", "cd claude-app/mcp-server; npm ci; node bin/set-password.mjs --profile " + app + "; complete the description as CLAUDE-APP.md says; add claude_desktop_config.json to Claude Desktop, restart it; publish app/app.html with app/capabilities.json and choose Install as app");
    }

    // ---------------------------------------------------------------- clone
    public static JO Clone(Config cfg, string renderer, bool force) {
        var spec = NeedSpec(cfg);
        string dir = cfg.Sub("clone");
        if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0 && !force) throw new Exception("the folder 'clone' exists already; use --force to prepare it again (your files with other names stay)");
        CopyTemplate(Share.File("templates", "clone"), dir, new JO("APP_NAME", Path.GetFileName(cfg.Dir), "DB_ENV", "APP_DB"));
        Directory.CreateDirectory(Path.Combine(dir, "reference")); Directory.CreateDirectory(Path.Combine(dir, "public"));
        string rname = string.IsNullOrEmpty(renderer) ? "backdrop" : renderer; Plugin plugin = null;
        if (rname != "backdrop") { plugin = Plugin.Find(cfg.Plugins(), "clone-renderer", rname); if (plugin == null) throw new Exception("clone renderer '" + rname + "' not found (built-in: backdrop; others are plugins of type clone-renderer)"); }
        var pages = new List<object>(); var writing = new HashSet<string>();
        // controls that changed rows when they were used: they are greyed out in the clone
        foreach (JO s in spec.Arr("steps")) foreach (JO a in s.Arr("actions")) if (a.Int("transactions", 0) > 0 && a.Str("text", "").StartsWith("click \"")) { string t = a.Str("text"); int e = t.IndexOf('"', 7); if (e > 7) writing.Add(a.Str("mask") + "\u0001" + t.Substring(7, e - 7)); }
        foreach (JO m in spec.Arr("masks")) {
            var shots = m.Strings("shots");
            if (shots.Count == 0) { pages.Add(new JO("mask", m.Str("id"), "title", m.Str("title"), "page", null, "note", "no reference picture was recorded for this mask")); continue; }
            // the last picture of the mask shows it filled in
            string shot = shots[shots.Count - 1];
            string src = Path.Combine(cfg.RecordingsDir, shot.Replace('/', Path.DirectorySeparatorChar));
            string refPng = Path.Combine(dir, "reference", m.Str("id") + ".png");
            File.Copy(src, refPng, true);
            JO controls = File.Exists(Path.ChangeExtension(src, ".json")) ? Json.ReadFile(Path.ChangeExtension(src, ".json")) : null;
            var ctl = new List<object>();
            if (controls != null) foreach (JO c in controls.Arr("controls")) { c["writes"] = writing.Contains(m.Str("title") + "\u0001" + c.Str("text")); ctl.Add(c); }
            int w, h; using (var bmp = new Bitmap(refPng)) { w = bmp.Width; h = bmp.Height; }
            var request = new JO("mask", new JO("id", m.Str("id"), "title", m.Str("title")), "reference", "../reference/" + m.Str("id") + ".png", "width", w, "height", h, "controls", ctl,
                "queries", m["queries"], "tables_read", m["tables_read"], "out", Path.Combine(dir, "public"));
            string page;
            if (plugin == null) page = BackdropRenderer(request);
            else page = RunRenderer(plugin, request);
            pages.Add(new JO("mask", m.Str("id"), "title", m.Str("title"), "page", "public/" + page, "reference", "reference/" + m.Str("id") + ".png", "controls", ctl.Count));
        }
        Json.WriteFile(Path.Combine(dir, "masks.json"), new JO("renderer", rname, "masks", pages));
        string guide = Path.Combine(dir, "CLONE.md");
        File.WriteAllText(guide, Prompt(cfg, "clone"), new UTF8Encoding(false));
        return new JO("workspace", dir, "renderer", rname, "masks", pages.Count, "instructions", guide,
            "next", "fill the pages as CLONE.md says (MCP prompt 'clone'), take pictures of them (eqlmaker web-shot), then: eqlmaker verify screens clone/reference <your pictures>");
    }

    static string Html(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }

    /// <summary>Built-in clone renderer: the reference picture as backdrop, one positioned element per recorded
    /// control. It is a scaffold: until data is bound, the page shows exactly the reference picture.</summary>
    static string BackdropRenderer(JO req) {
        var m = req.Obj("mask"); var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>" + Html(m.Str("title")) + " (archive)</title>\n<link rel=\"stylesheet\" href=\"clone.css\"></head>\n<body>\n");
        sb.Append("<div class=\"mask\" id=\"" + Html(m.Str("id")) + "\" style=\"width:" + req.Int("width", 0) + "px;height:" + req.Int("height", 0) + "px;background-image:url('" + req.Str("reference") + "')\">\n");
        int n = 0;
        foreach (JO c in req.Arr("controls")) {
            string cls = c.Str("class", ""); bool button = cls.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0, list = cls.IndexOf("ListView", StringComparison.OrdinalIgnoreCase) >= 0 || cls.IndexOf("LISTBOX", StringComparison.OrdinalIgnoreCase) >= 0, edit = cls.IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!button && !list && !edit) continue;
            string kind = button ? "button" : list ? "list" : "field";
            sb.Append("  <div class=\"ctl " + kind + (c.Bool("writes", false) ? " archive-off" : "") + "\" data-control=\"" + (++n) + "\" data-text=\"" + Html(c.Str("text")) + "\" style=\"left:" + c.Int("x", 0) + "px;top:" + c.Int("y", 0) + "px;width:" + c.Int("w", 0) + "px;height:" + c.Int("h", 0) + "px\""
                + (c.Bool("writes", false) ? " title=\"not available in the archive (this function changes data)\"" : "") + "></div>\n");
        }
        sb.Append("</div>\n<script src=\"clone.js\"></script>\n</body></html>\n");
        string name = m.Str("id") + ".html";
        File.WriteAllText(Path.Combine(req.Str("out"), name), sb.ToString(), new UTF8Encoding(false));
        return name;
    }

    /// <summary>Clone renderer plugin: gets the request as one JSON line on standard input, writes its files into
    /// request.out and answers with one JSON line {"page": "file.html"}.</summary>
    static string RunRenderer(Plugin p, JO request) {
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + p.Manifest.Str("command") + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true; psi.WorkingDirectory = p.Dir;
        psi.StandardOutputEncoding = Encoding.UTF8;
        PluginIo.NoByteOrderMark();
        using (var proc = Process.Start(psi)) {
            var w = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)); w.Write(Json.Write(request) + "\n"); w.Flush(); proc.StandardInput.Close();
            string err = null; var t = new System.Threading.Thread(delegate () { err = proc.StandardError.ReadToEnd(); }); t.Start();
            string o = proc.StandardOutput.ReadToEnd(); proc.WaitForExit(60000); t.Join(2000);
            if (proc.ExitCode != 0) throw new Exception("clone renderer '" + p.Name + "' failed: " + (err ?? "").Trim());
            foreach (var line in o.Split('\n')) { if (line.Trim().StartsWith("{")) { var r = Json.ParseObject(line.Trim()); if (r.Str("page") != null) return r.Str("page"); } }
            throw new Exception("clone renderer '" + p.Name + "' did not answer with {\"page\": ...}");
        }
    }

    // ---------------------------------------------------------------- enrich
    static string Sha(string file) { using (var sha = SHA256.Create()) using (var fs = File.OpenRead(file)) { var sb = new StringBuilder(); foreach (var b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2")); return sb.ToString(); } }
    static JO Hashes(string dir) {
        var o = new JO();
        if (!Directory.Exists(dir)) return o;
        var files = new List<string>(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)); files.Sort(StringComparer.Ordinal);
        foreach (var f in files) { string rel = f.Substring(dir.Length + 1).Replace('\\', '/'); if (rel.StartsWith("node_modules/") || rel.Contains("/node_modules/")) continue; o[rel] = Sha(f); }
        return o;
    }
    static string FindingsFile(Config cfg, string name) {
        string root = cfg.Sub("findings");
        if (!Directory.Exists(root)) throw new Exception("no findings yet: run 'eqlmaker video-import' first");
        if (!string.IsNullOrEmpty(name)) { string f = Path.Combine(root, name, "findings.json"); if (!File.Exists(f)) throw new Exception("findings not found: " + f); return f; }
        var dirs = new List<string>(); foreach (var d in Directory.GetDirectories(root)) if (File.Exists(Path.Combine(d, "findings.json"))) dirs.Add(d);
        if (dirs.Count == 0) throw new Exception("no findings yet: run 'eqlmaker video-import' first");
        if (dirs.Count > 1) throw new Exception("several sets of findings exist: name one with --findings <name>");
        return Path.Combine(dirs[0], "findings.json");
    }
    static List<JO> AllFindings(JO findings) {
        var l = new List<JO>();
        foreach (JO op in findings.Arr("operations")) foreach (JO f in op.Arr("findings")) { var x = new JO(); foreach (var kv in f) x[kv.Key] = kv.Value; x["operation"] = op.Str("id"); x["step"] = op.Obj("step") != null ? op.Obj("step").Str("caption") : null; l.Add(x); }
        return l;
    }

    /// <summary>Starts the step: remembers the state of the web application (and, when two recordings are named,
    /// the result of the database comparison) and writes the instructions with all findings.</summary>
    public static JO EnrichPrepare(Config cfg, string findingsName, string verifyA, string verifyB) {
        string ff = FindingsFile(cfg, findingsName); var findings = Json.ReadFile(ff);
        string web = cfg.Sub("web");
        if (!Directory.Exists(web)) throw new Exception("no web application yet (folder 'web'): run 'eqlmaker generate' and build it first");
        string dir = cfg.Sub("enrich", findings.Str("name")); Directory.CreateDirectory(dir);
        JO before = null;
        if (!string.IsNullOrEmpty(verifyA) && !string.IsNullOrEmpty(verifyB)) { var r = VerifyDb.Run(cfg, verifyA, verifyB, new List<string>(), new List<string>(), new List<string>(), "enrich-before-" + findings.Str("name")); before = new JO("a", verifyA, "b", verifyB, "equal", r["equal"], "equal_rows", r["equal_rows"], "only_in_a", r["only_in_a"], "only_in_b", r["only_in_b"], "report", r["report"]); }
        var all = AllFindings(findings);
        Json.WriteFile(Path.Combine(dir, "state.json"), new JO("format", "eqlmaker-enrich", "version", DbEvent.FormatVersion, "findings", ff, "web_before", Hashes(web), "verify_before", before, "decisions", new JO()));
        string guide = Path.Combine(dir, "ENRICH.md");
        File.WriteAllText(guide, Prompt(cfg, "enrich", findings.Str("name")), new UTF8Encoding(false));
        return new JO("workspace", dir, "instructions", guide, "findings", all.Count, "verify_before", before == null ? "not run (name two recordings to compare before and after)" : (object)before["equal"],
            "next", "go through ENRICH.md (MCP prompt 'enrich'); record each decision with: eqlmaker enrich --decide <finding> --status adopted|rejected|open --note \"...\"; then: eqlmaker enrich --finish");
    }

    static string EnrichDir(Config cfg, string findingsName) {
        string root = cfg.Sub("enrich");
        if (!string.IsNullOrEmpty(findingsName)) return Path.Combine(root, findingsName);
        var dirs = new List<string>(); if (Directory.Exists(root)) foreach (var d in Directory.GetDirectories(root)) if (File.Exists(Path.Combine(d, "state.json"))) dirs.Add(d);
        if (dirs.Count == 0) throw new Exception("enrich was not started: run 'eqlmaker enrich' first");
        if (dirs.Count > 1) throw new Exception("several enrich runs exist: name one with --findings <name>");
        return dirs[0];
    }

    public static JO EnrichDecide(Config cfg, string findingsName, string id, string status, string note, List<string> files) {
        if (status != "adopted" && status != "rejected" && status != "open") throw new Exception("--status must be adopted, rejected or open");
        if (string.IsNullOrEmpty(note)) throw new Exception("a note is required: say what was taken over, or why not");
        string dir = EnrichDir(cfg, findingsName); var state = Json.ReadFile(Path.Combine(dir, "state.json"));
        var all = AllFindings(Json.ReadFile(state.Str("findings")));
        if (!all.Exists(delegate (JO f) { return f.Str("id") == id; })) throw new Exception("unknown finding: " + id);
        state.Obj("decisions")[id] = new JO("status", status, "note", note, "files", files);
        Json.WriteFile(Path.Combine(dir, "state.json"), state);
        return new JO("finding", id, "status", status, "decided", state.Obj("decisions").Count, "of", all.Count);
    }

    /// <summary>Ends the step: what changed in the web application, the comparison after, and the report that lists
    /// every finding as adopted, rejected or open with its reason.</summary>
    public static JO EnrichFinish(Config cfg, string findingsName, string verifyA, string verifyB) {
        string dir = EnrichDir(cfg, findingsName); var state = Json.ReadFile(Path.Combine(dir, "state.json"));
        var findings = Json.ReadFile(state.Str("findings")); var all = AllFindings(findings);
        var beforeH = state.Obj("web_before"); var afterH = Hashes(cfg.Sub("web"));
        var changed = new List<object>(); var added = new List<object>(); var removed = new List<object>();
        foreach (var kv in afterH) { if (!beforeH.Has(kv.Key)) added.Add(kv.Key); else if (beforeH.Str(kv.Key) != (string)kv.Value) changed.Add(kv.Key); }
        foreach (var kv in beforeH) if (!afterH.Has(kv.Key)) removed.Add(kv.Key);
        JO before = state.Obj("verify_before"), after = null;
        if (string.IsNullOrEmpty(verifyA) && before != null) { verifyA = before.Str("a"); verifyB = before.Str("b"); }
        if (!string.IsNullOrEmpty(verifyA) && !string.IsNullOrEmpty(verifyB)) { var r = VerifyDb.Run(cfg, verifyA, verifyB, new List<string>(), new List<string>(), new List<string>(), "enrich-after-" + findings.Str("name")); after = new JO("a", verifyA, "b", verifyB, "equal", r["equal"], "equal_rows", r["equal_rows"], "only_in_a", r["only_in_a"], "only_in_b", r["only_in_b"], "report", r["report"]); }
        var items = new List<object>(); int adopted = 0, rejected = 0, open = 0; var dec = state.Obj("decisions");
        foreach (var f in all) {
            var d = dec.Obj(f.Str("id"));
            string status = d == null ? "open" : d.Str("status"), note = d == null ? "no decision was recorded" : d.Str("note");
            if (status == "adopted") adopted++; else if (status == "rejected") rejected++; else open++;
            items.Add(new JO("finding", f.Str("id"), "kind", f.Str("kind"), "operation", f.Str("operation"), "step", f.Str("step"), "what", f.Str("text") ?? f.Str("file") ?? f.Str("shape"), "status", status, "reason", note, "files", d == null ? null : d["files"]));
        }
        var warnings = new List<object>();
        if (adopted > 0 && changed.Count + added.Count == 0) warnings.Add("findings are marked as adopted but no file of the web application changed");
        if (before != null && after != null && before.Bool("equal", false) && !after.Bool("equal", false)) warnings.Add("the database comparison was equal before and differs now: see " + after.Str("report"));
        if (after != null && before != null && before.Str("b") == after.Str("b")) warnings.Add("the comparison after used the same recording as before: record the changed application again to prove its behaviour");
        var rep = new JO("format", "eqlmaker-enrich-report", "version", DbEvent.FormatVersion, "findings", findings.Str("name"), "adopted", adopted, "rejected", rejected, "open", open, "items", items,
            "web_changes", new JO("changed", changed, "added", added, "removed", removed), "verify_before", before, "verify_after", after, "warnings", warnings);
        Json.WriteFile(Path.Combine(dir, "report.json"), rep);
        var sb = new StringBuilder("# Enrich report: " + findings.Str("name") + "\n\n" + adopted + " adopted, " + rejected + " rejected, " + open + " open.\n\n| finding | kind | step | status | reason |\n|---|---|---|---|---|\n");
        foreach (JO i in items) sb.Append("| " + i.Str("finding") + " | " + i.Str("kind") + " | " + (i.Str("step") ?? "-") + " | " + i.Str("status") + " | " + (i.Str("reason") ?? "").Replace("|", "/").Replace("\n", " ") + " |\n");
        sb.Append("\nFiles of the web application: " + changed.Count + " changed, " + added.Count + " added, " + removed.Count + " removed.\n");
        if (before != null || after != null) sb.Append("\nDatabase comparison before: " + (before == null ? "not run" : before.Bool("equal", false) ? "equal" : "different") + "; after: " + (after == null ? "not run" : after.Bool("equal", false) ? "equal" : "different") + ".\n");
        foreach (var wn in warnings) sb.Append("\nWarning: " + wn + "\n");
        File.WriteAllText(Path.Combine(dir, "report.md"), sb.ToString(), new UTF8Encoding(false));
        return new JO("report", Path.Combine(dir, "report.json"), "readable", Path.Combine(dir, "report.md"), "adopted", adopted, "rejected", rejected, "open", open, "files_changed", changed.Count + added.Count + removed.Count,
            "verify_after", after == null ? "not run" : (object)after["equal"], "warnings", warnings);
    }

    // ---------------------------------------------------------------- instructions
    public static string Prompt(Config cfg, string name) { return Prompt(cfg, name, null); }
    public static string Prompt(Config cfg, string name, string findingsName) {
        var spec = NeedSpec(cfg);
        var vars = new JO("PROJECT", Path.GetFileName(cfg.Dir), "PROJECT_DIR", cfg.Dir, "STEPS", StepList(spec), "MASKS", MaskList(spec), "STEP_COUNT", spec.Arr("steps").Count, "MASK_COUNT", spec.Arr("masks").Count,
            "OPEN_POINTS", spec.Arr("open_points").Count == 0 ? "(none)" : "- " + string.Join("\n- ", spec.Strings("open_points").ToArray()));
        if (name == "enrich") {
            var findings = Json.ReadFile(FindingsFile(cfg, findingsName)); var sb = new StringBuilder();
            foreach (JO op in findings.Arr("operations")) {
                if (op.Arr("findings").Count == 0) continue;
                sb.Append("### " + op.Str("id") + (op.Obj("step") != null ? " - step \"" + op.Obj("step").Str("caption") + "\" (match " + op.Obj("step").Str("score") + ", " + op.Obj("step").Str("method") + ")" : " - no step of the specification matches") + ", video " + op.Str("video_from") + " s to " + op.Str("video_to") + " s\n\n");
                foreach (JO f in op.Arr("findings")) {
                    if (f.Str("kind") == "said") sb.Append("- " + f.Str("id") + " said: \"" + f.Str("text") + "\"" + (f.Arr("words_not_in_spec").Count > 0 ? " (words not in the specification of this step: " + string.Join(", ", f.Strings("words_not_in_spec").ToArray()) + ")" : "") + "\n");
                    else if (f.Str("kind") == "seen") sb.Append("- " + f.Str("id") + " seen: picture findings/" + findings.Str("name") + "/" + f.Str("file") + "\n");
                    else sb.Append("- " + f.Str("id") + " database: statement not in the specification: `" + f.Str("shape") + "`\n");
                }
                sb.Append("\n");
            }
            var un = findings.Obj("unassigned");
            if (un.Arr("said").Count + un.Arr("seen").Count > 0) sb.Append("Not tied to an operation (for context only): said " + string.Join(", ", un.Strings("said").ToArray()) + "; seen " + string.Join(", ", un.Strings("seen").ToArray()) + " - see findings.json.\n");
            vars["FINDINGS"] = sb.Length == 0 ? "(no findings)" : sb.ToString(); vars["FINDINGS_NAME"] = findings.Str("name");
        }
        return Fill(name, vars);
    }
}
}
