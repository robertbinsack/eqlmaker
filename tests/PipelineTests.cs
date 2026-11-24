// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Plugins, guided steps, pair video, MCP server, and the whole pipeline with the sample application.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace EqlMaker.Tests {

public static class Fixture {
    /// <summary>A project with the hand-written recording of TimelineTests, reference pictures and a specification.</summary>
    public static Config Project(string name, bool withPlugins) {
        string dir = T.TempDir(name); var cfg = Config.Load(dir);
        string rec = TimelineTests.MakeRecording(Path.Combine(cfg.RecordingsDir, "rec"), false);
        Directory.CreateDirectory(Path.Combine(rec, "shots"));
        string[] titles = { "Shop", "Customer 17", "New invoice" }; string[] buttons = { "New customer", "Save customer", "Save invoice" };
        for (int i = 0; i < 3; i++) {
            using (var bmp = new Bitmap(300, 200, PixelFormat.Format24bppRgb)) {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(240, 240, 240)); g.FillRectangle(Brushes.White, 10, 10, 200, 120); g.DrawRectangle(Pens.Gray, 20, 150, 100, 26); }
                bmp.Save(Path.Combine(rec, "shots", "000" + (i + 1) + ".png"), ImageFormat.Png);
            }
            Json.WriteFile(Path.Combine(rec, "shots", "000" + (i + 1) + ".json"), new JO("window", titles[i], "width", 300, "height", 200, "controls", new List<object> {
                new JO("text", "", "class", "WindowsForms10.SysListView32.app", "x", 10, "y", 10, "w", 200, "h", 120, "enabled", true),
                new JO("text", buttons[i], "class", "WindowsForms10.BUTTON.app", "x", 20, "y", 150, "w", 100, "h", 26, "enabled", true),
                new JO("text", "Name", "class", "WindowsForms10.STATIC.app", "x", 220, "y", 10, "w", 60, "h", 20, "enabled", true) }));
        }
        if (withPlugins) {
            var raw = Config.Example(); raw["plugins"] = new List<object> { Path.Combine(T.RepoRoot, "plugins", "examples") };
            Json.WriteFile(Path.Combine(dir, Config.FileName), raw); cfg = Config.Load(dir);
        }
        Spec.Run(cfg, new List<string>());
        return cfg;
    }
}

public static class PluginTests {
    public static void ExamplesAreValidAndOfEveryType() {
        var list = Plugin.LoadAll(new[] { Path.Combine(T.RepoRoot, "plugins", "examples") });
        var types = list.ConvertAll(delegate (Plugin p) { return p.Type + ":" + p.Name; }); types.Sort(StringComparer.Ordinal);
        T.Eq("clone-renderer:outline,source:jsonl-file,video-layout:stacked,web-template:minimal", string.Join(",", types.ToArray()), "one example per plugin type");
    }

