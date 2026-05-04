namespace psu_archive_explorer.FileViewers
{
    partial class XnrFileViewer
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
            this.rightPanel = new System.Windows.Forms.Panel();
            this.actionBar = new System.Windows.Forms.Panel();
            this.scaleLabel = new System.Windows.Forms.Label();
            this.scaleCombo = new System.Windows.Forms.ComboBox();
            this.applyScaleButton = new System.Windows.Forms.Button();
            this.actionBarHint = new System.Windows.Forms.Label();
            this.propertiesGrid = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            this.rightPanel.SuspendLayout();
            this.actionBar.SuspendLayout();
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
            // mainSplit.Panel2 (action bar + properties grid)
            //
            this.mainSplit.Panel2.Controls.Add(this.rightPanel);
            this.mainSplit.Size = new System.Drawing.Size(720, 480);
            this.mainSplit.SplitterDistance = 220;
            this.mainSplit.TabIndex = 0;
            //
            // treeView1
            //
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.HideSelection = false;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(220, 480);
            this.treeView1.TabIndex = 0;
            //
            // rightPanel
            //
            this.rightPanel.Controls.Add(this.propertiesGrid);
            this.rightPanel.Controls.Add(this.actionBar);
            this.rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightPanel.Location = new System.Drawing.Point(0, 0);
            this.rightPanel.Name = "rightPanel";
            this.rightPanel.Size = new System.Drawing.Size(496, 480);
            this.rightPanel.TabIndex = 0;
            //
            // actionBar (top of right panel; visible only for Inventory HUD)
            //
            this.actionBar.Controls.Add(this.actionBarHint);
            this.actionBar.Controls.Add(this.applyScaleButton);
            this.actionBar.Controls.Add(this.scaleCombo);
            this.actionBar.Controls.Add(this.scaleLabel);
            this.actionBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.actionBar.Location = new System.Drawing.Point(0, 0);
            this.actionBar.Name = "actionBar";
            this.actionBar.Size = new System.Drawing.Size(496, 34);
            this.actionBar.TabIndex = 0;
            this.actionBar.Visible = false;
            //
            // scaleLabel
            //
            this.scaleLabel.AutoSize = true;
            this.scaleLabel.Location = new System.Drawing.Point(8, 9);
            this.scaleLabel.Name = "scaleLabel";
            this.scaleLabel.Size = new System.Drawing.Size(76, 13);
            this.scaleLabel.TabIndex = 0;
            this.scaleLabel.Text = "Scale (× base):";
            //
            // scaleCombo
            //
            this.scaleCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.scaleCombo.FormattingEnabled = true;
            this.scaleCombo.Items.AddRange(new object[] {
            "1.00x",
            "1.25x",
            "1.50x",
            "1.75x",
            "2.00x"});
            this.scaleCombo.Location = new System.Drawing.Point(90, 6);
            this.scaleCombo.Name = "scaleCombo";
            this.scaleCombo.Size = new System.Drawing.Size(80, 21);
            this.scaleCombo.TabIndex = 1;
            //
            // applyScaleButton
            //
            this.applyScaleButton.Location = new System.Drawing.Point(178, 5);
            this.applyScaleButton.Name = "applyScaleButton";
            this.applyScaleButton.Size = new System.Drawing.Size(75, 23);
            this.applyScaleButton.TabIndex = 2;
            this.applyScaleButton.Text = "Apply";
            this.applyScaleButton.UseVisualStyleBackColor = true;
            //
            // actionBarHint
            //
            this.actionBarHint.AutoSize = true;
            this.actionBarHint.ForeColor = System.Drawing.SystemColors.GrayText;
            this.actionBarHint.Location = new System.Drawing.Point(260, 9);
            this.actionBarHint.Name = "actionBarHint";
            this.actionBarHint.Size = new System.Drawing.Size(180, 13);
            this.actionBarHint.TabIndex = 3;
            this.actionBarHint.Text = "Multiplies base values (296, 188).";
            //
            // propertiesGrid
            //
            this.propertiesGrid.AllowUserToAddRows = false;
            this.propertiesGrid.AllowUserToDeleteRows = false;
            this.propertiesGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.propertiesGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.propertiesGrid.Location = new System.Drawing.Point(0, 34);
            this.propertiesGrid.Name = "propertiesGrid";
            this.propertiesGrid.RowHeadersVisible = false;
            this.propertiesGrid.Size = new System.Drawing.Size(496, 446);
            this.propertiesGrid.TabIndex = 1;
            //
            // XnrFileViewer
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.mainSplit);
            this.Name = "XnrFileViewer";
            this.Size = new System.Drawing.Size(720, 480);
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            this.rightPanel.ResumeLayout(false);
            this.actionBar.ResumeLayout(false);
            this.actionBar.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.propertiesGrid)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Panel rightPanel;
        private System.Windows.Forms.Panel actionBar;
        private System.Windows.Forms.Label scaleLabel;
        private System.Windows.Forms.ComboBox scaleCombo;
        private System.Windows.Forms.Button applyScaleButton;
        private System.Windows.Forms.Label actionBarHint;
        private System.Windows.Forms.DataGridView propertiesGrid;
    }
}