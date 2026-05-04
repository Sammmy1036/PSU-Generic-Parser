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
    public partial class XncpFileViewer : UserControl
    {
        public XncpFile loadedFile;
        private XncpScene currentScene;
        private readonly HashSet<TreeNode> multiSelected = new HashSet<TreeNode>();
        private static readonly Color multiSelectColor = Color.FromArgb(180, 215, 255);

        public XncpFileViewer(XncpFile xncp)
        {
            InitializeComponent();
            loadedFile = xncp;

            BuildTree();
            treeView1.AfterSelect += TreeView1_AfterSelect;
            treeView1.MouseDown += TreeView1_MouseDown;

            // Auto select the first scene
            if (treeView1.Nodes.Count > 0 && treeView1.Nodes[0].Nodes.Count > 0)
                treeView1.SelectedNode = treeView1.Nodes[0].Nodes[0];
        }

        // ---- Tree multi-select ------------------------------------------------
        private void TreeView1_MouseDown(object sender, MouseEventArgs e)
        {
            var node = treeView1.GetNodeAt(e.X, e.Y);

            if (e.Button == MouseButtons.Left)
            {
                // Ctrl+click toggles multi-selection on cast nodes.
                if ((Control.ModifierKeys & Keys.Control) != 0
                    && node?.Tag is XncpCast)
                {
                    if (multiSelected.Contains(node))
                    {
                        multiSelected.Remove(node);
                        node.BackColor = treeView1.BackColor;
                    }
                    else
                    {
                        multiSelected.Add(node);
                        node.BackColor = multiSelectColor;
                    }
                }
                else
                {
                    ClearMultiSelection();
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (node == null) return;
                if (node.Tag is XncpCast || multiSelected.Count > 0)
                {
                    ShowCastContextMenu(node, e.Location);
                }
            }
        }

        private void ClearMultiSelection()
        {
            foreach (var n in multiSelected) n.BackColor = treeView1.BackColor;
            multiSelected.Clear();
        }

        private void ShowCastContextMenu(TreeNode clickedNode, System.Drawing.Point screenSpot)
        {
            var targets = new List<XncpCast>();
            if (multiSelected.Count > 0)
            {
                foreach (var n in multiSelected)
                    if (n.Tag is XncpCast c) targets.Add(c);
            }
            else if (clickedNode.Tag is XncpCast c1)
            {
                targets.Add(c1);
            }
            if (targets.Count == 0) return;

            var menu = new ContextMenuStrip();
            var disable = menu.Items.Add($"Disable {targets.Count} cast(s)");
            disable.Click += (s, ev) =>
            {
                foreach (var c in targets) c.IsEnabled = false;
                BuildTree();
            };
            var enable = menu.Items.Add($"Enable {targets.Count} cast(s)");
            enable.Click += (s, ev) =>
            {
                foreach (var c in targets) c.IsEnabled = true;
                BuildTree();
            };
            menu.Show(treeView1, screenSpot);
        }

        // ---- Tree population --------------------------------------------------

        private void BuildTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            var rootNode = new TreeNode(string.IsNullOrEmpty(loadedFile.ProjectName)
                ? "(project)"
                : loadedFile.ProjectName);
            rootNode.Tag = loadedFile;

            foreach (var scene in loadedFile.Scenes)
            {
                var sceneNode = new TreeNode($"{scene.Name}  [z={scene.ZIndex}, {scene.TotalCastCount} casts]");
                sceneNode.Tag = scene;

                foreach (var group in scene.Groups)
                {
                    var groupNode = new TreeNode($"group {group.Index}  ({group.Casts.Count} casts)");
                    groupNode.Tag = group;

                    foreach (var cast in group.Casts)
                    {
                        var castNode = new TreeNode(
                            $"{cast} [{cast.DrawType}]{(cast.IsEnabled ? "" : " (disabled)")}");
                        castNode.Tag = cast;
                        groupNode.Nodes.Add(castNode);
                    }
                    sceneNode.Nodes.Add(groupNode);
                }

                if (scene.Animations.Count > 0)
                {
                    var animsNode = new TreeNode($"Animations ({scene.Animations.Count})");
                    animsNode.Tag = scene.Animations;
                    foreach (var anim in scene.Animations)
                    {
                        var n = new TreeNode(anim.ToString()) { Tag = anim };
                        animsNode.Nodes.Add(n);
                    }
                    sceneNode.Nodes.Add(animsNode);
                }

                rootNode.Nodes.Add(sceneNode);
            }

            // Fonts
            if (loadedFile.Fonts.Count > 0)
            {
                var fontsNode = new TreeNode($"Fonts ({loadedFile.Fonts.Count})");
                fontsNode.Tag = loadedFile.Fonts;
                foreach (var f in loadedFile.Fonts)
                    fontsNode.Nodes.Add(new TreeNode(f.ToString()) { Tag = f });
                rootNode.Nodes.Add(fontsNode);
            }

            treeView1.Nodes.Add(rootNode);
            rootNode.Expand();
            if (rootNode.Nodes.Count > 0) rootNode.Nodes[0].Expand();
            treeView1.EndUpdate();
        }

        // ---- Selection handling ----------------------------------------------

        private void TreeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var tag = e.Node?.Tag;

            currentScene = FindOwningScene(e.Node);

            switch (tag)
            {
                case XncpFile xncp:
                    ShowFileSummary(xncp);
                    break;
                case XncpScene scene:
                    ShowSceneSummary(scene);
                    break;
                case XncpCastGroup group:
                    ShowGroupSummary(group);
                    break;
                case XncpCast cast:
                    ShowCastDetails(cast);
                    break;
                case XncpAnimation anim:
                    ShowAnimDetails(anim);
                    break;
                case XncpFontInfo font:
                    ShowFontDetails(font);
                    break;
                default:
                    ClearGrid();
                    break;
            }
        }

        private XncpScene FindOwningScene(TreeNode node)
        {
            while (node != null)
            {
                if (node.Tag is XncpScene s) return s;
                node = node.Parent;
            }
            return null;
        }

        // ---- Property grid populators ----------------------------------------

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
            propertiesGrid.CellMouseDown += PropertiesGrid_CellMouseDown;
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
        private void AddEditableBoolRow(string label, bool value, Action<bool> commit)
        {
            int idx = propertiesGrid.Rows.Add();
            var row = propertiesGrid.Rows[idx];
            row.Cells[0].Value = label;
            row.Cells[0].ReadOnly = true;

            var checkCell = new DataGridViewCheckBoxCell { Value = value };
            row.Cells[1] = checkCell;
            row.Cells[1].Tag = new EditEntry
            {
                OriginalValue = value.ToString(),
                Commit = s => { commit(s == "True"); return true; }
            };
        }
        private void AddEditableEnumRow(string label, string current, string[] options,
                                        Action<string> commit)
        {
            int idx = propertiesGrid.Rows.Add();
            var row = propertiesGrid.Rows[idx];
            row.Cells[0].Value = label;
            row.Cells[0].ReadOnly = true;

            var combo = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
            foreach (var o in options) combo.Items.Add(o);
            combo.Value = current;
            row.Cells[1] = combo;
            row.Cells[1].Tag = new EditEntry
            {
                OriginalValue = current,
                Commit = s => { commit(s); return true; }
            };
        }
        private void AddColorRow(string label, uint engineColor, Action<uint> commit)
        {
            int idx = propertiesGrid.Rows.Add();
            var row = propertiesGrid.Rows[idx];
            row.Cells[0].Value = label;
            row.Cells[0].ReadOnly = true;
            var valCell = row.Cells[1];
            valCell.ReadOnly = true;
            UpdateColorCell(valCell, engineColor);

            valCell.Tag = new ColorEditEntry
            {
                EngineValue = engineColor,
                Commit = newVal =>
                {
                    commit(newVal);
                    UpdateColorCell(valCell, newVal);
                }
            };
        }

        private static void UpdateColorCell(DataGridViewCell cell, uint engineColor)
        {
            var visual = EngineColorToDrawing(engineColor);

            byte alpha = visual.A;
            float a = alpha / 255f;
            int displayR = (int)(visual.R * a + 128 * (1 - a));
            int displayG = (int)(visual.G * a + 128 * (1 - a));
            int displayB = (int)(visual.B * a + 128 * (1 - a));
            var displayBg = Color.FromArgb(255, displayR, displayG, displayB);

            cell.Style.BackColor = displayBg;
            cell.Style.SelectionBackColor = displayBg;
            int luma = (displayR * 299 + displayG * 587 + displayB * 114) / 1000;
            var textColor = luma > 140 ? Color.Black : Color.White;
            cell.Style.ForeColor = textColor;
            cell.Style.SelectionForeColor = textColor;

            cell.Value = $"#{visual.R:X2}{visual.G:X2}{visual.B:X2}  α={visual.A}";
        }

        private static Color EngineColorToDrawing(uint engineColor)
        {
            // Little-endian read: byte0 is bits 0..7 of the uint.
            byte b0 = (byte)(engineColor & 0xFF);          // A
            byte b1 = (byte)((engineColor >> 8) & 0xFF);   // B
            byte b2 = (byte)((engineColor >> 16) & 0xFF);  // G
            byte b3 = (byte)((engineColor >> 24) & 0xFF);  // R
            return Color.FromArgb(b0, b3, b2, b1);
        }

        /// <summary>Inverse of <see cref="EngineColorToDrawing"/>.</summary>
        private static uint DrawingToEngineColor(Color visual)
        {
            // Pack bytes as A B G R → little-endian uint32.
            return (uint)visual.A
                 | ((uint)visual.B << 8)
                 | ((uint)visual.G << 16)
                 | ((uint)visual.R << 24);
        }

        private class ColorEditEntry
        {
            public uint EngineValue;
            public Action<uint> Commit;
        }

        private class EditEntry
        {
            public string OriginalValue;
            public Func<string, bool> Commit;
        }

        // Static reference to the most recently copied engine color (raw u32)
        // so the "Paste" menu item can transfer values from one color cell to
        // another without leaving the cast.
        private static uint? _clipboardColor;

        /// <summary>
        /// Mouse-down on a properties-grid cell. Replaces the older CellClick
        /// handler so we can distinguish left-button (open color picker) from
        /// right-button (show context menu with Copy / Paste / Enter Hex).
        /// </summary>
        private void PropertiesGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex != 1 || e.RowIndex < 0) return;
            var cell = propertiesGrid.Rows[e.RowIndex].Cells[1];
            var entry = cell.Tag as ColorEditEntry;
            if (entry == null) return;

            if (e.Button == MouseButtons.Left)
            {
                OpenColorDialogForEntry(entry);
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowColorContextMenu(entry, cell);
            }
        }

        private void OpenColorDialogForEntry(ColorEditEntry entry)
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                dlg.Color = EngineColorToDrawing(entry.EngineValue);
                if (dlg.ShowDialog() != DialogResult.OK) return;

                // Preserve the alpha byte from the engine value because
                // ColorDialog has no alpha channel.
                byte preservedAlpha = (byte)(entry.EngineValue & 0xFF);
                var newColor = Color.FromArgb(preservedAlpha, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                uint newEngineValue = DrawingToEngineColor(newColor);

                entry.EngineValue = newEngineValue;
                entry.Commit(newEngineValue);
            }
        }

        /// Right click context menu for color cells
        private void ShowColorContextMenu(ColorEditEntry entry, DataGridViewCell cell)
        {
            var menu = new ContextMenuStrip();

            var copyItem = menu.Items.Add("Copy color");
            copyItem.Click += (s, ev) => _clipboardColor = entry.EngineValue;

            var pasteItem = menu.Items.Add("Paste color");
            pasteItem.Enabled = _clipboardColor.HasValue;
            pasteItem.Click += (s, ev) =>
            {
                if (!_clipboardColor.HasValue) return;
                var v = _clipboardColor.Value;
                entry.EngineValue = v;
                entry.Commit(v);
            };

            menu.Items.Add(new ToolStripSeparator());

            var hexItem = menu.Items.Add("Hex edit");
            hexItem.Click += (s, ev) =>
            {
                var typed = PromptForHex(entry.EngineValue);
                if (typed.HasValue)
                {
                    entry.EngineValue = typed.Value;
                    entry.Commit(typed.Value);
                }
            };

            var rect = propertiesGrid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, false);
            var screenPt = propertiesGrid.PointToScreen(new System.Drawing.Point(rect.Left, rect.Bottom));
            menu.Show(screenPt);
        }
        private static uint? PromptForHex(uint current)
        {
            using (var f = new Form())
            {
                f.Text = "Enter hex color (RRGGBBAA bytes — full 32-bit value)";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new System.Drawing.Size(360, 100);

                var label = new Label
                {
                    Text = "Hex value (8 digits):",
                    Left = 12,
                    Top = 12,
                    Width = 200
                };
                var tb = new TextBox
                {
                    Text = "0x" + current.ToString("X8"),
                    Left = 12,
                    Top = 36,
                    Width = 320
                };
                var ok = new Button
                {
                    Text = "OK",
                    Left = 170,
                    Top = 64,
                    Width = 75,
                    DialogResult = DialogResult.OK
                };
                var cancel = new Button
                {
                    Text = "Cancel",
                    Left = 255,
                    Top = 64,
                    Width = 75,
                    DialogResult = DialogResult.Cancel
                };
                f.Controls.Add(label);
                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog() != DialogResult.OK) return null;
                var s = (tb.Text ?? "").Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out var u))
                    return u;
                return null;
            }
        }

        private void PropertiesGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (suppressEditCallback) return;
            if (e.ColumnIndex != 1 || e.RowIndex < 0) return;

            var cell = propertiesGrid.Rows[e.RowIndex].Cells[1];
            var entry = cell.Tag as EditEntry;
            if (entry == null) return;
            string newVal = CellValueToString(cell.Value);
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
            }
        }

        private static string CellValueToString(object v)
        {
            if (v == null) return "";
            if (v is bool b) return b ? "True" : "False";
            if (v is System.Windows.Forms.CheckState cs)
                return cs == System.Windows.Forms.CheckState.Checked ? "True" : "False";
            return v.ToString();
        }

        // ---- Row builders ----

        private void ShowFileSummary(XncpFile f)
        {
            ClearGrid();
            AddRow("Project name", f.ProjectName ?? "");
            AddRow("Scenes", f.Scenes.Count.ToString());
            AddRow("Fonts", f.Fonts.Count.ToString());
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

        private void ShowSceneSummary(XncpScene s)
        {
            ClearGrid();
            AddRow("Name", s.Name);
            AddEditableRow("Z-index", s.ZIndex.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { s.ZIndex = f; return true; } return false; });
            AddEditableRow("Framerate", s.Framerate.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { s.Framerate = f; return true; } return false; });
            AddEditableRow("Aspect ratio", s.AspectRatio.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { s.AspectRatio = f; return true; } return false; });
            AddRow("Groups", s.Groups.Count.ToString());
            AddRow("Total casts", s.TotalCastCount.ToString());
            AddRow("Animations", s.Animations.Count.ToString());
            AddRow("SubImages", s.SubImages.Count.ToString());
        }

        private void ShowGroupSummary(XncpCastGroup g)
        {
            ClearGrid();
            AddRow("Group index", g.Index.ToString());
            AddRow("Casts", g.Casts.Count.ToString());

            int enabledCount = 0;
            int disabledCount = 0;
            foreach (var c in g.Casts)
            {
                if (c.IsEnabled) enabledCount++; else disabledCount++;
            }

            string state;
            Action<bool> setAll = on =>
            {
                foreach (var c in g.Casts) c.IsEnabled = on;
                BuildTree();
                ShowGroupSummary(g);
            };

            if (g.Casts.Count == 0)
                state = "(empty group)";
            else if (disabledCount == 0)
                state = "All enabled";
            else if (enabledCount == 0)
                state = "All disabled";
            else
                state = $"Mixed: {enabledCount} on / {disabledCount} off";

            AddRow("Children state", state);

            AddEditableEnumRow("Bulk action", "(choose...)",
                new[] { "(choose...)", "Enable all", "Disable all" },
                v =>
                {
                    if (v == "Enable all") setAll(true);
                    if (v == "Disable all") setAll(false);
                });
        }

        private void ShowCastDetails(XncpCast c)
        {
            ClearGrid();
            AddRow("Name", c.Name);
            AddRow("Group/Index", $"{c.GroupIndex} / {c.IndexInGroup}");
            AddEditableEnumRow("Type", c.DrawType.ToString(),
                new[] { "None", "Sprite", "Font" },
                v => { if (Enum.TryParse<XncpCastType>(v, out var t)) c.DrawType = t; });

            if (c.HasCastInfo)
                AddEditableBoolRow("Enabled", c.IsEnabled, v => c.IsEnabled = v);

            if (c.HasCastInfo)
            {
                AddColorRow("Color", c.Color, val => c.Color = val);
                AddColorRow("Gradient TL", c.GradientTL, val => c.GradientTL = val);
                AddColorRow("Gradient TR", c.GradientTR, val => c.GradientTR = val);
                AddColorRow("Gradient BL", c.GradientBL, val => c.GradientBL = val);
                AddColorRow("Gradient BR", c.GradientBR, val => c.GradientBR = val);
            }

            AddEditableRow("TopLeftX", c.TopLeftX.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.TopLeftX = f; return true; } return false; });
            AddEditableRow("TopLeftY", c.TopLeftY.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.TopLeftY = f; return true; } return false; });
            AddEditableRow("BottomLeftX", c.BottomLeftX.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.BottomLeftX = f; return true; } return false; });
            AddEditableRow("BottomLeftY", c.BottomLeftY.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.BottomLeftY = f; return true; } return false; });
            AddEditableRow("TopRightX", c.TopRightX.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.TopRightX = f; return true; } return false; });
            AddEditableRow("TopRightY", c.TopRightY.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.TopRightY = f; return true; } return false; });
            AddEditableRow("BottomRightX", c.BottomRightX.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.BottomRightX = f; return true; } return false; });
            AddEditableRow("BottomRightY", c.BottomRightY.ToString("R"),
                v => { if (float.TryParse(v, out var f)) { c.BottomRightY = f; return true; } return false; });

            if (c.HasCastInfo)
            {
                AddEditableRow("ScaleX", c.ScaleX.ToString("R"),
                    v => { if (float.TryParse(v, out var f)) { c.ScaleX = f; return true; } return false; });
                AddEditableRow("ScaleY", c.ScaleY.ToString("R"),
                    v => { if (float.TryParse(v, out var f)) { c.ScaleY = f; return true; } return false; });
                AddEditableRow("TranslationX", c.TranslationX.ToString("R"),
                    v => { if (float.TryParse(v, out var f)) { c.TranslationX = f; return true; } return false; });
                AddEditableRow("TranslationY", c.TranslationY.ToString("R"),
                    v => { if (float.TryParse(v, out var f)) { c.TranslationY = f; return true; } return false; });
                AddEditableRow("Rotation", c.Rotation.ToString("R"),
                    v => { if (float.TryParse(v, out var f)) { c.Rotation = f; return true; } return false; });
            }
            if (c.DrawType == XncpCastType.Font)
            {
                AddRow("Font name", c.FontName);
                AddRow("Font chars", c.FontCharacters);
            }
            if (c.TextureIndex >= 0)
            {
                AddEditableRow("Texture idx", c.TextureIndex.ToString(),
                    v => { if (int.TryParse(v, out var i)) { c.TextureIndex = i; return true; } return false; });
            }

            AddEditableRow("Field34 (hex)", "0x" + c.Field34.ToString("X8"),
                v => TryParseHex(v, u => c.Field34 = u));
            AddEditableRow("Field3C (hex)", "0x" + c.Field3C.ToString("X8"),
                v => TryParseHex(v, u => c.Field3C = u));
        }

        /// <summary>parse a "0xNNNN" or "NNNN" hex string and run an
        /// action with the result. Returns true on success.</summary>
        private static bool TryParseHex(string input, Action<uint> apply)
        {
            var s = input ?? "";
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out var u))
            {
                apply(u);
                return true;
            }
            return false;
        }

        private void ShowAnimDetails(XncpAnimation a)
        {
            ClearGrid();
            AddRow("Name", a.Name ?? "");
            AddEditableRow("Index", a.Index.ToString(),
                v => { if (int.TryParse(v, out var i)) { a.Index = i; return true; } return false; });
        }
        private void ShowFontDetails(XncpFontInfo f)
        {
            ClearGrid();
            AddRow("Name", f.Name ?? "");
            AddRow("Character count", f.CharacterCount.ToString());
        }
    }
}