    public static void BrokenManifestsAreRefusedWithTheReason() {
        string d = T.TempDir("plugins-bad");
        Action<string, string, string> bad = delegate (string json, string part, string what) {
            string p = Path.Combine(d, what.Replace(' ', '-')); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "plugin.json"), json);
            T.Throws(delegate { Plugin.Load(p); }, part, what);
        };
        bad("{\"type\":\"theme\",\"name\":\"x\"}", "type must be one of", "unknown type");
        bad("{\"type\":\"source\",\"name\":\"my source\",\"command\":\"x\"}", "name", "name with a blank");
        bad("{\"type\":\"source\",\"name\":\"x\"}", "needs \"command\"", "source without command");
        bad("{\"type\":\"web-template\",\"name\":\"x\"}", "template files is missing", "template without files");
        bad("{\"type\":\"video-layout\",\"name\":\"x\"}", "needs \"filter\"", "layout without filter");
        T.Throws(delegate { Plugin.Load(Path.Combine(d, "nothing")); }, "no plugin.json", "folder without manifest");
    }

    public static void SourcePluginDeliversEventsThroughTheSameInterface() {
        string d = T.TempDir("plugin-source"), feed = Path.Combine(d, "feed.jsonl");
        File.WriteAllText(feed, Json.Write(new DbEvent { Ts = 1, Kind = "statement", Sql = "SELECT 'before the capture'" }.ToJson()) + "\n");
        var raw = new JO("source", "jsonl-file", "plugins", new List<object> { Path.Combine(T.RepoRoot, "plugins", "examples") }, "jsonl-file", new JO("file", feed));
        Json.WriteFile(Path.Combine(d, Config.FileName), raw);
        var cfg = Config.Load(d);
        var src = cfg.CreateSource();
        T.Eq("jsonl-file", src.Name, "the source of the configuration is the plugin");
        src.Start(cfg.Capture());
        File.AppendAllText(feed, Json.Write(new DbEvent { Ts = 1795630000000000L, Kind = "statement", Session = "s1", Application = "other tool", Sql = "SELECT 2" }.ToJson()) + "\n" +
                                 Json.Write(new DbEvent { Ts = 1795630000000500L, Kind = "execute", Session = "s1", Sql = "UPDATE t SET a = $1", Params = new List<string> { "x" } }.ToJson()) + "\n");
        var ev = src.Stop();
        T.Eq(2, ev.Count, "only what was added during the capture"); T.Eq("SELECT 2", ev[0].Sql, "first"); T.Eq("x", ev[1].Params[0], "parameters"); T.Eq("jsonl-file", ev[0].Source, "source name filled in");
        T.True(src.Info.Obj("fields") != null, "the plugin says what it delivers");
        // unknown source: the error lists what exists
        Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "nope"));
        T.Throws(delegate { Config.Load(d).CreateSource(); }, "known: eqlserver, postgres", "unknown source");
    }

    public static void WebTemplatePluginIsUsedByGenerate() {
        var cfg = Fixture.Project("plugin-template", true);
        var r = Guided.Generate(cfg, "minimal", false);
        T.Eq("minimal", r.Str("template"), "template");
        string html = File.ReadAllText(cfg.Sub("web", "index.html"));
        T.Contains(html, "<title>plugin-template</title>", "variables replaced"); T.True(!html.Contains("{{"), "no placeholder left");
        T.True(File.Exists(cfg.Sub("web", "spec", "spec.md")) && File.Exists(cfg.Sub("web", "GENERATE.md")), "specification and instructions next to it");
        T.Throws(delegate { Guided.Generate(cfg, "no-such", true); }, "not found", "unknown template");
    }

    public static void CloneRendererPluginIsUsedByClone() {
        var cfg = Fixture.Project("plugin-renderer", true);
        var r = Guided.Clone(cfg, "outline", false);
        T.Eq(3, r.Int("masks", 0), "masks");
        string page = File.ReadAllText(cfg.Sub("clone", "public", "m-customer.outline.html"));
        T.Contains(page, "Save customer", "control text drawn"); T.Contains(page, "class=\"ctl archive-off\"", "the control that changed data is marked");
        T.Contains(File.ReadAllText(cfg.Sub("clone", "masks.json")), "m-customer.outline.html", "page listed");
    }

    public static void VideoLayoutPluginIsUsedByVideo() {
        string ffmpeg = T.Ffmpeg; Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", ffmpeg);
        var cfg = Fixture.Project("plugin-layout", true);
        foreach (var n in new[] { "orig", "web" }) {
            string d = Path.Combine(cfg.RecordingsDir, n); Directory.CreateDirectory(d); string o;
            T.Eq(0, T.Run(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=" + (n == "orig" ? "6" : "5") + ":size=320x240:rate=15 -c:v mpeg4 \"" + Path.Combine(d, "video.mp4") + "\"", null, out o, 60000), "test video " + o);
            var ch = n == "orig" ? new List<object> { new JO("start", 0.0, "end", 2.0, "caption", "One"), new JO("start", 2.0, "end", 6.0, "caption", "Two"), new JO("start", 6.0, "end", 6.0, "caption", "Only original") }
                                 : new List<object> { new JO("start", 0.0, "end", 3.0, "caption", "One"), new JO("start", 3.0, "end", 5.0, "caption", "Two") };
            Json.WriteFile(Path.Combine(d, "recording.json"), new JO("format", "eqlmaker-recording", "version", "0.1", "name", n, "t0_us", 1L, "duration", n == "orig" ? 6.0 : 5.0, "video", "video.mp4", "chapters", ch));
        }
        var r = VideoPair.Run(cfg, "orig", "web", "stacked", "pair", false);
        T.Eq(2, r.Int("chapters", 0), "chapters with the same caption"); T.Eq(1, r.Int("unmatched", 0), "one chapter without partner");
        T.Eq(7.0, r.Num("duration", 0), "each chapter lasts as long as the longer side (3 + 4)");
        var pair = Json.ReadFile(Path.Combine(r.Str("folder"), "pair.json"));
        T.Eq("stacked", pair.Str("layout"), "layout"); T.Eq("column", pair.Str("player"), "player arrangement");
        T.Eq(3.0, ((JO)pair.Arr("chapters")[1]).Num("start", 0), "second chapter starts when the longer first one ends");
        var probe = Media.Probe(cfg, r.Str("video"));
        T.Near(7.0, probe.Num("duration", 0), 0.5, "combined video length"); T.True(probe.Int("height", 0) > probe.Int("width", 0), "stacked: higher than wide (" + probe.Int("width", 0) + "x" + probe.Int("height", 0) + ")");
        T.Contains(File.ReadAllText(r.Str("player")), "\"caption\":\"Two\"", "player page with the data");
        var side = VideoPair.Run(cfg, "orig", "web", null, "pair2", false);
        var p2 = Media.Probe(cfg, side.Str("video"));
        T.True(p2.Int("width", 0) > p2.Int("height", 0) * 2, "built-in layout: side by side (" + p2.Int("width", 0) + "x" + p2.Int("height", 0) + ")");
    }
}

