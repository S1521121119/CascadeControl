using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CascadeControl.Views.Columns
{
    // 編輯：唯讀切換、內建快顯功能表、就地編輯框、Windows 剪貼簿，以及插入／建立子節點／改名／複製／剪下／貼上／刪除。
    public partial class CascadeColumnsView
    {
        /// <summary>
        /// 複製節點時放到 Windows 剪貼簿上的自訂格式名稱；內容是 UTF-8 的 CSV 文字（<see cref="MenuRowStyle.Expanded"/>，含表頭）。
        /// </summary>
        /// <remarks>
        /// 同時也會放一份純文字，可以直接貼到記事本或 Excel；但貼回控制項時只認這個格式，
        /// 剪貼簿上隨便一段文字不會被當成節點貼進來。
        /// </remarks>
        public const string CLIPBOARD_FORMAT = "CascadeControl.MenuNodes";

        private const int CLIPBOARD_RETRY_TIMES = 5;
        private const int CLIPBOARD_RETRY_DELAY = 100;

        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        // 同一個 ToolStripItem 只能放在一個功能表裡，所以兩個功能表各有一組復原、重做。

        private readonly ToolStripMenuItem _undoItem = new ToolStripMenuItem("復原") { ShortcutKeyDisplayString = "Ctrl+Z" };
        private readonly ToolStripMenuItem _redoItem = new ToolStripMenuItem("重做") { ShortcutKeyDisplayString = "Ctrl+Y" };
        private readonly ToolStripMenuItem _columnUndoItem = new ToolStripMenuItem("復原") { ShortcutKeyDisplayString = "Ctrl+Z" };
        private readonly ToolStripMenuItem _columnRedoItem = new ToolStripMenuItem("重做") { ShortcutKeyDisplayString = "Ctrl+Y" };

        private readonly ToolStripMenuItem _insertItem = new ToolStripMenuItem("插入");
        private readonly ToolStripMenuItem _insertBeforeItem = new ToolStripMenuItem("之前") { ShortcutKeyDisplayString = "Ctrl+Shift+I" };
        private readonly ToolStripMenuItem _insertNextItem = new ToolStripMenuItem("之後") { ShortcutKeyDisplayString = "Ctrl+I" };
        private readonly ToolStripMenuItem _createChildItem = new ToolStripMenuItem("建立子節點") { ShortcutKeyDisplayString = "Ctrl+N" };
        private readonly ToolStripMenuItem _renameItem = new ToolStripMenuItem("重新命名") { ShortcutKeyDisplayString = "F2" };
        private readonly ToolStripMenuItem _copyItem = new ToolStripMenuItem("複製") { ShortcutKeyDisplayString = "Ctrl+C" };
        private readonly ToolStripMenuItem _copyPathItem = new ToolStripMenuItem("複製(路徑)") { ShortcutKeyDisplayString = "Ctrl+Shift+C" };
        private readonly ToolStripMenuItem _cutItem = new ToolStripMenuItem("剪下") { ShortcutKeyDisplayString = "Ctrl+X" };
        private readonly ToolStripMenuItem _pasteItem = new ToolStripMenuItem("貼上");
        private readonly ToolStripMenuItem _pasteBeforeItem = new ToolStripMenuItem("之前") { ShortcutKeyDisplayString = "Ctrl+Shift+V" };
        private readonly ToolStripMenuItem _pasteNextItem = new ToolStripMenuItem("之後") { ShortcutKeyDisplayString = "Ctrl+V" };
        private readonly ToolStripMenuItem _pasteChildItem = new ToolStripMenuItem("作為子節點") { ShortcutKeyDisplayString = "Ctrl+Alt+V" };
        private readonly ToolStripMenuItem _deleteItem = new ToolStripMenuItem("刪除") { ShortcutKeyDisplayString = "Del" };

        private readonly ToolStripMenuItem _columnInsertItem = new ToolStripMenuItem("插入");
        private readonly ToolStripMenuItem _columnPasteItem = new ToolStripMenuItem("貼上");
        private readonly ToolStripMenuItem _selectAllItem = new ToolStripMenuItem("全選") { ShortcutKeyDisplayString = "Ctrl+A" };

        private bool _readOnly = true;

        // 正在進行的就地編輯；沒有在編輯時為 null。
        private EditSession _edit;

        // Create Child 編輯期間，這個節點即使是葉節點也要多長出一欄放編輯框。
        private MenuNode _pendingChildParent;

        // 編輯中點了欄位：那一下只用來結束編輯，對應的放開也要略過。
        private bool _swallowMouseUp;

        #region 屬性與功能表

        /// <summary>
        /// 是否唯讀，預設為 true。
        /// </summary>
        /// <remarks>
        /// true：只能點選、多選與複製（右鍵功能表只有「複製」、「複製(路徑)」與欄位空白處的「全選」，快捷鍵只有 Ctrl+C、Ctrl+Shift+C、Ctrl+A），改到樹的步驟也不能復原或重做。
        /// false：右鍵功能表與快捷鍵可以插入、建立子節點、改名、剪下、貼上、刪除，以及復原、重做；樹是空的時也會留一個空欄，讓使用者在空白處插入第一個節點。
        /// 編輯只對 <see cref="SetTree"/> 顯示的 <see cref="MenuTree"/> 有作用；以 <see cref="SetTopLevelNodes"/> 顯示的資料一律唯讀。
        /// 改成 true 時，進行中的就地編輯會直接取消。
        /// </remarks>
        [Category("Cascade")]
        [Description("true：只能點選、多選與複製；false：右鍵功能表與快捷鍵可以插入、建立子節點、改名、剪下、貼上、刪除。只對 SetTree 顯示的樹有作用。")]
        [DefaultValue(true)]
        public bool ReadOnly
        {
            get => _readOnly;
            set
            {
                if (_readOnly == value) return;

                _readOnly = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// 復原／重做的歷史：使用者在這個控制項上對樹做的編輯（插入、建立子節點、改名、貼上、剪下、刪除）照順序記在這裡，
        /// <see cref="CheckBoxes"/> 為 true 時，勾選也記在同一條時間軸上；預設最多 <see cref="MenuHistory.DEFAULT_CAPACITY"/> 步。
        /// </summary>
        /// <remarks>
        /// 複製不改樹，不進歷史。<see cref="SetTree"/>、<see cref="SetTopLevelNodes"/>、<see cref="Clear"/> 會清空歷史。
        /// 在程式裡直接改 <see cref="MenuNode"/> 之後請呼叫 <see cref="MenuHistory.Clear"/>（或 <see cref="MenuCheckSet.Reconcile"/>，它也會清空）。
        /// 要調整保留的步驟數，設定 <see cref="MenuHistory.Capacity"/>。
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuHistory History { get; } = new MenuHistory();

        /// <summary>
        /// 現在能不能復原：<see cref="History"/> 有步驟，而且那一步改到樹時控制項不是唯讀（<see cref="ReadOnly"/> 為 false）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanUndo => History.CanUndo && (CanEdit || !History.UndoChangesTree);

        /// <summary>
        /// 現在能不能重做：<see cref="History"/> 有被復原的步驟，而且那一步改到樹時控制項不是唯讀。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanRedo => History.CanRedo && (CanEdit || !History.RedoChangesTree);

        /// <summary>
        /// 右鍵點節點時開啟的快顯功能表；可以加上自己的項目。
        /// </summary>
        /// <remarks>
        /// 右鍵不改選取，自己加的項目要用 <see cref="ContextMenuNode"/> 或 <see cref="ContextMenuNodes"/> 取得對象。
        /// 開啟前會依 <see cref="ReadOnly"/> 與對象決定內建項目的顯示與可用狀態，衍生類別可以在 <see cref="OnContextMenuOpening"/> 調整自己的項目；
        /// 沒有任何可見項目時不開啟。
        /// </remarks>
        [Browsable(false)]
        public ContextMenuStrip NodeContextMenu { get; } = new ContextMenuStrip();

        /// <summary>
        /// 右鍵點欄位空白處（最後一列下方）時開啟的快顯功能表；可以加上自己的項目。
        /// </summary>
        /// <remarks>
        /// 內建項目：復原、重做、插入、貼上（加在那一欄的最後）、全選（那一欄全部多選）。
        /// 自己加的項目用 <see cref="ContextMenuColumnIndex"/> 取得是哪一欄，<see cref="ContextMenuNode"/> 是那一欄的第一個節點（空欄時為 null）。
        /// 欄位排滿、沒有空白處時無法開啟，這時請把控制項拉高。
        /// </remarks>
        [Browsable(false)]
        public ContextMenuStrip ColumnContextMenu { get; } = new ContextMenuStrip();

        /// <summary>
        /// 最近一次開啟的快顯功能表針對的節點：<see cref="NodeContextMenu"/> 是被點的節點，
        /// <see cref="ColumnContextMenu"/> 是那一欄的第一個節點（代表那一層，空欄時為 null）。還沒開過時為 null。
        /// </summary>
        /// <remarks>開啟時記下來，之後選取即使改變也不影響。</remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuNode ContextMenuNode { get; private set; }

        /// <summary>
        /// 最近一次開啟的 <see cref="NodeContextMenu"/> 要操作的節點，依欄位中的順序排列：
        /// 右鍵點在多選的節點上時是全部多選的節點，否則只有被點的那一個。<see cref="ColumnContextMenu"/> 時為空集合。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<MenuNode> ContextMenuNodes { get; private set; } = NoMenuNodes;

        /// <summary>
        /// 最近一次開啟的快顯功能表是在哪一欄（0-based）；還沒開過時為 -1。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ContextMenuColumnIndex { get; private set; } = -1;

        /// <summary>
        /// 是否正在就地編輯節點名稱（插入、建立子節點、重新命名開啟的編輯框）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsEditing => _edit != null;

        /// <summary>
        /// 使用者透過功能表、快捷鍵或編輯框改了樹，或 <see cref="Undo"/>、<see cref="Redo"/> 退回或重做了改到樹的步驟之後觸發；選取與欄位已經更新完畢。
        /// </summary>
        /// <remarks>複製不改樹，所以不觸發。取消編輯、改名成原本的名稱、復原或重做只改勾選的步驟也不觸發。</remarks>
        [Category("Cascade")]
        public event EventHandler<CascadeTreeEditedEventArgs> TreeEdited;

        /// <summary>
        /// 觸發 <see cref="TreeEdited"/>。
        /// </summary>
        /// <remarks>
        /// <see cref="CheckBoxes"/> 為 true 時，剪下、刪除之後會先取消被移除節點（連同子孫）的勾選再觸發，和移除算同一步，復原時一起回來。
        /// </remarks>
        protected virtual void OnTreeEdited(CascadeTreeEditedEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            UncheckRemovedNodes(e);
            TreeEdited?.Invoke(this, e);
        }

        /// <summary>
        /// 內建快顯功能表開啟之前呼叫，內建項目的狀態已經設好；預設不做任何事。
        /// </summary>
        /// <param name="menu"><see cref="NodeContextMenu"/> 或 <see cref="ColumnContextMenu"/>。</param>
        /// <remarks>
        /// 衍生類別在這裡設定自己項目的 <see cref="ToolStripItem.Available"/>（不是 Visible：功能表還沒顯示時 Visible 一律讀到 false）。
        /// 之後基底會把多餘的分隔線藏起來，全部項目都不可見時就不開啟。
        /// </remarks>
        protected virtual void OnContextMenuOpening(ContextMenuStrip menu)
        {
        }

        // 在建構式呼叫。功能表動作一律等功能表關掉之後才執行：
        // 功能表關閉時可能把焦點還給原本的欄位，直接開編輯框的話，編輯框一拿到焦點就會被搶走而結束編輯。
        private void InitializeContextMenus()
        {
            _insertItem.DropDownItems.AddRange(new ToolStripItem[] { _insertBeforeItem, _insertNextItem });
            _pasteItem.DropDownItems.AddRange(new ToolStripItem[] { _pasteBeforeItem, _pasteNextItem, _pasteChildItem });

            NodeContextMenu.Items.AddRange(new ToolStripItem[]
            {
                _undoItem, _redoItem, new ToolStripSeparator(),
                _insertItem, _createChildItem, _renameItem, new ToolStripSeparator(),
                _copyItem, _copyPathItem, _cutItem, _pasteItem, new ToolStripSeparator(),
                _deleteItem,
            });

            ColumnContextMenu.Items.AddRange(new ToolStripItem[]
            {
                _columnUndoItem, _columnRedoItem, new ToolStripSeparator(),
                _columnInsertItem, _columnPasteItem, new ToolStripSeparator(), _selectAllItem,
            });

            _undoItem.Click += (sender, e) => RunMenuAction(Undo);
            _redoItem.Click += (sender, e) => RunMenuAction(Redo);
            _columnUndoItem.Click += (sender, e) => RunMenuAction(Undo);
            _columnRedoItem.Click += (sender, e) => RunMenuAction(Redo);

            _insertNextItem.Click += (sender, e) => RunMenuAction(() => BeginInsert(SingleContextMenuNode, before: false));
            _insertBeforeItem.Click += (sender, e) => RunMenuAction(() => BeginInsert(SingleContextMenuNode, before: true));
            _createChildItem.Click += (sender, e) => RunMenuAction(() => BeginCreateChild(SingleContextMenuNode));
            _renameItem.Click += (sender, e) => RunMenuAction(() => BeginRename(SingleContextMenuNode));
            _copyItem.Click += (sender, e) => RunMenuAction(() => CopyNodes(ContextMenuNodes));
            _copyPathItem.Click += (sender, e) => RunMenuAction(() => CopyNodePaths(ContextMenuNodes));
            _cutItem.Click += (sender, e) => RunMenuAction(() => CutNodes(ContextMenuNodes));
            _pasteNextItem.Click += (sender, e) => RunMenuAction(() => PasteBeside(SingleContextMenuNode, before: false));
            _pasteBeforeItem.Click += (sender, e) => RunMenuAction(() => PasteBeside(SingleContextMenuNode, before: true));
            _pasteChildItem.Click += (sender, e) => RunMenuAction(() => PasteIntoLeaf(SingleContextMenuNode));
            _deleteItem.Click += (sender, e) => RunMenuAction(() => DeleteNodes(ContextMenuNodes));

            _columnInsertItem.Click += (sender, e) => RunMenuAction(() => BeginInsertAtEnd(ContextMenuColumnIndex));
            _columnPasteItem.Click += (sender, e) => RunMenuAction(() => PasteAtEnd(ContextMenuColumnIndex));
            _selectAllItem.Click += (sender, e) => RunMenuAction(() => SelectAllInColumn(ContextMenuColumnIndex));

            NodeContextMenu.Opening += OnNodeContextMenuOpening;
            NodeContextMenu.Closed += (sender, e) => SetContextMenuRow(null, null);
            ColumnContextMenu.Opening += OnColumnContextMenuOpening;
        }

        private MenuNode SingleContextMenuNode => ContextMenuNodes.Count == 1 ? ContextMenuNodes[0] : null;

        private void RunMenuAction(Func<bool> action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed) action();
            }));
        }

        /// <summary>
        /// 右鍵點了某一列：點在多選的節點上時對象是全部多選的節點，否則取消多選、對象只有那一個節點。
        /// </summary>
        private void ShowNodeContextMenu(int columnIndex, ICascadeNode node, Point screenLocation)
        {
            if (!(node is MenuNode menuNode)) return;

            IReadOnlyList<MenuNode> targets;
            if (IsMarked(columnIndex, node))
            {
                targets = MultiSelectedNodes.OfType<MenuNode>().ToList();
            }
            else
            {
                ClearMultiSelection();
                targets = new[] { menuNode };
            }

            ContextMenuColumnIndex = columnIndex;
            ContextMenuNode = menuNode;
            ContextMenuNodes = targets;

            // 功能表開著的期間，被點的那一列畫虛線框，讓使用者確定自己點的是哪一個；沒有任何項目、功能表沒開時馬上拿掉。
            SetContextMenuRow(_lists[columnIndex], node);
            NodeContextMenu.Show(screenLocation);
            if (!NodeContextMenu.Visible) SetContextMenuRow(null, null);
        }

        // 右鍵功能表開著時，被點的那一列（見 OnColumnDrawItem 的虛線框）。
        private ListBox _contextMenuRowList;

        private ICascadeNode _contextMenuRowNode;

        private bool IsContextMenuRow(ListBox list, ICascadeNode node) =>
            _contextMenuRowNode != null && ReferenceEquals(list, _contextMenuRowList) && Equals(node, _contextMenuRowNode);

        private void SetContextMenuRow(ListBox list, ICascadeNode node)
        {
            InvalidateRow(_contextMenuRowList, _contextMenuRowNode);
            _contextMenuRowList = list;
            _contextMenuRowNode = node;
            InvalidateRow(list, node);
        }

        // 只重畫那一列；欄位已經被拆掉或那一列已經不在（功能表動作改了樹）時不做任何事。
        private static void InvalidateRow(ListBox list, ICascadeNode node)
        {
            if (list == null || node == null || list.IsDisposed || !list.IsHandleCreated) return;

            var index = list.Items.IndexOf(node);
            if (index >= 0) list.Invalidate(list.GetItemRectangle(index));
        }

        private void ShowColumnContextMenu(int columnIndex, Point screenLocation)
        {
            ContextMenuColumnIndex = columnIndex;
            ContextMenuNode = SafeColumnNodes(columnIndex).FirstOrDefault() as MenuNode;
            ContextMenuNodes = NoMenuNodes;
            ColumnContextMenu.Show(screenLocation);
        }

        private void OnNodeContextMenuOpening(object sender, CancelEventArgs e)
        {
            var targets = ContextMenuNodes;
            var single = SingleContextMenuNode;
            var show = targets.Count > 0;
            var editable = show && CanEdit && targets.All(IsInTree);

            _copyItem.Available = show;
            _copyPathItem.Available = show;

            _insertItem.Available = editable;
            _createChildItem.Available = editable;
            _renameItem.Available = editable;
            _cutItem.Available = editable;
            _pasteItem.Available = editable;
            _deleteItem.Available = editable;

            // 多選時只能複製、剪下、刪除；插入、建立子節點、改名、貼上都需要單一個對象。
            // 建立子節點與「貼上 ▸ 作為子節點」只對葉節點有作用：已經有子節點時，請在子節點那一欄插入或貼上。
            var singleLeaf = single != null && single.IsLeaf;
            _insertItem.Enabled = single != null;
            _createChildItem.Enabled = singleLeaf;
            _renameItem.Enabled = single != null;
            _pasteItem.Enabled = single != null && editable && ClipboardHasNodes();
            _pasteChildItem.Enabled = singleLeaf;

            SetHistoryItems(_undoItem, _redoItem);
            SetCheckBoxMenuItems(NodeContextMenu);

            OnContextMenuOpening(NodeContextMenu);
            e.Cancel = !TidySeparators(NodeContextMenu);
        }

        private void OnColumnContextMenuOpening(object sender, CancelEventArgs e)
        {
            var columnIndex = ContextMenuColumnIndex;
            var editable = CanEdit && ColumnParent(columnIndex) != null;

            _columnInsertItem.Available = editable;
            _columnPasteItem.Available = editable;
            _columnPasteItem.Enabled = editable && ClipboardHasNodes();
            _selectAllItem.Available = SafeColumnNodes(columnIndex).Count > 0;

            SetHistoryItems(_columnUndoItem, _columnRedoItem);
            SetCheckBoxMenuItems(ColumnContextMenu);

            OnContextMenuOpening(ColumnContextMenu);
            e.Cancel = !TidySeparators(ColumnContextMenu);
        }

        // 能改樹（或勾選）時一律顯示復原、重做，沒有步驟時停用，項目才不會忽隱忽現。
        private void SetHistoryItems(ToolStripMenuItem undoItem, ToolStripMenuItem redoItem)
        {
            var show = ShowsHistoryCommands;

            undoItem.Available = show;
            redoItem.Available = show;
            undoItem.Enabled = CanUndo;
            redoItem.Enabled = CanRedo;
        }

        // 右鍵功能表要不要顯示「復原」、「重做」：可以編輯樹時（ReadOnly 為 false、而且是 SetTree 顯示的樹）顯示；
        // 有勾選框時唯讀也可以復原勾選，所以一律顯示。
        private bool ShowsHistoryCommands => CanEdit || ActiveCheckSet != null;

        /// <summary>
        /// 只留下夾在兩個可見項目之間的分隔線。
        /// </summary>
        /// <returns>有任何可見的項目（分隔線不算）時為 true。</returns>
        private static bool TidySeparators(ToolStrip menu)
        {
            var seenItem = false;
            ToolStripSeparator gap = null;

            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripSeparator separator)
                {
                    separator.Available = false;
                    if (seenItem) gap = separator;
                    continue;
                }

                if (!item.Available) continue;

                if (gap != null)
                {
                    gap.Available = true;
                    gap = null;
                }

                seenItem = true;
            }

            return seenItem;
        }

        #endregion 屬性與功能表

        #region 公開的編輯指令

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）連同子孫複製到 Windows 剪貼簿，順序與欄位中相同。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        public bool Copy()
        {
            CommitPendingEdit();
            return CopyNodes(EditTargets());
        }

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）從第一層到它的路徑，以 CSV 文字（含表頭）複製到 Windows 剪貼簿，
        /// 每個節點一列，順序與欄位中相同；只到選取的那一層，不含子孫。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        /// <remarks>
        /// 例如路徑是 Motor &gt; Axis Y &gt; Jog，多選了 Forward、Backward，得到
        /// <c>Level1,Level2,Level3,Level4</c> / <c>Motor,Axis Y,Jog,Forward</c> / <c>Motor,Axis Y,Jog,Backward</c>。
        /// 只放純文字，不放 <see cref="CLIPBOARD_FORMAT"/>：內容含上層路徑，不是要貼回控制項的節點。
        /// </remarks>
        public bool CopyPath()
        {
            CommitPendingEdit();
            return CopyNodePaths(EditTargets());
        }

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）複製到 Windows 剪貼簿，然後從樹中移除。
        /// </summary>
        /// <returns>有剪下時為 true；唯讀、沒有對象或剪貼簿被佔用（這時不會移除）時為 false。</returns>
        public bool Cut()
        {
            CommitPendingEdit();
            return CutNodes(EditTargets());
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼到 <see cref="SelectedNode"/> 的後面或前面，順序與複製時相同；沒有選取時加在第一欄的最後。
        /// </summary>
        /// <param name="before">true 貼在前面，false 貼在後面。</param>
        /// <returns>有貼上時為 true。</returns>
        /// <remarks>同一層已有同名節點時，貼上的節點名稱自動加上後綴（例如 <c>Name - 2</c>）。貼完選取第一個貼上的節點，貼了多個時全部多選。</remarks>
        public bool Paste(bool before)
        {
            CommitPendingEdit();
            return SelectedNode is MenuNode node ? PasteBeside(node, before) : PasteAtEnd(0);
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼成 <see cref="SelectedNode"/> 的子節點，順序與複製時相同；只有 <see cref="SelectedNode"/> 是葉節點時才有作用。
        /// </summary>
        /// <returns>有貼上時為 true；唯讀、沒有選取、選取的節點已經有子節點，或剪貼簿上沒有節點時為 false。</returns>
        /// <remarks>貼完選取第一個貼上的節點（原本的葉節點長出下一欄），貼了多個時全部多選。</remarks>
        public bool PasteAsChild()
        {
            CommitPendingEdit();
            return PasteIntoLeaf(SelectedNode as MenuNode);
        }

        /// <summary>
        /// 刪除多選的節點（沒有多選時是 <see cref="SelectedNode"/>）及其所有子孫；刪除前一律跳出確認。
        /// </summary>
        /// <returns>有刪除時為 true；唯讀、沒有對象或使用者取消時為 false。</returns>
        public bool Delete()
        {
            CommitPendingEdit();
            return DeleteNodes(EditTargets());
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 的後面或前面插入一列空白的編輯框，按 Enter 以輸入的名稱建立新節點，按 Esc 取消。
        /// </summary>
        /// <param name="before">true 插在前面，false 插在後面。</param>
        /// <returns>有開啟編輯框時為 true。</returns>
        public bool BeginInsert(bool before)
        {
            CommitPendingEdit();
            return BeginInsert(SelectedNode as MenuNode, before);
        }

        /// <summary>
        /// 在葉節點 <see cref="SelectedNode"/> 底下長出下一欄並開啟編輯框，按 Enter 建立它的第一個子節點，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true；選取的節點已經有子節點時為 false（請在子節點那一欄用 <see cref="BeginInsert(bool)"/>）。</returns>
        public bool BeginCreateChild()
        {
            CommitPendingEdit();
            return BeginCreateChild(SelectedNode as MenuNode);
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 上開啟編輯框，文字是目前的名稱並全選；按 Enter 改名，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true。</returns>
        public bool BeginRename()
        {
            CommitPendingEdit();
            return BeginRename(SelectedNode as MenuNode);
        }

        /// <summary>
        /// 結束進行中的就地編輯；沒有在編輯時不做任何事。
        /// </summary>
        /// <param name="commit">true 套用輸入的名稱（空白或含有保留字元時當成取消），false 取消。</param>
        public void EndEdit(bool commit)
        {
            EndEdit(commit, interactive: false);
        }

        /// <summary>
        /// 復原 <see cref="History"/> 最近的一個步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有復原時為 true；<see cref="CanUndo"/> 為 false，或樹已經和記錄時對不上（這時整份歷史會被清除）時為 false。</returns>
        /// <remarks>
        /// 改到樹的步驟：重建欄位，選取跳到那一步的位置（回到樹裡的節點，或被拿掉的節點原位置的鄰居），回來的節點不只一個時全部多選，
        /// 然後觸發 <see cref="TreeEdited"/>（<see cref="CascadeEditAction.Undo"/>）。只改勾選的步驟：選取跳到那一步針對的節點。
        /// </remarks>
        public bool Undo() => Replay(undo: true);

        /// <summary>
        /// 重做最近一次被復原的步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有重做時為 true；<see cref="CanRedo"/> 為 false，或樹已經和記錄時對不上時為 false。</returns>
        /// <remarks>選取與事件的規則同 <see cref="Undo"/>，事件的種類是 <see cref="CascadeEditAction.Redo"/>。</remarks>
        public bool Redo() => Replay(undo: false);

        #endregion 公開的編輯指令

        #region 復原與重做

        private bool Replay(bool undo)
        {
            CommitPendingEdit();
            if (!(undo ? CanUndo : CanRedo)) return false;

            var hadFocus = ContainsFocus;
            var result = undo ? History.Undo() : History.Redo();
            if (result == null) return false;

            if (result.TreeChanged)
            {
                // 節點回到樹裡（復原刪除、重做貼上）且不只一個時多選，和貼上之後一樣。
                var restored = result.Nodes.Count > 1 && result.Nodes.All(IsInTree);
                FinishEdit(undo ? CascadeEditAction.Undo : CascadeEditAction.Redo, result.Nodes, result.Focus, restored);
            }
            else if (IsInTree(result.Focus))
            {
                TrySelectNode(result.Focus);
            }

            if (hadFocus) FocusActiveColumn();
            return true;
        }

        #endregion 復原與重做

        #region 對象與樹

        private bool CanEdit => !_readOnly && Tree != null;

        // 多選優先，沒有多選時是目前選取的節點。
        private IReadOnlyList<MenuNode> EditTargets()
        {
            if (_marks.Count > 0) return MultiSelectedNodes.OfType<MenuNode>().ToList();

            return SelectedNode is MenuNode node ? new[] { node } : NoMenuNodes;
        }

        // 節點是否屬於目前顯示的樹；樹根不算。
        private bool IsInTree(MenuNode node)
        {
            if (node == null || Tree == null || ReferenceEquals(node, Tree.Root)) return false;

            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, Tree.Root)) return true;
            }

            return false;
        }

        private bool IsInTreeOrRoot(MenuNode node) => Tree != null && (ReferenceEquals(node, Tree.Root) || IsInTree(node));

        // 第 columnIndex 欄的節點是誰的子節點：第一欄是樹根，其他欄是路徑上前一欄的節點；那一欄不存在時為 null。
        private MenuNode ColumnParent(int columnIndex)
        {
            if (Tree == null || columnIndex < 0) return null;
            if (columnIndex == 0) return Tree.Root;

            var path = _state.PathWithTrail;
            return columnIndex - 1 < path.Count ? path[columnIndex - 1] as MenuNode : null;
        }

        // 和 ColumnNodes 相同，但欄位索引超出範圍時回傳空集合。
        private IReadOnlyList<ICascadeNode> SafeColumnNodes(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex > _state.PathWithTrail.Count) return NoNodes;
            return ColumnNodes(columnIndex);
        }

        private bool CanEditNodes(IReadOnlyList<MenuNode> nodes) => CanEdit && nodes != null && nodes.Count > 0 && nodes.All(IsInTree);

        /// <summary>
        /// 改完樹之後：重建欄位並選取 <paramref name="select"/>（null 表示不選取），<paramref name="markNodes"/> 時把多個節點多選，最後觸發 <see cref="TreeEdited"/>。
        /// </summary>
        /// <remarks>
        /// 使用者的編輯要在 <see cref="MenuHistory.BeginGroup"/> 裡呼叫，衍生類別在 <see cref="OnTreeEdited"/> 跟著做的異動（例如取消被刪節點的勾選）才會和編輯算同一步。
        /// </remarks>
        private void FinishEdit(CascadeEditAction action, IReadOnlyList<MenuNode> nodes, MenuNode select, bool markNodes)
        {
            var hadFocus = ContainsFocus;

            RefreshColumns(select?.FullPath ?? NoMenuNodes);
            if (markNodes && nodes.Count > 1) SetMarks(nodes[0].Depth - 1, nodes);
            if (hadFocus) FocusActiveColumn();

            OnTreeEdited(new CascadeTreeEditedEventArgs(action, nodes));
        }

        #endregion 對象與樹

        #region 剪貼簿

        private bool CopyNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return false;

            string text;
            try
            {
                text = FormatClipboardText(nodes);
            }
            catch (ArgumentException)
            {
                // 子樹大到展開後超過路徑上限，寫出去讀不回來。
                return false;
            }

            var data = new DataObject();
            data.SetData(CLIPBOARD_FORMAT, new MemoryStream(Encoding.UTF8.GetBytes(text)));
            data.SetText(text);

            try
            {
                Clipboard.SetDataObject(data, true, CLIPBOARD_RETRY_TIMES, CLIPBOARD_RETRY_DELAY);
                return true;
            }
            catch (Exception ex) when (ex is ExternalException || ex is System.Threading.ThreadStateException)
            {
                return false;
            }
        }

        // 每個節點從第一層到它的路徑寫成一列 CSV 純文字（不含子孫），順序與欄位中相同。
        private static bool CopyNodePaths(IReadOnlyList<MenuNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return false;

            string text;
            try
            {
                var tree = new MenuTree();
                foreach (var node in nodes) tree.AddPath(node.FullPath.Select(step => step.Name));

                text = MenuTreeCsvText.Format(tree, MenuRowStyle.Expanded);
            }
            catch (ArgumentException)
            {
                // 名稱含保留字元或前後有空白，寫出去讀不回來。
                return false;
            }

            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, CLIPBOARD_RETRY_TIMES, CLIPBOARD_RETRY_DELAY);
                return true;
            }
            catch (Exception ex) when (ex is ExternalException || ex is System.Threading.ThreadStateException)
            {
                return false;
            }
        }

        // 每個節點各自當成第一層，連同子孫寫成 CSV；讀回時第一層的順序就是複製時的順序。
        private static string FormatClipboardText(IReadOnlyList<MenuNode> nodes)
        {
            var tree = new MenuTree();
            foreach (var node in nodes) tree.Root.InsertChildAt(tree.Root.Children.Count, node);

            return MenuTreeCsvText.Format(tree, MenuRowStyle.Expanded);
        }

        // 剪貼簿上的節點（第一層，依複製時的順序）；沒有本控制項的格式時為空集合。
        private static IReadOnlyList<MenuNode> ReadClipboardNodes()
        {
            string text;
            try
            {
                if (!Clipboard.ContainsData(CLIPBOARD_FORMAT)) return NoMenuNodes;

                // 存的是 MemoryStream，取回來也是原始位元組，不經過 BinaryFormatter。
                if (!(Clipboard.GetData(CLIPBOARD_FORMAT) is MemoryStream stream)) return NoMenuNodes;
                text = Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex) when (ex is ExternalException || ex is System.Threading.ThreadStateException)
            {
                return NoMenuNodes;
            }

            return MenuTreeCsvText.Parse(text, out _).TopLevelNodes.ToList();
        }

        private static bool ClipboardHasNodes()
        {
            try
            {
                return Clipboard.ContainsData(CLIPBOARD_FORMAT);
            }
            catch (Exception ex) when (ex is ExternalException || ex is System.Threading.ThreadStateException)
            {
                return false;
            }
        }

        #endregion 剪貼簿

        #region 剪下、貼上、刪除

        private bool CutNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes) || !CopyNodes(nodes)) return false;

            RemoveNodes(CascadeEditAction.Cut, nodes);
            return true;
        }

        private bool DeleteNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes)) return false;

            var hasChildren = nodes.Any(node => !node.IsLeaf);
            var message = nodes.Count == 1
                ? $"確定要刪除「{nodes[0].FullPathText}」？"
                : $"確定要刪除選取的 {nodes.Count} 個節點？";
            if (hasChildren) message += Environment.NewLine + "底下的子節點也會一起刪除。";

            var hadFocus = ContainsFocus;
            var answer = MessageBox.Show(this, message, "刪除", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (hadFocus) FocusActiveColumn();
            if (answer != DialogResult.Yes) return false;

            // 對話框開著的時候樹可能被換掉，再確認一次。
            if (!CanEditNodes(nodes)) return false;

            RemoveNodes(CascadeEditAction.Delete, nodes);
            return true;
        }

        // 移除後選取原位置的下一個兄弟（沒有就選前一個），一個都不剩時選父節點。
        // 對象都在同一欄，也就是同一個父節點底下；記下移除前的位置，復原時才接得回原處。
        private void RemoveNodes(CascadeEditAction action, IReadOnlyList<MenuNode> nodes)
        {
            var parent = nodes[0].Parent;
            var removed = nodes.Select(node => new KeyValuePair<int, MenuNode>(node.SiblingNumber - 1, node)).ToList();
            var firstIndex = removed.Min(entry => entry.Key);

            using (History.BeginGroup())
            {
                foreach (var node in nodes) node.RemoveFromParent();
                History.RecordRemove(Tree, parent, removed);

                MenuNode next = null;
                if (parent.Children.Count > 0) next = parent.Children[Math.Min(firstIndex, parent.Children.Count - 1)];
                else if (!ReferenceEquals(parent, Tree.Root)) next = parent;

                ClearMultiSelection();
                FinishEdit(action, nodes, next, markNodes: false);
            }
        }

        private bool PasteBeside(MenuNode target, bool before)
        {
            if (!IsInTree(target)) return false;

            var position = target.SiblingNumber - 1 + (before ? 0 : 1);
            return PasteInto(target.Parent, position);
        }

        // 貼成葉節點的子節點；已經有子節點的節點不接受，和建立子節點的規則一致。
        private bool PasteIntoLeaf(MenuNode target)
        {
            if (!IsInTree(target) || !target.IsLeaf) return false;

            return PasteInto(target, 0);
        }

        private bool PasteAtEnd(int columnIndex)
        {
            var parent = ColumnParent(columnIndex);
            return parent != null && PasteInto(parent, parent.Children.Count);
        }

        private bool PasteInto(MenuNode parent, int position)
        {
            if (!CanEdit || !IsInTreeOrRoot(parent)) return false;

            var sources = ReadClipboardNodes();
            if (sources.Count == 0) return false;

            using (History.BeginGroup())
            {
                // 名稱一律取不重複的，所以每一個都是新節點，不會合併進既有的節點。
                var pasted = new List<MenuNode>(sources.Count);
                foreach (var source in sources)
                {
                    pasted.Add(parent.InsertChildAt(position, source, parent.GetUniqueChildName(source.Name)));
                    position++;
                }

                History.RecordInsert(Tree, parent, pasted);
                FinishEdit(CascadeEditAction.Paste, pasted, pasted[0], markNodes: true);
            }

            return true;
        }

        #endregion 剪下、貼上、刪除

        #region 就地編輯

        private bool BeginInsert(MenuNode anchor, bool before)
        {
            if (!CanEditNodes(Single(anchor)) || !PrepareEdit(anchor)) return false;

            var position = anchor.SiblingNumber - 1 + (before ? 0 : 1);
            return OpenPlaceholderEditor(CascadeEditAction.Insert, anchor.Parent, position);
        }

        // 欄位空白處的 Insert：加在那一欄的最後。
        private bool BeginInsertAtEnd(int columnIndex)
        {
            var parent = ColumnParent(columnIndex);
            if (!CanEdit || !IsInTreeOrRoot(parent)) return false;

            CommitPendingEdit();
            ClearMultiSelection();

            // 選取父節點，讓這一欄成為它的下一欄並留在畫面上；第一欄本來就在。
            if (!ReferenceEquals(parent, Tree.Root) && !TrySelectNode(parent)) return false;

            return OpenPlaceholderEditor(CascadeEditAction.Insert, parent, parent.Children.Count);
        }

        // 只對葉節點：先長出下一欄（_pendingChildParent），編輯框開在那一欄的第一列。
        private bool BeginCreateChild(MenuNode node)
        {
            if (!CanEditNodes(Single(node)) || !node.IsLeaf || !PrepareEdit(node)) return false;

            _pendingChildParent = node;
            SyncColumns();

            return OpenPlaceholderEditor(CascadeEditAction.CreateChild, node, node.Children.Count);
        }

        private bool BeginRename(MenuNode node)
        {
            if (!CanEditNodes(Single(node)) || !PrepareEdit(node)) return false;

            var columnIndex = node.Depth - 1;
            if (!IsColumnShown(columnIndex)) return false;

            _edit = new EditSession(CascadeEditAction.Rename, _lists[columnIndex], node, node.Parent, node.SiblingNumber - 1);
            OpenEditBox(node.Name);
            return true;
        }

        private static IReadOnlyList<MenuNode> Single(MenuNode node) => node == null ? NoMenuNodes : new[] { node };

        // 開始編輯之前：結束上一個編輯、取消多選，並選取對象，讓它所在的欄成為操作欄。
        private bool PrepareEdit(MenuNode node)
        {
            CommitPendingEdit();
            ClearMultiSelection();
            return TrySelectNode(node);
        }

        /// <summary>
        /// 在 <paramref name="parent"/> 的子節點那一欄的第 <paramref name="position"/> 列插入一列空白，並在上面開啟編輯框。
        /// </summary>
        private bool OpenPlaceholderEditor(CascadeEditAction action, MenuNode parent, int position)
        {
            var columnIndex = parent.Depth;
            if (columnIndex >= _lists.Count || !IsColumnShown(columnIndex))
            {
                _pendingChildParent = null;
                SyncColumns();
                return false;
            }

            var list = _lists[columnIndex];
            var placeholder = new EditPlaceholder();
            var row = Math.Max(0, Math.Min(position, list.Items.Count));

            // 插在選取項前面時 ListBox 會跟著調整選取，那不是使用者改選。
            _syncing = true;
            try
            {
                list.Items.Insert(row, placeholder);
            }
            finally
            {
                _syncing = false;
            }

            _edit = new EditSession(action, list, placeholder, parent, position);
            OpenEditBox(string.Empty);
            return true;
        }

        // 編輯框是欄位 ListBox 的子控制項，蓋在那一列上面；欄位的 WS_CLIPCHILDREN 讓 ListBox 重畫時不會蓋掉它。
        // 單行 TextBox 不能讓文字垂直置中，自動調整的高度又比列高高，字會偏上；
        // 所以外框是另一個子控制項（EditFrame），沒有框的 TextBox 只有一行字高，垂直置中放在外框裡。
        private void OpenEditBox(string text)
        {
            var session = _edit;
            var list = session.List;
            var row = list.Items.IndexOf(session.RowNode);
            EnsureRowVisible(list, row);

            var frame = new EditFrame { Name = "pnlEdit" };
            var box = new TextBox
            {
                Name = "txtEdit",
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.WindowText,
                Font = list.Font,
                Text = text,

                // 欄位為了讓 Space 當指令可能關掉了輸入法（CheckBoxes），編輯框不跟著繼承。
                ImeMode = ImeMode.NoControl,
            };

            session.Frame = frame;
            session.Box = box;
            frame.Controls.Add(box);
            list.Controls.Add(frame);
            PositionEditBox();

            box.LostFocus += OnEditBoxLostFocus;
            box.Focus();
            box.SelectAll();
        }

        // 讓編輯框對齊它那一列；欄位捲動、改變大小之後都要再對一次。
        private void PositionEditBox()
        {
            var session = _edit;
            if (session?.Frame == null || session.List.IsDisposed) return;

            var row = session.List.Items.IndexOf(session.RowNode);
            if (row < 0) return;

            // 外框至少和列一樣高；字比列高時外框跟著撐高（上下各多出一點蓋到鄰列），文字仍然在列的正中間。
            var bounds = session.List.GetItemRectangle(row);
            var textHeight = session.Box.Height;
            var frameHeight = Math.Max(bounds.Height, textHeight + (EDIT_FRAME_PADDING * 2));
            var frameTop = bounds.Top + ((bounds.Height - frameHeight) / 2);
            var frameWidth = Math.Max(EDIT_FRAME_PADDING * 3, bounds.Width);

            session.Frame.Bounds = new Rectangle(bounds.Left, frameTop, frameWidth, frameHeight);
            session.Box.Bounds = new Rectangle(
                EDIT_FRAME_PADDING,
                (frameHeight - textHeight) / 2,
                frameWidth - (EDIT_FRAME_PADDING * 2),
                textHeight);
        }

        // 外框線 1 像素，再留 1 像素空白。
        private const int EDIT_FRAME_PADDING = 2;

        /// <summary>
        /// 就地編輯框的外框：白底、1 像素的選取色框線，裡面放沒有框的 TextBox。
        /// </summary>
        private sealed class EditFrame : Control
        {
            public EditFrame()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = SystemColors.Window;
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.Highlight, ButtonBorderStyle.Solid);
            }
        }

        private static void EnsureRowVisible(ListBox list, int row)
        {
            if (row < 0) return;

            if (row < list.TopIndex)
            {
                list.TopIndex = row;
                return;
            }

            var visibleRows = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));
            if (row >= list.TopIndex + visibleRows) list.TopIndex = row - visibleRows + 1;
        }

        // Sliding 模式下範圍外的欄位是隱藏的，編輯框開在那裡使用者看不到。
        private bool IsColumnShown(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _lists.Count) return false;
            if (!IsSlidingMode) return true;

            var viewportColumns = ViewportColumnCount;
            var start = ViewportStartColumn(_lists.Count, ActiveColumnIndex, viewportColumns, ActiveColumnHasBranch());
            return columnIndex >= start && columnIndex < start + viewportColumns;
        }

        // 焦點離開編輯框（Tab、點了表單上的其他控制項、切到別的程式）就套用；等這一輪訊息處理完再做，不在焦點切換的當下改欄位。
        private void OnEditBoxLostFocus(object sender, EventArgs e)
        {
            var session = _edit;
            if (session == null || session.Prompting || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (_edit == session && !session.Prompting && !session.Box.ContainsFocus) EndEdit(commit: true, interactive: false);
            }));
        }

        /// <summary>
        /// 編輯中點了任何一欄：這一下只用來結束編輯（套用輸入的名稱），不選取、不開功能表。
        /// </summary>
        /// <remarks>由欄位的 WndProc 呼叫，所以延到這個訊息處理完才做：結束編輯會重建欄位，可能拆掉正在處理訊息的那一欄。</remarks>
        private void RequestCommitEdit()
        {
            var session = _edit;
            _swallowMouseUp = true;

            BeginInvoke(new Action(() =>
            {
                if (_edit == session) EndEdit(commit: true, interactive: false);
            }));
        }

        // 公開指令開始前，先把還開著的編輯框套用掉。
        private void CommitPendingEdit()
        {
            if (_edit != null) EndEdit(commit: true, interactive: false);
        }

        /// <summary>
        /// 結束就地編輯。
        /// </summary>
        /// <param name="commit">是否套用。</param>
        /// <param name="interactive">
        /// 是否由使用者按 Enter 觸發：名稱含有保留字元時跳出提示並繼續編輯；
        /// 其他情況（失去焦點、點了別的地方）直接當成取消，不跳對話框。
        /// </param>
        /// <returns>編輯已經結束時為 true；跳出提示、繼續編輯時為 false。</returns>
        private bool EndEdit(bool commit, bool interactive)
        {
            var session = _edit;
            if (session == null) return true;

            var name = session.Box.Text.Trim();

            if (commit && name.IndexOf(MultiNameCellSyntax.NAME_SEPARATOR) >= 0)
            {
                if (interactive)
                {
                    session.Prompting = true;
                    try
                    {
                        MessageBox.Show(this, $"節點名稱不可含有 '{MultiNameCellSyntax.NAME_SEPARATOR}' 字元。", CaptionOf(session.Action),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        session.Prompting = false;
                    }

                    if (_edit == session && !session.Box.IsDisposed)
                    {
                        session.Box.Focus();
                        session.Box.SelectAll();
                    }

                    return false;
                }

                commit = false;
            }

            var hadFocus = session.Box.ContainsFocus;
            CloseEditBox(session, hadFocus);

            using (History.BeginGroup())
            {
                var result = commit && name.Length > 0 ? ApplyEdit(session, name) : null;
                _pendingChildParent = null;

                if (result != null)
                {
                    FinishEdit(session.Action, new[] { result }, result, markNodes: false);
                }
                else
                {
                    RemovePlaceholder(session);
                    SyncColumns();
                }
            }

            if (hadFocus) FocusActiveColumn();
            return true;
        }

        // 套用輸入的名稱並記進 History；同一層已有同名節點時自動加上後綴，所以不會合併。對象已經不在樹裡，或改名成原本的名稱時回傳 null。
        private MenuNode ApplyEdit(EditSession session, string name)
        {
            switch (session.Action)
            {
                case CascadeEditAction.Insert:
                case CascadeEditAction.CreateChild:
                    if (!IsInTreeOrRoot(session.Parent)) return null;
                    var inserted = session.Parent.InsertChildAt(session.Position, new MenuNode(name), session.Parent.GetUniqueChildName(name));
                    History.RecordInsert(Tree, session.Parent, new[] { inserted });
                    return inserted;

                case CascadeEditAction.Rename:
                    var target = (MenuNode)session.RowNode;
                    if (!IsInTree(target) || string.Equals(target.Name, name, StringComparison.Ordinal)) return null;
                    var oldName = target.Name;
                    var renamed = target.Rename(target.Parent.GetUniqueChildName(name));
                    History.RecordRename(Tree, renamed, oldName);
                    return renamed;

                default:
                    return null;
            }
        }

        private static string CaptionOf(CascadeEditAction action)
        {
            switch (action)
            {
                case CascadeEditAction.Insert: return "插入";
                case CascadeEditAction.CreateChild: return "建立子節點";
                case CascadeEditAction.Rename: return "重新命名";
                default: return action.ToString();
            }
        }

        // 拿掉編輯框。編輯框原本有焦點時先把焦點交給它所在的欄，焦點才不會因為編輯框消失而掉出控制項。
        private void CloseEditBox(EditSession session, bool keepFocus)
        {
            _edit = null;

            var box = session.Box;
            box.LostFocus -= OnEditBoxLostFocus;

            if (keepFocus && !session.List.IsDisposed && session.List.CanFocus) session.List.Focus();

            if (!session.List.IsDisposed) session.List.Controls.Remove(session.Frame);

            // 編輯框是外框的子控制項，一起釋放。
            session.Frame.Dispose();
        }

        private void RemovePlaceholder(EditSession session)
        {
            if (!(session.RowNode is EditPlaceholder) || session.List.IsDisposed) return;

            _syncing = true;
            try
            {
                session.List.Items.Remove(session.RowNode);
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// 不套用、不重建欄位，直接丟掉進行中的編輯；欄位要重建、換樹或改成唯讀時呼叫。
        /// </summary>
        private void AbortEdit()
        {
            var session = _edit;
            if (session == null) return;

            CloseEditBox(session, session.Box.ContainsFocus);
            RemovePlaceholder(session);
            _pendingChildParent = null;
        }

        /// <summary>
        /// 就地編輯的狀態：編輯框蓋在哪一欄的哪一列，以及按 Enter 時要套用到哪裡。
        /// </summary>
        private sealed class EditSession
        {
            public EditSession(CascadeEditAction action, ListBox list, ICascadeNode rowNode, MenuNode parent, int position)
            {
                Action = action;
                List = list;
                RowNode = rowNode;
                Parent = parent;
                Position = position;
            }

            public CascadeEditAction Action { get; }

            public ListBox List { get; }

            /// <summary>編輯框蓋住的那一列：插入與建立子節點是暫時的空白列，改名是被改名的節點。</summary>
            public ICascadeNode RowNode { get; }

            /// <summary>新節點的父節點；改名時是被改名節點的父節點。</summary>
            public MenuNode Parent { get; }

            /// <summary>新節點在 <see cref="Parent"/> 底下的位置。</summary>
            public int Position { get; }

            /// <summary>編輯框的外框，欄位 ListBox 的子控制項。</summary>
            public Control Frame { get; set; }

            /// <summary>輸入名稱的 TextBox，<see cref="Frame"/> 的子控制項。</summary>
            public TextBox Box { get; set; }

            /// <summary>正在跳出提示；這時編輯框失去焦點不算結束編輯。</summary>
            public bool Prompting { get; set; }
        }

        /// <summary>
        /// 插入與建立子節點時暫時放在欄位裡的空白列，編輯框就蓋在它上面；不屬於任何樹。
        /// </summary>
        private sealed class EditPlaceholder : ICascadeNode
        {
            public string Name => string.Empty;

            public string DisplayText => string.Empty;

            public IReadOnlyList<ICascadeNode> Children => NoNodes;
        }

        #endregion 就地編輯

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachState(_state);
                _edit = null;
                NodeContextMenu.Dispose();
                ColumnContextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}