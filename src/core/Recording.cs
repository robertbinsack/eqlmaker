// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// A recording: actions (what was operated), database events (what the application did in the
// database), optional video and reference pictures, all on one time line.
// Result: a folder with recording.json, actions.jsonl, events.jsonl, video.mp4, sql.vtt,
// viewer.html and shots/.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EqlMaker {

public class Chapter { public double Start; public string Caption; }

public class Recording {
    public string Dir, Name; public Config Cfg;
    public Rectangle Region; public int Fps = 15; public bool Video = true, Capture = true;
    public FramePainter Painter;   // background mode: frames painted by the window or the browser
    public IEventSource Source; public ScreenRecorder Rec; public long T0Us; public bool Running;
    /// <summary>Roles and databases to capture instead of those of the configuration (scenario key "capture" as object).</summary>
    public JO CaptureOverride;
    public List<Chapter> Chapters = new List<Chapter>(); public List<JO> Actions = new List<JO>();
    public string Input = "real";
    int shots; readonly object gate = new object();
    string StateFile { get { return Path.Combine(Dir, "capture-active.json"); } }

    public double Now() { return (Clock.NowUs() - T0Us) / 1e6; }

    public void Start() {
        Directory.CreateDirectory(Dir);
        foreach (var f in new[] { "events.jsonl", "actions.jsonl", "recording.json", "video.mp4", "sql.vtt", "sql.tsv", "viewer.html" }) { string p = Path.Combine(Dir, f); if (File.Exists(p)) File.Delete(p); }
        if (Directory.Exists(Path.Combine(Dir, "shots"))) Directory.Delete(Path.Combine(Dir, "shots"), true);
        if (Capture) {
            Source = Cfg.CreateSource();
            var o = Cfg.Capture(CaptureOverride); o.StateFile = StateFile;
            Source.Start(o);
        }
        if (Video) {
            try {
                if (Painter == null && !Desktop.Visible()) throw new Exception(Desktop.NotVisible);
                Rec = new ScreenRecorder { File = Path.Combine(Dir, "video.mp4"), Region = Region, Fps = Fps, Painter = Painter };
                Rec.Start();
            } catch { if (Source != null) { try { Source.Stop(); } catch { } } throw; }
            T0Us = Rec.StartedAtUs;
        } else T0Us = Clock.NowUs();
        Running = true;
    }

    public void Mark(string caption) {
        lock (gate) Chapters.Add(new Chapter { Start = Math.Max(0, Now()), Caption = caption });
        Log(new JO("kind", "chapter", "caption", caption));
    }

    /// <summary>Adds an action to the time line; "ts" and "offset" are filled here unless given.</summary>
    public JO Log(JO a) {
        lock (gate) {
            var o = new JO(); long ts = a.Has("ts") ? a.Long("ts", 0) : Clock.NowUs();
            o["v"] = DbEvent.FormatVersion; o["seq"] = Actions.Count + 1; o["ts"] = ts; o["time"] = Clock.Iso(ts); o["offset"] = Math.Round((ts - T0Us) / 1e6, 6);
            foreach (var kv in a) if (kv.Key != "ts") o[kv.Key] = kv.Value;
            Actions.Add(o);
            return o;
        }
    }

    /// <summary>Saves a reference picture and returns its path relative to the recording folder.</summary>
    public string SaveShot(byte[] png) {
        int n; lock (gate) n = ++shots;
        string rel = "shots/" + n.ToString("0000") + ".png";
        Directory.CreateDirectory(Path.Combine(Dir, "shots"));
        File.WriteAllBytes(Path.Combine(Dir, rel.Replace('/', Path.DirectorySeparatorChar)), png);
        return rel;
    }

    public JO Stop() {
        Running = false;
        double duration = Now(); string err = null;
        if (Rec != null) { try { Rec.Stop(); duration = Rec.Duration; } catch (Exception e) { err = e.Message; } }
        var events = new List<DbEvent>(); JO info = new JO();
        if (Source != null) { events = Source.Stop(); info = Source.Info ?? new JO(); }
        long serverMinusLocal = info.Long("server_minus_local_us", 0);
        foreach (var e in events) e.Offset = (e.Ts - serverMinusLocal - T0Us) / 1e6;
        var res = Write(duration, events, info);
        if (err != null) throw new Exception(err);
        return res;
    }