public static class GuidedTests {
    public static void GenerateIsAWorkspaceAndInstructionsNotAnApplication() {
        var cfg = Fixture.Project("guided-generate", false);
        var r = Guided.Generate(cfg, null, false);
        T.Eq("default", r.Str("template"), "built-in template");
        T.True(File.Exists(cfg.Sub("web", "server.js")) && File.Exists(cfg.Sub("web", "lib", "entity.js")) && File.Exists(cfg.Sub("web", "public", "style.css")), "template copied");
        T.True(!File.ReadAllText(cfg.Sub("web", "app", "config.js")).Contains("{{"), "placeholders replaced");
        string guide = File.ReadAllText(r.Str("instructions"));
        T.Contains(guide, "It does **not** write", "the instructions say who builds"); T.Contains(guide, "s3 \"New invoice\"", "steps of the specification listed"); T.Contains(guide, "verify_db", "the proof is named");
        T.True(!guide.Contains("{{"), "no placeholder left in the instructions");
        T.Throws(delegate { Guided.Generate(cfg, null, false); }, "exists already", "second call without --force");
        File.WriteAllText(cfg.Sub("web", "my-page.js"), "// mine"); Guided.Generate(cfg, null, true);
        T.True(File.Exists(cfg.Sub("web", "my-page.js")), "--force keeps files of the user");
        string dir2 = T.TempDir("guided-nospec");
        T.Throws(delegate { Guided.Generate(Config.Load(dir2), null, false); }, "run 'eqlmaker spec' first", "without a specification");
    }

    public static void CloneScaffoldShowsTheReferenceAndGreysOutWritingControls() {
        var cfg = Fixture.Project("guided-clone", false);
        var r = Guided.Clone(cfg, null, false);
        T.Eq(3, r.Int("masks", 0), "masks");
        T.True(File.Exists(cfg.Sub("clone", "reference", "m-shop.png")) && File.Exists(cfg.Sub("clone", "server.js")) && File.Exists(cfg.Sub("clone", "public", "clone.css")), "reference pictures and read-only server");
        string page = File.ReadAllText(cfg.Sub("clone", "public", "m-new-invoice.html"));
        T.Contains(page, "width:300px;height:200px;background-image:url('../reference/m-new-invoice.png')", "reference picture as backdrop at its size");
        T.Contains(page, "class=\"ctl button archive-off\" data-control=\"2\" data-text=\"Save invoice\" style=\"left:20px;top:150px;width:100px;height:26px\"", "the button that changed data is positioned and greyed out");
        T.Contains(page, "class=\"ctl list\"", "list region"); T.True(!page.Contains("STATIC") && !page.Contains("data-text=\"Name\""), "labels stay part of the picture");
        string shop = File.ReadAllText(cfg.Sub("clone", "public", "m-shop.html"));
        T.Contains(shop, "class=\"ctl button\" data-control=\"2\" data-text=\"New customer\"", "a button that only opens a mask is not greyed out");
        string guide = File.ReadAllText(r.Str("instructions"));
        T.Contains(guide, "READ ONLY", "read-only rule"); T.Contains(guide, "m-customer", "masks listed");
        string srv = File.ReadAllText(cfg.Sub("clone", "server.js"));
        T.Contains(srv, "BEGIN TRANSACTION READ ONLY", "the server opens read-only transactions");
    }

