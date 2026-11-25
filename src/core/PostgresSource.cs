// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Event source "postgres": PostgreSQL 15 or later with nothing but what the server ships.
//   statements   log_statement = all for the captured roles/databases (new sessions), read back from
//                the server log; parameters from the DETAIL line of the log
//   changed rows logical decoding with the test_decoding output plugin (needs wal_level = logical)
//   sessions     pg_stat_activity
// The server is never restarted and no server-wide setting is changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EqlMaker {

public class LogEntry {
    public long Ts; public int Pid; public string Level, Message, User, Database, Application, Xid;
}

/// <summary>Reads the plain-text server log of PostgreSQL (log_destination = stderr).</summary>
public static class PgLog {
    /// <summary>Builds the regular expression for one log line from the server's log_line_prefix.</summary>
    public static Regex LineRegex(string prefix) {
        var sb = new StringBuilder("^");
        for (int i = 0; i < prefix.Length; i++) {
            char c = prefix[i];
            if (c != '%' || i + 1 >= prefix.Length) { sb.Append(Regex.Escape(c.ToString())); continue; }
            i++;
            while (i < prefix.Length && (prefix[i] == '-' || char.IsDigit(prefix[i]))) i++;   // padding
            if (i >= prefix.Length) break;
            switch (prefix[i]) {
                case 'a': sb.Append(@"\s*(?<a>.*?)\s*"); break;
                case 'u': sb.Append(@"\s*(?<u>.*?)\s*"); break;
                case 'd': sb.Append(@"\s*(?<d>.*?)\s*"); break;
                case 'p': sb.Append(@"\s*(?<p>\d+)\s*"); break;
                case 'm': sb.Append(@"(?<m>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)(?: \S+)?"); break;
                case 't': sb.Append(@"(?<t>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)(?: \S+)?"); break;
                case 'n': sb.Append(@"(?<n>\d+\.\d+)"); break;
                case 'c': sb.Append(@"(?<c>[0-9a-f]+\.[0-9a-f]+)"); break;
                case 'x': sb.Append(@"\s*(?<x>\d+)\s*"); break;
                case 'q': break;
                case '%': sb.Append("%"); break;
                default: sb.Append(@".*?"); break;   // r h b P i e l s v L Q
            }
        }
        sb.Append(@"(?<level>[A-Z]+):  (?<msg>.*)$");
        return new Regex(sb.ToString(), RegexOptions.Singleline);
    }

    /// <summary>Null when the prefix is good for capturing, else the reason.</summary>
    public static string PrefixProblem(string prefix) {
        bool time = prefix.Contains("%m") || prefix.Contains("%t") || prefix.Contains("%n");
        bool pid = prefix.Contains("%p") || prefix.Contains("%c");
        if (time && pid) return null;
        return "log_line_prefix is '" + prefix + "' but must contain a time (%m, %n or %t) and the process id (%p or %c); the PostgreSQL default '%m [%p] ' is fine";
    }

