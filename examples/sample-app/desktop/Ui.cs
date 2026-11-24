// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Shared pieces of the windows: fixed pixel layout, standard controls only, input checks, and
// the list with its "Selected id" box.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using EqlMaker;

namespace SampleDesktop {

public static class Ui {
    public static readonly Font Font = new Font("Tahoma", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Fixed size, no scaling, fixed place on the screen.</summary>
    public static void Prepare(Form form, string title, int width, int height, int x, int y) {
        form.AutoScaleMode = AutoScaleMode.None;
        form.Font = Font;
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowIcon = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(x, y);
        form.ClientSize = new Size(width, height);
    }

    public static Label Label(Control parent, string text, int x, int y, int w, int h) {
        var l = new Label();
        l.Text = text; l.Bounds = new Rectangle(x, y, w, h); l.AutoSize = false; l.TextAlign = ContentAlignment.MiddleLeft;
        parent.Controls.Add(l);
        return l;
    }

    public static TextBox Box(Control parent, int x, int y, int w, int tab) {
        var t = new TextBox();
        t.Bounds = new Rectangle(x, y, w, 21); t.TabIndex = tab;
        parent.Controls.Add(t);
        return t;
    }

    public static Button Button(Control parent, string text, int x, int y, int w, int tab, EventHandler click) {
        var b = new Button();
        // the system style reacts to posted mouse messages even when the window is not visible
        b.FlatStyle = FlatStyle.System;
        b.Text = text; b.Bounds = new Rectangle(x, y, w, 26); b.TabIndex = tab;
        b.Click += click;
        parent.Controls.Add(b);
        return b;
    }

    public static ListView List(Control parent, int x, int y, int w, int h, int tab, string[] titles, int[] widths, bool[] right) {
        var v = new ListView();
        v.View = View.Details; v.FullRowSelect = true; v.HideSelection = false; v.MultiSelect = false; v.GridLines = true;
        v.HeaderStyle = ColumnHeaderStyle.Nonclickable; v.LabelEdit = false; v.AllowColumnReorder = false;
        v.Bounds = new Rectangle(x, y, w, h); v.TabIndex = tab;
        for (int i = 0; i < titles.Length; i++) v.Columns.Add(titles[i], widths[i], right[i] && i > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        parent.Controls.Add(v);
        return v;
    }

    public static void Fill(ListView list, List<string[]> rows) {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var row in rows) {
            var item = new ListViewItem(row[0] ?? "");
            for (int i = 1; i < row.Length; i++) item.SubItems.Add(row[i] ?? "");
            list.Items.Add(item);
        }
        list.EndUpdate();
    }

    // ---------------------------------------------------------------- input checks (same texts as the browser variant)
    public static string Required(string label, string text) {
        return text.Trim().Length == 0 ? label + " is required" : null;
    }

    public static string Number(string label, string text, bool notNegative) {
        string t = text.Trim();
        if (t.Length == 0) return label + " is required";
        if (!Regex.IsMatch(t, "^-?[0-9]{1,18}([.][0-9]{1,10})?$")) return label + " must be a number";
        if (notNegative && t.StartsWith("-") && t.Trim('-', '0', '.').Length > 0) return label + " must be at least 0";
        return null;
    }

    public static string Integer(string label, string text) {
        string t = text.Trim();
        if (t.Length == 0) return label + " is required";
        if (!Regex.IsMatch(t, "^-?[0-9]{1,18}$")) return label + " must be a whole number";
        return null;
    }

    /// <summary>Text for the status line when the database refused something.</summary>
    public static string DatabaseMessage(Exception e) {
        var pg = e as PgException;
        if (pg != null && pg.Code != null && (pg.Code.StartsWith("22") || pg.Code.StartsWith("23"))) return "The database did not accept the values";
        return "Database error: " + e.Message;
    }
}

/// <summary>A list in details view and the "Selected id" box that follows it and steers it.</summary>
public class IdList {
    public readonly ListView List;
    public readonly TextBox SelectedBox;
    bool syncing;

    public IdList(ListView list, TextBox selectedBox) {
        List = list; SelectedBox = selectedBox;
        List.SelectedIndexChanged += delegate {
            if (syncing) return;
            syncing = true;
            SelectedBox.Text = List.SelectedItems.Count > 0 ? List.SelectedItems[0].Text : "";
            syncing = false;
        };
        SelectedBox.TextChanged += delegate {
            if (syncing) return;
            syncing = true;
            SelectRow(SelectedBox.Text.Trim());
            syncing = false;
        };
    }

    void SelectRow(string id) {
        foreach (ListViewItem item in List.Items) {
            bool on = item.Text == id;
            if (item.Selected != on) item.Selected = on;
            if (on) item.EnsureVisible();
        }
    }

    /// <summary>Id of the selected row, or null.</summary>
    public string SelectedId { get { return List.SelectedItems.Count > 0 ? List.SelectedItems[0].Text : null; } }

    public void Select(string id) {
        syncing = true;
        SelectRow(id ?? "");
        SelectedBox.Text = SelectedId ?? "";
        syncing = false;
    }

    /// <summary>New content; the selection stays on the same id when it still exists.</summary>
    public void SetRows(List<string[]> rows) {
        string keep = SelectedId;
        syncing = true;
        Ui.Fill(List, rows);
        syncing = false;
        Select(keep);
    }
}
}
