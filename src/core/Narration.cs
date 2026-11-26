// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Narration: a spoken commentary for a finished demo video. A narration script holds timed segments
// (start, end, text). "narrate-draft" builds the script skeleton from the caption timeline of the video
// (SRT, WebVTT, whisper JSON or a JSON list of segments) with a character budget per segment;
// the texts are then written by a person or an AI. "narrate" speaks every segment with a local
// Fish Speech server and a voice reference of the user, measures the clips, fits them into their
// segments (a small speed-up at most), and lays the speech, optionally with background music that
// is ducked under the speech, over the video. The picture is copied unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker {

public class NarrationSegment {
    public double Start, End; public string Text = "", Hint;
    public double Length { get { return End - Start; } }
}

public class NarrationScript {
    public string Language; public double CharsPerSecond; public double VideoDuration;
    public List<NarrationSegment> Segments = new List<NarrationSegment>();

    public static NarrationScript Load(string file) {
        if (!File.Exists(file)) throw new Exception("narration script not found: " + file);
        var o = Json.ReadFile(file);
        var s = new NarrationScript { Language = o.Str("language"), CharsPerSecond = o.Num("chars_per_second", 0), VideoDuration = o.Num("video_duration", 0) };
        var segs = o.Arr("segments");
        if (segs == null) throw new Exception(file + ": 'segments' is missing");
        foreach (object x in segs) {
            var j = x as JO; if (j == null) continue;
            s.Segments.Add(new NarrationSegment { Start = j.Num("start", 0), End = j.Num("end", 0), Text = (j.Str("text") ?? "").Trim(), Hint = j.Str("hint") });
        }
        return s;
    }

    public JO ToJson(string video, string instructions) {
        var l = new List<object>();
        foreach (var g in Segments) l.Add(new JO("start", Math.Round(g.Start, 2), "end", Math.Round(g.End, 2), "budget_chars", Narration.Budget(g, CharsPerSecond), "hint", g.Hint, "text", g.Text));
        return new JO("format", "eqlmaker-narration", "version", 1, "language", Language, "chars_per_second", CharsPerSecond, "video", video,
            "video_duration", Math.Round(VideoDuration, 2), "instructions", instructions, "segments", l);
    }

    /// <summary>Problems that make the script unusable: order, overlaps, times outside the video.</summary>
    public List<string> Problems() {
        var p = new List<string>();
        for (int i = 0; i < Segments.Count; i++) {
            var g = Segments[i];
            if (g.End <= g.Start) p.Add("segment " + (i + 1) + ": end is not after start");
            if (i > 0 && g.Start < Segments[i - 1].End - 0.001) p.Add("segment " + (i + 1) + " starts before segment " + i + " ends");
            if (VideoDuration > 0 && g.Start >= VideoDuration) p.Add("segment " + (i + 1) + " starts after the end of the video");
        }
        return p;
    }
}

/// <summary>Speech synthesis of one voice. Check fails with a clear message when the service or the voice is missing.</summary>
public interface ITtsEngine {
    void Check();
    /// <summary>One request; returns a RIFF WAV file (16 bit PCM).</summary>
    byte[] Synthesize(string text);
    string Describe { get; }
}

/// <summary>Client of the HTTP interface of a local Fish Speech server (POST /v1/tts) with a registered voice reference.</summary>
public class FishSpeechTts : ITtsEngine {
    public string BaseUrl = "http://127.0.0.1:8080", ReferenceId; public int TimeoutMs = 900000;
    public JO Params = new JO("format", "wav", "chunk_length", 200, "normalize", true, "use_memory_cache", "on", "seed", 1996, "temperature", 0.7, "top_p", 0.7, "max_new_tokens", 1024);
    public string Describe { get { return "Fish Speech " + BaseUrl.TrimEnd('/') + ", voice reference " + ReferenceId; } }

    string Url(string path) { return BaseUrl.TrimEnd('/') + path; }

