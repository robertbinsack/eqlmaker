// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Small test runner without external packages: every public static void method without
// parameters in a class whose name ends with "Tests" is a test.
//   eqlmaker-tests.exe [filter] [--json results.json] [--list]
// Environment:
//   EQLMAKER_TEST_PG       connection string of a PostgreSQL superuser for the database tests
//   EQLMAKER_TEST_FFMPEG   path of ffmpeg for the video tests (default: ffmpeg from PATH)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace EqlMaker.Tests {

public class SkipException : Exception { public SkipException(string why) : base(why) { } }

public static class T {
    public static void True(bool ok, string what) { if (!ok) throw new Exception("expected: " + what); }
    public static void Eq(object expected, object actual, string what) {
        if (!Equals(expected, actual)) throw new Exception(what + ": expected <" + expected + "> but was <" + actual + ">");
    }
    public static void Near(double expected, double actual, double tolerance, string what) {
        if (Math.Abs(expected - actual) > tolerance) throw new Exception(what + ": expected " + expected + " +/- " + tolerance + " but was " + actual);
    }
    public static void Contains(string text, string part, string what) {
        if (text == null || text.IndexOf(part, StringComparison.Ordinal) < 0) throw new Exception(what + ": '" + part + "' not found in: " + (text == null ? "null" : text.Length > 600 ? text.Substring(0, 600) + " ..." : text));
    }
    public static void Throws(Action a, string part, string what) {
        try { a(); } catch (Exception e) { if (part == null || e.Message.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return; throw new Exception(what + ": wrong error: " + e.Message); }
        throw new Exception(what + ": no error");
    }
    public static void Skip(string why) { throw new SkipException(why); }

    /// <summary>Numbers a test wants to show in the result file (e.g. measured errors in milliseconds).</summary>
    public static void Note(string key, object value) { Runner.Notes[Runner.Current + "." + key] = value; Console.WriteLine("      " + key + " = " + value); }

    static string workRoot;
    public static string WorkRoot {
        get {
            if (workRoot == null) { workRoot = Path.Combine(Path.GetTempPath(), "eqlmaker-tests-" + Process.GetCurrentProcess().Id); Directory.CreateDirectory(workRoot); }
            return workRoot;
        }
    }
    public static string TempDir(string name) {
        string d = Path.Combine(WorkRoot, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }
    public static string RepoRoot {
        get {
            string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (d != null && !File.Exists(Path.Combine(d, "build.ps1"))) d = Path.GetDirectoryName(d);
            if (d == null) throw new SkipException("repository folder not found (tests run from a release package)");
            return d;
        }
    }
    public static string BinDir { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }

    /// <summary>Connection string of the test server; the test is skipped without it.</summary>
    public static string Pg {
        get {
            string s = Environment.GetEnvironmentVariable("EQLMAKER_TEST_PG");
            if (string.IsNullOrEmpty(s)) throw new SkipException("EQLMAKER_TEST_PG is not set");
            return s;
        }
    }
    public static string Ffmpeg {
        get {
            string s = Environment.GetEnvironmentVariable("EQLMAKER_TEST_FFMPEG");
            if (string.IsNullOrEmpty(s)) s = "ffmpeg";
            try {
                var psi = new ProcessStartInfo(s, "-version"); psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
                using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); if (p.ExitCode == 0) return s; }
            } catch { }
            throw new SkipException("ffmpeg not found");
        }
    }

    /// <summary>Runs a program and returns exit code and output.</summary>
    public static int Run(string exe, string args, string cwd, out string output, int timeoutMs) {
        var psi = new ProcessStartInfo(exe, args); psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8; psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        if (cwd != null) psi.WorkingDirectory = cwd;
        using (var p = Process.Start(psi)) {
            var sb = new System.Text.StringBuilder();
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } output = sb.ToString(); throw new Exception("timeout: " + exe + " " + args + "\n" + output); }
            p.WaitForExit();
            output = sb.ToString();
            return p.ExitCode;
        }
    }
}

public static class Runner {
    public static string Current = "";
    public static readonly Dictionary<string, object> Notes = new Dictionary<string, object>();

    public static int Main(string[] args) {
        // the tests check English texts: programs started by them must not follow the language of Windows
        Environment.SetEnvironmentVariable("EQLMAKER_LANG", "en");
        string filter = null, jsonFile = null; bool list = false;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--json" && i + 1 < args.Length) jsonFile = args[++i];
            else if (args[i] == "--list") list = true;
            else filter = args[i];
        }
        var tests = new List<MethodInfo>();
        foreach (var t in Assembly.GetExecutingAssembly().GetTypes()) {
            if (!t.Name.EndsWith("Tests") || !t.IsClass) continue;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)) if (m.ReturnType == typeof(void) && m.GetParameters().Length == 0) tests.Add(m);
        }
        tests.Sort(delegate (MethodInfo a, MethodInfo b) { return string.CompareOrdinal(a.DeclaringType.Name + "." + a.Name, b.DeclaringType.Name + "." + b.Name); });
        int pass = 0, fail = 0, skip = 0; var results = new List<object>();
        var total = Stopwatch.StartNew();
        foreach (var m in tests) {
            string name = m.DeclaringType.Name + "." + m.Name;
            if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (list) { Console.WriteLine(name); continue; }
            Current = name; var sw = Stopwatch.StartNew(); string status = "pass", info = null;
            try { m.Invoke(null, null); }
            catch (TargetInvocationException e) {
                var inner = e.InnerException;
                if (inner is SkipException) { status = "skip"; info = inner.Message; }
                else { status = "fail"; info = inner.Message + "\n" + inner.StackTrace; }
            }
            if (status == "pass") { pass++; Console.WriteLine("ok    " + name + "  (" + sw.ElapsedMilliseconds + " ms)"); }
            else if (status == "skip") { skip++; Console.WriteLine("skip  " + name + "  (" + info + ")"); }
            else { fail++; Console.WriteLine("FAIL  " + name + "\n      " + info.Replace("\n", "\n      ")); }
            results.Add(new JO("test", name, "status", status, "ms", sw.ElapsedMilliseconds, "info", status == "fail" ? info.Split('\n')[0] : info));
        }
        if (list) return 0;
        Console.WriteLine(pass + " passed, " + fail + " failed, " + skip + " skipped (" + (total.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s)");
        if (jsonFile != null) {
            var notes = new JO(); var keys = new List<string>(Notes.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) notes[k] = Notes[k];
            Json.WriteFile(jsonFile, new JO("passed", pass, "failed", fail, "skipped", skip, "results", results, "numbers", notes));
        }
        try { if (Directory.Exists(T.WorkRoot) && fail == 0) Directory.Delete(T.WorkRoot, true); } catch { }
        return fail > 0 ? 1 : 0;
    }
}
}
