// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Database events (event format v0.1, see docs/event-format.md) and the EventSource interface.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace EqlMaker {

/// <summary>One database event. Fields a source cannot deliver stay null.</summary>
public class DbEvent {
    public const string FormatVersion = "0.1";
    /// <summary>Position in the file (1-based), set when written.</summary>
    public int Seq;
    /// <summary>Time in microseconds since 1970-01-01 UTC, clock of the database server.</summary>
    public long Ts;
    /// <summary>statement | execute | error | transaction</summary>
    public string Kind;
    /// <summary>Name of the event source (postgres, eqlserver, or a plugin).</summary>
    public string Source;
    /// <summary>How the source saw the event: log | trace | replication.</summary>
    public string Via;
    public string Session, User, Database, Application;
    public string Sql;
    /// <summary>Parameter values as text; a null entry is SQL NULL. Null when the statement had none or the source cannot tell.</summary>
    public List<string> Params;
    /// <summary>Transaction id of the server.</summary>
    public string Tx;
    /// <summary>Tables changed (schema.table), measured, not guessed from the SQL text.</summary>
    public List<string> Tables;
    /// <summary>Changed rows: {table, op: insert|update|delete|truncate, new: {column: value}, old: {column: value}}.</summary>
    public List<JO> Rows;
    /// <summary>Error message for kind = error.</summary>
    public string Error;
    /// <summary>Seconds since the start of the recording (recorder clock), added by the recorder.</summary>
    public double? Offset;

    public JO ToJson() {
        var o = new JO();
        o["v"] = FormatVersion; o["seq"] = Seq; o["ts"] = Ts; o["time"] = Clock.Iso(Ts);
        o["offset"] = Offset.HasValue ? (object)Math.Round(Offset.Value, 6) : null;
        o["kind"] = Kind; o["source"] = Source; o["via"] = Via;
        o["session"] = Session; o["user"] = User; o["database"] = Database; o["application"] = Application;
        o["sql"] = Sql;
        o["params"] = Params == null ? null : new List<object>(Params.ConvertAll(delegate (string s) { return (object)s; }));
        o["tx"] = Tx;
        o["tables"] = Tables == null ? null : new List<object>(Tables.ConvertAll(delegate (string s) { return (object)s; }));
        o["rows"] = Rows == null ? null : new List<object>(Rows.ConvertAll(delegate (JO r) { return (object)r; }));
        o["error"] = Error;
        return o;
    }

    public static DbEvent FromJson(JO o) {
        string v = o.Str("v", "");
        if (!v.StartsWith("0.")) throw new FormatException("event format version " + v + " is not supported (this build reads 0.x)");
        var e = new DbEvent();
        e.Seq = o.Int("seq", 0); e.Ts = o.Long("ts", 0); e.Kind = o.Str("kind"); e.Source = o.Str("source"); e.Via = o.Str("via");
        e.Session = o.Str("session"); e.User = o.Str("user"); e.Database = o.Str("database"); e.Application = o.Str("application");
        e.Sql = o.Str("sql"); e.Tx = o.Str("tx"); e.Error = o.Str("error");
        if (o["offset"] != null) e.Offset = o.Num("offset", 0);
        if (o["params"] != null) { e.Params = new List<string>(); foreach (var p in o.Arr("params")) e.Params.Add(p == null ? null : Convert.ToString(p, CultureInfo.InvariantCulture)); }
        if (o["tables"] != null) e.Tables = o.Strings("tables");
        if (o["rows"] != null) { e.Rows = new List<JO>(); foreach (var r in o.Arr("rows")) e.Rows.Add((JO)r); }
        return e;
    }

