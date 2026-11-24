// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Main window "Sample Shop": three tab pages with a list, buttons and the "Selected id" box,
// and the status line at the bottom. Every position is a fixed pixel value.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SampleDesktop {

public class MainForm : Form {
    readonly Db db;
    readonly Label status;
    readonly IdList customers, articles, invoices;

    public MainForm(Db db) {
        this.db = db;
        Ui.Prepare(this, "Sample Shop", 900, 600, 40, 40);
        MinimizeBox = true;

        var tabs = new TabControl();
        tabs.Bounds = new Rectangle(8, 8, 884, 556);
        tabs.TabIndex = 0;
        Controls.Add(tabs);

        var page = Page(tabs, "Customers");
        customers = Lists(page, new[] { "Id", "Name", "City", "Group", "Credit limit" }, new[] { 50, 300, 220, 80, 120 }, new[] { false, false, false, false, true });
        Ui.Button(page, "New customer", 8, 490, 120, 1, delegate { Run(NewCustomer); });
        Ui.Button(page, "Edit customer", 136, 490, 120, 2, delegate { Run(EditCustomer); });
        Ui.Button(page, "Refresh customers", 264, 490, 120, 3, delegate { Run(delegate { LoadCustomers(); status.Text = "Customers refreshed"; }); });

        page = Page(tabs, "Articles");
        articles = Lists(page, new[] { "Id", "SKU", "Name", "Price", "Stock", "Tax class" }, new[] { 50, 100, 300, 100, 80, 80 }, new[] { false, false, false, true, true, false });
        Ui.Button(page, "Edit article", 8, 490, 120, 1, delegate { Run(EditArticle); });
        Ui.Button(page, "Refresh articles", 136, 490, 120, 2, delegate { Run(delegate { LoadArticles(); status.Text = "Articles refreshed"; }); });

        page = Page(tabs, "Invoices");
        invoices = Lists(page, new[] { "Id", "Number", "Customer", "Date", "Net", "Discount", "Tax", "Gross", "Status" },
            new[] { 50, 120, 200, 90, 80, 80, 80, 80, 60 }, new[] { false, false, false, false, true, true, true, true, false });
        Ui.Button(page, "New invoice", 8, 490, 120, 1, delegate { Run(NewInvoice); });
        Ui.Button(page, "Mark paid", 136, 490, 120, 2, delegate { Run(MarkPaid); });
        Ui.Button(page, "Refresh invoices", 264, 490, 120, 3, delegate { Run(delegate { LoadInvoices(); status.Text = "Invoices refreshed"; }); });

        status = Ui.Label(this, "Ready", 10, 572, 880, 21);
        Run(delegate { LoadCustomers(); LoadArticles(); LoadInvoices(); });
    }

    static TabPage Page(TabControl tabs, string title) {
        var page = new TabPage(title);
        page.UseVisualStyleBackColor = true;
        tabs.TabPages.Add(page);
        return page;
    }

    static IdList Lists(TabPage page, string[] titles, int[] widths, bool[] right) {
        var list = Ui.List(page, 8, 8, 860, 472, 0, titles, widths, right);
        Ui.Label(page, "Selected id", 680, 492, 70, 21);
        var box = Ui.Box(page, 754, 492, 114, 4);
        return new IdList(list, box);
    }

    /// <summary>Runs an action; a database error ends in the status line, never in a message box.</summary>
    void Run(Action action) {
        try { action(); } catch (Exception e) { status.Text = Ui.DatabaseMessage(e); }
    }

    public void ShowError(Exception e) { status.Text = Ui.DatabaseMessage(e); }

    void LoadCustomers() { customers.SetRows(db.Customers()); }
    void LoadArticles() { articles.SetRows(db.Articles()); }
    void LoadInvoices() { invoices.SetRows(db.Invoices()); }

    void NewCustomer() {
        using (var dialog = new CustomerDialog(db, null)) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LoadCustomers();
            customers.Select(dialog.SavedId);
            status.Text = "Customer " + dialog.SavedId + " saved";
        }
    }

    void EditCustomer() {
        string id = customers.SelectedId;
        string[] row = id == null ? null : db.Customer(id);
        if (row == null) { status.Text = "No customer selected"; return; }
        using (var dialog = new CustomerDialog(db, row)) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LoadCustomers();
            customers.Select(id);
            status.Text = "Customer " + id + " saved";
        }
    }

    void EditArticle() {
        string id = articles.SelectedId;
        string[] row = id == null ? null : db.Article(id);
        if (row == null) { status.Text = "No article selected"; return; }
        using (var dialog = new ArticleDialog(db, row)) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LoadArticles();
            articles.Select(id);
            status.Text = "Article " + id + " saved";
        }
    }

    void NewInvoice() {
        using (var dialog = new InvoiceDialog(db)) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LoadInvoices();
            LoadArticles();
            invoices.Select(dialog.SavedId);
            status.Text = "Invoice " + dialog.SavedNumber + " saved";
        }
    }

    void MarkPaid() {
        string id = invoices.SelectedId;
        if (id == null) { status.Text = "No invoice selected"; return; }
        string number = db.MarkPaid(id);
        if (number == null) { status.Text = "Unknown invoice"; return; }
        LoadInvoices();
        invoices.Select(id);
        status.Text = "Invoice " + number + " marked paid";
    }
}
}
