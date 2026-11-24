// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The whole pipeline with the sample application "Sample Shop", against the database of the
// project configuration: the same steps are operated in the Windows program and in its browser
// variant, both are recorded, and the database changes are compared. Used by "eqlmaker sample",
// by the self test of the package and by the end-to-end test of the repository.
// The Windows program runs on a separate desktop that is never shown, so nothing moves on screen.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EqlMaker {

public static class SampleRun {
    public const string Database = "eqlmaker_sample", Role = "sample_app";

    public static string SampleDir {
        get {
            string d = Path.Combine(Media.PackageRoot, "examples", "sample-app");
            if (!Directory.Exists(d)) throw new Exception("the sample application was not found: " + d);
            return d;
        }
    }

    static string RandomPassword() { var b = new byte[18]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return Convert.ToBase64String(b).Replace('+', 'x').Replace('/', 'y'); }

    /// <summary>Creates role and database of the sample and loads tables and data; an existing sample
    /// database is replaced. Returns the connection of the sample role (password inside).</summary>
    public static PgConnInfo ResetDatabase(Config cfg, ref string password) {
        var admin = PgConnInfo.Parse(cfg.Connection);
        if (password == null) password = Environment.GetEnvironmentVariable("SAMPLE_APP_PASSWORD");
        if (string.IsNullOrEmpty(password)) password = RandomPassword();
        using (var c = new PgClient(admin.WithApplication(PostgresSource.AppName))) {
            if (c.Query("SELECT 1 FROM pg_roles WHERE rolname = $1", Role).Rows.Count == 0) c.Exec("CREATE ROLE " + Role + " LOGIN");
            c.Exec("ALTER ROLE " + Role + " LOGIN PASSWORD " + PgClient.Lit(password));
            c.Exec("DROP DATABASE IF EXISTS " + Database + " WITH (FORCE)");
            c.Exec("CREATE DATABASE " + Database + " OWNER " + Role);
        }
        var app = admin.WithDatabase(Database); app.User = Role; app.Password = password; app.PassFile = null; app.Application = "sample-setup";
        using (var c = new PgClient(app)) {
            c.Exec(File.ReadAllText(Path.Combine(SampleDir, "db", "schema.sql"), Encoding.UTF8));
            c.Exec(File.ReadAllText(Path.Combine(SampleDir, "db", "seed.sql"), Encoding.UTF8));
        }
        return app;
    }

