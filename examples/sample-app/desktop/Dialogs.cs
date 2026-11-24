// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// The three dialogs: "Customer", "Article" and "New invoice". Each is a modal window with its
// own title and its own status line; a dialog only closes after a successful save or on Cancel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SampleDesktop {

public class CustomerDialog : Form {
    readonly Db db;
    readonly string id;   // null for a new customer
    readonly TextBox name, city, group, creditLimit;
    readonly Label status;
    public string SavedId;

    public CustomerDialog(Db db, string[] row) {
        this.db = db;
        id = row == null ? null : row[0];
        Ui.Prepare(this, "Customer", 380, 210, 100, 100);
        Ui.Label(this, "Name", 12, 12, 90, 21);
        name = Ui.Box(this, 110, 12, 250, 0);
        Ui.Label(this, "City", 12, 42, 90, 21);
        city = Ui.Box(this, 110, 42, 250, 1);
        Ui.Label(this, "Group", 12, 72, 90, 21);
        group = Ui.Box(this, 110, 72, 40, 2);
        Ui.Label(this, "Credit limit", 12, 102, 90, 21);
        creditLimit = Ui.Box(this, 110, 102, 100, 3);
        status = Ui.Label(this, "", 12, 136, 356, 21);
        AcceptButton = Ui.Button(this, "Save customer", 154, 170, 110, 4, delegate { Save(); });
        CancelButton = Ui.Button(this, "Cancel", 270, 170, 90, 5, delegate { DialogResult = DialogResult.Cancel; Close(); });
        if (row != null) { name.Text = row[1]; city.Text = row[2]; group.Text = row[3]; creditLimit.Text = row[4]; }
    }