    public static List<DbEvent> ReadFile(string path) {
        var list = new List<DbEvent>();
        foreach (var o in Json.ReadLines(path)) list.Add(FromJson(o));
        return list;
    }
    public static void WriteFile(string path, List<DbEvent> events) {
        var sb = new StringBuilder();
        for (int i = 0; i < events.Count; i++) { events[i].Seq = i + 1; sb.Append(Json.Write(events[i].ToJson())); sb.Append('\n'); }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
    /// <summary>Sorts by time; events with the same time keep their order.</summary>
    public static void SortByTime(List<DbEvent> events) {
        var idx = new Dictionary<DbEvent, int>();
        for (int i = 0; i < events.Count; i++) idx[events[i]] = i;
        events.Sort(delegate (DbEvent a, DbEvent b) { int c = a.Ts.CompareTo(b.Ts); return c != 0 ? c : idx[a].CompareTo(idx[b]); });
    }
}

/// <summary>What to capture. Filled from the "capture" section of the configuration.</summary>
public class CaptureOptions {
    public string Connection = "";
    public List<string> Roles = new List<string>(), Databases = new List<string>();
    /// <summary>Also capture changed rows (needs logical decoding on the server).</summary>
    public bool Changes = true;
    /// <summary>Path of the server log when the server does not use its logging collector.</summary>
    public string LogFile = "";
    /// <summary>Settings of the chosen source (section named like the source in the configuration).</summary>
    public JO SourceSettings = new JO();
    /// <summary>File in which a source notes what it switched on, so "reset" can undo an aborted capture.</summary>
    public string StateFile = "";
}

/// <summary>A source of database events. Built-in: postgres, eqlserver. Others: plugins (docs/plugins.md).</summary>
public interface IEventSource {
    string Name { get; }
    /// <summary>Switches the capture on. Applications started afterwards are captured.</summary>
    void Start(CaptureOptions options);
    /// <summary>Switches the capture off and returns the events, sorted by time.</summary>
    List<DbEvent> Stop();
    /// <summary>Undoes what an aborted capture left behind on the server.</summary>
    void Reset(CaptureOptions options);
    /// <summary>After Start: what the source delivers, the clock offset of the server, warnings.
    /// Keys used by the recorder: server_minus_local_us (long), warnings (list of strings), fields (object).</summary>
    JO Info { get; }
}

/// <summary>Standard input of plugin programs.</summary>
public static class PluginIo {
    /// <summary>Call before starting a program whose standard input is written by EQLMaker. When the console
    /// code page is UTF-8 (65001), the .NET Framework would otherwise put a byte order mark in front of
    /// everything written to that input; plugins get UTF-8 without one.</summary>
    public static void NoByteOrderMark() {
        try { if (Console.InputEncoding.GetPreamble().Length > 0) Console.InputEncoding = new UTF8Encoding(false); } catch { }
    }
}

public delegate IEventSource SourceFactory();

public static class Sources {
    static readonly Dictionary<string, SourceFactory> factories = new Dictionary<string, SourceFactory>(StringComparer.OrdinalIgnoreCase);
    static Sources() {
        Register("postgres", delegate { return new PostgresSource(); });
        Register("eqlserver", delegate { return new EqlServerSource(); });
    }
    public static void Register(string name, SourceFactory f) { factories[name] = f; }
    public static bool Has(string name) { return factories.ContainsKey(name); }
    public static IEventSource Create(string name) {
        SourceFactory f;
        if (!factories.TryGetValue(name ?? "", out f)) throw new Exception("unknown event source '" + name + "' (known: " + string.Join(", ", Names().ToArray()) + ")");
        return f();
    }
    public static List<string> Names() { var l = new List<string>(factories.Keys); l.Sort(StringComparer.Ordinal); return l; }
}

/// <summary>Source plugin as a separate program in any language: it gets the capture options as one JSON
/// line on standard input, answers with one line {"info": {...}} when it is capturing, writes events
/// (one JSON object per line, event format v0.1) to standard output while it runs, and ends when it
/// reads the line "stop". Start returns only after the plugin said that it is capturing, so that
/// nothing done right after the start is missed.</summary>
public class ExecSource : IEventSource {
    readonly string name, command, workDir;
    /// <summary>How long the plugin may take to say that it is capturing.</summary>
    public int StartTimeoutSeconds = 60;
    readonly ManualResetEvent ready = new ManualResetEvent(false);
    Process proc; readonly List<DbEvent> events = new List<DbEvent>(); readonly StringBuilder stderr = new StringBuilder();
    Thread reader; JO info = new JO();
    public ExecSource(string name, string command, string workDir) { this.name = name; this.command = command; this.workDir = workDir; }
    public string Name { get { return name; } }
    public JO Info { get { return info; } }

    static JO OptionsJson(CaptureOptions o, string action) {
        return new JO("action", action, "connection", o.Connection, "roles", o.Roles, "databases", o.Databases, "changes", o.Changes, "logFile", o.LogFile, "settings", o.SourceSettings);
    }
    Process Launch() {
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"");
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8; psi.WorkingDirectory = workDir;
        PluginIo.NoByteOrderMark();
        return Process.Start(psi);
    }
    public void Start(CaptureOptions options) {
        proc = Launch();
        proc.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
        proc.BeginErrorReadLine();
        reader = new Thread(delegate () {
            string line;
            while ((line = proc.StandardOutput.ReadLine()) != null) {
                if (line.Trim().Length == 0) continue;
                try {
                    var o = Json.ParseObject(line);
                    if (o.Has("info")) { info = o.Obj("info") ?? new JO(); ready.Set(); continue; }   // "I am capturing", with what the source delivers
                    var ev = DbEvent.FromJson(o); if (ev.Source == null) ev.Source = name;
                    lock (events) events.Add(ev);
                } catch (Exception ex) { lock (stderr) stderr.AppendLine("bad event line: " + ex.Message); }
            }
        });
        reader.IsBackground = true; reader.Start();
        var w = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false));
        w.Write(Json.Write(OptionsJson(options, "start")) + "\n"); w.Flush();
        // wait until the plugin says that it is capturing
        var sw = Stopwatch.StartNew();
        while (!ready.WaitOne(100)) {
            if (proc.HasExited) { reader.Join(2000); if (ready.WaitOne(0)) break; throw new Exception("source plugin '" + name + "' ended before it was capturing: " + stderr.ToString().Trim()); }
            if (sw.Elapsed.TotalSeconds > StartTimeoutSeconds) { try { proc.Kill(); } catch { } throw new Exception("source plugin '" + name + "' did not answer with {\"info\": ...} within " + StartTimeoutSeconds + " seconds"); }
        }
    }
    public List<DbEvent> Stop() {
        if (proc == null) return new List<DbEvent>();
        try { var w = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)); w.Write("stop\n"); w.Flush(); proc.StandardInput.Close(); } catch { }
        if (!proc.WaitForExit(30000)) { try { proc.Kill(); } catch { } throw new Exception("source plugin '" + name + "' did not end after 'stop'"); }
        reader.Join(5000);
        if (proc.ExitCode != 0) throw new Exception("source plugin '" + name + "' failed: " + stderr.ToString().Trim());
        List<DbEvent> list; lock (events) list = new List<DbEvent>(events);
        DbEvent.SortByTime(list);
        return list;
    }
    public void Reset(CaptureOptions options) {
        using (var p = Launch()) {
            var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)); w.Write(Json.Write(OptionsJson(options, "reset")) + "\n"); w.Flush(); p.StandardInput.Close();
            p.StandardOutput.ReadToEnd(); p.WaitForExit(30000);
        }
    }
}
}
