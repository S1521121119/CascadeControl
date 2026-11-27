using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MindMap
{
    public partial class MainForm : Form
    {
        private const string ROOT_NAME = "Root";
        private const string FILE_FILTER = "CSV (*.csv)|*.csv|All files (*.*)|*.*";

        // 目前文件：存檔路徑（新文件為 null）與是否有未存的變更。
        private string _filePath;
        private bool _dirty;

        public MainForm() : this(null)
        {
        }

        /// <param name="startupPath">要開啟的 CSV；null、空字串或檔案不存在時開一份新文件。</param>
        public MainForm(string startupPath)
        {
            InitializeComponent();
            Icon = Properties.Resources.MindMap;
            diagramView1.State.TreeEdited += State_TreeEdited;
            diagramView1.DrawSettingsChanged += (sender, e) => UpdateZoomStatus();
            EnableFileDrop(this);

            if (!string.IsNullOrEmpty(startupPath) && File.Exists(startupPath)) OpenFile(startupPath);
            else NewDocument();

            UpdateZoomStatus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && !ConfirmDiscardChanges()) e.Cancel = true;
        }

        #region 文件

        private void NewDocument()
        {
            var tree = new MenuTree();
            var root = tree.AddPath(ROOT_NAME);
            LoadTree(tree, null);
            diagramView1.TrySelectNode(root);
        }

        private void OpenFile(string filePath)
        {
            var tree = MenuTreeCsvFile.Load(filePath, out IList<string> warnings);
            if (warnings.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, warnings), "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            LoadTree(tree, filePath);
        }

        private void LoadTree(MenuTree tree, string filePath)
        {
            diagramView1.SetTree(tree);
            _filePath = filePath;
            _dirty = false;
            UpdateTitle();
            UpdateTreeStatus();
        }

        /// <returns>有存檔時為 true；使用者取消另存對話框或寫檔失敗時為 false。</returns>
        private bool Save(bool saveAs)
        {
            diagramView1.EndEdit(commit: true);

            var filePath = _filePath;
            if (saveAs || filePath == null)
            {
                using (var dialog = new SaveFileDialog { Filter = FILE_FILTER, FileName = Path.GetFileName(filePath ?? "MindMap.csv") })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    filePath = dialog.FileName;
                }
            }

            try
            {
                MenuTreeCsvFile.Save(diagramView1.Tree, filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _filePath = filePath;
            _dirty = false;
            UpdateTitle();
            return true;
        }

        /// <summary>
        /// 有未存的變更時詢問是否存檔。
        /// </summary>
        /// <returns>可以丟掉目前文件（已存檔、選擇不存或本來就沒有變更）時為 true；選擇取消或存檔失敗時為 false。</returns>
        private bool ConfirmDiscardChanges()
        {
            if (!_dirty) return true;

            switch (MessageBox.Show(this, $"Save changes to {DocumentName}?", "MindMap", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes: return Save(saveAs: false);
                case DialogResult.No: return true;
                default: return false;
            }
        }

        private string DocumentName => _filePath == null ? "Untitled" : Path.GetFileName(_filePath);

        private void UpdateTitle()
        {
            Text = $"{DocumentName}{(_dirty ? " *" : string.Empty)} - MindMap";
        }

        private void State_TreeEdited(object sender, CascadeTreeEditedEventArgs e)
        {
            _dirty = true;
            UpdateTitle();
            UpdateTreeStatus();
        }

        #endregion 文件

        #region 拖放開啟

        /// <summary>
        /// 讓 <paramref name="control"/> 與它底下的所有子控制項都能接收拖進來的 CSV。
        /// </summary>
        /// <remarks>拖放事件不會從子控制項往上傳到表單，所以每個子控制項都要接；之後才加入的子控制項（例如編輯框）也接。</remarks>
        private void EnableFileDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += Form_DragEnter;
            control.DragDrop += Form_DragDrop;
            control.ControlAdded += (sender, e) => EnableFileDrop(e.Control);

            foreach (Control child in control.Controls) EnableFileDrop(child);
        }

        private void Form_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = TryGetDroppedCsv(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Form_DragDrop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedCsv(e.Data, out string filePath)) return;

            // 在拖放事件裡跳出訊息框會讓來源的檔案總管卡住，等拖放結束再詢問與載入。
            BeginInvoke(new Action(() =>
            {
                Activate();
                if (ConfirmDiscardChanges()) OpenFile(filePath);
            }));
        }

        /// <summary>
        /// 拖進來的是不是一個 .csv 檔；拖了多個檔案時取第一個 .csv。
        /// </summary>
        private static bool TryGetDroppedCsv(IDataObject data, out string filePath)
        {
            filePath = (data?.GetData(DataFormats.FileDrop) as string[])?
                .FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
            return filePath != null;
        }

        #endregion 拖放開啟

        #region 狀態列

        private void DiagramView1_SelectedPathChanged(object sender, CascadeNodeEventArgs e) => UpdateTreeStatus();

        private void DiagramView1_MultiSelectionChanged(object sender, EventArgs e) => UpdateTreeStatus();

        private void UpdateTreeStatus()
        {
            var tree = diagramView1.Tree;
            var nodeCount = tree == null ? 0 : CountDescendants(tree.Root);
            var multiCount = diagramView1.MultiSelectedNodes.Count;
            var selection = multiCount > 0 ? $"{multiCount} nodes"
                : diagramView1.SelectedNode == null ? "(none)"
                : diagramView1.SelectedPathText;

            lblTreeInfo.Text = $"Nodes: {nodeCount}    Levels: {tree?.Height ?? 0}    Selected: {selection}";
        }

        private static int CountDescendants(MenuNode node) => node.Children.Sum(child => 1 + CountDescendants(child));

        private void UpdateZoomStatus()
        {
            lblZoom.Text = $"{diagramView1.ZoomPercent}%";
        }

        #endregion 狀態列

        #region 功能表：File

        private void MnuFileNew_Click(object sender, EventArgs e)
        {
            if (ConfirmDiscardChanges()) NewDocument();
        }

        private void MnuFileOpen_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscardChanges()) return;

            using (var dialog = new OpenFileDialog { Filter = FILE_FILTER })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenFile(dialog.FileName);
            }
        }

        private void MnuFileSave_Click(object sender, EventArgs e) => Save(saveAs: false);

        private void MnuFileSaveAs_Click(object sender, EventArgs e) => Save(saveAs: true);

        private void MnuFileExit_Click(object sender, EventArgs e) => Close();

        #endregion 功能表：File

        #region 功能表：Edit

        // Edit 的快捷鍵由圖自己處理（功能表只顯示文字），所以停用項目不會擋到按鍵。
        private void MnuEdit_DropDownOpening(object sender, EventArgs e)
        {
            var hasTarget = diagramView1.SelectedNode != null || diagramView1.MultiSelectedNodes.Count > 0;

            mnuEditUndo.Enabled = diagramView1.CanUndo;
            mnuEditRedo.Enabled = diagramView1.CanRedo;
            mnuEditCut.Enabled = hasTarget;
            mnuEditCopy.Enabled = hasTarget;
            mnuEditDelete.Enabled = hasTarget;
            mnuEditInsert.Enabled = diagramView1.SelectedNode != null;
        }

        private void MnuEditUndo_Click(object sender, EventArgs e) => diagramView1.Undo();

        private void MnuEditRedo_Click(object sender, EventArgs e) => diagramView1.Redo();

        private void MnuEditCut_Click(object sender, EventArgs e) => diagramView1.Cut();

        private void MnuEditCopy_Click(object sender, EventArgs e) => diagramView1.Copy();

        private void MnuEditPaste_Click(object sender, EventArgs e) => diagramView1.Paste(before: false);

        private void MnuEditDelete_Click(object sender, EventArgs e) => diagramView1.Delete();

        private void MnuEditSelectAll_Click(object sender, EventArgs e) => diagramView1.SelectAll();

        private void MnuEditInsert_Click(object sender, EventArgs e) => diagramView1.BeginInsert(before: false);

        #endregion 功能表：Edit

        #region 功能表：View

        private void MnuView_DropDownOpening(object sender, EventArgs e)
        {
            mnuViewShowIndex.Checked = diagramView1.ShowIndex;
            mnuViewShowChildCount.Checked = diagramView1.ShowChildCount;
        }

        private void MnuViewZoomIn_Click(object sender, EventArgs e) => diagramView1.ZoomIn();

        private void MnuViewZoomOut_Click(object sender, EventArgs e) => diagramView1.ZoomOut();

        private void MnuViewResetZoom_Click(object sender, EventArgs e) => diagramView1.ResetZoom();

        private void MnuFitToWindow_Click(object sender, EventArgs e) => diagramView1.FitToWindow();

        private void MnuViewShowIndex_Click(object sender, EventArgs e) => diagramView1.ShowIndex = !diagramView1.ShowIndex;

        private void MnuViewShowChildCount_Click(object sender, EventArgs e) => diagramView1.ShowChildCount = !diagramView1.ShowChildCount;

        #endregion 功能表：View
    }
}
