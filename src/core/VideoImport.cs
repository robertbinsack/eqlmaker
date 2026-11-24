// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// video-import: a narrated screen recording (MP4 with the time stamp strip in the picture) that
// arrives later than the database events. Steps, all by fixed rules:
//   1. read the time stamps from sampled frames (pattern first, digits as fallback)
//   2. clock mapping video time -> wall clock, with drift and cuts (cuts located frame-exactly)
//   3. key frames (the picture changed and came to rest) and the moments of change
//   4. transcript of the sound track (speech recogniser, or a transcript file)
//   5. database events of the same period, grouped into operations and matched to the specification
//   6. findings per operation: what was said, what was seen, what the database did that the
//      specification does not contain yet. Judging the findings is the guided step "enrich".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace EqlMaker {

public class VideoImportOptions {
    public string File, Name, TranscriptFile, Language;
    public double SampleFps = 2; public int MaxKeyframes = 120;
    public List<string> Recordings = new List<string>();
    /// <summary>Clock of the database server minus clock of the recording machine, in milliseconds (when known).</summary>
    public double? ClockOffsetMs;
    public bool EstimateOffset, NoTranscript;
    public double MaxShiftSeconds = 900, AttachSeconds = 8, OperationGapSeconds = 2;
}

public static class VideoImport {
    const int TW = 32, TH = 18;