    /// <summary>Splits log text into entries. Lines that start with a tab continue the previous entry.
    /// tzOffsetSeconds: UTC offset of the log_timezone (ignored when the prefix has %n).</summary>
    public static List<LogEntry> Parse(string text, Regex line, int tzOffsetSeconds) {
        var list = new List<LogEntry>(); LogEntry cur = null;
        foreach (var raw in text.Split('\n')) {
            string l = raw.TrimEnd('\r');
            if (l.Length == 0) { if (cur != null && raw.Length > 0) cur.Message += "\n"; continue; }
            if (l[0] == '\t') { if (cur != null) cur.Message += "\n" + l.Substring(1); continue; }
            var m = line.Match(l);
            if (!m.Success) { if (cur != null) cur.Message += "\n" + l; continue; }
            cur = new LogEntry();
            cur.Level = m.Groups["level"].Value; cur.Message = m.Groups["msg"].Value;
            if (m.Groups["n"].Success) cur.Ts = (long)Math.Round(double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) * 1000) * 1000;
            else {
                string t = m.Groups["m"].Success ? m.Groups["m"].Value : m.Groups["t"].Value;
                DateTime dt = DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.None);
                cur.Ts = Clock.ToUs(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) - tzOffsetSeconds * 1000000L;
            }
            if (m.Groups["p"].Success) cur.Pid = int.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture);
            else if (m.Groups["c"].Success) { string c = m.Groups["c"].Value; cur.Pid = int.Parse(c.Substring(c.IndexOf('.') + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
            if (m.Groups["u"].Success) cur.User = m.Groups["u"].Value;
            if (m.Groups["d"].Success) cur.Database = m.Groups["d"].Value;
            if (m.Groups["a"].Success) cur.Application = m.Groups["a"].Value;
            if (m.Groups["x"].Success && m.Groups["x"].Value != "0") cur.Xid = m.Groups["x"].Value;
            list.Add(cur);
        }
        return list;
    }

    static readonly Regex Stmt = new Regex(@"^(statement|execute [^:]*|execute): (.*)$", RegexOptions.Singleline);
    static readonly Regex ParamLine = new Regex(@"^[Pp]arameters: (.*)$", RegexOptions.Singleline);

    /// <summary>Parameter values of a "DETAIL:  Parameters: $1 = 'a', $2 = NULL" line.</summary>
    public static List<string> ParseParameters(string detail) {
        var m = ParamLine.Match(detail);
        if (!m.Success) return null;
        string s = m.Groups[1].Value; var list = new List<string>(); int i = 0;
        while (i < s.Length) {
            int eq = s.IndexOf(" = ", i, StringComparison.Ordinal);
            if (eq < 0) break;
            i = eq + 3;
            if (i < s.Length && s[i] == '\'') {
                var v = new StringBuilder(); i++;
                while (i < s.Length) {
                    if (s[i] == '\'') { if (i + 1 < s.Length && s[i + 1] == '\'') { v.Append('\''); i += 2; continue; } i++; break; }
                    v.Append(s[i++]);
                }
                list.Add(v.ToString());
            } else {
                int e = s.IndexOf(", $", i, StringComparison.Ordinal); if (e < 0) e = s.Length;
                string v = s.Substring(i, e - i).Trim();
                list.Add(v == "NULL" ? null : v); i = e;
            }
            if (i < s.Length && s[i] == ',') i++;
        }
        return list;
    }

    public delegate bool SessionFilter(LogEntry e, out string user, out string database, out string application);

    /// <summary>Turns log entries into events: statement, execute (with parameters), error (with its statement).</summary>
    public static List<DbEvent> ToEvents(List<LogEntry> entries, string source, SessionFilter filter) {
        var list = new List<DbEvent>();
        var lastOfPid = new Dictionary<int, DbEvent>();
        foreach (var e in entries) {
            string user, db, app;
            if (!filter(e, out user, out db, out app)) continue;
            DbEvent last; lastOfPid.TryGetValue(e.Pid, out last);
            if (e.Level == "LOG") {
                var s = Stmt.Match(e.Message);
                if (!s.Success) continue;
                var ev = new DbEvent { Ts = e.Ts, Kind = s.Groups[1].Value.StartsWith("execute") ? "execute" : "statement", Source = source, Via = "log",
                    Session = e.Pid.ToString(CultureInfo.InvariantCulture), User = user, Database = db, Application = app, Sql = s.Groups[2].Value.TrimEnd(), Tx = e.Xid };
                list.Add(ev); lastOfPid[e.Pid] = ev;
            } else if (e.Level == "DETAIL") {
                if (last != null && last.Kind == "execute" && last.Params == null) { var p = ParseParameters(e.Message.TrimEnd()); if (p != null) last.Params = p; }
            } else if (e.Level == "ERROR" || e.Level == "FATAL") {
                var ev = new DbEvent { Ts = e.Ts, Kind = "error", Source = source, Via = "log", Session = e.Pid.ToString(CultureInfo.InvariantCulture), User = user, Database = db, Application = app,
                    Error = e.Message.TrimEnd(), Tx = e.Xid };
                list.Add(ev); lastOfPid[e.Pid] = ev;
            } else if (e.Level == "STATEMENT") {
                if (last != null && last.Kind == "error" && last.Sql == null) last.Sql = e.Message.TrimEnd();
            }
        }
        return list;
    }
}