    static string Vtt(double t) { if (t < 0) t = 0; var ts = TimeSpan.FromSeconds(t); return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds); }

    JO Write(double duration, List<DbEvent> events, JO info) {
        DbEvent.WriteFile(Path.Combine(Dir, "events.jsonl"), events);
        Json.WriteLines(Path.Combine(Dir, "actions.jsonl"), Actions);

        var stm = events.FindAll(delegate (DbEvent e) { return e.Kind != "transaction"; });
        var v = new StringBuilder("WEBVTT\n\n");
        var shown = stm.FindAll(delegate (DbEvent e) { return e.Offset >= 0 && e.Offset <= duration; });
        for (int i = 0; i < shown.Count; i++) {
            double s = shown[i].Offset.Value, end = Math.Min(s + 3, i + 1 < shown.Count ? Math.Max(s + 0.4, shown[i + 1].Offset.Value) : Math.Max(duration, s + 0.4));
            string txt = shown[i].Kind == "error" ? "ERROR: " + shown[i].Error : shown[i].Sql ?? "";
            if (txt.Length > 300) txt = txt.Substring(0, 300) + " ...";
            v.Append((i + 1) + "\n" + Vtt(s) + " --> " + Vtt(end) + "\n" + Regex.Replace(txt.Replace("-->", "->"), "\n\\s*\n", "\n") + "\n\n");   // a blank line would end the cue
        }
        File.WriteAllText(Path.Combine(Dir, "sql.vtt"), v.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Dir, "sql.tsv"), SqlExport.Tsv(events), new UTF8Encoding(false));

        var ch = new List<object>();
        for (int i = 0; i < Chapters.Count; i++) ch.Add(new JO("start", Math.Round(Chapters[i].Start, 3), "end", Math.Round(i + 1 < Chapters.Count ? Chapters[i + 1].Start : duration, 3), "caption", Chapters[i].Caption));
        int tx = events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count;
        int errors = events.FindAll(delegate (DbEvent e) { return e.Kind == "error"; }).Count;
        var warnings = new List<object>();
        if (info.Arr("warnings") != null) warnings.AddRange(info.Arr("warnings"));
        else if (info["warnings"] is List<string>) foreach (var w in (List<string>)info["warnings"]) warnings.Add(w);
        if (Rec != null && Rec.BlackFrames > 0) warnings.Add(Rec.BlackFrames + " of " + Rec.Frames + " frames could not be captured (desktop not visible); the video holds the last picture there");

        var man = new JO();
        man["format"] = "eqlmaker-recording"; man["version"] = DbEvent.FormatVersion; man["name"] = Name;
        man["t0_us"] = T0Us; man["t0"] = Clock.Iso(T0Us); man["duration"] = Math.Round(duration, 3);
        man["input"] = Input;
        man["video"] = Rec != null ? "video.mp4" : null; man["fps"] = Rec != null ? (object)Fps : null; man["frames"] = Rec != null ? (object)Rec.Frames : null;
        man["region"] = Rec != null ? new List<object> { Region.X, Region.Y, Region.Width, Region.Height } : null;
        man["source"] = Source != null ? Source.Name : null;
        man["capture"] = Source != null ? new JO("roles", Cfg.Capture(CaptureOverride).Roles, "databases", Cfg.Capture(CaptureOverride).Databases, "changes", info.Str("changes"), "fields", info["fields"],
            "server_minus_local_us", info.Long("server_minus_local_us", 0), "server_version", info.Str("server_version"), "trace", info.Str("trace")) : null;
        man["chapters"] = ch;
        man["counts"] = new JO("actions", Actions.Count, "statements", stm.Count - errors, "errors", errors, "transactions", tx, "shots", shots);
        man["warnings"] = warnings;
        man["files"] = new JO("events", "events.jsonl", "actions", "actions.jsonl", "subtitles", "sql.vtt", "sql_table", "sql.tsv", "viewer", "viewer.html");
        Json.WriteFile(Path.Combine(Dir, "recording.json"), man);

        // viewer with the data inlined
        string tpl = Share.File("viewer", "viewer.html");
        var sq = new List<object>();
        foreach (var e in events) sq.Add(new JO("t", Math.Round(e.Offset ?? 0, 3), "session", e.Session, "user", e.User, "db", e.Database, "app", e.Application, "kind", e.Kind,
            "sql", e.Kind == "transaction" ? Summary(e) : e.Kind == "error" ? e.Error + (e.Sql != null ? "\n-- statement: " + e.Sql : "") : e.Sql, "params", e.Params == null ? null : new List<object>(e.Params.ToArray())));
        var acts = new List<object>();
        foreach (var a in Actions) if (a.Str("kind") != "chapter") acts.Add(new JO("t", a.Num("offset", 0), "kind", a.Str("kind"), "text", ActionText(a), "shot", a.Str("shot")));
        var data = new JO("name", Name, "duration", Math.Round(duration, 3), "video", Rec != null ? "video.mp4" : null, "recorded", Clock.Iso(T0Us), "chapters", ch, "sql", sq, "actions", acts);
        File.WriteAllText(Path.Combine(Dir, "viewer.html"), File.ReadAllText(tpl, Encoding.UTF8).Replace("/*DATA*/null", Json.Write(data).Replace("</", "<\\/")), new UTF8Encoding(false));

        var res = new JO("recording", Dir, "duration", Math.Round(duration, 3), "actions", Actions.Count, "statements", stm.Count - errors, "errors", errors, "transactions", tx, "chapters", Chapters.Count);
        if (Rec != null) { res["video"] = Path.Combine(Dir, "video.mp4"); res["frames"] = Rec.Frames; }
        if (warnings.Count > 0) res["warnings"] = warnings;
        return res;
    }

    public static string Summary(DbEvent e) {
        var sb = new StringBuilder("-- transaction " + e.Tx + ": ");
        var n = new Dictionary<string, int>(); var order = new List<string>();
        foreach (var r in e.Rows) { string k = r.Str("op") + " " + r.Str("table"); if (!n.ContainsKey(k)) { n[k] = 0; order.Add(k); } n[k]++; }
        for (int i = 0; i < order.Count; i++) sb.Append((i > 0 ? ", " : "") + n[order[i]] + " x " + order[i]);
        return sb.ToString();
    }
    public static string ActionText(JO a) {
        string k = a.Str("kind", "");
        if (k == "click") return "click " + (a.Str("control") != null && a.Str("control") != "" ? "\"" + a.Str("control") + "\"" : a.Str("x") + "," + a.Str("y")) + (a.Str("window") != null ? " in " + a.Str("window") : "");
        if (k == "fill") return "fill " + a.Str("class") + " = " + a.Str("text");
        if (k == "type") return "type " + a.Str("text");
        if (k == "keys") return "keys " + a.Str("keys");
        if (k == "web_click" || k == "web_fill") return k + " " + a.Str("target") + (a.Str("text") != null ? " = " + a.Str("text") : "");
        if (k == "window") return "window " + a.Str("window");
        if (k == "launch") return "launch " + a.Str("path");
        return k + (a.Str("caption") != null ? " " + a.Str("caption") : "");
    }
}