    static byte[] Thumb(byte[] l, int w, int h, Rectangle skip) {
        var t = new byte[TW * TH];
        for (int ty = 0; ty < TH; ty++) for (int tx = 0; tx < TW; tx++) {
            int x0 = tx * w / TW, x1 = (tx + 1) * w / TW, y0 = ty * h / TH, y1 = (ty + 1) * h / TH; long sum = 0; int n = 0;
            for (int y = y0; y < y1; y += 3) for (int x = x0; x < x1; x += 3) { if (skip.Contains(x, y)) continue; sum += l[y * w + x]; n++; }
            t[ty * TW + tx] = (byte)(n == 0 ? 0 : sum / n);
        }
        return t;
    }
    static double Diff(byte[] a, byte[] b) { long s = 0; for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]); return (double)s / a.Length; }

    public static JO Run(Config cfg, VideoImportOptions o) {
        Media.RequireFfmpeg(cfg);
        string file = Path.GetFullPath(o.File);
        if (!File.Exists(file)) throw new Exception("video not found: " + file);
        string name = string.IsNullOrEmpty(o.Name) ? Spec.Slug(Path.GetFileNameWithoutExtension(file)) : o.Name;
        string outDir = cfg.Sub("findings", name);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(Path.Combine(outDir, "keyframes"));
        var warnings = new List<object>();

        // ---- 1. time stamps of sampled frames
        var probe = Media.Probe(cfg, file);
        int w = probe.Int("width", 0), h = probe.Int("height", 0); double fps = probe.Num("fps", 0), duration = probe.Num("duration", 0);
        if (fps <= 0) fps = 30;
        int every = Math.Max(1, (int)Math.Round(fps / o.SampleFps));
        var samples = new List<ClockSample>(); var times = new List<double>(); var thumbs = new List<byte[]>();
        var hint = Rectangle.Empty; int nPattern = 0, nDigits = 0, nNone = 0; int offsetMinutes = int.MinValue; long monoBase = long.MinValue; bool clockStep = false;
        Media.ReadFrames(cfg, file, "not(mod(n\\," + every + "))", w, h, delegate (double t, byte[] luma, int fw, int fh) {
            var v = Timecode.Read(luma, fw, fh, ref hint);
            if (v == null) nNone++;
            else {
                if (v.FromDigits) nDigits++; else nPattern++;
                samples.Add(new ClockSample { Video = t, WallUs = v.UtcMs * 1000, FromDigits = v.FromDigits });
                offsetMinutes = v.OffsetMinutes;
                if (!v.FromDigits && v.MonoMs >= 0) {   // wall clock minus monotonic clock stays constant unless the wall clock was set
                    long d = v.UtcMs - v.MonoMs;
                    if (monoBase == long.MinValue) monoBase = d; else if (Math.Abs(d - monoBase) > 250 && Math.Abs(d - monoBase) < 3600L * 1000 * 24 * 40) clockStep = true;
                }
            }
            times.Add(t); thumbs.Add(Thumb(luma, fw, fh, hint.IsEmpty ? Rectangle.Empty : Rectangle.Inflate(hint, 4, 4)));
        });
        if (samples.Count == 0) throw new Exception("no time stamp was found in " + times.Count + " sampled frames: the video must show the strip of eqlmaker-timestamp.exe (docs/timestamp-overlay.md)");
        if (clockStep) warnings.Add("the wall clock of the recording machine was set during the recording (wall clock and monotonic clock disagree); times after that moment follow the new wall clock");
        if (nNone > 0) warnings.Add(nNone + " of " + times.Count + " sampled frames show no readable time stamp (strip covered, faded or outside the picture)");

        // ---- 2. clock mapping, cuts located frame-exactly
        double cutThreshold = Math.Max(0.25, 3 / fps);
        var map = ClockMap.Fit(samples, cutThreshold);
        if (map.Cuts.Count > 0) {
            var sorted = new List<ClockSample>(samples); sorted.Sort(delegate (ClockSample a, ClockSample b) { return a.Video.CompareTo(b.Video); });
            var ranges = new List<double[]>(); var expr = new StringBuilder();
            foreach (double c in map.Cuts) {
                double lo = 0, hi = duration;
                foreach (var s in sorted) { if (s.Video <= c) lo = s.Video; else { hi = s.Video; break; } }
                ranges.Add(new[] { lo, hi });
                if (expr.Length > 0) expr.Append('+');
                expr.Append("between(t\\," + lo.ToString("0.####", CultureInfo.InvariantCulture) + "\\," + hi.ToString("0.####", CultureInfo.InvariantCulture) + ")");
            }
            var fine = new List<ClockSample>(); var hint2 = hint;
            Media.ReadFrames(cfg, file, expr.ToString(), w, h, delegate (double t, byte[] luma, int fw, int fh) {
                var v = Timecode.Read(luma, fw, fh, ref hint2);
                if (v != null) fine.Add(new ClockSample { Video = t, WallUs = v.UtcMs * 1000 });
            });
            for (int i = 0; i < ranges.Count; i++) {
                // the first frame of the range whose time does not fit the stretch before the cut
                var before = map.Segments[i];
                foreach (var f in fine) {
                    if (f.Video < ranges[i][0] - 1e-6 || f.Video > ranges[i][1] + 1e-6) continue;
                    double expected = before.A + before.B * f.Video, got = (f.WallUs - map.BaseUs) / 1e6;
                    if (Math.Abs(got - expected) > cutThreshold) { map.RefineCut(i, f.Video); break; }
                }
            }
        }

        // ---- 3. moments of change and key frames
        var changes = new List<double>(); var keyTimes = new List<double>();
        byte[] lastKey = null; const double ChangeLevel = 2.0;
        for (int i = 0; i < thumbs.Count; i++) {
            bool changed = i > 0 && Diff(thumbs[i], thumbs[i - 1]) > ChangeLevel;
            if (changed) changes.Add(times[i]);
            bool stable = i + 1 >= thumbs.Count || Diff(thumbs[i + 1], thumbs[i]) <= ChangeLevel;
            if (stable && (lastKey == null || Diff(thumbs[i], lastKey) > ChangeLevel)) { keyTimes.Add(times[i]); lastKey = thumbs[i]; }
        }
        if (keyTimes.Count > o.MaxKeyframes) {
            warnings.Add(keyTimes.Count + " key frames found, only " + o.MaxKeyframes + " kept (evenly thinned); raise --max-keyframes to keep more");
            var thin = new List<double>(); for (int i = 0; i < o.MaxKeyframes; i++) thin.Add(keyTimes[(int)((long)i * keyTimes.Count / o.MaxKeyframes)]);
            keyTimes = thin;
        }
        var keyframes = new List<JO>();
        for (int i = 0; i < keyTimes.Count; i++) {
            string rel = "keyframes/k" + (i + 1).ToString("000") + ".png";
            Media.ExtractFrame(cfg, file, keyTimes[i], Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            keyframes.Add(new JO("id", "k" + (i + 1), "video", Math.Round(keyTimes[i], 3), "wall_us", map.ToWallUs(keyTimes[i]), "file", rel));
        }

        // ---- 4. transcript
        var segs = new List<TranscriptSegment>(); string transcriptTool = null;
        if (!string.IsNullOrEmpty(o.TranscriptFile)) { segs = Transcript.Read(o.TranscriptFile); transcriptTool = "file " + Path.GetFileName(o.TranscriptFile); }
        else if (!o.NoTranscript) {
            if (!probe.Bool("audio", false)) warnings.Add("the video has no sound track: no transcript");
            else {
                string wav = Path.Combine(outDir, "audio.wav");
                Media.ExtractAudio(cfg, file, wav);
                var r = Transcript.Recognise(cfg, wav, o.Language, out transcriptTool);
                if (r == null) warnings.Add("no speech recogniser installed (tools\\whisper of the package, or tools.transcribe): no transcript; a transcript file can be given with --transcript");
                else segs = r;
                try { File.Delete(wav); } catch { }
            }
        }
        var transcript = new List<JO>();
        for (int i = 0; i < segs.Count; i++) transcript.Add(new JO("id", "t" + (i + 1), "video_from", Math.Round(segs[i].From, 3), "video_to", Math.Round(segs[i].To, 3), "wall_us", map.ToWallUs(segs[i].From), "text", segs[i].Text));

        // ---- 5. database events of the period
        var recs = new List<RecordingData>();
        if (o.Recordings.Count > 0) foreach (var r in o.Recordings) recs.Add(RecordingData.Load(cfg.RecordingDir(r)));
        else if (Directory.Exists(cfg.RecordingsDir)) {
            var dirs = new List<string>(Directory.GetDirectories(cfg.RecordingsDir)); dirs.Sort(StringComparer.Ordinal);
            foreach (var d in dirs) if (File.Exists(Path.Combine(d, "events.jsonl")) && File.Exists(Path.Combine(d, "recording.json"))) recs.Add(RecordingData.Load(d));
        }
        JO spec = File.Exists(cfg.Sub("spec", "spec.json")) ? Json.ReadFile(cfg.Sub("spec", "spec.json")) : null;

        // clock of the server minus clock of the recording machine
        long videoWallFrom = map.ToWallUs(0), videoWallTo = map.ToWallUs(duration);
        var bursts = new List<double>(); long measuredSum = 0; int measuredN = 0;
        foreach (var r in recs) {
            var cap = r.Manifest.Obj("capture");
            if (cap != null && cap["server_minus_local_us"] != null) { measuredSum += cap.Long("server_minus_local_us", 0); measuredN++; }
            long last = long.MinValue;
            foreach (var e in r.Events) { if (e.Kind == "transaction") continue; if (e.Ts - last > (long)(o.OperationGapSeconds * 1e6)) bursts.Add((e.Ts - map.BaseUs) / 1e6); last = e.Ts; }
        }
        var changeWall = new List<double>(); foreach (double c in changes) changeWall.Add((map.ToWallUs(c) - map.BaseUs) / 1e6);
        double conf = 0; int matched = 0; double est = bursts.Count > 0 && changeWall.Count > 0 ? ClockMap.EstimateOffset(changeWall, bursts, o.MaxShiftSeconds, 0.25, out conf, out matched) : double.NaN;
        double offsetS; string offsetMethod;
        if (o.ClockOffsetMs.HasValue) { offsetS = o.ClockOffsetMs.Value / 1000; offsetMethod = "given"; }
        else if (o.EstimateOffset && !double.IsNaN(est)) { offsetS = est; offsetMethod = "estimated from screen changes and database events"; }
        else if (measuredN > 0) { offsetS = measuredSum / (double)measuredN / 1e6; offsetMethod = "measured when the events were captured (valid when the capture ran on the recording machine)"; }
        else if (!double.IsNaN(est)) { offsetS = est; offsetMethod = "estimated from screen changes and database events"; }
        else { offsetS = 0; offsetMethod = "none (clocks assumed equal)"; }
        if (!double.IsNaN(est) && offsetMethod.StartsWith("measured") && Math.Abs(est - offsetS) > 1.0 && conf >= 3)
            warnings.Add("the measured clock offset (" + (offsetS * 1000).ToString("0", CultureInfo.InvariantCulture) + " ms) and the offset estimated from the data (" + (est * 1000).ToString("0", CultureInfo.InvariantCulture) + " ms) differ: if the video was recorded on another machine than the capture, run again with --estimate-offset or --clock-offset-ms");
        long offsetUs = (long)Math.Round(offsetS * 1e6);

        // ---- operations: bursts of database events inside the period of the video
        var ops = new List<JO>(); var specShapes = new HashSet<string>();
        if (spec != null) foreach (JO q in spec.Arr("queries")) specShapes.Add(q.Str("shape"));
        foreach (var r in recs) {
            JO tl = null; var chapterOf = new Dictionary<int, string>();
            if (r.Actions.Count > 0) {
                tl = Timeline.Build(r);
                foreach (JO ch in tl.Arr("chapters")) foreach (JO a in ch.Arr("actions")) { foreach (JO s in a.Arr("statements")) chapterOf[s.Int("seq", 0)] = ch.Str("caption"); foreach (JO t in a.Arr("transactions")) chapterOf[t.Int("seq", 0)] = ch.Str("caption"); }
            }
            List<DbEvent> cur = null; long lastTs = long.MinValue;
            Action close = delegate () {
                if (cur == null || cur.Count == 0) return;
                long from = cur[0].Ts - offsetUs, to = cur[cur.Count - 1].Ts - offsetUs;   // on the clock of the recording machine
                if (to < videoWallFrom - 2000000 || from > videoWallTo + 2000000) return;
                double vFrom = map.ToVideo(from, 2), vTo = map.ToVideo(to, 2);
                var shapes = new List<string>(); var written = new List<string>(); var notInSpec = new List<object>(); int st = 0, tx = 0; string chapter = null;
                foreach (var e in cur) {
                    if (chapter == null && chapterOf.ContainsKey(e.Seq)) chapter = chapterOf[e.Seq];
                    if (e.Kind == "transaction") { tx++; foreach (var t in e.Tables) if (!written.Contains(t)) written.Add(t); continue; }
                    if (e.Sql == null) continue;
                    string verb = SqlText.Verb(e.Sql);
                    if (SqlText.IsTransactionControl(verb) || verb == "set" || verb == "show") continue;
                    st++; string sh = SqlText.Shape(e.Sql);
                    if (!shapes.Contains(sh)) { shapes.Add(sh); if (spec != null && !specShapes.Contains(sh)) notInSpec.Add(new JO("shape", sh, "example", e.Sql, "event", r.Name + "#" + e.Seq)); }
                }
                JO step = null;
                if (spec != null) {
                    double best = 0; JO bestStep = null;
                    foreach (JO s in spec.Arr("steps")) {
                        if (chapter != null && s.Str("caption") == chapter && s.Str("recording") == r.Name) { bestStep = s; best = 1; break; }
                        var q = new HashSet<string>(); foreach (var id in s.Strings("queries")) foreach (JO x in spec.Arr("queries")) if (x.Str("id") == id) q.Add(x.Str("shape"));
                        int inter = 0; foreach (var sh in shapes) if (q.Contains(sh)) inter++;
                        int union = q.Count + shapes.Count - inter; double score = union == 0 ? 0 : (double)inter / union;
                        if (score > best) { best = score; bestStep = s; }
                    }
                    if (bestStep != null && best >= 0.34) step = new JO("id", bestStep.Str("id"), "caption", bestStep.Str("caption"), "score", Math.Round(best, 2), "method", chapter != null && best == 1 ? "chapter of the recording" : "statement shapes");
                }
                ops.Add(new JO("id", null, "step", step, "chapter", chapter, "recording", r.Name, "events_from", cur[0].Seq, "events_to", cur[cur.Count - 1].Seq,
                    "wall_from_us", from, "wall_to_us", to, "video_from", double.IsNaN(vFrom) ? null : (object)Math.Round(vFrom, 3), "video_to", double.IsNaN(vTo) ? null : (object)Math.Round(vTo, 3),
                    "database", new JO("statements", st, "transactions", tx, "tables_written", written, "not_in_spec", notInSpec), "said", new List<object>(), "seen", new List<object>(), "findings", new List<object>()));
            };
            foreach (var e in r.Events) {
                if (cur == null || e.Ts - lastTs > (long)(o.OperationGapSeconds * 1e6)) { close(); cur = new List<DbEvent>(); }
                cur.Add(e); lastTs = e.Ts;
            }
            close();
        }
        ops.Sort(delegate (JO a, JO b) { int c = a.Long("wall_from_us", 0).CompareTo(b.Long("wall_from_us", 0)); return c != 0 ? c : string.CompareOrdinal(a.Str("recording"), b.Str("recording")); });
        for (int i = 0; i < ops.Count; i++) ops[i]["id"] = "op" + (i + 1);

        // ---- 6. what was said and seen goes to the nearest operation in time
        var unSaid = new List<object>(); var unSeen = new List<object>();
        Func<long, long, JO> nearest = delegate (long fromUs, long toUs) {
            JO best = null; long bestD = long.MaxValue;
            foreach (var op in ops) {
                long a = op.Long("wall_from_us", 0), b = op.Long("wall_to_us", 0);
                long d = toUs < a ? a - toUs : fromUs > b ? fromUs - b : 0;
                if (d < bestD) { bestD = d; best = op; }
            }
            return best != null && bestD <= (long)(o.AttachSeconds * 1e6) ? best : null;
        };
        foreach (var t in transcript) {
            long from = map.ToWallUs(t.Num("video_from", 0)), to = map.ToWallUs(t.Num("video_to", 0));
            var op = nearest(from, to);
            if (op == null) unSaid.Add(t.Str("id")); else op.Arr("said").Add(t.Str("id"));
        }
        foreach (var k in keyframes) {
            var op = nearest(k.Long("wall_us", 0), k.Long("wall_us", 0));
            if (op == null) unSeen.Add(k.Str("id")); else op.Arr("seen").Add(k.Str("id"));
        }
        int fn = 0;
        foreach (var op in ops) {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (spec != null && op.Obj("step") != null) foreach (JO s in spec.Arr("steps")) if (s.Str("id") == op.Obj("step").Str("id")) {
                known.UnionWith(Spec.Words(s.Str("caption")));
                foreach (JO a in s.Arr("actions")) known.UnionWith(Spec.Words(a.Str("text")));
                foreach (JO wr in s.Arr("writes")) { known.UnionWith(Spec.Words(wr.Str("table"))); foreach (var c in wr.Strings("columns")) known.UnionWith(Spec.Words(c.Replace('_', ' '))); }
                foreach (JO m in s.Arr("manual")) known.UnionWith(Spec.Words(m.Str("title")));
            }
            var f = op.Arr("findings");
            foreach (var id in op.Strings("said")) {
                var t = transcript.Find(delegate (JO x) { return x.Str("id") == id; });
                var nw = new List<object>(); foreach (var word in Spec.Words(t.Str("text"))) if (!known.Contains(word)) nw.Add(word);
                nw.Sort(delegate (object a, object b) { return string.CompareOrdinal((string)a, (string)b); });
                f.Add(new JO("id", "f" + (++fn), "kind", "said", "ref", id, "text", t.Str("text"), "words_not_in_spec", nw, "status", "open"));
            }
            foreach (var id in op.Strings("seen")) { var k = keyframes.Find(delegate (JO x) { return x.Str("id") == id; }); f.Add(new JO("id", "f" + (++fn), "kind", "seen", "ref", id, "file", k.Str("file"), "status", "open")); }
            foreach (JO x in op.Obj("database").Arr("not_in_spec")) f.Add(new JO("id", "f" + (++fn), "kind", "database", "shape", x.Str("shape"), "example", x.Str("example"), "status", "open"));
        }

        var res = new JO();
        res["format"] = "eqlmaker-findings"; res["version"] = DbEvent.FormatVersion; res["name"] = name;
        res["source"] = new JO("kind", "video", "file", Path.GetFileName(file), "duration", duration, "fps", fps, "width", w, "height", h);
        res["readings"] = new JO("frames_sampled", times.Count, "pattern", nPattern, "digits", nDigits, "unreadable", nNone, "strip_at", hint.IsEmpty ? null : new List<object> { hint.X, hint.Y, hint.Width, hint.Height },
            "utc_offset_minutes", offsetMinutes == int.MinValue ? null : (object)offsetMinutes);
        res["clock"] = map.ToJson();
        res["clock_offset"] = new JO("server_minus_recorder_ms", Math.Round(offsetS * 1000, 1), "method", offsetMethod,
            "estimate", double.IsNaN(est) ? null : new JO("server_minus_recorder_ms", Math.Round(est * 1000, 1), "confidence", Math.Round(conf, 2), "matched", matched, "note", "moments the screen changed against starts of database activity; the estimate lags by the time the screen needs to show a result"));
        res["transcript"] = new JO("tool", transcriptTool, "segments", transcript.ConvertAll(delegate (JO x) { return (object)x; }));
        res["keyframes"] = keyframes.ConvertAll(delegate (JO x) { return (object)x; });
        res["screen_changes"] = changes.ConvertAll(delegate (double c) { return (object)Math.Round(c, 3); });
        res["recordings"] = recs.ConvertAll(delegate (RecordingData r) { return (object)r.Name; });
        res["operations"] = ops.ConvertAll(delegate (JO x) { return (object)x; });
        res["unassigned"] = new JO("said", unSaid, "seen", unSeen);
        res["warnings"] = warnings;
        string outFile = Path.Combine(outDir, "findings.json");
        Json.WriteFile(outFile, res);
        return new JO("findings", outFile, "frames_sampled", times.Count, "read_pattern", nPattern, "read_digits", nDigits, "unreadable", nNone, "cuts", map.Cuts.Count, "transcript_segments", transcript.Count, "keyframes", keyframes.Count,
            "operations", ops.Count, "findings_count", fn, "clock_offset_ms", Math.Round(offsetS * 1000, 1), "clock_offset_method", offsetMethod, "warnings", warnings);
    }
}
}
