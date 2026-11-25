// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Changed rows through the output plugin pgoutput, which every PostgreSQL server contains (fallback for
// servers without the module test_decoding).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EqlMaker.Tests {

public static class PgOutputTests {
    static void I32(MemoryStream m, int v) { m.WriteByte((byte)(v >> 24)); m.WriteByte((byte)(v >> 16)); m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
    static void I16(MemoryStream m, int v) { m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
    static void I64(MemoryStream m, long v) { for (int i = 7; i >= 0; i--) m.WriteByte((byte)(v >> (8 * i))); }
    static void Z(MemoryStream m, string s) { var b = Encoding.UTF8.GetBytes(s); m.Write(b, 0, b.Length); m.WriteByte(0); }
    static byte[] Msg(char type, Action<MemoryStream> body) { var m = new MemoryStream(); m.WriteByte((byte)type); body(m); return m.ToArray(); }
    static void Text(MemoryStream m, string v) { if (v == null) m.WriteByte((byte)'n'); else { m.WriteByte((byte)'t'); var b = Encoding.UTF8.GetBytes(v); I32(m, b.Length); m.Write(b, 0, b.Length); } }

    /// <summary>Messages as the server writes them: begin, relation, insert, update with old key, delete with key, commit.</summary>
    public static void MessagesAreReadAsTransactionsWithRows() {
        long us2000 = (1795630000L - 946684800L) * 1000000L;
        var msgs = new List<byte[]> {
            Msg('B', delegate (MemoryStream m) { I64(m, 100); I64(m, us2000); I32(m, 741); }),
            Msg('R', delegate (MemoryStream m) { I32(m, 16385); Z(m, "public"); Z(m, "item"); m.WriteByte((byte)'d'); I16(m, 3); m.WriteByte(1); Z(m, "id"); I32(m, 23); I32(m, -1); m.WriteByte(0); Z(m, "name"); I32(m, 25); I32(m, -1); m.WriteByte(0); Z(m, "qty"); I32(m, 23); I32(m, -1); }),
            Msg('I', delegate (MemoryStream m) { I32(m, 16385); m.WriteByte((byte)'N'); I16(m, 3); Text(m, "1"); Text(m, "bolt, 'M8'"); Text(m, null); }),
            Msg('U', delegate (MemoryStream m) { I32(m, 16385); m.WriteByte((byte)'K'); I16(m, 3); Text(m, "1"); Text(m, null); Text(m, null); m.WriteByte((byte)'N'); I16(m, 3); Text(m, "1"); Text(m, "bolt"); Text(m, "7"); }),
            Msg('D', delegate (MemoryStream m) { I32(m, 16385); m.WriteByte((byte)'K'); I16(m, 3); Text(m, "1"); Text(m, null); Text(m, null); }),
            Msg('C', delegate (MemoryStream m) { m.WriteByte(0); I64(m, 100); I64(m, 200); I64(m, us2000 + 1500); }),
            // a transaction without rows is not an event
            Msg('B', delegate (MemoryStream m) { I64(m, 300); I64(m, us2000 + 5); I32(m, 742); }),
            Msg('C', delegate (MemoryStream m) { m.WriteByte(0); I64(m, 300); I64(m, 400); I64(m, us2000 + 6); }) };
        var ev = PgOutput.ToEvents(msgs, "postgres", "appdb");
        T.Eq(1, ev.Count, "one transaction with rows"); var t = ev[0];
        T.Eq("741", t.Tx, "transaction id"); T.Eq("appdb", t.Database, "database"); T.Eq("replication", t.Via, "via"); T.Eq("public.item", string.Join(",", t.Tables.ToArray()), "tables");
        T.Eq(1795630000L * 1000000L + 1500, t.Ts, "commit time in microseconds since 1970");
        T.Eq(3, t.Rows.Count, "rows");
        T.Eq("insert", t.Rows[0].Str("op"), "insert"); T.Eq("bolt, 'M8'", t.Rows[0].Obj("new").Str("name"), "text value"); T.Eq(null, t.Rows[0].Obj("new")["qty"], "NULL");
        T.Eq("update", t.Rows[1].Str("op"), "update"); T.Eq("7", t.Rows[1].Obj("new").Str("qty"), "new value"); T.Eq("id", string.Join(",", new List<string>(t.Rows[1].Obj("old").Keys).ToArray()), "old row has the key columns only");
        T.Eq("delete", t.Rows[2].Str("op"), "delete"); T.Eq("1", t.Rows[2].Obj("old").Str("id"), "key of the deleted row"); T.Eq(null, t.Rows[2]["new"], "no new row");
        T.Eq("ä", Encoding.UTF8.GetString(PgOutput.FromHex("\\xc3a4")), "hex of a bytea value");
    }

    /// <summary>The same capture as with test_decoding, forced through pgoutput with a publication that is dropped afterwards.</summary>
    public static void CapturesChangedRowsThroughPgoutputAndCleansUp() {
        using (var db = new TestDb()) {
            using (var c = new PgClient(db.App)) c.Exec("CREATE TABLE item (id serial PRIMARY KEY, name text NOT NULL, qty integer)");
            var src = new PostgresSource();
            var opt = new CaptureOptions { Connection = T.Pg, Databases = new List<string> { db.Database }, Changes = true, Decoding = "pgoutput", StateFile = Path.Combine(T.TempDir("pgoutput"), "capture-active.json") };
            src.Start(opt);
            using (var c = new PgClient(db.App)) {
                c.Query("INSERT INTO item (name, qty) VALUES ($1, $2)", "bolt", "10");
                c.Exec("BEGIN"); c.Exec("UPDATE item SET qty = 7 WHERE name = 'bolt'"); c.Exec("DELETE FROM item WHERE name = 'bolt'"); c.Exec("COMMIT");
            }
            var ev = src.Stop();
            var tx = ev.FindAll(delegate (DbEvent e) { return e.Kind == "transaction"; });
            T.Eq(2, tx.Count, "two transactions"); T.Eq("insert", tx[0].Rows[0].Str("op"), "insert"); T.Eq("bolt", tx[0].Rows[0].Obj("new").Str("name"), "value");
            T.Eq("update,delete", tx[1].Rows[0].Str("op") + "," + tx[1].Rows[1].Str("op"), "block"); T.Eq("7", tx[1].Rows[0].Obj("new").Str("qty"), "new value");
            T.Eq("pgoutput", src.Info.Str("decoding"), "reported");
            using (var db2 = new PgClient(db.AdminFor(db.Database))) {
                T.Eq("0", db2.Scalar("SELECT count(*) FROM pg_publication WHERE pubname LIKE 'eqlmaker\\_%'"), "publication dropped");
                T.Eq("0", db2.Scalar("SELECT count(*) FROM pg_replication_slots WHERE slot_name LIKE 'eqlmaker\\_%'"), "slot dropped");
            }
        }
    }
}
}
