// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// External media tools: FFmpeg (frames, audio, cutting) and the speech recogniser (whisper.cpp).
// Tools are looked for in the configuration (tools.*), then in the "tools" folder of the package,
// then on the PATH.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EqlMaker {

public static class Media {
    /// <summary>Folder of the unpacked package (the parent of the folder with the programs).</summary>
    public static string PackageRoot { get { return Path.GetDirectoryName(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location).TrimEnd('\\', '/')); } }

    public static string Find(Config cfg, string tool) {
        string c = cfg == null ? null : cfg.ToolOrNull(tool);
        if (c != null) return c;
        string env = Environment.GetEnvironmentVariable("EQLMAKER_" + tool.ToUpperInvariant());
        if (!string.IsNullOrEmpty(env)) return env;
        string root = PackageRoot; string p = null;
        if (tool == "ffmpeg") p = Path.Combine(root, "tools", "ffmpeg", "bin", "ffmpeg.exe");
        else if (tool == "transcribe") p = Path.Combine(root, "tools", "whisper", "whisper-cli.exe");
        else if (tool == "node") p = Path.Combine(root, "tools", "node", "node.exe");
        if (p != null && File.Exists(p)) return p;
        return tool == "transcribe" ? null : tool;
    }

    public static int Run(string exe, string args, string cwd, out string stdout, out string stderr, int timeoutMs) {
        var psi = new ProcessStartInfo(exe, args); psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
        if (cwd != null) psi.WorkingDirectory = cwd;
        Process p;
        try { p = Process.Start(psi); } catch (Exception e) { throw new Exception("cannot start '" + exe + "': " + e.Message); }
        using (p) {
            var so = new StringBuilder(); var se = new StringBuilder();
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (so) so.AppendLine(e.Data); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (se) se.AppendLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } throw new Exception("timeout: " + exe + " " + args); }
            p.WaitForExit();
            stdout = so.ToString(); stderr = se.ToString();
            return p.ExitCode;
        }
    }

    static string Ffmpeg(Config cfg) { return Find(cfg, "ffmpeg"); }

    public static void RequireFfmpeg(Config cfg) {
        string o, e;
        try { if (Run(Ffmpeg(cfg), "-version", null, out o, out e, 20000) == 0) return; } catch { }
        throw new Exception("FFmpeg not found. It is not part of this repository: run eqlmaker-setup.cmd of the package, or set tools.ffmpeg in eqlmaker.json, or put ffmpeg on the PATH.");
    }

    /// <summary>duration (s), fps, width, height, audio (bool) of a media file.</summary>
    public static JO Probe(Config cfg, string file) {
        string o, e; Run(Ffmpeg(cfg), "-hide_banner -i \"" + file + "\"", null, out o, out e, 60000);
        var r = new JO("duration", 0.0, "fps", 0.0, "width", 0, "height", 0, "audio", false);
        var m = Regex.Match(e, @"Duration: (\d+):(\d\d):(\d\d(?:\.\d+)?)");
        if (m.Success) r["duration"] = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        m = Regex.Match(e, @"Stream #\S+.*Video:.*?(\d{2,5})x(\d{2,5})");
        if (!m.Success) throw new Exception("no video stream found in " + file + (e.Contains("No such file") ? " (file not found)" : ""));
        r["width"] = int.Parse(m.Groups[1].Value); r["height"] = int.Parse(m.Groups[2].Value);
        m = Regex.Match(e, @"Video:.*?([\d.]+) fps");
        if (m.Success) r["fps"] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        r["audio"] = Regex.IsMatch(e, @"Stream #\S+.*Audio:");
        return r;
    }

    public delegate void FrameHandler(double videoTime, byte[] luma, int width, int height);

    /// <summary>Decodes the frames that the FFmpeg expression "select" lets through and hands each one over
    /// as grey values together with its exact presentation time.</summary>
    public static int ReadFrames(Config cfg, string file, string select, int width, int height, FrameHandler handler) {
        string vf = (select == null ? "" : "select='" + select + "',") + "showinfo";
        var psi = new ProcessStartInfo(Ffmpeg(cfg), "-hide_banner -nostats -i \"" + file + "\" -an -vf \"" + vf + "\" -fps_mode passthrough -f rawvideo -pix_fmt gray -");
        psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        using (var p = Process.Start(psi)) {
            var times = new Queue<double>(); var err = new StringBuilder(); bool errDone = false;
            var rx = new Regex(@"pts_time:\s*([\d.]+)");
            var t = new Thread(delegate () {
                string line;
                while ((line = p.StandardError.ReadLine()) != null) {
                    var m = line.Contains("showinfo") ? rx.Match(line) : Match.Empty;
                    if (m.Success) lock (times) times.Enqueue(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                    else if (err.Length < 4000) err.AppendLine(line);
                }
                errDone = true;
            });
            t.IsBackground = true; t.Start();
            var stream = p.StandardOutput.BaseStream; int size = width * height, n = 0;
            while (true) {
                var buf = new byte[size]; int got = 0;
                while (got < size) { int r = stream.Read(buf, got, size - got); if (r <= 0) break; got += r; }
                if (got < size) break;
                double pts = double.NaN;
                for (int wait = 0; wait < 2000; wait++) {
                    lock (times) if (times.Count > 0) { pts = times.Dequeue(); break; }
                    if (errDone) { lock (times) if (times.Count > 0) pts = times.Dequeue(); break; }
                    Thread.Sleep(1);
                }
                if (double.IsNaN(pts)) throw new Exception("FFmpeg did not report the time of frame " + n);
                handler(pts, buf, width, height); n++;
            }
            p.WaitForExit(); t.Join(2000);
            if (p.ExitCode != 0) throw new Exception("FFmpeg failed: " + err.ToString().Trim());
            return n;
        }
    }

    public static void ExtractFrame(Config cfg, string file, double videoTime, string png) {
        string o, e;
        int code = Run(Ffmpeg(cfg), "-hide_banner -loglevel error -y -ss " + videoTime.ToString("0.###", CultureInfo.InvariantCulture) + " -i \"" + file + "\" -frames:v 1 \"" + png + "\"", null, out o, out e, 120000);
        if (code != 0 || !File.Exists(png)) throw new Exception("FFmpeg could not write " + png + ": " + e.Trim());
    }

    public static void ExtractAudio(Config cfg, string file, string wav) {
        string o, e;
        int code = Run(Ffmpeg(cfg), "-hide_banner -loglevel error -y -i \"" + file + "\" -vn -ac 1 -ar 16000 -c:a pcm_s16le \"" + wav + "\"", null, out o, out e, 3600000);
        if (code != 0) throw new Exception("FFmpeg could not extract the sound: " + e.Trim());
    }
}

