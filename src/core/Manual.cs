// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// manual: takes the manual of the original program over as project context: plain text and
// chapters, which "spec" links to masks and steps. Formats: TXT, Markdown, HTML, DOCX, PDF.
// The PDF reader is built in and small: it reads text PDFs (Flate-compressed content, object
// streams, fonts with a ToUnicode map or a standard encoding). Scanned pages contain pictures, not
// text; for those, convert the PDF with a text recognition tool of your choice first.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker {

public static class ManualImport {
    class Line { public string Text; public int Level; public int Page; public double Size; }   // Level 0 = body text

    public static JO Import(string file) {
        if (!File.Exists(file)) throw new Exception("manual not found: " + file);
        string ext = Path.GetExtension(file).ToLowerInvariant(); List<Line> lines; var warnings = new List<object>(); string type;
        if (ext == ".pdf") { lines = Pdf.Lines(File.ReadAllBytes(file), warnings); type = "pdf"; }
        else if (ext == ".docx") { lines = Docx(file); type = "docx"; }
        else if (ext == ".html" || ext == ".htm" || ext == ".xhtml") { lines = Html(ReadText(file)); type = "html"; }
        else if (ext == ".md" || ext == ".markdown") { lines = Markdown(ReadText(file)); type = "markdown"; }
        else if (ext == ".txt" || ext == ".text" || ext == "") { lines = PlainText(ReadText(file)); type = "text"; }
        else throw new Exception("manual format not supported: " + ext + " (supported: .pdf .docx .html .md .txt)");

        var chapters = new List<object>(); JO cur = null; var body = new StringBuilder(); var all = new StringBuilder(); string title = null; int n = 0;
        Action close = delegate () { if (cur != null) { cur["text"] = body.ToString().Trim(); chapters.Add(cur); } body.Length = 0; };
        foreach (var l in lines) {
            if (l.Level > 0) {
                close();
                if (title == null && l.Level == 1) title = l.Text;
                cur = new JO("id", "c" + (++n), "level", l.Level, "title", l.Text, "page", l.Page > 0 ? (object)l.Page : null, "text", "");
                all.Append("\n" + new string('#', Math.Min(6, l.Level)) + " " + l.Text + "\n\n");
            } else {
                if (cur == null) cur = new JO("id", "c" + (++n), "level", 1, "title", "(start)", "page", l.Page > 0 ? (object)l.Page : null, "text", "");
                body.Append(l.Text + "\n"); all.Append(l.Text + "\n");
            }
        }
        close();
        string text = all.ToString().Trim();
        if (text.Length == 0) warnings.Add("no text was found in the file" + (type == "pdf" ? " (a scanned PDF contains pictures of pages, not text)" : ""));
        return new JO("format", "eqlmaker-manual", "version", DbEvent.FormatVersion, "name", Spec.Slug(Path.GetFileNameWithoutExtension(file)), "title", title ?? Path.GetFileNameWithoutExtension(file),
            "source_file", Path.GetFileName(file), "source_type", type, "characters", text.Length, "chapters", chapters, "warnings", warnings, "text", text);
    }

    /// <summary>Writes manual/&lt;name&gt;.json and manual/&lt;name&gt;.txt into the project.</summary>
    public static JO Run(Config cfg, string file, string name) {
        var m = Import(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(name)) m["name"] = Spec.Slug(name);
        Directory.CreateDirectory(cfg.Sub("manual"));
        string text = m.Str("text"); m.Remove("text");
        Json.WriteFile(cfg.Sub("manual", m.Str("name") + ".json"), m);
        File.WriteAllText(cfg.Sub("manual", m.Str("name") + ".txt"), text + "\n", new UTF8Encoding(false));
        return new JO("manual", cfg.Sub("manual", m.Str("name") + ".json"), "text", cfg.Sub("manual", m.Str("name") + ".txt"), "type", m.Str("source_type"), "chapters", m.Arr("chapters").Count, "characters", m.Int("characters", 0),
            "warnings", m["warnings"], "next", "run 'eqlmaker spec' again: the specification links chapters to masks and steps");
    }

