namespace MindMap
{
    partial class MainForm
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
            this.msMenu = new System.Windows.Forms.MenuStrip();
            this.mnuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileNew = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileOpen = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileSave = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileSaveAs = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFileSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuFileExit = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditUndo = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditRedo = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuEditCut = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditPaste = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditSelectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuEditSep2 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuEditInsert = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuView = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewZoomIn = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewZoomOut = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewResetZoom = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuFitToWindow = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuViewShowIndex = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuViewShowChildCount = new System.Windows.Forms.ToolStripMenuItem();
            this.ssStatus = new System.Windows.Forms.StatusStrip();
            this.lblTreeInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblZoom = new System.Windows.Forms.ToolStripStatusLabel();
            this.diagramView1 = new MindMap.MindMapDiagramView();
            this.msMenu.SuspendLayout();
            this.ssStatus.SuspendLayout();
            this.SuspendLayout();
            // 
            // msMenu
            // 
            this.msMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.msMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuFile,
            this.mnuEdit,
            this.mnuView});
            this.msMenu.Location = new System.Drawing.Point(0, 0);
            this.msMenu.Name = "msMenu";
            this.msMenu.Padding = new System.Windows.Forms.Padding(4, 1, 0, 1);
            this.msMenu.Size = new System.Drawing.Size(784, 24);
            this.msMenu.TabIndex = 0;
            // 
            // mnuFile
            // 
            this.mnuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuFileNew,
            this.mnuFileOpen,
            this.mnuFileSave,
            this.mnuFileSaveAs,
            this.mnuFileSep1,
            this.mnuFileExit});
            this.mnuFile.Name = "mnuFile";
            this.mnuFile.Size = new System.Drawing.Size(37, 22);
            this.mnuFile.Text = "&File";
            // 
            // mnuFileNew
            // 
            this.mnuFileNew.Name = "mnuFileNew";
            this.mnuFileNew.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N)));
            this.mnuFileNew.Size = new System.Drawing.Size(195, 22);
            this.mnuFileNew.Text = "&New";
            this.mnuFileNew.Click += new System.EventHandler(this.MnuFileNew_Click);
            // 
            // mnuFileOpen
            // 
            this.mnuFileOpen.Name = "mnuFileOpen";
            this.mnuFileOpen.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.mnuFileOpen.Size = new System.Drawing.Size(195, 22);
            this.mnuFileOpen.Text = "&Open...";
            this.mnuFileOpen.Click += new System.EventHandler(this.MnuFileOpen_Click);
            // 
            // mnuFileSave
            // 
            this.mnuFileSave.Name = "mnuFileSave";
            this.mnuFileSave.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this.mnuFileSave.Size = new System.Drawing.Size(195, 22);
            this.mnuFileSave.Text = "&Save";
            this.mnuFileSave.Click += new System.EventHandler(this.MnuFileSave_Click);
            // 
            // mnuFileSaveAs
            // 
            this.mnuFileSaveAs.Name = "mnuFileSaveAs";
            this.mnuFileSaveAs.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.S)));
            this.mnuFileSaveAs.Size = new System.Drawing.Size(195, 22);
            this.mnuFileSaveAs.Text = "Save &As...";
            this.mnuFileSaveAs.Click += new System.EventHandler(this.MnuFileSaveAs_Click);
            // 
            // mnuFileSep1
            // 
            this.mnuFileSep1.Name = "mnuFileSep1";
            this.mnuFileSep1.Size = new System.Drawing.Size(192, 6);
            // 
            // mnuFileExit
            // 
            this.mnuFileExit.Name = "mnuFileExit";
            this.mnuFileExit.Size = new System.Drawing.Size(195, 22);
            this.mnuFileExit.Text = "E&xit";
            this.mnuFileExit.Click += new System.EventHandler(this.MnuFileExit_Click);
            // 
            // mnuEdit
            // 
            this.mnuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuEditUndo,
            this.mnuEditRedo,
            this.mnuEditSep1,
            this.mnuEditCut,
            this.mnuEditCopy,
            this.mnuEditPaste,
            this.mnuEditDelete,
            this.mnuEditSelectAll,
            this.mnuEditSep2,
            this.mnuEditInsert});
            this.mnuEdit.Name = "mnuEdit";
            this.mnuEdit.Size = new System.Drawing.Size(39, 22);
            this.mnuEdit.Text = "&Edit";
            this.mnuEdit.DropDownOpening += new System.EventHandler(this.MnuEdit_DropDownOpening);
            // 
            // mnuEditUndo
            // 
            this.mnuEditUndo.Name = "mnuEditUndo";
            this.mnuEditUndo.ShortcutKeyDisplayString = "Ctrl+Z";
            this.mnuEditUndo.Size = new System.Drawing.Size(164, 22);
            this.mnuEditUndo.Text = "&Undo";
            this.mnuEditUndo.Click += new System.EventHandler(this.MnuEditUndo_Click);
            // 
            // mnuEditRedo
            // 
            this.mnuEditRedo.Name = "mnuEditRedo";
            this.mnuEditRedo.ShortcutKeyDisplayString = "Ctrl+Y";
            this.mnuEditRedo.Size = new System.Drawing.Size(164, 22);
            this.mnuEditRedo.Text = "&Redo";
            this.mnuEditRedo.Click += new System.EventHandler(this.MnuEditRedo_Click);
            // 
            // mnuEditSep1
            // 
            this.mnuEditSep1.Name = "mnuEditSep1";
            this.mnuEditSep1.Size = new System.Drawing.Size(161, 6);
            // 
            // mnuEditCut
            // 
            this.mnuEditCut.Name = "mnuEditCut";
            this.mnuEditCut.ShortcutKeyDisplayString = "Ctrl+X";
            this.mnuEditCut.Size = new System.Drawing.Size(164, 22);
            this.mnuEditCut.Text = "Cu&t";
            this.mnuEditCut.Click += new System.EventHandler(this.MnuEditCut_Click);
            // 
            // mnuEditCopy
            // 
            this.mnuEditCopy.Name = "mnuEditCopy";
            this.mnuEditCopy.ShortcutKeyDisplayString = "Ctrl+C";
            this.mnuEditCopy.Size = new System.Drawing.Size(164, 22);
            this.mnuEditCopy.Text = "&Copy";
            this.mnuEditCopy.Click += new System.EventHandler(this.MnuEditCopy_Click);
            // 
            // mnuEditPaste
            // 
            this.mnuEditPaste.Name = "mnuEditPaste";
            this.mnuEditPaste.ShortcutKeyDisplayString = "Ctrl+V";
            this.mnuEditPaste.Size = new System.Drawing.Size(164, 22);
            this.mnuEditPaste.Text = "&Paste";
            this.mnuEditPaste.Click += new System.EventHandler(this.MnuEditPaste_Click);
            // 
            // mnuEditDelete
            // 
            this.mnuEditDelete.Name = "mnuEditDelete";
            this.mnuEditDelete.ShortcutKeyDisplayString = "Del";
            this.mnuEditDelete.Size = new System.Drawing.Size(164, 22);
            this.mnuEditDelete.Text = "&Delete";
            this.mnuEditDelete.Click += new System.EventHandler(this.MnuEditDelete_Click);
            // 
            // mnuEditSelectAll
            // 
            this.mnuEditSelectAll.Name = "mnuEditSelectAll";
            this.mnuEditSelectAll.ShortcutKeyDisplayString = "Ctrl+A";
            this.mnuEditSelectAll.Size = new System.Drawing.Size(164, 22);
            this.mnuEditSelectAll.Text = "Select &All";
            this.mnuEditSelectAll.Click += new System.EventHandler(this.MnuEditSelectAll_Click);
            // 
            // mnuEditSep2
            // 
            this.mnuEditSep2.Name = "mnuEditSep2";
            this.mnuEditSep2.Size = new System.Drawing.Size(161, 6);
            // 
            // mnuEditInsert
            // 
            this.mnuEditInsert.Name = "mnuEditInsert";
            this.mnuEditInsert.ShortcutKeyDisplayString = "Enter";
            this.mnuEditInsert.Size = new System.Drawing.Size(164, 22);
            this.mnuEditInsert.Text = "&Insert";
            this.mnuEditInsert.Click += new System.EventHandler(this.MnuEditInsert_Click);
            // 
            // mnuView
            // 
            this.mnuView.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuViewZoomIn,
            this.mnuViewZoomOut,
            this.mnuViewResetZoom,
            this.mnuFitToWindow,
            this.mnuViewSep1,
            this.mnuViewShowIndex,
            this.mnuViewShowChildCount});
            this.mnuView.Name = "mnuView";
            this.mnuView.Size = new System.Drawing.Size(44, 22);
            this.mnuView.Text = "&View";
            this.mnuView.DropDownOpening += new System.EventHandler(this.MnuView_DropDownOpening);
            // 
            // mnuViewZoomIn
            // 
            this.mnuViewZoomIn.Name = "mnuViewZoomIn";
            this.mnuViewZoomIn.ShortcutKeyDisplayString = "Ctrl + +";
            this.mnuViewZoomIn.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Oemplus)));
            this.mnuViewZoomIn.Size = new System.Drawing.Size(225, 22);
            this.mnuViewZoomIn.Text = "Zoom &In";
            this.mnuViewZoomIn.Click += new System.EventHandler(this.MnuViewZoomIn_Click);
            // 
            // mnuViewZoomOut
            // 
            this.mnuViewZoomOut.Name = "mnuViewZoomOut";
            this.mnuViewZoomOut.ShortcutKeyDisplayString = "Ctrl + -";
            this.mnuViewZoomOut.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.OemMinus)));
            this.mnuViewZoomOut.Size = new System.Drawing.Size(225, 22);
            this.mnuViewZoomOut.Text = "Zoom &Out";
            this.mnuViewZoomOut.Click += new System.EventHandler(this.MnuViewZoomOut_Click);
            // 
            // mnuViewResetZoom
            // 
            this.mnuViewResetZoom.Name = "mnuViewResetZoom";
            this.mnuViewResetZoom.ShortcutKeyDisplayString = "Ctrl + 0";
            this.mnuViewResetZoom.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.NumPad0)));
            this.mnuViewResetZoom.Size = new System.Drawing.Size(225, 22);
            this.mnuViewResetZoom.Text = "&Reset Zoom";
            this.mnuViewResetZoom.Click += new System.EventHandler(this.MnuViewResetZoom_Click);
            // 
            // mnuFitToWindow
            // 
            this.mnuFitToWindow.Name = "mnuFitToWindow";
            this.mnuFitToWindow.ShortcutKeyDisplayString = "Ctrl + Alt + 0";
            this.mnuFitToWindow.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt) 
            | System.Windows.Forms.Keys.NumPad0)));
            this.mnuFitToWindow.Size = new System.Drawing.Size(225, 22);
            this.mnuFitToWindow.Text = "&Fit To Window";
            this.mnuFitToWindow.Click += new System.EventHandler(this.MnuFitToWindow_Click);
            // 
            // mnuViewSep1
            // 
            this.mnuViewSep1.Name = "mnuViewSep1";
            this.mnuViewSep1.Size = new System.Drawing.Size(222, 6);
            // 
            // mnuViewShowIndex
            // 
            this.mnuViewShowIndex.Name = "mnuViewShowIndex";
            this.mnuViewShowIndex.Size = new System.Drawing.Size(225, 22);
            this.mnuViewShowIndex.Text = "&Show Index";
            this.mnuViewShowIndex.Click += new System.EventHandler(this.MnuViewShowIndex_Click);
            // 
            // mnuViewShowChildCount
            // 
            this.mnuViewShowChildCount.Name = "mnuViewShowChildCount";
            this.mnuViewShowChildCount.Size = new System.Drawing.Size(225, 22);
            this.mnuViewShowChildCount.Text = "Show &Child Count";
            this.mnuViewShowChildCount.Click += new System.EventHandler(this.MnuViewShowChildCount_Click);
            // 
            // ssStatus
            // 
            this.ssStatus.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.ssStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblTreeInfo,
            this.lblZoom});
            this.ssStatus.Location = new System.Drawing.Point(0, 537);
            this.ssStatus.Name = "ssStatus";
            this.ssStatus.Size = new System.Drawing.Size(784, 24);
            this.ssStatus.TabIndex = 2;
            // 
            // lblTreeInfo
            // 
            this.lblTreeInfo.Name = "lblTreeInfo";
            this.lblTreeInfo.Size = new System.Drawing.Size(730, 19);
            this.lblTreeInfo.Spring = true;
            this.lblTreeInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblZoom
            // 
            this.lblZoom.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.lblZoom.Name = "lblZoom";
            this.lblZoom.Size = new System.Drawing.Size(39, 19);
            this.lblZoom.Text = "100%";
            // 
            // diagramView1
            // 
            this.diagramView1.AutoScroll = true;
            this.diagramView1.AutoScrollMinSize = new System.Drawing.Size(1, 1);
            this.diagramView1.BackColor = System.Drawing.Color.White;
            this.diagramView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.diagramView1.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.diagramView1.Location = new System.Drawing.Point(0, 24);
            this.diagramView1.Name = "diagramView1";
            this.diagramView1.ReadOnly = false;
            this.diagramView1.Size = new System.Drawing.Size(784, 513);
            this.diagramView1.TabIndex = 1;
            this.diagramView1.SelectedPathChanged += new System.EventHandler<CascadeControl.State.CascadeNodeEventArgs>(this.DiagramView1_SelectedPathChanged);
            this.diagramView1.MultiSelectionChanged += new System.EventHandler(this.DiagramView1_MultiSelectionChanged);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.diagramView1);
            this.Controls.Add(this.ssStatus);
            this.Controls.Add(this.msMenu);
            this.MainMenuStrip = this.msMenu;
            this.Name = "MainForm";
            this.Text = "MindMap";
            this.msMenu.ResumeLayout(false);
            this.msMenu.PerformLayout();
            this.ssStatus.ResumeLayout(false);
            this.ssStatus.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip msMenu;
        private System.Windows.Forms.ToolStripMenuItem mnuFile;
        private System.Windows.Forms.ToolStripMenuItem mnuFileNew;
        private System.Windows.Forms.ToolStripMenuItem mnuFileOpen;
        private System.Windows.Forms.ToolStripMenuItem mnuFileSave;
        private System.Windows.Forms.ToolStripMenuItem mnuFileSaveAs;
        private System.Windows.Forms.ToolStripSeparator mnuFileSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuFileExit;
        private System.Windows.Forms.ToolStripMenuItem mnuEdit;
        private System.Windows.Forms.ToolStripMenuItem mnuEditUndo;
        private System.Windows.Forms.ToolStripMenuItem mnuEditRedo;
        private System.Windows.Forms.ToolStripSeparator mnuEditSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuEditCut;
        private System.Windows.Forms.ToolStripMenuItem mnuEditCopy;
        private System.Windows.Forms.ToolStripMenuItem mnuEditPaste;
        private System.Windows.Forms.ToolStripMenuItem mnuEditDelete;
        private System.Windows.Forms.ToolStripMenuItem mnuEditSelectAll;
        private System.Windows.Forms.ToolStripSeparator mnuEditSep2;
        private System.Windows.Forms.ToolStripMenuItem mnuEditInsert;
        private System.Windows.Forms.ToolStripMenuItem mnuView;
        private System.Windows.Forms.ToolStripMenuItem mnuViewZoomIn;
        private System.Windows.Forms.ToolStripMenuItem mnuViewZoomOut;
        private System.Windows.Forms.ToolStripMenuItem mnuViewResetZoom;
        private System.Windows.Forms.ToolStripSeparator mnuViewSep1;
        private System.Windows.Forms.ToolStripMenuItem mnuViewShowIndex;
        private System.Windows.Forms.ToolStripMenuItem mnuViewShowChildCount;
        private System.Windows.Forms.StatusStrip ssStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblTreeInfo;
        private System.Windows.Forms.ToolStripStatusLabel lblZoom;
        private MindMap.MindMapDiagramView diagramView1;
        private System.Windows.Forms.ToolStripMenuItem mnuFitToWindow;
    }
}
