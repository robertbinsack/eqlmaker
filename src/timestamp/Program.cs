// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// eqlmaker-timestamp.exe: a strip with date, time (milliseconds), UTC offset and monotonic time,
// always on top of all windows and transparent for the mouse. Start it before any screen recorder;
// "eqlmaker video-import" reads the time of every frame back from the recording.
//
//   eqlmaker-timestamp.exe [--position top-right|top-left|bottom-right|bottom-left|x,y]
//                          [--display N|all] [--cell 8] [--lang en|de] [--seconds N]
//   eqlmaker-timestamp.exe --stop                      ends a running overlay
//   eqlmaker-timestamp.exe --render file.png [--at <UTC milliseconds>] [--offset <minutes>] [--cell 8]
//                                                      draws one strip into a file (no window)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EqlMaker.Timestamp {

static class Native {
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("winmm.dll")] public static extern int timeBeginPeriod(int ms);
    [DllImport("winmm.dll")] public static extern int timeEndPeriod(int ms);
    [DllImport("kernel32.dll")] public static extern void GetSystemTimePreciseAsFileTime(out long fileTime);
}

class Strip : Form {
    readonly int cell, display; readonly Stopwatch mono; readonly string label; Bitmap buffer;
    public Strip(Rectangle screen, string position, int cell, int display, Stopwatch mono, string label, double opacity) {
        this.cell = cell; this.display = display; this.mono = mono; this.label = label;
        var size = Timecode.StripSize(cell);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = display == 1;
        Text = "EQLMaker time stamp"; BackColor = Color.Black; AutoScaleMode = AutoScaleMode.None;
        int x, y; var m = System.Text.RegularExpressions.Regex.Match(position, @"^(-?\d+),(-?\d+)$");
        if (m.Success) { x = screen.X + int.Parse(m.Groups[1].Value); y = screen.Y + int.Parse(m.Groups[2].Value); }
        else {
            x = position.EndsWith("left") ? screen.X : screen.Right - size.Width;
            y = position.StartsWith("bottom") ? screen.Bottom - size.Height : screen.Y;
        }
        Bounds = new Rectangle(x, y, size.Width, size.Height);
        Opacity = opacity;
        buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
    }
    protected override CreateParams CreateParams {
        get {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /* layered */ | 0x20 /* transparent for the mouse */ | 0x08000000 /* never activated */ | 0x8 /* topmost */;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { lock (paintLock) PaintNow(e.Graphics); }

    public void PaintNow(Graphics target) {
        var v = Program.Now(mono, display);
        using (var g = Graphics.FromImage(buffer)) Timecode.Draw(g, 0, 0, cell, v, Program.Line2(v, label));
        target.DrawImageUnscaled(buffer, 0, 0);
    }
    IntPtr hwnd;
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); hwnd = Handle; }
    protected override void OnHandleDestroyed(EventArgs e) { hwnd = IntPtr.Zero; base.OnHandleDestroyed(e); }
    readonly object paintLock = new object();
    /// <summary>Draws the current time; called every few milliseconds from the paint thread.</summary>
    public void Tick() {
        IntPtr h = hwnd;
        if (h == IntPtr.Zero) return;
        lock (paintLock) { try { using (var g = Graphics.FromHwnd(h)) PaintNow(g); } catch { } }
    }
    /// <summary>Other programs may put themselves on top: come back to the front without taking the focus.</summary>
    public void StayOnTop() { if (hwnd != IntPtr.Zero) Native.SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }
}

public static class Program {
    static readonly long Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
    static string lang = "en";

    public static TimecodeValue Now(Stopwatch mono, int display) {
        long ticks;
        try { long ft; Native.GetSystemTimePreciseAsFileTime(out ft); ticks = ft + 504911232000000000L; } catch (EntryPointNotFoundException) { ticks = DateTime.UtcNow.Ticks; }
        var utc = new DateTime(ticks, DateTimeKind.Utc);
        return new TimecodeValue { UtcMs = (ticks - Epoch) / 10000, OffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(utc).TotalMinutes, MonoMs = mono.ElapsedMilliseconds, Display = display };
    }

