// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Command line arguments: positional values and --key value / --key=value / --flag.
using System;
using System.Collections.Generic;

namespace EqlMaker {

public class Args {
    public List<string> Pos = new List<string>(); public JO Opt = new JO();
    public static Args Parse(string[] a, int from) {
        var r = new Args();
        for (int i = from; i < a.Length; i++) {
            if (a[i] == "--") { for (i++; i < a.Length; i++) r.Pos.Add(a[i]); break; }
            if (a[i].StartsWith("--")) {
                string k = a[i].Substring(2); int eq = k.IndexOf('=');
                if (eq > 0) { r.Opt[k.Substring(0, eq)] = k.Substring(eq + 1); continue; }
                if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) r.Opt[k] = a[++i]; else r.Opt[k] = "true";
            } else r.Pos.Add(a[i]);
        }
        return r;
    }
    public string P(int i) { return i < Pos.Count ? Pos[i] : null; }
    public string Need(int i, string what) { if (i >= Pos.Count) throw new Exception(Texts.M("msg.missing_argument", what)); return Pos[i]; }
}

}
