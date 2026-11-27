using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 右鍵功能表：編輯項目與切換顯示設定。
    public partial class CascadeDiagramView
    {
        // 右鍵按下時點到的節點；點在空白處、用鍵盤開啟或功能表關閉後為 null。
        private MenuNode _contextMenuNode;

        #region 右鍵功能表

        // 每次開啟前重建：編輯項目依 ReadOnly 與對象決定；沒開 ShowChecks 或 State 沒有勾選能力時不列出「未勾選節點」。
        private void ContextMenu_Opening(object sender, CancelEventArgs e)
        {
            SyncPendingCut(redrawOnClear: true);

            var items = _contextMenu.Items;
            items.Clear();

            var target = _contextMenuNode ?? SelectedMenuNode;
            AddEditItems(items, target);
            AddExpansionItems(items, target);

            var scopeMenu = new ToolStripMenuItem("範圍");
            scopeMenu.DropDownItems.Add(CreateChoice("整棵樹", _drawScope == CascadeDiagramDrawScope.WholeTree, () => DrawScope = CascadeDiagramDrawScope.WholeTree));
            scopeMenu.DropDownItems.Add(CreateChoice("選取的子樹", _drawScope == CascadeDiagramDrawScope.SelectedSubtree, () => DrawScope = CascadeDiagramDrawScope.SelectedSubtree));
            items.Add(scopeMenu);

            if (_showChecks && _state is CascadeCheckableState)
            {
                var uncheckedMenu = new ToolStripMenuItem("未勾選節點");
                uncheckedMenu.DropDownItems.Add(CreateChoice("變淡", _uncheckedDisplay == CascadeDiagramUncheckedDisplay.Dimmed, () => UncheckedDisplay = CascadeDiagramUncheckedDisplay.Dimmed));
                uncheckedMenu.DropDownItems.Add(CreateChoice("隱藏", _uncheckedDisplay == CascadeDiagramUncheckedDisplay.Hidden, () => UncheckedDisplay = CascadeDiagramUncheckedDisplay.Hidden));
                items.Add(uncheckedMenu);
            }

            var displayMenu = new ToolStripMenuItem("顯示");
            displayMenu.DropDownItems.Add(CreateChoice("顯示編號", _showIndex, () => ShowIndex = !_showIndex));
            displayMenu.DropDownItems.Add(CreateChoice("顯示子節點數量", _showChildCount, () => ShowChildCount = !_showChildCount));
            displayMenu.DropDownItems.Add(new ToolStripSeparator());
            displayMenu.DropDownItems.Add(CreateCommand("符合視窗大小", FitToWindow));
            displayMenu.DropDownItems.Add(CreateCommand("重設縮放", ResetZoom));
            displayMenu.DropDownItems.Add(new ToolStripSeparator());
            displayMenu.DropDownItems.Add(CreateCommand("放大", ZoomIn, ZoomPercent < ZoomSteps[ZoomSteps.Length - 1]));
            displayMenu.DropDownItems.Add(CreateCommand("縮小", ZoomOut, ZoomPercent > ZoomSteps[0]));
            items.Add(displayMenu);

            // 清空項目後 ContextMenuStrip 會預設取消開啟，要明確允許。
            e.Cancel = false;
        }

        private void ContextMenu_Closed(object sender, ToolStripDropDownClosedEventArgs e) => _contextMenuNode = null;

        // 復原、重做 ｜ 插入、建立子節點、重新命名 ｜ 複製、複製(路徑)、剪下、貼上 ｜ 刪除 ｜（顯示設定）。每一組以分隔線結尾，沒有編輯項目時不加。
        private void AddEditItems(ToolStripItemCollection items, MenuNode target)
        {
            // 有樹而且能改樹或顯示勾選時一律顯示復原、重做，沒有步驟時停用，項目才不會忽隱忽現。
            if (CanEdit || (Tree != null && _showChecks && _state is CascadeCheckableState))
            {
                items.Add(CreateCommand("復原", CascadeEditCommand.Undo, CanUndo, Undo));
                items.Add(CreateCommand("重做", CascadeEditCommand.Redo, CanRedo, Redo));
                items.Add(new ToolStripSeparator());
            }

            if (_state.IsInTree(target))
            {
                if (CanEdit)
                {
                    // 插入前會選取 target；只畫選取的子樹時它就成了圖的起點，旁邊沒有位置。建立子節點只對葉節點。
                    var drawn = FindImageNode(target) != null;
                    var insertMenu = new ToolStripMenuItem("插入") { Enabled = drawn && _drawScope != CascadeDiagramDrawScope.SelectedSubtree };
                    insertMenu.DropDownItems.Add(CreateCommand("之前", CascadeEditCommand.InsertBefore, true, () => BeginInsert(target, before: true)));
                    insertMenu.DropDownItems.Add(CreateCommand("之後", CascadeEditCommand.Insert, true, () => BeginInsert(target, before: false)));
                    items.Add(insertMenu);
                    items.Add(CreateCommand("建立子節點", CascadeEditCommand.CreateChild, drawn && target.IsLeaf, () => BeginCreateChild(target)));
                    items.Add(CreateCommand("重新命名", CascadeEditCommand.Rename, drawn, () => BeginRename(target)));
                    items.Add(new ToolStripSeparator());
                }

                items.Add(CreateCommand("複製", CascadeEditCommand.Copy, true, () => CopyNodes(EditTargets(target))));
                items.Add(CreateCommand("複製(路徑)", CascadeEditCommand.CopyPath, true, () => CopyNodePaths(CopyPathTargets(target))));

                if (CanEdit)
                {
                    items.Add(CreateCommand("剪下", CascadeEditCommand.Cut, true, () => CutNodes(EditTargets(target))));

                    // 「作為子節點」只對葉節點有作用：已經有子節點時，請在子節點旁邊貼上。
                    var canPasteBeside = !IsSameStatePendingCutTarget(target.Parent);
                    var canPasteAsChild = target.IsLeaf && !IsSameStatePendingCutTarget(target);
                    var pasteMenu = new ToolStripMenuItem("貼上") { Enabled = CascadeClipboard.ContainsNodes() && (canPasteBeside || canPasteAsChild) };
                    pasteMenu.DropDownItems.Add(CreateCommand("之前", CascadeEditCommand.PasteBefore, canPasteBeside, () => PasteBeside(target, before: true)));
                    pasteMenu.DropDownItems.Add(CreateCommand("之後", CascadeEditCommand.Paste, canPasteBeside, () => PasteBeside(target, before: false)));
                    pasteMenu.DropDownItems.Add(CreateCommand("作為子節點", CascadeEditCommand.PasteAsChild, canPasteAsChild, () => PasteIntoLeaf(target)));
                    items.Add(pasteMenu);
                    items.Add(new ToolStripSeparator());

                    items.Add(CreateCommand("刪除", CascadeEditCommand.Delete, true, () => DeleteNodes(EditTargets(target))));
                }

                items.Add(new ToolStripSeparator());
            }
        }

        private void AddExpansionItems(ToolStripItemCollection items, MenuNode target)
        {
            if (!_state.IsInTree(target)) return;

            var hasChildren = !target.IsLeaf;
            var expandAll = new ToolStripMenuItem("全部展開")
            {
                ShortcutKeyDisplayString = "*",
                Enabled = hasChildren,
            };
            expandAll.Click += (sender, e) => SetSubtreesExpanded(EditTargets(target), expanded: true);

            var collapseAll = new ToolStripMenuItem("全部摺疊")
            {
                ShortcutKeyDisplayString = "/",
                Enabled = hasChildren,
            };
            collapseAll.Click += (sender, e) => SetSubtreesExpanded(EditTargets(target), expanded: false);

            items.Add(expandAll);
            items.Add(collapseAll);
            items.Add(new ToolStripSeparator());
        }

        private ToolStripMenuItem CreateCommand(string text, CascadeEditCommand command, bool enabled, Func<bool> action)
        {
            var item = new ToolStripMenuItem(text)
            {
                ShortcutKeyDisplayString = CascadeEditShortcuts.GetDisplayString(command),
                Enabled = enabled,
            };
            item.Click += (sender, e) => RunMenuAction(action);
            return item;
        }

        // 等功能表關掉之後才執行：刪除的確認框不會和正在關閉的功能表搶焦點。
        private void RunMenuAction(Func<bool> action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed) action();
            }));
        }

        private static ToolStripMenuItem CreateCommand(string text, Action apply, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { Enabled = enabled };
            item.Click += (sender, e) => apply();
            return item;
        }

        private static ToolStripMenuItem CreateChoice(string text, bool isChecked, Action apply)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (sender, e) => apply();
            return item;
        }

        #endregion 右鍵功能表
    }
}
