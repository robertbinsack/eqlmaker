// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// All database access of the desktop variant, on one connection for the life of the program.
// Statements with values always use parameters.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using EqlMaker;

namespace SampleDesktop {

public class LineEntry {
    public string Sku, Name, UnitPrice, TaxClass;
    public int Quantity;
}

public class SaveResult {
    public bool Ok;
    public string Message, Id, Number;
}

public class Db : IDisposable {
    readonly PgClient pg;

    public Db(PgConnInfo info) { pg = new PgClient(info); }
    public void Dispose() { pg.Dispose(); }

    public static bool IsId(string text) { return Regex.IsMatch(text ?? "", "^[0-9]{1,9}$"); }

    // ---------------------------------------------------------------- customers
    public List<string[]> Customers() {
        return pg.Exec("SELECT id, name, city, customer_group, credit_limit FROM customer ORDER BY id").Rows;
    }
    public string[] Customer(string id) {
        var r = pg.Query("SELECT id, name, city, customer_group, credit_limit FROM customer WHERE id = $1", id);
        return r.Rows.Count > 0 ? r.Rows[0] : null;
    }
    public string InsertCustomer(string name, string city, string group, string creditLimit) {
        return pg.Query("INSERT INTO customer (name, city, customer_group, credit_limit) VALUES ($1, $2, $3, $4) RETURNING id", name, city, group, creditLimit).Scalar;
    }
    public void UpdateCustomer(string id, string name, string city, string group, string creditLimit) {
        pg.Query("UPDATE customer SET name = $1, city = $2, customer_group = $3, credit_limit = $4 WHERE id = $5", name, city, group, creditLimit, id);
    }

    // ---------------------------------------------------------------- articles
    public List<string[]> Articles() {
        return pg.Exec("SELECT id, sku, name, unit_price, stock, tax_class FROM article ORDER BY id").Rows;
    }
    public string[] Article(string id) {
        var r = pg.Query("SELECT id, sku, name, unit_price, stock, tax_class FROM article WHERE id = $1", id);
        return r.Rows.Count > 0 ? r.Rows[0] : null;
    }
    public void UpdateArticle(string id, string name, string unitPrice, string stock) {
        pg.Query("UPDATE article SET name = $1, unit_price = $2, stock = $3 WHERE id = $4", name, unitPrice, stock, id);
    }
    /// <summary>Article of a SKU with its current price, or null.</summary>
    public LineEntry ArticleBySku(string sku) {
        var r = pg.Query("SELECT id, sku, name, unit_price, tax_class FROM article WHERE sku = $1", sku);
        if (r.Rows.Count == 0) return null;
        var e = new LineEntry();
        e.Sku = r.Rows[0][1]; e.Name = r.Rows[0][2]; e.UnitPrice = r.Rows[0][3]; e.TaxClass = r.Rows[0][4];
        return e;
    }

    // ---------------------------------------------------------------- invoices
    public List<string[]> Invoices() {
        return pg.Exec("SELECT i.id, i.number, c.name AS customer, to_char(i.invoice_date, 'YYYY-MM-DD') AS invoice_date, " +
            "i.net, i.discount, i.tax, i.gross, i.status FROM invoice i JOIN customer c ON c.id = i.customer_id ORDER BY i.id").Rows;
    }

    /// <summary>Group of a customer; an unknown customer counts as group C.</summary>
    public string CustomerGroup(string customerNo) {
        if (!IsId(customerNo)) return "C";
        string g = pg.Query("SELECT customer_group FROM customer WHERE id = $1", customerNo).Scalar;
        return g ?? "C";
    }

    /// <summary>Saves an invoice in one transaction; on any refusal nothing is written.</summary>
    public SaveResult SaveInvoice(string customerNo, IList<LineEntry> lines) {
        var result = new SaveResult();
        if (!IsId(customerNo)) { result.Message = "Unknown customer"; return result; }
        if (lines.Count == 0) { result.Message = "No lines"; return result; }
        pg.Exec("BEGIN");
        try {
            var customer = pg.Query("SELECT id, customer_group, credit_limit FROM customer WHERE id = $1 FOR UPDATE", customerNo);
            if (customer.Rows.Count == 0) { pg.Exec("ROLLBACK"); result.Message = "Unknown customer"; return result; }
            var articleIds = new string[lines.Count];
            var calcLines = new List<InvoiceLine>();
            for (int i = 0; i < lines.Count; i++) {
                var article = pg.Query("SELECT id, unit_price, tax_class FROM article WHERE sku = $1 FOR UPDATE", lines[i].Sku);
                if (article.Rows.Count == 0) { pg.Exec("ROLLBACK"); result.Message = "Unknown SKU"; return result; }
                articleIds[i] = article.Rows[0][0];
                var l = new InvoiceLine();
                l.Quantity = lines[i].Quantity; l.UnitPrice = article.Rows[0][1]; l.TaxClass = article.Rows[0][2];
                calcLines.Add(l);
            }
            InvoiceTotals totals = InvoiceCalc.Calculate(customer.Rows[0][1], calcLines);

            string open = pg.Query("SELECT coalesce(sum(gross), 0) AS total FROM invoice WHERE customer_id = $1 AND status = 'open'", customerNo).Scalar;
            if (InvoiceCalc.ParseCents(open) + InvoiceCalc.ParseCents(totals.Gross) > InvoiceCalc.ParseCents(customer.Rows[0][2])) {
                pg.Exec("ROLLBACK"); result.Message = "Credit limit exceeded"; return result;
            }

            var counter = pg.Exec("UPDATE counter SET value = value + 1 WHERE name = 'invoice' RETURNING value, to_char(current_date, 'YYYY') AS year");
            string number = "INV-" + counter.Rows[0][1] + "-" + counter.Rows[0][0].PadLeft(5, '0');
            string invoiceId = pg.Query(
                "INSERT INTO invoice (number, customer_id, invoice_date, net, discount_percent, discount, tax, gross, status) " +
                "VALUES ($1, $2, current_date, $3, $4, $5, $6, $7, 'open') RETURNING id",
                number, customerNo, totals.Net, totals.DiscountPercent, totals.Discount, totals.Tax, totals.Gross).Scalar;
            for (int i = 0; i < lines.Count; i++) {
                string quantity = lines[i].Quantity.ToString(CultureInfo.InvariantCulture);
                pg.Query("INSERT INTO invoice_line (invoice_id, article_id, quantity, unit_price, line_net) VALUES ($1, $2, $3, $4, $5)",
                    invoiceId, articleIds[i], quantity, calcLines[i].UnitPrice, totals.LineNet[i]);
                pg.Query("UPDATE article SET stock = stock - $1 WHERE id = $2", quantity, articleIds[i]);
                pg.Query("INSERT INTO stock_movement (article_id, quantity, reason, invoice_id) VALUES ($1, $2, 'invoice', $3)",
                    articleIds[i], "-" + quantity, invoiceId);
            }
            pg.Exec("COMMIT");
            result.Ok = true; result.Id = invoiceId; result.Number = number;
            return result;
        } catch {
            try { pg.Exec("ROLLBACK"); } catch { }
            throw;
        }
    }

    /// <summary>Sets an invoice to paid; returns its number, or null when there is no such invoice.</summary>
    public string MarkPaid(string id) {
        var r = pg.Query("UPDATE invoice SET status = 'paid' WHERE id = $1 RETURNING id, number", id);
        return r.Rows.Count > 0 ? r.Rows[0][1] : null;
    }
}
}
