// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Time stamp strip: what eqlmaker-timestamp.exe shows and what video-import reads back from the
// frames of a screen recording. See docs/timestamp-overlay.md for the exact layout.
//
//   +----------------------------------+---------------------------------------------+
//   | [finder] data 16 x 7 cells [finder] |  2026-11-24 18:20:31.123 +0100   (7 segments) |
//   |  (white box, 34 x 9 cells)       |  UTC+01:00  mono 12.345 s  display 1  (text)  |
//   +----------------------------------+---------------------------------------------+
//
// The pattern carries 112 bits: version, UTC time in milliseconds, UTC offset, monotonic time,
// display number and a CRC-16. When the pattern cannot be read (too small, damaged), the reader
// falls back to the seven-segment digits, whose positions are known relative to the finders.
// This file has no dependency on the rest of the engine: the overlay program compiles it alone.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EqlMaker {

public class TimecodeValue {
    /// <summary>Wall clock: milliseconds since 1970-01-01 UTC.</summary>
    public long UtcMs;
    /// <summary>Offset of the local time shown from UTC, in minutes.</summary>
    public int OffsetMinutes;
    /// <summary>Milliseconds since the overlay started (never jumps); -1 when read from the digits.</summary>
    public long MonoMs = -1;
    /// <summary>Number of the display the strip was shown on (1-based).</summary>
    public int Display = 1;
    /// <summary>True when the value was read from the seven-segment digits instead of the pattern.</summary>
    public bool FromDigits;

    public DateTime LocalTime { get { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMilliseconds(UtcMs + OffsetMinutes * 60000L); } }
    /// <summary>Text of the digit line: "2026-11-24 18:20:31.123 +0100".</summary>
    public string Text {
        get {
            int o = Math.Abs(OffsetMinutes);
            return LocalTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + (OffsetMinutes < 0 ? "-" : "+") + (o / 60).ToString("00") + (o % 60).ToString("00");
        }
    }
}

public static class Timecode {
    public const int Version = 1;
    public const int Cols = 16, Rows = 7;          // data cells
    public const int PatternCells = 34;            // width of the white box in cells (quiet zone included)
    public const int HeightCells = 9;
    public const int TextChars = 29;               // "yyyy-MM-dd HH:mm:ss.fff +HHMM"
    public const double DigitOrigin = 35;          // cells from the left edge of the left finder to the first digit
    public const double DigitPitch = 2.5;          // cells from digit to digit
    public const int StripCells = 109;             // total width in cells
    public const int FinderDistance = 25;          // cells between the centres of the two finders

    public static Size StripSize(int cell) { return new Size(StripCells * cell, HeightCells * cell); }

