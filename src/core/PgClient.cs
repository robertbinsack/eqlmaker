// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Minimal PostgreSQL client (frontend/backend protocol 3.0): simple and parameterised queries in
// text format; trust, clear-text, MD5 and SCRAM-SHA-256 authentication; optional TLS.
// Enough for EQLMaker and the sample application, without any external driver.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EqlMaker {

public class PgException : Exception {
    public string Severity, Code, Detail;
    public PgException(string message) : base(message) { }
    public PgException(string severity, string code, string message, string detail) : base(message) { Severity = severity; Code = code; Detail = detail; }
}

public class PgResult {
    public string[] Columns = new string[0];
    public List<string[]> Rows = new List<string[]>();
    /// <summary>Command tag, e.g. "INSERT 0 1" or "SELECT 3".</summary>
    public string Tag = "";
    /// <summary>Rows affected according to the command tag (-1 when the tag has no count).</summary>
    public int Affected {
        get {
            var p = Tag.Split(' '); int n;
            return p.Length > 1 && int.TryParse(p[p.Length - 1], out n) ? n : -1;
        }
    }
    public string Scalar { get { return Rows.Count > 0 && Rows[0].Length > 0 ? Rows[0][0] : null; } }
    public int Col(string name) { for (int i = 0; i < Columns.Length; i++) if (Columns[i] == name) return i; throw new PgException("no column " + name); }
}

/// <summary>Connection settings in libpq style: "host=127.0.0.1 port=5432 user=app dbname=shop".</summary>
public class PgConnInfo {
    public string Host = "127.0.0.1", User = "postgres", Database = null, Password = null, Application = "eqlmaker", SslMode = "prefer";
    /// <summary>Password file in pgpass format named in the connection string (passfile=...).</summary>
    public string PassFile = null;
    public int Port = 5432;

    public static PgConnInfo Parse(string s) {
        var c = new PgConnInfo();
        s = (s ?? "").Trim();
        if (s.StartsWith("postgresql://") || s.StartsWith("postgres://")) {
            var u = new Uri(s);
            if (u.Host != "") c.Host = u.Host;
            if (u.Port > 0) c.Port = u.Port;
            if (u.UserInfo != "") { var up = u.UserInfo.Split(new[] { ':' }, 2); c.User = Uri.UnescapeDataString(up[0]); if (up.Length > 1) c.Password = Uri.UnescapeDataString(up[1]); }
            if (u.AbsolutePath.Length > 1) c.Database = Uri.UnescapeDataString(u.AbsolutePath.Substring(1));
            foreach (var kv in u.Query.TrimStart('?').Split('&')) { var p = kv.Split(new[] { '=' }, 2); if (p.Length == 2) c.Set(p[0], Uri.UnescapeDataString(p[1])); }
        } else {
            int i = 0;
            while (i < s.Length) {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                int k = i; while (i < s.Length && s[i] != '=' && !char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) { if (i > k) throw new PgException("connection string: '=' expected after " + s.Substring(k)); break; }
                string key = s.Substring(k, i - k);
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length || s[i] != '=') throw new PgException("connection string: '=' expected after " + key);
                i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                var v = new StringBuilder();
                if (i < s.Length && s[i] == '\'') {
                    i++;
                    while (i < s.Length && s[i] != '\'') { if (s[i] == '\\' && i + 1 < s.Length) i++; v.Append(s[i++]); }
                    i++;
                } else while (i < s.Length && !char.IsWhiteSpace(s[i])) { if (s[i] == '\\' && i + 1 < s.Length) i++; v.Append(s[i++]); }
                c.Set(key, v.ToString());
            }
        }
        if (c.Database == null) c.Database = c.User;
        return c;
    }

    void Set(string key, string v) {
        switch (key) {
            case "host": case "hostaddr": c_host(v); break;
            case "port": Port = int.Parse(v, CultureInfo.InvariantCulture); break;
            case "user": User = v; break;
            case "dbname": case "database": Database = v; break;
            case "password": Password = v; break;
            case "application_name": Application = v; break;
            case "sslmode": SslMode = v; break;
            case "passfile": PassFile = v; break;
            default: break;   // other libpq keywords are ignored
        }
    }
    void c_host(string v) { Host = v == "localhost" ? "127.0.0.1" : v; }

    /// <summary>Password, in this order: connection string, environment EQLMAKER_PGPASSWORD, the password file
    /// named in the connection string, environment PGPASSWORD, the pgpass file of the user.</summary>
    public string ResolvePassword() {
        if (Password != null) return Password;
        string e = AdminPassword();
        if (!string.IsNullOrEmpty(e)) return e;
        if (!string.IsNullOrEmpty(PassFile)) { string p = FromPassFile(PassFile); if (p != null) return p; }
        e = Environment.GetEnvironmentVariable("PGPASSWORD");
        if (!string.IsNullOrEmpty(e)) return e;
        string file = Environment.GetEnvironmentVariable("PGPASSFILE");
        if (string.IsNullOrEmpty(file)) file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "postgresql", "pgpass.conf");
        return FromPassFile(file) ?? "";
    }

    static string adminPassword; static bool adminRead;
    /// <summary>The password from the environment variable EQLMAKER_PGPASSWORD. It is read once and then removed
    /// from the environment of this process, so that programs started later (the recorded application)
    /// do not inherit it.</summary>
    public static string AdminPassword() {
        if (!adminRead) {
            adminPassword = Environment.GetEnvironmentVariable("EQLMAKER_PGPASSWORD"); adminRead = true;
            if (!string.IsNullOrEmpty(adminPassword)) Environment.SetEnvironmentVariable("EQLMAKER_PGPASSWORD", null);
        }
        return adminPassword;
    }
    public static void SetAdminPassword(string password) { adminPassword = password; adminRead = true; }

    string FromPassFile(string file) {
        if (File.Exists(file)) {
            foreach (var line in File.ReadAllLines(file)) {
                if (line.StartsWith("#")) continue;
                var p = line.Split(':');
                if (p.Length < 5) continue;
                if ((p[0] == "*" || p[0] == Host || (p[0] == "localhost" && Host == "127.0.0.1")) && (p[1] == "*" || p[1] == Port.ToString()) && (p[2] == "*" || p[2] == Database) && (p[3] == "*" || p[3] == User))
                    return string.Join(":", p, 4, p.Length - 4);
            }
        }
        return null;
    }

    public PgConnInfo WithDatabase(string db) { var c = (PgConnInfo)MemberwiseClone(); c.Database = db; return c; }
    public PgConnInfo WithApplication(string app) { var c = (PgConnInfo)MemberwiseClone(); c.Application = app; return c; }
    public override string ToString() { return "host=" + Host + " port=" + Port + " user=" + User + " dbname=" + Database; }
}

