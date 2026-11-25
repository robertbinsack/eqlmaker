// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Project folder and configuration (eqlmaker.json), plugins (plugin.json), shared resources.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace EqlMaker {

/// <summary>A project is a folder with eqlmaker.json and one sub-folder per pipeline step.</summary>
public class Config {
    public const string FileName = "eqlmaker.json";
    public const string Version = "0.2.0";
    public string Dir; public JO Raw = new JO();

    public string Source { get { return Raw.Str("source", "postgres"); } }
    /// <summary>Connection string of the configuration; a "passfile" of the configuration (relative to the
    /// project folder) is added as libpq keyword.</summary>
    public string Connection {
        get {
            string c = Raw.Str("connection", ""), pf = Raw.Str("passfile", "");
            if (pf == "" || c.Contains("passfile=")) return c;
            string full = Path.IsPathRooted(pf) ? pf : Path.GetFullPath(Path.Combine(Dir, pf));
            return c + " passfile='" + full.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }
    }
    /// <summary>real (mouse, keyboard, screen) or background (window messages, no visible desktop needed).</summary>
    public string Input { get { return Raw.Str("input", "real"); } }

    public static Config Load(string dir) {
        var c = new Config(); c.Dir = Path.GetFullPath(string.IsNullOrEmpty(dir) ? "." : dir);
        string f = Path.Combine(c.Dir, FileName);
        if (File.Exists(f)) c.Raw = Json.ReadFile(f);
        return c;
    }
    public bool Exists { get { return File.Exists(Path.Combine(Dir, FileName)); } }

    public string Sub(params string[] parts) {
        string p = Dir; foreach (var s in parts) p = Path.Combine(p, s);
        return p;
    }
    public string RecordingsDir { get { return Sub(Raw.Str("recordings", "recordings")); } }
    /// <summary>Folder of a recording: a path, or a name inside the recordings folder.</summary>
    public string RecordingDir(string nameOrPath) {
        if (Directory.Exists(nameOrPath) && File.Exists(Path.Combine(nameOrPath, "recording.json"))) return Path.GetFullPath(nameOrPath);
        return Path.Combine(RecordingsDir, nameOrPath);
    }

    public CaptureOptions Capture() { return Capture(null); }
    /// <summary>The capture options; roles and databases of "over" (a scenario or a tool call) replace those of the configuration.</summary>
    public CaptureOptions Capture(JO over) {
        var o = new CaptureOptions(); o.Connection = Connection;
        var cap = Raw.Obj("capture") ?? new JO();
        o.Roles = cap.Strings("roles"); o.Databases = cap.Strings("databases");
        if (over != null && (over.Has("roles") || over.Has("databases"))) { o.Roles = over.Strings("roles"); o.Databases = over.Strings("databases"); }
        o.Changes = cap.Bool("changes", true); o.LogFile = cap.Str("logFile", ""); o.Decoding = cap.Str("decoding", "auto");
        o.SourceSettings = Raw.Obj(Source) ?? new JO();
        return o;
    }

    /// <summary>Path of an external tool: tools.&lt;name&gt; in the configuration, else the name itself (found through PATH).</summary>
    public string Tool(string name) {
        var t = Raw.Obj("tools");
        string v = t == null ? null : t.Str(name);
        return string.IsNullOrEmpty(v) ? name : v;
    }
    public string ToolOrNull(string name) { var t = Raw.Obj("tools"); string v = t == null ? null : t.Str(name); return string.IsNullOrEmpty(v) ? null : v; }

    public List<Plugin> Plugins() {
        var dirs = new List<string>();
        foreach (var d in Raw.Strings("plugins")) dirs.Add(Path.IsPathRooted(d) ? d : Sub(d));
        if (dirs.Count == 0 && Directory.Exists(Sub("plugins"))) dirs.Add(Sub("plugins"));
        return Plugin.LoadAll(dirs);
    }

