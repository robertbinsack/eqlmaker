// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// MCP server (Model Context Protocol, JSON-RPC 2.0 over standard input/output) for any MCP client:
// remote control of Windows programs and web pages, recording, and the pipeline steps.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace EqlMaker {

public delegate object ToolHandler(JO args);

public class McpTool {
    public string Name, Description; public JO Properties = new JO(); public string[] Required = new string[0];
    public ToolHandler Handler; public bool NeedsDesktop;
    public JO ToJson() {
        return new JO("name", Name, "description", Description, "inputSchema", new JO("type", "object", "properties", Properties, "required", new List<object>(Required)));
    }
}

public class McpPrompt {
    public string Name, Description; public JO[] Arguments = new JO[0];
    public Func<JO, string> Text;
}

public class McpServer {
    public readonly List<McpTool> Tools = new List<McpTool>();
    public readonly List<McpPrompt> Prompts = new List<McpPrompt>();
    public readonly Controller C;
    public string Instructions =
        "EQLMaker: operate a Windows program or a web page, record what happens on the screen and in the database, and turn the recording step by step into a specification, a web application and comparison tests. " +
        "Remote control: take a screenshot first (or list_controls); coordinates are relative to the window chosen with focus_window. Start record_start before launching the application so its database access is captured. " +
        "Pipeline: record -> correlate -> spec -> generate / clone -> video -> verify; manual_import, video_import and enrich add knowledge from a manual and from a narrated video. " +
        "generate, clone and enrich are guided steps: the tool prepares a workspace and instructions, you do the work, verify checks the result.";
    Stream stdout;

    public McpServer(Controller c) { C = c; RemoteControl.Register(this); }

    public static JO Prop(string type, string description) { return new JO("type", type, "description", description); }
    public McpTool Add(string name, string description, JO props, string[] required, ToolHandler h) {
        var t = new McpTool { Name = name, Description = description, Properties = props ?? new JO(), Required = required ?? new string[0], Handler = h };
        Tools.Add(t); return t;
    }
    public static object Text(string text) { return new JO("content", new List<object> { new JO("type", "text", "text", text) }); }
    public static object Image(byte[] png, string note) {
        return new JO("content", new List<object> { new JO("type", "image", "data", Convert.ToBase64String(png), "mimeType", "image/png"), new JO("type", "text", "text", note) });
    }

    public object Call(string name, JO args) {
        var t = Tools.Find(delegate (McpTool x) { return x.Name == name; });
        if (t == null) throw new Exception("unknown tool: " + name);
        if (t.NeedsDesktop && !C.Background) Desktop.RequireVisible();
        object r = t.Handler(args ?? new JO());
        if (r is string) return Text((string)r);
        var jo = r as JO;
        if (jo != null && !jo.Has("content")) return Text(Json.Pretty(jo));
        return r;
    }

    void Send(object msg) {
        var b = Encoding.UTF8.GetBytes(Json.Write(msg) + "\n");
        lock (stdout) { stdout.Write(b, 0, b.Length); stdout.Flush(); }
    }

