// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Event source "eqlserver": the same capture as "postgres" (server log, sessions, logical decoding
// where the server offers it) plus, when a statistics port is configured, the statement trace that
// the server offers over HTTP for connections that do not speak the PostgreSQL protocol.
// Only documented, outward interfaces are used; all connection data comes from the EQLMaker
// configuration (section "eqlserver": { "statsPort": 0 }).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace EqlMaker {

public class EqlServerSource : PostgresSource {
    public override string Name { get { return "eqlserver"; } }
    int statsPort; string statsHost = "127.0.0.1"; bool tracing;

    string Http(string path) {
        var req = (HttpWebRequest)WebRequest.Create("http://" + statsHost + ":" + statsPort + path);
        req.Timeout = 5000; req.Proxy = null;
        using (var r = req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    public override void Start(CaptureOptions options) {
        try { base.Start(options); }
        catch (Exception e) {
            if (e.Message.Contains("capture.logFile")) throw new Exception(e.Message + " (the server writes it as eqlserver-server.log into its folder)");
            throw;
        }
        statsPort = options.SourceSettings.Int("statsPort", 0);
        statsHost = options.SourceSettings.Str("statsHost", "127.0.0.1");
        if (statsPort <= 0) { info["trace"] = "off (no statsPort configured)"; return; }
        try {
            string q = "/trace/start?logins=" + Uri.EscapeDataString(string.Join(",", options.Roles.ToArray())) + "&databases=" + Uri.EscapeDataString(string.Join(",", options.Databases.ToArray()));
            var r = Json.ParseObject(Http(q));
            tracing = r.Has("file");
            info["trace"] = tracing ? "on" : "off (" + r.Str("error", "?") + ")";
        } catch (Exception e) { info["trace"] = "off (statistics port not reachable: " + e.Message + ")"; }
    }

    public override List<DbEvent> Stop() {
        var list = base.Stop();
        if (!tracing) return list;
        try {
            var r = Json.ParseObject(Http("/trace/stop"));
            string file = r.Str("file");
            list.AddRange(ReadTrace(file, Name));
            try { File.Delete(file); } catch { }
            DbEvent.SortByTime(list);
        } catch (Exception e) { warnings.Add("the statement trace could not be read: " + e.Message); info["trace"] = "failed"; }
        tracing = false;
        return list;
    }

    public override void Reset(CaptureOptions options) {
        base.Reset(options);
        statsPort = options.SourceSettings.Int("statsPort", 0);
        statsHost = options.SourceSettings.Str("statsHost", "127.0.0.1");
        if (statsPort > 0) { try { Http("/trace/stop"); } catch { } }
    }

    /// <summary>Trace file: tab-separated text with a header line; columns time, session, user, database,
    /// application, kind, statement; backslash escapes \n, \t and \\. Times are local time of the server.</summary>
    public static List<DbEvent> ReadTrace(string file, string source) {
        var list = new List<DbEvent>();
        string[] lines;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            lines = new StreamReader(fs, Encoding.UTF8).ReadToEnd().Split('\n');
        for (int i = 1; i < lines.Length; i++) {
            var c = lines[i].TrimEnd('\r').Split('\t');
            if (c.Length < 7) continue;
            DateTime local = DateTime.Parse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
            string kind = c[5].IndexOf("rpc", StringComparison.OrdinalIgnoreCase) >= 0 ? "execute" : "statement";
            list.Add(new DbEvent { Ts = Clock.ToUs(local.ToUniversalTime()), Kind = kind, Source = source, Via = "trace", Session = c[1], User = NullIfEmpty(Unescape(c[2])), Database = NullIfEmpty(Unescape(c[3])),
                Application = NullIfEmpty(Unescape(c[4])), Sql = Unescape(c[6]).TrimEnd() });
        }
        return list;
    }

    static string NullIfEmpty(string s) { return s == "" ? null : s; }

    static string Unescape(string s) {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++) {
            if (s[i] == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n); }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
}
