// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Texts of the programs in more than one language. English is the reference and lives in the code;
// share\i18n\en.json is its export (for translators and for the check that nothing is missing),
// share\i18n\<language>.json holds a translation with the same keys.
// Language: --lang, else "lang" in eqlmaker.json, else the language of Windows, else English.
// File formats, identifiers, tool names and error messages of the engine stay English.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EqlMaker {

public static class Texts {
    public static string Lang = "en";
    static JO loaded; static string loadedLang;

    public static void Choose(string option, Config cfg) {
        string l = option;
        if (string.IsNullOrEmpty(l) && cfg != null) l = cfg.Raw.Str("lang");
        if (string.IsNullOrEmpty(l)) l = Environment.GetEnvironmentVariable("EQLMAKER_LANG");
        if (string.IsNullOrEmpty(l)) l = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        l = l.ToLowerInvariant();
        Lang = l != "en" && File.Exists(FileOf(l)) ? l : "en";
    }
    static string FileOf(string lang) { try { return Share.File("i18n", lang + ".json"); } catch { return ""; } }

    /// <summary>The text for a key in the chosen language; the English text when the translation has none.</summary>
    public static string T(string key, string english) {
        if (Lang == "en") return english;
        if (loadedLang != Lang) { try { loaded = Json.ReadFile(FileOf(Lang)); } catch { loaded = new JO(); } loadedLang = Lang; }
        string v = loaded.Str(key);
        return string.IsNullOrEmpty(v) ? english : v;
    }

    /// <summary>All English texts by key (the content of en.json).</summary>
    public static JO Export(string usage) {
        var o = new JO(); o["usage"] = usage;
        var s = new McpServer(new Controller(Config.Load(Path.GetTempPath()))); Pipeline.Register(s);
        o["mcp.instructions"] = s.Instructions;
        foreach (var t in s.Tools) {
            o["tool." + t.Name] = t.Description;
            foreach (var p in t.Properties) o["tool." + t.Name + "." + p.Key] = ((JO)p.Value).Str("description");
        }
        foreach (var p in s.Prompts) o["prompt." + p.Name] = p.Description;
        foreach (var kv in Messages) o[kv.Key] = kv.Value;
        return o;
    }

    /// <summary>Applies the chosen language to the texts an MCP client sees.</summary>
    public static void Apply(McpServer s) {
        if (Lang == "en") return;
        s.Instructions = T("mcp.instructions", s.Instructions);
        foreach (var t in s.Tools) {
            t.Description = T("tool." + t.Name, t.Description);
            foreach (var p in t.Properties) { var d = (JO)p.Value; d["description"] = T("tool." + t.Name + "." + p.Key, d.Str("description")); }
        }
        foreach (var p in s.Prompts) p.Description = T("prompt." + p.Name, p.Description);
    }

    /// <summary>Short messages of the command line that have a key.</summary>
    public static readonly Dictionary<string, string> Messages = new Dictionary<string, string> {
        { "msg.unknown_command", "unknown command '{0}' (eqlmaker help)" },
        { "msg.missing_argument", "missing argument: {0}" },
        { "msg.selftest_title", "EQLMaker {0} self test" },
        { "msg.selftest_passed", "SELF TEST PASSED" },
        { "msg.selftest_failed", "SELF TEST FAILED" },
        { "msg.recording_started", "recording '{0}' started. Start the application now; end with: eqlmaker record --stop" },
        { "msg.no_recording", "no recording is running" },
        { "msg.capture_reset", "capture settings reset" },
        { "msg.project_created", "edit eqlmaker.json (connection, capture.databases), then: eqlmaker record --scenario <file>" }
    };
    public static string M(string key, params object[] args) { return string.Format(T(key, Messages[key]), args); }
}
}