    public static void EnrichRecordsDecisionsAndReportsEveryFinding() {
        var cfg = Fixture.Project("guided-enrich", false);
        Guided.Generate(cfg, null, false);
        Directory.CreateDirectory(cfg.Sub("findings", "session"));
        var findings = new JO("format", "eqlmaker-findings", "version", "0.1", "name", "session",
            "operations", new List<object> {
                new JO("id", "op1", "step", new JO("id", "s3", "caption", "New invoice", "score", 0.8, "method", "statement shapes"), "video_from", 5.0, "video_to", 6.0,
                    "findings", new List<object> {
                        new JO("id", "f1", "kind", "said", "ref", "t1", "text", "Group A customers get five percent from five hundred.", "words_not_in_spec", new List<object> { "percent", "hundred" }, "status", "open"),
                        new JO("id", "f2", "kind", "seen", "ref", "k1", "file", "keyframes/k001.png", "status", "open"),
                        new JO("id", "f3", "kind", "database", "shape", "SELECT sum(gross) FROM invoice WHERE customer_id = $1", "example", "SELECT sum(gross) FROM invoice WHERE customer_id = $1", "status", "open") }),
                new JO("id", "op2", "step", null, "video_from", 9.0, "video_to", 9.5, "findings", new List<object>()) },
            "unassigned", new JO("said", new List<object> { "t9" }, "seen", new List<object>()));
        Json.WriteFile(cfg.Sub("findings", "session", "findings.json"), findings);

        var p = Guided.EnrichPrepare(cfg, null, null, null);
        T.Eq(3, p.Int("findings", 0), "findings");
        string guide = File.ReadAllText(p.Str("instructions"));
        T.Contains(guide, "f1 said: \"Group A customers get five percent", "what was said"); T.Contains(guide, "words not in the specification of this step: percent, hundred", "new words");
        T.Contains(guide, "f2 seen: picture findings/session/keyframes/k001.png", "what was seen"); T.Contains(guide, "f3 database: statement not in the specification", "what the database did");
        T.Contains(guide, "EQLMaker has not judged the findings", "the instructions say who judges");

        T.Throws(delegate { Guided.EnrichDecide(cfg, null, "f1", "done", "x", new List<string>()); }, "adopted, rejected or open", "unknown status");
        T.Throws(delegate { Guided.EnrichDecide(cfg, null, "f1", "adopted", "", new List<string>()); }, "note is required", "decision without reason");
        T.Throws(delegate { Guided.EnrichDecide(cfg, null, "f77", "open", "x", new List<string>()); }, "unknown finding", "unknown finding");
        Guided.EnrichDecide(cfg, null, "f1", "adopted", "discount rule implemented in app/invoice.js", new List<string> { "app/invoice.js" });
        Guided.EnrichDecide(cfg, null, "f2", "rejected", "shows the same mask as the reference picture", new List<string>());
        File.WriteAllText(cfg.Sub("web", "app", "invoice.js"), "// discount: group A, 5 % from 500\n");
        File.AppendAllText(cfg.Sub("web", "app", "routes.js"), "\n// changed\n");

        var r = Guided.EnrichFinish(cfg, null, null, null);
        T.Eq(1, r.Int("adopted", 0), "adopted"); T.Eq(1, r.Int("rejected", 0), "rejected"); T.Eq(1, r.Int("open", 0), "the finding without a decision is open");
        T.Eq(2, r.Int("files_changed", 0), "files of the web application that changed");
        var rep = Json.ReadFile(r.Str("report"));
        var f3 = rep.Arr("items").Find(delegate (object x) { return ((JO)x).Str("finding") == "f3"; }) as JO;
        T.Eq("open", f3.Str("status"), "status"); T.Eq("no decision was recorded", f3.Str("reason"), "reason of an undecided finding");
        T.Eq("app/invoice.js", rep.Obj("web_changes").Strings("added")[0], "added file"); T.Eq("app/routes.js", rep.Obj("web_changes").Strings("changed")[0], "changed file");
        string md = File.ReadAllText(r.Str("readable"));
        T.Contains(md, "| f1 | said | New invoice | adopted | discount rule implemented in app/invoice.js |", "readable report");
    }
}

