using CascadeControl.Trees;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 自由平移：Shift + 左鍵拖曳，超過捲軸範圍（或圖比視窗小）時也能把圖移動。
    public partial class CascadeDiagramView
    {
        private const int PAN_VISIBLE_MARGIN = 48;

        // 超過捲軸範圍（或圖比視窗小）時的自由平移量。
        private Point _panOffset;
        private MenuTree _pannedTree;
        private bool _isPanning;
        private bool _swallowPanRelease;
        private Point _panStartClient;
        private Point _panStartScreenOffset;
        private bool _allowFreePan = true;

        /// <summary>
        /// 是否允許按住 Shift + 左鍵拖曳自由平移畫布：圖比視窗小或已捲到盡頭時也能把任意節點移到畫面中央（至少保留一小塊圖面在視窗內）。
        /// 預設為 true；設為 false 時只能用捲軸移動，既有的平移量會歸零。
        /// </summary>
        [DefaultValue(true)]
        [Category("Behavior")]
        [Description("是否允許按住 Shift + 左鍵拖曳自由平移畫布。")]
        public bool AllowFreePan
        {
            get => _allowFreePan;
            set
            {
                if (_allowFreePan == value) return;

                _allowFreePan = value;
                if (value) return;

                EndPan(restore: false, cursorLocation: null);
                if (_panOffset == Point.Empty) return;

                _panOffset = Point.Empty;
                NotifyPanChanged();
            }
        }

        /// <summary>
        /// 目前是否只按著 Shift（沒有 Alt）；測試可覆寫以模擬按鍵狀態。
        /// </summary>
        internal virtual bool IsPanKeyDown => (ModifierKeys & (Keys.Shift | Keys.Alt)) == Keys.Shift;

        private Point GetScreenOffset()
        {
            GetTransform(out _, out var offset);
            return Point.Round(offset);
        }

        // 將圖面原點移到 desiredScreenOffset：可捲動範圍內優先由 AutoScrollPosition 吸收，超出邊界由 _panOffset 承接。
        private void SetFreeScreenOffset(Point desiredScreenOffset)
        {
            if (_image == null) return;

            var client = ClientSize;
            var size = _image.Image.Size;
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
    }
}
