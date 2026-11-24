// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// video-import end to end with real video files: frames with the time stamp strip are rendered,
// encoded by FFmpeg (MPEG-4, as lossy as a screen recording), and imported again. The true time of
// every frame is known, so the error of the time matching is measured in milliseconds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EqlMaker.Tests {

public static class VideoImportTests {
    const long W0 = 1795630000000L;          // wall clock (ms) of video time 0 on the recording machine
    const double Drift = 1.0002;             // wall seconds per video second
    const double CutAt = 8.0, CutLength = 7.3;   // at video time 8.0 s, 7.3 s of wall time are missing
    static readonly double[] Changes = { 2.0, 5.0, 10.0, 13.5, 17.0 };   // video times at which the screen changes
    const double ServerAhead = 42.5;         // clock of the database server minus clock of the recording machine (s)
    const double Latency = 0.2;              // the screen shows the result this long after the statement

    static double TruthMs(double video, bool cut) { return video * Drift * 1000 + (cut && video >= CutAt ? CutLength * 1000 : 0); }

    static string MakeVideo(string dir, int fps, bool cut, bool patternDamaged, double seconds) {
        string ffmpeg = T.Ffmpeg, file = Path.Combine(dir, "session-" + fps + (cut ? "-cut" : "") + ".mp4");
        const int W = 640, H = 360;
        var psi = new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgr24 -s " + W + "x" + H + " -r " + fps + " -i - -f lavfi -i sine=frequency=440:sample_rate=16000 -shortest -c:v mpeg4 -q:v 4 -c:a aac \"" + file + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        using (var p = Process.Start(psi)) using (var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb)) {
            var raw = new byte[W * H * 3]; var stdin = p.StandardInput.BaseStream;
            var colors = new[] { Color.SteelBlue, Color.DarkOliveGreen, Color.Sienna, Color.SlateGray, Color.Indigo, Color.Teal };
            int frames = (int)(seconds * fps);
            for (int k = 0; k < frames; k++) {
                double v = (double)k / fps; int scene = 0; foreach (double c in Changes) if (v >= c) scene++;
                using (var g = Graphics.FromImage(bmp)) {
                    g.Clear(colors[scene]);
                    using (var f = new Font("Segoe UI", 20)) g.DrawString("Mask " + (scene + 1), f, Brushes.White, 40, 150);
                    g.FillRectangle(Brushes.White, 40, 220, 300 + scene * 40, 60);
                    var tv = new TimecodeValue { UtcMs = W0 + (long)Math.Floor(TruthMs(v, cut)), OffsetMinutes = 60, MonoMs = (long)Math.Floor(TruthMs(v, cut)), Display = 1 };
                    Timecode.Draw(g, 100, 8, 4, tv, null);
                    if (patternDamaged) g.FillRectangle(Brushes.Gray, 100 + 4 * 10, 8 + 4 * 2, 4 * 12, 4 * 5);
                }
                var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                Marshal.Copy(data.Scan0, raw, 0, raw.Length); bmp.UnlockBits(data);
                stdin.Write(raw, 0, raw.Length);
            }
            stdin.Close();
            string err = p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("ffmpeg: " + err);
        }
        return file;
    }

    /// <summary>Events as the database server logged them (its clock is ahead), one burst per screen change.</summary>
    static void MakeEvents(Config cfg, bool cut) {
        string dir = Path.Combine(cfg.RecordingsDir, "server-log"); Directory.CreateDirectory(dir);
        var ev = new List<DbEvent>(); string[] sql = { "SELECT id, name FROM customer ORDER BY id", "INSERT INTO customer (name, city) VALUES ($1, $2)", "SELECT * FROM article WHERE sku = $1", "UPDATE article SET stock = stock - $1 WHERE id = $2", "SELECT sum(gross) FROM invoice WHERE status = 'open'" };
        for (int i = 0; i < Changes.Length; i++) {
            long ts = (W0 + (long)TruthMs(Changes[i], cut) - (long)(Latency * 1000) + (long)(ServerAhead * 1000)) * 1000;
            ev.Add(new DbEvent { Ts = ts, Kind = "execute", Source = "postgres", Via = "log", Session = "7", Database = "shop", Application = "legacy", Sql = sql[i] });
            ev.Add(new DbEvent { Ts = ts + 15000, Kind = "statement", Source = "postgres", Via = "log", Session = "7", Database = "shop", Application = "legacy", Sql = "SELECT now()" });
        }
        DbEvent.WriteFile(Path.Combine(dir, "events.jsonl"), ev);
        Json.WriteFile(Path.Combine(dir, "recording.json"), new JO("format", "eqlmaker-recording", "version", "0.1", "name", "server-log", "t0_us", ev[0].Ts, "duration", 60.0, "source", "postgres"));
    }

    static void Check(int fps) {
        string dir = T.TempDir("vi" + fps); var cfg = Config.Load(dir);
        Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", T.Ffmpeg);
        string video = MakeVideo(dir, fps, true, false, 20);
        MakeEvents(cfg, true);
        string vtt = Path.Combine(dir, "spoken.vtt");
        File.WriteAllText(vtt, "WEBVTT\n\n00:00:04.200 --> 00:00:06.000\nNew customers need a credit limit before the first invoice.\n\n00:00:16.500 --> 00:00:18.000\nThe open amount is checked here.\n\n00:00:19.700 --> 00:00:19.900\nBye.\n", new UTF8Encoding(false));
        var r = VideoImport.Run(cfg, new VideoImportOptions { File = video, TranscriptFile = vtt, SampleFps = 2, AttachSeconds = 1.5 });
        var f = Json.ReadFile(r.Str("findings"));

        T.Eq(0, f.Obj("readings").Int("unreadable", -1), "every sampled frame was read at " + fps + " fps");
        T.True(f.Obj("readings").Int("pattern", 0) >= 38, "read through the pattern: " + f.Obj("readings").Int("pattern", 0));
        T.Eq(60, f.Obj("readings").Int("utc_offset_minutes", 0), "UTC offset of the strip");
        // the cut: exact to one frame
        var cuts = f.Obj("clock").Arr("cuts");
        T.Eq(1, cuts.Count, "one cut");
        double cutErrMs = (Convert.ToDouble(cuts[0]) - CutAt) * 1000;
        T.True(Math.Abs(cutErrMs) <= 1000.0 / fps + 1, "cut located to one frame: " + cutErrMs + " ms");
        // mapping error against the truth, at arbitrary video times on both sides of the cut
        var segs = f.Obj("clock").Arr("segments"); double max = 0, sum = 0; int n = 0;
        for (double v = 0.2; v < 19.8; v += 0.137) {
            if (Math.Abs(v - CutAt) < 1.0 / fps) continue;
            var s = (JO)segs[v < Convert.ToDouble(cuts[0]) ? 0 : 1];
            double wallMs = (s.Long("wall_at_video_0_us", 0) + s.Num("rate", 1) * v * 1e6) / 1000.0;
            double err = wallMs - (W0 + TruthMs(v, true)); sum += err; n++; if (Math.Abs(err) > max) max = Math.Abs(err);
        }
        T.Note("fps" + fps + "_mapping_error_ms_mean_max", (sum / n).ToString("0.0", CultureInfo.InvariantCulture) + " / " + max.ToString("0.0", CultureInfo.InvariantCulture));
        T.Note("fps" + fps + "_cut_position_error_ms", cutErrMs.ToString("0.0", CultureInfo.InvariantCulture));
        T.True(max < 40, "mapping error below 40 ms on a stretch of 8 to 12 s: " + max);
        // the wrong clock: the server is 42.5 s ahead; nothing was measured, so it is estimated
        var co = f.Obj("clock_offset");
        double offErr = co.Num("server_minus_recorder_ms", 0) - ServerAhead * 1000;
        T.Note("fps" + fps + "_clock_offset_error_ms", offErr.ToString("0.0", CultureInfo.InvariantCulture) + " (screen latency in this test: " + (Latency * 1000) + " ms, sample interval 500 ms)");
        T.True(co.Str("method").StartsWith("estimated"), "offset estimated: " + co.Str("method"));
        T.True(Math.Abs(offErr) < 800, "offset of the wrong clock found: error " + offErr + " ms");
        // operations, transcript and key frames
        var ops = f.Arr("operations");
        T.Eq(5, ops.Count, "operations (bursts of statements)");
        for (int i = 0; i < 5; i++) T.Near(Changes[i], ((JO)ops[i]).Num("video_from", -99), 0.9, "operation " + (i + 1) + " in video time");
        T.True(f.Arr("keyframes").Count >= 5 && f.Arr("keyframes").Count <= 8, "key frames: " + f.Arr("keyframes").Count);
        T.True(File.Exists(Path.Combine(Path.GetDirectoryName(r.Str("findings")), "keyframes", "k001.png")), "key frame picture written");
        T.Eq("t1", ((JO)ops[1]).Strings("said")[0], "what was said at 4.2 to 6.0 s belongs to the operation at 5.0 s");
        T.Eq("t2", ((JO)ops[4]).Strings("said")[0], "what was said at 16.5 s belongs to the operation at 17.0 s");
        T.Eq("t3", f.Obj("unassigned").Strings("said")[0], "what was said far from any operation stays unassigned");
        var said = ((JO)ops[1]).Arr("findings").Find(delegate (object x) { return ((JO)x).Str("kind") == "said"; }) as JO;
        T.True(said.Strings("words_not_in_spec").Contains("credit") && said.Str("status") == "open", "finding with the words that are new");
        T.True(((JO)ops[1]).Arr("findings").Exists(delegate (object x) { return ((JO)x).Str("kind") == "seen"; }), "key frame attached as a finding");
    }

    public static void ImportAt15FramesPerSecond() { Check(15); }
    public static void ImportAt30FramesPerSecond() { Check(30); }
    public static void ImportAt60FramesPerSecond() { Check(60); }

    public static void DigitsAreUsedWhenThePatternIsDamaged() {
        string dir = T.TempDir("vidigits"); var cfg = Config.Load(dir);
        Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", T.Ffmpeg);
        string video = MakeVideo(dir, 15, false, true, 6);
        var r = VideoImport.Run(cfg, new VideoImportOptions { File = video, NoTranscript = true });
        var f = Json.ReadFile(r.Str("findings"));
        T.True(f.Obj("readings").Int("digits", 0) >= 10 && f.Obj("readings").Int("pattern", 0) == 0, "read from the digits: " + Json.Write(f.Obj("readings")));
        var s = (JO)f.Obj("clock").Arr("segments")[0]; double max = 0;
        for (double v = 0.5; v < 5.5; v += 0.21) { double err = (s.Long("wall_at_video_0_us", 0) + s.Num("rate", 1) * v * 1e6) / 1000.0 - (W0 + TruthMs(v, false)); if (Math.Abs(err) > max) max = Math.Abs(err); }
        T.Note("digits_mapping_error_ms_max", max.ToString("0.0", CultureInfo.InvariantCulture));
        T.True(max < 40, "mapping error through the digits: " + max);
        T.Eq(0, f.Arr("operations").Count, "no events given: no operations");
    }

    public static void VideoWithoutStripIsRefusedWithAClearMessage() {
        string dir = T.TempDir("vinone"); var cfg = Config.Load(dir);
        string ffmpeg = T.Ffmpeg; Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", ffmpeg);
        string file = Path.Combine(dir, "plain.mp4"), o;
        T.Eq(0, T.Run(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -c:v mpeg4 \"" + file + "\"", null, out o, 60000), "test video " + o);
        T.Throws(delegate { VideoImport.Run(cfg, new VideoImportOptions { File = file, NoTranscript = true }); }, "no time stamp was found", "refusal");
    }

    public static void TranscriptFormatsAreRead() {
        string dir = T.TempDir("transcripts");
        string srt = Path.Combine(dir, "a.srt"), json = Path.Combine(dir, "a.json");
        File.WriteAllText(srt, "1\n00:00:01,500 --> 00:00:03,000\nFirst line\nsecond line\n\n2\n01:00:00,000 --> 01:00:01,000\n<i>Late</i>\n");
        var s = Transcript.Read(srt);
        T.Eq(2, s.Count, "cues"); T.Eq(1.5, s[0].From, "start"); T.Eq("First line second line", s[0].Text, "text"); T.Eq(3600.0, s[1].From, "hours"); T.Eq("Late", s[1].Text, "tags removed");
        File.WriteAllText(json, "{\"transcription\":[{\"timestamps\":{\"from\":\"00:00:00,000\",\"to\":\"00:00:02,500\"},\"offsets\":{\"from\":0,\"to\":2500},\"text\":\" Hello there.\"},{\"offsets\":{\"from\":2500,\"to\":2600},\"text\":\" \"}]}");
        var j = Transcript.Read(json);
        T.Eq(1, j.Count, "empty segments dropped"); T.Eq(2.5, j[0].To, "end from milliseconds"); T.Eq("Hello there.", j[0].Text, "text");
    }
}
}