public class TranscriptSegment { public double From, To; public string Text; }

public static class Transcript {
    static double Time(string s) {
        var m = Regex.Match(s.Trim(), @"^(?:(\d+):)?(\d+):(\d+)[.,](\d+)$");
        if (!m.Success) throw new FormatException("time not understood: " + s);
        return (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 3600 : 0) + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value) + double.Parse("0." + m.Groups[4].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a transcript: WebVTT, SRT, or the JSON written by whisper.cpp (-oj).</summary>
    public static List<TranscriptSegment> Read(string file) {
        string text = File.ReadAllText(file, Encoding.UTF8); var list = new List<TranscriptSegment>();
        if (text.TrimStart().StartsWith("{")) {
            var o = Json.ParseObject(text);
            var tr = o.Arr("transcription") ?? o.Arr("segments");
            if (tr == null) throw new FormatException("no 'transcription' in " + file);
            foreach (JO s in tr) {
                var off = s.Obj("offsets"); double from, to;
                if (off != null) { from = off.Num("from", 0) / 1000.0; to = off.Num("to", 0) / 1000.0; }
                else { from = s.Num("start", 0); to = s.Num("end", 0); }
                string t = (s.Str("text") ?? "").Trim();
                if (t != "") list.Add(new TranscriptSegment { From = from, To = to, Text = t });
            }
            return list;
        }
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            var m = Regex.Match(lines[i], @"^\s*([\d:.,]+)\s+-->\s+([\d:.,]+)");
            if (!m.Success) continue;
            var sb = new StringBuilder(); int j = i + 1;
            while (j < lines.Length && lines[j].Trim() != "") { if (sb.Length > 0) sb.Append(' '); sb.Append(Regex.Replace(lines[j].Trim(), "<[^>]+>", "")); j++; }
            if (sb.Length > 0) list.Add(new TranscriptSegment { From = Time(m.Groups[1].Value), To = Time(m.Groups[2].Value), Text = sb.ToString() });
            i = j;
        }
        return list;
    }

    /// <summary>Runs the speech recogniser on a 16 kHz mono WAV file. Returns null when no recogniser is installed.</summary>
    public static List<TranscriptSegment> Recognise(Config cfg, string wav, string language, out string tool) {
        tool = null;
        string exe = Media.Find(cfg, "transcribe");
        if (exe == null) return null;
        string dir = Path.GetDirectoryName(exe);
        string model = cfg.ToolOrNull("transcribeModel");
        if (model == null) {
            string mt = Path.Combine(dir, "model.txt");
            if (File.Exists(mt)) model = Path.Combine(dir, "models", File.ReadAllText(mt).Trim());
            else if (Directory.Exists(Path.Combine(dir, "models"))) { var ms = Directory.GetFiles(Path.Combine(dir, "models"), "ggml-*.bin"); if (ms.Length > 0) model = ms[0]; }
        }
        if (model == null || !File.Exists(model)) throw new Exception("speech model not found next to " + exe + " (models\\ggml-*.bin); set tools.transcribeModel");
        string outBase = Path.Combine(Path.GetDirectoryName(wav), Path.GetFileNameWithoutExtension(wav) + ".transcript");
        string o, e;
        // the recogniser runs in its own folder with a relative model path: it cannot open a model
        // whose full path contains characters outside ASCII
        string modelArg = model.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? model.Substring(dir.Length + 1) : model;
        int threads = Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2));
        int code = Media.Run(exe, "-m \"" + modelArg + "\" -f \"" + wav + "\" -l " + (string.IsNullOrEmpty(language) ? "auto" : language) + " -t " + threads + " -np -oj -of \"" + outBase + "\"", dir, out o, out e, 6 * 3600 * 1000);
        if (code != 0 || !File.Exists(outBase + ".json")) throw new Exception("the speech recogniser failed: " + (e.Length > 600 ? e.Substring(e.Length - 600) : e).Trim());
        tool = Path.GetFileName(exe) + " / " + Path.GetFileName(model);
        return Read(outBase + ".json");
    }
}
}
