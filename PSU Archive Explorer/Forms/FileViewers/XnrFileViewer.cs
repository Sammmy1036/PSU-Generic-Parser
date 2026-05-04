using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PSULib.FileClasses.Models;

namespace psu_archive_explorer.FileViewers
{
    public partial class XnrFileViewer : UserControl
    {
        public XnrFile loadedFile;
        private class KnownItem
        {
            public string Name;
            public string FilenameMatch;
            // Optional. When set, items sharing the same Group string under
            // the same FilenameMatch are nested under a parent tree node
            // named Group. Leave null/empty for ungrouped items.
            public string Group;
            public List<KnownPoint> Points;
        }
        private class KnownPoint
        {
            public int PointNumber;          // 1-indexed
            public float BaseV1;
            public float BaseV2;
        }

        private static readonly List<KnownItem> s_knownItems = new List<KnownItem>
        {
            new KnownItem
            {
                Name = "Size",
                Group = "Inventory Item Window",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 632, BaseV1 = 296f, BaseV2 = 188f }
                }
            },

            new KnownItem
            {
                Name = "Spacing",
                Group = "Inventory Item Window",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 630, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Size",
                Group = "Form Party",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 841, BaseV1 = 224, BaseV2 = 0 }, // width
                // new KnownPoint { PointNumber = 841, BaseV1 = 224, BaseV2 = 0 } // height
                }
            },
            new KnownItem
            {
                Name = "Spacing",
                Group = "Form Party",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 839, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Size",
                Group = "Partner Card List",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 38, BaseV1 = 224, BaseV2 = 0 }, // width
                // new KnownPoint { PointNumber = 38, BaseV1 = 224, BaseV2 = 0 } // height
                }
            },
            new KnownItem
            {
                Name = "Spacing",
                Group = "Partner Card List",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 36, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Size",
                Group = "Simple Mail Window",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 1798, BaseV1 = 200, BaseV2 = 0 }, // width
                // new KnownPoint { PointNumber = 1798, BaseV1 = 200, BaseV2 = 0 }, // height
                }
            },
            new KnownItem
            {
                Name = "Spacing",
                Group = "Simple Mail Window",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 1796, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Size",
                Group = "Photon Charger Menu",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 7210, BaseV1 = 200, BaseV2 = 0 }, // width
                /// new KnownPoint { PointNumber = 7210, BaseV1 = 200, BaseV2 = 0 } // height
                }
            },
            new KnownItem
            {
                Name = "Spacing",
                Group = "Mission Map Menu",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 3171, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Spacing",
                Group = "Photon Charger Menu",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 7208, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Size",
                Group = "Blacklist Menu",
                FilenameMatch = "WindowConvert.xnr",
                Points = new List<KnownPoint>
                {
                new KnownPoint { PointNumber = 2282, BaseV1 = 200, BaseV2 = 0 }, // width
                // new KnownPoint { PointNumber = 2282, BaseV1 = 200, BaseV2 = 0 }, // height
                }
            },

            // ---- Option.xnr : System Options Menu --------------------------
            // Listed in the order rows appear in the in-game menu, top to
            // bottom. Camera Controls has nested Third-Person and First-Person
            // submenus, each with their own Rotate / Raise-Lower rows; those
            // are flattened into prefixed leaf names since the tree only
            // supports a single Group level.
            //
            // Position/Length apply to the menu container itself.
            new KnownItem
            {
                Name = "Position",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 1972, BaseV1 = 48, BaseV2 = 69 }
                }
            },
            new KnownItem
            {
                Name = "Length",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    // V1 has no effect; V2 controls Y length. Base V1 left at 0.
                    new KnownPoint { PointNumber = 1974, BaseV1 = 0, BaseV2 = 290 }
                }
            },
            // Per-row widths, in menu order. V1 affects X, V2 unused (base 0).
            // Row 1: Text Display Speed
            new KnownItem
            {
                Name = "Text Display Speed Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 126, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 2: Sound
            new KnownItem
            {
                Name = "Sound Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 1303, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 3: Music Volume
            new KnownItem
            {
                Name = "Music Volume Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 280, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 4: Sound Effect Volume
            new KnownItem
            {
                Name = "Sound Effect Volume Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 291, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 5: Vibration
            new KnownItem
            {
                Name = "Vibration Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 302, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 6: Radar Map Display
            new KnownItem
            {
                Name = "Radar Map Display Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 313, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 7: Cut-In Display
            new KnownItem
            {
                Name = "Cut-In Display Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 324, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 8: Button Detail Display
            new KnownItem
            {
                Name = "Button Detail Display Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 203, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 9: Main Menu Cursor Position
            new KnownItem
            {
                Name = "Main Menu Cursor Position Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 335, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 10: Camera Controls (with nested Third-Person and
            // First-Person submenus, each containing Rotate + Raise/Lower).
            new KnownItem
            {
                Name = "Camera Controls Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 368, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — Third-Person Camera Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 379, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — Third-Person Rotate Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 401, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — Third-Person Raise/Lower Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 104, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — First-Person Camera Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 1644, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — First-Person Rotate Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 1699, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            new KnownItem
            {
                Name = "Camera Controls — First-Person Raise/Lower Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 1688, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 11: Weapon/TECHNIC Swap
            new KnownItem
            {
                Name = "Weapon/TECHNIC Swap Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 412, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 12: Lock-On
            new KnownItem
            {
                Name = "Lock-On Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 929, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 13: Function Key Setting
            new KnownItem
            {
                Name = "Function Key Setting Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 940, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Row 14: Controller
            new KnownItem
            {
                Name = "Controller Width",
                Group = "System Options Menu",
                FilenameMatch = "Option.xnr",
                Points = new List<KnownPoint>
                {
                    new KnownPoint { PointNumber = 357, BaseV1 = 224, BaseV2 = 0 }
                }
            },
            // Rows 15–17 (Brightness, Return, Return to Default) have no
            // mapped points in the provided data, so they're omitted.
        };

        // Filtering thresholds
        private const float ZeroEpsilon = 1e-8f;

        // Currently displayed mode (drives which rows show in the grid).
        private enum ViewMode { None, FileSummary, KnownItem, UnknownPoints }
        private ViewMode _currentMode = ViewMode.None;
        private KnownItem _currentItem;
        private bool _suppressEditCallback;

        public XnrFileViewer(XnrFile xnr)
        {
            InitializeComponent();
            loadedFile = xnr;

            BuildTree();
            InitGrid();
            InitScaleCombo();

            treeView1.AfterSelect += TreeView1_AfterSelect;
            if (treeView1.Nodes.Count > 0)
            {
                var itemsNode = treeView1.Nodes[0];
                itemsNode.Expand();
                if (itemsNode.Nodes.Count > 0)
                    treeView1.SelectedNode = itemsNode.Nodes[0];
                else
                    treeView1.SelectedNode = itemsNode;
            }
        }

        // ---- Tree -----------------------------------------------------------

        private void BuildTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            // Root node: leaf of the loaded file's path, or "(file)" as a
            // fallback. Replaces the old static "HUD" label.
            string rootLabel = GetLeafFilename(loadedFile.filename);
            if (string.IsNullOrEmpty(rootLabel)) rootLabel = "(file)";
            var itemsRoot = new TreeNode(rootLabel) { Tag = "ITEMS_ROOT" };

            // Walk known items in declaration order. Items that share the
            // same Group string become children of a synthesized group node.
            // Items with no Group attach directly to the root.
            var groupNodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);

            foreach (var item in s_knownItems)
            {
                if (!string.IsNullOrEmpty(item.FilenameMatch)
                    && !FilenameMatches(loadedFile.filename, item.FilenameMatch))
                    continue;

                var leaf = new TreeNode(item.Name) { Tag = item };

                if (!string.IsNullOrEmpty(item.Group))
                {
                    if (!groupNodes.TryGetValue(item.Group, out var group))
                    {
                        group = new TreeNode(item.Group) { Tag = "GROUP" };
                        groupNodes[item.Group] = group;
                        itemsRoot.Nodes.Add(group);
                    }
                    group.Nodes.Add(leaf);
                }
                else
                {
                    itemsRoot.Nodes.Add(leaf);
                }
            }

            var unknownNode = new TreeNode("Unknown Points")
            {
                Tag = "UNKNOWN_POINTS",
            };
            itemsRoot.Nodes.Add(unknownNode);

            treeView1.Nodes.Add(itemsRoot);
            itemsRoot.Expand();
            // Expand groups so the user can see the Size/Spacing leaves
            // without an extra click.
            foreach (var g in groupNodes.Values) g.Expand();
            treeView1.EndUpdate();
        }

        private static string GetLeafFilename(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        private static bool FilenameMatches(string actual, string expected)
        {
            if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(expected))
                return false;
            string actualLeaf = actual;
            int slash = actualLeaf.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) actualLeaf = actualLeaf.Substring(slash + 1);
            return string.Equals(actualLeaf, expected,
                StringComparison.OrdinalIgnoreCase);
        }

        // ---- Selection handling --------------------------------------------

        private void TreeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var tag = e.Node?.Tag;

            if (tag is KnownItem ki)
            {
                _currentMode = ViewMode.KnownItem;
                _currentItem = ki;
                ShowKnownItem(ki);
                actionBar.Visible = true;
            }
            else if (tag is string s && s == "UNKNOWN_POINTS")
            {
                _currentMode = ViewMode.UnknownPoints;
                _currentItem = null;
                ShowUnknownPoints();
                actionBar.Visible = false;
            }
            else if (tag is string gs && gs == "GROUP")
            {
                // Selecting a group node (e.g. "Inventory Item Window")
                // forwards to its first child so the user immediately sees
                // a meaningful page rather than a blank/summary view. The
                // group itself stays expanded so siblings are one click away.
                if (e.Node.Nodes.Count > 0)
                {
                    treeView1.SelectedNode = e.Node.Nodes[0];
                }
                else
                {
                    _currentMode = ViewMode.FileSummary;
                    _currentItem = null;
                    ShowFileSummary();
                    actionBar.Visible = false;
                }
            }
            else
            {
                // "Items" root or anything else show file summary
                _currentMode = ViewMode.FileSummary;
                _currentItem = null;
                ShowFileSummary();
                actionBar.Visible = false;
            }
        }
        private enum GridLayout { Summary, Points }
        private GridLayout _layout = GridLayout.Summary;

        private void InitGrid()
        {
            propertiesGrid.AutoGenerateColumns = false;
            propertiesGrid.AllowUserToAddRows = false;
            propertiesGrid.AllowUserToDeleteRows = false;
            propertiesGrid.RowHeadersVisible = false;
            propertiesGrid.ReadOnly = false;
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
            propertiesGrid.SortCompare += PropertiesGrid_SortCompare;

            ApplyLayout(GridLayout.Summary);
        }

        private void ApplyLayout(GridLayout layout)
        {
            if (_layout == layout && propertiesGrid.Columns.Count > 0) return;
            _layout = layout;

            _suppressEditCallback = true;
            propertiesGrid.Rows.Clear();
            propertiesGrid.Columns.Clear();

            if (layout == GridLayout.Summary)
            {
                propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Property",
                    HeaderText = "Property",
                    ReadOnly = true,
                    Width = 130,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                });
                propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Value",
                    HeaderText = "Value",
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                });
            }
            else // Points
            {
                propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Point",
                    HeaderText = "Point #",
                    ReadOnly = true,
                    Width = 90,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = DataGridViewContentAlignment.MiddleCenter,
                        ForeColor = SystemColors.GrayText,
                    },
                });
                propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Value1",
                    HeaderText = "Value 1",
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                });
                propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Value2",
                    HeaderText = "Value 2",
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                });
            }
            _suppressEditCallback = false;
        }

        // ---- Edit-binding row tags -----------------------------------------
        // Rows that are editable carry an EditEntry on each editable cell's
        // Tag. CellValueChanged invokes the entry's commit lambda with the
        // cell's new string value; if it returns false, the cell reverts.

        private class EditEntry
        {
            public string OriginalValue;
            public Func<string, bool> Commit;
        }

        private void PropertiesGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressEditCallback) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var cell = propertiesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            var entry = cell.Tag as EditEntry;
            if (entry == null) return;

            string newVal = cell.Value == null ? "" : cell.Value.ToString();
            bool ok = false;
            try { ok = entry.Commit(newVal); }
            catch { ok = false; }

            if (!ok)
            {
                _suppressEditCallback = true;
                cell.Value = entry.OriginalValue;
                _suppressEditCallback = false;
            }
            else
            {
                entry.OriginalValue = newVal;
            }
        }
        private void PropertiesGrid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (_layout != GridLayout.Points) return;

            string a = e.CellValue1 == null ? "" : e.CellValue1.ToString();
            string b = e.CellValue2 == null ? "" : e.CellValue2.ToString();

            string colName = e.Column.Name;
            if (colName == "Point")
            {
                // Cell text looks like "#3173" — strip the '#' and compare
                // as integers so "#36" < "#1059" < "#3173".
                int ai = ParsePointNumber(a);
                int bi = ParsePointNumber(b);
                e.SortResult = ai.CompareTo(bi);
                e.Handled = true;
            }
            else if (colName == "Value1" || colName == "Value2")
            {
                float af = ParseSortFloat(a);
                float bf = ParseSortFloat(b);
                e.SortResult = af.CompareTo(bf);
                e.Handled = true;
            }
            if (e.Handled && e.SortResult == 0)
            {
                string pa = propertiesGrid.Rows[e.RowIndex1].Cells[0].Value as string ?? "";
                string pb = propertiesGrid.Rows[e.RowIndex2].Cells[0].Value as string ?? "";
                e.SortResult = ParsePointNumber(pa).CompareTo(ParsePointNumber(pb));
            }
        }

        private static int ParsePointNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return int.MaxValue;
            // Strip leading '#' and any spaces.
            string trimmed = s.TrimStart('#', ' ');
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n;
            return int.MaxValue;
        }

        private static float ParseSortFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return float.PositiveInfinity;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                // NaN compares equal to everything via CompareTo, which
                // breaks sort stability. Map it to +inf so it ends up at
                // the bottom of an ascending sort like Excel does.
                if (float.IsNaN(f)) return float.PositiveInfinity;
                return f;
            }
            return float.PositiveInfinity;
        }

        // ---- Page populators -----------------------------------------------

        private void ShowFileSummary()
        {
            ApplyLayout(GridLayout.Summary);
            _suppressEditCallback = true;
            propertiesGrid.Rows.Clear();

            AddSummaryRow("Filename", loadedFile.filename ?? "");
            AddSummaryRow("Total points", loadedFile.Points.Count.ToString(CultureInfo.InvariantCulture));
            AddSummaryRow("Float blob start",
                loadedFile.FloatStart >= 0
                    ? "0x" + loadedFile.FloatStart.ToString("X")
                    : "(not parsed)");
            AddSummaryRow("File size",
                (loadedFile.RawBytes != null ? loadedFile.RawBytes.Length : 0)
                    .ToString(CultureInfo.InvariantCulture) + " bytes");
            AddSummaryRow("Modified?", loadedFile.dirty ? "yes (export to save changes)" : "no");

            if (!string.IsNullOrEmpty(loadedFile.ParseError))
                AddSummaryRow("Parse error", loadedFile.ParseError);
            if (!string.IsNullOrEmpty(loadedFile.ParseInfo))
                AddSummaryRow("Parse info", loadedFile.ParseInfo);

            if (loadedFile.EditLog.Count > 0)
            {
                AddSummaryRow("", "");
                AddSummaryRow("Recent edits",
                    "(most recent " + loadedFile.EditLog.Count + ")");
                for (int i = loadedFile.EditLog.Count - 1; i >= 0; i--)
                {
                    AddSummaryRow("  edit " + (i + 1), loadedFile.EditLog[i]);
                }
            }

            _suppressEditCallback = false;
        }

        private void AddSummaryRow(string label, string value)
        {
            int idx = propertiesGrid.Rows.Add(label, value ?? "");
            propertiesGrid.Rows[idx].Cells[1].ReadOnly = true;
            propertiesGrid.Rows[idx].Cells[1].Style.ForeColor = SystemColors.GrayText;
        }

        // ---- Known item page -----------------------------------------------

        private void ShowKnownItem(KnownItem item)
        {
            ApplyLayout(GridLayout.Points);
            _suppressEditCallback = true;
            propertiesGrid.Rows.Clear();

            actionBarHint.Text = (string.IsNullOrEmpty(item.Group)
                    ? "Multiplies base values "
                    : item.Group + " — multiplies base values ")
                + FormatBaseSummary(item) + ".";

            foreach (var kp in item.Points)
            {
                var pt = FindPointByNumber(kp.PointNumber);
                if (pt == null)
                {
                    int idx = propertiesGrid.Rows.Add(
                        "#" + kp.PointNumber, "(missing)", "(missing)");
                    var row = propertiesGrid.Rows[idx];
                    row.Cells[1].ReadOnly = true;
                    row.Cells[2].ReadOnly = true;
                    row.Cells[1].Style.ForeColor = SystemColors.GrayText;
                    row.Cells[2].Style.ForeColor = SystemColors.GrayText;
                    continue;
                }

                AddEditablePointRow(pt);
            }

            _suppressEditCallback = false;
            scaleCombo.SelectedIndex = 0;
        }

        private static string FormatBaseSummary(KnownItem item)
        {
            if (item.Points.Count == 0) return "(none)";
            var first = item.Points[0];
            bool allSame = item.Points.TrueForAll(p =>
                p.BaseV1 == first.BaseV1 && p.BaseV2 == first.BaseV2);
            if (allSame)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "({0:0.###}, {1:0.###})", first.BaseV1, first.BaseV2);
            }
            return "per point";
        }

        // ---- Unknown points page -------------------------------------------

        private void ShowUnknownPoints()
        {
            ApplyLayout(GridLayout.Points);
            _suppressEditCallback = true;
            propertiesGrid.Rows.Clear();

            var claimed = new HashSet<int>();
            foreach (var item in s_knownItems)
            {
                if (!string.IsNullOrEmpty(item.FilenameMatch)
                    && !FilenameMatches(loadedFile.filename, item.FilenameMatch))
                    continue;
                foreach (var p in item.Points) claimed.Add(p.PointNumber);
            }

            int shown = 0;
            foreach (var pt in loadedFile.Points)
            {
                if (claimed.Contains(pt.PointNumber)) continue;
                if (pt.IsZeroPoint) continue;     // hide (0,0)
                if (pt.HasNaNOrInf) continue;     // hide NaN/Inf

                AddEditablePointRow(pt);
                shown++;
            }

            _suppressEditCallback = false;

            int total = loadedFile.Points.Count;
            int hidden = total - shown - claimed.Count;
            System.Diagnostics.Debug.WriteLine(string.Format(
                "Unknown Points: showing {0} / {1} ({2} hidden, {3} claimed)",
                shown, total, hidden, claimed.Count));
        }

        // ---- Editable point row builder -------------------------------------

        private void AddEditablePointRow(XnrPoint pt)
        {
            int idx = propertiesGrid.Rows.Add(
                "#" + pt.PointNumber.ToString(CultureInfo.InvariantCulture),
                FormatFloat(pt.Value1),
                FormatFloat(pt.Value2));
            var row = propertiesGrid.Rows[idx];

            row.Cells[1].Tag = new EditEntry
            {
                OriginalValue = FormatFloat(pt.Value1),
                Commit = newStr =>
                {
                    if (!TryParseFloat(newStr, out float f)) return false;
                    pt.Value1 = f;     // writes through to RawBytes + dirty=true
                    return true;
                },
            };
            row.Cells[2].Tag = new EditEntry
            {
                OriginalValue = FormatFloat(pt.Value2),
                Commit = newStr =>
                {
                    if (!TryParseFloat(newStr, out float f)) return false;
                    pt.Value2 = f;
                    return true;
                },
            };
        }

        private static string FormatFloat(float f)
        {
            return f.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool TryParseFloat(string s, out float result)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private XnrPoint FindPointByNumber(int pointNumber)
        {
            // Points are emitted in order so PointNumber == index+1
            int idx = pointNumber - 1;
            if (idx >= 0 && idx < loadedFile.Points.Count
                && loadedFile.Points[idx].PointNumber == pointNumber)
                return loadedFile.Points[idx];

            foreach (var p in loadedFile.Points)
                if (p.PointNumber == pointNumber) return p;
            return null;
        }

        // ---- Scale dropdown -------------------------------------------------

        private void InitScaleCombo()
        {
            scaleCombo.SelectedIndex = 0;     // "1.00x"
            applyScaleButton.Click += ApplyScaleButton_Click;
        }

        private void ApplyScaleButton_Click(object sender, EventArgs e)
        {
            if (_currentMode != ViewMode.KnownItem || _currentItem == null) return;

            float factor = ParseScaleSelection();
            if (factor <= 0f) return;

            // each point's value = base * factor. Writes go straight
            // through to RawBytes via the property setters, and the grid is
            // refreshed so u see the new values.
            int applied = 0;
            foreach (var kp in _currentItem.Points)
            {
                var pt = FindPointByNumber(kp.PointNumber);
                if (pt == null) continue;
                pt.Value1 = kp.BaseV1 * factor;
                pt.Value2 = kp.BaseV2 * factor;
                applied++;
            }
            ShowKnownItem(_currentItem);

            MessageBox.Show(this,
                string.Format(CultureInfo.InvariantCulture,
                    "Applied {0:0.##}x scale to {1} point(s) in '{2}'.",
                    factor, applied, _currentItem.Name),
                "Scale applied",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private float ParseScaleSelection()
        {
            var sel = scaleCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(sel)) return 0f;
            string trimmed = sel.TrimEnd('x', 'X', ' ');
            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return f;
            return 0f;
        }
    }
}