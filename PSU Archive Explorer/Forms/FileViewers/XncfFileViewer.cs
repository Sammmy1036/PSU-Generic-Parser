using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PSULib.FileClasses.Models;

namespace psu_archive_explorer.FileViewers
{
    /// <summary>
    /// Editable viewer for XNCF files (fonts & glyphs).
    /// </summary>
    public partial class XncfFileViewer : UserControl
    {
        public XncfFile loadedFile;

        public XncfFileViewer(XncfFile xncf)
        {
            InitializeComponent();
            loadedFile = xncf;

            BuildTree();
            treeView1.AfterSelect += TreeView1_AfterSelect;

            if (treeView1.Nodes.Count > 0)
                treeView1.SelectedNode = treeView1.Nodes[0];
        }

        // ---- Tree population --------------------------------------------------

        private void BuildTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            var rootNode = new TreeNode(loadedFile.filename ?? "(file)");
            rootNode.Tag = loadedFile;

            foreach (var font in loadedFile.Fonts)
            {
                var fontNode = new TreeNode(font.ToString());
                fontNode.Tag = font;

                foreach (var glyph in font.Glyphs)
                {
                    var glyphNode = new TreeNode(glyph.ToString());
                    glyphNode.Tag = glyph;
                    fontNode.Nodes.Add(glyphNode);
                }
                rootNode.Nodes.Add(fontNode);
            }

            treeView1.Nodes.Add(rootNode);
            rootNode.Expand();
            if (rootNode.Nodes.Count > 0) rootNode.Nodes[0].Expand();
            treeView1.EndUpdate();
        }
        private void RefreshSelectedNodeText()
        {
            var node = treeView1.SelectedNode;
            if (node?.Tag is XncfFontGlyph glyph)
                node.Text = glyph.ToString();
            else if (node?.Tag is XncfFont font)
                node.Text = font.ToString();
        }

        // ---- Selection handling ----------------------------------------------

        private void TreeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var tag = e.Node?.Tag;
            switch (tag)
            {
                case XncfFile xncf:
                    ShowFileSummary(xncf);
                    break;
                case XncfFont font:
                    ShowFontSummary(font);
                    break;
                case XncfFontGlyph glyph:
                    ShowGlyphDetails(glyph);
                    break;
                default:
                    ClearGrid();
                    break;
            }
        }
        private bool suppressEditCallback;

        private void InitGridIfNeeded()
        {
            if (propertiesGrid.Columns.Count > 0) return;

            propertiesGrid.AutoGenerateColumns = false;
            propertiesGrid.AllowUserToAddRows = false;
            propertiesGrid.AllowUserToDeleteRows = false;
            propertiesGrid.RowHeadersVisible = false;
            propertiesGrid.ReadOnly = false;

            var propCol = new DataGridViewTextBoxColumn
            {
                Name = "Property",
                HeaderText = "Property",
                ReadOnly = true,
                Width = 130,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            };
            var valCol = new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Value",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            };
            propertiesGrid.Columns.Add(propCol);
            propertiesGrid.Columns.Add(valCol);

            propertiesGrid.CellValueChanged += PropertiesGrid_CellValueChanged;
            propertiesGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (propertiesGrid.IsCurrentCellDirty)
                    propertiesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            propertiesGrid.DataError += (s, e) =>
            {
                e.ThrowException = false;
            };
        }

        private void ClearGrid()
        {
            InitGridIfNeeded();
            suppressEditCallback = true;
            propertiesGrid.Rows.Clear();
            suppressEditCallback = false;
        }
        private void AddRow(string label, string value)
        {
            int idx = propertiesGrid.Rows.Add(label, value ?? "");
            propertiesGrid.Rows[idx].Cells[1].ReadOnly = true;
            propertiesGrid.Rows[idx].Cells[1].Style.ForeColor = SystemColors.GrayText;
        }
        private void AddEditableRow(string label, string value, Func<string, bool> commit)
        {
            int idx = propertiesGrid.Rows.Add(label, value ?? "");
            var row = propertiesGrid.Rows[idx];
            row.Cells[1].ReadOnly = false;
            row.Cells[1].Tag = new EditEntry { OriginalValue = value, Commit = commit };
        }

        private class EditEntry
        {
            public string OriginalValue;
            public Func<string, bool> Commit;
        }

        private void PropertiesGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (suppressEditCallback) return;
            if (e.ColumnIndex != 1 || e.RowIndex < 0) return;

            var cell = propertiesGrid.Rows[e.RowIndex].Cells[1];
            var entry = cell.Tag as EditEntry;
            if (entry == null) return;

            string newVal = cell.Value?.ToString() ?? "";
            bool ok = false;
            try { ok = entry.Commit(newVal); }
            catch { ok = false; }

            if (!ok)
            {
                suppressEditCallback = true;
                cell.Value = entry.OriginalValue;
                suppressEditCallback = false;
            }
            else
            {
                entry.OriginalValue = newVal;
                RefreshSelectedNodeText();
            }
        }

        private void ShowFileSummary(XncfFile f)
        {
            ClearGrid();
            AddRow("Filename", f.filename ?? "");
            AddRow("Fonts", f.Fonts.Count.ToString());
            int totalGlyphs = 0;
            foreach (var fnt in f.Fonts) totalGlyphs += fnt.Glyphs.Count;
            AddRow("Total glyphs", totalGlyphs.ToString());
            AddRow("File size", $"{f.RawBytes?.Length ?? 0} bytes");
            AddRow("Modified?", f.dirty ? "yes (export to save changes)" : "no");
            if (!string.IsNullOrEmpty(f.ParseError))
                AddRow("Parse error", f.ParseError);
            if (!string.IsNullOrEmpty(f.ParseInfo))
                AddRow("Parse info", f.ParseInfo);

            if (f.EditLog.Count > 0)
            {
                AddRow("--- Recent edits ---", $"{f.EditLog.Count} total");
                int shown = 0;
                foreach (var line in f.EditLog)
                {
                    AddRow($"edit #{f.EditLog.Count - shown}", line);
                    if (++shown >= 16) break;
                }
            }
        }

        private void ShowFontSummary(XncfFont font)
        {
            ClearGrid();
            AddRow("Name", font.Name ?? "");
            AddRow("Index", font.Index.ToString());
            AddRow("Glyph count", font.Glyphs.Count.ToString());
        }

        private void ShowGlyphDetails(XncfFontGlyph g)
        {
            ClearGrid();
            AddRow("Character", g.CharDisplay);

            // Edit the char code as hex so the user can enter codepoints
            AddEditableRow("Char code (hex)", "0x" + g.CharCode.ToString("X2"),
                v =>
                {
                    if (TryParseUInt(v, out var u)) { g.CharCode = u; return true; }
                    return false;
                });

            AddEditableRow("SubImage index", g.SubImageIndex.ToString(),
                v =>
                {
                    if (int.TryParse(v, out var i)) { g.SubImageIndex = i; return true; }
                    return false;
                });
        }

        // Accept "0xNN", "NN" (hex), or decimal
        private static bool TryParseUInt(string s, out uint result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result);
            if (uint.TryParse(s, out result)) return true;
            return uint.TryParse(s,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out result);
        }
    }
}