// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EqlMaker.Tests {

public static class ManualTests {
    static List<string> Titles(JO m) { var l = new List<string>(); foreach (JO c in m.Arr("chapters")) l.Add(c.Int("level", 0) + ":" + c.Str("title")); return l; }
    static JO Chapter(JO m, string title) { foreach (JO c in m.Arr("chapters")) if (c.Str("title") == title) return c; return null; }

    public static void PlainText() {
        string f = Path.Combine(T.TempDir("man-txt"), "guide.txt");
        File.WriteAllText(f, "ORDER ENTRY\n\nThis program enters orders.\n\n1 Customers\n\nA customer has a number.\n\n1.1 Credit limit\n\nThe limit is checked.\n2 this is no heading because the line before is not empty.\n\nInvoices\n--------\n\nInvoices get a number.\n", new UTF8Encoding(false));
        var m = ManualImport.Import(f);
        T.Eq("1:ORDER ENTRY|1:1 Customers|2:1.1 Credit limit|2:Invoices", string.Join("|", Titles(m).ToArray()), "headings: capitals, numbered, underlined");
        T.Contains(Chapter(m, "1.1 Credit limit").Str("text"), "2 this is no heading", "a numbered line inside a paragraph stays text");
        T.Eq("ORDER ENTRY", m.Str("title"), "title"); T.Eq("text", m.Str("source_type"), "type");
    }

    public static void MarkdownAndTheSampleHandbook() {
        var m = ManualImport.Import(Path.Combine(T.RepoRoot, "examples", "sample-app", "manual", "handbook.md"));
        T.Eq("1:Sample Shop handbook|2:Customers|2:Articles|2:Writing an invoice|2:Payments", string.Join("|", Titles(m).ToArray()), "chapters");
        T.Contains(Chapter(m, "Writing an invoice").Str("text"), "credit limit allows", "text of a chapter");
        string f = Path.Combine(T.TempDir("man-md"), "x.md");
        File.WriteAllText(f, "Title\n=====\n\nSee [the **list**](http://example.invalid/x) and `code`.\n\n```\n# not a heading\n```\n\n### Deep ###\ntext\n");
        var x = ManualImport.Import(f);
        T.Eq("1:Title|3:Deep", string.Join("|", Titles(x).ToArray()), "setext and closed headings; fenced code is not a heading");
        T.Contains(Chapter(x, "Title").Str("text"), "See the list and code.", "links and emphasis reduced to text");
    }

    public static void Html() {
        string f = Path.Combine(T.TempDir("man-html"), "help.html");
        File.WriteAllText(f, "<html><head><title>x</title><style>h1{color:red}</style></head><body><h1>Help &amp; hints</h1><p>First <b>para</b>&nbsp;graph.</p><script>var h2 = '<h2>no</h2>';</script><h2 class=\"a\">Customers <small>(list)</small></h2><table><tr><td>Name</td><td>Müller</td></tr></table><ul><li>one</li><li>two</li></ul></body></html>", new UTF8Encoding(false));
        var m = ManualImport.Import(f);
        T.Eq("1:Help & hints|2:Customers (list)", string.Join("|", Titles(m).ToArray()), "headings; scripts and styles ignored; entities decoded");
        T.Contains(Chapter(m, "Help & hints").Str("text"), "First para graph.", "text");
        string t = Chapter(m, "Customers (list)").Str("text");
        T.Contains(t, "Name\tMüller", "table cells"); T.Contains(t, "one\ntwo", "list items on lines");
    }