    void Save() {
        string g = group.Text.Trim();
        string error = Ui.Required("Name", name.Text) ?? Ui.Required("City", city.Text) ?? Ui.Required("Group", g)
            ?? (g == "A" || g == "B" || g == "C" ? null : "Group must be one of A, B, C")
            ?? Ui.Number("Credit limit", creditLimit.Text, true);
        if (error != null) { status.Text = error; return; }
        try {
            if (id == null) SavedId = db.InsertCustomer(name.Text.Trim(), city.Text.Trim(), g, creditLimit.Text.Trim());
            else { db.UpdateCustomer(id, name.Text.Trim(), city.Text.Trim(), g, creditLimit.Text.Trim()); SavedId = id; }
        } catch (Exception e) { status.Text = Ui.DatabaseMessage(e); return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}

public class ArticleDialog : Form {
    readonly Db db;
    readonly string id;
    readonly TextBox name, price, stock;
    readonly Label status;

    public ArticleDialog(Db db, string[] row) {
        this.db = db;
        id = row[0];
        Ui.Prepare(this, "Article", 380, 180, 100, 100);
        Ui.Label(this, "Name", 12, 12, 90, 21);
        name = Ui.Box(this, 110, 12, 250, 0);
        Ui.Label(this, "Price", 12, 42, 90, 21);
        price = Ui.Box(this, 110, 42, 100, 1);
        Ui.Label(this, "Stock", 12, 72, 90, 21);
        stock = Ui.Box(this, 110, 72, 100, 2);
        status = Ui.Label(this, "", 12, 106, 356, 21);
        AcceptButton = Ui.Button(this, "Save article", 154, 140, 110, 3, delegate { Save(); });
        CancelButton = Ui.Button(this, "Cancel", 270, 140, 90, 4, delegate { DialogResult = DialogResult.Cancel; Close(); });
        name.Text = row[2]; price.Text = row[3]; stock.Text = row[4];
    }

    void Save() {
        string error = Ui.Required("Name", name.Text) ?? Ui.Number("Price", price.Text, true) ?? Ui.Integer("Stock", stock.Text);
        if (error != null) { status.Text = error; return; }
        try { db.UpdateArticle(id, name.Text.Trim(), price.Text.Trim(), stock.Text.Trim()); }
        catch (Exception e) { status.Text = Ui.DatabaseMessage(e); return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}

public class InvoiceDialog : Form {
    readonly Db db;
    readonly List<LineEntry> lines = new List<LineEntry>();
    readonly TextBox customerNo, sku, quantity, net, discount, tax, gross;
    readonly ListView list;
    readonly Label status;
    public string SavedId, SavedNumber;

    public InvoiceDialog(Db db) {
        this.db = db;
        Ui.Prepare(this, "New invoice", 620, 470, 100, 100);
        Ui.Label(this, "Customer no.", 12, 12, 90, 21);
        customerNo = Ui.Box(this, 110, 12, 80, 0);
        list = Ui.List(this, 12, 44, 596, 220, 1,
            new[] { "SKU", "Article", "Quantity", "Price", "Line net" }, new[] { 90, 230, 70, 90, 100 }, new[] { false, false, true, true, true });
        Ui.Label(this, "SKU", 12, 276, 40, 21);
        sku = Ui.Box(this, 56, 276, 100, 2);
        Ui.Label(this, "Quantity", 172, 276, 60, 21);
        quantity = Ui.Box(this, 236, 276, 60, 3);
        Ui.Button(this, "Add line", 312, 273, 90, 4, delegate { AddLine(); });
        net = Total("Net", 310);
        discount = Total("Discount", 336);
        tax = Total("Tax", 362);
        gross = Total("Gross", 388);
        status = Ui.Label(this, "", 12, 436, 370, 21);
        Ui.Button(this, "Save invoice", 402, 434, 110, 5, delegate { Save(); });
        CancelButton = Ui.Button(this, "Cancel", 518, 434, 90, 6, delegate { DialogResult = DialogResult.Cancel; Close(); });
        customerNo.TextChanged += delegate { try { Recalculate(lines); } catch (Exception e) { status.Text = Ui.DatabaseMessage(e); } };
        ShowTotals(lines, InvoiceCalc.Calculate("C", new List<InvoiceLine>()));
    }

    TextBox Total(string label, int y) {
        Ui.Label(this, label, 400, y, 66, 21);
        var box = Ui.Box(this, 470, y, 138, 0);
        box.ReadOnly = true; box.TabStop = false; box.TextAlign = HorizontalAlignment.Right;
        return box;
    }

    /// <summary>Reads group and current prices, then shows lines and totals.</summary>
    void Recalculate(List<LineEntry> entries) {
        string group = db.CustomerGroup(customerNo.Text.Trim());
        var calc = new List<InvoiceLine>();
        foreach (var e in entries) {
            var now = db.ArticleBySku(e.Sku);
            if (now == null) throw new InvalidOperationException("Unknown SKU");
            e.Name = now.Name; e.UnitPrice = now.UnitPrice; e.TaxClass = now.TaxClass;
            var l = new InvoiceLine();
            l.Quantity = e.Quantity; l.UnitPrice = e.UnitPrice; l.TaxClass = e.TaxClass;
            calc.Add(l);
        }
        ShowTotals(entries, InvoiceCalc.Calculate(group, calc));
    }

    void ShowTotals(List<LineEntry> entries, InvoiceTotals totals) {
        var rows = new List<string[]>();
        for (int i = 0; i < entries.Count; i++) {
            rows.Add(new[] { entries[i].Sku, entries[i].Name, entries[i].Quantity.ToString(CultureInfo.InvariantCulture), entries[i].UnitPrice, totals.LineNet[i] });
        }
        Ui.Fill(list, rows);
        net.Text = totals.Net; discount.Text = totals.Discount; tax.Text = totals.Tax; gross.Text = totals.Gross;
    }

    void AddLine() {
        string q = quantity.Text.Trim();
        if (!Regex.IsMatch(q, "^[1-9][0-9]{0,4}$")) { status.Text = "Quantity must be a whole number greater than 0"; return; }
        try {
            var entry = sku.Text.Trim().Length == 0 ? null : db.ArticleBySku(sku.Text.Trim());
            if (entry == null) { status.Text = "Unknown SKU"; return; }
            entry.Quantity = int.Parse(q, CultureInfo.InvariantCulture);
            var next = new List<LineEntry>(lines);
            next.Add(entry);
            Recalculate(next);
            lines.Add(entry);
        } catch (Exception e) { status.Text = Ui.DatabaseMessage(e); return; }
        sku.Text = ""; quantity.Text = "";
        status.Text = "Line added";
    }

    void Save() {
        SaveResult result;
        try { result = db.SaveInvoice(customerNo.Text.Trim(), lines); }
        catch (Exception e) { status.Text = Ui.DatabaseMessage(e); return; }
        if (!result.Ok) { status.Text = result.Message; return; }
        SavedId = result.Id; SavedNumber = result.Number;
        DialogResult = DialogResult.OK;
        Close();
    }
}
}
