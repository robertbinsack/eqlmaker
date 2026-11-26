// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Narration: text splitting, drafting from captions, fitting the clips into their segments, the
// sound buffers, and the whole run against a stand-in for the Fish Speech server (no real voice).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EqlMaker.Tests {

/// <summary>Stand-in for the HTTP interface of Fish Speech: health, list of voice references, and /v1/tts that
/// answers with a tone whose length follows the text (15 characters per second) between silences.</summary>
public class FakeFishServer : IDisposable {
    readonly TcpListener listener; readonly Thread thread; volatile bool stop;
    public readonly List<string> Bodies = new List<string>();
    public string[] Voices = { "test-voice" }; public double CharsPerSecond = 15; public bool Healthy = true;
    public string Url { get { return "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; } }

    public FakeFishServer() {
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        thread = new Thread(Loop); thread.IsBackground = true; thread.Start();
    }

    void Loop() {
        while (!stop) {
            TcpClient c; try { c = listener.AcceptTcpClient(); } catch { return; }
            try { using (c) Handle(c.GetStream()); } catch { }
        }
    }

    static string ReadLine(Stream s) { var sb = new StringBuilder(); int b; while ((b = s.ReadByte()) >= 0 && b != '\n') if (b != '\r') sb.Append((char)b); return sb.ToString(); }

    void Handle(NetworkStream s) {
        string first = ReadLine(s); int len = 0; string h;
        while ((h = ReadLine(s)) != "") { var m = Regex.Match(h, @"^Content-Length:\s*(\d+)", RegexOptions.IgnoreCase); if (m.Success) len = int.Parse(m.Groups[1].Value); }
        var body = new byte[len]; int got = 0; while (got < len) { int r = s.Read(body, got, len - got); if (r <= 0) break; got += r; }
        string path = first.Split(' ')[1]; byte[] answer; string type = "application/json"; int status = 200;
        if (path == "/v1/health") answer = Encoding.UTF8.GetBytes(Healthy ? "{\"status\":\"ok\"}" : "{\"status\":\"loading\"}");
        else if (path == "/v1/references/list") answer = Encoding.UTF8.GetBytes(Json.Write(new JO("success", true, "reference_ids", new List<object>(Voices))));
        else if (path == "/v1/tts") {
            string text = Encoding.UTF8.GetString(body); lock (Bodies) Bodies.Add(text);
            var o = Json.ParseObject(text);
            answer = Tone(o.Str("text").Length / CharsPerSecond, 0.3, 0.5).ToWav(); type = "audio/wav";
        } else { answer = Encoding.UTF8.GetBytes("{}"); status = 404; }
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " X\r\nContent-Type: " + type + "\r\nContent-Length: " + answer.Length + "\r\nConnection: close\r\n\r\n");
        s.Write(head, 0, head.Length); s.Write(answer, 0, answer.Length); s.Flush();
    }

    /// <summary>A 220 Hz tone of the given length with silence before and after.</summary>
    public static Pcm Tone(double seconds, double silenceBefore, double amplitude) {
        int rate = 44100, a = (int)(silenceBefore * rate), n = (int)(seconds * rate); var s = new short[a + n + a];
        for (int i = 0; i < n; i++) s[a + i] = (short)(amplitude * 32767 * Math.Sin(2 * Math.PI * 220 * i / rate));
        return new Pcm { Rate = rate, Samples = s };
    }

    public void Dispose() { stop = true; listener.Stop(); }
}

public static class NarrationTests {
    static NarrationSegment G(double a, double b, string text) { return new NarrationSegment { Start = a, End = b, Text = text }; }

    public static void SplitTextKeepsShortTextsWholeAndSplitsLongOnesAtSentences() {
        var one = Narration.SplitText("Ein kurzer Satz. Noch einer.", 500);
        T.Eq(1, one.Count, "short text in one request"); T.Eq(0.0, one[0].Value, "no pause after the last part");
        var paras = Narration.SplitText("Erster Absatz.\n\nZweiter Absatz.", 500);
        T.Eq(2, paras.Count, "paragraphs"); T.Eq(Narration.ParagraphPause, paras[0].Value, "pause after a paragraph");
        string sentence = "This sentence has exactly some words in it to fill space. ";
        var lng = Narration.SplitText(sentence + sentence + sentence + sentence, 130);
        T.True(lng.Count >= 2, "long paragraph split"); foreach (var kv in lng) T.True(kv.Key.Length <= 130, "part within the limit: " + kv.Key.Length);
        T.Eq(Narration.SentencePause, lng[0].Value, "pause between sentence groups");
        var word = Narration.SplitText(new string('x', 40) + " " + new string('y', 40), 50);
        T.Eq(2, word.Count, "a text without sentence marks is split at spaces");
    }

