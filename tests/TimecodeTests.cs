// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace EqlMaker.Tests {

public static class TimecodeTests {
    public static TimecodeValue Sample() { return new TimecodeValue { UtcMs = 1795630831123L, OffsetMinutes = 60, MonoMs = 4567890, Display = 2 }; }

    /// <summary>A "desktop" picture with busy content and the strip at the given place.</summary>
    public static Bitmap Frame(int w, int h, int x, int y, int cell, TimecodeValue v) {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.FromArgb(40, 90, 150));
            var rnd = new Random(7);
            for (int i = 0; i < 300; i++) using (var b = new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)))) g.FillRectangle(b, rnd.Next(w), rnd.Next(h), rnd.Next(4, 160), rnd.Next(4, 60));
            // window-like content with black/white stripes that must not be taken for finders
            for (int i = 0; i < 40; i++) g.FillRectangle(i % 2 == 0 ? Brushes.Black : Brushes.White, 100 + i * 9, h - 200, 9, 80);
            using (var f = new Font("Segoe UI", 14)) g.DrawString("Invoice INV-2026-00012 saved  1 1 3 1 1", f, Brushes.Black, 60, h - 100);
            if (v != null) Timecode.Draw(g, x, y, cell, v, "UTC+01:00   monotonic 4567.890 s   display 2");
        }
        return bmp;
    }
    static Bitmap Scale(Bitmap src, double f) {
        var dst = new Bitmap((int)(src.Width * f), (int)(src.Height * f), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(dst)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, dst.Width, dst.Height); }
        return dst;
    }

    public static void BitsRoundTripAndChecksum() {
        var v = Sample();
        var bits = Timecode.Encode(v);
        T.Eq(112, bits.Length, "cells");
        var back = Timecode.Decode(bits);
        T.Eq(v.UtcMs, back.UtcMs, "time"); T.Eq(60, back.OffsetMinutes, "offset"); T.Eq(4567890L, back.MonoMs, "monotonic"); T.Eq(2, back.Display, "display");
        for (int i = 0; i < bits.Length; i++) { bits[i] = !bits[i]; T.True(Timecode.Decode(bits) == null, "one wrong cell (" + i + ") is detected"); bits[i] = !bits[i]; }
        var neg = new TimecodeValue { UtcMs = 1, OffsetMinutes = -570, MonoMs = 0, Display = 1 };
        T.Eq(-570, Timecode.Decode(Timecode.Encode(neg)).OffsetMinutes, "negative offset");
        long ms = (long)(new DateTime(2026, 11, 24, 17, 20, 31, 123, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        T.Eq("2026-11-24 18:20:31.123 +0100", new TimecodeValue { UtcMs = ms, OffsetMinutes = 60 }.Text, "digit line");
    }

    public static void StripIsFoundAnywhereInAFrame() {
        var v = Sample(); var hint = Rectangle.Empty;
        foreach (var pos in new[] { new Point(1920 - 872, 0), new Point(0, 1080 - 72), new Point(333, 417) }) {
            using (var f = Frame(1920, 1080, pos.X, pos.Y, 8, v)) {
                hint = Rectangle.Empty;
                var r = Timecode.Read(f, ref hint);
                T.True(r != null && !r.FromDigits, "pattern read at " + pos);
                T.Eq(v.UtcMs, r.UtcMs, "time"); T.Eq(v.MonoMs, r.MonoMs, "monotonic"); T.Eq(2, r.Display, "display");
                T.True(Math.Abs(hint.X - pos.X) < 12 && Math.Abs(hint.Y - pos.Y) < 12, "place reported: " + hint);
            }
        }
    }

    public static void NoStripGivesNothing() {
        var hint = Rectangle.Empty;
        using (var f = Frame(1280, 720, 0, 0, 8, null)) T.True(Timecode.Read(f, ref hint) == null, "no false reading");
    }

    public static void ScaledAndCompressedFramesAreRead() {
        var v = Sample();
        using (var full = Frame(1920, 1080, 1920 - 872, 0, 8, v)) {
            string report = "";
            foreach (double s in new[] { 1.0, 0.75, 2.0 / 3, 0.5, 0.4, 1.0 / 3 }) {
                using (var sc = Scale(full, s)) {
                    // JPEG with low quality stands in for video compression
                    var ms = new MemoryStream(); var enc = Array.Find(ImageCodecInfo.GetImageEncoders(), delegate (ImageCodecInfo c) { return c.MimeType == "image/jpeg"; });
                    var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(Encoder.Quality, 35L);
                    sc.Save(ms, enc, ep); ms.Position = 0;
                    using (var jpg = new Bitmap(ms)) {
                        var hint = Rectangle.Empty; var r = Timecode.Read(jpg, ref hint);
                        report += " " + s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":" + (r == null ? "none" : r.FromDigits ? "digits" : "pattern");
                        if (s >= 0.5) { T.True(r != null, "read at scale " + s); T.Eq(v.UtcMs, r.UtcMs, "time at scale " + s); }
                        else if (r != null) T.Eq(v.UtcMs, r.UtcMs, "a reading is never wrong (scale " + s + ")");
                    }
                }
            }
            T.Note("read_by_scale_cell8_jpeg35", report.Trim());
        }
    }

    public static void DamagedPatternFallsBackToTheDigits() {
        var v = Sample();
        using (var f = Frame(1920, 1080, 400, 300, 8, v)) {
            using (var g = Graphics.FromImage(f)) g.FillRectangle(Brushes.Gray, 400 + 8 * 11, 300 + 8 * 2, 8 * 6, 8 * 4);   // a blob over data cells
            var hint = Rectangle.Empty; var r = Timecode.Read(f, ref hint);
            T.True(r != null && r.FromDigits, "read from the digits");
            T.Eq(v.UtcMs, r.UtcMs, "same time in milliseconds"); T.Eq(60, r.OffsetMinutes, "offset from the digits"); T.Eq(-1L, r.MonoMs, "no monotonic time in the digits");
        }
        var w = new TimecodeValue { UtcMs = 1795630831999L - 0, OffsetMinutes = -330, MonoMs = 1, Display = 1 };
        using (var f = Frame(1280, 720, 10, 10, 6, w)) {
            using (var g = Graphics.FromImage(f)) g.FillRectangle(Brushes.White, 10 + 6 * 9, 10 + 6, 6 * 16, 6 * 7);
            var hint = Rectangle.Empty; var r = Timecode.Read(f, ref hint);
            T.True(r != null && r.FromDigits && r.UtcMs == w.UtcMs && r.OffsetMinutes == -330, "negative offset and cell size 6 through the digits");
        }
    }

    public static void OverlayProgramRendersTheSameStrip() {
        string png = Path.Combine(T.TempDir("strip"), "strip.png"); string output;
        int code = T.Run(Path.Combine(T.BinDir, "eqlmaker-timestamp.exe"), "--render \"" + png + "\" --at 1795630831123 --offset 60 --mono 4567890 --cell 8", null, out output, 20000);
        T.Eq(0, code, "exit code " + output);
        using (var bmp = new Bitmap(png)) {
            T.Eq(872, bmp.Width, "width"); T.Eq(72, bmp.Height, "height");
            var hint = Rectangle.Empty; var r = Timecode.Read(bmp, ref hint);
            T.True(r != null && !r.FromDigits && r.UtcMs == 1795630831123L && r.MonoMs == 4567890 && r.OffsetMinutes == 60, "the program draws what the reader reads");
        }
    }
}
}
