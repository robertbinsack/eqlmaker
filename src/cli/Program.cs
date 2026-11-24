// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Command line of EQLMaker. Every pipeline step is also an MCP tool (eqlmaker mcp).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EqlMaker {

public static class Program {
    const string Usage = @"EQLMaker {V} - from a running legacy application to a specification, a web application and comparison tests

  eqlmaker init [folder]                      create a project folder with eqlmaker.json
  eqlmaker record --scenario <file.json>      run a scenario and record it
  eqlmaker record --start [--name n] [--no-video]   record while a person works; end with: eqlmaker record --stop
  eqlmaker reset                              undo what an aborted capture left on the database server
  eqlmaker correlate <recording>              time line: chapters -> actions -> statements -> tables
  eqlmaker spec <recording> [...]             specification of masks, queries and tables from time lines
  eqlmaker manual <file>                      take over a manual (PDF, HTML, DOCX, TXT, Markdown) as project context
  eqlmaker generate [--template name]         guided step: workspace and instructions for the new web application
  eqlmaker clone [--renderer name]            guided step: workspace and instructions for the read-only 1:1 clone
  eqlmaker video <recording A> <recording B>  two recordings side by side on one time axis, with player page
  eqlmaker verify screens <A> <B>             compare pictures (folders or files)
  eqlmaker verify db <recording A> <recording B>   compare the measured database changes of two recordings
  eqlmaker video-import <file.mp4>            read time stamps, transcript and key frames of a narrated video; findings
  eqlmaker enrich [--finish]                  guided step: take findings over into the web application, with report
  eqlmaker import-log <file>                  turn a server log written earlier into a recording with events only
  eqlmaker clock                              clock of the database server minus the clock of this machine
  eqlmaker web-shot <url> <file.png>          picture of a web page from a browser without a window
  eqlmaker sample [--flaw] [--no-video]       the whole pipeline with the sample application
  eqlmaker selftest                           check tools, database and the sample run
  eqlmaker mcp                                MCP server on standard input/output
  eqlmaker windows | controls <title>         list windows / the controls of a window
  eqlmaker background-run <desktop> <command...>   run a command on a separate desktop that is never shown
  eqlmaker version

Common options: --project <folder> (default: current folder), --json (machine-readable result), --lang en|de
Documentation: docs/ in the repository or the package.";

    public static int Main(string[] argv) {
        Console.OutputEncoding = new UTF8Encoding(false);
        var a = Args.Parse(argv, 1);
        try { Texts.Choose(a.Opt.Str("lang"), Config.Load(a.Opt.Str("project", "."))); } catch { }
        if (argv.Length == 0 || argv[0] == "help" || argv[0] == "--help" || argv[0] == "-h") { Console.WriteLine(Texts.T("usage", Usage).Replace("{V}", Config.Version)); return 0; }
        try {
            object r = Run(argv[0], a);
            if (r is int) return (int)r;
            if (r != null) Print(r, a.Opt.Bool("json", false));
            return 0;
        } catch (Exception e) {
            Console.Error.WriteLine("eqlmaker: " + e.Message);
            return 1;
        }
    }

