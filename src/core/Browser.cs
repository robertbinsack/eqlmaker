// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Browser: Microsoft Edge or Google Chrome through the DevTools protocol. Elements are found in the
// page. In real mode they are clicked with the real mouse (visible in a screen recording); in
// background mode input and pictures go through DevTools and the browser may run headless.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace EqlMaker {

public class Browser {
    public int Port = 9333; public string UrlPrefix = "";
    /// <summary>Path of msedge.exe or chrome.exe; empty = search the usual places.</summary>
    public string Executable = "";
    public bool Headless;
    /// <summary>Folder under which the browser keeps its own profile (default: the local application data of the user).</summary>
    public string ProfileRoot = "";
    /// <summary>Delete the profile when the browser is ended (for one-off runs).</summary>
    public bool TemporaryProfile;
    string profileDir;
    public int ProcessId;
    ClientWebSocket ws; int id; Process process;

    public string FindExecutable() {
        if (!string.IsNullOrEmpty(Executable)) { if (File.Exists(Executable)) return Executable; throw new Exception("browser not found: " + Executable); }
        string[] c = {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe") };
        foreach (var f in c) if (File.Exists(f)) return f;
        throw new Exception("Microsoft Edge or Google Chrome not found (set tools.browser in the configuration)");
    }

    string Http(string path, string method) {
        var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + path);
        req.Method = method; req.Timeout = 3000; req.Proxy = null;
        using (var r = req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream())) return sr.ReadToEnd();
    }
    public bool Running() { try { Http("/json/version", "GET"); return true; } catch { return false; } }

    /// <summary>Starts the browser with its own profile (unless one already listens on the port) and opens url.</summary>
    public void Open(string url, Rectangle? bounds) {
        if (!Running()) {
            string profile = Path.Combine(string.IsNullOrEmpty(ProfileRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EQLMaker") : ProfileRoot, "browser-" + Port);
            profileDir = profile;
            Directory.CreateDirectory(profile);
            string args = "--remote-debugging-port=" + Port + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --disable-features=Translate";
            if (Headless) args += " --headless=new --hide-scrollbars";
            if (bounds.HasValue) args += string.Format(" --window-position={0},{1} --window-size={2},{3}", bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
            else if (Headless) args += " --window-size=1280,800";
            args += " \"" + url + "\"";
            var psi = new ProcessStartInfo(FindExecutable(), args); psi.UseShellExecute = false;
            // the browser writes notes to its error output: read and drop them, they are not ours to show
            psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
            process = Process.Start(psi); ProcessId = process.Id;
            process.ErrorDataReceived += delegate { }; process.OutputDataReceived += delegate { };
            process.BeginErrorReadLine(); process.BeginOutputReadLine();
            var sw = Stopwatch.StartNew();
            while (!Running()) { if (sw.ElapsedMilliseconds > 30000) throw new Exception("the browser did not open the DevTools port " + Port); Thread.Sleep(200); }
            UrlPrefix = Origin(url);
            Thread.Sleep(1000);
        } else {
            UrlPrefix = Origin(url);
            Close();
            if (Page() == null) Http("/json/new?" + url, "PUT");
            else Navigate(url);
        }
    }
    static string Origin(string url) { try { var u = new Uri(url); return u.GetLeftPart(UriPartial.Authority); } catch { return ""; } }

    /// <summary>Ends a browser started by Open (asks it through DevTools, then by process id).</summary>
    public void Quit() {
        try { CallRaw(BrowserSocket(), "Browser.close", new JO()); } catch { }
        Close();
        if (ProcessId != 0) { try { var p = Process.GetProcessById(ProcessId); if (!p.WaitForExit(5000)) p.Kill(); } catch { } ProcessId = 0; }
        if (process != null) { try { process.Dispose(); } catch { } process = null; }
        if (TemporaryProfile && profileDir != null) {
            for (int i = 0; i < 10; i++) { try { if (Directory.Exists(profileDir)) Directory.Delete(profileDir, true); break; } catch { Thread.Sleep(300); } }
            profileDir = null;
        }
    }
    ClientWebSocket BrowserSocket() {
        var v = Json.ParseObject(Http("/json/version", "GET"));
        var s = new ClientWebSocket();
        s.ConnectAsync(new Uri(v.Str("webSocketDebuggerUrl")), CancellationToken.None).Wait(10000);
        return s;
    }

    JO Page() {
        var list = Json.Parse(Http("/json/list", "GET")) as List<object>;
        JO first = null;
        if (list == null) return null;
        foreach (JO p in list) {
            if (p.Str("type") != "page") continue;
            string u = p.Str("url", "");
            if (first == null && !u.StartsWith("devtools:")) first = p;
            if (UrlPrefix != "" && u.StartsWith(UrlPrefix)) return p;
        }
        return first;
    }
    public string Title() { var p = Page(); return p == null ? "" : p.Str("title", ""); }
    public string Url() { var p = Page(); return p == null ? "" : p.Str("url", ""); }

    void Connect() {
        if (ws != null && ws.State == WebSocketState.Open) return;
        var p = Page();
        if (p == null) throw new Exception("no browser page on port " + Port);
        ws = new ClientWebSocket();
        ws.ConnectAsync(new Uri(p.Str("webSocketDebuggerUrl")), CancellationToken.None).Wait(10000);
    }
    public void Close() { if (ws != null) { try { ws.Abort(); } catch { } ws = null; } }

    JO CallRaw(ClientWebSocket s, string method, JO prm) {
        int my = ++id;
        var bytes = Encoding.UTF8.GetBytes(Json.Write(new JO("id", my, "method", method, "params", prm)));
        s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(10000);
        var buf = new byte[1 << 16];
        while (true) {
            var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do {
                var t = s.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (!t.Wait(30000)) throw new Exception("the browser did not answer " + method);
                r = t.Result; ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            var m = Json.ParseObject(Encoding.UTF8.GetString(ms.ToArray()));
            if (m.Has("id") && m.Int("id", -1) == my) {
                if (m.Has("error")) throw new Exception("browser: " + Json.Write(m["error"]));
                return m.Obj("result") ?? new JO();
            }
        }
    }
    readonly object gate = new object();
    public JO Call(string method, JO prm) { lock (gate) { Connect(); return CallRaw(ws, method, prm); } }

    public object Eval(string expression) {
        var res = Call("Runtime.evaluate", new JO("expression", expression, "returnByValue", true, "awaitPromise", true));
        if (res.Has("exceptionDetails")) throw new Exception("page script: " + Json.Write(res["exceptionDetails"]));
        var v = res.Obj("result");
        return v != null && v.Has("value") ? v["value"] : null;
    }

    public void Navigate(string url) { Call("Page.navigate", new JO("url", url)); Thread.Sleep(600); WaitLoaded(15000); }
    public void Reload() { Call("Page.reload", new JO()); Thread.Sleep(600); WaitLoaded(15000); }
    public void WaitLoaded(int timeoutMs) {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) {
            try { if (Convert.ToString(Eval("document.readyState")) == "complete") return; } catch { }
            Thread.Sleep(100);
        }
    }

    /// <summary>Finds an element. Query: css (selector), text (exact visible text), contains (part of the
    /// text), index; the smallest match wins. Returns screen x, y and viewport x, y of its centre.</summary>
    public double[] Locate(JO q, int timeoutMs) {
        string expr = "(() => { const q = " + Json.Write(q) + @";
            let els = [...document.querySelectorAll(q.css || 'a,button,label,td,th,li,span,div,h1,h2,h3,h4,p,input,select,textarea,option,summary')];
            const vis = (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
            els = els.filter(vis);
            const txt = (e) => (e.innerText || e.value || e.getAttribute('aria-label') || e.placeholder || '').trim();
            if (q.text != null) els = els.filter((e) => txt(e) === q.text);
            if (q.contains != null) els = els.filter((e) => txt(e).includes(q.contains));
            if (!els.length) return null;
            const area = (e) => { const r = e.getBoundingClientRect(); return r.width * r.height; };
            els.sort((a, b) => area(a) - area(b));
            const e = els[q.index || 0] || els[0];
            e.scrollIntoView({ block: 'nearest' });
            const r = e.getBoundingClientRect();
            const ox = screenX + (outerWidth - innerWidth) / 2, oy = screenY + (outerHeight - innerHeight) - (outerWidth - innerWidth) / 2;
            const vx = r.left + r.width / 2, vy = r.top + r.height / 2;
            return [Math.round(ox + vx), Math.round(oy + vy), vx, vy];
        })()";
        var sw = Stopwatch.StartNew();
        while (true) {
            var v = Eval(expr) as List<object>;
            if (v != null) return new[] { Convert.ToDouble(v[0]), Convert.ToDouble(v[1]), Convert.ToDouble(v[2]), Convert.ToDouble(v[3]) };
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("web element not found: " + Json.Write(q));
            Thread.Sleep(200);
        }
    }

    // ------------------------------------------------------------ background mode (DevTools input)
    public void DevClick(double vx, double vy, bool dbl) {
        Call("Input.dispatchMouseEvent", new JO("type", "mouseMoved", "x", vx, "y", vy));
        for (int n = 1; n <= (dbl ? 2 : 1); n++) {
            Call("Input.dispatchMouseEvent", new JO("type", "mousePressed", "x", vx, "y", vy, "button", "left", "clickCount", n));
            Call("Input.dispatchMouseEvent", new JO("type", "mouseReleased", "x", vx, "y", vy, "button", "left", "clickCount", n));
        }
    }
    public void DevType(string text) { Call("Input.insertText", new JO("text", text)); }
    public void DevSelectAll() { Eval("(() => { const e = document.activeElement; if (e && e.select) e.select(); })()"); }
    public void DevKey(string key) {
        int code = key == "Enter" ? 13 : key == "Tab" ? 9 : key == "Escape" ? 27 : key == "Backspace" ? 8 : key == "Delete" ? 46 : 0;
        var p = new JO("type", "keyDown", "key", key, "code", key, "windowsVirtualKeyCode", code, "nativeVirtualKeyCode", code);
        if (key == "Enter") { p["text"] = "\r"; p["unmodifiedText"] = "\r"; }
        Call("Input.dispatchKeyEvent", p);
        Call("Input.dispatchKeyEvent", new JO("type", "keyUp", "key", key, "code", key, "windowsVirtualKeyCode", code, "nativeVirtualKeyCode", code));
    }
    /// <summary>PNG of the page (viewport) drawn by the browser.</summary>
    public byte[] Screenshot() {
        var r = Call("Page.captureScreenshot", new JO("format", "png"));
        return Convert.FromBase64String(r.Str("data", ""));
    }
    public void SetViewport(int width, int height) {
        Call("Emulation.setDeviceMetricsOverride", new JO("width", width, "height", height, "deviceScaleFactor", 1, "mobile", false));
    }

    public Win Window() {
        string title = Title();
        foreach (var w in Desktop.Windows())
            if ((w.Process == "msedge" || w.Process == "chrome") && title != "" && w.Title.StartsWith(title)) return w;
        foreach (var w in Desktop.Windows()) if (w.Process == "msedge" || w.Process == "chrome") return w;
        return null;
    }
}
}