public static class McpTests {
    static List<JO> Talk(string project, params string[] lines) {
        var psi = new ProcessStartInfo(Path.Combine(T.BinDir, "eqlmaker.exe"), "mcp --project \"" + project + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true; psi.StandardOutputEncoding = Encoding.UTF8;
        using (var p = Process.Start(psi)) {
            p.ErrorDataReceived += delegate { }; p.BeginErrorReadLine();
            var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
            foreach (var l in lines) w.Write(l + "\n");
            w.Flush(); p.StandardInput.Close();
            var res = new List<JO>(); string line;
            while ((line = p.StandardOutput.ReadLine()) != null) if (line.Trim() != "") res.Add(Json.ParseObject(line));
            p.WaitForExit(30000);
            return res;
        }
    }

    public static void ProtocolToolsAndPrompts() {
        var cfg = Fixture.Project("mcp", false);
        var m = Talk(cfg.Dir,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"no_such_tool\",\"arguments\":{}}}",
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"nope\"}",
            "this is not JSON",
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"correlate\",\"arguments\":{\"recording\":\"rec\"}}}",
            "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"prompts/list\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"prompts/get\",\"params\":{\"name\":\"generate\"}}",
            "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"generate\",\"arguments\":{}}}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"nine\",\"method\":\"ping\"}");
        T.Eq(9, m.Count, "one answer per request, nothing else on standard output");
        T.Eq("eqlmaker", m[0].Obj("result").Obj("serverInfo").Str("name"), "server name"); T.Eq("2025-06-18", m[0].Obj("result").Str("protocolVersion"), "protocol version");
        var tools = m[1].Obj("result").Arr("tools").ConvertAll(delegate (object t) { return ((JO)t).Str("name"); });
        foreach (var t in new[] { "screenshot", "click", "click_control", "list_controls", "web_click", "record_start", "record_stop", "run_scenario", "correlate", "spec", "manual_import", "generate", "clone", "video", "verify_db", "verify_screens", "video_import", "enrich", "enrich_decide", "enrich_finish" })
            T.True(tools.Contains(t), "tool " + t + " is offered");
        foreach (JO t in m[1].Obj("result").Arr("tools")) T.True(t.Str("description").Length >= 6 && t.Obj("inputSchema").Str("type") == "object", "tool " + t.Str("name") + " has a description and a schema");
        var gen = m[1].Obj("result").Arr("tools").Find(delegate (object t) { return ((JO)t).Str("name") == "generate"; }) as JO;
        T.Contains(gen.Str("description"), "guided step", "generate is described as a guided step"); T.Contains(gen.Str("description"), "Not an automatic generator", "and not as a generator");
        T.True(m[2].Obj("result").Bool("isError", false), "unknown tool is a tool error"); T.Eq(-32601, m[3].Obj("error").Int("code", 0), "unknown method is a protocol error");
        string text = ((JO)m[4].Obj("result").Arr("content")[0]).Str("text");
        T.Contains(text, "timeline.json", "correlate ran through the tool"); T.True(File.Exists(Path.Combine(cfg.RecordingsDir, "rec", "timeline.json")), "and wrote its file");
        T.Eq(3, m[5].Obj("result").Arr("prompts").Count, "three prompts: generate, clone, enrich");
        T.Contains(((JO)m[6].Obj("result").Arr("messages")[0]).Obj("content").Str("text"), "Build the web application", "prompt text");
        T.Contains(((JO)m[7].Obj("result").Arr("content")[0]).Str("text"), "instructions_text", "the generate tool returns the instructions");
        T.Eq("nine", m[8].Str("id"), "string ids are echoed");
    }
}

public static class PipelineTests {
    public static void ServerLogWrittenEarlierBecomesARecording() {
        string d = T.TempDir("import-log"); var cfg = Config.Load(d);
        string log = Path.Combine(d, "postgresql.log");
        File.WriteAllText(log, "2026-03-01 10:00:00.120 CET [4711] LOG:  statement: SELECT 1\n2026-03-01 10:00:02.500 CET [4711] LOG:  execute <unnamed>: UPDATE t SET a = $1\n2026-03-01 10:00:02.500 CET [4711] DETAIL:  Parameters: $1 = 'x'\n2026-03-01 10:00:03.000 CET [1] LOG:  checkpoint complete\n");
        var r = Pipeline.ImportLog(cfg, log, "old", "%m [%p] ", 60, null, null);
        T.Eq(2, r.Int("events", 0), "events"); T.Eq("2026-03-01T09:00:00.120000Z", r.Str("from"), "log time converted with the given UTC offset");
        var tl = Timeline.Run(Path.Combine(cfg.RecordingsDir, "old"));
        T.Eq(2, tl.Int("before_first_action", 0), "usable by correlate: events without actions");
        T.Throws(delegate { Pipeline.ImportLog(cfg, log, "x", "%u ", 0, null, null); }, "must contain a time", "prefix check");
    }

    public static void CommandLineReportsUnknownCommandsAndMissingArguments() {
        string o; string exe = Path.Combine(T.BinDir, "eqlmaker.exe"), d = T.TempDir("cli");
        T.Eq(0, T.Run(exe, "version", d, out o, 20000), "version"); T.Eq(Config.Version, o.Trim(), "version text");
        T.Eq(1, T.Run(exe, "frobnicate", d, out o, 20000), "unknown command fails"); T.Contains(o, "unknown command 'frobnicate'", "message");
        T.Eq(1, T.Run(exe, "correlate", d, out o, 20000), "missing argument fails"); T.Contains(o, "missing argument: recording", "message");
        T.Eq(0, T.Run(exe, "init proj", d, out o, 20000), "init"); T.True(File.Exists(Path.Combine(d, "proj", "eqlmaker.json")), "configuration written");
        T.Eq(1, T.Run(exe, "init proj", d, out o, 20000), "init twice fails"); T.Contains(o, "exists already", "message");
        T.Eq(0, T.Run(exe, "help", d, out o, 20000), "help");
        foreach (var c in new[] { "record", "correlate", "spec", "manual", "generate", "clone", "video", "verify", "video-import", "enrich", "mcp" }) T.Contains(o, "eqlmaker " + c, "help lists " + c);
    }

