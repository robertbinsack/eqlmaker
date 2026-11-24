// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EqlMaker.Tests {

public static class I18nTests {
    static string Dir { get { return Path.Combine(T.RepoRoot, "src", "i18n"); } }

    /// <summary>en.json is the export of the texts in the code; when a text changes, export again.</summary>
    public static void EnglishFileIsTheExportOfTheCode() {
        string exported = Path.Combine(T.TempDir("i18n"), "en.json"), o;
        T.Eq(0, T.Run(Path.Combine(T.BinDir, "eqlmaker.exe"), "i18n-export \"" + exported + "\" --lang en", null, out o, 30000), "export " + o);
        var a = Json.ReadFile(exported); var b = Json.ReadFile(Path.Combine(Dir, "en.json"));
        var missing = new List<string>(); var changed = new List<string>();
        foreach (var kv in a) { if (!b.Has(kv.Key)) missing.Add(kv.Key); else if (b.Str(kv.Key) != (string)kv.Value) changed.Add(kv.Key); }
        foreach (var k in b.Keys) if (!a.Has(k)) missing.Add("(stale) " + k);
        T.True(missing.Count == 0 && changed.Count == 0, "src/i18n/en.json matches the code; run 'eqlmaker i18n-export src\\i18n\\en.json'. Missing: " + string.Join(", ", missing.ToArray()) + "; changed: " + string.Join(", ", changed.ToArray()));
    }

    public static void TranslationsHaveTheSameKeysAndPlaceholders() {
        var en = Json.ReadFile(Path.Combine(Dir, "en.json")); int n = 0;
        foreach (var f in Directory.GetFiles(Dir, "*.json")) {
            if (Path.GetFileName(f) == "en.json") continue;
            var tr = Json.ReadFile(f); n++; var problems = new List<string>();
            foreach (var k in en.Keys) {
                if (!tr.Has(k) || string.IsNullOrEmpty(tr.Str(k))) { problems.Add("missing " + k); continue; }
                // placeholders, tool names and option names must survive the translation
                foreach (Match m in Regex.Matches(en.Str(k), @"\{\d\}|--[a-z][a-z-]+|\b[a-z]+_[a-z_]+\b")) if (tr.Str(k).IndexOf(m.Value, StringComparison.Ordinal) < 0) problems.Add(k + " lost '" + m.Value + "'");
            }
            foreach (var k in tr.Keys) if (!en.Has(k)) problems.Add("unknown key " + k);
            T.True(problems.Count == 0, Path.GetFileName(f) + ": " + string.Join("; ", problems.GetRange(0, Math.Min(12, problems.Count)).ToArray()) + (problems.Count > 12 ? " ... (" + problems.Count + ")" : ""));
        }
        T.True(n >= 1, "at least one translation (de.json) exists");
    }

    public static void GermanIsUsedWhenAskedFor() {
        string exe = Path.Combine(T.BinDir, "eqlmaker.exe"), d = T.TempDir("i18n-de"), o;
        var de = Json.ReadFile(Path.Combine(Dir, "de.json"));
        T.Eq(1, T.Run(exe, "frobnicate --lang de", d, out o, 20000), "exit code"); T.Contains(o, string.Format(de.Str("msg.unknown_command"), "frobnicate"), "German message");
        T.Eq(1, T.Run(exe, "frobnicate --lang en", d, out o, 20000), "exit code"); T.Contains(o, "unknown command 'frobnicate'", "English message");
        T.Eq(1, T.Run(exe, "frobnicate --lang xx", d, out o, 20000), "exit code"); T.Contains(o, "unknown command 'frobnicate'", "an unknown language falls back to English");
        File.WriteAllText(Path.Combine(d, "eqlmaker.json"), "{\"lang\":\"de\"}");
        T.Run(exe, "help", d, out o, 20000); T.Contains(o, de.Str("usage").Split('\n')[2].Trim(), "language from the configuration: German help");
        T.Run(exe, "help --lang en", d, out o, 20000); T.Contains(o, "create a project folder", "--lang wins over the configuration");
        // MCP: tool names stay, descriptions are German
        var psi = new ProcessStartInfo(exe, "mcp --lang de --project \"" + d + "\""); psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true; psi.StandardOutputEncoding = Encoding.UTF8;
        using (var p = Process.Start(psi)) {
            p.ErrorDataReceived += delegate { }; p.BeginErrorReadLine();
            p.StandardInput.Write("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}\n"); p.StandardInput.Close();
            var r = Json.ParseObject(p.StandardOutput.ReadLine()); p.WaitForExit(20000);
            var tool = r.Obj("result").Arr("tools").Find(delegate (object t) { return ((JO)t).Str("name") == "verify_db"; }) as JO;
            T.Eq(de.Str("tool.verify_db"), tool.Str("description"), "German tool description"); T.True(tool.Obj("inputSchema").Obj("properties").Has("ignore"), "parameter names stay");
        }
    }
}
}
