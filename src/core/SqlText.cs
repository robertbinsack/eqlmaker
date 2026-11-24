// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Light-weight reading of SQL text: verb, referenced tables, and a "shape" in which literal values
// are replaced by placeholders so that equal statements with different values group together.
// This is a lexer, not a parser: it is good enough to group and label statements and says so.
using System;
using System.Collections.Generic;
using System.Text;

namespace EqlMaker {

public static class SqlText {
    class Tok { public char Kind; public string Text; }   // w word, q quoted identifier, s string, n number, p punctuation, $ parameter

    static List<Tok> Lex(string sql) {
        var t = new List<Tok>(); int i = 0, n = sql.Length;
        while (i < n) {
            char c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < n && sql[i + 1] == '-') { while (i < n && sql[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < n && sql[i + 1] == '*') { int e = sql.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? n : e + 2; continue; }
            if (c == '\'' || ((c == 'E' || c == 'e' || c == 'N' || c == 'n') && i + 1 < n && sql[i + 1] == '\'')) {
                bool esc = c == 'E' || c == 'e'; if (c != '\'') i++;
                int s = i; i++;
                while (i < n) {
                    if (esc && sql[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (sql[i] == '\'') { if (i + 1 < n && sql[i + 1] == '\'') { i += 2; continue; } i++; break; }
                    i++;
                }
                t.Add(new Tok { Kind = 's', Text = sql.Substring(s, Math.Min(i, n) - s) }); continue;
            }
            if (c == '"' || c == '[' && LooksLikeBracketName(sql, i) || c == '`') {
                char close = c == '[' ? ']' : c; int s = i + 1; i++;
                var sb = new StringBuilder();
                while (i < n) { if (sql[i] == close) { if (close == '"' && i + 1 < n && sql[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; } sb.Append(sql[i++]); }
                if (s > n) s = n;
                t.Add(new Tok { Kind = 'q', Text = sb.ToString() }); continue;
            }
            if (c == '$' && i + 1 < n && char.IsDigit(sql[i + 1])) { int s = i; i++; while (i < n && char.IsDigit(sql[i])) i++; t.Add(new Tok { Kind = '$', Text = sql.Substring(s, i - s) }); continue; }
            if (c == '$') {   // dollar-quoted string
                int e = sql.IndexOf('$', i + 1);
                if (e > i) { string tag = sql.Substring(i, e - i + 1); int close = sql.IndexOf(tag, e + 1, StringComparison.Ordinal); if (close > 0) { t.Add(new Tok { Kind = 's', Text = sql.Substring(i, close + tag.Length - i) }); i = close + tag.Length; continue; } }
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(sql[i + 1]))) {
                int s = i; while (i < n && (char.IsLetterOrDigit(sql[i]) || sql[i] == '.')) i++;
                t.Add(new Tok { Kind = 'n', Text = sql.Substring(s, i - s) }); continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#') {
                int s = i; i++; while (i < n && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$' || sql[i] == '@' || sql[i] == '#')) i++;
                t.Add(new Tok { Kind = 'w', Text = sql.Substring(s, i - s) }); continue;
            }
            t.Add(new Tok { Kind = 'p', Text = c.ToString() }); i++;
        }
        return t;
    }
    static bool LooksLikeBracketName(string s, int i) { int e = s.IndexOf(']', i + 1); return e > i + 1 && e - i < 130 && s.IndexOf('\n', i, e - i) < 0 && (i == 0 || !char.IsLetterOrDigit(s[i - 1])); }

    /// <summary>First keyword in lower case: select, insert, update, delete, begin, commit, ...; "with" resolves to the main verb when it can be seen.</summary>
    public static string Verb(string sql) {
        var t = Lex(sql ?? "");
        if (t.Count == 0 || t[0].Kind != 'w') return "";
        string v = t[0].Text.ToLowerInvariant();
        if (v == "with") { foreach (var k in t) if (k.Kind == 'w') { string w = k.Text.ToLowerInvariant(); if (w == "insert" || w == "update" || w == "delete" || w == "merge") return w; } return "select"; }
        if (v == "start") return "begin";
        if (v == "end") return "commit";
        if (v == "abort") return "rollback";
        return v;
    }
    public static bool IsWrite(string verb) { return verb == "insert" || verb == "update" || verb == "delete" || verb == "merge" || verb == "truncate" || verb == "copy" || verb == "call" || verb == "do"; }
    public static bool IsTransactionControl(string verb) { return verb == "begin" || verb == "commit" || verb == "rollback" || verb == "savepoint" || verb == "release"; }

    static readonly HashSet<string> NotATable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "select", "lateral", "only", "unnest", "generate_series", "values", "set", "where", "group", "order", "limit", "on", "using", "as", "returning", "default" };

    /// <summary>Tables named after FROM, JOIN, INTO, UPDATE, DELETE FROM, TRUNCATE (schema-qualified when written so), in order, without duplicates.</summary>
    public static List<string> Tables(string sql) {
        var t = Lex(sql ?? ""); var list = new List<string>();
        string verb = Verb(sql);
        for (int i = 0; i < t.Count; i++) {
            if (t[i].Kind != 'w') continue;
            string w = t[i].Text.ToLowerInvariant(); bool lead = false, listAllowed = false;
            if (w == "from") { lead = true; listAllowed = true; if (i > 0 && t[i - 1].Kind == 'w' && (t[i - 1].Text.ToLowerInvariant() == "distinct" || t[i - 1].Text.ToLowerInvariant() == "extract")) lead = false; }
            else if (w == "join" || w == "into") lead = true;
            else if (w == "update" && i == FirstWord(t)) lead = true;
            else if ((w == "truncate" || w == "table") && verb == "truncate") lead = w == "truncate";
            if (!lead) continue;
            int j = i + 1;
            while (true) {
                if (j < t.Count && t[j].Kind == 'w' && (t[j].Text.ToLowerInvariant() == "only" || t[j].Text.ToLowerInvariant() == "table")) j++;
                string name = Name(t, ref j);
                if (name == null) break;
                if ((w == "from" || w == "join") && j < t.Count && t[j].Kind == 'p' && t[j].Text == "(") break;   // a function call, not a table
                if (!list.Contains(name)) list.Add(name);
                if (!listAllowed) break;
                // skip an alias, then continue after a comma
                if (j < t.Count && t[j].Kind == 'w' && t[j].Text.ToLowerInvariant() == "as") j++;
                if (j < t.Count && (t[j].Kind == 'w' || t[j].Kind == 'q') && !IsClause(t[j])) j++;
                if (j < t.Count && t[j].Kind == 'p' && t[j].Text == ",") { j++; continue; }
                break;
            }
        }
        return list;
    }
    static int FirstWord(List<Tok> t) { for (int i = 0; i < t.Count; i++) if (t[i].Kind == 'w') return i; return -1; }
    static bool IsClause(Tok k) {
        if (k.Kind != 'w') return false;
        string w = k.Text.ToLowerInvariant();
        return w == "where" || w == "join" || w == "inner" || w == "left" || w == "right" || w == "full" || w == "cross" || w == "natural" || w == "on" || w == "group" || w == "order" || w == "limit" || w == "having" || w == "union" || w == "set" || w == "using" || w == "returning" || w == "for" || w == "offset" || w == "window" || w == "fetch" || w == "values" || w == "select" || w == "default" || w == "except" || w == "intersect";
    }
    static string Name(List<Tok> t, ref int j) {
        if (j >= t.Count || (t[j].Kind != 'w' && t[j].Kind != 'q')) return null;
        if (t[j].Kind == 'w' && NotATable.Contains(t[j].Text)) return null;
        string name = t[j].Kind == 'w' ? t[j].Text.ToLowerInvariant() : t[j].Text; j++;
        while (j + 1 < t.Count && t[j].Kind == 'p' && t[j].Text == "." && (t[j + 1].Kind == 'w' || t[j + 1].Kind == 'q')) {
            name += "." + (t[j + 1].Kind == 'w' ? t[j + 1].Text.ToLowerInvariant() : t[j + 1].Text); j += 2;
        }
        return name;
    }

    /// <summary>The statement with values replaced: strings and numbers become ?, parameter numbers stay,
    /// lists of placeholders collapse to (?..), white space is single, keywords keep their spelling.</summary>
    public static string Shape(string sql) {
        var t = Lex(sql ?? ""); var sb = new StringBuilder();
        for (int i = 0; i < t.Count; i++) {
            string s = t[i].Kind == 's' || t[i].Kind == 'n' ? "?" : t[i].Kind == 'q' ? "\"" + t[i].Text + "\"" : t[i].Text;
            // "( ? , ? , ? )" -> "(?..)"
            if (t[i].Kind == 'p' && t[i].Text == "(") {
                int j = i + 1, vals = 0; bool ok = true;
                while (j < t.Count && !(t[j].Kind == 'p' && t[j].Text == ")")) {
                    if (t[j].Kind == 's' || t[j].Kind == 'n' || t[j].Kind == '$') vals++;
                    else if (!(t[j].Kind == 'p' && t[j].Text == ",")) { ok = false; break; }
                    j++;
                }
                bool afterIn = i > 0 && t[i - 1].Kind == 'w' && t[i - 1].Text.ToLowerInvariant() == "in";
                if (ok && j < t.Count && vals >= 2 && afterIn) { Append(sb, "(?..)", 'p'); i = j; continue; }
            }
            Append(sb, s, t[i].Kind);
        }
        return sb.ToString();
    }
    static void Append(StringBuilder sb, string s, char kind) {
        if (sb.Length > 0) {
            char last = sb[sb.Length - 1];
            bool tight = s == "," || s == ")" || s == "." || s == ";" || last == '(' || last == '.' || (s == "(" && char.IsLetterOrDigit(last) && !EndsWithKeyword(sb));
            if (!tight) sb.Append(' ');
        }
        sb.Append(s);
    }
    static bool EndsWithKeyword(StringBuilder sb) {
        string s = sb.ToString(); int i = s.Length; while (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) i--;
        string w = s.Substring(i).ToLowerInvariant();
        return w == "in" || w == "values" || w == "from" || w == "where" || w == "and" || w == "or" || w == "on" || w == "select" || w == "exists" || w == "not" || w == "as" || w == "into" || w == "join" || w == "using" || w == "set" || w == "by" || w == "when" || w == "then" || w == "else" || w == "over";
    }
}
}
