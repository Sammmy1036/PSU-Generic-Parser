namespace psu_archive_explorer.FileViewers
{
    partial class XncfFileViewer
    {
        private System.ComponentModel.IContainer components = null;
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code
        private void InitializeComponent()
        {
            this.mainSplit = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.propertiesGrid = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.propertiesGrid)).BeginInit();
            this.SuspendLayout();
            //
            // mainSplit
            //
            this.mainSplit.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.mainSplit.Location = new System.Drawing.Point(0, 0);
            this.mainSplit.Name = "mainSplit";
            //
            // mainSplit.Panel1 (tree)
            //
            this.mainSplit.Panel1.Controls.Add(this.treeView1);
            //
            // mainSplit.Panel2 (properties grid)
            //
            this.mainSplit.Panel2.Controls.Add(this.propertiesGrid);
            this.mainSplit.Size = new System.Drawing.Size(720, 480);
            this.mainSplit.SplitterDistance = 280;
            this.mainSplit.TabIndex = 0;
            //
            // treeView1
            //
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.HideSelection = false;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(280, 480);
            this.treeView1.TabIndex = 0;
            //
            // propertiesGrid
            //
            this.propertiesGrid.AllowUserToAddRows = false;
            this.propertiesGrid.AllowUserToDeleteRows = false;
            this.propertiesGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.propertiesGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.propertiesGrid.Location = new System.Drawing.Point(0, 0);
            this.propertiesGrid.Name = "propertiesGrid";
            this.propertiesGrid.ReadOnly = true;
            this.propertiesGrid.RowHeadersVisible = false;
            this.propertiesGrid.Size = new System.Drawing.Size(436, 480);
            this.propertiesGrid.TabIndex = 0;
            //
            // XncfFileViewer
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.mainSplit);
            this.Name = "XncfFileViewer";
            this.Size = new System.Drawing.Size(720, 480);
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.propertiesGrid)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.DataGridView propertiesGrid;
    }
}