    public static void Docx() {
        string f = Path.Combine(T.TempDir("man-docx"), "manual.docx");
        string doc = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Order </w:t></w:r><w:r><w:t>entry</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Enter the customer &amp; the </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>article</w:t></w:r><w:r><w:br/><w:t>Second line</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"berschrift2\"/></w:pPr><w:r><w:t>Rabatt</w:t></w:r></w:p><w:p><w:r><w:t>Gruppe A: 5 %</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr><w:r><w:t>By outline level</w:t></w:r></w:p>" +
            "</w:body></w:document>";
        using (var zip = ZipFile.Open(f, ZipArchiveMode.Create)) { var e = zip.CreateEntry("word/document.xml"); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(doc); }
        var m = ManualImport.Import(f);
        T.Eq("1:Order entry|2:Rabatt|3:By outline level", string.Join("|", Titles(m).ToArray()), "heading styles (English, German, outline level)");
        T.Eq("Enter the customer & the article\nSecond line", Chapter(m, "Order entry").Str("text"), "runs joined, line break kept");
        string bad = Path.Combine(Path.GetDirectoryName(f), "bad.docx");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) zip.CreateEntry("other.txt");
        T.Throws(delegate { ManualImport.Import(bad); }, "not a DOCX", "wrong archive");
    }

    // ---------------------------------------------------------------- PDF
    static byte[] Zlib(byte[] data) {
        using (var ms = new MemoryStream()) {
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(data, 0, data.Length);
            uint a = 1, b = 0; foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a; ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
            return ms.ToArray();
        }
    }
    /// <summary>Writes a PDF from object bodies (1-based); a body is text or {dictionary text, stream bytes}.</summary>
    static byte[] Pdf(params object[] objects) {
        var ms = new MemoryStream(); var l1 = Encoding.GetEncoding(28591); var offsets = new List<long>();
        Action<string> w = delegate (string s) { var b = l1.GetBytes(s); ms.Write(b, 0, b.Length); };
        w("%PDF-1.5\n%âãÏÓ\n");
        for (int i = 0; i < objects.Length; i++) {
            offsets.Add(ms.Position); w((i + 1) + " 0 obj\n");
            var pair = objects[i] as object[];
            if (pair == null) w((string)objects[i] + "\n");
            else { var data = (byte[])pair[1]; w(((string)pair[0]).Replace("LEN", data.Length.ToString()) + "\nstream\n"); ms.Write(data, 0, data.Length); w("\nendstream\n"); }
            w("endobj\n");
        }
        long xref = ms.Position; w("xref\n0 " + (objects.Length + 1) + "\n0000000000 65535 f \n");
        foreach (long o in offsets) w(o.ToString("0000000000") + " 00000 n \n");
        w("trailer\n<< /Size " + (objects.Length + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ms.ToArray();
    }

    public static void PdfWithStandardFontAndHeadingsBySize() {
        string content = "BT /F1 20 Tf 72 720 Td (Order entry) Tj ET\n" +
            "BT /F1 11 Tf 72 690 Td (Enter the customer \\(number\\) and the) Tj 0 -14 Td [(arti) -20 (cle.) -300 (Then) -250 (save.)] TJ ET\n" +
            "BT /F1 15 Tf 1 0 0 1 72 640 Tm (Discounts) Tj ET\n" +
            "BT /F1 11 Tf 72 615 Td 14 TL (Group A: 5 % from 500.) Tj T* (Gr\\374n is M\\374ller's colour.) Tj ET\n";
        var pdf = Pdf("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>", new object[] { "<< /Length LEN >>", Encoding.GetEncoding(28591).GetBytes(content) },
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        string f = Path.Combine(T.TempDir("man-pdf1"), "simple.pdf"); File.WriteAllBytes(f, pdf);
        var m = ManualImport.Import(f);
        T.Eq("1:Order entry|2:Discounts", string.Join("|", Titles(m).ToArray()), "headings by font size");
        T.Eq("Enter the customer (number) and the\narticle. Then save.", Chapter(m, "Order entry").Str("text"), "escapes, kerning inside words, gaps between words, line break");
        T.Eq("Group A: 5 % from 500.\nGrün is Müller's colour.", Chapter(m, "Discounts").Str("text"), "octal escapes in the standard encoding; T* starts a line");
        T.Eq(1, Chapter(m, "Discounts").Int("page", 0), "page number");
    }

    public static void PdfCompressedWithCharacterMapAndObjectStream() {
        // a font whose two-byte codes mean something only through its ToUnicode map
        string cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n" +
            "3 beginbfchar\n<0001> <0050>\n<0002> <0072>\n<0003> <00FC>\nendbfchar\n2 beginbfrange\n<0010> <0012> <0061>\n<0020> <0021> [<0066> <20AC>]\nendbfrange\nendcmap end end\n";
        // codes: 0001 P, 0002 r, 0003 ue, 0010..12 a b c, 0020 f, 0021 euro
        string content = "BT /F7 12 Tf 50 700 Td <000100020003002000100011> Tj 60 0 Td <00210012> Tj ET\n";
        // the page and the font dictionary live inside an object stream (objects 3 and 6)
        string o3 = "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F7 6 0 R >> >> /Contents [4 0 R] >>", o6 = "<< /Type /Font /Subtype /Type0 /Encoding /Identity-H /ToUnicode 5 0 R >>";
        string header = "3 0 6 " + (o3.Length + 1) + " "; string packed = header + o3 + "\n" + o6;
        var pdf = Pdf("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "null", new object[] { "<< /Length LEN /Filter /FlateDecode >>", Zlib(Encoding.ASCII.GetBytes(content)) },
            new object[] { "<< /Length LEN /Filter /FlateDecode >>", Zlib(Encoding.ASCII.GetBytes(cmap)) }, "null",
            new object[] { "<< /Type /ObjStm /N 2 /First " + header.Length + " /Length LEN /Filter /FlateDecode >>", Zlib(Encoding.ASCII.GetBytes(packed)) });
        // objects 3 and 6 must come from the object stream: remove the placeholders
        string s = Encoding.GetEncoding(28591).GetString(pdf).Replace("3 0 obj\nnull\nendobj\n", "").Replace("6 0 obj\nnull\nendobj\n", "");
        string f = Path.Combine(T.TempDir("man-pdf2"), "packed.pdf"); File.WriteAllBytes(f, Encoding.GetEncoding(28591).GetBytes(s));
        var m = ManualImport.Import(f);
        T.Eq("Prüfab €c", m.Arr("chapters").Count > 0 ? ((JO)m.Arr("chapters")[0]).Str("text") : "(none)", "text through the character map (single codes, ranges, list)");
        T.Eq(0, m.Arr("warnings").Count, "no warning");
    }

    public static void PdfProblemsAreNamed() {
        string d = T.TempDir("man-pdf3");
        File.WriteAllText(Path.Combine(d, "no.pdf"), "hello");
        T.Throws(delegate { ManualImport.Import(Path.Combine(d, "no.pdf")); }, "not a PDF", "not a PDF");
        var enc = Pdf("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "<< /Filter /Standard >>");
        string es = Encoding.GetEncoding(28591).GetString(enc).Replace("/Root 1 0 R", "/Root 1 0 R /Encrypt 3 0 R");
        File.WriteAllBytes(Path.Combine(d, "enc.pdf"), Encoding.GetEncoding(28591).GetBytes(es));
        T.Throws(delegate { ManualImport.Import(Path.Combine(d, "enc.pdf")); }, "encrypted", "encrypted PDF");
        // pages that hold only a picture: no text, and the result says so
        var scan = Pdf("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", new object[] { "<< /Length LEN >>", Encoding.ASCII.GetBytes("q 612 0 0 792 0 0 cm /Im1 Do Q\n") });
        File.WriteAllBytes(Path.Combine(d, "scan.pdf"), scan);
        var m = ManualImport.Import(Path.Combine(d, "scan.pdf"));
        T.Eq(0, m.Int("characters", -1), "no text"); T.Contains(m.Strings("warnings")[0], "scanned", "warning names the likely reason");
        T.Throws(delegate { ManualImport.Import(Path.Combine(d, "x.rtf")); }, "not found", "missing file");
        File.WriteAllText(Path.Combine(d, "x.rtf"), "{}");
        T.Throws(delegate { ManualImport.Import(Path.Combine(d, "x.rtf")); }, "not supported", "unknown format");
    }

    /// <summary>A PDF as a browser prints it (compressed, embedded fonts with character maps).</summary>
    public static void PdfPrintedByABrowser() {
        string exe; try { exe = new Browser().FindExecutable(); } catch (Exception) { T.Skip("no browser to print a PDF with"); return; }
        string d = T.TempDir("man-pdf4"), html = Path.Combine(d, "handbook.html"), pdf = Path.Combine(d, "handbook.pdf"), o;
        File.WriteAllText(html, "<html><head><meta charset=\"utf-8\"><style>body{font:12pt Georgia} h1{font-size:26pt} h2{font-size:18pt}</style></head><body><h1>Sample handbook</h1><p>This handbook explains the masks.</p><h2>Customers</h2><p>Every customer has a credit limit. Müller pays in €.</p><h2>Invoices</h2><p>Press Save invoice to store it.</p></body></html>", new UTF8Encoding(false));
        int code = T.Run(exe, "--headless=new --disable-gpu --no-first-run --no-pdf-header-footer --user-data-dir=\"" + Path.Combine(d, "profile") + "\" --print-to-pdf=\"" + pdf + "\" \"file:///" + html.Replace('\\', '/') + "\"", null, out o, 90000);
        if (code != 0 || !File.Exists(pdf)) { T.Skip("the browser did not print a PDF here (exit code " + code + ")"); return; }
        var m = ManualImport.Import(pdf);
        string text = string.Join("\n", m.Arr("chapters").ConvertAll(delegate (object c) { return ((JO)c).Str("title") + "\n" + ((JO)c).Str("text"); }).ToArray());
        T.Contains(text, "Sample handbook", "title"); T.Contains(text, "Every customer has a credit limit.", "body text"); T.Contains(text, "Müller pays in €.", "characters outside ASCII");
        T.True(Titles(m).Contains("1:Sample handbook") && Titles(m).Contains("2:Customers") && Titles(m).Contains("2:Invoices"), "headings by size: " + string.Join("|", Titles(m).ToArray()));
        T.Note("browser_pdf_bytes", new FileInfo(pdf).Length);
    }

    public static void RunWritesIntoTheProjectAndSpecLinksIt() {
        string dir = T.TempDir("man-run"); var cfg = Config.Load(dir);
        var r = ManualImport.Run(cfg, Path.Combine(T.RepoRoot, "examples", "sample-app", "manual", "handbook.md"), null);
        T.Eq(5, r.Int("chapters", 0), "chapters"); T.True(File.Exists(cfg.Sub("manual", "handbook.json")) && File.Exists(cfg.Sub("manual", "handbook.txt")), "files written");
        T.True(!File.ReadAllText(cfg.Sub("manual", "handbook.json")).Contains("\"text\": \"# Sample"), "the whole text is in the .txt file, not twice in the JSON");
    }
}
}