/// <summary>Reads the text output of the test_decoding plugin of PostgreSQL.</summary>
public static class TestDecoding {
    static readonly Regex Begin = new Regex(@"^BEGIN(?: (\d+))?$");
    static readonly Regex Commit = new Regex(@"^COMMIT(?: (\d+))?(?: \(at (.+)\))?$");

    /// <summary>One event of kind "transaction" per committed transaction that changed rows.</summary>
    public static List<DbEvent> ToEvents(IEnumerable<string> lines, string source, string database) {
        var list = new List<DbEvent>(); DbEvent cur = null;
        foreach (var raw in lines) {
            string l = raw;
            Match m = Begin.Match(l);
            if (m.Success) { cur = new DbEvent { Kind = "transaction", Source = source, Via = "replication", Database = database, Tx = m.Groups[1].Success ? m.Groups[1].Value : null, Rows = new List<JO>(), Tables = new List<string>() }; continue; }
            m = Commit.Match(l);
            if (m.Success) {
                if (cur != null) {
                    if (m.Groups[2].Success) cur.Ts = ParseTime(m.Groups[2].Value);
                    if (cur.Rows.Count > 0) list.Add(cur);
                }
                cur = null; continue;
            }
            if (cur == null || !l.StartsWith("table ")) continue;
            JO row = ParseRow(l);
            if (row == null) continue;
            cur.Rows.Add(row);
            string t = row.Str("table"); if (!cur.Tables.Contains(t)) cur.Tables.Add(t);
        }
        return list;
    }

    /// <summary>"2026-11-24 18:20:31.123456+01" -> microseconds since 1970 UTC.</summary>
    public static long ParseTime(string s) {
        var m = Regex.Match(s.Trim(), @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)(?:\.(\d+))?([+-]\d\d)(?::?(\d\d))?(?::?(\d\d))?$");
        if (!m.Success) throw new FormatException("commit time not understood: " + s);
        DateTime dt = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        long us = Clock.ToUs(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        if (m.Groups[2].Success) us += long.Parse(m.Groups[2].Value.PadRight(6, '0').Substring(0, 6), CultureInfo.InvariantCulture);
        int h = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        int min = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        int sec = m.Groups[5].Success ? int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) : 0;
        long off = (Math.Abs(h) * 3600L + min * 60 + sec) * (m.Groups[3].Value[0] == '-' ? -1 : 1);
        return us - off * 1000000L;
    }