    public static void DraftCoversTheWholeVideoAndSplitsLongCaptions() {
        var caps = new List<TranscriptSegment> {
            new TranscriptSegment { From = 1.0, To = 5.0, Text = "intro" },
            new TranscriptSegment { From = 6.0, To = 36.0, Text = "long part" },
            new TranscriptSegment { From = 36.0, To = 37.0, Text = "blink" },
            new TranscriptSegment { From = 40.0, To = 44.0, Text = "end" } };
        var d = Narration.Draft(caps, 50, 14, 2.5);
        T.Eq(0.0, d[0].Start, "first segment starts at 0"); T.Eq(6.0, d[0].End, "gap goes to the previous caption");
        T.Eq(50.0, d[d.Count - 1].End, "last segment ends with the video");
        for (int i = 1; i < d.Count; i++) T.Near(d[i - 1].End, d[i].Start, 1e-9, "segments touch");
        int parts = 0; foreach (var g in d) { T.True(g.Length <= 14.01, "segment at most 14 s: " + g.Length); if (g.Hint.StartsWith("long part")) parts++; }
        T.Eq(3, parts, "a 34 s caption (with the merged 1 s caption) becomes 3 parts");
        T.True(d.Exists(delegate (NarrationSegment g) { return g.Hint.Contains("blink"); }), "a very short caption is merged into the previous one");
        T.Eq((int)Math.Floor((6 - 0.35) * 12.9), Narration.Budget(d[0], 12.9), "budget follows length and speaking rate");
    }

    public static void DraftReadsJsonCaptionsWithNotesAndSrt() {
        string dir = T.TempDir("narr-draft"), j = Path.Combine(dir, "c.json"), srt = Path.Combine(dir, "c.srt");
        File.WriteAllText(j, "{\"duration\": 20, \"segments\": [{\"start\": 0, \"end\": 8, \"text\": \"A\", \"notes\": [\"1.0 s: click\"]}, {\"start\": 8, \"end\": 20, \"text\": \"B\"}]}");
        double dur; var caps = Narration.ReadCaptions(j, out dur);
        T.Eq(20.0, dur, "duration from the file"); T.Contains(caps[0].Text, "click", "notes become part of the hint");
        File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:04,000\nHallo\n\n2\n00:00:05,000 --> 00:00:09,500\nWelt\n");
        caps = Narration.ReadCaptions(srt, out dur); T.Eq(2, caps.Count, "SRT captions"); T.Near(9.5, caps[1].To, 1e-9, "SRT time");
        var r = Narration.DraftFile(Config.Load(dir), j, null, "de", 12.9, 14, 2.5, Path.Combine(dir, "n.json"), false);
        var s = NarrationScript.Load(r.Str("script"));
        T.Eq("de", s.Language, "language"); T.Eq(2, s.Segments.Count, "segments"); T.Eq("", s.Segments[0].Text, "texts are left to the author");
        T.Throws(delegate { Narration.DraftFile(Config.Load(dir), j, null, "de", 12.9, 14, 2.5, Path.Combine(dir, "n.json"), false); }, "exists already", "no silent overwrite");
    }

    public static void FitSpeedsUpSlightlyAndReportsWhatToCut() {
        var segs = new List<NarrationSegment> { G(0, 5, "a"), G(5, 10, "b"), G(10, 15, "c"), G(15, 20, "") };
        var p = Narration.Fit(segs, new[] { 4.0, 5.3, 8.0, 0 }, 20, 1.1, 0.15, 14);
        T.Eq(3, p.Count, "an empty segment has no clip");
        T.Eq(1.0, p[0].Tempo, "a clip that fits keeps its tempo"); T.Near(0, p[0].Start, 1e-9, "starts with its segment");
        T.Near(5.3 / 5.0, p[1].Tempo, 0.001, "a clip a little too long is sped up"); T.True(p[1].Problem == null, "within the limit");
        T.True(p[2].Problem == null, "the last clip may use the following empty segment (room up to the end of the video)");
        var q = Narration.Fit(segs, new[] { 4.0, 7.0, 3.0, 0 }, 20, 1.1, 0.15, 14);
        T.Eq("too long", q[1].Problem, "far too long"); T.Eq((int)Math.Ceiling((7.0 / 1.1 - 5.0) * 14), q[1].CutChars, "characters to cut");
        var cov = Narration.Coverage(p, 20);
        T.Near(100.0 * (4.0 + 5.0 + 8.0) / 20, cov.Num("coverage_percent", 0), 0.2, "coverage"); T.Eq(false, cov.Bool("overlaps", true), "no overlaps");
    }