    static void Print(object r, bool json) {
        var o = r as JO;
        if (o == null || json) { Console.WriteLine(r is string ? (string)r : Json.Pretty(r).TrimEnd()); return; }
        foreach (var kv in o) Console.WriteLine("{0,-14} {1}", kv.Key, kv.Value is string || kv.Value == null || kv.Value.GetType().IsPrimitive ? Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) : Json.Write(kv.Value));
    }

    static object Run(string cmd, Args a) {
        var cfg = Config.Load(a.Opt.Str("project", "."));
        switch (cmd) {
            case "version": return Config.Version;
            case "i18n-export": { string f = Path.GetFullPath(a.Need(0, "file")); Json.WriteFile(f, Texts.Export(Usage)); return f; }
            case "init": {
                string dir = Path.GetFullPath(a.P(0) ?? ".");
                Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, Config.FileName);
                if (File.Exists(f)) throw new Exception(f + " exists already");
                Json.WriteFile(f, Config.Example());
                return new JO("project", dir, "next", Texts.M("msg.project_created"));
            }
            case "windows": { var sb = new StringBuilder(); foreach (var w in Desktop.Windows()) sb.AppendLine(w.ToString()); return sb.ToString().TrimEnd(); }
            case "controls": {
                var w = Desktop.WaitWindow(a.Need(0, "window title pattern"), 5000);
                var l = new List<object>(); foreach (var ch in Desktop.Children(w)) l.Add(ch.ToJson(w));
                return Json.Pretty(new JO("window", w.Title, "controls", l)).TrimEnd();
            }
            case "background-run": {
                string desk = a.Need(0, "desktop name");
                var sb = new StringBuilder();
                for (int i = 1; i < a.Pos.Count; i++) { if (i > 1) sb.Append(' '); sb.Append(a.Pos[i].IndexOf(' ') >= 0 ? "\"" + a.Pos[i] + "\"" : a.Pos[i]); }
                foreach (var kv in a.Opt) if (kv.Key != "timeout") sb.Append(" --" + kv.Key + (Convert.ToString(kv.Value) == "true" ? "" : " \"" + kv.Value + "\""));
                return Desktop.RunOnBackgroundDesktop(desk, sb.ToString(), null, a.Opt.Int("timeout", 0) * 1000);
            }
            case "reset": {
                var src = cfg.CreateSource(); int n = 0;
                if (Directory.Exists(cfg.RecordingsDir))
                    foreach (var f in Directory.GetFiles(cfg.RecordingsDir, "capture-active.json", SearchOption.AllDirectories)) { var o = cfg.Capture(); o.StateFile = f; src.Reset(o); n++; }
                if (n == 0) src.Reset(cfg.Capture());
                return Texts.M("msg.capture_reset");
            }
            case "record": return Record(cfg, a);
            case "mcp": { var c = new Controller(cfg); var s = new McpServer(c); Pipeline.Register(s); Texts.Apply(s); s.Run(); return 0; }
            default: {
                var r = Pipeline.Run(cfg, cmd, a);
                if (r == null) throw new Exception(Texts.M("msg.unknown_command", cmd));
                return r;
            }
        }
    }

    // ---------------------------------------------------------------- record
    static string StateDir(Config cfg) { string d = cfg.Sub(".eqlmaker"); Directory.CreateDirectory(d); return d; }

    static object Record(Config cfg, Args a) {
        if (a.Opt.Has("capture-database")) {   // capture exactly this database, whatever the configuration names
            var cap = cfg.Raw.Obj("capture") ?? new JO(); cfg.Raw["capture"] = cap;
            cap["roles"] = new List<object>(); cap["databases"] = new List<object> { a.Opt.Str("capture-database") };
        }
        var c = new Controller(cfg);
        if (a.Opt.Has("input")) { c.Input = a.Opt.Str("input"); c.InputForced = true; }
        c.NoVideo = a.Opt.Has("no-video");
        string sd = StateDir(cfg), state = Path.Combine(sd, "session.json"), stopFile = Path.Combine(sd, "stop"), done = Path.Combine(sd, "done.json"), started = Path.Combine(sd, "started");
        if (a.Opt.Has("scenario")) {
            if (!a.Opt.Has("quiet")) c.LogLine = delegate (string s) { Console.Error.WriteLine(s); };
            var res = Scenario.Run(c, Path.GetFullPath(a.Opt.Str("scenario")), a.Opt.Str("name"));
            return a.Opt.Has("quiet") ? (object)0 : res;
        }
        if (a.Opt.Has("start")) {
            if (File.Exists(state)) {
                var s = Json.ReadFile(state);
                try { Process.GetProcessById(s.Int("pid", 0)); throw new Exception("a recording is already running (" + s.Str("name") + "); end it with: eqlmaker record --stop"); } catch (ArgumentException) { File.Delete(state); }
            }
            string name = a.Opt.Str("name") ?? "recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (var f in new[] { stopFile, done, started }) if (File.Exists(f)) File.Delete(f);
            var psi = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
                "record --serve --name \"" + name + "\" --project \"" + cfg.Dir + "\" --region \"" + a.Opt.Str("region", "workarea") + "\" --fps " + a.Opt.Int("fps", 15) + (a.Opt.Has("no-video") ? " --no-video" : "") + (a.Opt.Has("no-capture") ? " --no-capture" : "") + " --input " + c.Input);
            // started through the shell: the recorder must not inherit the output pipes of this process,
            // or a script that reads the output of "record --start" would wait until the recording ends
            psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
            int pid; using (var p = Process.Start(psi)) pid = p.Id;
            Json.WriteFile(state, new JO("pid", pid, "name", name));
            var sw = Stopwatch.StartNew();
            while (!File.Exists(started) && !File.Exists(done)) {
                bool alive = true; try { Process.GetProcessById(pid); } catch (ArgumentException) { alive = false; }
                if ((!alive && !File.Exists(done) && !File.Exists(started)) || sw.ElapsedMilliseconds > 60000) { File.Delete(state); throw new Exception("the recording did not start"); }
                Thread.Sleep(150);
            }
            if (File.Exists(done)) { var e = Json.ReadFile(done); File.Delete(state); throw new Exception("recording failed: " + e.Str("error")); }
            return Texts.M("msg.recording_started", name);
        }
        if (a.Opt.Has("stop")) {
            if (!File.Exists(state)) throw new Exception(Texts.M("msg.no_recording"));
            var s = Json.ReadFile(state);
            File.WriteAllText(stopFile, "");
            var sw = Stopwatch.StartNew();
            while (!File.Exists(done)) {
                bool alive = true; try { Process.GetProcessById(s.Int("pid", 0)); } catch (ArgumentException) { alive = false; }
                if (!alive) { Thread.Sleep(300); if (!File.Exists(done)) { File.Delete(state); throw new Exception("the recording process ended without result"); } }
                if (sw.ElapsedMilliseconds > 180000) throw new Exception("the recording did not finish within 3 minutes");
                Thread.Sleep(150);
            }
            Thread.Sleep(100);
            var r = Json.ReadFile(done);
            File.Delete(state); File.Delete(done);
            if (r.Has("error")) throw new Exception("recording failed: " + r.Str("error"));
            return r;
        }
        if (a.Opt.Has("serve")) {
            // background process of --start: records until the stop file appears
            try {
                c.StartRecording(a.Opt.Str("name"), a.Opt.Str("region"), a.Opt.Int("fps", 15), !a.Opt.Has("no-video"), !a.Opt.Has("no-capture"));
                File.WriteAllText(started, "");
                while (!File.Exists(stopFile)) Thread.Sleep(150);
                Json.WriteFile(done, c.StopRecording());
            } catch (Exception e) { Json.WriteFile(done, new JO("error", e.Message)); }
            finally { foreach (var f in new[] { started, stopFile }) { try { File.Delete(f); } catch { } } }
            return 0;
        }
        throw new Exception("record needs --scenario <file>, --start or --stop");
    }
}
}