/// <summary>The operations shared by scenarios, the command line and the MCP server.</summary>
public class Controller {
    public Config Cfg; public Win Current; public Browser Web = new Browser(); public Recording Rec;
    public int MoveMs = 450, TypeDelay = 55;
    /// <summary>real or background (see Desktop).</summary>
    public string Input = "real";
    /// <summary>Save a reference picture after every action while recording.</summary>
    public bool Shots = true;
    public bool WebCurrent;   // the current target is the browser page
    public bool NoVideo;      // scenarios record without video whatever they say
    public bool InputForced;  // the input mode was chosen by the caller; a scenario does not change it
    public List<int> Started = new List<int>();   // process ids started through Launch
    public Action<string> LogLine = delegate (string s) { };
    public bool Background { get { return Input == "background"; } }

    public Controller(Config cfg) {
        Cfg = cfg; Input = cfg.Input;
        string b = cfg.ToolOrNull("browser"); if (b != null) Web.Executable = b;
        Web.ProfileRoot = cfg.Sub(".eqlmaker");   // the browser profile stays inside the project folder
    }

    public Point Abs(int x, int y) {
        if (Current == null) return new Point(x, y);
        Desktop.Refresh(Current);
        return new Point(Current.X + x, Current.Y + y);
    }

    void Act(JO a) {
        if (Rec == null || !Rec.Running) return;
        if (Shots) TakeShot(a);
        if (!a.Has("window")) a["window"] = WebCurrent ? Web.Title() : Current != null ? Desktop.Refresh(Current).Title : null;
        Rec.Log(a);
    }

