// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// JSON helpers: ordered objects, a compact writer with stable key order, and a small parser.
// No external library; the same code runs in the engine, the tests and the sample application.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EqlMaker {

/// <summary>JSON object that keeps the order in which keys were added.</summary>
public class JO : IEnumerable<KeyValuePair<string, object>> {
    readonly List<string> keys = new List<string>();
    readonly Dictionary<string, object> map = new Dictionary<string, object>();

    public JO() { }
    /// <summary>JO("a", 1, "b", "x")</summary>
    public JO(params object[] kv) { for (int i = 0; i + 1 < kv.Length; i += 2) this[(string)kv[i]] = kv[i + 1]; }

    public object this[string key] {
        get { object v; return map.TryGetValue(key, out v) ? v : null; }
        set { if (!map.ContainsKey(key)) keys.Add(key); map[key] = value; }
    }
    public bool Has(string key) { return map.ContainsKey(key); }
    public void Remove(string key) { if (map.Remove(key)) keys.Remove(key); }
    public int Count { get { return keys.Count; } }
    public IList<string> Keys { get { return keys; } }

    public string Str(string key) { return Str(key, null); }
    public string Str(string key, string def) { object v = this[key]; return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
    public int Int(string key, int def) { object v = this[key]; return v == null ? def : Convert.ToInt32(v, CultureInfo.InvariantCulture); }
    public long Long(string key, long def) { object v = this[key]; return v == null ? def : Convert.ToInt64(v, CultureInfo.InvariantCulture); }
    public double Num(string key, double def) { object v = this[key]; return v == null ? def : Convert.ToDouble(v, CultureInfo.InvariantCulture); }
    public bool Bool(string key, bool def) { object v = this[key]; if (v == null) return def; if (v is bool) return (bool)v; string s = Convert.ToString(v, CultureInfo.InvariantCulture); return s == "true" || s == "1"; }
    public JO Obj(string key) { return this[key] as JO; }
    public List<object> Arr(string key) { return this[key] as List<object>; }
    /// <summary>Array of strings; a comma-separated string is accepted too.</summary>
    public List<string> Strings(string key) {
        var l = new List<string>(); object v = this[key];
        if (v == null) return l;
        if (v is string) { foreach (var s in ((string)v).Split(',')) if (s.Trim() != "") l.Add(s.Trim()); return l; }
        foreach (var o in (IEnumerable)v) l.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
        return l;
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { foreach (var k in keys) yield return new KeyValuePair<string, object>(k, map[k]); }
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
}

public static class Json {
    // ---------------------------------------------------------------- writer
    public static string Write(object v) { var sb = new StringBuilder(); Write(sb, v, -1, 0); return sb.ToString(); }
    public static string Pretty(object v) { var sb = new StringBuilder(); Write(sb, v, 2, 0); sb.Append('\n'); return sb.ToString(); }

    static void Nl(StringBuilder sb, int indent, int depth) { if (indent >= 0) { sb.Append('\n'); sb.Append(' ', indent * depth); } }

    static void Write(StringBuilder sb, object v, int indent, int depth) {
        if (v == null) { sb.Append("null"); return; }
        if (v is string) { Quote(sb, (string)v); return; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
        if (v is int || v is long || v is short || v is byte || v is uint || v is ulong) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
        if (v is double || v is float) {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
        }
        if (v is decimal) { sb.Append(((decimal)v).ToString(CultureInfo.InvariantCulture)); return; }
        if (v is JO) {
            var o = (JO)v;
            if (o.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{'); bool first = true;
            foreach (var kv in o) {
                if (!first) sb.Append(','); first = false;
                Nl(sb, indent, depth + 1);
                Quote(sb, kv.Key); sb.Append(indent >= 0 ? ": " : ":");
                Write(sb, kv.Value, indent, depth + 1);
            }
            Nl(sb, indent, depth); sb.Append('}'); return;
        }
        if (v is IDictionary) {
            var o = new JO();
            foreach (DictionaryEntry e in (IDictionary)v) o[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
            Write(sb, o, indent, depth); return;
        }
        if (v is IEnumerable) {
            sb.Append('['); bool first = true; bool any = false;
            foreach (var x in (IEnumerable)v) {
                if (!first) sb.Append(','); first = false; any = true;
                Nl(sb, indent, depth + 1);
                Write(sb, x, indent, depth + 1);
            }
            if (any) Nl(sb, indent, depth);
            sb.Append(']'); return;
        }
        Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
    }

    public static void Quote(StringBuilder sb, string s) {
        sb.Append('"');
        foreach (char c in s) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x2028 || c == 0x2029) sb.Append("\\u" + ((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // ---------------------------------------------------------------- parser
    /// <summary>Parses JSON into JO, List&lt;object&gt;, string, long, double, bool or null.</summary>
    public static object Parse(string text) {
        int i = 0;
        object v = Value(text, ref i);
        Ws(text, ref i);
        if (i != text.Length) throw new FormatException("JSON: unexpected text at position " + i);
        return v;
    }
    public static JO ParseObject(string text) {
        var o = Parse(text) as JO;
        if (o == null) throw new FormatException("JSON: an object was expected");
        return o;
    }
    public static JO ReadFile(string path) { return ParseObject(File.ReadAllText(path, Encoding.UTF8)); }
    public static void WriteFile(string path, object v) { File.WriteAllText(path, Pretty(v), new UTF8Encoding(false)); }

    /// <summary>Reads a JSON Lines file: one object per non-empty line.</summary>
    public static List<JO> ReadLines(string path) {
        var list = new List<JO>();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs, Encoding.UTF8)) {
            string line; int n = 0;
            while ((line = sr.ReadLine()) != null) {
                n++;
                if (line.Trim().Length == 0) continue;
                try { list.Add(ParseObject(line)); }
                catch (FormatException e) { throw new FormatException(path + " line " + n + ": " + e.Message); }
            }
        }
        return list;
    }
    public static void WriteLines(string path, IEnumerable<JO> items) {
        var sb = new StringBuilder();
        foreach (var o in items) { sb.Append(Write(o)); sb.Append('\n'); }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++; }

    static object Value(string s, ref int i) {
        Ws(s, ref i);
        if (i >= s.Length) throw new FormatException("JSON: unexpected end");
        char c = s[i];
        if (c == '{') {
            var o = new JO(); i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true) {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON: key expected at position " + i);
                string k = String(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: ':' expected at position " + i);
                i++;
                o[k] = Value(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return o; }
                throw new FormatException("JSON: ',' or '}' expected at position " + i);
            }
        }
        if (c == '[') {
            var a = new List<object>(); i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true) {
                a.Add(Value(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw new FormatException("JSON: ',' or ']' expected at position " + i);
            }
        }
        if (c == '"') return String(s, ref i);
        if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int st = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (i == st) throw new FormatException("JSON: unexpected character '" + c + "' at position " + i);
        string num = s.Substring(st, i - st);
        long l;
        if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
        double d;
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw new FormatException("JSON: bad number at position " + st);
        return d;
    }

    static string String(string s, ref int i) {
        var sb = new StringBuilder(); i++;
        while (true) {
            if (i >= s.Length) throw new FormatException("JSON: unterminated string");
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) throw new FormatException("JSON: unterminated string");
            c = s[i++];
            switch (c) {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new FormatException("JSON: bad \\u escape");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                default: sb.Append(c); break;
            }
        }
    }
}
}
