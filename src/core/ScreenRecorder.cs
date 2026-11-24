// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Screen recorder: GDI capture of a screen region (with mouse pointer) or of one window
// (PrintWindow, background mode) -> H.264 MP4 through the Media Foundation encoder built into
// Windows. Frame times follow the wall clock, so database events line up with the video.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace EqlMaker {

// Media Foundation (mfplat/mfreadwrite): only the interface slots that are used are typed.
[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes {
    void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
    void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17();
    void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
    void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
    void _20();
    void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
}
[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType {
    void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
    void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17();
    void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
    void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
    void _20();
    void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
}
[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample {
    void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
    void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
    void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
    void _30(); void _31(); void _32();
    void SetSampleTime(long time);
    void _34();
    void SetSampleDuration(long duration);
    void _36(); void _37(); void _38();
    void AddBuffer(IMFMediaBuffer buffer);
}
[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer {
    void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    void Unlock();
    void _2();
    void SetCurrentLength(int length);
}
[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSinkWriter {
    void AddStream(IMFMediaType type, out int index);
    void SetInputMediaType(int index, IMFMediaType type, IMFAttributes encodingParameters);
    void BeginWriting();
    void WriteSample(int index, IMFSample sample);
    void _4(); void _5(); void _6(); void _7();
    void Finalize_();
}
static class MF {
    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType t);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes a, int size);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int length, out IMFMediaBuffer b);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample s);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr stream, IMFAttributes attr, out IMFSinkWriter w);
    public static void Ok(int hr, string what) { if (hr < 0) throw new Exception(what + " failed: 0x" + hr.ToString("x8")); }
    public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid AvgBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid HardwareTransforms = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid DisableThrottling = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
}

/// <summary>Paints one frame into a device context of the given size; false when no picture was available.</summary>
public delegate bool FramePainter(IntPtr dc, int width, int height);

public class ScreenRecorder {
    public string File; public Rectangle Region; public int Fps = 15; public int Bitrate;
    /// <summary>When set, every frame is painted by this delegate (a window drawn by itself, a browser page)
    /// instead of being copied from the screen; Region gives the size.</summary>
    public FramePainter Painter;
    /// <summary>Wall clock (microseconds since 1970 UTC) of the first frame.</summary>
    public long StartedAtUs;
    public double Duration; public int Frames, BlackFrames; public string Error;
    Thread thread; volatile bool stop;

    public static void DrawCursor(IntPtr dc, int originX, int originY) {
        var ci = new W.CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(W.CURSORINFO));
        if (!W.GetCursorInfo(ref ci) || (ci.flags & 1) == 0) return;
        W.ICONINFO ii;
        int hx = 0, hy = 0;
        if (W.GetIconInfo(ci.hCursor, out ii)) { hx = ii.xHotspot; hy = ii.yHotspot; if (ii.hbmMask != IntPtr.Zero) W.DeleteObject(ii.hbmMask); if (ii.hbmColor != IntPtr.Zero) W.DeleteObject(ii.hbmColor); }
        W.DrawIconEx(dc, ci.ptScreenPos.X - originX - hx, ci.ptScreenPos.Y - originY - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
    }

    public void Start() {
        Desktop.Init();
        Region = new Rectangle(Region.X, Region.Y, Region.Width & ~1, Region.Height & ~1);   // H.264: even sizes
        if (Bitrate <= 0) Bitrate = Math.Max(1000000, Region.Width * Region.Height * Fps / 6);
        var ready = new ManualResetEvent(false);
        thread = new Thread(delegate () { Run(ready); });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        ready.WaitOne(15000);
        if (Error != null) throw new Exception("recorder: " + Error);
    }

    public void Stop() {
        stop = true;
        if (thread != null) thread.Join(60000);
        if (Error != null) throw new Exception("recorder: " + Error);
    }

