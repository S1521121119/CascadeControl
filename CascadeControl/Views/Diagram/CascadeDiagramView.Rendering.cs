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
        /// <see cref="TreeDrawScope.SelectedSubtree"/> 沒有選取時畫成空白；<see cref="State"/> 的 <see cref="CascadeState.Tree"/> 為 null 時也畫成空白。
        /// 插入或建立子節點的編輯框開著時，新節點的位置另外畫一個空白方塊。
        /// </remarks>
        public void Redraw()
        {
            var tree = _state.Tree;
            SyncExpandTree();

            var selected = _state.SelectedNode as MenuNode;
            if (_drawScope == TreeDrawScope.SelectedSubtree && selected == null) tree = null;

            var checkSet = DisplayedCheckSet;
            var isChecked = checkSet == null ? (Func<MenuNode, bool>)null : checkSet.IsChecked;

            UpdateImage(TreeDrawer.RenderImageWithPlaceholder(tree, Font, isChecked, _drawScope, _uncheckedDisplay, selected, IsExpanded, _edit?.Placeholder));
        }

        // ShowChecks 關閉時當成沒有勾選資訊。
        private MenuCheckSet DisplayedCheckSet => _showChecks ? (_state as CascadeCheckableState)?.CheckSet : null;

        private void UpdateImage(TreeImage image)
        {
            var old = _image;
            _image = image ?? TreeDrawer.RenderImage(null, Font);
            old?.Dispose();

            UpdateScrollSize();
            PositionEditBox();
            Invalidate();
        }

        private void UpdateScrollSize()
        {
            AutoScrollMinSize = _sizeMode == TreeImageSizeMode.ActualSize && _image != null ? _image.Image.Size : Size.Empty;
        }

        /// <summary>
        /// 圖畫在控制項上的縮放比例與位置：控制項座標 = 圖座標 × <paramref name="scale"/> + <paramref name="offset"/>。
        /// </summary>
        private void GetTransform(out float scale, out PointF offset)
        {
            if (_sizeMode == TreeImageSizeMode.ActualSize || _image == null)
            {
                scale = 1f;
                offset = AutoScrollPosition;
                return;
            }

            // 自動縮放只縮小不放大；控制項被縮到 0 時照原始尺寸算，避免除以 0。
            var size = _image.Image.Size;
            var client = ClientSize;
            scale = Math.Min(1f, Math.Min(client.Width / (float)size.Width, client.Height / (float)size.Height));
            if (scale <= 0f) scale = 1f;
            offset = PointF.Empty;
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null) return;

            GetTransform(out var scale, out var offset);
            var size = _image.Image.Size;

            if (scale < 1f)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }

            e.Graphics.DrawImage(_image.Image, new RectangleF(offset.X, offset.Y, size.Width * scale, size.Height * scale));
        }

        /// <inheritdoc />
        /// <remarks>自動縮放時比例跟著大小變，就地編輯框要重新對齊節點方塊。</remarks>
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

        private PointF ToImagePoint(Point clientLocation)
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
        /// 原始尺寸模式下，捲動到選取節點的方塊完整露出（外圍多留 <see cref="ENSURE_VISIBLE_MARGIN"/>）；自動縮放時整張都看得到，不用捲。
        /// </summary>
        private void EnsureVisible(MenuNode node)
        {
            if (_sizeMode != TreeImageSizeMode.ActualSize) return;

            var imageNode = FindImageNode(node);
            if (imageNode == null) return;

            var bounds = RectangleF.Inflate(imageNode.Bounds, ENSURE_VISIBLE_MARGIN, ENSURE_VISIBLE_MARGIN);
            var client = ClientSize;

            // AutoScrollPosition 讀出來是負的，設定時要給正的。
            var x = -AutoScrollPosition.X;
            var y = -AutoScrollPosition.Y;

            if (bounds.Left < x) x = (int)Math.Floor(bounds.Left);
            else if (bounds.Right > x + client.Width) x = (int)Math.Ceiling(bounds.Right) - client.Width;

            if (bounds.Top < y) y = (int)Math.Floor(bounds.Top);
            else if (bounds.Bottom > y + client.Height) y = (int)Math.Ceiling(bounds.Bottom) - client.Height;

            var target = new Point(Math.Max(0, x), Math.Max(0, y));
            if (target == new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)) return;

            AutoScrollPosition = target;
            Invalidate();
        }

        #endregion 座標轉換
    }
}