    // ---------------------------------------------------------------- bits
    static ushort Crc16(byte[] data, int n) {
        ushort crc = 0xFFFF;
        for (int i = 0; i < n; i++) {
            crc ^= (ushort)(data[i] << 8);
            for (int b = 0; b < 8; b++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
    static void Put(byte[] d, ref int pos, long value, int bits) {
        for (int i = bits - 1; i >= 0; i--) { if (((value >> i) & 1) != 0) d[pos >> 3] |= (byte)(0x80 >> (pos & 7)); pos++; }
    }
    static long Get(byte[] d, ref int pos, int bits) {
        long v = 0;
        for (int i = 0; i < bits; i++) { v = (v << 1) | (uint)((d[pos >> 3] >> (7 - (pos & 7))) & 1); pos++; }
        return v;
    }

    /// <summary>112 cells, row by row; true = dark. A checkerboard mask keeps large areas from being one colour.</summary>
    public static bool[] Encode(TimecodeValue v) {
        var d = new byte[14]; int pos = 0;
        Put(d, ref pos, Version, 4);
        Put(d, ref pos, v.UtcMs, 44);
        Put(d, ref pos, v.OffsetMinutes + 1024, 11);
        Put(d, ref pos, v.MonoMs < 0 ? 0 : v.MonoMs & 0xFFFFFFFFL, 32);
        Put(d, ref pos, v.Display & 7, 3);
        Put(d, ref pos, 0, 2);
        Put(d, ref pos, Crc16(d, 12), 16);
        var bits = new bool[Cols * Rows];
        for (int i = 0; i < bits.Length; i++) {
            bool bit = (d[i >> 3] & (0x80 >> (i & 7))) != 0;
            bits[i] = bit ^ ((((i / Cols) + (i % Cols)) & 1) == 0);
        }
        return bits;
    }

    /// <summary>Null when the checksum does not match.</summary>
    public static TimecodeValue Decode(bool[] bits) {
        var d = new byte[14];
        for (int i = 0; i < bits.Length; i++) {
            bool bit = bits[i] ^ ((((i / Cols) + (i % Cols)) & 1) == 0);
            if (bit) d[i >> 3] |= (byte)(0x80 >> (i & 7));
        }
        int pos = 96;
        if ((ushort)Get(d, ref pos, 16) != Crc16(d, 12)) return null;
        pos = 0;
        if (Get(d, ref pos, 4) != Version) return null;
        var v = new TimecodeValue();
        v.UtcMs = Get(d, ref pos, 44); v.OffsetMinutes = (int)Get(d, ref pos, 11) - 1024; v.MonoMs = Get(d, ref pos, 32); v.Display = (int)Get(d, ref pos, 3);
        return v;
    }

    // ---------------------------------------------------------------- seven segments
    // segments a b c d e f g as bits 6..0
    static readonly int[] Seg = { 0x7E, 0x30, 0x6D, 0x79, 0x33, 0x5B, 0x5F, 0x70, 0x7F, 0x7B };
    // sample point of each segment inside a digit box of 4 x 7 units (unit = half a cell)
    static readonly double[,] SegPoint = { { 2, 0.5 }, { 3.5, 2 }, { 3.5, 5 }, { 2, 6.5 }, { 0.5, 5 }, { 0.5, 2 }, { 2, 3.5 } };
    static readonly int[] DigitIndex = { 0, 1, 2, 3, 5, 6, 8, 9, 11, 12, 14, 15, 17, 18, 20, 21, 22, 25, 26, 27, 28 };
    const int SignIndex = 24;

    static void DrawGlyph(Graphics g, Brush on, float x, float y, float u, char ch) {
        if (ch >= '0' && ch <= '9') {
            int s = Seg[ch - '0'];
            if ((s & 0x40) != 0) g.FillRectangle(on, x, y, 4 * u, u);
            if ((s & 0x20) != 0) g.FillRectangle(on, x + 3 * u, y, u, 4 * u);
            if ((s & 0x10) != 0) g.FillRectangle(on, x + 3 * u, y + 3 * u, u, 4 * u);
            if ((s & 0x08) != 0) g.FillRectangle(on, x, y + 6 * u, 4 * u, u);
            if ((s & 0x04) != 0) g.FillRectangle(on, x, y + 3 * u, u, 4 * u);
            if ((s & 0x02) != 0) g.FillRectangle(on, x, y, u, 4 * u);
            if ((s & 0x01) != 0) g.FillRectangle(on, x, y + 3 * u, 4 * u, u);
        } else if (ch == '-') g.FillRectangle(on, x, y + 3 * u, 4 * u, u);
        else if (ch == '+') { g.FillRectangle(on, x, y + 3 * u, 4 * u, u); g.FillRectangle(on, x + 1.5f * u, y + 1.5f * u, u, 4 * u); }
        else if (ch == ':') { g.FillRectangle(on, x + 1.5f * u, y + 2 * u, u, u); g.FillRectangle(on, x + 1.5f * u, y + 4.5f * u, u, u); }
        else if (ch == '.') g.FillRectangle(on, x + 1.5f * u, y + 6 * u, u, u);
    }

    /// <summary>Draws the strip with its top-left corner at x, y. line2 is free text for people (may be null).</summary>
    public static void Draw(Graphics g, int x, int y, int cell, TimecodeValue v, string line2) {
        var size = StripSize(cell);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.FillRectangle(Brushes.Black, x, y, size.Width, size.Height);
        g.FillRectangle(Brushes.White, x, y, PatternCells * cell, size.Height);
        int fx = x + cell, fy = y + cell;
        foreach (int ox in new[] { 0, 7 + 1 + Cols + 1 }) {
            int px = fx + ox * cell;
            g.FillRectangle(Brushes.Black, px, fy, 7 * cell, 7 * cell);
            g.FillRectangle(Brushes.White, px + cell, fy + cell, 5 * cell, 5 * cell);
            g.FillRectangle(Brushes.Black, px + 2 * cell, fy + 2 * cell, 3 * cell, 3 * cell);
        }
        var bits = Encode(v);
        for (int i = 0; i < bits.Length; i++)
            if (bits[i]) g.FillRectangle(Brushes.Black, fx + (8 + i % Cols) * cell, fy + (i / Cols) * cell, cell, cell);
        string text = v.Text; float u = cell / 2f;
        for (int i = 0; i < text.Length; i++) DrawGlyph(g, Brushes.White, fx + (float)(DigitOrigin + i * DigitPitch) * cell, fy, u, text[i]);
        if (!string.IsNullOrEmpty(line2)) {
            using (var f = new Font("Segoe UI", Math.Max(6f, cell * 2.1f), FontStyle.Regular, GraphicsUnit.Pixel)) {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.DrawString(line2, f, Brushes.White, fx + (float)DigitOrigin * cell, fy + 4.4f * cell);
            }
        }
    }

    // ---------------------------------------------------------------- reading
    /// <summary>Grey values (0..255) of a picture, row by row.</summary>
    public static byte[] Luma(Bitmap bmp, out int w, out int h) {
        w = bmp.Width; h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try {
            var row = new byte[data.Stride]; var luma = new byte[w * h];
            for (int y = 0; y < h; y++) {
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, data.Stride);
                int o = y * w;
                for (int x = 0, i = 0; x < w; x++, i += 3) luma[o + x] = (byte)((row[i] * 29 + row[i + 1] * 150 + row[i + 2] * 77) >> 8);
            }
            return luma;
        } finally { bmp.UnlockBits(data); }
    }

    class Finder { public double X, Y, Mx, My; public int N; }

    static bool Ratio(int[] r, out double m) {
        int total = r[0] + r[1] + r[2] + r[3] + r[4];
        m = total / 7.0;
        if (total < 10) return false;
        double t = m * 0.7 + 0.5;
        return Math.Abs(r[0] - m) <= t && Math.Abs(r[1] - m) <= t && Math.Abs(r[2] - 3 * m) <= 2 * t && Math.Abs(r[3] - m) <= t && Math.Abs(r[4] - m) <= t;
    }

    static bool Vertical(byte[] l, int w, int h, int x, int y, int thr, out double cy, out double my) {
        cy = 0; my = 0;
        var r = new int[5];
        int up = y; while (up > 0 && l[(up - 1) * w + x] < thr) up--;
        int dn = y; while (dn < h - 1 && l[(dn + 1) * w + x] < thr) dn++;
        r[2] = dn - up + 1;
        int a = up - 1, s = a; while (a >= 0 && l[a * w + x] >= thr && s - a < r[2]) a--; r[1] = s - a;
        s = a; while (a >= 0 && l[a * w + x] < thr && s - a < r[2]) a--; r[0] = s - a;
        int b = dn + 1; s = b; while (b < h && l[b * w + x] >= thr && b - s < r[2]) b++; r[3] = b - s;
        s = b; while (b < h && l[b * w + x] < thr && b - s < r[2]) b++; r[4] = b - s;
        if (!Ratio(r, out my)) return false;
        cy = (up + dn + 1) / 2.0;
        return true;
    }

    static List<Finder> FindFinders(byte[] l, int w, int h, Rectangle roi, int thr) {
        var list = new List<Finder>();
        var run = new int[5]; var start = new int[5];
        int x1 = Math.Min(w, roi.Right), y1 = Math.Min(h, roi.Bottom);
        for (int y = Math.Max(0, roi.Top); y < y1; y++) {
            int n = 0, o = y * w, x0 = Math.Max(0, roi.Left);
            bool dark = l[o + x0] < thr; int len = 0, st = x0;
            for (int x = x0; x <= x1; x++) {
                bool d = x < x1 ? l[o + x] < thr : !dark;
                if (d == dark) { len++; continue; }
                // a run ended: keep the last five, the newest last
                if (n < 5) { run[n] = len; start[n] = st; n++; }
                else { for (int i = 0; i < 4; i++) { run[i] = run[i + 1]; start[i] = start[i + 1]; } run[4] = len; start[4] = st; }
                if (n == 5 && dark) {
                    double m;
                    if (Ratio(run, out m)) {
                        double cx = start[2] + run[2] / 2.0, cy, my;
                        if (Vertical(l, w, h, (int)cx, y, thr, out cy, out my) && my > m * 0.5 && my < m * 2) {
                            Finder f = null;
                            foreach (var c in list) if (Math.Abs(c.X - cx) < m * 2 && Math.Abs(c.Y - cy) < my * 2) { f = c; break; }
                            if (f == null) list.Add(new Finder { X = cx, Y = cy, Mx = m, My = my, N = 1 });
                            else { f.X = (f.X * f.N + cx) / (f.N + 1); f.Y = (f.Y * f.N + cy) / (f.N + 1); f.Mx = (f.Mx * f.N + m) / (f.N + 1); f.My = (f.My * f.N + my) / (f.N + 1); f.N++; }
                        }
                    }
                }
                dark = d; len = 1; st = x;
            }
        }
        return list;
    }

    static int Sample(byte[] l, int w, int h, double x, double y, double m) {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        int r = m >= 5 ? 1 : 0, sum = 0, n = 0;
        for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) {
            int px = ix + dx, py = iy + dy;
            if (px < 0 || py < 0 || px >= w || py >= h) continue;
            sum += l[py * w + px]; n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    static TimecodeValue ReadDigits(byte[] l, int w, int h, double x0, double y0, double mx, double my, int thr) {
        var digits = new int[DigitIndex.Length];
        for (int k = 0; k < DigitIndex.Length; k++) {
            double bx = x0 + (DigitOrigin + DigitIndex[k] * DigitPitch) * mx; int s = 0;
            for (int i = 0; i < 7; i++) {
                s <<= 1;
                if (Sample(l, w, h, bx + SegPoint[i, 0] * mx / 2, y0 + SegPoint[i, 1] * my / 2, Math.Min(mx, my) / 2) >= thr) s |= 1;
            }
            int dgt = Array.IndexOf(Seg, s);
            if (dgt < 0) return null;
            digits[k] = dgt;
        }
        double sx = x0 + (DigitOrigin + SignIndex * DigitPitch) * mx;
        bool bar = Sample(l, w, h, sx + 2 * mx / 2, y0 + 3.5 * my / 2, Math.Min(mx, my) / 2) >= thr;
        bool plus = Sample(l, w, h, sx + 2 * mx / 2, y0 + 2 * my / 2, Math.Min(mx, my) / 2) >= thr;
        if (!bar) return null;
        int year = digits[0] * 1000 + digits[1] * 100 + digits[2] * 10 + digits[3], month = digits[4] * 10 + digits[5], day = digits[6] * 10 + digits[7];
        int hh = digits[8] * 10 + digits[9], mi = digits[10] * 10 + digits[11], ss = digits[12] * 10 + digits[13], ms = digits[14] * 100 + digits[15] * 10 + digits[16];
        int oh = digits[17] * 10 + digits[18], om = digits[19] * 10 + digits[20];
        if (year < 2000 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hh > 23 || mi > 59 || ss > 59 || oh > 14 || om > 59) return null;
        int off = (oh * 60 + om) * (plus ? 1 : -1);
        var local = new DateTime(year, month, day, hh, mi, ss, ms, DateTimeKind.Unspecified);
        long localMs = (long)(local - new DateTime(1970, 1, 1)).TotalMilliseconds;
        return new TimecodeValue { UtcMs = localMs - off * 60000L, OffsetMinutes = off, MonoMs = -1, Display = 0, FromDigits = true };
    }

    /// <summary>Reads the strip from a picture. hint: where the strip was found last time (empty = unknown);
    /// it is updated. Returns null when no strip can be read.</summary>
    public static TimecodeValue Read(byte[] luma, int w, int h, ref Rectangle hint) {
        TimecodeValue v = null;
        if (!hint.IsEmpty) {
            var r = Rectangle.Inflate(hint, hint.Height, hint.Height); r.Intersect(new Rectangle(0, 0, w, h));
            v = ReadIn(luma, w, h, r, ref hint);
            if (v != null && !v.FromDigits) return v;
        }
        var full = new Rectangle(0, 0, w, h); var keep = hint;
        var v2 = ReadIn(luma, w, h, full, ref hint);
        if (v2 != null && (v == null || !v2.FromDigits)) return v2;
        hint = keep;
        return v;
    }
    public static TimecodeValue Read(Bitmap frame, ref Rectangle hint) { int w, h; var l = Luma(frame, out w, out h); return Read(l, w, h, ref hint); }

    static TimecodeValue ReadIn(byte[] l, int w, int h, Rectangle roi, ref Rectangle hint) {
        TimecodeValue fallback = null; Rectangle fbRect = Rectangle.Empty;
        foreach (int thr in new[] { 128, 96, 160 }) {
            var f = FindFinders(l, w, h, roi, thr);
            for (int a = 0; a < f.Count; a++) for (int b = 0; b < f.Count; b++) {
                var L = f[a]; var R = f[b];
                if (a == b || R.X <= L.X || L.N < 2 || R.N < 2) continue;
                double my = (L.My + R.My) / 2, mx = (R.X - L.X) / FinderDistance;
                if (Math.Abs(L.Y - R.Y) > my || mx < 1.5 || mx / my < 0.6 || mx / my > 1.7) continue;
                double avg = (L.Mx + R.Mx) / 2;
                if (mx / avg < 0.75 || mx / avg > 1.35) continue;
                double cy = (L.Y + R.Y) / 2, x0 = L.X - 3.5 * mx, y0 = cy - 3.5 * my;
                int black = (Sample(l, w, h, L.X, L.Y, mx) + Sample(l, w, h, R.X, R.Y, mx)) / 2;
                int white = (Sample(l, w, h, L.X - 2 * mx, L.Y, mx) + Sample(l, w, h, R.X + 2 * mx, R.Y, mx)) / 2;
                if (white - black < 40) continue;
                int t = (black + white) / 2;
                var rect = new Rectangle((int)(x0 - mx), (int)(y0 - my), (int)(StripCells * mx), (int)(HeightCells * my));
                foreach (double dy in new[] { 0, -0.2, 0.2 }) foreach (double dx in new[] { 0, -0.2, 0.2 }) {
                    var bits = new bool[Cols * Rows];
                    for (int i = 0; i < bits.Length; i++)
                        bits[i] = Sample(l, w, h, x0 + (8 + i % Cols + 0.5 + dx) * mx, y0 + (i / Cols + 0.5 + dy) * my, Math.Min(mx, my)) < t;
                    var v = Decode(bits);
                    if (v != null) { hint = rect; return v; }
                }
                if (fallback == null) {
                    foreach (double dy in new[] { 0, -0.15, 0.15 }) {
                        fallback = ReadDigits(l, w, h, x0, y0 + dy * my, mx, my, t);
                        if (fallback != null) { fbRect = rect; break; }
                    }
                }
            }
            if (fallback != null) break;
        }
        if (fallback != null) hint = fbRect;
        return fallback;
    }
}
}