    static string ReadText(string file) {
        var b = File.ReadAllBytes(file);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(b).TrimStart('﻿'); } catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(b); }
    }

    static readonly Regex Numbered = new Regex(@"^(\d+(?:\.\d+)*)\.?\s+(\p{Lu}.{0,90})$");

    static List<Line> PlainText(string text) {
        var src = text.Replace("\r", "").Split('\n'); var l = new List<Line>();
        for (int i = 0; i < src.Length; i++) {
            string s = src[i].TrimEnd(), t = s.Trim();
            if (i + 1 < src.Length && t.Length > 0 && Regex.IsMatch(src[i + 1].Trim(), @"^(={3,}|-{3,})$")) { l.Add(new Line { Text = t, Level = src[i + 1].Trim()[0] == '=' ? 1 : 2 }); i++; continue; }
            var m = Numbered.Match(t);
            if (m.Success && !t.EndsWith(".") && !t.EndsWith(",") && (i == 0 || src[i - 1].Trim() == "")) { l.Add(new Line { Text = t, Level = m.Groups[1].Value.Split('.').Length }); continue; }
            if (t.Length >= 4 && t.Length <= 60 && t == t.ToUpperInvariant() && Regex.IsMatch(t, @"\p{Lu}{3}") && (i == 0 || src[i - 1].Trim() == "") && (i + 1 >= src.Length || src[i + 1].Trim() == "")) { l.Add(new Line { Text = t, Level = 1 }); continue; }
            l.Add(new Line { Text = s });
        }
        return l;
    }

    static List<Line> Markdown(string text) {
        var src = text.Replace("\r", "").Split('\n'); var l = new List<Line>(); bool code = false;
        for (int i = 0; i < src.Length; i++) {
            string s = src[i];
            if (s.TrimStart().StartsWith("```")) { code = !code; continue; }
            if (code) { l.Add(new Line { Text = s }); continue; }
            var m = Regex.Match(s, @"^(#{1,6})\s+(.*?)\s*#*\s*$");
            if (m.Success) { l.Add(new Line { Text = Inline(m.Groups[2].Value), Level = m.Groups[1].Length }); continue; }
            if (i + 1 < src.Length && s.Trim().Length > 0 && Regex.IsMatch(src[i + 1], @"^(=+|-{2,})\s*$") && !s.TrimStart().StartsWith("|")) { l.Add(new Line { Text = Inline(s.Trim()), Level = src[i + 1][0] == '=' ? 1 : 2 }); i++; continue; }
            l.Add(new Line { Text = Inline(s) });
        }
        return l;
    }
    static string Inline(string s) {
        s = Regex.Replace(s, @"!\[([^\]]*)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
        return Regex.Replace(s, @"(\*\*|__|\*|`)", "");
    }

    static List<Line> Html(string html) {
        html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<(script|style|head|noscript)\b.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var l = new List<Line>(); int pos = 0;
        var rx = new Regex(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in rx.Matches(html)) {
            foreach (var s in Strip(html.Substring(pos, m.Index - pos))) l.Add(new Line { Text = s });
            string h = string.Join(" ", Strip(m.Groups[2].Value).ToArray()).Trim();
            if (h != "") l.Add(new Line { Text = h, Level = int.Parse(m.Groups[1].Value) });
            pos = m.Index + m.Length;
        }
        foreach (var s in Strip(html.Substring(pos))) l.Add(new Line { Text = s });
        return l;
    }
    static List<string> Strip(string html) {
        string s = Regex.Replace(html, @"<(br|/p|/div|/li|/tr|/h[1-6]|/table|/ul|/ol|/section|/article|/dd|/dt|/pre|/blockquote)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        var l = new List<string>();
        foreach (var line in s.Replace("\r", "").Split('\n')) { string t = Regex.Replace(line, @"[  ]+", " ").Trim(); if (t != "") l.Add(t); }
        return l;
    }

    static List<Line> Docx(string file) {
        string xml;
        using (var zip = ZipFile.OpenRead(file)) {
            var e = zip.GetEntry("word/document.xml");
            if (e == null) throw new Exception("not a DOCX file (no word/document.xml): " + file);
            using (var sr = new StreamReader(e.Open(), Encoding.UTF8)) xml = sr.ReadToEnd();
        }
        var l = new List<Line>();
        foreach (Match p in Regex.Matches(xml, @"<w:p[ >].*?</w:p>", RegexOptions.Singleline)) {
            string px = p.Value; int level = 0;
            var st = Regex.Match(px, @"<w:pStyle w:val=""([^""]+)""");
            if (st.Success) {
                string v = st.Groups[1].Value; var hm = Regex.Match(v, @"(?i)^(?:heading|berschrift|Überschrift|titre|ttulo)\s*([1-9])$");
                if (hm.Success) level = int.Parse(hm.Groups[1].Value); else if (v == "Title" || v == "Titel") level = 1;
            }
            var ol = Regex.Match(px, @"<w:outlineLvl w:val=""(\d)""");
            if (level == 0 && ol.Success) level = int.Parse(ol.Groups[1].Value) + 1;
            var sb = new StringBuilder();
            foreach (Match t in Regex.Matches(px, @"<w:t(?: [^>]*)?>(.*?)</w:t>|<w:tab/>|<w:br/>|<w:cr/>", RegexOptions.Singleline)) {
                if (t.Value.StartsWith("<w:tab")) sb.Append('\t'); else if (t.Value.StartsWith("<w:br") || t.Value.StartsWith("<w:cr")) sb.Append('\n'); else sb.Append(System.Net.WebUtility.HtmlDecode(t.Groups[1].Value));
            }
            string text = sb.ToString().Trim();
            if (text == "") { if (level == 0) l.Add(new Line { Text = "" }); continue; }
            if (level > 0) l.Add(new Line { Text = text.Replace('\n', ' '), Level = level });
            else foreach (var s in text.Split('\n')) l.Add(new Line { Text = s });
        }
        return l;
    }

    // ------------------------------------------------------------------ PDF
    static class Pdf {
        static readonly Encoding L1 = Encoding.GetEncoding(28591), Cp1252 = Encoding.GetEncoding(1252);
        class Obj { public string Dict; public byte[] Stream; }
        class Font {
            public Dictionary<int, string> Map; public int CodeBytes = 1; public bool Unreadable;
            public Dictionary<int, double> Widths = new Dictionary<int, double>(); public double DefaultWidth = 500;   // thousandths of the font size
        }

        static byte[] Inflate(byte[] data) {
            try {
                using (var ms = new MemoryStream(data, 2, Math.Max(0, data.Length - 2))) using (var ds = new DeflateStream(ms, CompressionMode.Decompress)) using (var o = new MemoryStream()) { ds.CopyTo(o); return o.ToArray(); }
            } catch { return null; }
        }

        static Dictionary<int, Obj> Objects(byte[] bytes, List<object> warnings) {
            string s = L1.GetString(bytes); var objs = new Dictionary<int, Obj>();
            if (Regex.IsMatch(s, @"/Encrypt\s+\d+\s+\d+\s+R|/Encrypt\s*<<")) throw new Exception("the PDF is encrypted: remove the protection first");
            foreach (Match m in Regex.Matches(s, @"(?<![\d.])(\d+)\s+(\d+)\s+obj\b")) {
                int start = m.Index + m.Length, end = s.IndexOf("endobj", start, StringComparison.Ordinal); if (end < 0) end = s.Length;
                int st = s.IndexOf("stream", start, StringComparison.Ordinal); var o = new Obj();
                if (st >= 0 && st < end && (s[st + 6] == '\r' || s[st + 6] == '\n')) {
                    o.Dict = s.Substring(start, st - start);
                    int ds = st + 6; if (s[ds] == '\r') ds++; if (ds < s.Length && s[ds] == '\n') ds++;
                    int es = s.IndexOf("endstream", ds, StringComparison.Ordinal); if (es < 0) es = end;
                    // the object may end later than the first "endobj" when the stream contains that word
                    var lm = Regex.Match(o.Dict, @"/Length\s+(\d+)(?!\s+\d+\s+R)");
                    int len = lm.Success ? int.Parse(lm.Groups[1].Value) : es - ds;
                    if (ds + len > bytes.Length || len < 0) len = Math.Max(0, es - ds);
                    var raw = new byte[len]; Array.Copy(bytes, ds, raw, 0, len);
                    if (o.Dict.Contains("/FlateDecode")) { var inf = Inflate(raw); if (inf == null && es - ds != len) { raw = new byte[Math.Max(0, es - ds)]; Array.Copy(bytes, ds, raw, 0, raw.Length); inf = Inflate(raw); } o.Stream = inf; }
                    else if (!Regex.IsMatch(o.Dict, @"/Filter")) o.Stream = raw;
                } else o.Dict = s.Substring(start, end - start);
                objs[int.Parse(m.Groups[1].Value)] = o;
            }
            // objects packed into object streams
            foreach (var o in new List<Obj>(objs.Values)) {
                if (o.Stream == null || !Regex.IsMatch(o.Dict, @"/Type\s*/ObjStm")) continue;
                var nm = Regex.Match(o.Dict, @"/N\s+(\d+)"); var fm = Regex.Match(o.Dict, @"/First\s+(\d+)");
                if (!nm.Success || !fm.Success) continue;
                string t = L1.GetString(o.Stream); int n = int.Parse(nm.Groups[1].Value), first = int.Parse(fm.Groups[1].Value);
                var nums = Regex.Matches(t.Substring(0, Math.Min(first, t.Length)), @"\d+");
                for (int i = 0; i + 1 < nums.Count && i / 2 < n; i += 2) {
                    int num = int.Parse(nums[i].Value), off = first + int.Parse(nums[i + 1].Value);
                    int next = i + 3 < nums.Count ? first + int.Parse(nums[i + 3].Value) : t.Length;
                    if (off <= t.Length && next <= t.Length && next >= off && !objs.ContainsKey(num)) objs[num] = new Obj { Dict = t.Substring(off, next - off) };
                }
            }
            return objs;
        }

        static string Deref(Dictionary<int, Obj> objs, string value) {
            var m = Regex.Match(value.Trim(), @"^(\d+)\s+\d+\s+R");
            Obj o; return m.Success && objs.TryGetValue(int.Parse(m.Groups[1].Value), out o) ? o.Dict : value;
        }
        /// <summary>Value of a key in a dictionary text: a reference, a nested dictionary, an array or a token.</summary>
        static string Value(string dict, string key) {
            var m = Regex.Match(dict, Regex.Escape(key) + @"(?![A-Za-z0-9])\s*"); if (!m.Success) return null;
            int i = m.Index + m.Length; if (i >= dict.Length) return null;
            if (dict[i] == '<' && i + 1 < dict.Length && dict[i + 1] == '<') { int depth = 0, j = i; while (j + 1 < dict.Length) { if (dict[j] == '<' && dict[j + 1] == '<') { depth++; j += 2; } else if (dict[j] == '>' && dict[j + 1] == '>') { depth--; j += 2; if (depth == 0) break; } else j++; } return dict.Substring(i, j - i); }
            if (dict[i] == '[') { int j = dict.IndexOf(']', i); return j < 0 ? null : dict.Substring(i, j - i + 1); }
            var r = Regex.Match(dict.Substring(i), @"^(\d+\s+\d+\s+R|/[^\s/<>\[\]()]+|[^\s/<>\[\]]+)");
            return r.Success ? r.Value : null;
        }

        static Font LoadFont(Dictionary<int, Obj> objs, string fontDict) {
            var f = new Font(); string tu = Value(fontDict, "/ToUnicode");
            var m = tu == null ? Match.Empty : Regex.Match(tu, @"^(\d+)\s+\d+\s+R"); Obj o;
            if (m.Success && objs.TryGetValue(int.Parse(m.Groups[1].Value), out o) && o.Stream != null) {
                f.Map = new Dictionary<int, string>(); string c = L1.GetString(o.Stream);
                foreach (Match sec in Regex.Matches(c, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                    foreach (Match p in Regex.Matches(sec.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>")) { f.CodeBytes = Math.Max(1, p.Groups[1].Length / 2); f.Map[Convert.ToInt32(p.Groups[1].Value, 16)] = Utf16(p.Groups[2].Value); }
                foreach (Match sec in Regex.Matches(c, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline)) {
                    foreach (Match p in Regex.Matches(sec.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]+)>|\[(.*?)\])", RegexOptions.Singleline)) {
                        int a = Convert.ToInt32(p.Groups[1].Value, 16), b = Convert.ToInt32(p.Groups[2].Value, 16); f.CodeBytes = Math.Max(1, p.Groups[1].Length / 2);
                        if (b - a > 65535) continue;
                        if (p.Groups[4].Success) { string d = Utf16(p.Groups[4].Value); for (int k = a; k <= b; k++) f.Map[k] = d.Length == 0 ? "" : d.Substring(0, d.Length - 1) + (char)(d[d.Length - 1] + (k - a)); }
                        else { var arr = Regex.Matches(p.Groups[5].Value, @"<([0-9A-Fa-f]*)>"); for (int k = a; k <= b && k - a < arr.Count; k++) f.Map[k] = Utf16(arr[k - a].Groups[1].Value); }
                    }
                }
                var cs = Regex.Match(c, @"begincodespacerange\s*<([0-9A-Fa-f]+)>");
                if (cs.Success) f.CodeBytes = Math.Max(1, cs.Groups[1].Length / 2);
            } else if (Regex.IsMatch(fontDict, @"/Identity-[HV]")) f.Unreadable = true;
            if (Regex.IsMatch(fontDict, @"/Identity-[HV]|/Type0")) f.CodeBytes = 2;
            // glyph widths: they tell where a piece of text ends, and so whether a gap is a blank
            string wd = Value(fontDict, "/Widths"); var fc = Regex.Match(fontDict, @"/FirstChar\s+(\d+)");
            if (wd != null && fc.Success) {
                int code = int.Parse(fc.Groups[1].Value);
                foreach (Match w in Regex.Matches(Deref(objs, wd), @"-?[\d.]+")) f.Widths[code++] = double.Parse(w.Value, CultureInfo.InvariantCulture);
            }
            string desc = Value(fontDict, "/DescendantFonts");
            if (desc != null) {
                string cid = Deref(objs, Deref(objs, desc).Trim().TrimStart('[').Trim());
                var dw = Regex.Match(cid, @"/DW\s+([\d.]+)"); f.DefaultWidth = dw.Success ? double.Parse(dw.Groups[1].Value, CultureInfo.InvariantCulture) : 1000;
                int wi = cid.IndexOf("/W", StringComparison.Ordinal);
                while (wi >= 0 && wi + 2 < cid.Length && char.IsLetterOrDigit(cid[wi + 2])) wi = cid.IndexOf("/W", wi + 2, StringComparison.Ordinal);
                if (wi >= 0) {
                    string rest = cid.Substring(wi + 2).TrimStart(); string arr;
                    if (rest.StartsWith("[")) { int depth = 0, j = 0; for (; j < rest.Length; j++) { if (rest[j] == '[') depth++; else if (rest[j] == ']') { depth--; if (depth == 0) break; } } arr = rest.Substring(1, Math.Max(0, j - 1)); }
                    else { arr = Deref(objs, rest).Trim(); if (arr.StartsWith("[")) arr = arr.Substring(1, Math.Max(0, arr.LastIndexOf(']') - 1)); }
                    var toks = Regex.Matches(arr, @"\[[^\]]*\]|-?[\d.]+");
                    for (int i = 0; i < toks.Count; i++) {
                        if (toks[i].Value.StartsWith("[")) continue;
                        int first = (int)double.Parse(toks[i].Value, CultureInfo.InvariantCulture);
                        if (i + 1 < toks.Count && toks[i + 1].Value.StartsWith("[")) { foreach (Match w in Regex.Matches(toks[i + 1].Value, @"-?[\d.]+")) f.Widths[first++] = double.Parse(w.Value, CultureInfo.InvariantCulture); i++; }
                        else if (i + 2 < toks.Count) { int last = (int)double.Parse(toks[i + 1].Value, CultureInfo.InvariantCulture); double w = double.Parse(toks[i + 2].Value, CultureInfo.InvariantCulture); for (int c2 = first; c2 <= last && c2 - first < 70000; c2++) f.Widths[c2] = w; i += 2; }
                    }
                }
            }
            return f;
        }
        /// <summary>Advance of a piece of text in thousandths of the font size.</summary>
        static double Advance(byte[] raw, Font f) {
            if (f == null) return raw.Length * 500.0;
            double sum = 0;
            for (int i = 0; i + f.CodeBytes <= raw.Length; i += f.CodeBytes) { int code = 0; for (int k = 0; k < f.CodeBytes; k++) code = (code << 8) | raw[i + k]; double w; sum += f.Widths.TryGetValue(code, out w) ? w : f.DefaultWidth; }
            return sum;
        }
        static string Utf16(string hex) { if (hex.Length % 4 != 0) hex = hex.PadLeft((hex.Length + 3) / 4 * 4, '0'); var sb = new StringBuilder(); for (int i = 0; i + 4 <= hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16)); return sb.ToString(); }

        static string Decode(byte[] raw, Font f) {
            if (f == null || (f.Map == null && !f.Unreadable)) return Cp1252.GetString(raw);
            if (f.Map == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i + f.CodeBytes <= raw.Length; i += f.CodeBytes) { int code = 0; for (int k = 0; k < f.CodeBytes; k++) code = (code << 8) | raw[i + k]; string v; if (f.Map.TryGetValue(code, out v)) sb.Append(v); }
            return sb.ToString();
        }

        static void Pages(Dictionary<int, Obj> objs, int num, string inherited, List<string[]> pages, int depth) {
            Obj o; if (depth > 40 || !objs.TryGetValue(num, out o)) return;
            string res = Value(o.Dict, "/Resources") ?? inherited;
            if (Regex.IsMatch(o.Dict, @"/Type\s*/Pages\b")) {
                string kids = Value(o.Dict, "/Kids"); if (kids != null) kids = Deref(objs, kids);
                if (kids != null) foreach (Match k in Regex.Matches(kids, @"(\d+)\s+\d+\s+R")) Pages(objs, int.Parse(k.Groups[1].Value), res, pages, depth + 1);
            } else pages.Add(new[] { o.Dict, res });
        }

        public static List<Line> Lines(byte[] bytes, List<object> warnings) {
            if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new Exception("not a PDF file");
            var objs = Objects(bytes, warnings); var pages = new List<string[]>();
            foreach (var kv in objs) if (Regex.IsMatch(kv.Value.Dict, @"/Type\s*/Catalog\b")) { var r = Regex.Match(Value(kv.Value.Dict, "/Pages") ?? "", @"^(\d+)"); if (r.Success) Pages(objs, int.Parse(r.Groups[1].Value), null, pages, 0); break; }
            if (pages.Count == 0) { var keys = new List<int>(objs.Keys); keys.Sort(); foreach (int k in keys) if (Regex.IsMatch(objs[k].Dict, @"/Type\s*/Page\b(?!s)")) pages.Add(new[] { objs[k].Dict, Value(objs[k].Dict, "/Resources") }); }
            var fontCache = new Dictionary<string, Font>(); var raw = new List<Line>(); bool unreadable = false;
            for (int pn = 0; pn < pages.Count; pn++) {
                string res = pages[pn][1] == null ? "" : Deref(objs, pages[pn][1]);
                string fonts = Value(res, "/Font"); fonts = fonts == null ? "" : Deref(objs, fonts);
                var pageFonts = new Dictionary<string, Font>();
                foreach (Match fm in Regex.Matches(fonts, @"/([^\s/<>\[\]()]+)\s+(\d+)\s+\d+\s+R")) {
                    string key = fm.Groups[2].Value; Font f;
                    if (!fontCache.TryGetValue(key, out f)) { Obj fo; f = objs.TryGetValue(int.Parse(key), out fo) ? LoadFont(objs, fo.Dict) : new Font(); fontCache[key] = f; }
                    pageFonts[fm.Groups[1].Value] = f;
                }
                string contents = Value(pages[pn][0], "/Contents"); if (contents == null) continue;
                if (contents.StartsWith("[") == false && Regex.IsMatch(contents, @"^\d+\s+\d+\s+R")) { Obj co; if (objs.TryGetValue(int.Parse(Regex.Match(contents, @"^\d+").Value), out co) && co.Stream == null && co.Dict.TrimStart().StartsWith("[")) contents = co.Dict; }
                using (var ms = new MemoryStream()) {
                    foreach (Match c in Regex.Matches(contents, @"(\d+)\s+\d+\s+R")) { Obj co; if (objs.TryGetValue(int.Parse(c.Groups[1].Value), out co) && co.Stream != null) { ms.Write(co.Stream, 0, co.Stream.Length); ms.WriteByte((byte)'\n'); } }
                    Content(ms.ToArray(), pageFonts, pn + 1, raw, ref unreadable);
                }
            }
            if (unreadable) warnings.Add("some text uses fonts without a character map (ToUnicode) and could not be read");
            if (pages.Count == 0) warnings.Add("no pages were found in the PDF");
            // headings: clearly larger than the body text, or numbered like "2.1 Title"
            var bySize = new Dictionary<double, int>(); foreach (var l in raw) { double k = Math.Round(l.Size, 1); int c; bySize.TryGetValue(k, out c); bySize[k] = c + l.Text.Length; }
            double bodySize = 0; int best = -1; foreach (var kv in bySize) if (kv.Value > best) { best = kv.Value; bodySize = kv.Key; }
            var sizes = new List<double>(); foreach (var l in raw) { double k = Math.Round(l.Size, 1); if (k >= bodySize * 1.15 && l.Text.Length <= 120 && !sizes.Contains(k)) sizes.Add(k); }
            sizes.Sort(); sizes.Reverse();
            foreach (var l in raw) {
                int idx = sizes.IndexOf(Math.Round(l.Size, 1));
                if (idx >= 0 && l.Text.Length <= 120 && Regex.IsMatch(l.Text, @"\p{L}")) l.Level = Math.Min(6, idx + 1);
                else { var m = Numbered.Match(l.Text.Trim()); if (m.Success && sizes.Count == 0 && !l.Text.TrimEnd().EndsWith(".")) l.Level = m.Groups[1].Value.Split('.').Length; }
            }
            return raw;
        }

        static void Content(byte[] data, Dictionary<string, Font> fonts, int page, List<Line> lines, ref bool unreadable) {
            var stack = new List<object>(); Font font = null; double size = 0, a = 1, d = 1, lineX = 0, x = 0, y = 0, penEnd = 0, lineY = double.NaN, leading = 0; var cur = new StringBuilder(); double curSize = 0;
            int i = 0, n = data.Length;
            Action flush = delegate () { string t = Regex.Replace(cur.ToString(), @"\s+", " ").Trim(); if (t != "") lines.Add(new Line { Text = t, Page = page, Size = curSize }); cur.Length = 0; curSize = 0; };
            // a piece of text at the current position: a new line when the height changed, a blank when
            // the piece starts clearly to the right of where the last one ended
            Action<byte[]> show = delegate (byte[] raw) {
                string t = Decode(raw, font);
                double eff = Math.Abs(size * d), em = Math.Abs(size * a);
                if (t.Length > 0) {
                    if (cur.Length > 0 && !double.IsNaN(lineY) && Math.Abs(y - lineY) > Math.Max(1, eff * 0.5)) flush();
                    if (cur.Length == 0) lineY = y;
                    else if (x - penEnd > 0.2 * em && cur[cur.Length - 1] != ' ' && t[0] != ' ') cur.Append(' ');
                    cur.Append(t); if (eff > curSize) curSize = eff;
                }
                x += Advance(raw, font) / 1000.0 * size * a; penEnd = x;
            };
            while (i < n) {
                byte c = data[i];
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == 0 || c == 12) { i++; continue; }
                if (c == '%') { while (i < n && data[i] != '\n' && data[i] != '\r') i++; continue; }
                if (c == '(') {
                    var b = new List<byte>(); int depth = 1; i++;
                    while (i < n && depth > 0) {
                        byte ch = data[i++];
                        if (ch == '\\' && i < n) {
                            byte e = data[i++];
                            if (e == 'n') b.Add(10); else if (e == 'r') b.Add(13); else if (e == 't') b.Add(9); else if (e == 'b') b.Add(8); else if (e == 'f') b.Add(12);
                            else if (e >= '0' && e <= '7') { int v = e - '0'; for (int k = 0; k < 2 && i < n && data[i] >= '0' && data[i] <= '7'; k++) v = v * 8 + (data[i++] - '0'); b.Add((byte)v); }
                            else if (e == '\r') { if (i < n && data[i] == '\n') i++; } else if (e == '\n') { } else b.Add(e);
                        } else if (ch == '(') { depth++; b.Add(ch); } else if (ch == ')') { depth--; if (depth > 0) b.Add(ch); } else b.Add(ch);
                    }
                    stack.Add(b.ToArray()); continue;
                }
                if (c == '<' && i + 1 < n && data[i + 1] != '<') {
                    var hex = new StringBuilder(); i++;
                    while (i < n && data[i] != '>') { if (Uri.IsHexDigit((char)data[i])) hex.Append((char)data[i]); i++; }
                    i++; if (hex.Length % 2 == 1) hex.Append('0');
                    var b = new byte[hex.Length / 2]; for (int k = 0; k < b.Length; k++) b[k] = Convert.ToByte(hex.ToString(k * 2, 2), 16);
                    stack.Add(b); continue;
                }
                if (c == '<' || c == '>') { i += 2; continue; }
                if (c == '[') { stack.Add("["); i++; continue; }
                if (c == ']') {
                    var arr = new List<object>(); int k = stack.Count - 1; while (k >= 0 && !(stack[k] is string && (string)stack[k] == "[")) k--;
                    if (k >= 0) { for (int j = k + 1; j < stack.Count; j++) arr.Add(stack[j]); stack.RemoveRange(k, stack.Count - k); }
                    stack.Add(arr); i++; continue;
                }
                int s = i; while (i < n && " \n\r\t()<>[]/%".IndexOf((char)data[i]) < 0) i++;
                if (i == s) { i++; while (i < n && " \n\r\t()<>[]/%".IndexOf((char)data[i]) < 0) i++; }   // a name: "/F1"
                string tok = Encoding.ASCII.GetString(data, s, i - s); double num;
                if (tok.StartsWith("/")) { stack.Add(tok); continue; }
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) { stack.Add(num); continue; }
                // an operator
                Func<int, double> arg = delegate (int fromEnd) { int idx = stack.Count - fromEnd; return idx >= 0 && stack[idx] is double ? (double)stack[idx] : 0; };
                if (tok == "Tf" && stack.Count >= 2) { string fn = (stack[stack.Count - 2] as string ?? "/").Substring(1); fonts.TryGetValue(fn, out font); if (font != null && font.Unreadable) unreadable = true; size = stack[stack.Count - 1] is double ? (double)stack[stack.Count - 1] : size; }
                else if (tok == "Tm" && stack.Count >= 6) { a = arg(6) == 0 ? 1 : arg(6); d = arg(3) == 0 ? 1 : arg(3); lineX = x = arg(2); y = arg(1); }
                else if ((tok == "Td" || tok == "TD") && stack.Count >= 2) { lineX += arg(2) * a; x = lineX; y += arg(1) * d; if (tok == "TD") leading = -arg(1); }
                else if (tok == "TL" && stack.Count >= 1) leading = arg(1);
                else if (tok == "T*") { y -= (leading == 0 ? size : leading) * d; x = lineX; }
                else if (tok == "BT") { a = 1; d = 1; lineX = x = 0; y = 0; }
                else if (tok == "Tj" && stack.Count >= 1 && stack[stack.Count - 1] is byte[]) show((byte[])stack[stack.Count - 1]);
                else if ((tok == "'" || tok == "\"") && stack.Count >= 1 && stack[stack.Count - 1] is byte[]) { y -= (leading == 0 ? size : leading) * d; x = lineX; show((byte[])stack[stack.Count - 1]); }
                else if (tok == "TJ" && stack.Count >= 1 && stack[stack.Count - 1] is List<object>) {
                    foreach (var e in (List<object>)stack[stack.Count - 1]) { if (e is byte[]) show((byte[])e); else if (e is double) x -= (double)e / 1000.0 * size * a; }
                }
                stack.Clear();
            }
            flush();
        }
    }
}
}