    /// <summary>Reference picture of the current target after an action, and, for a window, the list of its
    /// controls (text, class, position) next to it: shots/0001.png and shots/0001.json.</summary>
    void TakeShot(JO a) {
        try {
            Thread.Sleep(Background ? 80 : 200);
            byte[] png = ShotPng(0);
            if (png == null) return;
            string rel = Rec.SaveShot(png); a["shot"] = rel;
            if (!WebCurrent && Current != null) {
                Desktop.Refresh(Current);
                var l = new List<object>(); foreach (var ch in Desktop.Children(Current)) l.Add(ch.ToJson(Current));
                Json.WriteFile(Path.Combine(Rec.Dir, rel.Replace('/', Path.DirectorySeparatorChar).Replace(".png", ".json")), new JO("window", Current.Title, "width", Current.W, "height", Current.H, "controls", l));
            }
        } catch { }
    }

    /// <summary>Picture of the current target (window, browser page or screen) as PNG.</summary>
    public byte[] ShotPng(int maxWidth) {
        if (WebCurrent && Background) return Web.Screenshot();
        if (Current != null) {
            if (Background) return Desktop.PngWindow(Current, maxWidth);
            Desktop.Refresh(Current);
            return Desktop.Png(Rectangle.Intersect(new Rectangle(Current.X, Current.Y, Current.W, Current.H), Desktop.Screen()), maxWidth);
        }
        if (Background) return null;
        return Desktop.Png(Desktop.Screen(), maxWidth);
    }

    public void Click(int x, int y, string button, bool dbl, int moveMs) {
        long ts = Clock.NowUs();
        var p = Abs(x, y);
        if (Background) {
            if (Current == null) throw new Exception("background mode needs a current window (focus_window / \"window\" step)");
            Desktop.BackgroundClick(Current, p.X, p.Y, button, dbl); Thread.Sleep(120);
        } else {
            Desktop.RequireVisible();
            Desktop.Move(p.X, p.Y, moveMs < 0 ? MoveMs : moveMs); Thread.Sleep(120);
            ts = Clock.NowUs();
            Desktop.Click(button, dbl); Thread.Sleep(dbl ? 300 : 250);
        }
        Act(new JO("ts", ts, "kind", "click", "x", x, "y", y, "button", button, "double", dbl));
    }
    /// <summary>Clicks a control found by text/class; at = offset inside the control (default: its centre).</summary>
    public Child ClickControl(JO q, bool dbl) {
        if (Current == null) throw new Exception("choose a window first");
        var ch = Desktop.FindChild(Current, q.Str("text"), q.Str("class"), q.Int("index", 0), q.Int("timeout", 10000));
        Desktop.Refresh(Current);
        int x = ch.Bounds.X - Current.X + ch.Bounds.Width / 2, y = ch.Bounds.Y - Current.Y + ch.Bounds.Height / 2;
        var at = q.Arr("at");
        if (at != null) { x = ch.Bounds.X - Current.X + Convert.ToInt32(at[0]); y = ch.Bounds.Y - Current.Y + Convert.ToInt32(at[1]); }
        bool shots = Shots; Shots = false;
        try { Click(x, y, "left", dbl, -1); } finally { Shots = shots; }
        // the generic click entry is replaced by one that names the control
        if (Rec != null && Rec.Running && Rec.Actions.Count > 0) {
            var a = Rec.Actions[Rec.Actions.Count - 1];
            a["kind"] = "click"; a["control"] = ch.Text; a["class"] = ch.Class;
            if (Shots) TakeShot(a);
        }
        return ch;
    }
    /// <summary>Clicks an edit control, replaces its content with value.</summary>
    public void FillControl(JO q, string value) {
        bool shots = Shots; Shots = false;
        try {
            var ch = ClickControl(q, false);
            if (Background) { Desktop.SelectAll(ch.Handle); Desktop.BackgroundType(Current, value, 2); }
            else { Desktop.Keys("^a"); Thread.Sleep(80); Desktop.Type(value, TypeDelay); }
        } finally { Shots = shots; }
        if (Rec != null && Rec.Running && Rec.Actions.Count > 0) {
            var a = Rec.Actions[Rec.Actions.Count - 1];
            a["kind"] = "fill"; a["text"] = q.Bool("secret", false) ? "***" : value;
            if (Shots) TakeShot(a);
        }
    }

    public void MoveTo(int x, int y, int ms) { if (Background) return; var p = Abs(x, y); Desktop.Move(p.X, p.Y, ms < 0 ? MoveMs : ms); }