    public static void WebPagePictureWithoutAWindow() {
        try { new Browser().FindExecutable(); } catch (Exception) { T.Skip("no browser"); }
        string d = T.TempDir("webshot"), html = Path.Combine(d, "p.html"), png = Path.Combine(d, "p.png");
        File.WriteAllText(html, "<html><body style='margin:0;background:#fff'><div style='width:200px;height:100px;background:#c00'></div></body></html>");
        Pipeline.WebShot(Config.Load(d), "file:///" + html.Replace('\\', '/'), png, 400, 300, 200);
        using (var bmp = new Bitmap(png)) {
            T.Eq(400, bmp.Width, "width"); T.Eq(300, bmp.Height, "height");
            var red = bmp.GetPixel(100, 50); var white = bmp.GetPixel(300, 200);
            T.True(red.R > 180 && red.G < 40 && white.R > 240 && white.G > 240, "the page was drawn: " + red + " " + white);
        }
    }

    /// <summary>The whole pipeline with the sample application against a real PostgreSQL: the Windows program on
    /// a desktop that is never shown, the browser variant without a window, both recorded and compared.</summary>
    public static void SampleEndToEndOnPlainPostgres() {
        string admin = T.Pg; string node;
        try { string o; if (T.Run(Media.Find(null, "node"), "--version", null, out o, 20000) != 0) throw new Exception(); node = o.Trim(); } catch (Exception) { T.Skip("Node.js not found"); return; }
        if (!Directory.Exists(Path.Combine(SampleRun.SampleDir, "web", "node_modules", "pg"))) T.Skip("run 'npm ci' in examples/sample-app/web first");
        using (var c = new PgClient(admin)) if (c.Scalar("SHOW wal_level") != "logical") T.Skip("the test server has wal_level " + c.Scalar("SHOW wal_level") + ", the comparison needs logical");
        string d = T.TempDir("e2e"); var ci = PgConnInfo.Parse(admin);
        Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres", "connection", "host=" + ci.Host + " port=" + ci.Port + " user=" + ci.User + " dbname=" + ci.Database,
            "capture", new JO("roles", new List<object>(), "databases", new List<object> { SampleRun.Database }, "changes", true), "input", "background"));
        PgConnInfo.SetAdminPassword(ci.ResolvePassword());
        var cfg = Config.Load(d); var log = new List<string>();
        bool ok = SampleRun.Run(cfg, false, false, delegate (string s) { log.Add(s); });
        T.True(ok, "faithful run:\n" + string.Join("\n", log.ToArray()));
        T.True(log.Exists(delegate (string s) { return s.Contains("changes the database exactly like the Windows program"); }), "verify db equal");
        var rec = RecordingData.Load(Path.Combine(cfg.RecordingsDir, "desktop"));
        T.Eq("background", rec.Manifest.Str("input"), "input mode");
        var click = rec.Actions.Find(delegate (JO a) { return a.Str("control") == "Save invoice"; });
        T.True(click != null && click.Str("window") == "New invoice" && File.Exists(Path.Combine(rec.Dir, click.Str("shot").Replace('/', '\\'))), "action with control, window and reference picture");
        T.True(File.Exists(Path.Combine(rec.Dir, Path.ChangeExtension(click.Str("shot").Replace('/', '\\'), ".json"))), "control list next to the reference picture");
        var ins = rec.Events.Find(delegate (DbEvent e) { return e.Kind == "execute" && e.Sql.StartsWith("INSERT INTO invoice ", StringComparison.OrdinalIgnoreCase) && e.Params != null; });
        T.True(ins != null && ins.Application == "sample-desktop", "parameterised statement of the Windows program captured with its parameters");
        var spec = Json.ReadFile(cfg.Sub("spec", "spec.json"));
        var step = spec.Arr("steps").Find(delegate (object s) { return ((JO)s).Str("caption") == "New invoice"; }) as JO;
        var written = step.Arr("writes").ConvertAll(delegate (object w) { return ((JO)w).Str("op") + " " + ((JO)w).Str("table"); });
        foreach (var w in new[] { "update public.counter", "insert public.invoice", "insert public.invoice_line", "update public.article", "insert public.stock_movement" }) T.True(written.Contains(w), "the specification names the hidden side effect: " + w + " (found: " + string.Join(", ", written.ToArray()) + ")");
        T.True(step.Arr("manual").Exists(delegate (object m) { return ((JO)m).Str("title") == "Writing an invoice"; }), "manual chapter linked to the step");
        if (rec.Manifest.Str("video") != null) { T.True(new FileInfo(Path.Combine(rec.Dir, "video.mp4")).Length > 20000, "video written"); T.Note("desktop_video_frames", rec.Manifest.Int("frames", 0)); }
        else T.Note("desktop_video", "not available on this machine: " + string.Join("; ", rec.Manifest.Strings("warnings").ToArray()));
        T.Note("desktop_actions_statements_transactions", rec.Actions.Count + " / " + rec.Events.FindAll(delegate (DbEvent e) { return e.Kind != "transaction"; }).Count + " / " + rec.Events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count);

