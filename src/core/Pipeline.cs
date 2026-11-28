// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The pipeline steps as commands and as MCP tools. Each command line step has an MCP tool that
// does the same and returns the same result object.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace EqlMaker {

public static class Pipeline {
    static List<string> Csv(string s) { var l = new List<string>(); if (!string.IsNullOrEmpty(s)) foreach (var p in s.Split(',')) if (p.Trim() != "") l.Add(p.Trim()); return l; }
    static JO S(string d) { return McpServer.Prop("string", d); }
    static JO B(string d) { return McpServer.Prop("boolean", d); }
    static JO N(string d) { return McpServer.Prop("number", d); }

    // ------------------------------------------------------------ steps
    public static JO Correlate(Config cfg, string recording) { return Timeline.Run(cfg.RecordingDir(recording)); }

    public static JO WebShot(Config cfg, string url, string png, int width, int height, int waitMs) {
        var b = new Browser { Port = 9334, Headless = true, ProfileRoot = cfg.Sub(".eqlmaker"), TemporaryProfile = true }; string exe = cfg.ToolOrNull("browser"); if (exe != null) b.Executable = exe;
        try {
            b.Open(url, new Rectangle(0, 0, width, height));
            b.WaitLoaded(15000); b.SetViewport(width, height);
            Thread.Sleep(Math.Max(0, waitMs));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png)));
            File.WriteAllBytes(png, b.Screenshot());
        } finally { b.Quit(); }
        return new JO("picture", Path.GetFullPath(png), "width", width, "height", height);
    }

    /// <summary>Clock of the database server minus clock of this machine, measured now.</summary>
    public static JO ClockCheck(Config cfg) {
        using (var c = new PgClient(PgConnInfo.Parse(cfg.Connection).WithApplication(PostgresSource.AppName))) {
            long best = long.MaxValue, offset = 0;
            for (int i = 0; i < 7; i++) {
                long t1 = Clock.NowUs(); long server = long.Parse(c.Scalar("SELECT (extract(epoch from clock_timestamp()) * 1000000)::bigint"), CultureInfo.InvariantCulture); long t2 = Clock.NowUs();
                if (t2 - t1 < best) { best = t2 - t1; offset = server - (t1 + t2) / 2; }
            }
            return new JO("server_minus_local_ms", Math.Round(offset / 1000.0, 3), "round_trip_ms", Math.Round(best / 1000.0, 3), "measured_at", Clock.Iso(Clock.NowUs()),
                "use", "video-import --clock-offset-ms " + Math.Round(offset / 1000.0, 1).ToString(CultureInfo.InvariantCulture) + " (when the video was recorded on this machine)");
        }
    }

    /// <summary>Turns a server log that was written earlier into a recording with events only, so that
    /// correlate, spec and video-import can use it after the fact.</summary>
    public static JO ImportLog(Config cfg, string file, string name, string prefix, int utcOffsetMinutes, string database, string application) {
        if (!File.Exists(file)) throw new Exception("log file not found: " + file);
        string problem = PgLog.PrefixProblem(prefix); if (problem != null) throw new Exception(problem);
        string text; using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
        var entries = PgLog.Parse(text, PgLog.LineRegex(prefix), utcOffsetMinutes * 60);
        var events = PgLog.ToEvents(entries, "postgres", delegate (LogEntry e, out string u, out string d, out string a) {
            u = e.User; d = e.Database; a = e.Application;
            if (a == PostgresSource.AppName) return false;
            if (!string.IsNullOrEmpty(database) && d != null && d != database) return false;
            if (!string.IsNullOrEmpty(application) && a != null && a != application) return false;
            return true;
        });
        if (events.Count == 0) throw new Exception("no statements found in " + file + " with log_line_prefix '" + prefix + "' (statements are in the log only while log_statement was on)");
        DbEvent.SortByTime(events);
        if (string.IsNullOrEmpty(name)) name = "log-" + Spec.Slug(Path.GetFileNameWithoutExtension(file));
        string dir = Path.Combine(cfg.RecordingsDir, name); Directory.CreateDirectory(dir);
        long t0 = events[0].Ts; foreach (var e in events) e.Offset = (e.Ts - t0) / 1e6;
        DbEvent.WriteFile(Path.Combine(dir, "events.jsonl"), events);
        File.WriteAllText(Path.Combine(dir, "actions.jsonl"), "");
        Json.WriteFile(Path.Combine(dir, "recording.json"), new JO("format", "eqlmaker-recording", "version", DbEvent.FormatVersion, "name", name, "t0_us", t0, "t0", Clock.Iso(t0),
            "duration", Math.Round((events[events.Count - 1].Ts - t0) / 1e6, 3), "input", null, "video", null, "source", "postgres",
            "capture", new JO("from_log_file", Path.GetFileName(file), "log_line_prefix", prefix, "utc_offset_minutes", utcOffsetMinutes), "chapters", new List<object>(),
            "counts", new JO("actions", 0, "statements", events.Count), "warnings", new List<object> { "imported from a log file: no actions, no changed rows; session details only as far as the log line prefix has them" }));
        return new JO("recording", dir, "events", events.Count, "from", Clock.Iso(t0), "to", Clock.Iso(events[events.Count - 1].Ts));
    }

    // ------------------------------------------------------------ command line
    /// <summary>Runs a pipeline command; null when the command is not one.</summary>
    public static object Run(Config cfg, string cmd, Args a) {
        switch (cmd) {
            case "correlate": return Correlate(cfg, a.Need(0, "recording"));
            case "spec": return Spec.Run(cfg, a.Pos);
            case "read": return Readers.Run(cfg, a.Opt.Str("reader"), a.Pos, a.Opt.Str("out"), Csv(a.Opt.Str("bundle")), a.Opt.Int("max-depth", 3), a.Opt.Str("exclude"), a.Opt.Bool("no-raw", false));
            case "export-sql": return SqlExport.Run(cfg, a.Need(0, "recording"), a.Opt.Str("out"));
            case "convert-scenario": return SqlExport.ConvertScenario(a.Need(0, "scenario file"), a.Opt.Str("out"));
            case "manual": return ManualImport.Run(cfg, a.Need(0, "file"), a.Opt.Str("name"));
            case "generate": return Guided.Generate(cfg, a.Opt.Str("template"), a.Opt.Bool("force", false));
            case "clone": return Guided.Clone(cfg, a.Opt.Str("renderer"), a.Opt.Bool("force", false));
            case "claude-app": return Guided.ClaudeApp(cfg, a.Opt.Str("name"), a.Opt.Str("title"), a.Opt.Str("schema"), a.Opt.Str("entity"), a.Opt.Bool("force", false));
            case "video": return VideoPair.Run(cfg, a.Need(0, "recording A"), a.Need(1, "recording B"), a.Opt.Str("layout"), a.Opt.Str("name"), a.Opt.Bool("no-video-file", false));
            case "verify": {
                string what = a.Need(0, "screens or db"); JO r;
                if (what == "screens") r = VerifyScreens.Run(cfg, a.Need(1, "A"), a.Need(2, "B"), a.Opt.Int("tolerance", 16), a.Opt.Num("max-percent", 0.5), a.Opt.Str("ignore"), a.Opt.Str("name"));
                else if (what == "db") r = VerifyDb.Run(cfg, a.Need(1, "recording A"), a.Need(2, "recording B"), Csv(a.Opt.Str("ignore")), Csv(a.Opt.Str("keys")), Csv(a.Opt.Str("references")), a.Opt.Str("name"));
                else throw new Exception("verify needs 'screens' or 'db'");
                Print(r, a);
                if (what == "db" && !a.Opt.Bool("json", false)) Console.WriteLine("\n" + File.ReadAllText(r.Str("readable")));
                return r.Bool(what == "db" ? "equal" : "passed", false) ? 0 : 2;
            }
            case "video-import": {
                var o = new VideoImportOptions { File = a.Need(0, "video file"), Name = a.Opt.Str("name"), TranscriptFile = a.Opt.Str("transcript"), Language = a.Opt.Str("language"), SampleFps = a.Opt.Num("sample-fps", 2),
                    MaxKeyframes = a.Opt.Int("max-keyframes", 120), Recordings = Csv(a.Opt.Str("recordings")), EstimateOffset = a.Opt.Bool("estimate-offset", false), NoTranscript = a.Opt.Bool("no-transcript", false),
                    MaxShiftSeconds = a.Opt.Num("max-shift", 900), AttachSeconds = a.Opt.Num("attach-seconds", 8) };
                if (a.Opt.Has("clock-offset-ms")) o.ClockOffsetMs = a.Opt.Num("clock-offset-ms", 0);
                return VideoImport.Run(cfg, o);
            }
            case "enrich":
                if (a.Opt.Has("decide")) return Guided.EnrichDecide(cfg, a.Opt.Str("findings"), a.Opt.Str("decide"), a.Opt.Str("status"), a.Opt.Str("note"), Csv(a.Opt.Str("files")));
                if (a.Opt.Has("finish")) return Guided.EnrichFinish(cfg, a.Opt.Str("findings"), a.P(0), a.P(1));
                return Guided.EnrichPrepare(cfg, a.Opt.Str("findings"), a.P(0), a.P(1));
            case "web-shot": { var size = (a.Opt.Str("size", "1280x800")).Split('x'); return WebShot(cfg, a.Need(0, "url"), a.Need(1, "picture file"), int.Parse(size[0]), int.Parse(size[1]), a.Opt.Int("wait", 300)); }
            case "narrate-draft": return Narration.DraftFile(cfg, a.Need(0, "caption timeline"), a.Opt.Str("video"), a.Opt.Str("lang"), a.Opt.Num("chars-per-second", 0),
                a.Opt.Num("max-segment", 14), a.Opt.Num("min-segment", 2.5), a.Opt.Str("out"), a.Opt.Bool("force", false));
            case "narrate": return Narration.RunWithConfig(cfg, NarrateOptionsFrom(a));
            case "clock": return ClockCheck(cfg);
            case "import-log": return ImportLog(cfg, a.Need(0, "log file"), a.Opt.Str("name"), a.Opt.Str("prefix", "%m [%p] "), a.Opt.Int("utc-offset-minutes", (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes), a.Opt.Str("database"), a.Opt.Str("application"));
            case "prompt": return Guided.Prompt(cfg, a.Need(0, "generate, clone or enrich"), a.Opt.Str("findings"));
            case "sample": return SampleRun.Run(cfg, a.Opt.Bool("flaw", false), a.Opt.Bool("no-video", false), a.Opt.Bool("visible", false), delegate (string s) { Console.WriteLine(s); }) ? 0 : 2;
            case "selftest": return SampleRun.SelfTest(cfg, a.Opt.Str("audio"), a.Opt.Str("expect"), a.Opt.Bool("quick", false)) ? 0 : 2;
        }
        return null;
    }
    static NarrateOptions NarrateOptionsFrom(Args a) {
        return new NarrateOptions { Video = a.Opt.Str("video"), Script = a.Opt.Str("script"), Out = a.Opt.Str("out"), Voice = a.Opt.Str("voice"), Language = a.Opt.Str("lang"),
            Music = a.Opt.Str("music"), MusicVolume = a.Opt.Num("music-volume", 30), TtsUrl = a.Opt.Str("tts-url"), MaxTempo = a.Opt.Num("max-tempo", 1.1),
            CharsPerSecond = a.Opt.Num("chars-per-second", 0), NoDuck = a.Opt.Bool("no-duck", false), Force = a.Opt.Bool("force", false) };
    }
    static void Print(JO r, Args a) {
        if (a.Opt.Bool("json", false)) { Console.WriteLine(Json.Pretty(r).TrimEnd()); return; }
        foreach (var kv in r) Console.WriteLine("{0,-14} {1}", kv.Key, kv.Value is string || kv.Value == null || kv.Value.GetType().IsPrimitive ? Convert.ToString(kv.Value, CultureInfo.InvariantCulture) : Json.Write(kv.Value));
    }

    // ------------------------------------------------------------ MCP
    public static void Register(McpServer s) {
        var cfg = s.C.Cfg;
        s.Add("correlate", "Pipeline step 2. Builds the time line of a recording by fixed rules: chapters -> actions -> statements -> transactions -> tables. Writes timeline.json into the recording folder.",
            new JO("recording", S("name of the recording")), new[] { "recording" }, delegate (JO a) { return Correlate(cfg, a.Str("recording")); });
        s.Add("spec", "Pipeline step 3. Derives the specification (steps, masks, statement shapes, tables read and written, links to the manual) from the time lines by fixed rules. Writes spec/spec.json and spec/spec.md.",
            new JO("recordings", S("comma-separated recordings (default: all of the project)")), null, delegate (JO a) { return Spec.Run(cfg, Csv(a.Str("recordings"))); });
        s.Add("read_application", "Reads the files of the original application (built in: PowerBuilder libraries .pbd/.pbl; others through reader plugins) and writes menus, windows, labels, SQL statements and the tables they use to static/<reader>/reader.json; 'spec' takes the result over. The files are only read.",
            new JO("paths", S("comma-separated files or folders of the application, in the order of its library list"), "reader", S("reader name (default: by file extension)"), "bundle", S("comma-separated start objects for bundles (what an object uses)"), "out", S("output folder (default: static/<reader>)")), new[] { "paths" },
            delegate (JO a) { return Readers.Run(cfg, a.Str("reader"), Csv(a.Str("paths")), a.Str("out"), Csv(a.Str("bundle")), a.Int("max_depth", 3), a.Str("exclude"), a.Bool("no_raw", false)); });
        s.Add("export_sql", "Writes the statements of a recording as a tab-separated table (sql.tsv): offset, time, session, user, database, application, kind, statement. Kinds tsql-batch and tsql-rpc are statements that came through the statement trace of the server.",
            new JO("recording", S("name of the recording"), "out", S("file to write (default: sql.tsv in the recording folder)")), new[] { "recording" }, delegate (JO a) { return SqlExport.Run(cfg, a.Str("recording"), a.Str("out")); });
        s.Add("manual_import", "Takes the manual of the original program over as project context (PDF, HTML, DOCX, TXT, Markdown): text and chapters; 'spec' links chapters to masks and steps.",
            new JO("file", S("path of the manual"), "name", S("name in the project (default: file name)")), new[] { "file" }, delegate (JO a) { return ManualImport.Run(cfg, a.Str("file"), a.Str("name")); });
        s.Add("generate", "Pipeline step 4, a guided step: prepares the folder 'web' from a web template, puts the specification next to it and returns the instructions. You build the application; verify_db checks it. Not an automatic generator.",
            new JO("template", S("web template (default: 'default'; others are plugins)"), "force", B("copy the template over an existing folder")), null, delegate (JO a) { var r = Guided.Generate(cfg, a.Str("template"), a.Bool("force", false)); r["instructions_text"] = File.ReadAllText(r.Str("instructions")); return r; });
        s.Add("clone", "Pipeline step 5, a guided step: prepares the folder 'clone' for the read-only 1:1 archive: reference picture and scaffold page per mask, read-only server, instructions. You fill the pages; verify_screens checks them. Not an automatic generator.",
            new JO("renderer", S("clone renderer (default: 'backdrop'; others are plugins)"), "force", B("prepare again over an existing folder")), null, delegate (JO a) { var r = Guided.Clone(cfg, a.Str("renderer"), a.Bool("force", false)); r["instructions_text"] = File.ReadAllText(r.Str("instructions")); return r; });
        s.Add("claude_app", "Guided step: prepares the folder 'claude-app' for a Claude app that runs inside Claude Desktop: a local MCP server of its own (eqlmaker-<app>, one database), the application description from the specification, a page with lists, record forms and a preview before every write, German and English texts, the Claude Desktop entry and the instructions. You complete the app; verify_db checks it. Not an automatic generator.",
            new JO("name", S("name of the app (default: project folder); the server is eqlmaker-<name>"), "title", S("title shown in the app"), "schema", S("database schema of the app (default: public)"), "entity", S("entities with create/update/delete as table:key, comma separated"), "force", B("copy the template over an existing folder")), null, delegate (JO a) { var r = Guided.ClaudeApp(cfg, a.Str("name"), a.Str("title"), a.Str("schema"), a.Str("entity"), a.Bool("force", false)); r["instructions_text"] = File.ReadAllText(r.Str("instructions")); return r; });
        s.Add("video", "Pipeline step 6. Puts two recordings of the same steps (same chapter captions) on one time axis: player page and, with FFmpeg, one combined video.",
            new JO("a", S("recording of the original"), "b", S("recording of the rebuilt application"), "layout", S("side-by-side (default) or a video-layout plugin"), "no_video_file", B("only the player page")), new[] { "a", "b" },
            delegate (JO a) { return VideoPair.Run(cfg, a.Str("a"), a.Str("b"), a.Str("layout"), a.Str("name"), a.Bool("no_video_file", false)); });
        s.Add("verify_db", "Pipeline step 7. Compares the measured database changes of two recordings of the same steps (original and rebuilt application), step by step. 'equal' only when every changed row agrees.",
            new JO("a", S("recording of the original"), "b", S("recording of the rebuilt application"), "ignore", S("comma-separated columns to leave out, e.g. invoice.updated_at or *.changed"), "keys", S("comma-separated key columns whose new values are compared by order of creation (default *.id)")), new[] { "a", "b" },
            delegate (JO a) { var r = VerifyDb.Run(cfg, a.Str("a"), a.Str("b"), Csv(a.Str("ignore")), Csv(a.Str("keys")), Csv(a.Str("references")), a.Str("name")); r["report_text"] = File.ReadAllText(r.Str("readable")); return r; });
        s.Add("verify_screens", "Pipeline step 7. Compares two pictures or two folders of pictures (paired by file name) pixel by pixel; writes a picture of the differences.",
            new JO("a", S("picture or folder (reference)"), "b", S("picture or folder (to check)"), "tolerance", N("difference per colour channel that still counts as equal (default 16)"), "max_percent", N("share of differing pixels that still passes (default 0.5)"), "ignore", S("regions not compared: x,y,w,h;x,y,w,h")), new[] { "a", "b" },
            delegate (JO a) { return VerifyScreens.Run(cfg, a.Str("a"), a.Str("b"), a.Int("tolerance", 16), a.Num("max_percent", 0.5), a.Str("ignore"), a.Str("name")); });
        s.Add("web_shot", "Takes a picture of a web page at a given size with a browser that runs without a window (for verify_screens).",
            new JO("url", S("URL"), "file", S("PNG file to write"), "width", N("width (default 1280)"), "height", N("height (default 800)")), new[] { "url", "file" }, delegate (JO a) { return WebShot(cfg, a.Str("url"), a.Str("file"), a.Int("width", 1280), a.Int("height", 800), a.Int("wait_ms", 300)); });
        s.Add("video_import", "Imports a narrated screen recording (MP4) that shows the time stamp strip: reads the time of the frames, maps video time to wall clock (drift, cuts), transcribes the sound, finds key frames, matches everything to the database events and writes findings per operation.",
            new JO("file", S("video file"), "recordings", S("comma-separated recordings whose events belong to the video (default: all)"), "transcript", S("transcript file (VTT, SRT, JSON) instead of speech recognition"), "language", S("language of the speech: auto (default), en, de, ..."),
                "clock_offset_ms", N("clock of the database server minus clock of the recording machine, when known"), "estimate_offset", B("estimate that offset from the data")), new[] { "file" },
            delegate (JO a) {
                var o = new VideoImportOptions { File = a.Str("file"), Name = a.Str("name"), TranscriptFile = a.Str("transcript"), Language = a.Str("language"), Recordings = Csv(a.Str("recordings")), EstimateOffset = a.Bool("estimate_offset", false), NoTranscript = a.Bool("no_transcript", false) };
                if (a["clock_offset_ms"] != null) o.ClockOffsetMs = a.Num("clock_offset_ms", 0);
                return VideoImport.Run(cfg, o);
            });
        s.Add("enrich", "A guided step: starts taking the findings of video_import over into the web application. Remembers the state before (files, and the database comparison when two recordings are named) and returns the instructions with every finding. You decide and change; enrich_decide records, enrich_finish reports.",
            new JO("findings", S("name of the findings (default: the only one)"), "a", S("recording of the original, for the comparison before"), "b", S("recording of the web application, for the comparison before")), null,
            delegate (JO a) { var r = Guided.EnrichPrepare(cfg, a.Str("findings"), a.Str("a"), a.Str("b")); r["instructions_text"] = File.ReadAllText(r.Str("instructions")); return r; });
        s.Add("enrich_decide", "Records the decision for one finding: adopted (the web application was changed), rejected (not relevant) or open (relevant, cannot be done yet), each with a reason.",
            new JO("finding", S("id of the finding, e.g. f3"), "status", S("adopted, rejected or open"), "note", S("what was changed and where, or why not"), "files", S("comma-separated files of the web application that were changed"), "findings", S("name of the findings")), new[] { "finding", "status", "note" },
            delegate (JO a) { return Guided.EnrichDecide(cfg, a.Str("findings"), a.Str("finding"), a.Str("status"), a.Str("note"), Csv(a.Str("files"))); });
        s.Add("enrich_finish", "Ends the enrich step: lists the files of the web application that changed, runs the database comparison after (name the original recording and the new recording of the changed application) and writes the report: every finding adopted, rejected or open with its reason.",
            new JO("a", S("recording of the original"), "b", S("new recording of the changed web application"), "findings", S("name of the findings")), null,
            delegate (JO a) { var r = Guided.EnrichFinish(cfg, a.Str("findings"), a.Str("a"), a.Str("b")); r["report_text"] = File.ReadAllText(r.Str("readable")); return r; });
        s.Add("narrate_draft", "Prepares a narration script for a finished video from its caption timeline (SRT, WebVTT, whisper JSON or JSON segments): timed segments with a hint and a character budget each. Write the spoken text of every segment into 'text' (at most budget_chars), then call narrate.",
            new JO("captions", S("caption timeline file"), "video", S("the video (for its length)"), "language", S("language of the narration: de, en, ..."), "chars_per_second", N("speaking rate of the voice in characters per second (default 14)"),
                "max_segment", N("longest segment in seconds (default 14)"), "out", S("script file to write (default: next to the captions)"), "force", B("replace an existing script")), new[] { "captions" },
            delegate (JO a) { return Narration.DraftFile(cfg, a.Str("captions"), a.Str("video"), a.Str("language"), a.Num("chars_per_second", 0), a.Num("max_segment", 14), 2.5, a.Str("out"), a.Bool("force", false)); });
        s.Add("narrate", "Lays a spoken narration over a video: speaks every segment of the narration script with a local Fish Speech server and a registered voice reference, fits the clips into their segments (speed-up up to max_tempo), mixes optional background music (ducked under the speech) and writes a new video; the picture is copied unchanged. Fails with the segments to shorten when the speech does not fit.",
            new JO("video", S("video file"), "script", S("narration script (narrate_draft)"), "out", S("video file to write"), "voice", S("voice reference id registered on the Fish Speech server"), "language", S("language of the narration (picks narration.voices of eqlmaker.json)"),
                "music", S("background music (mp3 or wav), looped to the length of the video"), "music_volume", N("music volume in percent (default 30)"), "tts_url", S("address of the Fish Speech server (default http://127.0.0.1:8080)"),
                "max_tempo", N("largest speed-up of a clip (default 1.1)"), "force", B("replace an existing output file")), new[] { "video", "script", "out" },
            delegate (JO a) {
                return Narration.RunWithConfig(cfg, new NarrateOptions { Video = a.Str("video"), Script = a.Str("script"), Out = a.Str("out"), Voice = a.Str("voice"), Language = a.Str("language"), Music = a.Str("music"),
                    MusicVolume = a.Num("music_volume", 30), TtsUrl = a.Str("tts_url"), MaxTempo = a.Num("max_tempo", 1.1), Force = a.Bool("force", false) });
            });
        s.Add("clock_check","Measures the clock of the database server against the clock of this machine (for video_import).", null, null, delegate (JO a) { return ClockCheck(cfg); });
        s.Add("import_log", "Turns a PostgreSQL server log written earlier into a recording with events only (for correlate, spec and video_import after the fact).",
            new JO("file", S("log file"), "name", S("name of the recording"), "prefix", S("log_line_prefix of the server (default '%m [%p] ')"), "utc_offset_minutes", N("UTC offset of the log times in minutes (default: this machine)")), new[] { "file" },
            delegate (JO a) { return ImportLog(cfg, a.Str("file"), a.Str("name"), a.Str("prefix", "%m [%p] "), a.Int("utc_offset_minutes", (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes), a.Str("database"), a.Str("application")); });

        foreach (var name in new[] { "generate", "clone", "enrich" }) {
            string n = name;
            s.Prompts.Add(new McpPrompt { Name = n, Description = n == "generate" ? "Instructions for building the web application from the specification (guided step)." : n == "clone" ? "Instructions for building the read-only 1:1 archive (guided step)." : "Instructions for taking the findings of a narrated video over into the web application (guided step).",
                Arguments = n == "enrich" ? new[] { new JO("name", "findings", "description", "name of the findings", "required", false) } : new JO[0],
                Text = delegate (JO a) { return Guided.Prompt(cfg, n, a.Str("findings")); } });
        }
    }
}
}
