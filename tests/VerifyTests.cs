// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace EqlMaker.Tests {

public static class VerifyScreensTests {
    static string Picture(string file, Color back, bool extra) {
        using (var bmp = new Bitmap(200, 120, PixelFormat.Format24bppRgb)) {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(back); g.FillRectangle(Brushes.Black, 20, 20, 60, 30); if (extra) g.FillRectangle(Brushes.Red, 150, 80, 10, 10); }
            bmp.Save(file, ImageFormat.Png);
        }
        return file;
    }

    public static void EqualDifferentToleranceIgnoreAndSize() {
        string d = T.TempDir("screens");
        string a = Picture(Path.Combine(d, "a.png"), Color.FromArgb(240, 240, 240), false), b = Picture(Path.Combine(d, "b.png"), Color.FromArgb(240, 240, 240), true);
        string c = Picture(Path.Combine(d, "c.png"), Color.FromArgb(236, 238, 243), false);
        T.True(VerifyScreens.Compare(a, a, 0, null, null).Bool("equal", false), "a picture equals itself");
        var r = VerifyScreens.Compare(a, b, 0, null, Path.Combine(d, "diff.png"));
        T.Eq(100L, r.Long("different_pixels", 0), "the 10 x 10 square"); T.Eq("150,80,10,10", string.Join(",", r.Strings("bounds_of_differences").ToArray()), "where");
        T.True(File.Exists(Path.Combine(d, "diff.png")), "picture of the differences written");
        T.True(VerifyScreens.Compare(a, b, 0, VerifyScreens.ParseIgnore("140,70,30,30"), null).Bool("equal", false), "an ignored region is not compared");
        T.True(!VerifyScreens.Compare(a, c, 0, null, null).Bool("equal", true), "slightly different colours differ at tolerance 0");
        T.True(VerifyScreens.Compare(a, c, 8, null, null).Bool("equal", false), "and are equal at tolerance 8");
        using (var bmp = new Bitmap(100, 100)) bmp.Save(Path.Combine(d, "small.png"), ImageFormat.Png);
        T.True(!VerifyScreens.Compare(a, Path.Combine(d, "small.png"), 0, null, null).Bool("same_size", true), "different sizes are reported");
    }

    public static void FoldersArePairedByName() {
        string d = T.TempDir("screens2"); var cfg = Config.Load(d);
        Directory.CreateDirectory(Path.Combine(d, "A")); Directory.CreateDirectory(Path.Combine(d, "B"));
        Picture(Path.Combine(d, "A", "m1.png"), Color.White, false); Picture(Path.Combine(d, "B", "m1.png"), Color.White, false);
        Picture(Path.Combine(d, "A", "m2.png"), Color.White, false); Picture(Path.Combine(d, "B", "m2.png"), Color.White, true);
        Picture(Path.Combine(d, "A", "m3.png"), Color.White, false);
        var r = VerifyScreens.Run(cfg, Path.Combine(d, "A"), Path.Combine(d, "B"), 0, 0.1, null, "t");
        T.Eq(2, r.Int("pairs", 0), "pairs"); T.Eq(1, r.Int("failed", 0), "failed"); T.Eq(1, r.Int("unpaired", 0), "unpaired"); T.True(!r.Bool("passed", true), "not passed");
        var rep = Json.ReadFile(r.Str("report"));
        T.Eq("only in A: m3.png", rep.Strings("unpaired")[0], "unpaired file named");
        var ok = VerifyScreens.Run(cfg, Path.Combine(d, "A", "m2.png"), Path.Combine(d, "B", "m2.png"), 0, 1.0, null, "t2");
        T.True(ok.Bool("passed", false), "0.42 % differing pixels pass a limit of 1 %");
    }
}

public static class VerifyDbTests {
    /// <summary>A recording with one chapter "Invoice" whose transaction writes an invoice, two lines, stock.</summary>
    static RecordingData Rec(string dir, string name, int invoiceId, string tax, bool stockMovement, string createdAt) {
        string d = Path.Combine(dir, name); Directory.CreateDirectory(d);
        long t0 = 1795630000000000L;
        Json.WriteLines(Path.Combine(d, "actions.jsonl"), new List<JO> {
            new JO("v", "0.1", "seq", 1, "ts", t0, "offset", 0.0, "kind", "chapter", "caption", "Invoice"),
            new JO("v", "0.1", "seq", 2, "ts", t0 + 1000000, "offset", 1.0, "kind", "click", "control", "Save invoice", "window", "New invoice") });
        var rows = new List<JO> {
            new JO("table", "public.counter", "op", "update", "new", new JO("name", "invoice", "value", "4"), "old", null),
            new JO("table", "public.invoice", "op", "insert", "new", new JO("id", invoiceId.ToString(), "number", "INV-2026-00004", "customer_id", "2", "net", "100.00", "tax", tax, "created_at", createdAt), "old", null),
            new JO("table", "public.invoice_line", "op", "insert", "new", new JO("id", (invoiceId * 10).ToString(), "invoice_id", invoiceId.ToString(), "article_id", "5", "quantity", "2"), "old", null),
            new JO("table", "public.invoice_line", "op", "insert", "new", new JO("id", (invoiceId * 10 + 1).ToString(), "invoice_id", invoiceId.ToString(), "article_id", "6", "quantity", "1"), "old", null),
            new JO("table", "public.article", "op", "update", "new", new JO("id", "5", "stock", "8"), "old", null) };
        if (stockMovement) rows.Add(new JO("table", "public.stock_movement", "op", "insert", "new", new JO("id", (invoiceId * 100).ToString(), "article_id", "5", "quantity", "-2", "invoice_id", invoiceId.ToString()), "old", null));
        var tx = new DbEvent { Ts = t0 + 1200000, Offset = 1.2, Kind = "transaction", Source = "postgres", Via = "replication", Database = "shop", Tx = "9", Tables = new List<string> { "public.invoice" }, Rows = rows };
        var st = new DbEvent { Ts = t0 + 1100000, Offset = 1.1, Kind = "statement", Source = "postgres", Via = "log", Session = "1", Database = "shop", Sql = "COMMIT" };
        DbEvent.WriteFile(Path.Combine(d, "events.jsonl"), new List<DbEvent> { st, tx });
        Json.WriteFile(Path.Combine(d, "recording.json"), new JO("format", "eqlmaker-recording", "version", "0.1", "name", name, "t0_us", t0, "duration", 3.0, "source", "postgres"));
        return RecordingData.Load(d);
    }
    static List<string> L(params string[] s) { return new List<string>(s); }