        // the same with the deliberate defects of the browser variant: the comparison must find them
        log.Clear();
        ok = SampleRun.Run(cfg, true, true, delegate (string s) { log.Add(s); });
        T.True(ok, "flawed run:\n" + string.Join("\n", log.ToArray()));
        string report = File.ReadAllText(cfg.Sub("verify", "db-desktop-web-flawed", "report.txt"));
        T.Contains(report, "RESULT: DIFFERENT", "result"); T.Contains(report, "only A: insert public.stock_movement", "the missing stock movements are found");
        T.Contains(report, "tax=108.34", "tax of the original"); T.Contains(report, "tax=108.33", "tax of the flawed variant (rounded per line)");
        T.Note("flawed_variant", log.Find(delegate (string s) { return s.Contains("deliberate defects"); }));
        // nothing is left behind on the server
        using (var c = new PgClient(admin)) {
            T.Eq("0", c.Scalar("SELECT count(*) FROM pg_replication_slots WHERE slot_name LIKE 'eqlmaker%'"), "no replication slot left");
            T.Eq("0", c.Query("SELECT count(*) FROM pg_db_role_setting s JOIN pg_database d ON d.oid = s.setdatabase WHERE d.datname = $1", SampleRun.Database).Scalar, "log_statement reset");
        }
    }
}
}

namespace EqlMaker.Tests {

public static class PluginCodePageTests {
    static int UnderCodePage(int cp, string args, string dir, out string output) {
        // cmd sets the code page of its (hidden) console; the program started from it inherits that console
        return T.Run("cmd.exe", "/d /c \"chcp " + cp + " >nul && \"" + System.IO.Path.Combine(T.BinDir, "eqlmaker.exe") + "\" " + args + "\"", dir, out output, 120000);
    }

    /// <summary>Plugins are separate programs that get JSON on standard input. The console code page of the
    /// caller (437, 1252, 65001 ...) must not change what they receive: UTF-8 without byte order mark.</summary>
    public static void PluginsGetCleanUtf8WhateverTheConsoleCodePageIs() {
        foreach (int cp in new[] { 65001, 437, 1252 }) {
            var cfg = Fixture.Project("plugin-cp" + cp, true);
            // a control text outside ASCII must arrive intact in the page of the renderer plugin
            string shot = System.IO.Path.Combine(cfg.RecordingsDir, "rec", "shots", "0002.json");
            var j = Json.ReadFile(shot); ((JO)j.Arr("controls")[1])["text"] = "Prüfen €";
            Json.WriteFile(shot, j);
            string o;
            T.Eq(0, UnderCodePage(cp, "clone --renderer outline --project \"" + cfg.Dir + "\"", cfg.Dir, out o), "clone with a renderer plugin under code page " + cp + ": " + o);
            string page = System.IO.File.ReadAllText(cfg.Sub("clone", "public", "m-customer.outline.html"), System.Text.Encoding.UTF8);
            T.Contains(page, "Prüfen €", "text outside ASCII arrives intact under code page " + cp);

            // the source plugin through the command line: start, events appended, stop
            string feed = System.IO.Path.Combine(cfg.Dir, "feed.jsonl"); System.IO.File.WriteAllText(feed, "");
            var raw = Json.ReadFile(System.IO.Path.Combine(cfg.Dir, Config.FileName)); raw["source"] = "jsonl-file"; raw["jsonl-file"] = new JO("file", feed);
            Json.WriteFile(System.IO.Path.Combine(cfg.Dir, Config.FileName), raw);
            T.Eq(0, UnderCodePage(cp, "record --start --no-video --name cp --project \"" + cfg.Dir + "\"", cfg.Dir, out o), "start with a source plugin under code page " + cp + ": " + o);
            System.IO.File.AppendAllText(feed, Json.Write(new DbEvent { Ts = Clock.NowUs(), Kind = "statement", Session = "s", Sql = "SELECT 'grün'" }.ToJson()) + "\n", new System.Text.UTF8Encoding(false));
            T.Eq(0, UnderCodePage(cp, "record --stop --project \"" + cfg.Dir + "\"", cfg.Dir, out o), "stop under code page " + cp + ": " + o);
            var ev = DbEvent.ReadFile(System.IO.Path.Combine(cfg.RecordingsDir, "cp", "events.jsonl"));
            T.True(ev.Count == 1 && ev[0].Sql == "SELECT 'grün'", "event of the source plugin arrives intact under code page " + cp);
        }
    }
}
}

