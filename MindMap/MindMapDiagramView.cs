using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControl.Views.Diagram;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MindMap
{
    /// <summary>
    /// 心智圖用的 <see cref="CascadeDiagramView"/>：Tab 插入子項、Enter 在下方插入、Ctrl+A 全選、Ctrl+滾輪縮放，表單功能表的快捷鍵優先於圖的編輯快捷鍵。
    /// </summary>
    internal sealed class MindMapDiagramView : CascadeDiagramView
    {
        // 縮放的級距（百分比）：Zoom In / Zoom Out 與 Ctrl+滾輪在相鄰級距間移動。
        private static readonly int[] ZoomSteps = { 25, 33, 50, 67, 75, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400 };

        private const int ACTUAL_SIZE_INDEX = 6;

        private int _zoomIndex = ACTUAL_SIZE_INDEX;

        // 還沒湊滿一格（WHEEL_DELTA）的滾輪量；觸控板一次只送一小段。
        private int _pendingWheelDelta;

        /// <summary>
        /// 目前的縮放百分比。
        /// </summary>
        public int ZoomPercent => ZoomSteps[_zoomIndex];

        public void ZoomIn() => SetZoomIndex(_zoomIndex + 1, anchor: null);

        public void ZoomOut() => SetZoomIndex(_zoomIndex - 1, anchor: null);

        public void ResetZoom() => SetZoomIndex(ACTUAL_SIZE_INDEX, anchor: null);

        /// <summary>
        /// 換到 <paramref name="index"/> 的級距（超出範圍時停在兩端）。
        /// </summary>
        /// <param name="anchor">縮放前後要留在原處的控制項座標（滾輪縮放時是游標）；null 時保持左上角。</param>
        private void SetZoomIndex(int index, Point? anchor)
        {
            index = Math.Max(0, Math.Min(index, ZoomSteps.Length - 1));
            if (index == _zoomIndex) return;

            var oldFactor = ZoomFactor;
            var scroll = AutoScrollPosition;
            _zoomIndex = index;
            ZoomFactor = ZoomPercent / 100f;

            if (anchor is Point point)
            {
                // 圖上原本在 point 底下的位置，放大後仍在 point 底下。AutoScrollPosition 讀出來是負的，設定時給正的。
                var ratio = ZoomFactor / oldFactor;
                AutoScrollPosition = new Point(
                    (int)Math.Round(((point.X - scroll.X) * ratio) - point.X),
                    (int)Math.Round(((point.Y - scroll.Y) * ratio) - point.Y));
            }
        }

        /// <remarks>按住 Ctrl 時滾輪縮放（往上放大、往下縮小），以游標為中心；沒按 Ctrl 照常捲動。</remarks>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0)
            {
                _pendingWheelDelta = 0;
                base.OnMouseWheel(e);
                return;
            }

            if (e is HandledMouseEventArgs handled) handled.Handled = true;

            _pendingWheelDelta += e.Delta;
            var steps = _pendingWheelDelta / SystemInformation.MouseWheelScrollDelta;
            if (steps == 0) return;

            _pendingWheelDelta -= steps * SystemInformation.MouseWheelScrollDelta;
            SetZoomIndex(_zoomIndex + steps, e.Location);
        }

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