    public static void FitStartsAClipAfterTheLongPreviousOne() {
        var segs = new List<NarrationSegment> { G(0, 5, "a"), G(5, 8, ""), G(8, 12, "b") };
        var p = Narration.Fit(segs, new[] { 7.0, 0, 3.0 }, 12, 1.1, 0.15, 14);
        T.Near(7.0, p[0].End, 1e-9, "the first clip runs into the empty segment");
        T.Near(8.0, p[1].Start, 1e-9, "the next clip still starts with its segment");
        var segs2 = new List<NarrationSegment> { G(0, 4, "a"), G(4, 9, "b") };
        var p2 = Narration.Fit(segs2, new[] { 4.3, 2.0 }, 9, 1.1, 0.15, 14);
        T.True(p2[1].Start >= p2[0].End + 0.149, "a gap of at least min_gap between clips");
    }

    public static void PcmTrimsSilenceAndLimitsThePeak() {
        var t = FakeFishServer.Tone(1.0, 0.5, 1.0);
        var w = Pcm.FromWav(t.ToWav()); T.Eq(t.Samples.Length, w.Samples.Length, "WAV round trip");
        var tr = w.Trim(-45, 60); T.Near(1.0 + 0.12, tr.Seconds, 0.01, "silence trimmed, 60 ms kept on both sides");
        tr.LimitPeak(-1); int peak = 0; foreach (var x in tr.Samples) peak = Math.Max(peak, Math.Abs((int)x));
        T.True(peak <= (int)(32767 * Math.Pow(10, -1 / 20.0)), "peak at most -1 dBFS");
        var quiet = FakeFishServer.Tone(0.2, 0, 0.1); quiet.LimitPeak(-1); int qp = 0; foreach (var x in quiet.Samples) qp = Math.Max(qp, Math.Abs((int)x));
        T.True(qp < 3300 && qp > 3200, "a quiet clip is never raised");
    }

    public static void MixFilterSetsMusicVolumeDuckingAndFades() {
        string f = Narration.MixFilter(true, 60, 30, true, 1.5, 3);
        T.Contains(f, "volume=0.3", "music at 30 %"); T.Contains(f, "sidechaincompress", "music ducked under the speech");
        T.Contains(f, "afade=t=out:st=57", "fade out at the end"); T.Contains(f, "atrim=0:60", "trimmed to the video");
        T.True(!Narration.MixFilter(true, 60, 40, false, 1.5, 3).Contains("sidechaincompress"), "no ducking when switched off");
        T.True(!Narration.MixFilter(false, 60, 25, true, 1.5, 3).Contains("[2:a]"), "no music input without music");
    }

    public static void CheckNamesAMissingServerOrVoice() {
        using (var srv = new FakeFishServer()) {
            var tts = new FishSpeechTts { BaseUrl = srv.Url, ReferenceId = "other-voice" };
            T.Throws(delegate { tts.Check(); }, "not registered", "unknown voice");
            T.Throws(delegate { new FishSpeechTts { BaseUrl = srv.Url }.Check(); }, "no voice", "no voice given");
            srv.Healthy = false; T.Throws(delegate { new FishSpeechTts { BaseUrl = srv.Url, ReferenceId = "test-voice" }.Check(); }, "not healthy", "server not ready");
        }
        T.Throws(delegate { new FishSpeechTts { BaseUrl = "http://127.0.0.1:1", ReferenceId = "x" }.Check(); }, "not reachable", "no server");
    }

