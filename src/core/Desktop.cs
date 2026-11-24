// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Windows desktop: windows, mouse, keyboard, screenshots.
// Two ways to operate a program:
//   real        mouse and keyboard input through SendInput, pictures from the screen. Needs a
//               visible, unlocked desktop; the person at the machine sees (and shares) the pointer.
//   background  window messages to the target window, pictures through PrintWindow. Works without a
//               visible desktop and without taking mouse or keyboard; limited to programs that
//               react to posted messages (classic Win32, Windows Forms and similar toolkits).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EqlMaker {

static class W {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public int type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public int dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct STARTUPINFO {
        public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, StringBuilder l, int flags, int timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT p, int flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int action, int param, ref RECT r, int win);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out ICONINFO ii);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, int flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, int access, IntPtr sa);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, byte[] bmi, int usage, out IntPtr bits, IntPtr section, int offset);
    [DllImport("kernel32.dll")] public static extern void GetSystemTimePreciseAsFileTime(out long fileTime);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out int code);
    [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] public static extern bool SetHandleInformation(IntPtr h, int mask, int flags);
}

/// <summary>Wall clock in microseconds since 1970-01-01 UTC.</summary>
public static class Clock {
    static readonly long Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
    public static long NowUs() {
        try { long ft; W.GetSystemTimePreciseAsFileTime(out ft); return (ft + 504911232000000000L - Epoch) / 10; }
        catch (EntryPointNotFoundException) { return (DateTime.UtcNow.Ticks - Epoch) / 10; }
    }
    public static long ToUs(DateTime utc) { return (utc.Ticks - Epoch) / 10; }
    public static DateTime FromUs(long us) { return new DateTime(Epoch + us * 10, DateTimeKind.Utc); }
    /// <summary>ISO 8601 in UTC with microseconds.</summary>
    public static string Iso(long us) { return FromUs(us).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture); }
}

public class Win {
    public IntPtr Handle; public string Title; public int Pid; public string Process;
    public int X, Y, W, H;
    public override string ToString() { return string.Format("{0} | {1} | pid {2} | {3},{4} {5}x{6} | hwnd {7}", Title, Process, Pid, X, Y, W, H, Handle.ToInt64()); }
}

/// <summary>A control (child window) of a top-level window.</summary>
public class Child {
    public IntPtr Handle; public string Text, Class; public Rectangle Bounds; public bool Enabled;
    public JO ToJson(Win top) { return new JO("text", Text, "class", Class, "x", Bounds.X - top.X, "y", Bounds.Y - top.Y, "w", Bounds.Width, "h", Bounds.Height, "enabled", Enabled); }
}

public static class Desktop {
    static bool dpi;
    public static void Init() { if (!dpi) { W.SetProcessDPIAware(); dpi = true; } }