public class PgClient : IDisposable {
    TcpClient tcp; Stream stream;
    public readonly Dictionary<string, string> ServerParameters = new Dictionary<string, string>();
    public int BackendPid;
    public List<string> Notices = new List<string>();
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    public PgClient(string connectionString) : this(PgConnInfo.Parse(connectionString)) { }

    public PgClient(PgConnInfo c) {
        tcp = new TcpClient();
        tcp.NoDelay = true;
        tcp.Connect(c.Host, c.Port);
        stream = tcp.GetStream();
        if (c.SslMode == "require" || c.SslMode == "verify-ca" || c.SslMode == "verify-full") {
            var req = new byte[8]; PutInt(req, 0, 8); PutInt(req, 4, 80877103);
            stream.Write(req, 0, 8);
            int answer = stream.ReadByte();
            if (answer != 'S') throw new PgException("the server does not accept TLS (sslmode=" + c.SslMode + ")");
            bool verify = c.SslMode != "require";
            var ssl = new SslStream(stream, false, delegate (object o, System.Security.Cryptography.X509Certificates.X509Certificate cert, System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors err) {
                return !verify || err == SslPolicyErrors.None || (c.SslMode == "verify-ca" && err == SslPolicyErrors.RemoteCertificateNameMismatch);
            });
            ssl.AuthenticateAsClient(c.Host);
            stream = ssl;
        }
        stream = new BufferedStream(stream, 16384);
        var ms = new MemoryStream();
        WriteInt(ms, 196608);
        WriteZ(ms, "user"); WriteZ(ms, c.User);
        WriteZ(ms, "database"); WriteZ(ms, c.Database);
        WriteZ(ms, "application_name"); WriteZ(ms, c.Application ?? "");
        WriteZ(ms, "client_encoding"); WriteZ(ms, "UTF8");
        ms.WriteByte(0);
        var body = ms.ToArray();
        var head = new byte[4]; PutInt(head, 0, body.Length + 4);
        stream.Write(head, 0, 4); stream.Write(body, 0, body.Length); stream.Flush();
        Authenticate(c);
    }