    /// <summary>"table public.customer: UPDATE: old-key: id[integer]:1 new-tuple: id[integer]:1 name[text]:'x'"</summary>
    public static JO ParseRow(string l) {
        int i = 6;
        string table = ReadName(l, ref i);
        if (i < l.Length && l[i] == '.') { i++; table += "." + ReadName(l, ref i); }
        if (i + 1 >= l.Length || l[i] != ':') return null;
        i += 2;
        int e = l.IndexOf(':', i); if (e < 0) return null;
        string op = l.Substring(i, e - i).ToLowerInvariant(); i = e + 1;
        var row = new JO("table", table, "op", op, "new", null, "old", null);
        if (op == "truncate") return row;
        JO cols = new JO(); bool isOld = false, anyOld = false, anyNew = false; JO oldCols = new JO();
        while (i < l.Length) {
            while (i < l.Length && l[i] == ' ') i++;
            if (i >= l.Length) break;
            if (string.CompareOrdinal(l, i, "old-key:", 0, 8) == 0) { isOld = true; i += 8; continue; }
            if (string.CompareOrdinal(l, i, "new-tuple:", 0, 10) == 0) { isOld = false; i += 10; continue; }
            if (l[i] == '(') break;   // (no-tuple-data)
            string col = ReadName(l, ref i);
            if (i >= l.Length || l[i] != '[') break;
            int depth = 0, ts = i;
            while (i < l.Length) { if (l[i] == '[') depth++; else if (l[i] == ']') { depth--; if (depth == 0 && i + 1 < l.Length && l[i + 1] == ':') break; } i++; }
            if (i >= l.Length) break;
            i += 2;   // "]:"
            object val;
            if (i < l.Length && l[i] == '\'') {
                var v = new StringBuilder(); i++;
                while (i < l.Length) {
                    if (l[i] == '\'') { if (i + 1 < l.Length && l[i + 1] == '\'') { v.Append('\''); i += 2; continue; } i++; break; }
                    v.Append(l[i++]);
                }
                val = v.ToString();
            } else {
                int s = i; while (i < l.Length && l[i] != ' ') i++;
                string v = l.Substring(s, i - s);
                val = v == "null" ? null : v;
            }
            if (ts < 0) break;
            if (isOld) { oldCols[col] = val; anyOld = true; } else { cols[col] = val; anyNew = true; }
        }
        if (op == "delete") { row["old"] = anyNew ? cols : (anyOld ? oldCols : null); }
        else { row["new"] = anyNew ? cols : null; row["old"] = anyOld ? oldCols : null; }
        return row;
    }

