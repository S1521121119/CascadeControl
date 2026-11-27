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
    /// 心智圖用的 <see cref="CascadeDiagramView"/>：Tab 插入子項、Enter 在下方插入、Ctrl+A 全選、Shift+左鍵拖曳自由平移畫布，表單功能表的快捷鍵優先於圖的編輯快捷鍵。
    /// </summary>
    internal sealed class MindMapDiagramView : CascadeDiagramView
    {
        private const int PAN_VISIBLE_MARGIN = 48;

        // 超過捲軸範圍（或圖比視窗小）時的自由平移量。
        private Point _panOffset;
        private MenuTree _pannedTree;
        private bool _isPanning;
        private bool _swallowPanRelease;
        private Point _panStartClient;
        private Point _panStartScreenOffset;

        // 測試可指定 Shift 狀態；正式執行時為 null，直接讀 ModifierKeys。
        internal bool? ShiftKeyOverride { get; set; }

        private bool IsPanModifierOnly => ShiftKeyOverride ?? ((ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) == Keys.Shift);

        /// <inheritdoc />
        protected override Point PanOffset => _panOffset;

        /// <summary>
        /// 重設自由平移與捲動位置。
        /// </summary>
        public override void ResetPan()
        {
            var changed = _panOffset != Point.Empty;
            _panOffset = Point.Empty;
            if (AutoScrollPosition != Point.Empty)
            {
                AutoScrollPosition = Point.Empty;
                changed = true;
            }

            if (changed) NotifyPanChanged();
        }

        /// <inheritdoc />
        protected override void ShiftViewport(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;

            var current = GetScreenOffset();
            SetScreenOffset(new Point(current.X + dx, current.Y + dy));
        }

        /// <inheritdoc />
        protected override void OnImageUpdated()
        {
            if (!ReferenceEquals(Tree, _pannedTree))
            {
                _pannedTree = Tree;
                _panOffset = Point.Empty;
                if (AutoScrollPosition != Point.Empty) AutoScrollPosition = Point.Empty;
                return;
            }

            if (!IsZooming) ClampCurrentView();
        }

        /// <inheritdoc />
        protected override void ClampCurrentView()
        {
            if (_panOffset == Point.Empty) return;

            SetScreenOffset(GetScreenOffset());
        }

        private Point GetScreenOffset()
        {
            GetTransform(out _, out var offset);
            return Point.Round(offset);
        }

        // 將圖面原點移到 desiredScreenOffset：可捲動範圍內優先由 AutoScrollPosition 吸收，超出邊界由 _panOffset 承接。
        /// <inheritdoc />
        protected override void SetScreenOffset(Point desiredScreenOffset)
        {
            if (Image == null) return;

            var client = ClientSize;
            var size = Image.Size;
            if (client.Width <= 0 || client.Height <= 0 || size.Width <= 0 || size.Height <= 0) return;

            var clamped = ClampScreenOffset(desiredScreenOffset, size, client);
            GetTransform(out _, out var currentOffset);
            var verticalCenter = (int)Math.Round(currentOffset.Y - AutoScrollPosition.Y - _panOffset.Y);

            var targetX = clamped.X;
            var targetY = clamped.Y - verticalCenter;

            var desiredScroll = new Point(Math.Max(0, -targetX), Math.Max(0, -targetY));
            var oldScroll = AutoScrollPosition;
            if (desiredScroll != new Point(-oldScroll.X, -oldScroll.Y))
                AutoScrollPosition = desiredScroll;

            var actualScroll = AutoScrollPosition;
            var nextPan = new Point(targetX - actualScroll.X, targetY - actualScroll.Y);
            if (nextPan == _panOffset && actualScroll == oldScroll) return;

            _panOffset = nextPan;
            NotifyPanChanged();
        }

        private static Point ClampScreenOffset(Point offset, Size imageSize, Size clientSize)
        {
            var visibleX = Math.Min(PAN_VISIBLE_MARGIN, Math.Min(imageSize.Width, clientSize.Width / 2));
            var visibleY = Math.Min(PAN_VISIBLE_MARGIN, Math.Min(imageSize.Height, clientSize.Height / 2));

            var minX = visibleX - imageSize.Width;
            var maxX = clientSize.Width - visibleX;
            var minY = visibleY - imageSize.Height;
            var maxY = clientSize.Height - visibleY;

            return new Point(Clamp(offset.X, minX, maxX), Clamp(offset.Y, minY, maxY));
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private void BeginPan(Point clientLocation)
        {
            Focus();
            _isPanning = true;
            _swallowPanRelease = false;
            _panStartClient = clientLocation;
            _panStartScreenOffset = GetScreenOffset();
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        private void EndPan(bool restore, Point? cursorLocation)
        {
            if (!_isPanning) return;

            _isPanning = false;
            if (restore) SetScreenOffset(_panStartScreenOffset);
            if (Capture) Capture = false;
            Cursor = cursorLocation is Point p && GetNodeAt(p) != null ? Cursors.Hand : Cursors.Default;
        }

        /// <remarks>按住 Shift + 左鍵拖曳平移畫布，不觸發選取、展開、框選或搬移；編輯中則照常套用編輯。</remarks>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            _swallowPanRelease = false;
            if (!IsEditing && e.Button == MouseButtons.Left && IsPanModifierOnly)
            {
                BeginPan(e.Location);
                return;
            }

            base.OnMouseDown(e);
        }

        /// <inheritdoc />
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isPanning)
            {
                if ((e.Button & MouseButtons.Left) != 0)
                {
                    SetScreenOffset(new Point(
                        _panStartScreenOffset.X + (e.Location.X - _panStartClient.X),
                        _panStartScreenOffset.Y + (e.Location.Y - _panStartClient.Y)));
                }

                Cursor = Cursors.SizeAll;
                return;
            }

            base.OnMouseMove(e);
        }

        /// <inheritdoc />
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_isPanning || _swallowPanRelease) return;
            base.OnMouseClick(e);
        }

        /// <inheritdoc />
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (_isPanning || _swallowPanRelease) return;
            base.OnMouseDoubleClick(e);
        }

        /// <inheritdoc />
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && (_isPanning || _swallowPanRelease))
            {
                _swallowPanRelease = false;
                EndPan(restore: false, cursorLocation: e.Location);
                return;
            }

            base.OnMouseUp(e);
        }

        /// <inheritdoc />
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _isPanning) EndPan(restore: false, cursorLocation: null);
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
                if (_isPanning && keyData == Keys.Escape)
                {
                    EndPan(restore: true, cursorLocation: null);
                    _swallowPanRelease = true;
                    return true;
                }

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