    // ------------------------------------------------------------ wire helpers
    static void PutInt(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void WriteInt(Stream s, int v) { var b = new byte[4]; PutInt(b, 0, v); s.Write(b, 0, 4); }
    static void WriteShort(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    static void WriteZ(Stream s, string v) { var b = Utf8.GetBytes(v); s.Write(b, 0, b.Length); s.WriteByte(0); }

    void Send(char type, byte[] body) {
        var head = new byte[5]; head[0] = (byte)type; PutInt(head, 1, body.Length + 4);
        stream.Write(head, 0, 5); stream.Write(body, 0, body.Length);
    }
    void ReadFull(byte[] b, int n) {
        int got = 0;
        while (got < n) { int r = stream.Read(b, got, n - got); if (r <= 0) throw new PgException("the server closed the connection"); got += r; }
    }
    byte[] Receive(out char type) {
        var head = new byte[5]; ReadFull(head, 5);
        type = (char)head[0];
        int len = ((head[1] << 24) | (head[2] << 16) | (head[3] << 8) | head[4]) - 4;
        var body = new byte[len]; ReadFull(body, len);
        return body;
    }
    static int Int(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }
    static string Z(byte[] b, ref int o) { int s = o; while (o < b.Length && b[o] != 0) o++; string v = Utf8.GetString(b, s, o - s); o++; return v; }

    static PgException Error(byte[] b) {
        string sev = "", code = "", msg = "", detail = null; int o = 0;
        while (o < b.Length && b[o] != 0) {
            char f = (char)b[o++]; string v = Z(b, ref o);
            if (f == 'S') sev = v; else if (f == 'C') code = v; else if (f == 'M') msg = v; else if (f == 'D') detail = v;
        }
        return new PgException(sev, code, msg, detail);
    }

    // ------------------------------------------------------------ authentication
    void Authenticate(PgConnInfo c) {
        string password = null; byte[] serverSignature = null; string clientFirstBare = null, nonce = null;
        while (true) {
            char t; var b = Receive(out t);
            if (t == 'E') throw Error(b);
            if (t == 'S') { int o = 0; string k = Z(b, ref o); ServerParameters[k] = Z(b, ref o); continue; }
            if (t == 'K') { BackendPid = Int(b, 0); continue; }
            if (t == 'N') continue;
            if (t == 'Z') return;
            if (t != 'R') continue;
            int code = Int(b, 0);
            if (code == 0) continue;
            if (password == null) password = c.ResolvePassword();
            if (code == 3) { var ms = new MemoryStream(); WriteZ(ms, password); Send('p', ms.ToArray()); stream.Flush(); }
            else if (code == 5) {
                var salt = new byte[4]; Array.Copy(b, 4, salt, 0, 4);
                string inner = Hex(Md5(Utf8.GetBytes(password + c.User)));
                var buf = new byte[inner.Length + 4]; Encoding.ASCII.GetBytes(inner).CopyTo(buf, 0); salt.CopyTo(buf, inner.Length);
                var ms = new MemoryStream(); WriteZ(ms, "md5" + Hex(Md5(buf))); Send('p', ms.ToArray()); stream.Flush();
            } else if (code == 10) {
                int o = 4; bool scram = false;
                while (o < b.Length && b[o] != 0) if (Z(b, ref o) == "SCRAM-SHA-256") scram = true;
                if (!scram) throw new PgException("the server offers no supported SASL mechanism");
                var rnd = new byte[18]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(rnd);
                nonce = Convert.ToBase64String(rnd);
                clientFirstBare = "n=,r=" + nonce;
                var first = Encoding.ASCII.GetBytes("n,," + clientFirstBare);
                var ms = new MemoryStream(); WriteZ(ms, "SCRAM-SHA-256"); WriteInt(ms, first.Length); ms.Write(first, 0, first.Length);
                Send('p', ms.ToArray()); stream.Flush();
            } else if (code == 11) {
                string serverFirst = Encoding.ASCII.GetString(b, 4, b.Length - 4);
                string r = null, s = null; int iter = 4096;
                foreach (var part in serverFirst.Split(',')) {
                    if (part.StartsWith("r=")) r = part.Substring(2); else if (part.StartsWith("s=")) s = part.Substring(2); else if (part.StartsWith("i=")) iter = int.Parse(part.Substring(2), CultureInfo.InvariantCulture);
                }
                if (r == null || s == null || !r.StartsWith(nonce)) throw new PgException("SCRAM: unexpected answer of the server");
                byte[] salted = Pbkdf2(Utf8.GetBytes(password.Normalize(NormalizationForm.FormKC)), Convert.FromBase64String(s), iter);
                byte[] clientKey = Hmac(salted, Encoding.ASCII.GetBytes("Client Key"));
                byte[] storedKey; using (var sha = SHA256.Create()) storedKey = sha.ComputeHash(clientKey);
                string finalNoProof = "c=biws,r=" + r;
                byte[] auth = Encoding.ASCII.GetBytes(clientFirstBare + "," + serverFirst + "," + finalNoProof);
                byte[] sig = Hmac(storedKey, auth);
                var proof = new byte[clientKey.Length]; for (int i = 0; i < proof.Length; i++) proof[i] = (byte)(clientKey[i] ^ sig[i]);
                serverSignature = Hmac(Hmac(salted, Encoding.ASCII.GetBytes("Server Key")), auth);
                Send('p', Encoding.ASCII.GetBytes(finalNoProof + ",p=" + Convert.ToBase64String(proof))); stream.Flush();
            } else if (code == 12) {
                string fin = Encoding.ASCII.GetString(b, 4, b.Length - 4);
                if (serverSignature == null || fin != "v=" + Convert.ToBase64String(serverSignature)) throw new PgException("SCRAM: the server could not prove that it knows the password");
            } else throw new PgException("authentication method " + code + " is not supported");
        }
    }
    static byte[] Md5(byte[] b) { using (var m = MD5.Create()) return m.ComputeHash(b); }
    static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
    static byte[] Hmac(byte[] key, byte[] data) { using (var h = new HMACSHA256(key)) return h.ComputeHash(data); }
    static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations) {
        var block = new byte[salt.Length + 4]; salt.CopyTo(block, 0); block[block.Length - 1] = 1;
        using (var h = new HMACSHA256(password)) {
            byte[] u = h.ComputeHash(block); var res = (byte[])u.Clone();
            for (int i = 1; i < iterations; i++) { u = h.ComputeHash(u); for (int j = 0; j < res.Length; j++) res[j] ^= u[j]; }
            return res;
        }
    }

    // ------------------------------------------------------------ queries
    /// <summary>Simple query protocol; several statements separated by ';' are allowed. Returns the last result.</summary>
    public PgResult Exec(string sql) {
        var ms = new MemoryStream(); WriteZ(ms, sql);
        Send('Q', ms.ToArray()); stream.Flush();
        return ReadResults();
    }

    /// <summary>Extended query protocol with text parameters ($1, $2, ...); null is SQL NULL.</summary>
    public PgResult Query(string sql, params string[] args) {
        if (args == null) args = new string[0];
        var ms = new MemoryStream(); ms.WriteByte(0); WriteZ(ms, sql); WriteShort(ms, 0);
        Send('P', ms.ToArray());
        ms = new MemoryStream(); ms.WriteByte(0); ms.WriteByte(0); WriteShort(ms, 0); WriteShort(ms, args.Length);
        foreach (var a in args) {
            if (a == null) { WriteInt(ms, -1); continue; }
            var b = Utf8.GetBytes(a); WriteInt(ms, b.Length); ms.Write(b, 0, b.Length);
        }
        WriteShort(ms, 0);
        Send('B', ms.ToArray());
        Send('D', new byte[] { (byte)'P', 0 });
        ms = new MemoryStream(); ms.WriteByte(0); WriteInt(ms, 0);
        Send('E', ms.ToArray());
        Send('S', new byte[0]); stream.Flush();
        return ReadResults();
    }

    public string Scalar(string sql, params string[] args) { return (args == null || args.Length == 0 ? Exec(sql) : Query(sql, args)).Scalar; }

    PgResult ReadResults() {
        PgResult cur = new PgResult(), last = cur; PgException err = null;
        while (true) {
            char t; var b = Receive(out t);
            switch (t) {
                case 'T': {
                    cur = new PgResult(); last = cur;
                    int n = (b[0] << 8) | b[1], o = 2; cur.Columns = new string[n];
                    for (int i = 0; i < n; i++) { cur.Columns[i] = Z(b, ref o); o += 18; }
                    break;
                }
                case 'D': {
                    int n = (b[0] << 8) | b[1], o = 2; var row = new string[n];
                    for (int i = 0; i < n; i++) { int len = Int(b, o); o += 4; if (len < 0) row[i] = null; else { row[i] = Utf8.GetString(b, o, len); o += len; } }
                    cur.Rows.Add(row);
                    break;
                }
                case 'C': { int o = 0; cur.Tag = Z(b, ref o); last = cur; cur = new PgResult(); break; }
                case 'E': if (err == null) err = Error(b); break;
                case 'N': { var n = Error(b); Notices.Add(n.Message); break; }
                case 'S': { int o = 0; string k = Z(b, ref o); ServerParameters[k] = Z(b, ref o); break; }
                case 'Z': if (err != null) throw err; return last;
                default: break;   // ParseComplete, BindComplete, NoData, EmptyQuery, ...
            }
        }
    }

    public static string Ident(string s) { return "\"" + s.Replace("\"", "\"\"") + "\""; }
    public static string Lit(string s) { return s == null ? "NULL" : "'" + s.Replace("'", "''") + "'"; }

    public void Dispose() {
        try { if (stream != null) { Send('X', new byte[0]); stream.Flush(); } } catch { }
        try { if (stream != null) stream.Dispose(); } catch { }
        try { if (tcp != null) tcp.Close(); } catch { }
        stream = null; tcp = null;
    }
}
}