    static string Get(string url, int timeoutMs) {
        var r = (HttpWebRequest)WebRequest.Create(url); r.Method = "GET"; r.Accept = "application/json"; r.Timeout = timeoutMs; r.Proxy = null;
        using (var resp = (HttpWebResponse)r.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    public void Check() {
        if (string.IsNullOrEmpty(ReferenceId)) throw new Exception("no voice: give --voice <reference id> (a voice reference registered on the Fish Speech server) or narration.voices in eqlmaker.json");
        string health;
        try { health = Get(Url("/v1/health"), 10000); }
        catch (Exception e) { throw new Exception("Fish Speech is not reachable at " + BaseUrl + " (" + e.Message + "). Narration needs a running Fish Speech server; start it, or give its address with --tts-url."); }
        if (health.IndexOf("ok", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("Fish Speech at " + BaseUrl + " is not healthy: " + health);
        List<object> ids = null;
        try { var o = Json.ParseObject(Get(Url("/v1/references/list"), 10000)); ids = o.Arr("reference_ids"); }
        catch (Exception e) { throw new Exception("Fish Speech at " + BaseUrl + " did not list its voice references: " + e.Message); }
        bool found = false; if (ids != null) foreach (object x in ids) if (Convert.ToString(x) == ReferenceId) found = true;
        if (!found) throw new Exception("the voice reference '" + ReferenceId + "' is not registered on the Fish Speech server at " + BaseUrl + " (GET /v1/references/list). No other voice is used.");
    }

    public byte[] Synthesize(string text) {
        var body = new JO(); foreach (var kv in Params) body[kv.Key] = kv.Value;
        body["format"] = "wav"; body["streaming"] = false; body["text"] = text; body["reference_id"] = ReferenceId;
        byte[] data = new UTF8Encoding(false).GetBytes(Json.Write(body));
        Exception last = null;
        for (int attempt = 0; attempt < 2; attempt++) {
            try {
                var r = (HttpWebRequest)WebRequest.Create(Url("/v1/tts")); r.Method = "POST"; r.ContentType = "application/json; charset=utf-8"; r.Proxy = null;
                r.Timeout = TimeoutMs; r.ReadWriteTimeout = TimeoutMs; r.ContentLength = data.Length; r.ServicePoint.Expect100Continue = false;
                using (var s = r.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)r.GetResponse()) using (var ms = new MemoryStream()) {
                    resp.GetResponseStream().CopyTo(ms); var b = ms.ToArray();
                    if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new Exception("the answer is not a WAV file");
                    return b;
                }
            } catch (WebException e) {
                string detail = e.Message; var resp = e.Response as HttpWebResponse;
                if (resp != null) { try { using (var sr = new StreamReader(resp.GetResponseStream())) detail = "HTTP " + (int)resp.StatusCode + ": " + sr.ReadToEnd(); } catch { } if ((int)resp.StatusCode < 500) throw new Exception("Fish Speech refused the request: " + Cut(detail)); }
                last = new Exception("Fish Speech request failed: " + Cut(detail));
            }
        }
        throw last;
    }
    static string Cut(string s) { return s.Length > 300 ? s.Substring(0, 300) : s; }
}

/// <summary>16 bit PCM mono sample buffers.</summary>
public class Pcm {
    public int Rate; public short[] Samples;
    public double Seconds { get { return Rate == 0 ? 0 : Samples.Length / (double)Rate; } }

    public static Pcm FromWav(byte[] b) {
        if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new Exception("not a WAV file");
        int pos = 12, channels = 0, rate = 0, bits = 0, dataPos = -1, dataLen = 0;
        while (pos + 8 <= b.Length) {
            string id = Encoding.ASCII.GetString(b, pos, 4); int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { channels = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
            else if (id == "data") { dataPos = pos + 8; dataLen = (len < 0 || dataPos + len > b.Length) ? b.Length - dataPos : len; break; }
            pos += 8 + len + (len & 1);
        }
        if (dataPos < 0 || bits != 16 || channels < 1) throw new Exception("unsupported WAV file (16 bit PCM expected)");
        int frames = dataLen / (2 * channels); var s = new short[frames];
        for (int i = 0; i < frames; i++) {
            int sum = 0; for (int c = 0; c < channels; c++) sum += BitConverter.ToInt16(b, dataPos + 2 * (i * channels + c));
            s[i] = (short)(sum / channels);
        }
        return new Pcm { Rate = rate, Samples = s };
    }

    public byte[] ToWav() {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        int n = Samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n);
        foreach (var x in Samples) w.Write(x);
        w.Flush(); return ms.ToArray();
    }

    /// <summary>Removes silence at both ends (below the threshold), keeps a margin.</summary>
    public Pcm Trim(double thresholdDbfs, int padMs) {
        int limit = (int)(32767 * Math.Pow(10, thresholdDbfs / 20.0)), a = 0, e = Samples.Length;
        while (a < e && Math.Abs((int)Samples[a]) < limit) a++;
        while (e > a && Math.Abs((int)Samples[e - 1]) < limit) e--;
        int pad = Rate * padMs / 1000; a = Math.Max(0, a - pad); e = Math.Min(Samples.Length, e + pad);
        var r = new short[Math.Max(0, e - a)]; Array.Copy(Samples, a, r, 0, r.Length);
        return new Pcm { Rate = Rate, Samples = r };
    }

    /// <summary>Lowers the level when the peak is above the limit; never raises it.</summary>
    public void LimitPeak(double maxDbfs) {
        int peak = 0; foreach (var x in Samples) { int v = Math.Abs((int)x); if (v > peak) peak = v; }
        int limit = (int)(32767 * Math.Pow(10, maxDbfs / 20.0));
        if (peak <= limit) return;
        double f = limit / (double)peak; for (int i = 0; i < Samples.Length; i++) Samples[i] = (short)(Samples[i] * f);
    }

    public static Pcm Join(List<Pcm> parts, List<double> pausesAfter) {
        int rate = 0; var l = new List<short>();
        for (int i = 0; i < parts.Count; i++) {
            if (rate == 0) rate = parts[i].Rate; else if (parts[i].Rate != rate) throw new Exception("speech parts with different sample rates");
            l.AddRange(parts[i].Samples);
            l.AddRange(new short[(int)(rate * pausesAfter[i])]);
        }
        return new Pcm { Rate = rate, Samples = l.ToArray() };
    }
}

/// <summary>Where a spoken clip lands in the video.</summary>
public class PlacedClip {
    public int Segment; public double Start, Duration, Tempo = 1, Room; public string Problem; public int CutChars;
    public double End { get { return Start + Duration / Tempo; } }
}

public class NarrateOptions {
    public string Video, Script, Out, Voice, Language, Music, TtsUrl, WorkDir;
    public double MusicVolume = 30, MaxTempo = 1.1, MinGap = 0.15, CharsPerSecond = 0, FadeIn = 1.5, FadeOut = 3;
    public bool NoDuck, Force;
}

public static class Narration {
    public const double DefaultCharsPerSecond = 14.0;
    public const int MaxCharsPerRequest = 500;
    public const double ParagraphPause = 0.45, SentencePause = 0.2, TrimDbfs = -45, PeakDbfs = -1;
    public const int TrimPadMs = 60;

    public const string Instructions = "Write the spoken text of every segment into 'text'. Describe what the viewer sees at that moment and why it matters; keep speaking through slow passages with fitting background information, so that the speech flows. Keep each text at or below 'budget_chars' (characters the voice speaks in the segment). Leave 'text' empty for silence. Times and the order of the segments may be changed; segments must not overlap. Then: eqlmaker narrate --video <file> --script <this file> --voice <reference id> --out <file>.";

    /// <summary>Characters that fit into a segment: its length minus a short breath, at the speaking rate of the voice.</summary>
    public static int Budget(NarrationSegment g, double cps) {
        if (cps <= 0) cps = DefaultCharsPerSecond;
        return Math.Max(0, (int)Math.Floor((g.Length - 0.35) * cps));
    }

    // ------------------------------------------------------------ draft
    /// <summary>Segments from a caption timeline: gaps go to the previous caption, the first segment starts at 0,
    /// the last one ends with the video; segments longer than maxSegment are split into equal parts,
    /// captions shorter than minSegment are merged into the previous segment.</summary>
    public static List<NarrationSegment> Draft(List<TranscriptSegment> captions, double videoDuration, double maxSegment, double minSegment) {
        var caps = new List<TranscriptSegment>(captions); caps.Sort(delegate (TranscriptSegment a, TranscriptSegment b) { return a.From.CompareTo(b.From); });
        var merged = new List<NarrationSegment>();
        foreach (var c in caps) {
            if (videoDuration > 0 && c.From >= videoDuration) break;
            var g = new NarrationSegment { Start = c.From, End = c.To, Hint = c.Text };
            if (merged.Count > 0 && (g.Length < minSegment || g.Start < merged[merged.Count - 1].End - 0.001)) {
                var p = merged[merged.Count - 1]; p.End = Math.Max(p.End, g.End); p.Hint = p.Hint + " / " + g.Hint; continue;
            }
            merged.Add(g);
        }
        if (merged.Count == 0) {
            if (videoDuration <= 0) throw new Exception("the caption timeline is empty and the length of the video is unknown");
            merged.Add(new NarrationSegment { Start = 0, End = videoDuration, Hint = "" });
        }
        merged[0].Start = 0;
        for (int i = 0; i + 1 < merged.Count; i++) merged[i].End = merged[i + 1].Start;
        if (videoDuration > 0) merged[merged.Count - 1].End = videoDuration;
        var r = new List<NarrationSegment>();
        foreach (var g in merged) {
            int n = maxSegment > 0 ? (int)Math.Ceiling(g.Length / maxSegment - 1e-9) : 1; if (n < 1) n = 1;
            double step = g.Length / n;
            for (int k = 0; k < n; k++)
                r.Add(new NarrationSegment { Start = Math.Round(g.Start + k * step, 2), End = Math.Round(k == n - 1 ? g.End : g.Start + (k + 1) * step, 2), Hint = n == 1 ? g.Hint : g.Hint + " (part " + (k + 1) + " of " + n + ")" });
        }
        return r;
    }

    /// <summary>Reads a caption timeline: SRT, WebVTT, whisper JSON, or JSON {"segments":[{start,end,text,notes?}]}.</summary>
    public static List<TranscriptSegment> ReadCaptions(string file, out double duration) {
        duration = 0;
        if (!File.Exists(file)) throw new Exception("caption timeline not found: " + file);
        string text = File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿');
        if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("[")) {
            object root = Json.Parse(text); List<object> arr = root as List<object>;
            var o = root as JO;
            if (o != null) { duration = o.Num("duration", 0); arr = o.Arr("segments") ?? o.Arr("captions"); if (arr == null && o.Has("transcription")) return Transcript.Read(file); }
            if (arr == null) throw new Exception(file + ": no 'segments'");
            var l = new List<TranscriptSegment>();
            foreach (object x in arr) {
                var j = x as JO; if (j == null) continue;
                string t = (j.Str("text") ?? "").Trim(); var notes = j.Strings("notes");
                if (notes != null && notes.Count > 0) t += " [" + string.Join("; ", notes.ToArray()) + "]";
                l.Add(new TranscriptSegment { From = j.Num("start", 0), To = j.Num("end", 0), Text = t });
            }
            return l;
        }
        return Transcript.Read(file);
    }

    public static JO DraftFile(Config cfg, string captionsFile, string video, string language, double cps, double maxSegment, double minSegment, string outFile, bool force) {
        double duration; var caps = ReadCaptions(captionsFile, out duration);
        if (!string.IsNullOrEmpty(video)) duration = Media.Probe(cfg, video).Num("duration", duration);
        if (cps <= 0) cps = DefaultCharsPerSecond;
        var s = new NarrationScript { Language = language, CharsPerSecond = cps, VideoDuration = duration, Segments = Draft(caps, duration, maxSegment, minSegment) };
        if (string.IsNullOrEmpty(outFile)) outFile = Path.ChangeExtension(captionsFile, null) + ".narration.json";
        outFile = Path.GetFullPath(outFile);
        if (File.Exists(outFile) && !force) throw new Exception(outFile + " exists already (--force replaces it)");
        Json.WriteFile(outFile, s.ToJson(string.IsNullOrEmpty(video) ? null : Path.GetFullPath(video), Instructions));
        int budget = 0; foreach (var g in s.Segments) budget += Budget(g, cps);
        return new JO("script", outFile, "segments", s.Segments.Count, "video_duration", Math.Round(duration, 2), "chars_per_second", cps, "budget_chars", budget,
            "next", "write 'text' of every segment (at most budget_chars), then: eqlmaker narrate --video <file> --script \"" + outFile + "\" --voice <reference id> --out <file>");
    }

    // ------------------------------------------------------------ text
    /// <summary>Splits a text at paragraphs, then at sentences (then commas) into requests of at most maxChars,
    /// each with the pause that follows it.</summary>
    public static List<KeyValuePair<string, double>> SplitText(string text, int maxChars) {
        var r = new List<KeyValuePair<string, double>>();
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        var paras = new List<string>(); foreach (var p in Regex.Split(text, @"\n\s*\n")) if (p.Trim() != "") paras.Add(Regex.Replace(p.Trim(), @"\s*\n\s*", " "));
        for (int pi = 0; pi < paras.Count; pi++) {
            var pieces = SplitLong(paras[pi], maxChars);
            for (int i = 0; i < pieces.Count; i++) {
                bool lastInPara = i == pieces.Count - 1, last = lastInPara && pi == paras.Count - 1;
                r.Add(new KeyValuePair<string, double>(pieces[i], last ? 0 : lastInPara ? ParagraphPause : SentencePause));
            }
        }
        return r;
    }
    static List<string> SplitLong(string para, int max) {
        if (para.Length <= max) return new List<string> { para };
        return Pack(Regex.Split(para, @"(?<=[.!?…])\s+"), max, true);
    }
    static List<string> Pack(string[] parts, int max, bool sentences) {
        var outl = new List<string>(); string cur = "";
        foreach (var part0 in parts) {
            var subs = new List<string>();
            if (part0.Length <= max) subs.Add(part0);
            else if (sentences) subs.AddRange(Pack(Regex.Split(part0, @"(?<=[,;:])\s+"), max, false));
            else { string p = part0; while (p.Length > max) { int cut = p.LastIndexOf(' ', max); if (cut <= 0) cut = max; subs.Add(p.Substring(0, cut).Trim()); p = p.Substring(cut).Trim(); } if (p != "") subs.Add(p); }
            foreach (var s in subs) {
                if (cur != "" && cur.Length + 1 + s.Length > max) { outl.Add(cur); cur = s; }
                else cur = (cur + " " + s).Trim();
            }
        }
        if (cur != "") outl.Add(cur);
        return outl;
    }

    /// <summary>Speaks a text with as many requests as needed; silence trimmed, pauses between the parts, peak limited.</summary>
    public static Pcm Speak(ITtsEngine tts, string text) {
        var parts = SplitText(text, MaxCharsPerRequest); var pcm = new List<Pcm>(); var pauses = new List<double>();
        foreach (var kv in parts) { pcm.Add(Pcm.FromWav(tts.Synthesize(kv.Key)).Trim(TrimDbfs, TrimPadMs)); pauses.Add(kv.Value); }
        var r = Pcm.Join(pcm, pauses);
        if (r.Samples.Length == 0) throw new Exception("the speech synthesis returned no sound for: " + text);
        r.LimitPeak(PeakDbfs);
        return r;
    }

    // ------------------------------------------------------------ fitting
    /// <summary>Places the clips: each starts at its segment (or after the previous clip and a short gap) and must
    /// end before the next segment with text starts (or the video ends). A clip that is too long is sped up,
    /// at most to maxTempo; beyond that it gets a problem with the number of characters to cut.</summary>
    public static List<PlacedClip> Fit(List<NarrationSegment> segs, double[] durations, double videoEnd, double maxTempo, double minGap, double cps) {
        if (cps <= 0) cps = DefaultCharsPerSecond;
        var r = new List<PlacedClip>(); double prevEnd = double.NegativeInfinity;
        var spoken = new List<int>(); for (int i = 0; i < segs.Count; i++) if (durations[i] > 0) spoken.Add(i);
        for (int k = 0; k < spoken.Count; k++) {
            int i = spoken[k]; var g = segs[i];
            double limit = k + 1 < spoken.Count ? segs[spoken[k + 1]].Start : (videoEnd > 0 ? videoEnd : g.End);
            var c = new PlacedClip { Segment = i, Duration = durations[i], Start = Math.Max(g.Start, prevEnd + minGap) };
            c.Room = limit - c.Start;
            if (c.Room <= 0) { c.Problem = "no room left: the previous clip runs up to " + prevEnd.ToString("0.00", CultureInfo.InvariantCulture) + " s"; c.Tempo = maxTempo; c.CutChars = (int)Math.Ceiling(c.Duration * cps); }
            else if (c.Duration > c.Room) {
                c.Tempo = c.Duration / c.Room;
                if (c.Tempo > maxTempo + 1e-9) { c.Problem = "too long"; c.CutChars = (int)Math.Ceiling((c.Duration / maxTempo - c.Room) * cps); c.Tempo = maxTempo; }
            }
            c.Tempo = Math.Round(c.Tempo, 4);
            prevEnd = c.End; r.Add(c);
        }
        return r;
    }

    /// <summary>Share of the video with speech and the longest pause between clips.</summary>
    public static JO Coverage(List<PlacedClip> clips, double videoEnd) {
        double speech = 0, maxGap = 0, prev = 0; bool overlap = false;
        foreach (var c in clips) { speech += c.Duration / c.Tempo; maxGap = Math.Max(maxGap, c.Start - prev); if (c.Start < prev - 0.001) overlap = true; prev = c.End; }
        if (videoEnd > 0) maxGap = Math.Max(maxGap, videoEnd - prev);
        return new JO("speech_seconds", Math.Round(speech, 2), "coverage_percent", videoEnd > 0 ? Math.Round(100 * speech / videoEnd, 1) : 0, "longest_pause_seconds", Math.Round(maxGap, 2), "overlaps", overlap);
    }

    // ------------------------------------------------------------ mixing
    /// <summary>FFmpeg filter graph: input 1 is the speech track, input 2 (optional) the looped music.</summary>
    public static string MixFilter(bool music, double duration, double musicVolumePercent, bool duck, double fadeIn, double fadeOut) {
        var ci = CultureInfo.InvariantCulture; string d = duration.ToString("0.###", ci);
        string fmt = "aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo";
        if (!music) return "[1:a]" + fmt + ",atrim=0:" + d + ",apad=whole_dur=" + d + "[a]";
        double fo = Math.Min(fadeOut, duration / 3), fi = Math.Min(fadeIn, duration / 3);
        string m = "[2:a]" + fmt + ",atrim=0:" + d + ",asetpts=N/SR/TB,volume=" + (musicVolumePercent / 100.0).ToString("0.####", ci)
            + ",afade=t=in:st=0:d=" + fi.ToString("0.###", ci) + ",afade=t=out:st=" + (duration - fo).ToString("0.###", ci) + ":d=" + fo.ToString("0.###", ci);
        string s = "[1:a]" + fmt + ",atrim=0:" + d + ",apad=whole_dur=" + d;
        if (!duck) return m + "[m];" + s + "[s];[s][m]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95:level=0:latency=1[a]";
        return m + "[m];" + s + ",asplit=2[s][k];[m][k]sidechaincompress=threshold=0.03:ratio=3:attack=30:release=600:makeup=1[md];[s][md]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95:level=0:latency=1[a]";
    }

    static string Q(string s) { return "\"" + s + "\""; }
    static string Hash(string s) { using (var h = SHA1.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 16).ToLowerInvariant(); }

    static Pcm Tempo(Config cfg, Pcm p, double tempo, string dir) {
        string a = Path.Combine(dir, "tempo-in.wav"), b = Path.Combine(dir, "tempo-out.wav"), o, e;
        File.WriteAllBytes(a, p.ToWav());
        int code = Media.Run(Media.Find(cfg, "ffmpeg"), "-hide_banner -loglevel error -y -i " + Q(a) + " -filter:a atempo=" + tempo.ToString("0.####", CultureInfo.InvariantCulture) + " -ar " + p.Rate + " -ac 1 -c:a pcm_s16le " + Q(b), null, out o, out e, 300000);
        if (code != 0) throw new Exception("FFmpeg could not change the tempo: " + e.Trim());
        var r = Pcm.FromWav(File.ReadAllBytes(b)); File.Delete(a); File.Delete(b); return r;
    }

    /// <summary>Speaks the script, fits the clips, mixes speech (and music) and writes the video with the new sound.</summary>
    public static JO Run(Config cfg, NarrateOptions o, ITtsEngine tts) {
        if (string.IsNullOrEmpty(o.Video) || !File.Exists(o.Video)) throw new Exception("video not found: " + o.Video);
        if (string.IsNullOrEmpty(o.Out)) throw new Exception("give the output file with --out");
        string video = Path.GetFullPath(o.Video), outFile = Path.GetFullPath(o.Out);
        if (string.Equals(video, outFile, StringComparison.OrdinalIgnoreCase)) throw new Exception("--out must not be the input video");
        if (File.Exists(outFile) && !o.Force) throw new Exception(outFile + " exists already (--force replaces it)");
        if (!string.IsNullOrEmpty(o.Music) && !File.Exists(o.Music)) throw new Exception("music file not found: " + o.Music);
        if (o.MusicVolume < 0 || o.MusicVolume > 200) throw new Exception("--music-volume is a percentage from 0 to 200");
        if (o.MaxTempo < 1 || o.MaxTempo > 1.5) throw new Exception("--max-tempo must be between 1.0 and 1.5");
        Media.RequireFfmpeg(cfg);
        var info = Media.Probe(cfg, video); double dur = info.Num("duration", 0);
        if (dur <= 0) throw new Exception("the length of the video could not be read: " + video);
        var script = NarrationScript.Load(o.Script);
        if (script.VideoDuration <= 0) script.VideoDuration = dur;
        var problems = script.Problems(); if (problems.Count > 0) throw new Exception("narration script: " + string.Join("; ", problems.ToArray()));
        double cps = o.CharsPerSecond > 0 ? o.CharsPerSecond : script.CharsPerSecond > 0 ? script.CharsPerSecond : DefaultCharsPerSecond;
        tts.Check();

        string work = o.WorkDir ?? Path.Combine(Path.GetDirectoryName(outFile), Path.GetFileNameWithoutExtension(outFile) + ".narration");
        string clipsDir = Path.Combine(work, "clips"); Directory.CreateDirectory(clipsDir);
        var segs = script.Segments; var durations = new double[segs.Count]; var clips = new Pcm[segs.Count]; int made = 0, cached = 0;
        for (int i = 0; i < segs.Count; i++) {
            if (segs[i].Text == "") continue;
            string f = Path.Combine(clipsDir, Hash(tts.Describe + "\n" + segs[i].Text) + ".wav");
            if (File.Exists(f)) { clips[i] = Pcm.FromWav(File.ReadAllBytes(f)); cached++; }
            else { clips[i] = Speak(tts, segs[i].Text); File.WriteAllBytes(f, clips[i].ToWav()); made++; }
            durations[i] = clips[i].Seconds;
        }
        var placed = Fit(segs, durations, dur, o.MaxTempo, o.MinGap, cps);
        var list = new List<object>(); var bad = new List<string>(); var ci = CultureInfo.InvariantCulture;
        foreach (var c in placed) {
            var g = segs[c.Segment];
            list.Add(new JO("segment", c.Segment + 1, "segment_start", g.Start, "segment_end", g.End, "chars", g.Text.Length, "speech_seconds", Math.Round(c.Duration, 2),
                "start", Math.Round(c.Start, 2), "end", Math.Round(c.End, 2), "tempo", c.Tempo, "measured_chars_per_second", Math.Round(g.Text.Length / c.Duration, 1), "problem", c.Problem, "cut_chars", c.Problem == null ? 0 : c.CutChars));
            if (c.Problem != null) bad.Add("segment " + (c.Segment + 1) + " (" + g.Start.ToString("0.0", ci) + "-" + g.End.ToString("0.0", ci) + " s): speech " + c.Duration.ToString("0.0", ci) + " s, room " + c.Room.ToString("0.0", ci) + " s, " + c.Problem + "; shorten the text by about " + c.CutChars + " characters");
        }
        var cov = Coverage(placed, dur);
        var report = new JO("video", video, "out", outFile, "voice", tts.Describe, "video_duration", Math.Round(dur, 2), "chars_per_second", cps, "clips_synthesized", made, "clips_from_cache", cached,
            "coverage", cov, "music", string.IsNullOrEmpty(o.Music) ? null : Path.GetFullPath(o.Music), "music_volume_percent", string.IsNullOrEmpty(o.Music) ? (object)null : o.MusicVolume, "ducking", !o.NoDuck, "clips", list);
        string reportFile = Path.Combine(work, "report.json");
        if (bad.Count > 0) { report["result"] = "too long"; Json.WriteFile(reportFile, report); throw new Exception("the speech does not fit (report: " + reportFile + "):\n  " + string.Join("\n  ", bad.ToArray())); }

        int rate = 0; foreach (var p in clips) if (p != null) { rate = p.Rate; break; }
        if (rate == 0) throw new Exception("the narration script has no text");
        var track = new int[(int)Math.Ceiling(dur * rate)];
        foreach (var c in placed) {
            var p = clips[c.Segment]; if (p.Rate != rate) throw new Exception("clips with different sample rates");
            if (c.Tempo > 1.0005) p = Tempo(cfg, p, c.Tempo, work);
            int at = (int)Math.Round(c.Start * rate);
            for (int k = 0; k < p.Samples.Length && at + k < track.Length; k++) track[at + k] += p.Samples[k];
        }
        var speech = new short[track.Length]; for (int k = 0; k < track.Length; k++) speech[k] = (short)Math.Max(-32768, Math.Min(32767, track[k]));
        string speechWav = Path.Combine(work, "speech.wav"); File.WriteAllBytes(speechWav, new Pcm { Rate = rate, Samples = speech }.ToWav());

        bool music = !string.IsNullOrEmpty(o.Music);
        string filterFile = Path.Combine(work, "mix.filter"); File.WriteAllText(filterFile, MixFilter(music, dur, o.MusicVolume, !o.NoDuck, o.FadeIn, o.FadeOut), new UTF8Encoding(false));
        string part = outFile + ".part" + Path.GetExtension(outFile), so, se;
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        string args = "-hide_banner -loglevel error -y -i " + Q(video) + " -i " + Q(speechWav) + (music ? " -stream_loop -1 -i " + Q(Path.GetFullPath(o.Music)) : "")
            + " -filter_complex " + Q(File.ReadAllText(filterFile)) + " -map 0:v:0 -map [a] -c:v copy -c:a aac -b:a 192k -ar 44100 -t " + dur.ToString("0.###", ci) + " -movflags +faststart " + Q(part);
        int code = Media.Run(Media.Find(cfg, "ffmpeg"), args, null, out so, out se, 3600000);
        if (code != 0 || !File.Exists(part)) throw new Exception("FFmpeg could not mix the sound: " + se.Trim());
        if (File.Exists(outFile)) File.Delete(outFile);
        File.Move(part, outFile);
        var check = Media.Probe(cfg, outFile);
        report["result"] = "ok"; report["out_duration"] = Math.Round(check.Num("duration", 0), 2); report["out_has_audio"] = check.Bool("audio", false); report["speech_track"] = speechWav;
        Json.WriteFile(reportFile, report);
        return new JO("video", outFile, "duration", report["out_duration"], "audio", report["out_has_audio"], "coverage_percent", cov["coverage_percent"], "longest_pause_seconds", cov["longest_pause_seconds"],
            "clips", placed.Count, "synthesized", made, "from_cache", cached, "report", reportFile);
    }

    /// <summary>Options of the command line or the MCP tool, with the defaults of eqlmaker.json (narration.*).</summary>
    public static ITtsEngine Engine(Config cfg, NarrateOptions o, NarrationScript script) {
        var n = cfg.Raw.Obj("narration") ?? new JO();
        string lang = o.Language ?? (script == null ? null : script.Language);
        string voice = o.Voice;
        if (string.IsNullOrEmpty(voice) && n.Obj("voices") != null && lang != null) voice = n.Obj("voices").Str(lang);
        if (string.IsNullOrEmpty(voice)) voice = n.Str("voice");
        return new FishSpeechTts { BaseUrl = o.TtsUrl ?? n.Str("ttsUrl", "http://127.0.0.1:8080"), ReferenceId = voice };
    }

    public static JO RunWithConfig(Config cfg, NarrateOptions o) {
        if (string.IsNullOrEmpty(o.Script)) throw new Exception("give the narration script with --script (made by eqlmaker narrate-draft)");
        var n = cfg.Raw.Obj("narration");
        if (o.CharsPerSecond <= 0 && n != null && n.Obj("charsPerSecond") != null) {
            var s0 = NarrationScript.Load(o.Script); string lang = o.Language ?? s0.Language;
            if (lang != null) o.CharsPerSecond = n.Obj("charsPerSecond").Num(lang, 0);
        }
        return Run(cfg, o, Engine(cfg, o, NarrationScript.Load(o.Script)));
    }
}
}