namespace EqlMaker.Tests {

public static class SpeechTests {
    /// <summary>Speech recognition through the recogniser of the package (set EQLMAKER_TRANSCRIBE to its
    /// whisper-cli.exe); the audio is spoken on the spot by a Windows voice.</summary>
    public static void SpokenSentenceIsTranscribed() {
        var cfg = Config.Load(T.TempDir("speech"));
        if (Media.Find(cfg, "transcribe") == null) T.Skip("no speech recogniser (EQLMAKER_TRANSCRIBE is not set and the package tools are not next to the programs)");
        string wav = System.IO.Path.Combine(cfg.Dir, "spoken.wav"), o;
        int code = T.Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + System.IO.Path.Combine(T.RepoRoot, "scripts", "setup", "make-test-audio.ps1") + "\" -Text \"First I open the customer list. Then I save the invoice.\" -Language en -OutFile \"" + wav + "\"", null, out o, 120000);
        if (code == 3) T.Skip("no English speech synthesis voice installed");
        T.Eq(0, code, "test audio " + o);
        string tool; var segs = Transcript.Recognise(cfg, wav, "en", out tool);
        T.True(segs != null && segs.Count > 0, "segments");
        string text = string.Join(" ", segs.ConvertAll(delegate (TranscriptSegment s) { return s.Text; }).ToArray()).ToLowerInvariant();
        T.Contains(text, "customer list", "words"); T.Contains(text, "invoice", "words");
        T.True(segs[0].From >= 0 && segs[segs.Count - 1].To > 1.0 && segs[segs.Count - 1].To < 12, "times of the segments in seconds: " + segs[segs.Count - 1].To);
        T.Note("recogniser", tool); T.Note("text", text);
    }
}
}