    public void TypeText(string text, bool secret) {
        long ts = Clock.NowUs();
        if (WebCurrent && Background) Web.DevType(text);
        else if (Background) { if (Current == null) throw new Exception("background mode needs a current window"); Desktop.BackgroundType(Current, text, 5); }
        else { Desktop.RequireVisible(); Desktop.Type(text, TypeDelay); }
        Act(new JO("ts", ts, "kind", "type", "text", secret ? "***" : text));
    }
    public void Keys(string keys) {
        long ts = Clock.NowUs();
        if (WebCurrent && Background) {
            foreach (Match m in Regex.Matches(keys, @"\{(\w+)\}")) { string k = m.Groups[1].Value.ToUpperInvariant(); Web.DevKey(k == "ENTER" ? "Enter" : k == "TAB" ? "Tab" : k == "ESC" ? "Escape" : k == "DEL" ? "Delete" : k == "BACKSPACE" ? "Backspace" : k); }
        } else if (Background) { if (Current == null) throw new Exception("background mode needs a current window"); Desktop.BackgroundKeys(Current, keys); }
        else { Desktop.RequireVisible(); Desktop.Keys(keys); }
        Thread.Sleep(150);
        Act(new JO("ts", ts, "kind", "keys", "keys", keys));
    }
    public void Scroll(int notches) {
        if (Background) { if (Current == null) throw new Exception("background mode needs a current window"); Desktop.Refresh(Current); Desktop.BackgroundScroll(Current, Current.X + Current.W / 2, Current.Y + Current.H / 2, notches); }
        else Desktop.Scroll(notches);
        Act(new JO("kind", "scroll", "notches", notches));
    }

    public Win UseWindow(string pattern, bool maximize, int timeoutMs) {
        Current = Desktop.WaitWindow(pattern, timeoutMs); WebCurrent = false;
        if (!Background) Desktop.Focus(Current, maximize);
        else { Thread.Sleep(150); Desktop.Refresh(Current); }
        Act(new JO("kind", "window", "window", Current.Title));
        return Current;
    }

    public int Launch(string path, string args, string cwd) {
        PgConnInfo.AdminPassword();   // takes the capture password out of the environment before a program is started
        int pid = Desktop.Launch(path, args, cwd);
        if (pid != 0) Started.Add(pid);
        if (Rec != null && Rec.Running) Rec.Log(new JO("kind", "launch", "path", Path.GetFileName(path), "args", args));
        return pid;
    }

