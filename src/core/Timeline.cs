// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// correlate: the time line of a recording, evaluated by fixed rules (no model, no guessing beyond
// the rules stated here and in docs/pipeline.md):
//   chapters -> actions -> statements -> transactions -> tables
// 1. Every database event belongs to the last action that started at or before it (tolerance 2 ms).
// 2. A transaction (changed rows) is tied to statements by, in this order: the same transaction id
//    (exact; needs %x in log_line_prefix), else the session whose last write statement before the
//    commit names one of the changed tables ("time+table"), else the last statement before the
//    commit ("time"). The method is written next to every result.
using System;
using System.Collections.Generic;
using System.IO;

namespace EqlMaker {

public class RecordingData {
    public string Dir; public JO Manifest; public List<JO> Actions; public List<DbEvent> Events;
    public static RecordingData Load(string dir) {
        string m = Path.Combine(dir, "recording.json");
        if (!File.Exists(m)) throw new Exception("not a recording folder (no recording.json): " + dir);
        var r = new RecordingData { Dir = Path.GetFullPath(dir), Manifest = Json.ReadFile(m) };
        string a = Path.Combine(dir, "actions.jsonl"), e = Path.Combine(dir, "events.jsonl");
        r.Actions = File.Exists(a) ? Json.ReadLines(a) : new List<JO>();
        r.Events = File.Exists(e) ? DbEvent.ReadFile(e) : new List<DbEvent>();
        return r;
    }
    public string Name { get { return Manifest.Str("name", Path.GetFileName(Dir)); } }
}

public static class Timeline {
    const double Tolerance = 0.002;

