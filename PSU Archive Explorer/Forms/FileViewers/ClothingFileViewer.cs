using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using PSULib.FileClasses.Items;

namespace psu_archive_explorer
{
    public partial class ClothingFileViewer : UserControl
    {
        private readonly ItemSuitParamFile internalFile;
        private readonly DataGridView[] clothesViews = new DataGridView[6];
        private bool updatingTable = false;

        public ClothingFileViewer(ItemSuitParamFile toImport)
        {
            InitializeComponent();
            internalFile = toImport;
            clothesViews[0] = dataGridView1;
            clothesViews[1] = dataGridView2;
            clothesViews[2] = dataGridView3;
            clothesViews[3] = dataGridView4;
            clothesViews[4] = dataGridView5;
            clothesViews[5] = dataGridView6;

            for (int i = 0; i < 6; i++)
            {
                clothesViews[i].AutoGenerateColumns = true;
                clothesViews[i].DataSource = internalFile.clothes[i];
                clothesViews[i].AllowUserToAddRows = false;
                clothesViews[i].AllowUserToDeleteRows = false;
                clothesViews[i].AllowUserToOrderColumns = false;
                clothesViews[i].VirtualMode = false;

                for (int j = 0; j < clothesViews[i].Columns.Count; j++)
                {
                    clothesViews[i].Columns[j].MinimumWidth = 32;
                }
                clothesViews[i].CellValueChanged += new DataGridViewCellEventHandler(clothingGrid_CellValueChanged);
            }
        }

        private void clothingGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (updatingTable) return;

            DataGridView temp = sender as DataGridView;
            if (temp == null) return;
            if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
            if (temp.Columns[e.ColumnIndex].DataPropertyName != "IsValid") return;

            DataGridViewCheckBoxCell cell = temp[e.ColumnIndex, e.RowIndex] as DataGridViewCheckBoxCell;
            if (cell == null) return;

            bool isChecked = cell.Value is bool && (bool)cell.Value;
            if (!isChecked) return;

            updatingTable = true;
            try
            {
                // I still need to replace with the actual property names on the clothing record type
                // (the items in internalFile.clothes[i]). I copied this method
                // from the weapon viewer and referenced WeaponModel/MinATP/Ata/etc
                // which do not exist on clothing rows and would throw at runtime.
                //
                // Example of what this should look like once I find out the correct columns:
                //
                // temp["ClothingModel", e.RowIndex].Value = (short)e.RowIndex;
                // temp["Defense",       e.RowIndex].Value = 1;
                // temp["AvailableRaces",e.RowIndex].Value = 63;
                // temp["Rank",          e.RowIndex].Value = 1;
                // temp["Price",         e.RowIndex].Value = 1;
            }
            finally
            {
                updatingTable = false;
            }
        }

        private void numberRows(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            DataGridView gridView = sender as DataGridView;
            if (gridView == null) return;

            if (gridView == dataGridView5)
            {
                foreach (DataGridViewRow r in gridView.Rows)
                    gridView.Rows[r.Index].HeaderCell.Value = r.Index.ToString();
            }
            else
            {
                foreach (DataGridViewRow r in gridView.Rows)
                    gridView.Rows[r.Index].HeaderCell.Value = r.Index.ToString("X2");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog tempDialog = new SaveFileDialog())
            {
                if (tempDialog.ShowDialog() != DialogResult.OK) return;
                using (Stream outStream = tempDialog.OpenFile())
                {
                    internalFile.saveTextFile(outStream);
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog tempDialog = new OpenFileDialog())
            {
                if (tempDialog.ShowDialog() != DialogResult.OK) return;
                using (Stream inStream = tempDialog.OpenFile())
                {
                    internalFile.loadTextFile(inStream);
                }
            }

            // Rebind the grids so any list replacements done by loadTextFile are reflected.
            // (Control.Refresh() only repaints; it does not rebind data sources.)
            updatingTable = true;
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    clothesViews[i].DataSource = null;
                    clothesViews[i].DataSource = internalFile.clothes[i];
                }
            }
            finally
            {
                updatingTable = false;
            }
        }
    }
}