namespace EqlMaker.Tests {

public static class PluginStartTests {
    static string CopyOfSourcePlugin(string dir, string name, string insertAfterOptions) {
        string src = System.IO.Path.Combine(T.RepoRoot, "plugins", "examples", "source-jsonl-file"), dst = System.IO.Path.Combine(dir, name);
        System.IO.Directory.CreateDirectory(dst);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dst, "plugin.json"), System.IO.File.ReadAllText(System.IO.Path.Combine(src, "plugin.json")).Replace("\"jsonl-file\"", "\"" + name + "\"").Replace("\"command\"", "\"startTimeoutSeconds\": 4, \"command\""));
        string ps = System.IO.File.ReadAllText(System.IO.Path.Combine(src, "source.ps1"));
        string marker = "$file = $options.settings.file";
        if (ps.IndexOf(marker, StringComparison.Ordinal) < 0) throw new Exception("the example plugin changed: marker not found");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dst, "source.ps1"), ps.Replace(marker, insertAfterOptions + "\n" + marker));
        return dst;
    }

    /// <summary>A plugin that needs seconds to get going (slow machine, slow start of its run time): Start
    /// returns only when the plugin says it is capturing, so nothing written right after is lost.</summary>
    public static void StartWaitsUntilASlowPluginIsCapturing() {
        string d = T.TempDir("plugin-slow"), feed = System.IO.Path.Combine(d, "feed.jsonl");
        System.IO.File.WriteAllText(feed, "");
        CopyOfSourcePlugin(System.IO.Path.Combine(d, "plugins"), "slow", "Start-Sleep -Milliseconds 2500");
        Json.WriteFile(System.IO.Path.Combine(d, Config.FileName), new JO("source", "slow", "slow", new JO("file", feed)));
        var src = Config.Load(d).CreateSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        src.Start(Config.Load(d).Capture());
        T.True(sw.ElapsedMilliseconds >= 2400, "Start waited for the plugin: " + sw.ElapsedMilliseconds + " ms");
        System.IO.File.AppendAllText(feed, Json.Write(new DbEvent { Ts = 5, Kind = "statement", Sql = "SELECT 'right after the start'" }.ToJson()) + "\r\n");   // line ending of another tool
        var ev = src.Stop();
        T.Eq(1, ev.Count, "the event written right after the start is there"); T.Eq("SELECT 'right after the start'", ev[0].Sql, "its text");
    }

    public static void APluginThatNeverAnswersFailsTheStartWithAClearMessage() {
        string d = T.TempDir("plugin-mute"), feed = System.IO.Path.Combine(d, "feed.jsonl");
        System.IO.File.WriteAllText(feed, "");
        CopyOfSourcePlugin(System.IO.Path.Combine(d, "plugins"), "mute", "Start-Sleep -Seconds 30");
        Json.WriteFile(System.IO.Path.Combine(d, Config.FileName), new JO("source", "mute", "mute", new JO("file", feed)));
        var src = Config.Load(d).CreateSource();
        T.Throws(delegate { src.Start(Config.Load(d).Capture()); }, "did not answer with {\"info\": ...} within 4 seconds", "timeout");
        CopyOfSourcePlugin(System.IO.Path.Combine(d, "plugins"), "broken", "throw 'cannot reach the thing'");
        Json.WriteFile(System.IO.Path.Combine(d, Config.FileName), new JO("source", "broken", "broken", new JO("file", feed)));
        var src2 = Config.Load(d).CreateSource();
        T.Throws(delegate { src2.Start(Config.Load(d).Capture()); }, "cannot reach the thing", "a plugin that fails at once reports its own message");
    }
}
}