    /// <summary>Creates the source named in the configuration: built-in, or a source plugin of the project.</summary>
    public IEventSource CreateSource() {
        if (Sources.Has(Source)) return Sources.Create(Source);
        var p = Plugin.Find(Plugins(), "source", Source);
        if (p == null) return Sources.Create(Source);   // throws with the list of known sources
        return new ExecSource(p.Name, p.Manifest.Str("command", ""), p.Dir) { StartTimeoutSeconds = p.Manifest.Int("startTimeoutSeconds", 60) };
    }

    public static JO Example() {
        return new JO("source", "postgres", "connection", "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
            "capture", new JO("roles", new List<object>(), "databases", new List<object> { "appdb" }, "changes", true, "logFile", ""),
            "eqlserver", new JO("statsPort", 0),
            "input", "real",
            "tools", new JO("ffmpeg", "", "transcribe", "", "transcribeModel", "", "browser", "", "node", ""),
            "plugins", new List<object> { "plugins" });
    }
}

/// <summary>Resources next to the program (viewer, templates, prompts).</summary>
public static class Share {
    public static string Root {
        get {
            string exe = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string s = Path.Combine(exe, "share");
            if (Directory.Exists(s)) return s;
            throw new Exception("resource folder not found: " + s + " (run build.cmd, or unpack the release completely)");
        }
    }
    public static string File(params string[] parts) {
        string p = Root; foreach (var s in parts) p = Path.Combine(p, s);
        return p;
    }
    public static void CopyDir(string from, string to, bool overwrite) {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) {
            string t = Path.Combine(to, Path.GetFileName(f));
            if (overwrite || !System.IO.File.Exists(t)) System.IO.File.Copy(f, t, true);
        }
        foreach (var d in Directory.GetDirectories(from)) {
            string n = Path.GetFileName(d);
            if (n == "node_modules" || n == ".git") continue;
            CopyDir(d, Path.Combine(to, n), overwrite);
        }
    }
}

/// <summary>A plugin is a folder with plugin.json: { "type": "source" | "web-template" | "clone-renderer" |
/// "video-layout" | "reader", "name": "...", ... }. See docs/plugins.md.</summary>
public class Plugin {
    public string Type, Name, Dir; public JO Manifest;
    public static readonly string[] Types = { "source", "web-template", "clone-renderer", "video-layout", "reader" };

    public static Plugin Load(string dir) {
        string f = Path.Combine(dir, "plugin.json");
        if (!File.Exists(f)) throw new Exception("no plugin.json in " + dir);
        var m = Json.ReadFile(f);
        var p = new Plugin { Dir = Path.GetFullPath(dir), Manifest = m, Type = m.Str("type", ""), Name = m.Str("name", "") };
        if (Array.IndexOf(Types, p.Type) < 0) throw new Exception(f + ": type must be one of " + string.Join(", ", Types));
        if (p.Name == "" || p.Name.IndexOfAny(new[] { '/', '\\', ' ' }) >= 0) throw new Exception(f + ": name is missing or contains spaces or slashes");
        if ((p.Type == "source" || p.Type == "clone-renderer" || p.Type == "reader") && m.Str("command", "") == "") throw new Exception(f + ": a " + p.Type + " plugin needs \"command\"");
        if (p.Type == "web-template" && !Directory.Exists(Path.Combine(p.Dir, m.Str("files", "files")))) throw new Exception(f + ": folder '" + m.Str("files", "files") + "' with the template files is missing");
        if (p.Type == "video-layout" && m.Str("filter", "") == "") throw new Exception(f + ": a video-layout plugin needs \"filter\"");
        return p;
    }

    public static List<Plugin> LoadAll(IEnumerable<string> dirs) {
        var list = new List<Plugin>();
        foreach (var root in dirs) {
            if (!Directory.Exists(root)) continue;
            if (File.Exists(Path.Combine(root, "plugin.json"))) { list.Add(Load(root)); continue; }
            foreach (var d in Directory.GetDirectories(root)) if (File.Exists(Path.Combine(d, "plugin.json"))) list.Add(Load(d));
        }
        return list;
    }

    public static Plugin Find(List<Plugin> list, string type, string name) {
        foreach (var p in list) if (p.Type == type && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
}
