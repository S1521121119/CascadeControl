using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 繪製、座標轉換與捲動到節點。
    public partial class CascadeDiagramView
    {
        #region 繪製

        /// <summary>
        /// 依 <see cref="State"/> 目前的樹、選取與勾選重畫。
        /// </summary>
        /// <remarks>
        /// <see cref="CascadeDiagramDrawScope.SelectedSubtree"/> 沒有選取時畫成空白；<see cref="State"/> 的 <see cref="CascadeState.Tree"/> 為 null 時也畫成空白。
        /// 插入或建立子節點的編輯框開著時，新節點的位置另外畫一個空白方塊。
        /// </remarks>
        public void Redraw()
        {
            UpdateImage(RenderTreeImage(null));
        }

        // 依目前的樹、選取、勾選與展開畫圖；preview 不是 null 時畫成移動之後的樣子（拖曳移動時的預覽）。
        private TreeImage RenderTreeImage(TreeDrawer.MovePreview preview)
        {
            var tree = _state.Tree;
            SyncExpandTree();

            var selected = _state.SelectedNode as MenuNode;
            if (_drawScope == CascadeDiagramDrawScope.SelectedSubtree && selected == null) tree = null;

            var checkSet = DisplayedCheckSet;
            var isChecked = checkSet == null ? (Func<MenuNode, bool>)null : checkSet.IsChecked;

            return TreeDrawer.RenderImageWithPlaceholder(tree, Font, isChecked, _drawScope, _uncheckedDisplay, selected, IsExpanded, _edit?.Placeholder, preview, _zoomFactor, _showIndex, _showChildCount);
        }

        // ShowChecks 關閉時當成沒有勾選資訊。
        private MenuCheckSet DisplayedCheckSet => _showChecks ? (_state as CascadeCheckableState)?.CheckSet : null;

        private void UpdateImage(TreeImage image)
        {
            var old = _image;
            _image = image ?? TreeDrawer.RenderImage(null, Font);
            old?.Dispose();

            PruneMarks();
            UpdateScrollSize();
            OnImageUpdated();
            PositionEditBox();
            Invalidate();
        }

        /// <summary>
        /// 每次樹狀圖重畫並更新捲動大小後呼叫（在重新定位就地編輯框與重繪畫面之前）。
        /// </summary>
        /// <remarks>預設：換成另一棵樹時重設平移與捲動位置；否則縮放以外的重畫會把畫面夾回合法範圍（<see cref="AllowFreePan"/> 為 false 時不做事）。</remarks>
        protected virtual void OnImageUpdated()
        {
            if (!AllowFreePan) return;

            if (!ReferenceEquals(Tree, _pannedTree))
            {
                _pannedTree = Tree;
                _panOffset = Point.Empty;
                if (AutoScrollPosition != Point.Empty) AutoScrollPosition = Point.Empty;
                return;
            }

            if (!IsZooming) ClampCurrentView();
        }

        private void UpdateScrollSize()
        {
            AutoScrollMinSize = _image != null ? _image.Image.Size : Size.Empty;
        }

        /// <summary>
        /// 額外畫布平移量（控制項像素）；預設為自由平移的量，<see cref="AllowFreePan"/> 為 false 時是 <see cref="Point.Empty"/>。
        /// </summary>
        protected virtual Point PanOffset => _allowFreePan ? _panOffset : Point.Empty;

        /// <summary>
        /// 圖畫在控制項上的縮放比例與位置：控制項座標 = 圖座標 × <paramref name="scale"/> + <paramref name="offset"/>。
        /// </summary>
        /// <param name="scale">圖面縮放比例。</param>
        /// <param name="offset">圖面原點在控制項用戶區的偏移量。</param>
        protected void GetTransform(out float scale, out PointF offset)
        {
            if (_image == null)
            {
                scale = 1f;
                offset = AutoScrollPosition;
                return;
            }

            scale = 1f;
            var pan = PanOffset;
            offset = new PointF(
                AutoScrollPosition.X + pan.X,
                AutoScrollPosition.Y + VerticalCenterOffset(_image.Image.Height, ClientSize.Height) + pan.Y);
        }

        private static float VerticalCenterOffset(float contentHeight, int clientHeight)
        {
            return clientHeight > contentHeight ? (float)Math.Floor((clientHeight - contentHeight) / 2f) : 0f;
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null) return;

            GetTransform(out var scale, out var offset);

            if (scale < 1f)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }

            // 拖曳移動時畫預覽圖（比例和原本的圖相同，位移讓放下的目標留在原處）；多選的節點這時畫在預覽裡，不另外蓋顏色。
            var preview = _move?.Preview;
            if (preview != null)
            {
                var previewSize = preview.Image.Size;
                var origin = new PointF(offset.X + (_move.Offset.X * scale), offset.Y + (_move.Offset.Y * scale));
                e.Graphics.DrawImage(preview.Image, new RectangleF(origin.X, origin.Y, previewSize.Width * scale, previewSize.Height * scale));
                PaintDropTarget(e.Graphics);
                return;
            }

            var size = _image.Image.Size;
            e.Graphics.DrawImage(_image.Image, new RectangleF(offset.X, offset.Y, size.Width * scale, size.Height * scale));
            PaintMarks(e.Graphics);
        }

        /// <inheritdoc />
        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            PositionEditBox();
        }

        /// <inheritdoc />
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Redraw();
        }

        #endregion 繪製

        #region 座標轉換

        /// <summary>
        /// 控制項上的某一點落在哪個節點方塊裡，已考慮捲動位置與縮放。
        /// </summary>
        /// <param name="clientLocation">控制項的用戶區座標。</param>
        /// <returns>點到的節點；沒點到，或點到插入時佔位的空白方塊時為 null。</returns>
        public MenuNode GetNodeAt(Point clientLocation)
        {
            if (_image == null) return null;

            var node = _image.GetNodeAt(ToImagePoint(clientLocation));
            return ReferenceEquals(node, _edit?.Placeholder?.Node) ? null : node;
        }

        /// <summary>
        /// 控制項上的某一點落在哪個節點的展開鈕（[+] 或 [-]）上，已考慮捲動位置與縮放。
        /// </summary>
        /// <param name="clientLocation">控制項的用戶區座標。</param>
        /// <returns>展開鈕所屬的節點；沒點到時為 null。</returns>
        public MenuNode GetExpanderAt(Point clientLocation)
        {
            return _image?.GetExpanderAt(ToImagePoint(clientLocation));
        }

        /// <summary>
        /// 將控制項的用戶區座標換算為圖上座標，已考慮捲動位置、縮放與垂直置中。
        /// </summary>
        /// <param name="clientLocation">控制項的用戶區座標。</param>
        /// <returns>對應的圖上座標。</returns>
        protected PointF ToImagePoint(Point clientLocation)
        {
            GetTransform(out var scale, out var offset);
            return new PointF((clientLocation.X - offset.X) / scale, (clientLocation.Y - offset.Y) / scale);
        }

        private Rectangle GetClientBoundsOf(MenuNode node)
        {
            var imageNode = FindImageNode(node);
            if (imageNode == null) return Rectangle.Empty;

            GetTransform(out var scale, out var offset);
            var b = imageNode.Bounds;
            return Rectangle.Round(new RectangleF(
                (b.X * scale) + offset.X,
                (b.Y * scale) + offset.Y,
                b.Width * scale,
                b.Height * scale));
        }

        // 節點畫在圖上的那一格；沒畫出來（被收合、隱藏，或不在範圍內）時為 null。
        private TreeImageNode FindImageNode(MenuNode node)
        {
            return node == null || _image == null ? null : _image.Nodes.FirstOrDefault(imageNode => ReferenceEquals(imageNode.Node, node));
        }

        // 選取改變或按鍵後捲動到選取的節點時，方塊外圍多留的空白。
        private const int ENSURE_VISIBLE_MARGIN = 12;

        /// <summary>
        /// 平移畫面內容（正值往右／往下移動圖面）。
        /// </summary>
        /// <param name="dx">水平位移量（像素）。</param>
        /// <param name="dy">垂直位移量（像素）。</param>
        protected virtual void ShiftViewport(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;

            if (AllowFreePan)
            {
                var current = GetScreenOffset();
                SetScreenOffset(new Point(current.X + dx, current.Y + dy));
                return;
            }

            var target = new Point(Math.Max(0, -AutoScrollPosition.X - dx), Math.Max(0, -AutoScrollPosition.Y - dy));
            if (target == new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)) return;

            AutoScrollPosition = target;
            Invalidate();
        }

        /// <summary>
        /// 通知基底 <see cref="PanOffset"/> 已變更，重新對齊就地編輯框並重畫。
        /// </summary>
        protected void NotifyPanChanged()
        {
            PositionEditBox();
            Invalidate();
        }

        /// <summary>
        /// 捲動到選取節點的方塊完整露出（外圍多留 <see cref="ENSURE_VISIBLE_MARGIN"/>）。
        /// </summary>
        private void EnsureVisible(MenuNode node)
        {
            var imageNode = FindImageNode(node);
            if (imageNode == null) return;

            GetTransform(out _, out var offset);
            var bounds = RectangleF.Inflate(imageNode.Bounds, ENSURE_VISIBLE_MARGIN, ENSURE_VISIBLE_MARGIN);
            var left = bounds.Left + offset.X;
            var right = bounds.Right + offset.X;
            var top = bounds.Top + offset.Y;
            var bottom = bounds.Bottom + offset.Y;
            var client = ClientSize;

            var dx = 0;
            if (left < 0f) dx = (int)Math.Ceiling(-left);
            else if (right > client.Width) dx = (int)Math.Floor(client.Width - right);

            var dy = 0;
            if (top < 0f) dy = (int)Math.Ceiling(-top);
            else if (bottom > client.Height) dy = (int)Math.Floor(client.Height - bottom);

            if (dx == 0 && dy == 0) return;

            ShiftViewport(dx, dy);
            PositionEditBox();
        }

        #endregion 座標轉換
    }
}