    void Run(ManualResetEvent ready) {
        int w = Region.Width, h = Region.Height, stride = w * 4;
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero, bits;
        IMFSinkWriter writer = null;
        try {
            MF.Ok(MF.MFStartup(0x20070, 0), "MFStartup");
            IMFAttributes attr; MF.Ok(MF.MFCreateAttributes(out attr, 2), "MFCreateAttributes");
            attr.SetUINT32(MF.HardwareTransforms, 1);
            attr.SetUINT32(MF.DisableThrottling, 1);
            if (System.IO.File.Exists(File)) System.IO.File.Delete(File);
            MF.Ok(MF.MFCreateSinkWriterFromURL(File, IntPtr.Zero, attr, out writer), "MFCreateSinkWriterFromURL");
            IMFMediaType outType; MF.Ok(MF.MFCreateMediaType(out outType), "MFCreateMediaType");
            outType.SetGUID(MF.MajorType, MF.Video);
            outType.SetGUID(MF.Subtype, MF.H264);
            outType.SetUINT32(MF.AvgBitrate, Bitrate);
            outType.SetUINT32(MF.Interlace, 2);
            outType.SetUINT64(MF.FrameSize, ((long)w << 32) | (uint)h);
            outType.SetUINT64(MF.FrameRate, ((long)Fps << 32) | 1);
            outType.SetUINT64(MF.PixelAspect, (1L << 32) | 1);
            int stream; writer.AddStream(outType, out stream);
            IMFMediaType inType; MF.Ok(MF.MFCreateMediaType(out inType), "MFCreateMediaType");
            inType.SetGUID(MF.MajorType, MF.Video);
            inType.SetGUID(MF.Subtype, MF.RGB32);
            inType.SetUINT32(MF.Interlace, 2);
            inType.SetUINT32(MF.DefaultStride, stride);          // top-down rows
            inType.SetUINT64(MF.FrameSize, ((long)w << 32) | (uint)h);
            inType.SetUINT64(MF.FrameRate, ((long)Fps << 32) | 1);
            inType.SetUINT64(MF.PixelAspect, (1L << 32) | 1);
            writer.SetInputMediaType(stream, inType, null);
            writer.BeginWriting();

            // top-down 32-bit DIB section as capture target
            var bmi = new byte[40];
            BitConverter.GetBytes(40).CopyTo(bmi, 0); BitConverter.GetBytes(w).CopyTo(bmi, 4); BitConverter.GetBytes(-h).CopyTo(bmi, 8);
            BitConverter.GetBytes((short)1).CopyTo(bmi, 12); BitConverter.GetBytes((short)32).CopyTo(bmi, 14);
            screen = W.GetDC(IntPtr.Zero); mem = W.CreateCompatibleDC(screen);
            dib = W.CreateDIBSection(screen, bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Exception("CreateDIBSection failed");
            old = W.SelectObject(mem, dib);

            long frameTicks = 10000000L / Fps;
            var sw = Stopwatch.StartNew();
            StartedAtUs = Clock.NowUs();
            ready.Set();
            long index = 0, written = -1;
            while (true) {
                bool last = stop;
                if (Painter != null) { bool ok = false; try { ok = Painter(mem, w, h); } catch { } if (!ok) BlackFrames++; }
                else if (W.BitBlt(mem, 0, 0, w, h, screen, Region.X, Region.Y, 0x00CC0020 | 0x40000000)) DrawCursor(mem, Region.X, Region.Y);
                else BlackFrames++;   // desktop not visible: the previous image stays
                // the frame belongs to the current wall-clock slot; slots missed while busy are skipped
                index = sw.ElapsedTicks * 10000000L / Stopwatch.Frequency / frameTicks;
                if (index <= written) index = written + 1;
                IMFMediaBuffer buf; MF.Ok(MF.MFCreateMemoryBuffer(stride * h, out buf), "MFCreateMemoryBuffer");
                IntPtr p; int max, cur; buf.Lock(out p, out max, out cur);
                CopyMemory(p, bits, stride * h);
                buf.Unlock(); buf.SetCurrentLength(stride * h);
                IMFSample s; MF.Ok(MF.MFCreateSample(out s), "MFCreateSample");
                s.AddBuffer(buf);
                s.SetSampleTime(index * frameTicks);
                s.SetSampleDuration(frameTicks);
                writer.WriteSample(stream, s);
                Marshal.ReleaseComObject(s); Marshal.ReleaseComObject(buf);
                written = index; Frames++;
                if (last) break;
                long next = (index + 1) * frameTicks;
                long nowTicks = sw.ElapsedTicks * 10000000L / Stopwatch.Frequency;
                int wait = (int)((next - nowTicks) / 10000);
                if (wait > 0) Thread.Sleep(wait);
            }
            Duration = (written + 1) / (double)Fps;
            writer.Finalize_();
        } catch (Exception e) {
            Error = e.Message;
            ready.Set();
        } finally {
            if (old != IntPtr.Zero) W.SelectObject(mem, old);
            if (dib != IntPtr.Zero) W.DeleteObject(dib);
            if (mem != IntPtr.Zero) W.DeleteDC(mem);
            if (screen != IntPtr.Zero) W.ReleaseDC(IntPtr.Zero, screen);
            if (writer != null) Marshal.ReleaseComObject(writer);
            MF.MFShutdown();
        }
    }
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dst, IntPtr src, int len);
}
}