    static double MeanVolume(string ffmpeg, string file, double from, double length) {
        var psi = new ProcessStartInfo(ffmpeg, "-hide_banner -nostats -ss " + from.ToString(CultureInfo.InvariantCulture) + " -t " + length.ToString(CultureInfo.InvariantCulture) + " -i \"" + file + "\" -vn -af volumedetect -f null -");
        psi.UseShellExecute = false; psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
        using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd(); p.WaitForExit();
            var m = Regex.Match(e, @"mean_volume:\s*(-?[\d.]+) dB"); if (!m.Success) throw new Exception("volumedetect: " + e); return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); }
    }

    static void Ff(string ffmpeg, string args) {
        string o; T.Eq(0, T.Run(ffmpeg, "-hide_banner -loglevel error -y " + args, null, out o, 120000), "ffmpeg " + args + " " + o);
    }

    public static void NarrateMixesSpeechAndMusicOverTheVideo() {
        string ffmpeg = T.Ffmpeg; Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", ffmpeg);
        string dir = T.TempDir("narr-run"), video = Path.Combine(dir, "in.mp4"), music = Path.Combine(dir, "music.wav"), script = Path.Combine(dir, "n.json"), outFile = Path.Combine(dir, "out.mp4");
        Ff(ffmpeg, "-f lavfi -i testsrc=size=320x240:rate=25:duration=12 -c:v libx264 -pix_fmt yuv420p \"" + video + "\"");
        Ff(ffmpeg, "-f lavfi -i sine=frequency=330:sample_rate=44100:duration=5 -ac 2 \"" + music + "\"");   // shorter than the video: must loop
        Json.WriteFile(script, new JO("format", "eqlmaker-narration", "version", 1, "language", "en", "chars_per_second", 15, "segments", new List<object> {
            new JO("start", 0.5, "end", 3.0, "text", "Twenty-five characters ok"),   // 25 chars = 1.67 s
            new JO("start", 3.0, "end", 4.0, "text", ""),
            new JO("start", 4.0, "end", 6.0, "text", "Thirty characters of text, ok."),   // 2.0 s + 0.12 s margin in 2 s: tempo 1.06
            new JO("start", 6.0, "end", 7.0, "text", "Ok.") }));
        using (var srv = new FakeFishServer()) {
            var o = new NarrateOptions { Video = video, Script = script, Out = outFile, Music = music, MusicVolume = 30 };
            var r = Narration.Run(Config.Load(dir), o, new FishSpeechTts { BaseUrl = srv.Url, ReferenceId = "test-voice" });
            T.Eq(3, srv.Bodies.Count, "one request per spoken segment");
            var body = Json.ParseObject(srv.Bodies[0]);
            T.Eq("test-voice", body.Str("reference_id"), "voice reference sent"); T.Eq(1996, body.Int("seed", 0), "fixed seed"); T.Eq("wav", body.Str("format"), "WAV requested");
            T.Eq(false, body.Bool("streaming", true), "no streaming"); T.True(!body.Has("language"), "no language parameter");
            T.Eq(true, r.Bool("audio", false), "the output has sound");
            T.Near(12.0, r.Num("duration", 0), 0.15, "same length as the video");
            var rep = Json.ReadFile(r.Str("report")); var clips = rep.Arr("clips");
            T.Near(2.12 / 2.0, ((JO)clips[1]).Num("tempo", 0), 0.02, "second clip sped up");
            T.Eq(false, rep.Obj("coverage").Bool("overlaps", true), "no overlaps");
            // music alone (after the speech, before the fade-out) at 30 % of its level: about -10.5 dB
            double musicAlone = MeanVolume(ffmpeg, music, 1, 2), mixed = MeanVolume(ffmpeg, outFile, 8.0, 1.0);
            T.Note("music_db_relative", Math.Round(mixed - musicAlone, 2));
            T.Near(20 * Math.Log10(0.30), mixed - musicAlone, 1.5, "music level as set");
            T.True(MeanVolume(ffmpeg, outFile, 1.0, 1.0) > mixed + 3, "speech is louder than the music");
            // again: every clip comes from the cache
            o.Force = true; var r2 = Narration.Run(Config.Load(dir), o, new FishSpeechTts { BaseUrl = srv.Url, ReferenceId = "test-voice" });
            T.Eq(3, srv.Bodies.Count, "no new requests"); T.Eq(3, r2.Int("from_cache", 0), "clips from the cache");
        }
    }

    public static void NarrateRefusesSpeechThatDoesNotFit() {
        string ffmpeg = T.Ffmpeg; Environment.SetEnvironmentVariable("EQLMAKER_FFMPEG", ffmpeg);
        string dir = T.TempDir("narr-long"), video = Path.Combine(dir, "in.mp4"), script = Path.Combine(dir, "n.json");
        Ff(ffmpeg, "-f lavfi -i testsrc=size=160x120:rate=25:duration=4 -c:v libx264 -pix_fmt yuv420p \"" + video + "\"");
        Json.WriteFile(script, new JO("segments", new List<object> { new JO("start", 0, "end", 2, "text", new string('a', 60)), new JO("start", 2, "end", 4, "text", "short") }));
        using (var srv = new FakeFishServer()) {
            T.Throws(delegate { Narration.Run(Config.Load(dir), new NarrateOptions { Video = video, Script = script, Out = Path.Combine(dir, "o.mp4") }, new FishSpeechTts { BaseUrl = srv.Url, ReferenceId = "test-voice" }); },
                "shorten the text", "too long");
            T.True(!File.Exists(Path.Combine(dir, "o.mp4")), "no output written");
        }
    }
}
}
