// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// video: two recordings of the same steps (the original and the rebuilt application) on one time
// axis. Chapters with the same caption are shown together; the shorter one waits on its last
// picture. Result: pair.json, a player page that keeps both videos in step, and (with FFmpeg) one
// combined video file. How the two pictures are arranged is a layout: built in "side-by-side",
// others as plugins of type video-layout.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EqlMaker {

public static class VideoPair {
    static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    /// <summary>Chapters of both recordings matched by caption, in the order of recording A.</summary>
    public static List<JO> Align(RecordingData a, RecordingData b, List<object> unmatched) {
        var list = new List<JO>(); var cb = b.Manifest.Arr("chapters") ?? new List<object>(); var used = new HashSet<int>();
        foreach (JO ca in a.Manifest.Arr("chapters") ?? new List<object>()) {
            int hit = -1;
            for (int i = 0; i < cb.Count; i++) if (!used.Contains(i) && ((JO)cb[i]).Str("caption") == ca.Str("caption")) { hit = i; break; }
            if (hit < 0) { unmatched.Add("only in " + a.Name + ": " + ca.Str("caption")); continue; }
            used.Add(hit); var x = (JO)cb[hit];
            double da = ca.Num("end", 0) - ca.Num("start", 0), db = x.Num("end", 0) - x.Num("start", 0);
            list.Add(new JO("caption", ca.Str("caption"), "a_start", ca.Num("start", 0), "a_end", ca.Num("end", 0), "b_start", x.Num("start", 0), "b_end", x.Num("end", 0), "duration", Math.Round(Math.Max(da, db), 3)));
        }
        for (int i = 0; i < cb.Count; i++) if (!used.Contains(i)) unmatched.Add("only in " + b.Name + ": " + ((JO)cb[i]).Str("caption"));
        double t = 0; foreach (var c in list) { c["start"] = Math.Round(t, 3); t += c.Num("duration", 0); }
        return list;
    }

    public static JO BuiltInLayout() {
        return new JO("name", "side-by-side", "height", 720, "filter", "[a][b]hstack=inputs=2[v]", "player", "row");
    }

    static string Encoder(Config cfg, out string ext) {
        string o, e; Media.Run(Media.Find(cfg, "ffmpeg"), "-hide_banner -encoders", null, out o, out e, 30000);
        ext = ".mp4";
        if (o.Contains(" libx264 ")) return "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p";
        if (o.Contains(" h264_mf ")) return "-c:v h264_mf -b:v 3M -pix_fmt yuv420p";
        if (o.Contains(" libopenh264 ")) return "-c:v libopenh264 -b:v 3M -pix_fmt yuv420p";
        return "-c:v mpeg4 -q:v 4";   // plays in media players, not in every browser
    }