    public static List<Win> Windows() {
        Init();
        var list = new List<Win>();
        W.EnumWindows(delegate (IntPtr h, IntPtr l) {
            if (!W.IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512); W.GetWindowText(h, sb, 512);
            if (sb.Length == 0) return true;
            W.RECT r; W.GetWindowRect(h, out r);
            uint pid; W.GetWindowThreadProcessId(h, out pid);
            string pn = "";
            try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            list.Add(new Win { Handle = h, Title = sb.ToString(), Pid = (int)pid, Process = pn, X = r.Left, Y = r.Top, W = r.Right - r.Left, H = r.Bottom - r.Top });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Title pattern with * and ? (case-insensitive), or "pid:&lt;n&gt;" / "process:&lt;name&gt;".</summary>
    public static Win Find(string pattern) {
        if (string.IsNullOrEmpty(pattern)) return null;
        foreach (var w in Windows()) {
            if (pattern.StartsWith("pid:")) { if (w.Pid.ToString() == pattern.Substring(4)) return w; continue; }
            if (pattern.StartsWith("process:")) { if (string.Equals(w.Process, pattern.Substring(8), StringComparison.OrdinalIgnoreCase)) return w; continue; }
            string rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            if (Regex.IsMatch(w.Title, rx, RegexOptions.IgnoreCase)) return w;
        }
        return null;
    }

    public static Win WaitWindow(string pattern, int timeoutMs) {
        var sw = Stopwatch.StartNew();
        while (true) {
            var w = Find(pattern);
            if (w != null) return w;
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("window not found: " + pattern);
            Thread.Sleep(200);
        }
    }

    public static Win Refresh(Win w) {
        W.RECT r; W.GetWindowRect(w.Handle, out r);
        w.X = r.Left; w.Y = r.Top; w.W = r.Right - r.Left; w.H = r.Bottom - r.Top;
        var sb = new StringBuilder(512); W.GetWindowText(w.Handle, sb, 512);
        if (sb.Length > 0) w.Title = sb.ToString();
        return w;
    }

    public static void Focus(Win w, bool maximize) {
        if (W.IsIconic(w.Handle)) W.ShowWindow(w.Handle, 9);
        if (maximize) W.ShowWindow(w.Handle, 3);
        // an Alt key press lets this process take the foreground
        W.keybd_event(0x12, 0, 0, IntPtr.Zero); W.keybd_event(0x12, 0, 2, IntPtr.Zero);
        W.SetForegroundWindow(w.Handle);
        Thread.Sleep(300);
        Refresh(w);
    }

    /// <summary>False when Windows draws no desktop for this session (locked, disconnected, minimized
    /// remote desktop window, or a separate background desktop): screen pictures are black and real
    /// input goes nowhere.</summary>
    public static bool Visible() {
        Init();
        IntPtr screen = W.GetDC(IntPtr.Zero), mem = W.CreateCompatibleDC(screen), bits;
        var bmi = new byte[40];
        BitConverter.GetBytes(40).CopyTo(bmi, 0); BitConverter.GetBytes(1).CopyTo(bmi, 4); BitConverter.GetBytes(1).CopyTo(bmi, 8);
        BitConverter.GetBytes((short)1).CopyTo(bmi, 12); BitConverter.GetBytes((short)32).CopyTo(bmi, 14);
        IntPtr dib = W.CreateDIBSection(screen, bmi, 0, out bits, IntPtr.Zero, 0), old = W.SelectObject(mem, dib);
        bool ok = W.BitBlt(mem, 0, 0, 1, 1, screen, 0, 0, 0x00CC0020);
        W.SelectObject(mem, old); W.DeleteObject(dib); W.DeleteDC(mem); W.ReleaseDC(IntPtr.Zero, screen);
        return ok;
    }
    public const string NotVisible = "the desktop of this Windows session is not visible (locked, disconnected or minimized remote desktop window): Windows draws nothing, so screen pictures would be black and mouse and keyboard input would go nowhere. Keep the session connected and unlocked, or use the background mode (\"input\": \"background\").";
    public static void RequireVisible() { if (!Visible()) throw new Exception(NotVisible); }

    public static Rectangle Screen() { Init(); return new Rectangle(0, 0, W.GetSystemMetrics(0), W.GetSystemMetrics(1)); }
    public static Rectangle WorkArea() { Init(); var r = new W.RECT(); W.SystemParametersInfo(0x30, 0, ref r, 0); return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); }

    public static Point Cursor() { W.POINT p; W.GetCursorPos(out p); return new Point(p.X, p.Y); }

    // ------------------------------------------------------------ real input
    /// <summary>Smooth movement with ease-in-out, so a viewer can follow the pointer.</summary>
    public static void Move(int x, int y, int ms) {
        Init();
        var p = Cursor();
        int steps = Math.Max(1, ms / 15);
        for (int i = 1; i <= steps; i++) {
            double t = (double)i / steps, e = 0.5 - Math.Cos(t * Math.PI) / 2;
            W.SetCursorPos((int)Math.Round(p.X + (x - p.X) * e), (int)Math.Round(p.Y + (y - p.Y) * e));
            Thread.Sleep(15);
        }
        W.SetCursorPos(x, y);
    }

    static void Mouse(int flags, int data) {
        var i = new W.INPUT[1]; i[0].type = 0; i[0].u.mi.dwFlags = flags; i[0].u.mi.mouseData = data;
        W.SendInput(1, i, Marshal.SizeOf(typeof(W.INPUT)));
    }
    public static void Click(string button, bool dbl) {
        int down = 2, up = 4;
        if (button == "right") { down = 8; up = 0x10; } else if (button == "middle") { down = 0x20; up = 0x40; }
        for (int n = 0; n < (dbl ? 2 : 1); n++) { Mouse(down, 0); Thread.Sleep(60); Mouse(up, 0); if (dbl && n == 0) Thread.Sleep(90); }
    }
    public static void Scroll(int notches) { Mouse(0x800, notches * 120); }

    /// <summary>Text as Unicode key events.</summary>
    public static void Type(string text, int delayMs) {
        foreach (char c in text) {
            if (c == '\n') { System.Windows.Forms.SendKeys.SendWait("{ENTER}"); Thread.Sleep(delayMs); continue; }
            if (c == '\r') continue;
            var i = new W.INPUT[2];
            i[0].type = 1; i[0].u.ki.wScan = c; i[0].u.ki.dwFlags = 4;
            i[1].type = 1; i[1].u.ki.wScan = c; i[1].u.ki.dwFlags = 4 | 2;
            W.SendInput(2, i, Marshal.SizeOf(typeof(W.INPUT)));
            Thread.Sleep(delayMs);
        }
    }
    /// <summary>SendKeys syntax: {ENTER}, {F5}, ^a (Ctrl+A), %{F4} (Alt+F4), +{TAB} (Shift+Tab).</summary>
    public static void Keys(string keys) { System.Windows.Forms.SendKeys.SendWait(keys); }

    public static int Launch(string file, string args, string cwd) {
        var psi = new ProcessStartInfo(file, args ?? "");
        psi.UseShellExecute = false;   // the child stays on the desktop of this process
        psi.WorkingDirectory = string.IsNullOrEmpty(cwd) ? Path.GetDirectoryName(Path.GetFullPath(file)) : cwd;
        using (var p = System.Diagnostics.Process.Start(psi)) return p == null ? 0 : p.Id;
    }

    // ------------------------------------------------------------ background input (window messages)
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203,
        WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MOUSEWHEEL = 0x20A;
    static IntPtr lastTarget = IntPtr.Zero;

    /// <summary>Deepest visible child window at a screen point inside top-level window w.</summary>
    public static IntPtr ChildAt(IntPtr top, int screenX, int screenY) {
        IntPtr cur = top;
        for (int depth = 0; depth < 32; depth++) {
            var p = new W.POINT { X = screenX, Y = screenY };
            W.ScreenToClient(cur, ref p);
            IntPtr child = W.ChildWindowFromPointEx(cur, p, 1 | 2);   // skip invisible and disabled
            if (child == IntPtr.Zero || child == cur) break;
            cur = child;
        }
        return cur;
    }
    static IntPtr LParam(IntPtr h, int screenX, int screenY) {
        var p = new W.POINT { X = screenX, Y = screenY }; W.ScreenToClient(h, ref p);
        return (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
    }
    /// <summary>Waits until the thread of the window has worked off the messages posted so far.</summary>
    static void Settle(IntPtr h) { IntPtr r; W.SendMessageTimeout(h, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, 2 /* abort if hung */, 3000, out r); }

    public static IntPtr BackgroundClick(Win w, int screenX, int screenY, string button, bool dbl) {
        IntPtr h = ChildAt(w.Handle, screenX, screenY);
        IntPtr lp = LParam(h, screenX, screenY);
        bool right = button == "right";
        W.PostMessage(h, WM_MOUSEMOVE, IntPtr.Zero, lp);
        W.PostMessage(h, right ? WM_RBUTTONDOWN : WM_LBUTTONDOWN, (IntPtr)(right ? 2 : 1), lp);
        W.PostMessage(h, right ? WM_RBUTTONUP : WM_LBUTTONUP, IntPtr.Zero, lp);
        if (dbl && !right) { W.PostMessage(h, WM_LBUTTONDBLCLK, (IntPtr)1, lp); W.PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp); }
        lastTarget = h;
        Thread.Sleep(30); Settle(h);
        return h;
    }

    /// <summary>The control that has the keyboard focus in the thread of window w (else the control clicked last).</summary>
    public static IntPtr FocusOf(Win w) {
        uint pid; uint thread = W.GetWindowThreadProcessId(w.Handle, out pid);
        var gi = new W.GUITHREADINFO(); gi.cbSize = Marshal.SizeOf(typeof(W.GUITHREADINFO));
        if (W.GetGUIThreadInfo(thread, ref gi) && gi.hwndFocus != IntPtr.Zero) return gi.hwndFocus;
        if (lastTarget != IntPtr.Zero && W.IsWindow(lastTarget)) return lastTarget;
        return w.Handle;
    }

    public static void BackgroundType(Win w, string text, int delayMs) {
        IntPtr h = FocusOf(w);
        foreach (char c in text) {
            if (c == '\r') continue;
            W.PostMessage(h, WM_CHAR, (IntPtr)(c == '\n' ? '\r' : c), (IntPtr)1);
            if (delayMs > 0) Thread.Sleep(delayMs);
        }
        Settle(h);
    }

    static readonly Dictionary<string, int> VKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
        { "ENTER", 0x0D }, { "TAB", 0x09 }, { "ESC", 0x1B }, { "ESCAPE", 0x1B }, { "DEL", 0x2E }, { "DELETE", 0x2E }, { "BACKSPACE", 0x08 }, { "BS", 0x08 },
        { "UP", 0x26 }, { "DOWN", 0x28 }, { "LEFT", 0x25 }, { "RIGHT", 0x27 }, { "HOME", 0x24 }, { "END", 0x23 }, { "PGUP", 0x21 }, { "PGDN", 0x22 }, { "INSERT", 0x2D },
        { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 }, { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B } };

    /// <summary>Keys in SendKeys syntax, posted as messages. Named keys ({ENTER}, {TAB}, {F5}, ...) and plain
    /// characters work; combinations with Ctrl, Alt or Shift (^ % +) are not possible with posted
    /// messages and are refused.</summary>
    public static void BackgroundKeys(Win w, string keys) {
        IntPtr h = FocusOf(w);
        int i = 0;
        while (i < keys.Length) {
            char c = keys[i];
            if (c == '^' || c == '%' || c == '+') throw new Exception("key combinations (^ % +) are not available in background mode: " + keys);
            if (c == '{') {
                int e = keys.IndexOf('}', i + 1);
                if (e < 0) throw new Exception("keys: '}' missing in " + keys);
                string name = keys.Substring(i + 1, e - i - 1); int vk;
                if (!VKeys.TryGetValue(name, out vk)) throw new Exception("keys: unknown key {" + name + "}");
                W.PostMessage(h, WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
                W.PostMessage(h, WM_KEYUP, (IntPtr)vk, unchecked((IntPtr)(long)0xC0000001));
                i = e + 1;
                Thread.Sleep(30); Settle(h);
                h = FocusOf(w);   // Tab and Enter may have moved the focus
            } else { W.PostMessage(h, WM_CHAR, (IntPtr)c, (IntPtr)1); i++; }
        }
        Settle(h);
    }

    public static void BackgroundScroll(Win w, int screenX, int screenY, int notches) {
        IntPtr h = ChildAt(w.Handle, screenX, screenY);
        W.PostMessage(h, WM_MOUSEWHEEL, (IntPtr)((notches * 120) << 16), (IntPtr)(((screenY & 0xFFFF) << 16) | (screenX & 0xFFFF)));
        Settle(h);
    }

    // ------------------------------------------------------------ controls (child windows)
    static bool Like(string value, string pattern) {
        if (pattern == null) return true;
        return Regex.IsMatch(value ?? "", "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
    public static string TextOf(IntPtr h) {
        var sb = new StringBuilder(2048); IntPtr r;
        W.SendMessageTimeout(h, 0x0D /* WM_GETTEXT */, (IntPtr)sb.Capacity, sb, 2, 1000, out r);
        return sb.ToString();
    }

    /// <summary>The visible controls of a window with text, window class and position: a simple view of
    /// the structure of a mask for programs built from standard controls.</summary>
    public static List<Child> Children(Win w) {
        var list = new List<Child>();
        W.EnumChildWindows(w.Handle, delegate (IntPtr h, IntPtr l) {
            if (!W.IsWindowVisible(h)) return true;
            var cls = new StringBuilder(256); W.GetClassName(h, cls, 256);
            W.RECT r; W.GetWindowRect(h, out r);
            list.Add(new Child { Handle = h, Text = TextOf(h), Class = cls.ToString(), Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), Enabled = W.IsWindowEnabled(h) });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Finds a control by text and/or window class (* and ? wildcards); index picks the n-th match.</summary>
    public static Child FindChild(Win w, string text, string cls, int index, int timeoutMs) {
        var sw = Stopwatch.StartNew();
        while (true) {
            int n = 0;
            foreach (var c in Children(w)) if (Like(c.Text, text) && Like(c.Class, cls) && n++ == index) return c;
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("control not found: text=" + text + " class=" + cls + " index=" + index);
            Thread.Sleep(150);
        }
    }

    /// <summary>Selects the whole content of an edit control (background mode: typing then replaces it).</summary>
    public static void SelectAll(IntPtr edit) { W.PostMessage(edit, 0xB1 /* EM_SETSEL */, IntPtr.Zero, (IntPtr)(-1)); Settle(edit); }

    // ------------------------------------------------------------ pictures
    /// <summary>PNG of a screen rectangle with the mouse pointer.</summary>
    public static byte[] Png(Rectangle r, int maxWidth) {
        Init();
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb)) {
            using (var g = Graphics.FromImage(bmp)) {
                g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
                IntPtr dc = g.GetHdc(); try { ScreenRecorder.DrawCursor(dc, r.X, r.Y); } finally { g.ReleaseHdc(dc); }
            }
            return Encode(bmp, maxWidth);
        }
    }

    /// <summary>Picture of a window drawn by the window itself (PrintWindow); works when the window is
    /// covered or on a desktop that is not shown.</summary>
    public static Bitmap WindowBitmap(Win w) {
        Init(); Refresh(w);
        var bmp = new Bitmap(Math.Max(1, w.W), Math.Max(1, w.H), PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr dc = g.GetHdc();
            try { if (!W.PrintWindow(w.Handle, dc, 2)) W.PrintWindow(w.Handle, dc, 0); } finally { g.ReleaseHdc(dc); }
        }
        return bmp;
    }
    public static byte[] PngWindow(Win w, int maxWidth) { using (var bmp = WindowBitmap(w)) return Encode(bmp, maxWidth); }

    static byte[] Encode(Bitmap bmp, int maxWidth) {
        Bitmap outBmp = bmp;
        if (maxWidth > 0 && bmp.Width > maxWidth) outBmp = new Bitmap(bmp, maxWidth, (int)Math.Round((double)bmp.Height * maxWidth / bmp.Width));
        using (var ms = new MemoryStream()) {
            outBmp.Save(ms, ImageFormat.Png);
            if (outBmp != bmp) outBmp.Dispose();
            return ms.ToArray();
        }
    }

    // ------------------------------------------------------------ separate background desktop
    /// <summary>Starts a command on a separate desktop of this window station that is never shown. Programs
    /// started there (and their children) open their windows on it; together with the background mode
    /// a recording runs without disturbing the person at the machine. Returns the exit code.</summary>
    public static int RunOnBackgroundDesktop(string desktopName, string commandLine, string cwd, int timeoutMs) {
        IntPtr desk = W.CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /* GENERIC_ALL */, IntPtr.Zero);
        if (desk == IntPtr.Zero) throw new Exception("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        try {
            var si = new W.STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(W.STARTUPINFO)); si.lpDesktop = desktopName;
            si.dwFlags = 0x100;   // STARTF_USESTDHANDLES: the child writes to the console/pipes of this process
            si.hStdInput = W.GetStdHandle(-10); si.hStdOutput = W.GetStdHandle(-11); si.hStdError = W.GetStdHandle(-12);
            foreach (var h in new[] { si.hStdInput, si.hStdOutput, si.hStdError }) if (h != IntPtr.Zero && h != (IntPtr)(-1)) W.SetHandleInformation(h, 1, 1);
            W.PROCESS_INFORMATION pi;
            if (!W.CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, string.IsNullOrEmpty(cwd) ? null : cwd, ref si, out pi))
                throw new Exception("cannot start: " + commandLine + " (error " + Marshal.GetLastWin32Error() + ")");
            try {
                if (W.WaitForSingleObject(pi.hProcess, timeoutMs <= 0 ? 0xFFFFFFFF : (uint)timeoutMs) != 0) {
                    try { System.Diagnostics.Process.GetProcessById(pi.dwProcessId).Kill(); } catch { }
                    throw new Exception("timeout after " + timeoutMs + " ms: " + commandLine);
                }
                int code; W.GetExitCodeProcess(pi.hProcess, out code);
                return code;
            } finally { W.CloseHandle(pi.hThread); W.CloseHandle(pi.hProcess); }
        } finally { W.CloseDesktop(desk); }
    }
}
}
