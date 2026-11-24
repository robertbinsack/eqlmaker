// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Sample Shop, desktop variant of the sample application.
//   sample-desktop.exe --connection "host=127.0.0.1 port=5432 user=sample_app dbname=eqlmaker_sample"
//   sample-desktop.exe --selftest <path of test-vectors.json>
// The password comes from EQLMAKER_PGPASSWORD, PGPASSWORD or the password file of PostgreSQL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using EqlMaker;

namespace SampleDesktop {

public static class Program {
    const string DefaultConnection = "host=127.0.0.1 port=5432 user=sample_app dbname=eqlmaker_sample";

    [STAThread]
    public static int Main(string[] args) {
        string connection = DefaultConnection, vectors = null;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--connection" && i + 1 < args.Length) connection = args[++i];
            else if (args[i] == "--selftest" && i + 1 < args.Length) vectors = args[++i];
            else return 2;
        }
        if (vectors != null) return SelfTest(vectors);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // errors of event handlers go to the status line, never to a message box
        MainForm main = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e) { if (main != null) main.ShowError(e.Exception); };
        Db db;
        try {
            db = new Db(PgConnInfo.Parse(connection).WithApplication("sample-desktop"));
        } catch (Exception e) {
            Application.Run(new ErrorForm("No connection to the database: " + e.Message));
            return 3;
        }
        using (db) {
            main = new MainForm(db);
            Application.Run(main);
        }
        return 0;
    }

    /// <summary>Runs the calculation against the test vectors and writes selftest-result.json next to them.</summary>
    static int SelfTest(string vectorsPath) {
        int passed = 0, failed = 0;
        var failures = new List<object>();
        string resultPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(vectorsPath)), "selftest-result.json");
        try {
            foreach (object item in Json.ReadFile(vectorsPath).Arr("vectors")) {
                var vector = (JO)item;
                var lines = new List<InvoiceLine>();
                foreach (object l in vector.Arr("lines")) {
                    var line = new InvoiceLine();
                    line.Quantity = ((JO)l).Int("quantity", 0); line.UnitPrice = ((JO)l).Str("unit_price"); line.TaxClass = ((JO)l).Str("tax_class");
                    lines.Add(line);
                }
                JO expected = vector.Obj("expected");
                string difference;
                try {
                    InvoiceTotals t = InvoiceCalc.Calculate(vector.Str("group"), lines);
                    difference = Compare("line_net", string.Join(",", expected.Strings("line_net").ToArray()), string.Join(",", t.LineNet))
                        ?? Compare("net", expected.Str("net"), t.Net) ?? Compare("discount_percent", expected.Str("discount_percent"), t.DiscountPercent)
                        ?? Compare("discount", expected.Str("discount"), t.Discount) ?? Compare("tax", expected.Str("tax"), t.Tax)
                        ?? Compare("gross", expected.Str("gross"), t.Gross);
                } catch (Exception e) { difference = e.Message; }
                if (difference == null) passed++;
                else { failed++; failures.Add(vector.Str("name") + ": " + difference); }
            }
        } catch (Exception e) {
            failed++;
            failures.Add("test vectors not readable: " + e.Message);
        }
        try { Json.WriteFile(resultPath, new JO("passed", passed, "failed", failed, "failures", failures)); } catch { return 1; }
        return failed == 0 && passed > 0 ? 0 : 1;
    }

    static string Compare(string what, string expected, string actual) {
        return expected == actual ? null : what + " expected " + expected + " but was " + actual;
    }
}

/// <summary>Shown instead of the main window when the database cannot be reached.</summary>
public class ErrorForm : Form {
    public ErrorForm(string message) {
        Ui.Prepare(this, "Sample Shop - connection error", 560, 110, 40, 40);
        Ui.Label(this, message, 12, 12, 536, 48);
        CancelButton = Ui.Button(this, "Close", 458, 72, 90, 0, delegate { Close(); });
    }
}
}