    /// <summary>Ends the programs started through Launch: asks their windows to close, then ends what is left (by process id).</summary>
    public void CloseStarted() {
        foreach (int pid in Started) {
            try {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                foreach (var w in Desktop.Windows()) if (w.Pid == pid) W.PostMessage(w.Handle, 0x10 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                if (!p.WaitForExit(3000)) p.Kill();
            } catch { }
        }
        Started.Clear();
        if (Web.ProcessId != 0) Web.Quit();
    }

    // ------------------------------------------------------------ web
    public void WebOpen(string url, Rectangle? bounds, bool maximize) {
        Web.Headless = Background;
        Web.Open(url, bounds);
        WebCurrent = true;
        if (!Background) { var w = Web.Window(); if (w != null) { Current = w; Desktop.Focus(w, maximize); } }
        else { Current = null; Web.WaitLoaded(15000); }
        Act(new JO("kind", "web_open", "url", url));
    }
    public void WebClick(JO q, bool dbl) {
        var p = Web.Locate(q, q.Int("timeout", 10000));
        long ts = Clock.NowUs();
        if (Background) { Web.DevClick(p[2], p[3], dbl); Thread.Sleep(150); }
        else { Desktop.RequireVisible(); Desktop.Move((int)p[0], (int)p[1], MoveMs); Thread.Sleep(120); ts = Clock.NowUs(); Desktop.Click("left", dbl); Thread.Sleep(250); }
        Act(new JO("ts", ts, "kind", "web_click", "target", Target(q)));
    }
    /// <summary>Moves the mouse to a web element without clicking (real mode; in background mode there is no mouse).</summary>
    public void WebMove(JO q, int ms) {
        var p = Web.Locate(q, q.Int("timeout", 10000));
        if (Background) return;
        Desktop.RequireVisible(); Desktop.Move((int)p[0], (int)p[1], ms < 0 ? MoveMs : ms); Thread.Sleep(120);
    }
    public void WebFill(JO q, string value, bool clear) {
        var p = Web.Locate(q, q.Int("timeout", 10000));
        long ts = Clock.NowUs();
        if (Background) {
            Web.DevClick(p[2], p[3], false);
            if (clear) Web.DevSelectAll();
            Web.DevType(value);
        } else {
            Desktop.RequireVisible();
            Desktop.Move((int)p[0], (int)p[1], MoveMs); Thread.Sleep(120); Desktop.Click("left", false); Thread.Sleep(200);
            if (clear) { Desktop.Keys("^a"); Thread.Sleep(80); Desktop.Keys("{DEL}"); Thread.Sleep(80); }
            Desktop.Type(value, TypeDelay);
        }
        Act(new JO("ts", ts, "kind", "web_fill", "target", Target(q), "text", q.Bool("secret", false) ? "***" : value));
    }
    static string Target(JO q) { return q.Str("css") ?? (q.Str("text") != null ? "text=" + q.Str("text") : "contains=" + q.Str("contains")); }

    // ------------------------------------------------------------ recording
    public Rectangle RegionOf(string region) {
        if (string.IsNullOrEmpty(region) || region == "workarea") return Desktop.WorkArea();
        if (region == "screen") return Desktop.Screen();
        var m = Regex.Match(region, @"^(-?\d+),(-?\d+),(\d+),(\d+)$");
        if (m.Success) return new Rectangle(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
        var w = Desktop.WaitWindow(region.StartsWith("window:") ? region.Substring(7) : region, 10000);
        if (!Background) Desktop.Focus(w, false);
        return Rectangle.Intersect(new Rectangle(w.X, w.Y, w.W, w.H), Desktop.WorkArea());
    }

    /// <summary>Roles and databases to capture instead of the configuration, for the next recordings.</summary>
    public JO CaptureOverride;

    public Recording StartRecording(string name, string region, int fps, bool video, bool capture) {
        if (Rec != null && Rec.Running) throw new Exception("a recording is already running: " + Rec.Name);
        if (string.IsNullOrEmpty(name)) name = "recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var r = new Recording { Cfg = Cfg, Name = name, Dir = Path.Combine(Cfg.RecordingsDir, name), Fps = fps > 0 ? fps : 15, Video = video, Capture = capture, Input = Input, CaptureOverride = CaptureOverride };
        if (video) {
            if (Background) {
                // frames are painted by the current window, or by the browser page
                if (WebCurrent) {
                    var size = Web.Eval("[innerWidth, innerHeight]") as List<object>;
                    r.Region = new Rectangle(0, 0, Convert.ToInt32(size[0]), Convert.ToInt32(size[1]));
                    var web = Web;
                    r.Painter = delegate (IntPtr dc, int w, int h) {
                        using (var ms = new MemoryStream(web.Screenshot())) using (var bmp = new Bitmap(ms)) using (var g = Graphics.FromHdc(dc)) g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                        return true;
                    };
                } else {
                    if (Current == null) throw new Exception("background mode records the current window: choose it first (focus_window / \"window\" step), or record without video");
                    Desktop.Refresh(Current);
                    r.Region = new Rectangle(0, 0, Current.W, Current.H);
                    var win = Current;
                    r.Painter = delegate (IntPtr dc, int w, int h) { return W.PrintWindow(win.Handle, dc, 2) || W.PrintWindow(win.Handle, dc, 0); };
                }
            } else r.Region = RegionOf(region);
        }
        r.Start();
        Rec = r;
        return r;
    }
    public JO StopRecording() {
        if (Rec == null || !Rec.Running) throw new Exception("no recording is running");
        return Rec.Stop();
    }
    public void Mark(string caption) {
        if (Rec == null || !Rec.Running) return;
        Rec.Mark(caption);
    }
}

/// <summary>Scenario: a JSON file with settings and steps (docs/scenario.md).</summary>
public static class Scenario {
    public static JO Run(Controller c, string file, string nameOverride) { return Run(c, file, nameOverride, null); }

    /// <summary>vars: values for ${NAME} in the scenario file; names not given are taken from the environment.</summary>
    public static JO Run(Controller c, string file, string nameOverride, JO vars) {
        string text = File.ReadAllText(file, Encoding.UTF8);
        text = Regex.Replace(text, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", delegate (Match m) {
            string k = m.Groups[1].Value, v = vars != null && vars.Has(k) ? vars.Str(k) : Environment.GetEnvironmentVariable(k);
            if (v == null) throw new Exception("scenario " + Path.GetFileName(file) + ": no value for ${" + k + "} (set the environment variable " + k + ")");
            var sb = new StringBuilder(); Json.Quote(sb, v); return sb.ToString(1, sb.Length - 2);   // escaped for use inside a JSON string
        });
        var sc = Json.ParseObject(text);
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(file));
        string name = !string.IsNullOrEmpty(nameOverride) ? nameOverride : sc.Str("name") ?? Path.GetFileNameWithoutExtension(file);
        var capObj = sc.Obj("capture");
        bool video = sc.Bool("video", true) && !c.NoVideo, capture = capObj != null || sc.Bool("capture", true);
        if (capObj != null) c.CaptureOverride = capObj;
        if (sc.Has("sql") && !sc.Has("capture")) c.LogLine("note: the scenario has the key \"sql\" of the earlier recorder; EQLMaker reads \"capture\" (run: eqlmaker convert-scenario " + Path.GetFileName(file) + ")");
        string region = sc.Str("region", "workarea");
        int fps = sc.Int("fps", 15);
        if (sc.Has("input") && !c.InputForced) c.Input = sc.Str("input");
        if (sc.Has("moveMs")) c.MoveMs = sc.Int("moveMs", 450);
        if (sc.Has("typeDelay")) c.TypeDelay = sc.Int("typeDelay", 55);
        if (sc.Has("shots")) c.Shots = sc.Bool("shots", true);
        var steps = sc.Arr("steps");
        if (steps == null) throw new Exception("scenario without \"steps\": " + file);
        bool hasRecordStep = false;
        foreach (JO s in steps) if (s.Has("record")) hasRecordStep = true;
        if (!c.Background) Desktop.RequireVisible();
        // without "record" steps the whole scenario is recorded; the capture starts first, so
        // programs started by the scenario are captured from their first statement. In background
        // mode the video needs the window, so there the video part starts with the first window.
        bool videoPending = false;
        if (!hasRecordStep) {
            if (c.Background && video) { c.StartRecording(name, region, fps, false, capture); videoPending = true; }
            else c.StartRecording(name, region, fps, video, capture);
        }
        JO result = null; int n = 0;
        try {
            foreach (JO s in steps) {
                n++;
                c.LogLine("step " + n + ": " + Json.Write(s));
                Step(c, s, baseDir, name, region, fps, video, capture, ref result);
                if (videoPending && (c.Current != null || c.WebCurrent) && (s.Has("window") || s.Has("webopen"))) { StartBackgroundVideo(c, fps); videoPending = false; }
            }
            if (c.Rec != null && c.Rec.Running) result = c.StopRecording();
        } catch (Exception e) {
            if (c.Rec != null && c.Rec.Running) { try { c.StopRecording(); } catch { } }
            if (sc.Bool("closeAtEnd", false)) c.CloseStarted();
            throw new Exception("step " + n + ": " + e.Message, e);
        }
        if (sc.Bool("closeAtEnd", false)) c.CloseStarted();
        return result;
    }

