// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The calculation of an invoice: line amounts, discount by customer group, tax by tax class.
// Pure function on whole cents; the browser variant has the same function in JavaScript and
// both are tested against ..\test-vectors.json.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SampleDesktop {

public class InvoiceLine {
    public int Quantity;
    public string UnitPrice;
    public string TaxClass;
}

public class InvoiceTotals {
    public string[] LineNet;
    public string Net, DiscountPercent, Discount, Tax, Gross;
}

public static class InvoiceCalc {
    static readonly string[] TaxClasses = { "S", "R" };

    static int TaxRate(string taxClass) {
        if (taxClass == "S") return 19;
        if (taxClass == "R") return 7;
        throw new ArgumentException("unknown tax class: " + taxClass);
    }

    /// <summary>"12.5" becomes 1250.</summary>
    public static long ParseCents(string text) {
        string s = (text ?? "").Trim();
        int dot = s.IndexOf('.');
        string whole = dot < 0 ? s : s.Substring(0, dot);
        string fraction = dot < 0 ? "" : s.Substring(dot + 1);
        if (whole.Length == 0 || whole.Length > 12 || fraction.Length > 2 || (dot >= 0 && fraction.Length == 0)) throw new FormatException("not an amount: " + text);
        foreach (char c in whole + fraction) if (c < '0' || c > '9') throw new FormatException("not an amount: " + text);
        return long.Parse(whole, CultureInfo.InvariantCulture) * 100 + long.Parse((fraction + "00").Substring(0, 2), CultureInfo.InvariantCulture);
    }

    /// <summary>1250 becomes "12.50".</summary>
    public static string FormatCents(long cents) {
        string digits = cents.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        return digits.Substring(0, digits.Length - 2) + "." + digits.Substring(digits.Length - 2);
    }

    /// <summary>cents * percent / 100, half a cent rounds up.</summary>
    static long PercentOf(long cents, int percent) {
        return (cents * percent + 50) / 100;
    }

    static int DiscountPercent(string group, long net) {
        if (group == "A" && net >= 50000) return 5;
        if (group == "B" && net >= 100000) return 3;
        return 0;
    }

    /// <summary>Group "A", "B" or "C"; the unit prices are decimal texts.</summary>
    public static InvoiceTotals Calculate(string group, IList<InvoiceLine> lines) {
        var lineNet = new long[lines.Count];
        long net = 0, netStandard = 0, netReduced = 0;
        for (int i = 0; i < lines.Count; i++) {
            TaxRate(lines[i].TaxClass);
            lineNet[i] = lines[i].Quantity * ParseCents(lines[i].UnitPrice);
            if (lines[i].TaxClass == "S") netStandard += lineNet[i]; else netReduced += lineNet[i];
            net += lineNet[i];
        }
        int percent = DiscountPercent(group, net);
        long discount = PercentOf(net, percent);
        long tax = 0;
        foreach (string taxClass in TaxClasses) {
            long classNet = taxClass == "S" ? netStandard : netReduced;
            long share = classNet - PercentOf(classNet, percent);
            tax += PercentOf(share, TaxRate(taxClass));
        }
        var t = new InvoiceTotals();
        t.LineNet = new string[lineNet.Length];
        for (int i = 0; i < lineNet.Length; i++) t.LineNet[i] = FormatCents(lineNet[i]);
        t.Net = FormatCents(net);
        t.DiscountPercent = FormatCents(percent * 100);
        t.Discount = FormatCents(discount);
        t.Tax = FormatCents(tax);
        t.Gross = FormatCents(net - discount + tax);
        return t;
    }
}
}