    /// <summary>Handles one JSON-RPC message; null for notifications.</summary>
    public JO Handle(JO m) {
        if (!m.Has("id")) return null;
        string method = m.Str("method"); var prm = m.Obj("params");
        object result = null; JO error = null;
        try {
            if (method == "initialize") {
                string pv = prm != null ? prm.Str("protocolVersion", "2025-06-18") : "2025-06-18";
                result = new JO("protocolVersion", pv, "capabilities", new JO("tools", new JO(), "prompts", new JO()), "serverInfo", new JO("name", "eqlmaker", "version", Config.Version), "instructions", Instructions);
            } else if (method == "tools/list") {
                var l = new List<object>(); foreach (var t in Tools) l.Add(t.ToJson());
                result = new JO("tools", l);
            } else if (method == "tools/call") {
                string tn = prm != null ? prm.Str("name") : null;
                try { result = Call(tn, prm != null ? prm.Obj("arguments") : null); }
                catch (Exception e) { result = new JO("content", new List<object> { new JO("type", "text", "text", "Error: " + e.Message) }, "isError", true); }
            } else if (method == "prompts/list") {
                var l = new List<object>();
                foreach (var p in Prompts) l.Add(new JO("name", p.Name, "description", p.Description, "arguments", new List<object>(p.Arguments)));
                result = new JO("prompts", l);
            } else if (method == "prompts/get") {
                string pn = prm != null ? prm.Str("name") : null;
                var p = Prompts.Find(delegate (McpPrompt x) { return x.Name == pn; });
                if (p == null) error = new JO("code", -32602, "message", "unknown prompt: " + pn);
                else result = new JO("description", p.Description, "messages", new List<object> { new JO("role", "user", "content", new JO("type", "text", "text", p.Text(prm.Obj("arguments") ?? new JO()))) });
            } else if (method == "ping") result = new JO();
            else error = new JO("code", -32601, "message", "method not found: " + method);
        } catch (Exception e) { error = new JO("code", -32603, "message", e.Message); }
        var resp = new JO("jsonrpc", "2.0", "id", m["id"]);
        if (error != null) resp["error"] = error; else resp["result"] = result;
        return resp;
    }

    public void Run() {
        stdout = Console.OpenStandardOutput();
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        C.LogLine = delegate (string s) { Console.Error.WriteLine(s); };
        string line;
        while ((line = input.ReadLine()) != null) {
            if (line.Trim() == "") continue;
            JO m;
            try { m = Json.ParseObject(line); } catch { continue; }
            var resp = Handle(m);
            if (resp != null) Send(resp);
        }
        if (C.Rec != null && C.Rec.Running) { try { C.StopRecording(); } catch { } }
    }
}

/// <summary>The remote-control and recording tools.</summary>
public static class RemoteControl {
    static JO P(params object[] kv) { return new JO(kv); }
    static List<object> CsvList(string s) { var l = new List<object>(); if (!string.IsNullOrEmpty(s)) foreach (var x in s.Split(',')) if (x.Trim() != "") l.Add(x.Trim()); return l; }
    static JO S(string d) { return McpServer.Prop("string", d); }
    static JO I(string d) { return McpServer.Prop("integer", d); }
    static JO B(string d) { return McpServer.Prop("boolean", d); }
    static string[] R(params string[] r) { return r; }