    static int FreePort() { var l = new TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    static bool PortOpen(int port) { try { using (var c = new TcpClient()) { c.Connect("127.0.0.1", port); return true; } } catch { return false; } }

    static Config SampleConfig(Config cfg) {
        // the same project, capturing the sample database
        var c = Config.Load(cfg.Dir);
        var cap = c.Raw.Obj("capture") ?? new JO(); c.Raw["capture"] = cap;
        cap["roles"] = new List<object>(); cap["databases"] = new List<object> { Database };
        if (!cap.Has("changes")) cap["changes"] = true;
        c.Raw["input"] = "background";
        return c;
    }

    /// <summary>Runs the sample end to end. flaw: start the browser variant with its deliberate defects, so
    /// that the comparison must find differences. Returns true when the result is the expected one.</summary>
    public static bool Run(Config cfg, bool flaw, bool noVideo, Action<string> say) { return Run(cfg, flaw, noVideo, false, say); }

    /// <summary>visible: operate both variants with the real mouse on the visible desktop (for a demonstration
    /// video); otherwise nothing is shown.</summary>
    public static bool Run(Config cfg, bool flaw, bool noVideo, bool visible, Action<string> say) {
        bool ok = true; string step = "start";
        Action<bool, string> check = delegate (bool good, string text) { say((good ? "ok    " : "FAIL  ") + text); if (!good) ok = false; };
        Process node = null; string password = null;
        string oldPg = Environment.GetEnvironmentVariable("PGPASSWORD");
        try {
            var c = SampleConfig(cfg);
            if (string.IsNullOrEmpty(c.Connection)) throw new Exception("no connection in " + Path.Combine(cfg.Dir, Config.FileName) + " (eqlmaker init, then set \"connection\")");
            // the administrator password is fixed for this process before PGPASSWORD is used for the sample role
            string adminPw = PgConnInfo.Parse(c.Connection).ResolvePassword();
            PgConnInfo.SetAdminPassword(adminPw);
            string suffix = flaw ? "-flawed" : "";

            step = "database";
            var app = ResetDatabase(c, ref password);
            check(true, "sample database " + Database + " created on " + app.Host + ":" + app.Port);
            Environment.SetEnvironmentVariable("PGPASSWORD", password);
            Environment.SetEnvironmentVariable("SAMPLE_BIN", Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            Environment.SetEnvironmentVariable("SAMPLE_CONNECTION", "host=" + app.Host + " port=" + app.Port + " user=" + Role + " dbname=" + Database);

            step = "record the Windows program";
            string exe = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "eqlmaker.exe"), scen = Path.Combine(SampleDir, "scenarios");
            string cmd = "\"" + exe + "\" record --scenario \"" + Path.Combine(scen, "desktop.json") + "\" --name desktop --project \"" + c.Dir + "\" --input " + (visible ? "real" : "background") + " --quiet --json --capture-database " + Database + (noVideo ? " --no-video" : "");
            Environment.SetEnvironmentVariable("EQLMAKER_PGPASSWORD", adminPw);   // for the recording process only; it removes it before it starts the sample program
            int code;
            try {
                if (!visible) code = Desktop.RunOnBackgroundDesktop("eqlmaker-sample-" + Process.GetCurrentProcess().Id, cmd, c.Dir, 300000);
                else {
                    Desktop.RequireVisible();
                    var rp = new ProcessStartInfo(exe, cmd.Substring(exe.Length + 3)); rp.UseShellExecute = false; rp.WorkingDirectory = c.Dir;
                    using (var p = Process.Start(rp)) { if (!p.WaitForExit(600000)) { try { p.Kill(); } catch { } throw new Exception("the recording did not end"); } code = p.ExitCode; }
                }
            }
            finally { Environment.SetEnvironmentVariable("EQLMAKER_PGPASSWORD", null); }
            check(code == 0 && File.Exists(Path.Combine(c.RecordingsDir, "desktop", "recording.json")), "recorded the Windows program (exit code " + code + ")");
            if (!ok) return false;
            var d = RecordingData.Load(Path.Combine(c.RecordingsDir, "desktop"));
            int dtx = d.Events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count;
            check(d.Actions.Count > 10 && d.Events.Count > 10, "recording 'desktop': " + d.Actions.Count + " actions, " + (d.Events.Count - dtx) + " statements, " + dtx + " transactions with changed rows" + (d.Manifest.Str("video") != null ? ", video" : ", no video"));
            foreach (var w in d.Manifest.Strings("warnings")) say("      note: " + w);

            step = "record the browser variant";
            ResetDatabase(c, ref password);   // both runs start from the same data
            string nodeExe = Media.Find(c, "node"), web = Path.Combine(SampleDir, "web");
            if (!Directory.Exists(Path.Combine(web, "node_modules", "pg"))) throw new Exception("the browser variant needs its one dependency: run 'npm ci' in " + web + " (the setup of the package does that)");
            int port = FreePort();
            var psi = new ProcessStartInfo(nodeExe, "server.js"); psi.WorkingDirectory = web; psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
            psi.EnvironmentVariables.Remove("EQLMAKER_PGPASSWORD"); psi.EnvironmentVariables["PORT"] = port.ToString(); psi.EnvironmentVariables["PGPASSWORD"] = password; psi.EnvironmentVariables["SAMPLE_FLAW"] = flaw ? "1" : "0";
            psi.EnvironmentVariables["SAMPLE_DB"] = "postgres://" + Role + "@" + app.Host + ":" + app.Port + "/" + Database;
            try { node = Process.Start(psi); } catch (Exception e) { throw new Exception("Node.js not found (" + nodeExe + "): " + e.Message + ". Run eqlmaker-setup.cmd of the package, or set tools.node."); }
            node.OutputDataReceived += delegate { }; node.ErrorDataReceived += delegate { }; node.BeginOutputReadLine(); node.BeginErrorReadLine();
            var sw = Stopwatch.StartNew(); while (!PortOpen(port)) { if (node.HasExited || sw.ElapsedMilliseconds > 30000) throw new Exception("the browser variant did not start on port " + port); Thread.Sleep(150); }
            Environment.SetEnvironmentVariable("SAMPLE_URL", "http://127.0.0.1:" + port + "/?lang=en");
            var ctl = new Controller(c); ctl.Input = visible ? "real" : "background"; ctl.InputForced = true; ctl.Web.Port = FreePort(); ctl.Web.TemporaryProfile = true; ctl.NoVideo = noVideo;
            JO wr;
            try { wr = Scenario.Run(ctl, Path.Combine(scen, "web.json"), "web" + suffix); }
            finally { ctl.CloseStarted(); }
            var wrec = RecordingData.Load(Path.Combine(c.RecordingsDir, "web" + suffix));
            int wtx = wrec.Events.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; }).Count;
            check(wrec.Actions.Count > 10 && wrec.Events.Count > 10, "recording 'web" + suffix + "': " + wrec.Actions.Count + " actions, " + (wrec.Events.Count - wtx) + " statements, " + wtx + " transactions with changed rows");

            step = "correlate and spec";
            var tl = Timeline.Run(d.Dir); Timeline.Run(wrec.Dir);
            check(tl.Int("chapters", 0) >= 4, "correlate: " + tl.Int("chapters", 0) + " chapters, " + tl.Int("statements", 0) + " statements, " + tl.Int("transactions", 0) + " transactions on the time line");
            if (!flaw) {
                ManualImport.Run(c, Path.Combine(SampleDir, "manual", "handbook.md"), null);
                var sp = Spec.Run(c, new List<string> { "desktop" });
                check(sp.Int("steps", 0) >= 4 && sp.Int("masks", 0) >= 3 && sp.Int("tables", 0) >= 5, "spec: " + sp.Int("steps", 0) + " steps, " + sp.Int("masks", 0) + " masks, " + sp.Int("queries", 0) + " statement shapes, " + sp.Int("tables", 0) + " tables, manual linked");
            }

            step = "verify";
            if (dtx == 0) check(false, "the database comparison needs changed rows: the server does not deliver them (PostgreSQL: wal_level = logical)");
            else {
                var v = VerifyDb.Run(c, "desktop", "web" + suffix, new List<string>(), new List<string>(), new List<string>(), null);
                if (!flaw) check(v.Bool("equal", false), "verify db: the browser variant changes the database exactly like the Windows program (" + v.Int("equal_rows", 0) + " rows agree)");
                else check(!v.Bool("equal", true) && v.Int("only_in_a", 0) + v.Int("only_in_b", 0) > 0, "verify db finds the deliberate defects of the flawed variant: " + v.Int("only_in_a", 0) + " rows only in the original, " + v.Int("only_in_b", 0) + " only in the rebuilt one");
                say("      report: " + v.Str("readable"));
            }
            if (!flaw && !noVideo && d.Manifest.Str("video") != null && wrec.Manifest.Str("video") != null) {
                step = "video";
                var p = VideoPair.Run(c, "desktop", "web", null, null, true);
                check(p.Int("chapters", 0) >= 4, "video: player page for both recordings, " + p.Int("chapters", 0) + " chapters in step");
            }
        } catch (Exception e) { check(false, step + ": " + e.Message); }
        finally {
            if (node != null) { try { if (!node.HasExited) node.Kill(); } catch { } node.Dispose(); }
            Environment.SetEnvironmentVariable("PGPASSWORD", oldPg);
        }
        return ok;
    }

    /// <summary>Self test of an installed package: tools, database, the sample run in both forms, and (when a
    /// sound file is given) speech recognition.</summary>
    public static bool SelfTest(Config cfg, string audio, string expect, bool quick) {
        bool ok = true;
        Action<bool, string> check = delegate (bool good, string text) { Console.WriteLine((good ? "ok    " : "FAIL  ") + text); if (!good) ok = false; };
        Console.WriteLine(Texts.M("msg.selftest_title", Config.Version));
        try { Share.File("viewer", "viewer.html"); check(File.Exists(Share.File("viewer", "viewer.html")) && File.Exists(Share.File("prompts", "generate.md")) && Directory.Exists(Share.File("templates", "web")), "resources of the program are complete"); } catch (Exception e) { check(false, e.Message); }
        string o, er;
        try { Media.Run(Media.Find(cfg, "ffmpeg"), "-version", null, out o, out er, 20000); check(o.StartsWith("ffmpeg"), "FFmpeg: " + o.Split('\n')[0].Trim()); } catch (Exception e) { check(false, "FFmpeg not found: " + e.Message); }
        try { Media.Run(Media.Find(cfg, "node"), "--version", null, out o, out er, 20000); check(o.Trim().StartsWith("v"), "Node.js: " + o.Trim()); } catch (Exception e) { check(false, "Node.js not found: " + e.Message); }
        try { using (var c = new PgClient(PgConnInfo.Parse(cfg.Connection).WithApplication(PostgresSource.AppName))) check(true, "database: " + c.Scalar("SHOW server_version") + ", wal_level " + c.Scalar("SHOW wal_level")); } catch (Exception e) { check(false, "database not reachable: " + e.Message); }
        if (!string.IsNullOrEmpty(audio)) {
            try {
                string tool; string wav = Path.Combine(Path.GetTempPath(), "eqlmaker-selftest-" + Process.GetCurrentProcess().Id + ".wav");
                Media.ExtractAudio(cfg, audio, wav);
                var segs = Transcript.Recognise(cfg, wav, null, out tool);
                try { File.Delete(wav); File.Delete(Path.ChangeExtension(wav, null) + ".transcript.json"); } catch { }
                if (segs == null) check(false, "speech recognition: no recogniser found (tools\\whisper)");
                else {
                    var sb = new StringBuilder(); foreach (var s in segs) sb.Append(s.Text + " ");
                    string text = sb.ToString().ToLowerInvariant(); var missing = new List<string>();
                    foreach (var w in (expect ?? "").Split(',')) if (w.Trim() != "" && text.IndexOf(w.Trim().ToLowerInvariant(), StringComparison.Ordinal) < 0) missing.Add(w.Trim());
                    check(segs.Count > 0 && missing.Count == 0, "speech recognition (" + tool + "): \"" + sb.ToString().Trim() + "\"" + (missing.Count > 0 ? "; expected words missing: " + string.Join(", ", missing.ToArray()) : ""));
                }
            } catch (Exception e) { check(false, "speech recognition: " + e.Message); }
        }
        if (ok) {
            ok = Run(cfg, false, false, delegate (string s) { Console.WriteLine(s); });
            if (ok && !quick) ok = Run(cfg, true, true, delegate (string s) { Console.WriteLine(s); });
        }
        Console.WriteLine(ok ? Texts.M("msg.selftest_passed") : Texts.M("msg.selftest_failed"));
        return ok;
    }
}
}
