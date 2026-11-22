using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Columns
{
    // 內建快顯功能表：節點與欄位空白處的兩個功能表、開啟前決定項目狀態，以及功能表開著時被點的那一列。
    public partial class CascadeColumnsView
    {
        // 同一個 ToolStripItem 只能放在一個功能表裡，所以兩個功能表各有一組復原、重做。

        private readonly ToolStripMenuItem _undoItem = new ToolStripMenuItem("復原") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Undo) };
        private readonly ToolStripMenuItem _redoItem = new ToolStripMenuItem("重做") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Redo) };
        private readonly ToolStripMenuItem _columnUndoItem = new ToolStripMenuItem("復原") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Undo) };
        private readonly ToolStripMenuItem _columnRedoItem = new ToolStripMenuItem("重做") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Redo) };

        private readonly ToolStripMenuItem _insertItem = new ToolStripMenuItem("插入");
        private readonly ToolStripMenuItem _insertBeforeItem = new ToolStripMenuItem("之前") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.InsertBefore) };
        private readonly ToolStripMenuItem _insertNextItem = new ToolStripMenuItem("之後") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Insert) };
        private readonly ToolStripMenuItem _createChildItem = new ToolStripMenuItem("建立子節點") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.CreateChild) };
        private readonly ToolStripMenuItem _renameItem = new ToolStripMenuItem("重新命名") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Rename) };
        private readonly ToolStripMenuItem _copyItem = new ToolStripMenuItem("複製") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Copy) };
        private readonly ToolStripMenuItem _copyPathItem = new ToolStripMenuItem("複製(路徑)") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.CopyPath) };
        private readonly ToolStripMenuItem _cutItem = new ToolStripMenuItem("剪下") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Cut) };
        private readonly ToolStripMenuItem _pasteItem = new ToolStripMenuItem("貼上");
        private readonly ToolStripMenuItem _pasteBeforeItem = new ToolStripMenuItem("之前") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.PasteBefore) };
        private readonly ToolStripMenuItem _pasteNextItem = new ToolStripMenuItem("之後") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Paste) };
        private readonly ToolStripMenuItem _pasteChildItem = new ToolStripMenuItem("作為子節點") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.PasteAsChild) };
        private readonly ToolStripMenuItem _deleteItem = new ToolStripMenuItem("刪除") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.Delete) };

        private readonly ToolStripMenuItem _columnInsertItem = new ToolStripMenuItem("插入");
        private readonly ToolStripMenuItem _columnPasteItem = new ToolStripMenuItem("貼上");
        private readonly ToolStripMenuItem _selectAllItem = new ToolStripMenuItem("全選") { ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(CascadeEditCommand.SelectAll) };

        #region 右鍵功能表

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
            _copyItem.Click += (sender, e) => RunMenuAction(() => CascadeClipboard.CopyNodes(ContextMenuNodes));
            _copyPathItem.Click += (sender, e) => RunMenuAction(() => CascadeClipboard.CopyNodePaths(ContextMenuNodes));
            _cutItem.Click += (sender, e) => RunMenuAction(() => CutNodes(ContextMenuNodes));
            _pasteNextItem.Click += (sender, e) => RunMenuAction(() => PasteBeside(SingleContextMenuNode, before: false));
            _pasteBeforeItem.Click += (sender, e) => RunMenuAction(() => PasteBeside(SingleContextMenuNode, before: true));
            _pasteChildItem.Click += (sender, e) => RunMenuAction(() => PasteIntoLeaf(SingleContextMenuNode));
            _deleteItem.Click += (sender, e) => RunMenuAction(() => DeleteNodes(ContextMenuNodes));

            _columnInsertItem.Click += (sender, e) => RunMenuAction(() => BeginInsertAtEnd(ContextMenuColumnIndex));
            _columnPasteItem.Click += (sender, e) => RunMenuAction(() => PasteAtEnd(ContextMenuColumnIndex));
            _selectAllItem.Click += (sender, e) => RunMenuAction(() => SelectAllInColumn(ContextMenuColumnIndex));

            NodeContextMenu.Opening += NodeContextMenu_Opening;
            NodeContextMenu.Closed += (sender, e) => SetContextMenuRow(null, null);
            ColumnContextMenu.Opening += ColumnContextMenu_Opening;
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

        // 右鍵功能表開著時，被點的那一列（見 Column_DrawItem 的虛線框）。
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

        private void NodeContextMenu_Opening(object sender, CancelEventArgs e)
        {
            var targets = ContextMenuNodes;
            var single = SingleContextMenuNode;
            var show = targets.Count > 0;
            var editable = show && CanEdit && targets.All(_state.IsInTree);

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
            _pasteItem.Enabled = single != null && editable && CascadeClipboard.ContainsNodes();
            _pasteChildItem.Enabled = singleLeaf;

            SetHistoryItems(_undoItem, _redoItem);
            SetCheckBoxMenuItems(NodeContextMenu);

            OnContextMenuOpening(NodeContextMenu);
            e.Cancel = !TidySeparators(NodeContextMenu);
        }

        private void ColumnContextMenu_Opening(object sender, CancelEventArgs e)
        {
            var columnIndex = ContextMenuColumnIndex;
            var editable = CanEdit && ColumnParent(columnIndex) != null;

            _columnInsertItem.Available = editable;
            _columnPasteItem.Available = editable;
            _columnPasteItem.Enabled = editable && CascadeClipboard.ContainsNodes();
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

        #endregion 右鍵功能表
    }
}