    public static JO Build(RecordingData rec) {
        var ev = rec.Events; var acts = rec.Actions;
        // --- 1. events to actions
        var evOfAction = new List<List<DbEvent>>(); foreach (var a in acts) evOfAction.Add(new List<DbEvent>());
        var before = new List<DbEvent>(); var actionOf = new Dictionary<DbEvent, int>();
        foreach (var e in ev) {
            double t = e.Offset ?? 0; int idx = -1;
            for (int i = 0; i < acts.Count; i++) { if (acts[i].Num("offset", 0) <= t + Tolerance) idx = i; else break; }
            if (idx < 0) before.Add(e); else { evOfAction[idx].Add(e); actionOf[e] = idx; }
        }
        // --- 2. transactions to statements
        var attribution = new List<object>(); var txOfStatement = new Dictionary<DbEvent, List<DbEvent>>(); var methodOf = new Dictionary<DbEvent, string>();
        var sessionOfTx = new Dictionary<DbEvent, string>();
        for (int i = 0; i < ev.Count; i++) {
            var tx = ev[i]; if (tx.Kind != "transaction") continue;
            var stm = new List<DbEvent>(); string method = null, session = null;
            // exact: same transaction id
            if (tx.Tx != null) foreach (var s in ev) if (s.Kind != "transaction" && s.Tx == tx.Tx && !OtherDatabase(s, tx)) stm.Add(s);
            if (stm.Count > 0) { method = "tx-id"; session = stm[0].Session; }
            if (method == null) {
                // the session whose last write statement before the commit names a changed table
                for (int j = i - 1; j >= 0 && session == null; j--) {
                    var s = ev[j];
                    if (s.Kind == "transaction" || s.Sql == null) continue;
                    if (tx.Ts - s.Ts > 120000000L) break;
                    if (OtherDatabase(s, tx)) continue;
                    if (!SqlText.IsWrite(SqlText.Verb(s.Sql))) continue;
                    foreach (var tn in SqlText.Tables(s.Sql)) if (SameTable(tn, tx.Tables, s.Database)) { session = s.Session; method = "time+table"; break; }
                }
                if (session == null) {
                    for (int j = i - 1; j >= 0; j--) if (ev[j].Kind != "transaction" && ev[j].Sql != null && !OtherDatabase(ev[j], tx)) { session = ev[j].Session; method = "time"; break; }
                }
                if (session != null) {
                    // statements of that session back to the start of its transaction block (or the one statement)
                    // (statements are logged before they run and log times are cut to milliseconds, so every
                    // statement of the transaction stands before its commit in the time order)
                    var block = new List<DbEvent>();
                    for (int j = i - 1; j >= 0; j--) {
                        var s = ev[j];
                        if (s.Kind != "statement" && s.Kind != "execute") continue;
                        if (s.Session != session || s.Sql == null) continue;
                        if (tx.Ts - s.Ts > 120000000L) break;
                        string verb = SqlText.Verb(s.Sql);
                        if (block.Count == 0) { block.Add(s); if (verb == "commit") continue; break; }   // COMMIT: an explicit block follows backwards
                        if (verb == "commit" || verb == "rollback" || methodOf.ContainsKey(s)) break;
                        block.Insert(0, s);
                        if (verb == "begin") break;
                    }
                    stm = block;
                }
            }
            sessionOfTx[tx] = session;
            foreach (var s in stm) { methodOf[s] = method; if (!txOfStatement.ContainsKey(s)) txOfStatement[s] = new List<DbEvent>(); txOfStatement[s].Add(tx); }
            attribution.Add(new JO("transaction", tx.Seq, "tx", tx.Tx, "session", session, "method", method, "statements", stm.ConvertAll(delegate (DbEvent s) { return (object)s.Seq; })));
        }
        // --- 3. output
        var chapters = new List<object>(); JO cur = null; List<object> curActs = null;
        Action<string, double> open = delegate (string caption, double start) {
            cur = new JO("index", chapters.Count + 1, "caption", caption, "start", start, "end", null, "actions", null, "tables", null, "counts", null);
            curActs = new List<object>(); cur["actions"] = curActs; chapters.Add(cur);
        };
        double duration = rec.Manifest.Num("duration", 0);
        for (int i = 0; i < acts.Count; i++) {
            var a = acts[i];
            if (a.Str("kind") == "chapter") { if (cur != null) cur["end"] = a.Num("offset", 0); open(a.Str("caption"), a.Num("offset", 0)); }
            else if (cur == null) open(null, 0);
            var o = new JO("seq", a.Int("seq", i + 1), "offset", a.Num("offset", 0), "kind", a.Str("kind"), "text", Recording.ActionText(a), "window", a.Str("window"), "control", a.Str("control"), "class", a.Str("class"), "shot", a.Str("shot"));
            var st = new List<object>(); var txs = new List<object>();
            foreach (var e in evOfAction[i]) {
                if (e.Kind == "transaction") txs.Add(TxJson(e, sessionOfTx.ContainsKey(e) ? sessionOfTx[e] : null));
                else st.Add(StatementJson(e, txOfStatement, methodOf));
            }
            o["statements"] = st; o["transactions"] = txs;
            curActs.Add(o);
        }
        if (cur != null) cur["end"] = duration;
        foreach (JO ch in chapters) Summarise(ch);
        var beforeJson = new List<object>();
        foreach (var e in before) beforeJson.Add(e.Kind == "transaction" ? TxJson(e, null) : StatementJson(e, txOfStatement, methodOf));
        var tl = new JO();
        tl["format"] = "eqlmaker-timeline"; tl["version"] = DbEvent.FormatVersion; tl["recording"] = rec.Name; tl["t0_us"] = rec.Manifest.Long("t0_us", 0); tl["duration"] = duration;
        tl["source"] = rec.Manifest.Str("source");
        tl["rules"] = new JO("event_to_action", "last action that started at or before the event (tolerance 2 ms)", "transaction_to_statements", "tx-id, else time+table, else time; see 'attribution'");
        tl["chapters"] = chapters; tl["before_first_action"] = beforeJson; tl["attribution"] = attribution;
        int nSt = 0, nTx = 0, nErr = 0; foreach (var e in ev) { if (e.Kind == "transaction") nTx++; else if (e.Kind == "error") nErr++; else nSt++; }
        tl["counts"] = new JO("chapters", chapters.Count, "actions", acts.Count, "statements", nSt, "errors", nErr, "transactions", nTx, "before_first_action", before.Count);
        return tl;
    }