    static string ReadName(string l, ref int i) {
        if (i < l.Length && l[i] == '"') {
            var sb = new StringBuilder(); i++;
            while (i < l.Length) {
                if (l[i] == '"') { if (i + 1 < l.Length && l[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; }
                sb.Append(l[i++]);
            }
            return sb.ToString();
        }
        int s = i;
        while (i < l.Length && l[i] != '.' && l[i] != ':' && l[i] != '[' && l[i] != ' ') i++;
        return l.Substring(s, i - s);
    }
}

public class PostgresSource : IEventSource {
    public virtual string Name { get { return "postgres"; } }
    protected CaptureOptions opt; protected PgConnInfo conn; protected JO info = new JO();
    protected List<string> warnings = new List<string>();
    string logFile, localLog; long logStart; Regex lineRegex; int tzOffset; bool strict, messagesSwitched;
    readonly Dictionary<int, string[]> sessions = new Dictionary<int, string[]>();   // pid -> user, database, application, "1" when captured
    readonly List<string[]> slots = new List<string[]>();   // database, slot name (also the name of the publication), output plugin
    Thread poller; volatile bool stop; PgClient pollClient;
    public const string AppName = "eqlmaker";

    public JO Info { get { return info; } }

    protected PgClient Open(string database) {
        var c = database == null ? conn : conn.WithDatabase(database);
        return new PgClient(c.WithApplication(AppName));
    }

    string Settings(bool on) {
        var sb = new StringBuilder();
        foreach (var r in opt.Roles) {
            sb.Append("ALTER ROLE " + PgClient.Ident(r) + (on ? " SET log_statement = 'all';\n" : " RESET log_statement;\n"));
            if (messagesSwitched) sb.Append("ALTER ROLE " + PgClient.Ident(r) + (on ? " SET lc_messages = 'C';\n" : " RESET lc_messages;\n"));
        }
        foreach (var d in opt.Databases) {
            sb.Append("ALTER DATABASE " + PgClient.Ident(d) + (on ? " SET log_statement = 'all';\n" : " RESET log_statement;\n"));
            if (messagesSwitched) sb.Append("ALTER DATABASE " + PgClient.Ident(d) + (on ? " SET lc_messages = 'C';\n" : " RESET lc_messages;\n"));
        }
        return sb.ToString();
    }

    void WriteState() {
        if (string.IsNullOrEmpty(opt.StateFile)) return;
        var sl = new List<object>(); foreach (var s in slots) sl.Add(new JO("database", s[0], "slot", s[1], "plugin", s[2]));
        Json.WriteFile(opt.StateFile, new JO("source", Name, "connection", conn.ToString(), "roles", opt.Roles, "databases", opt.Databases, "lc_messages", messagesSwitched, "slots", sl));
    }

    public virtual void Start(CaptureOptions options) {
        opt = options; conn = PgConnInfo.Parse(options.Connection);
        if (opt.Roles.Count == 0 && opt.Databases.Count == 0) throw new Exception("the capture needs roles or databases (capture.roles / capture.databases)");
        using (var c = Open(null)) {
            int ver = int.Parse(c.Scalar("SHOW server_version_num"), CultureInfo.InvariantCulture);
            info["server_version"] = c.Scalar("SHOW server_version");
            if (ver < 150000) warnings.Add("PostgreSQL " + info.Str("server_version") + " is older than 15: not tested");
            if (c.Scalar("SELECT rolsuper FROM pg_roles WHERE rolname = current_user") != "t") throw new Exception("the capture connection needs a superuser (ALTER ROLE ... SET log_statement and reading the server log)");

            string prefix = c.Scalar("SHOW log_line_prefix");
            string problem = PgLog.PrefixProblem(prefix);
            if (problem != null) throw new Exception(problem);
            lineRegex = PgLog.LineRegex(prefix);
            info["log_line_prefix"] = prefix;

            string dest = c.Scalar("SHOW log_destination");
            if (!string.IsNullOrEmpty(opt.LogFile)) { localLog = Path.GetFullPath(opt.LogFile); if (!File.Exists(localLog)) throw new Exception("server log not found: " + localLog); logFile = localLog; }
            else {
                if (dest.IndexOf("stderr", StringComparison.Ordinal) < 0) throw new Exception("log_destination is '" + dest + "': the capture reads the plain-text log (stderr)");
                string cur = c.Scalar("SELECT coalesce(pg_current_logfile('stderr'), '')");
                if (cur == "") throw new Exception("the server does not write a log file itself (logging_collector is off): name the log file of the server in capture.logFile");
                logFile = cur;
            }
            logStart = LogSize(c, logFile);

            // offset of the log time zone and of the server clock
            string tz = c.Scalar("SHOW log_timezone");
            tzOffset = (int)Math.Round(double.Parse(c.Scalar("SELECT extract(epoch from ((now() AT TIME ZONE " + PgClient.Lit(tz) + ") - (now() AT TIME ZONE 'UTC')))"), CultureInfo.InvariantCulture));
            long best = long.MaxValue, offset = 0;
            for (int i = 0; i < 5; i++) {
                long t1 = Clock.NowUs();
                long server = long.Parse(c.Scalar("SELECT (extract(epoch from clock_timestamp()) * 1000000)::bigint"), CultureInfo.InvariantCulture);
                long t2 = Clock.NowUs();
                if (t2 - t1 < best) { best = t2 - t1; offset = server - (t1 + t2) / 2; }
            }
            info["server_minus_local_us"] = offset; info["clock_round_trip_us"] = best;

            string lc = c.Scalar("SHOW lc_messages");
            messagesSwitched = !(lc == "C" || lc == "POSIX" || lc.StartsWith("en", StringComparison.OrdinalIgnoreCase) || lc.StartsWith("English", StringComparison.OrdinalIgnoreCase));
            if (messagesSwitched) warnings.Add("lc_messages of the server is '" + lc + "': during the capture the captured roles/databases get lc_messages = 'C', so their log lines can be read (their error messages are English meanwhile)");
            string global = c.Scalar("SHOW log_statement");
            strict = global != "none" && global != "ddl";

            if (opt.Changes) StartChanges(c);
            WriteState();
            c.Exec(Settings(true));
        }
        var fields = new JO("ts", "milliseconds (log time), microseconds for transactions (commit time)", "session", true, "user", true, "database", true, "application", true, "sql", true,
            "params", "statements sent with parameters (extended protocol)", "tx", slots.Count > 0 ? "transactions; statements only when log_line_prefix has %x" : "only when log_line_prefix has %x",
            "tables", slots.Count > 0, "rows", slots.Count > 0);
        info["fields"] = fields; info["changes"] = slots.Count > 0 ? "on" : "off"; info["warnings"] = warnings;
        stop = false;
        pollClient = Open(null);
        Poll1();
        poller = new Thread(Poll); poller.IsBackground = true; poller.Start();
    }

    void StartChanges(PgClient c) {
        string wal = c.Scalar("SHOW wal_level");
        if (wal != "logical") { warnings.Add("changed rows are not captured: wal_level is '" + wal + "' (set wal_level = logical and restart the server once)"); return; }
        var dbs = new List<string>(opt.Databases);
        if (dbs.Count == 0) dbs.Add(conn.Database);
        foreach (var d in dbs) {
            string slot = "eqlmaker_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string plugin = opt.Decoding == "pgoutput" ? "pgoutput" : "test_decoding";
            // asked before, so that a server without the module does not log an error of this tool
            if (opt.Decoding == "auto" && !HasModule(c, "test_decoding")) plugin = "pgoutput";
            bool noModule = plugin == "pgoutput" && opt.Decoding == "auto";
            try {
                try { CreateSlot(d, slot, plugin); if (noModule) warnings.Add("the server has no test_decoding module: changed rows of database " + d + " are read with pgoutput through a publication named " + slot + ", dropped when the capture ends"); }
                catch (PgException e) {
                    // test_decoding is an optional module: servers without it are read through pgoutput, which every server has
                    if (opt.Decoding != "auto" || plugin != "test_decoding" || e.Message.IndexOf("test_decoding", StringComparison.Ordinal) < 0) throw;
                    plugin = "pgoutput"; CreateSlot(d, slot, plugin);
                    warnings.Add("the server has no test_decoding module: changed rows of database " + d + " are read with pgoutput through a publication named " + slot + ", dropped when the capture ends");
                }
                slots.Add(new[] { d, slot, plugin });
                info["decoding"] = plugin;
            } catch (Exception e) { warnings.Add("changed rows of database " + d + " are not captured: " + e.Message); }
        }
    }

    /// <summary>False only when the library directory of the server can be listed and has no such module.</summary>
    static bool HasModule(PgClient c, string name) {
        try {
            string dir = c.Scalar("SELECT setting FROM pg_config WHERE name = 'PKGLIBDIR'");
            if (string.IsNullOrEmpty(dir)) return true;
            foreach (var row in c.Query("SELECT pg_ls_dir($1)", dir).Rows) if (row[0].StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        } catch { return true; }
    }

    void CreateSlot(string database, string slot, string plugin) {
        using (var dc = Open(database)) {
            if (plugin == "pgoutput") dc.Exec("CREATE PUBLICATION " + PgClient.Ident(slot) + " FOR ALL TABLES");
            try { dc.Query("SELECT pg_create_logical_replication_slot($1, $2)", slot, plugin); }
            catch { if (plugin == "pgoutput") { try { dc.Exec("DROP PUBLICATION IF EXISTS " + PgClient.Ident(slot)); } catch { } } throw; }
        }
    }

    long LogSize(PgClient c, string file) {
        if (localLog != null) return new FileInfo(localLog).Length;
        return long.Parse(c.Query("SELECT size FROM pg_stat_file($1)", file).Scalar, CultureInfo.InvariantCulture);
    }
    string ReadLog(PgClient c, string file, long from) {
        if (localLog != null) {
            using (var fs = new FileStream(localLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                fs.Seek(Math.Min(from, fs.Length), SeekOrigin.Begin);
                return new StreamReader(fs, Encoding.UTF8).ReadToEnd();
            }
        }
        long size = LogSize(c, file);
        if (size <= from) return "";
        var sb = new StringBuilder(); long pos = from;
        while (pos < size) {
            long n = Math.Min(8L * 1024 * 1024, size - pos);
            string hex = c.Query("SELECT encode(pg_read_binary_file($1, $2::bigint, $3::bigint), 'hex')", file, pos.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture)).Scalar ?? "";
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((HexVal(hex[2 * i]) << 4) | HexVal(hex[2 * i + 1]));
            sb.Append(Latin1.GetString(bytes)); pos += n;
        }
        // bytes were carried as Latin-1 so chunk borders cannot split a character; decode as UTF-8 now
        return Encoding.UTF8.GetString(Latin1.GetBytes(sb.ToString()));
    }
    static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    static int HexVal(char c) { return c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10; }

    string Filter() {
        var f = new List<string>();
        if (opt.Roles.Count > 0) f.Add("usename IN (" + string.Join(",", opt.Roles.ConvertAll<string>(PgClient.Lit).ToArray()) + ")");
        if (opt.Databases.Count > 0) f.Add("datname IN (" + string.Join(",", opt.Databases.ConvertAll<string>(PgClient.Lit).ToArray()) + ")");
        return string.Join(" OR ", f.ToArray());
    }
    void Poll1() {
        var r = pollClient.Exec("SELECT pid, coalesce(usename, ''), coalesce(datname, ''), coalesce(application_name, ''), CASE WHEN " + Filter() + " THEN 1 ELSE 0 END FROM pg_stat_activity WHERE backend_type = 'client backend'");
        // a session that is still logging in has neither user nor database yet: it is not judged before it has them
        lock (sessions) foreach (var row in r.Rows) { if (row[1] == "" && row[2] == "") continue; sessions[int.Parse(row[0], CultureInfo.InvariantCulture)] = new[] { row[1], row[2], row[3], row[4] }; }
    }
    void Poll() {
        while (!stop) {
            try { Poll1(); } catch { }
            for (int i = 0; i < 2 && !stop; i++) Thread.Sleep(50);
        }
    }

    bool SessionOf(LogEntry e, out string user, out string db, out string app) {
        string[] s; lock (sessions) sessions.TryGetValue(e.Pid, out s);
        user = e.User; db = e.Database; app = e.Application;
        if (s != null) {
            if (user == null) user = s[0]; if (db == null) db = s[1]; if (app == null) app = s[2];
            // statements are logged for the captured roles/databases only; a session that was seen and
            // belongs to someone else, or to this tool, is skipped
            if (s[3] != "1" || s[2] == AppName) return false;
            return true;
        }
        if (app == AppName) return false;
        // a session that ended between two polls was never seen in pg_stat_activity. When exactly one
        // database (or one role) is captured, that much is known anyway; the rest stays null.
        if (user == null && db == null && !strict) {
            if (opt.Roles.Count == 0 && opt.Databases.Count == 1) db = opt.Databases[0];
            else if (opt.Databases.Count == 0 && opt.Roles.Count == 1) user = opt.Roles[0];
            return true;
        }
        // a short session that ended between two polls: kept when the log line itself names a captured
        // role/database; without that only when log_statement is not on server-wide
        if (user != null || db != null) return (user != null && opt.Roles.Contains(user)) || (db != null && opt.Databases.Contains(db));
        return !strict;
    }

    public virtual List<DbEvent> Stop() {
        stop = true; if (poller != null) poller.Join(3000); poller = null;
        try { Poll1(); } catch { }
        if (pollClient != null) { pollClient.Dispose(); pollClient = null; }
        var list = new List<DbEvent>();
        using (var c = Open(null)) {
            c.Exec(Settings(false));
            string text;
            string cur = localLog != null ? logFile : c.Scalar("SELECT coalesce(pg_current_logfile('stderr'), '')");
            if (cur == logFile || cur == "") text = ReadLog(c, logFile, logStart);
            else text = ReadLog(c, logFile, logStart) + "\n" + ReadLog(c, cur, 0);   // the log file was rotated meanwhile
            var entries = PgLog.Parse(text, lineRegex, tzOffset);
            list.AddRange(PgLog.ToEvents(entries, Name, SessionOf));
        }
        foreach (var s in slots) {
            try {
                using (var dc = Open(s[0])) {
                    if (s[2] == "pgoutput") {
                        var r = dc.Query("SELECT data FROM pg_logical_slot_get_binary_changes($1, NULL, NULL, 'proto_version', '1', 'publication_names', $2)", s[1], s[1]);
                        var msgs = new List<byte[]>(); foreach (var row in r.Rows) msgs.Add(PgOutput.FromHex(row[0]));
                        list.AddRange(PgOutput.ToEvents(msgs, Name, s[0]));
                        dc.Query("SELECT pg_drop_replication_slot($1)", s[1]);
                        dc.Exec("DROP PUBLICATION IF EXISTS " + PgClient.Ident(s[1]));
                    } else {
                        var r = dc.Query("SELECT data FROM pg_logical_slot_get_changes($1, NULL, NULL, 'include-xids', '1', 'include-timestamp', '1', 'skip-empty-xacts', '1')", s[1]);
                        var lines = new List<string>(); foreach (var row in r.Rows) lines.Add(row[0]);
                        list.AddRange(TestDecoding.ToEvents(lines, Name, s[0]));
                        dc.Query("SELECT pg_drop_replication_slot($1)", s[1]);
                    }
                }
            } catch (Exception e) { warnings.Add("changed rows of database " + s[0] + " could not be read: " + e.Message); }
        }
        slots.Clear();
        if (!string.IsNullOrEmpty(opt.StateFile)) { try { File.Delete(opt.StateFile); } catch { } }
        info["warnings"] = warnings;
        DbEvent.SortByTime(list);
        return list;
    }

    /// <summary>Resets log_statement and drops replication slots left by an aborted capture.</summary>
    public virtual void Reset(CaptureOptions options) {
        opt = options; conn = PgConnInfo.Parse(options.Connection);
        var drop = new List<string[]>();
        if (!string.IsNullOrEmpty(options.StateFile) && File.Exists(options.StateFile)) {
            var st = Json.ReadFile(options.StateFile);
            foreach (var r in st.Strings("roles")) if (!opt.Roles.Contains(r)) opt.Roles.Add(r);
            foreach (var d in st.Strings("databases")) if (!opt.Databases.Contains(d)) opt.Databases.Add(d);
            messagesSwitched = st.Bool("lc_messages", false);
            if (st.Arr("slots") != null) foreach (JO s in st.Arr("slots")) drop.Add(new[] { s.Str("database"), s.Str("slot") });
        }
        using (var c = Open(null)) {
            string s = Settings(false);
            if (s.Length > 0) c.Exec(s);
            // slots of this tool that are not in use any more
            var r = c.Exec("SELECT database, slot_name FROM pg_replication_slots WHERE slot_name LIKE 'eqlmaker\\_%' AND NOT active");
            foreach (var row in r.Rows) if (!drop.Exists(delegate (string[] x) { return x[1] == row[1]; })) drop.Add(new[] { row[0], row[1] });
        }
        foreach (var s in drop) {
            try { using (var dc = Open(s[0])) { dc.Query("SELECT pg_drop_replication_slot($1)", s[1]); } } catch { }
            try { using (var dc = Open(s[0])) dc.Exec("DROP PUBLICATION IF EXISTS " + PgClient.Ident(s[1])); } catch { }
        }
        if (!string.IsNullOrEmpty(options.StateFile)) { try { File.Delete(options.StateFile); } catch { } }
    }
}
}