    public static void SameChangesWithOtherGeneratedKeysAreEqual() {
        string d = T.TempDir("vdb1");
        var a = Rec(d, "original", 4, "19.00", true, "2026-11-24 18:20:31.123456"); var b = Rec(d, "web", 7, "19.00", true, "2026-11-24 18:25:02.5");
        var r = VerifyDb.Compare(a, b, L(), L("*.id"), L());
        T.True(r.Bool("equal", false), "equal although the new invoice got id 4 in one run and 7 in the other: " + VerifyDb.Text(r));
        T.Eq(6, r.Int("equal_rows", 0), "rows that agree");
        T.True(r.Arr("notes").Exists(delegate (object n) { return ((string)n).Contains("created_at"); }), "the time stamp rule is named in the notes");
    }

    public static void DeliberateDifferencesAreFound() {
        string d = T.TempDir("vdb2");
        var a = Rec(d, "original", 4, "19.00", true, "2026-11-24 18:20:31"); var b = Rec(d, "web", 5, "19.01", false, "2026-11-24 18:25:02");
        var r = VerifyDb.Compare(a, b, L(), L("*.id"), L());
        T.True(!r.Bool("equal", true), "different");
        T.Eq(2, r.Int("only_in_a", 0), "only in A: the invoice with the right tax and the stock movement"); T.Eq(1, r.Int("only_in_b", 0), "only in B: the invoice with the other tax");
        string text = VerifyDb.Text(r);
        T.Contains(text, "tax=19.00", "the value of A"); T.Contains(text, "tax=19.01", "the value of B"); T.Contains(text, "only A: insert public.stock_movement", "the missing row");
        // with the tax column ignored, only the missing row remains
        var r2 = VerifyDb.Compare(a, b, L("invoice.tax"), L("*.id"), L());
        T.Eq(1, r2.Int("only_in_a", 0), "ignoring a column"); T.Eq(0, r2.Int("only_in_b", 0), "nothing only in B");
    }

    public static void AWrongReferenceIsNotHiddenByTheKeyRule() {
        string d = T.TempDir("vdb3");
        var a = Rec(d, "original", 4, "19.00", true, "x"); var b = Rec(d, "web", 4, "19.00", true, "x");
        // B writes the line for another article: existing keys are compared literally
        var ev = DbEvent.ReadFile(Path.Combine(b.Dir, "events.jsonl")); ev[1].Rows[2].Obj("new")["article_id"] = "9"; DbEvent.WriteFile(Path.Combine(b.Dir, "events.jsonl"), ev);
        var r = VerifyDb.Compare(a, RecordingData.Load(b.Dir), L(), L("*.id"), L());
        T.True(!r.Bool("equal", true) && r.Int("only_in_a", 0) == 1 && r.Int("only_in_b", 0) == 1, "difference in a reference to an existing row is reported");
    }

    public static void MissingStepAndEmptyRecordingsAreNotEqual() {
        string d = T.TempDir("vdb4");
        var a = Rec(d, "original", 4, "19.00", true, "x"); var b = Rec(d, "web", 4, "19.00", true, "x");
        File.WriteAllText(Path.Combine(b.Dir, "events.jsonl"), "");
        var r = VerifyDb.Compare(a, RecordingData.Load(b.Dir), L(), L("*.id"), L());
        T.True(!r.Bool("equal", true) && r.Int("only_in_a", 0) == 6, "B changed nothing");
        File.WriteAllText(Path.Combine(a.Dir, "events.jsonl"), "");
        var r2 = VerifyDb.Compare(RecordingData.Load(a.Dir), RecordingData.Load(b.Dir), L(), L("*.id"), L());
        T.True(!r2.Bool("equal", true), "two recordings without changed rows are not reported as equal");
        T.True(r2.Arr("notes").Count > 0, "and the reason is given");
    }
}
}
