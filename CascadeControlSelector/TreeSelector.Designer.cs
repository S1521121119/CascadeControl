namespace CascadeControlSelector
{
    partial class TreeSelector
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.cascadeView1 = new CascadeControl.Views.CascadeView();
            this.treeImageView1 = new CascadeControl.Views.TreeImage.CascadeTreeImageView();
            this.txtPath = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).BeginInit();
            this.splitContainer2.Panel1.SuspendLayout();
            this.splitContainer2.Panel2.SuspendLayout();
            this.splitContainer2.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer2
            // 
            this.splitContainer2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer2.Location = new System.Drawing.Point(12, 40);
            this.splitContainer2.Name = "splitContainer2";
            // 
            // splitContainer2.Panel1
            // 
            this.splitContainer2.Panel1.Controls.Add(this.cascadeView1);
            // 
            // splitContainer2.Panel2
            // 
            this.splitContainer2.Panel2.Controls.Add(this.treeImageView1);
            this.splitContainer2.Size = new System.Drawing.Size(659, 398);
            this.splitContainer2.SplitterDistance = 343;
            this.splitContainer2.TabIndex = 7;
            // 
            // cascadeView1
            // 
            this.cascadeView1.AutoScroll = true;
            this.cascadeView1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.cascadeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cascadeView1.Location = new System.Drawing.Point(0, 0);
            this.cascadeView1.Name = "cascadeView1";
            this.cascadeView1.Size = new System.Drawing.Size(343, 398);
            this.cascadeView1.TabIndex = 0;
            this.cascadeView1.NodeActivated += new System.EventHandler<CascadeControl.State.CascadeNodeEventArgs>(this.CascadeView1_NodeActivated);
            // 
            // treeImageView1
            // 
            this.treeImageView1.AutoScroll = true;
            this.treeImageView1.AutoScrollMinSize = new System.Drawing.Size(1, 1);
            this.treeImageView1.BackColor = System.Drawing.Color.White;
            this.treeImageView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeImageView1.Location = new System.Drawing.Point(0, 0);
            this.treeImageView1.Name = "treeImageView1";
            this.treeImageView1.Size = new System.Drawing.Size(312, 398);
            this.treeImageView1.TabIndex = 5;
            this.treeImageView1.NodeMouseClick += new System.EventHandler<CascadeControl.Views.CascadeNodeMouseEventArgs>(this.TreeImageView1_NodeMouseClick);
            this.treeImageView1.NodeMouseDoubleClick += new System.EventHandler<CascadeControl.Views.CascadeNodeMouseEventArgs>(this.TreeImageView1_NodeMouseDoubleClick);
            this.treeImageView1.NodeActivated += new System.EventHandler<CascadeControl.State.CascadeNodeEventArgs>(this.CascadeView1_NodeActivated);
            // 
            // txtPath
            // 
            this.txtPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPath.Location = new System.Drawing.Point(12, 12);
            this.txtPath.Name = "txtPath";
            this.txtPath.ReadOnly = true;
            this.txtPath.Size = new System.Drawing.Size(659, 22);
            this.txtPath.TabIndex = 8;
            // 
            // TreeSelector
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(683, 450);
            this.Controls.Add(this.txtPath);
            this.Controls.Add(this.splitContainer2);
            this.Name = "TreeSelector";
            this.Text = "Tree Selector";
            this.splitContainer2.Panel1.ResumeLayout(false);
            this.splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).EndInit();
            this.splitContainer2.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.SplitContainer splitContainer2;
        private CascadeControl.Views.TreeImage.CascadeTreeImageView treeImageView1;
        private CascadeControl.Views.CascadeView cascadeView1;
        private System.Windows.Forms.TextBox txtPath;
    }
}