    // the recording already runs (capture first); the video joins when the window exists. The time
    // line keeps its start, so the video gets an offset that is written to recording.json.
    static void StartBackgroundVideo(Controller c, int fps) {
        var r = c.Rec; FramePainter painter; Rectangle region;
        if (c.WebCurrent) {
            var size = c.Web.Eval("[innerWidth, innerHeight]") as List<object>;
            region = new Rectangle(0, 0, Convert.ToInt32(size[0]), Convert.ToInt32(size[1]));
            var web = c.Web;
            painter = delegate (IntPtr dc, int w, int h) {
                using (var ms = new MemoryStream(web.Screenshot())) using (var bmp = new Bitmap(ms)) using (var g = Graphics.FromHdc(dc)) g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                return true;
            };
        } else {
            Desktop.Refresh(c.Current);
            region = new Rectangle(0, 0, c.Current.W, c.Current.H);
            var win = c.Current;
            painter = delegate (IntPtr dc, int w, int h) { return W.PrintWindow(win.Handle, dc, 2) || W.PrintWindow(win.Handle, dc, 0); };
        }
        try {
            var rec = new ScreenRecorder { File = Path.Combine(r.Dir, "video.mp4"), Region = region, Fps = fps > 0 ? fps : 15, Painter = painter };
            rec.Start();
            // everything recorded so far is shifted so that offset 0 is the first video frame
            r.Rec = rec; r.Region = rec.Region; r.Fps = rec.Fps;
            long shift = rec.StartedAtUs - r.T0Us; r.T0Us = rec.StartedAtUs;
            foreach (var a in r.Actions) a["offset"] = Math.Round(a.Num("offset", 0) - shift / 1e6, 6);
            foreach (var ch in r.Chapters) ch.Start = Math.Max(0, ch.Start - shift / 1e6);
        } catch (Exception e) { c.LogLine("video not available: " + e.Message); r.Log(new JO("kind", "note", "caption", "video not available: " + e.Message)); }
    }

