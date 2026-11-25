// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EqlMaker.Tests {

public static class RecordCliTests {
    static int Cli(string project, string args, string adminPassword, out string output) {
        var psi = new ProcessStartInfo(Path.Combine(T.BinDir, "eqlmaker.exe"), args + " --project \"" + project + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        psi.EnvironmentVariables["EQLMAKER_PGPASSWORD"] = adminPassword;
        using (var p = Process.Start(psi)) {
            // reading to the end must work: the background recorder started by --start does not hold these pipes
            string e = null; var t = new System.Threading.Thread(delegate () { e = p.StandardError.ReadToEnd(); }); t.Start();
            string o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } throw new Exception("timeout: eqlmaker " + args); }
            t.Join(5000);
            output = o + "\n" + e;
            return p.ExitCode;
        }
    }

    /// <summary>A person works the application while the capture runs: record --start ... record --stop.</summary>
    public static void StartAndStopWhileSomeoneElseWorks() {
        using (var db = new TestDb()) {
            var ci = PgConnInfo.Parse(T.Pg); string d = T.TempDir("record-cli"), o;
            Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres", "connection", "host=" + ci.Host + " port=" + ci.Port + " user=" + ci.User + " dbname=" + ci.Database,
                "capture", new JO("roles", new List<object>(), "databases", new List<object> { db.Database }, "changes", true)));
            string pw = ci.ResolvePassword();
            T.Eq(1, Cli(d, "record --stop", pw, out o), "stop without a recording fails: " + o); T.Contains(o, "no recording is running", "message");
            T.Eq(0, Cli(d, "record --start --no-video --name manual", pw, out o), "start: " + o);
            T.Eq(1, Cli(d, "record --start --no-video --name second", pw, out o), "a second start is refused"); T.Contains(o, "already running", "message");
            using (var c = new PgClient(db.App)) {
                c.Exec("CREATE TABLE note (id serial PRIMARY KEY, body text)");
                c.Exec("SELECT pg_sleep(1.0)");
                c.Query("INSERT INTO note (body) VALUES ($1)", "written while the capture ran");
            }
            T.Eq(0, Cli(d, "record --stop --json", pw, out o), "stop: " + o);
            var rec = RecordingData.Load(Path.Combine(d, "recordings", "manual"));
            T.True(rec.Events.Exists(delegate (DbEvent e) { return e.Kind == "execute" && e.Params != null && e.Params[0] == "written while the capture ran" && e.Application == "test-app"; }), "statement with parameter captured");
            T.True(rec.Events.Exists(delegate (DbEvent e) { return e.Kind == "transaction" && e.Tables.Contains("public.note"); }), "changed row captured");
            T.True(rec.Manifest["video"] == null && File.Exists(Path.Combine(rec.Dir, "viewer.html")), "no video, viewer written");
            T.True(!File.Exists(Path.Combine(rec.Dir, "capture-active.json")), "state file of the capture removed");
            T.Eq(0, Cli(d, "correlate manual", pw, out o), "correlate works on a recording without actions: " + o);
        }
    }

    /// <summary>The logins and the database can be named on the command line, also for the background recording of --start.</summary>
    public static void StartTakesLoginsAndDatabaseFromTheCommandLine() {
        using (var db = new TestDb()) {
            var ci = PgConnInfo.Parse(T.Pg); string d = T.TempDir("record-cli-capture"), o;
            Json.WriteFile(Path.Combine(d, Config.FileName), new JO("source", "postgres", "connection", "host=" + ci.Host + " port=" + ci.Port + " user=" + ci.User + " dbname=" + ci.Database));
            string pw = ci.ResolvePassword();
            T.Eq(0, Cli(d, "record --start --no-video --name named --capture-database " + db.Database + " --capture-role " + db.Role, pw, out o), "start: " + o);
            using (var c = new PgClient(db.App)) { c.Exec("CREATE TABLE note (id serial PRIMARY KEY, body text)"); c.Exec("SELECT pg_sleep(1.0)"); c.Query("INSERT INTO note (body) VALUES ($1)", "named on the command line"); }
            T.Eq(0, Cli(d, "record --stop --json", pw, out o), "stop: " + o);
            var rec = RecordingData.Load(Path.Combine(d, "recordings", "named"));
            var cap = rec.Manifest.Obj("capture");
            T.Eq(db.Database, string.Join(",", cap.Strings("databases").ToArray()), "database of the recording"); T.Eq(db.Role, string.Join(",", cap.Strings("roles").ToArray()), "login of the recording");
            T.True(rec.Events.Exists(delegate (DbEvent e) { return e.Params != null && e.Params.Count > 0 && e.Params[0] == "named on the command line"; }), "statement captured");
        }
    }
}
}