    public static JO Run(Config cfg, string recA, string recB, string layoutName, string name, bool noVideoFile) {
        var a = RecordingData.Load(cfg.RecordingDir(recA)); var b = RecordingData.Load(cfg.RecordingDir(recB));
        string va = a.Manifest.Str("video"), vb = b.Manifest.Str("video");
        if (va == null || vb == null) throw new Exception("both recordings need a video (" + (va == null ? a.Name : b.Name) + " has none)");
        JO layout = BuiltInLayout();
        if (!string.IsNullOrEmpty(layoutName) && layoutName != "side-by-side") {
            var p = Plugin.Find(cfg.Plugins(), "video-layout", layoutName);
            if (p == null) throw new Exception("video layout '" + layoutName + "' not found (built-in: side-by-side; others are plugins of type video-layout)");
            layout = p.Manifest;
        }
        var unmatched = new List<object>(); var chapters = Align(a, b, unmatched);
        if (chapters.Count == 0) throw new Exception("the two recordings have no chapter with the same caption: set the same chapter marks in both");
        if (string.IsNullOrEmpty(name)) name = a.Name + "-" + b.Name;
        string dir = cfg.Sub("video", name); Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(a.Dir, va), Path.Combine(dir, "a.mp4"), true); File.Copy(Path.Combine(b.Dir, vb), Path.Combine(dir, "b.mp4"), true);
        double total = 0; foreach (var c in chapters) total += c.Num("duration", 0);
        var pair = new JO("format", "eqlmaker-video-pair", "version", DbEvent.FormatVersion, "a", new JO("name", a.Name, "video", "a.mp4"), "b", new JO("name", b.Name, "video", "b.mp4"),
            "layout", layout.Str("name"), "player", layout.Str("player", "row"), "duration", Math.Round(total, 3), "chapters", chapters.ConvertAll(delegate (JO c) { return (object)c; }), "unmatched", unmatched);
        Json.WriteFile(Path.Combine(dir, "pair.json"), pair);
        string html = File.ReadAllText(Share.File("viewer", "pair.html"), Encoding.UTF8).Replace("/*DATA*/null", Json.Write(pair).Replace("</", "<\\/"));
        File.WriteAllText(Path.Combine(dir, "player.html"), html, new UTF8Encoding(false));
        var res = new JO("folder", dir, "player", Path.Combine(dir, "player.html"), "chapters", chapters.Count, "duration", Math.Round(total, 3), "unmatched", unmatched.Count);

        if (!noVideoFile) {
            Media.RequireFfmpeg(cfg);
            string ext, enc = Encoder(cfg, out ext), ff = Media.Find(cfg, "ffmpeg"); int h = layout.Int("height", 720);
            var listFile = new StringBuilder(); int i = 0;
            foreach (var c in chapters) {
                double da = c.Num("a_end", 0) - c.Num("a_start", 0), db = c.Num("b_end", 0) - c.Num("b_start", 0), d = c.Num("duration", 0);
                string seg = Path.Combine(dir, "seg" + (++i).ToString("000") + ext);
                string fc = "[0:v]scale=-2:" + h + ",fps=15,tpad=stop_mode=clone:stop_duration=" + N(Math.Max(0, d - da) + 0.2) + "[a];[1:v]scale=-2:" + h + ",fps=15,tpad=stop_mode=clone:stop_duration=" + N(Math.Max(0, d - db) + 0.2) + "[b];" + layout.Str("filter");
                string o, e;
                int code = Media.Run(ff, "-hide_banner -loglevel error -y -ss " + N(c.Num("a_start", 0)) + " -t " + N(da) + " -i \"" + Path.Combine(dir, "a.mp4") + "\" -ss " + N(c.Num("b_start", 0)) + " -t " + N(db) + " -i \"" + Path.Combine(dir, "b.mp4") + "\" -filter_complex \"" + fc + "\" -map \"[v]\" -t " + N(d) + " -an " + enc + " \"" + seg + "\"", null, out o, out e, 3600000);
                if (code != 0) throw new Exception("FFmpeg failed on chapter '" + c.Str("caption") + "': " + e.Trim());
                listFile.Append("file '" + Path.GetFileName(seg) + "'\n");
            }
            File.WriteAllText(Path.Combine(dir, "segments.txt"), listFile.ToString(), new UTF8Encoding(false));
            string outFile = Path.Combine(dir, "pair" + ext), o2, e2;
            int code2 = Media.Run(ff, "-hide_banner -loglevel error -y -f concat -safe 0 -i segments.txt -c copy \"" + outFile + "\"", dir, out o2, out e2, 3600000);
            if (code2 != 0) throw new Exception("FFmpeg could not join the chapters: " + e2.Trim());
            for (int k = 1; k <= i; k++) { try { File.Delete(Path.Combine(dir, "seg" + k.ToString("000") + ext)); } catch { } }
            try { File.Delete(Path.Combine(dir, "segments.txt")); } catch { }
            res["video"] = outFile; res["encoder"] = enc.Split(' ')[1];
        }
        return res;
    }
}
}
