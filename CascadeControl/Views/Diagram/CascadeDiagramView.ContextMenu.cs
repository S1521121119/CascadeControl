using CascadeControl.State;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 右鍵功能表：切換顯示設定。
    public partial class CascadeDiagramView
    {
        #region 右鍵功能表

        // 每次開啟前重建：沒開 ShowChecks 或 State 沒有勾選能力時不列出「未勾選節點」。
        private void ContextMenu_Opening(object sender, CancelEventArgs e)
        {
            var items = _contextMenu.Items;
            items.Clear();

            var scopeMenu = new ToolStripMenuItem("範圍");
            scopeMenu.DropDownItems.Add(CreateChoice("整棵樹", _drawScope == TreeDrawScope.WholeTree, () => DrawScope = TreeDrawScope.WholeTree));
            scopeMenu.DropDownItems.Add(CreateChoice("選取的子樹", _drawScope == TreeDrawScope.SelectedSubtree, () => DrawScope = TreeDrawScope.SelectedSubtree));
            items.Add(scopeMenu);

            if (_showChecks && _state is CascadeCheckableState)
            {
                var uncheckedMenu = new ToolStripMenuItem("未勾選節點");
                uncheckedMenu.DropDownItems.Add(CreateChoice("變淡", _uncheckedDisplay == UncheckedNodeDisplay.Dimmed, () => UncheckedDisplay = UncheckedNodeDisplay.Dimmed));
                uncheckedMenu.DropDownItems.Add(CreateChoice("隱藏", _uncheckedDisplay == UncheckedNodeDisplay.Hidden, () => UncheckedDisplay = UncheckedNodeDisplay.Hidden));
                items.Add(uncheckedMenu);
            }

            var displayMenu = new ToolStripMenuItem("顯示");
            displayMenu.DropDownItems.Add(CreateChoice("原始尺寸", _sizeMode == TreeImageSizeMode.ActualSize, () => SizeMode = TreeImageSizeMode.ActualSize));
            displayMenu.DropDownItems.Add(CreateChoice("自動縮放", _sizeMode == TreeImageSizeMode.Zoom, () => SizeMode = TreeImageSizeMode.Zoom));
            items.Add(displayMenu);

            // 清空項目後 ContextMenuStrip 會預設取消開啟，要明確允許。
            e.Cancel = false;
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