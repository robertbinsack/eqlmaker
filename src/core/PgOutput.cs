// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Reads the messages of the output plugin "pgoutput" (logical replication protocol version 1), which every
// PostgreSQL server contains. It is the fallback for servers that lack the optional module test_decoding.
// A message is one value of the column "data" of pg_logical_slot_get_binary_changes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EqlMaker {

public static class PgOutput {
    class Rel { public string Table; public List<string> Columns = new List<string>(); public List<bool> Key = new List<bool>(); }

    // microseconds between 1970-01-01 and 2000-01-01 (the time base of the protocol)
    const long Epoch2000Us = 946684800L * 1000000L;

    /// <summary>"\x0a1b..." as returned for a bytea value in text format.</summary>
    public static byte[] FromHex(string s) {
        if (s == null) return new byte[0];
        int start = s.StartsWith("\\x") ? 2 : 0;
        var b = new byte[(s.Length - start) / 2];
        for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(s.Substring(start + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return b;
    }

    class Reader {
        public byte[] B; public int P;
        public byte U8() { return B[P++]; }
        public int I16() { int v = (B[P] << 8) | B[P + 1]; P += 2; return v; }
        public int I32() { int v = (B[P] << 24) | (B[P + 1] << 16) | (B[P + 2] << 8) | B[P + 3]; P += 4; return v; }
        public long I64() { long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | B[P + i]; P += 8; return v; }
        public string Str() { int s = P; while (B[P] != 0) P++; string r = Encoding.UTF8.GetString(B, s, P - s); P++; return r; }
        public string Bytes(int n) { string r = Encoding.UTF8.GetString(B, P, n); P += n; return r; }
    }

    /// <summary>The values of a tuple by column name; SQL NULL is null. Columns that are unchanged TOAST values
    /// (the server does not send them) are left out.</summary>
    static JO Tuple(Reader r, Rel rel, bool keyOnly) {
        var o = new JO(); int n = r.I16();
        for (int i = 0; i < n; i++) {
            char kind = (char)r.U8(); string name = i < rel.Columns.Count ? rel.Columns[i] : "column" + (i + 1);
            if (keyOnly && i < rel.Key.Count && !rel.Key[i]) { if (kind == 't') r.Bytes(r.I32()); continue; }
            if (kind == 'n') o[name] = null;
            else if (kind == 'u') continue;
            else { int len = r.I32(); o[name] = r.Bytes(len); }
        }
        return o;
    }

    /// <summary>One event of kind "transaction" per committed transaction that changed rows.</summary>
    public static List<DbEvent> ToEvents(IEnumerable<byte[]> messages, string source, string database) {
        var list = new List<DbEvent>(); var rels = new Dictionary<int, Rel>(); DbEvent cur = null;
        foreach (var msg in messages) {
            if (msg == null || msg.Length == 0) continue;
            var r = new Reader { B = msg, P = 1 };
            switch ((char)msg[0]) {
                case 'B': r.I64(); long ct = r.I64(); int xid = r.I32();
                    cur = new DbEvent { Kind = "transaction", Source = source, Via = "replication", Database = database, Tx = xid.ToString(CultureInfo.InvariantCulture), Rows = new List<JO>(), Tables = new List<string>(), Ts = ct + Epoch2000Us };
                    break;
                case 'C':
                    if (cur != null) { r.U8(); r.I64(); r.I64(); cur.Ts = r.I64() + Epoch2000Us; if (cur.Rows.Count > 0) list.Add(cur); }
                    cur = null; break;
                case 'R': {
                    int id = r.I32(); string ns = r.Str(), name = r.Str(); r.U8(); int n = r.I16();
                    var rel = new Rel { Table = ns + "." + name };
                    for (int i = 0; i < n; i++) { rel.Key.Add((r.U8() & 1) != 0); rel.Columns.Add(r.Str()); r.I32(); r.I32(); }
                    rels[id] = rel; break;
                }
                case 'I': case 'U': case 'D': {
                    if (cur == null) break;
                    char t = (char)msg[0]; Rel rel; int id = r.I32();
                    if (!rels.TryGetValue(id, out rel)) break;
                    JO oldRow = null, newRow = null;
                    char k = (char)r.U8();
                    if (k == 'K' || k == 'O') { oldRow = Tuple(r, rel, k == 'K'); if (t == 'U') k = (char)r.U8(); }
                    if (k == 'N') newRow = Tuple(r, rel, false);
                    cur.Rows.Add(new JO("table", rel.Table, "op", t == 'I' ? "insert" : t == 'U' ? "update" : "delete", "new", newRow, "old", oldRow));
                    if (!cur.Tables.Contains(rel.Table)) cur.Tables.Add(rel.Table);
                    break;
                }
                case 'T': {
                    if (cur == null) break;
                    int n = r.I32(); r.U8();
                    for (int i = 0; i < n; i++) {
                        Rel rel; if (!rels.TryGetValue(r.I32(), out rel)) continue;
                        cur.Rows.Add(new JO("table", rel.Table, "op", "truncate", "new", null, "old", null));
                        if (!cur.Tables.Contains(rel.Table)) cur.Tables.Add(rel.Table);
                    }
                    break;
                }
            }
        }
        return list;
    }
}
}