    static void Step(Controller c, JO s, string baseDir, string name, string region, int fps, bool video, bool capture, ref JO result) {
        int moveMs = s.Int("ms", -1);
        if (s.Has("record")) {
            if (s.Str("record") == "start") c.StartRecording(name, s.Str("region") ?? region, fps, video, capture);
            else result = c.StopRecording();
        }
        else if (s.Has("caption")) c.Mark(s.Str("caption"));
        else if (s.Has("run")) {
            string f = s.Str("run"); if (!Path.IsPathRooted(f) && File.Exists(Path.Combine(baseDir, f))) f = Path.Combine(baseDir, f);
            string cwd = s.Str("cwd"); if (cwd != null && !Path.IsPathRooted(cwd)) cwd = Path.Combine(baseDir, cwd);
            c.Launch(f, s.Str("args"), cwd);
        }
        else if (s.Has("window")) c.UseWindow(s.Str("window"), s.Bool("maximize", false), s.Int("timeout", 30000));
        else if (s.Has("screen")) { c.Current = null; c.WebCurrent = false; }
        else if (s.Has("control")) c.ClickControl(Q(s["control"]), s.Bool("double", false));
        else if (s.Has("fill")) { var q = Q(s["fill"]); c.FillControl(q, q.Str("value", "")); }
        else if (s.Has("click")) { var p = Pt(s["click"]); c.Click(p.X, p.Y, s.Str("button", "left"), false, moveMs); }
        else if (s.Has("dclick")) { var p = Pt(s["dclick"]); c.Click(p.X, p.Y, "left", true, moveMs); }
        else if (s.Has("rclick")) { var p = Pt(s["rclick"]); c.Click(p.X, p.Y, "right", false, moveMs); }
        else if (s.Has("move")) { var p = Pt(s["move"]); c.MoveTo(p.X, p.Y, moveMs); }
        else if (s.Has("type")) c.TypeText(s.Str("type"), s.Bool("secret", false));
        else if (s.Has("keys")) c.Keys(s.Str("keys"));
        else if (s.Has("scroll")) c.Scroll(s.Int("scroll", 0));
        else if (s.Has("wait")) Thread.Sleep(s.Int("wait", 0));
        else if (s.Has("webopen")) {
            Rectangle? bounds = null;
            if (s.Has("bounds")) { var b = s.Arr("bounds"); bounds = new Rectangle(Convert.ToInt32(b[0]), Convert.ToInt32(b[1]), Convert.ToInt32(b[2]), Convert.ToInt32(b[3])); }
            c.WebOpen(s.Str("webopen"), bounds, s.Bool("maximize", false));
        }
        else if (s.Has("webgo")) c.Web.Navigate(s.Str("webgo"));
        else if (s.Has("webreload")) c.Web.Reload();
        else if (s.Has("webclick")) c.WebClick(Q(s["webclick"]), false);
        else if (s.Has("webdclick")) c.WebClick(Q(s["webdclick"]), true);
        else if (s.Has("webmove")) { var q = Q(s["webmove"]); c.WebMove(q, moveMs); }
        else if (s.Has("webfill")) { var q = Q(s["webfill"]); c.WebFill(q, q.Str("value", ""), q.Bool("clear", true)); }
        else if (s.Has("webwait")) { var q = Q(s["webwait"]); c.Web.Locate(q, q.Int("timeout", 15000)); }
        else if (s.Has("webeval")) c.Web.Eval(s.Str("webeval"));
        else if (s.Has("screenshot")) {
            string f = s.Str("screenshot"); if (!Path.IsPathRooted(f)) f = Path.Combine(c.Rec != null ? c.Rec.Dir : c.Cfg.RecordingsDir, f);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllBytes(f, c.ShotPng(0));
        }
        else throw new Exception("unknown step: " + Json.Write(s));
    }
    static Point Pt(object v) { var a = (List<object>)v; return new Point(Convert.ToInt32(a[0]), Convert.ToInt32(a[1])); }
    static JO Q(object v) { var d = v as JO; return d ?? new JO("text", Convert.ToString(v, CultureInfo.InvariantCulture)); }
}
}
