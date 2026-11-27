using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControl.Views.Diagram;
using System.Linq;
using System.Windows.Forms;

namespace MindMap
{
    /// <summary>
    /// 心智圖用的 <see cref="CascadeDiagramView"/>：Tab 插入子項、Enter 在下方插入、Ctrl+A 全選，左鍵點節點就選取，表單功能表的快捷鍵優先於圖的編輯快捷鍵。
    /// </summary>
    internal sealed class MindMapDiagramView : CascadeDiagramView
    {
        /// <remarks>基底點節點只觸發事件、不改選取；心智圖左鍵點節點就選取它（畫紅框）。</remarks>
        protected override void OnNodeMouseClick(CascadeNodeMouseEventArgs e)
        {
            base.OnNodeMouseClick(e);
            if (e.Button == MouseButtons.Left) TrySelectPath(e.Path);
        }

        /// <summary>
        /// 在選取的節點底下最後面插入子項：葉節點直接建立子節點；已經有子項時選取最後一個子項，在它後面插入。
        /// </summary>
        /// <returns>有開啟編輯框時為 true。</returns>
        public bool BeginInsertChild()
        {
            if (!(SelectedNode is MenuNode node)) return false;
            if (node.IsLeaf) return BeginCreateChild();

            return TrySelectNode(node.Children[node.Children.Count - 1]) && BeginInsert(before: false);
        }

        /// <remarks>
        /// 就地編輯時不攔，按鍵照基底的規則交給編輯框與表單。
        /// 功能表快捷鍵要先查：基底會把 Ctrl+N 當成建立子節點，在這裡是 File &gt; New。
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!IsEditing)
            {
                var menuItem = FindMenuItem(FindForm()?.MainMenuStrip?.Items, keyData);
                if (menuItem != null)
                {
                    menuItem.PerformClick();
                    return true;
                }

                switch (keyData)
                {
                    case Keys.Tab:
                        BeginInsertChild();
                        return true;

                    case Keys.Enter:
                        BeginInsert(before: false);
                        return true;

                    case Keys.Control | Keys.A:
                        SelectAll();
                        return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static ToolStripMenuItem FindMenuItem(ToolStripItemCollection items, Keys shortcut)
        {
            if (items == null) return null;

            foreach (var item in items.OfType<ToolStripMenuItem>())
            {
                if (item.Enabled && item.ShortcutKeys == shortcut) return item;

                var child = FindMenuItem(item.DropDownItems, shortcut);
                if (child != null) return child;
            }

            return null;
        }
    }
}