    /// <summary>True when both name a database and the names differ. Statements that came through the statement trace of a
    /// server name the database of the application's protocol (for example a T-SQL database), changed rows the physical
    /// one: those names are not comparable.</summary>
    static bool OtherDatabase(DbEvent s, DbEvent tx) {
        return s.Database != null && tx.Database != null && s.Database != tx.Database && s.Via != "trace";
    }

    static void SplitName(string n, out string schema, out string table) {
        int dot = n.LastIndexOf('.');
        schema = dot >= 0 ? n.Substring(0, dot).Trim('"') : null; table = (dot >= 0 ? n.Substring(dot + 1) : n).Trim('"');
    }

    /// <summary>The table of a statement is the changed table: the same name, an unqualified name, or - when the server keeps
    /// several databases in one (multi-database mode) - the schema prefixed with the name of the database ("shop_dbo" for
    /// database "shop", schema "dbo").</summary>
    static bool SameTable(string name, List<string> changed, string database) {
        if (changed == null) return false;
        string ns, nt; SplitName(name, out ns, out nt);
        foreach (var c in changed) {
            string cs, ct; SplitName(c, out cs, out ct);
            if (!string.Equals(ct, nt, StringComparison.OrdinalIgnoreCase)) continue;
            if (ns == null || cs == null || string.Equals(cs, ns, StringComparison.OrdinalIgnoreCase)) return true;
            if (database != null && string.Equals(cs, database + "_" + ns, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static JO StatementJson(DbEvent e, Dictionary<DbEvent, List<DbEvent>> txOf, Dictionary<DbEvent, string> methodOf) {
        var o = new JO("seq", e.Seq, "offset", e.Offset, "kind", e.Kind, "session", e.Session, "application", e.Application);
        if (e.Kind == "error") { o["error"] = e.Error; o["sql"] = e.Sql; return o; }
        string verb = SqlText.Verb(e.Sql);
        o["verb"] = verb; o["sql"] = e.Sql; o["shape"] = SqlText.Shape(e.Sql);
        o["params"] = e.Params == null ? null : new List<object>(e.Params.ToArray());
        o["tables_referenced"] = SqlText.Tables(e.Sql);
        if (txOf.ContainsKey(e)) { o["transactions"] = txOf[e].ConvertAll(delegate (DbEvent t) { return (object)t.Seq; }); o["attribution"] = methodOf[e]; }
        return o;
    }
    static JO TxJson(DbEvent e, string session) {
        return new JO("seq", e.Seq, "offset", e.Offset, "tx", e.Tx, "session", session, "tables", e.Tables, "rows", e.Rows == null ? null : new List<object>(e.Rows.ToArray()));
    }

    static void Summarise(JO ch) {
        var read = new List<string>(); var written = new List<string>(); var refWrite = new List<string>(); int st = 0, tx = 0, err = 0, rows = 0;
        foreach (JO a in ch.Arr("actions")) {
            foreach (JO s in a.Arr("statements")) {
                if (s.Str("kind") == "error") { err++; continue; }
                st++;
                string verb = s.Str("verb", "");
                foreach (var t in s.Strings("tables_referenced")) {
                    if (verb == "select") { if (!read.Contains(t)) read.Add(t); }
                    else if (SqlText.IsWrite(verb)) { if (!refWrite.Contains(t)) refWrite.Add(t); }
                }
            }
            foreach (JO t in a.Arr("transactions")) { tx++; foreach (var n in t.Strings("tables")) if (!written.Contains(n)) written.Add(n); if (t.Arr("rows") != null) rows += t.Arr("rows").Count; }
        }
        ch["tables"] = new JO("read", read, "written", written, "written_according_to_sql_text", refWrite);
        ch["counts"] = new JO("actions", ch.Arr("actions").Count, "statements", st, "errors", err, "transactions", tx, "rows_changed", rows);
    }

    /// <summary>Writes timeline.json into the recording folder and returns a short result.</summary>
    public static JO Run(string recordingDir) {
        var rec = RecordingData.Load(recordingDir);
        var tl = Build(rec);
        string f = Path.Combine(rec.Dir, "timeline.json");
        Json.WriteFile(f, tl);
        var r = new JO("timeline", f);
        foreach (var kv in tl.Obj("counts")) r[kv.Key] = kv.Value;
        return r;
    }
}
}