    public static string Line2(TimecodeValue v, string label) {
        int o = Math.Abs(v.OffsetMinutes);
        string s = "UTC" + (v.OffsetMinutes < 0 ? "-" : "+") + (o / 60).ToString("00") + ":" + (o % 60).ToString("00")
            + "   " + (lang == "de" ? "monoton " : "monotonic ") + (v.MonoMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s"
            + "   " + (lang == "de" ? "Anzeige " : "display ") + v.Display;
        return string.IsNullOrEmpty(label) ? s : s + "   " + label;
    }

    static string Opt(string[] a, string name, string def) {
        for (int i = 0; i < a.Length; i++) {
            if (a[i] == "--" + name && i + 1 < a.Length) return a[i + 1];
            if (a[i].StartsWith("--" + name + "=")) return a[i].Substring(name.Length + 3);
        }
        return def;
    }
    static bool Flag(string[] a, string name) { return Array.IndexOf(a, "--" + name) >= 0; }

    [STAThread]
    public static int Main(string[] args) {
        lang = Opt(args, "lang", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en");
        int cell = int.Parse(Opt(args, "cell", "8"), CultureInfo.InvariantCulture);
        if (cell < 2 || cell > 40) cell = 8;

        string render = Opt(args, "render", null);
        if (render != null) {
            var v = new TimecodeValue {
                UtcMs = long.Parse(Opt(args, "at", ((DateTime.UtcNow.Ticks - Epoch) / 10000).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
                OffsetMinutes = int.Parse(Opt(args, "offset", ((int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
                MonoMs = long.Parse(Opt(args, "mono", "0"), CultureInfo.InvariantCulture), Display = 1 };
            var size = Timecode.StripSize(cell);
            using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb)) {
                using (var g = Graphics.FromImage(bmp)) Timecode.Draw(g, 0, 0, cell, v, Line2(v, Opt(args, "label", "")));
                bmp.Save(render, ImageFormat.Png);
            }
            return 0;
        }

        bool created;
        using (var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\eqlmaker-timestamp-stop", out created)) {
            if (Flag(args, "stop")) { stopEvent.Set(); Thread.Sleep(500); if (created) stopEvent.Reset(); return 0; }
            if (!created) { stopEvent.Reset(); }
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            string position = Opt(args, "position", "top-right"), disp = Opt(args, "display", "1");
            double opacity = double.Parse(Opt(args, "opacity", "1"), CultureInfo.InvariantCulture);
            int seconds = int.Parse(Opt(args, "seconds", "0"), CultureInfo.InvariantCulture);
            var mono = Stopwatch.StartNew();
            var strips = new List<Strip>(); var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++) {
                if (disp != "all" && disp != (i + 1).ToString(CultureInfo.InvariantCulture)) continue;
                strips.Add(new Strip(screens[i].Bounds, position, cell, i + 1, mono, Opt(args, "label", ""), opacity));
            }
            if (strips.Count == 0) { MessageBox.Show((lang == "de" ? "Anzeige nicht gefunden: " : "display not found: ") + disp, "EQLMaker"); return 2; }
            foreach (var s in strips) s.Show();
            Native.timeBeginPeriod(1);
            // a thread repaints every 2 ms; the screen shows the newest picture at its next refresh
            bool run = true;
            var painter = new Thread(delegate () { while (run) { foreach (var s in strips) s.Tick(); Thread.Sleep(2); } });
            painter.IsBackground = true; painter.Priority = ThreadPriority.AboveNormal; painter.Start();
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += delegate {
                if (stopEvent.WaitOne(0) || (seconds > 0 && mono.Elapsed.TotalSeconds >= seconds)) { timer.Stop(); run = false; painter.Join(500); foreach (var s in strips) s.Close(); Application.ExitThread(); return; }
                foreach (var s in strips) s.StayOnTop();
            };
            strips[0].FormClosed += delegate { timer.Stop(); run = false; foreach (var s in strips) if (!s.IsDisposed) s.Close(); Application.ExitThread(); };
            timer.Start();
            Application.Run();
            run = false;
            Native.timeEndPeriod(1);
        }
        return 0;
    }
}
}