    public static void Register(McpServer s) {
        var c = s.C;
        s.Add("list_windows", "Lists the visible top-level windows (title, process, pid, position, size).", null, null, delegate (JO a) {
            var sb = new StringBuilder(); foreach (var w in Desktop.Windows()) sb.AppendLine(w.ToString()); return sb.ToString();
        });
        s.Add("focus_window", "Makes a window the current window: later coordinates are relative to its top-left corner. Title pattern with * and ?, or pid:<n> / process:<name>. In real mode the window comes to the foreground.",
            P("title", S("window title pattern"), "maximize", B("maximize the window"), "timeout_ms", I("wait for the window (default 10000)")), R("title"), delegate (JO a) {
            var w = c.UseWindow(a.Str("title"), a.Bool("maximize", false), a.Int("timeout_ms", 10000)); return "current window: " + w;
        });
        s.Add("use_screen", "Clears the current window: coordinates are screen coordinates again (real mode).", null, null, delegate (JO a) { c.Current = null; c.WebCurrent = false; return "coordinates are screen coordinates"; });
        s.Add("set_input_mode", "Chooses how programs are operated: 'real' (mouse, keyboard and screen; needs a visible desktop) or 'background' (window messages and window pictures; nothing visible moves, limited to programs that react to posted messages).",
            P("mode", S("real or background")), R("mode"), delegate (JO a) {
            string m = a.Str("mode"); if (m != "real" && m != "background") throw new Exception("mode must be real or background");
            c.Input = m; return "input mode: " + m;
        });
        s.Add("list_controls", "Lists the controls (child windows) of the current window with text, window class and position: a simple view of the structure of a mask.", null, null, delegate (JO a) {
            if (c.Current == null) throw new Exception("choose a window first (focus_window)");
            Desktop.Refresh(c.Current);
            var l = new List<object>(); foreach (var ch in Desktop.Children(c.Current)) l.Add(ch.ToJson(c.Current));
            return new JO("window", c.Current.Title, "controls", l);
        });
        s.Add("launch", "Starts a program.", P("path", S("program"), "args", S("arguments"), "cwd", S("working directory (default: program folder)")), R("path"), delegate (JO a) {
            return "started, pid " + c.Launch(a.Str("path"), a.Str("args"), a.Str("cwd"));
        });
        s.Add("screenshot", "Picture of the current window (or the screen / the web page) as PNG.", P("max_width", I("scale down to this width (default 1280, 0 = original)"), "screen", B("whole screen instead of the current window (real mode)")), null, delegate (JO a) {
            int mw = a.Int("max_width", 1280);
            if (a.Bool("screen", false)) { var sr = Desktop.Screen(); return McpServer.Image(Desktop.Png(sr, mw), Note("screen", sr.Width, mw)); }
            byte[] png = c.ShotPng(mw);
            if (png == null) throw new Exception("nothing to show: choose a window first (focus_window)");
            int width; using (var ms = new MemoryStream(png)) using (var bmp = new Bitmap(ms)) width = bmp.Width;
            int orig = c.Current != null && !c.WebCurrent ? Desktop.Refresh(c.Current).W : width;
            return McpServer.Image(png, Note(c.WebCurrent && c.Background ? "web page" : c.Current != null ? "window \"" + c.Current.Title + "\"" : "screen", orig, mw));
        }).NeedsDesktop = true;
        s.Add("click", "Clicks at x,y (relative to the current window, or the screen).", P("x", I("x"), "y", I("y"), "button", S("left (default), right, middle"), "double", B("double click")), R("x", "y"), delegate (JO a) {
            c.Click(a.Int("x", 0), a.Int("y", 0), a.Str("button", "left"), a.Bool("double", false), -1); return "clicked";
        }).NeedsDesktop = true;
        s.Add("click_control", "Clicks a control of the current window found by its text and/or window class (* and ? wildcards).", P("text", S("text of the control"), "class", S("window class"), "index", I("n-th match, default 0"), "double", B("double click")), null, delegate (JO a) {
            var ch = c.ClickControl(a, a.Bool("double", false)); return "clicked " + ch.Class + " \"" + ch.Text + "\"";
        }).NeedsDesktop = true;
        s.Add("fill_control", "Clicks an edit control found by text/class/index and replaces its content.", P("text", S("text of the control"), "class", S("window class, e.g. *EDIT*"), "index", I("n-th match, default 0"), "value", S("new content")), R("value"), delegate (JO a) {
            c.FillControl(a, a.Str("value", "")); return "filled";
        }).NeedsDesktop = true;
        s.Add("move", "Moves the mouse visibly to x,y (real mode).", P("x", I("x"), "y", I("y"), "ms", I("duration of the movement (default 450)")), R("x", "y"), delegate (JO a) { c.MoveTo(a.Int("x", 0), a.Int("y", 0), a.Int("ms", -1)); return "moved"; }).NeedsDesktop = true;
        s.Add("type_text", "Types text into the focused control.", P("text", S("text"), "secret", B("do not write the text into the recording")), R("text"), delegate (JO a) { c.TypeText(a.Str("text", ""), a.Bool("secret", false)); return "typed"; }).NeedsDesktop = true;
        s.Add("press_keys", "Sends keys in SendKeys syntax: {ENTER}, {TAB}, {F5}, {DEL}; in real mode also ^a (Ctrl+A), %{F4} (Alt+F4), +{TAB} (Shift+Tab).", P("keys", S("keys")), R("keys"), delegate (JO a) { c.Keys(a.Str("keys", "")); return "sent"; }).NeedsDesktop = true;
        s.Add("scroll", "Turns the mouse wheel (positive = up).", P("notches", I("notches")), R("notches"), delegate (JO a) { c.Scroll(a.Int("notches", 0)); return "scrolled"; }).NeedsDesktop = true;
        s.Add("wait", "Waits.", P("ms", I("milliseconds")), R("ms"), delegate (JO a) { Thread.Sleep(a.Int("ms", 0)); return "waited"; });
        s.Add("web_open", "Opens a URL in Microsoft Edge or Chrome under remote control (DevTools port 9333, own profile) and makes the page the current target. In background mode the browser runs without a window.", P("url", S("URL"), "maximize", B("maximize the browser window")), R("url"), delegate (JO a) {
            c.WebOpen(a.Str("url"), null, a.Bool("maximize", false)); return "opened " + c.Web.Url();
        });
        var q = P("css", S("CSS selector"), "text", S("exact visible text of the element"), "contains", S("part of the visible text"), "index", I("n-th match (smallest elements first), default 0"));
        s.Add("web_click", "Finds an element in the web page and clicks it.", q, null, delegate (JO a) { c.WebClick(a, a.Bool("double", false)); return "clicked"; }).NeedsDesktop = true;
        s.Add("web_fill", "Clicks into a field of the web page and types a value.", P("css", S("CSS selector"), "text", S("exact visible text/label/placeholder"), "contains", S("part of the text"), "value", S("text to type"), "clear", B("clear the field first (default true)")), R("value"), delegate (JO a) {
            c.WebFill(a, a.Str("value", ""), a.Bool("clear", true)); return "filled";
        }).NeedsDesktop = true;
        s.Add("web_eval", "Evaluates JavaScript in the web page and returns the result (JSON).", P("expression", S("JavaScript expression")), R("expression"), delegate (JO a) { return Json.Write(c.Web.Eval(a.Str("expression"))); });
        s.Add("web_reload", "Reloads the web page.", null, null, delegate (JO a) { c.Web.Reload(); return "reloaded"; });
        s.Add("record_start", "Starts a recording: actions, database events of the roles/databases named in the configuration (new sessions: start the application after this call), optional video and reference pictures.",
            P("name", S("name of the recording (folder name)"), "region", S("real mode: workarea (default), screen, x,y,w,h or a window title pattern"), "fps", I("frames per second (default 15)"), "video", B("record video (default true)"), "capture", B("capture database events (default true)"), "capture_roles", S("comma-separated login roles to capture instead of the configuration"), "capture_databases", S("comma-separated databases to capture instead of the configuration")), null, delegate (JO a) {
            c.CaptureOverride = a.Has("capture_roles") || a.Has("capture_databases") ? new JO("roles", CsvList(a.Str("capture_roles")), "databases", CsvList(a.Str("capture_databases"))) : null;
            var r = c.StartRecording(a.Str("name"), a.Str("region"), a.Int("fps", 15), a.Bool("video", true), a.Bool("capture", true));
            return "recording " + r.Name + " -> " + r.Dir;
        });
        s.Add("record_caption", "Sets a chapter mark with a caption at the current time of the recording. Use one chapter per mask or business step.", P("caption", S("caption")), R("caption"), delegate (JO a) { c.Mark(a.Str("caption")); return "marked"; });
        s.Add("record_stop", "Stops the recording and writes the recording folder (recording.json, actions.jsonl, events.jsonl, video.mp4, viewer.html, shots).", null, null, delegate (JO a) { return c.StopRecording(); });
        s.Add("run_scenario", "Runs a scenario file (JSON steps) and records it.", P("path", S("scenario file"), "name", S("name of the recording")), R("path"), delegate (JO a) { return Scenario.Run(c, a.Str("path"), a.Str("name")); });
    }

    static string Note(string what, int originalWidth, int maxWidth) {
        double scale = maxWidth > 0 && originalWidth > maxWidth ? (double)originalWidth / maxWidth : 1;
        return what + (scale > 1 ? string.Format(CultureInfo.InvariantCulture, "; picture scaled by 1/{0:0.###}: multiply picture coordinates by {0:0.###} for click", scale) : "");
    }
}
}
