using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using CascadeControl.Views;
using CascadeControl.Views.Columns;
using CascadeControl.Views.Diagram;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlViewer
{
    public partial class TreeViewer : Form
    {
        private const int DRAW_LEVEL = 3;

        private MenuTree _menu = new MenuTree();

        /// <summary>
        /// 建立表單並載入預設的 <see cref="FilePath"/>（不存在時先產生範本）。
        /// </summary>
        public TreeViewer() : this(null, false)
        {
        }

        /// <summary>
        /// 建立表單並載入指定的 CSV；把 CSV 拖到 exe 上開啟時，檔案路徑由命令列傳進來。
        /// </summary>
        /// <param name="startupPath">要載入的 CSV；null 或空字串時載入預設的 <see cref="FilePath"/>（不存在時先產生範本）。</param>
        public TreeViewer(string startupPath, bool isReadonly)
        {
            InitializeComponent();
            diagramView1.State = cascadeView1.State;

            // Demo 預設開啟編輯；勾選 ReadOnly 切回唯讀（只能點選、多選與複製）。
            cascadeView1.ReadOnly = isReadonly;
            diagramView1.ReadOnly = isReadonly;
            cascadeView1.State.TreeEdited += State_TreeEdited;
            cascadeView1.MultiSelectionChanged += CascadeView1_MultiSelectionChanged;

            // 拖進表單的任何地方都能載入：子控制項各自接收拖放，事件轉給同一組處理常式。
            EnableFileDrop(this);

            if (string.IsNullOrEmpty(startupPath))
            {
                var _menu = new MenuTree();
                _menu.AddPath("root");
                cascadeView1.SetTree(_menu);
                cascadeView1.ReadOnly = false;
                diagramView1.ReadOnly = false;

                return;
            }

            if (!File.Exists(startupPath))
            {
                return;
            }

            LoadMenu(startupPath);
            return;
        }

        /// <summary>
        /// 載入 CSV 並換上新的選單樹。<see cref="MenuTreeCsvFile.Load"/> 會把每格的 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 展開成兄弟節點。
        /// </summary>
        private void LoadMenu(string filePath)
        {
            _menu = MenuTreeCsvFile.Load(filePath, out IList<string> warnings);

            foreach (var warning in warnings) LogMessage($"警告：{warning}");
            LogMessage($"已載入 {filePath}：{_menu.GetLeaves().Count()} 條路徑，最深 {_menu.Height} 層。");
            Text = $"TestForm - {Path.GetFileName(filePath)}";

            // SetTree 觸發 SelectedPathChanged，diagramView1 會跟著重畫新的樹。
            cascadeView1.SetTree(_menu);
            cascadeView1.ColumnWidth = 40;
            cascadeView1.ColumnLayoutMode = CascadeColumnLayoutMode.Sliding;
            //cascadeView1.DisplayColumnCount = _menu.Height;
            cascadeView1.DisplayColumnCount = DRAW_LEVEL;

            // 從建構式呼叫時表單還沒顯示，這一行不會有作用，由 OnShown 補做。
            cascadeView1.FocusActiveColumn();

            diagramView1.DrawScope = TreeDrawScope.WholeTree;
            diagramView1.UncheckedDisplay = UncheckedNodeDisplay.Dimmed;
        }

        #region 拖放載入

        /// <summary>
        /// 讓 <paramref name="control"/> 與它底下的所有子控制項都能接收拖進來的檔案。
        /// </summary>
        /// <remarks>
        /// 拖放事件不會從子控制項往上傳到表單，只設表單的 AllowDrop，拖到選單或樹狀圖上時游標會是禁止符號。
        /// <see cref="CascadeColumnsView"/> 的欄位 ListBox 是之後才動態建立的，所以也接 ControlAdded，新加入的子控制項一樣能接收。
        /// </remarks>
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

            // 在拖放事件裡跳出訊息框會讓來源的檔案總管卡住，等拖放結束再載入。
            BeginInvoke(new Action(() =>
            {
                try
                {
                    LoadMenu(filePath);
                    Activate();
                    cascadeView1.FocusActiveColumn();
                }
                catch (Exception ex)
                {
                    LogMessage($"載入失敗：{filePath}：{ex.Message}");
                }
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

        #endregion 拖放載入

        /// <summary>
        /// 表單顯示後把焦點放進選單的操作欄，一啟動就能直接用方向鍵。
        /// </summary>
        /// <remarks>
        /// 不能在建構式做：那時表單還沒顯示，控制項無法取得焦點；顯示時 WinForms 會把焦點交給 TabIndex 最小的控制項。
        /// </remarks>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            cascadeView1.FocusActiveColumn();
        }

        private void LogMessage(string message)
        {
            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }

        #region 事件處理

        private void CascadeView1_SelectedPathChanged(object sender, CascadeNodeEventArgs e)
        {
            // 樹狀圖由 diagramView1 自己跟著選取重畫。

            if (e.Node == null)
            {
                LogMessage($"SelectedPathChanged：未選取，第一欄 {e.Children.Count} 項。");
                return;
            }

            LogMessage($"SelectedPathChanged：{cascadeView1.SelectedPathText}"
                + $"｜ColumnIndex={e.ColumnIndex}"
                + $"｜Parent={e.Parent?.Name ?? "(無)"}"
                + $"｜Children={e.Children.Count}"
                + $"｜IsLeaf={e.IsLeaf}");
        }

        private void CascadeView1_NodeActivated(object sender, CascadeNodeEventArgs e)
        {
            ExecuteSelection((e.IsLeaf ? "Leaf" : "Node") + " Activated");
        }

        /// <summary>
        /// 點了右側樹狀圖上的節點：左鍵讓選單跳到那個節點，左右鍵都把路徑寫進記錄。
        /// </summary>
        private void DiagramView1_NodeMouseClick(object sender, CascadeNodeMouseEventArgs e)
        {
            LogMessage($"Diagram NodeMouseClick（{e.Button}）：{e.PathText}");

            if (e.Button == MouseButtons.Left) cascadeView1.TrySelectPath(e.Path);
        }

        private void DiagramView1_NodeMouseDoubleClick(object sender, CascadeNodeMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleImageNodeCheck(e.Node as MenuNode);
        }

        /// <summary>
        /// 在右側樹狀圖上按 Enter：和左鍵雙擊一樣切換勾選。
        /// </summary>
        private void DiagramView1_NodeActivated(object sender, CascadeNodeEventArgs e)
        {
            ToggleImageNodeCheck(e.Node as MenuNode);
        }

        // 透過 cascadeView1 的 CheckSet 切換：會記進同一份 Undo/Redo，並觸發 CheckedChanged、兩邊一起重畫。
        private void ToggleImageNodeCheck(MenuNode node)
        {
            var checkSet = cascadeView1.CheckSet;
            if (node == null || checkSet == null || !checkSet.Contains(node)) return;

            checkSet.Toggle(node);
        }

        private void CascadeView1_CheckedChanged(object sender, MenuCheckChangedEventArgs e)
        {
            // 樹狀圖由 diagramView1 自己跟著勾選重畫。
            LogMessage($"CheckedChanged：{e.Reason}"
                + $"｜Target={e.Target?.FullPathText ?? "(整棵樹)"}"
                + $"｜+{e.Added.Count} / -{e.Removed.Count}"
                + $"｜共勾選 {cascadeView1.CheckSet.Count} 個"
                + $"｜Undo={cascadeView1.State.History.UndoCount} Redo={cascadeView1.State.History.RedoCount}");
        }

        #endregion 事件處理

        /// <summary>
        /// Start 按鈕與 NodeActivated 共用的執行入口，確保兩條觸發途徑的結果一致。
        /// </summary>
        /// <remarks>兩條途徑觸發時，事件參數的 Path 都等於當下的選取路徑，所以直接取控制項的選取即可。</remarks>
        private void ExecuteSelection(string trigger)
        {
            LogMessage($"{trigger}：執行 {cascadeView1.SelectedPathText}");
        }

        #region 編輯（按鈕轉呼叫控制項的編輯指令；右鍵功能表與快捷鍵由控制項自己處理）

        /// <summary>
        /// 樹被編輯之後（功能表、快捷鍵或按鈕都會走到這裡）：寫記錄；樹狀圖已經由共用的 State 通知重畫，被移除節點的勾選也已經由 State 取消。
        /// </summary>
        private void State_TreeEdited(object sender, CascadeTreeEditedEventArgs e)
        {
            // 剪下與刪除的節點已經脫離樹，FullPathText 是空的，改寫名稱。
            var nodes = string.Join("、", e.Nodes.Select(node => node.Parent == null ? node.Name : node.FullPathText));
            LogMessage($"TreeEdited：{e.Action}｜{nodes}");
        }

        private void CascadeView1_MultiSelectionChanged(object sender, EventArgs e)
        {
            var nodes = cascadeView1.MultiSelectedNodes;
            LogMessage(nodes.Count == 0
                ? "MultiSelectionChanged：取消多選"
                : $"MultiSelectionChanged：第 {cascadeView1.MultiSelectedColumnIndex + 1} 欄 {nodes.Count} 個｜{string.Join("、", nodes.Select(node => node.Name))}");
        }

        #endregion 編輯（按鈕轉呼叫控制項的編輯指令；右鍵功能表與快捷鍵由控制項自己處理）
    }
}