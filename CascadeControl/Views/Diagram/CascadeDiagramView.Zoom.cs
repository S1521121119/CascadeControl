using System;
using System.Drawing;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 縮放：級距、選單（以可視區中心為錨點）、Ctrl+滾輪（以游標為錨點）、符合視窗大小。
    public partial class CascadeDiagramView
    {
        /// <summary>
        /// 縮放的級距（百分比）：<see cref="ZoomIn"/>、<see cref="ZoomOut"/> 與 Ctrl+滾輪在相鄰級距間移動。
        /// </summary>
        protected static readonly int[] ZoomSteps = { 20, 25, 33, 50, 67, 75, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500 };

        // 還沒湊滿一格（WHEEL_DELTA）的滾輪量；觸控板一次只送一小段。
        private int _pendingWheelDelta;

        // 縮放重畫期間不要讓 OnImageUpdated 先把畫面夾回去，等錨點位置算好再處理。
        private bool _isZooming;

        /// <summary>
        /// 目前的縮放百分比（<see cref="ZoomFactor"/> × 100，四捨五入）。
        /// </summary>
        public int ZoomPercent => (int)Math.Round(_zoomFactor * 100f);

        /// <summary>
        /// 縮放造成的重畫期間為 true；子類別的 <see cref="OnImageUpdated"/> 可據此略過夾回範圍。
        /// </summary>
        protected bool IsZooming => _isZooming;

        /// <summary>
        /// 放大一個級距，以可視區中心為錨點。
        /// </summary>
        public void ZoomIn() => StepZoom(1, null);

        /// <summary>
        /// 縮小一個級距，以可視區中心為錨點。
        /// </summary>
        public void ZoomOut() => StepZoom(-1, null);

        /// <summary>
        /// 回到 100%，以可視區中心為錨點，並重設平移。
        /// </summary>
        public void ResetZoom()
        {
            SetZoomPercent(100, null);
            ResetPan();
        }

        /// <summary>
        /// 縮放到整張圖剛好放進視窗（限制在最小與最大級距之間），一次性套用，不隨視窗大小自動調整。
        /// </summary>
        public void FitToWindow()
        {
            if (_image == null) return;

            var client = ClientSize;
            var size = _image.Image.Size;
            if (client.Width <= 0 || client.Height <= 0 || size.Width <= 0 || size.Height <= 0) return;

            // 圖目前是以 ZoomFactor 倍率畫的，換算回 100% 的尺寸。
            var baseWidth = size.Width / _zoomFactor;
            var baseHeight = size.Height / _zoomFactor;
            var percent = (int)Math.Floor(Math.Min(client.Width / baseWidth, client.Height / baseHeight) * 100f);
            SetZoomPercent(Math.Max(ZoomSteps[0], Math.Min(percent, ZoomSteps[ZoomSteps.Length - 1])), null);
            ResetPan();
        }

        /// <summary>
        /// 重設自由平移與捲動位置。
        /// </summary>
        public virtual void ResetPan()
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

        /// <summary>
        /// 把圖面原點移到控制項座標 <paramref name="desiredScreenOffset"/>（縮放錨點用）；<see cref="AllowFreePan"/> 為 true 時超出捲軸範圍的部分由自由平移承接，否則只能用捲軸吸收。
        /// </summary>
        /// <param name="desiredScreenOffset">圖面原點在控制項用戶區的目標位置。</param>
        protected virtual void SetScreenOffset(Point desiredScreenOffset)
        {
            if (AllowFreePan)
            {
                SetFreeScreenOffset(desiredScreenOffset);
                return;
            }

            GetTransform(out _, out var current);
            var scroll = AutoScrollPosition;
            var target = new Point(
                Math.Max(0, -scroll.X - (int)Math.Round(desiredScreenOffset.X - current.X)),
                Math.Max(0, -scroll.Y - (int)Math.Round(desiredScreenOffset.Y - current.Y)));
            AutoScrollPosition = target;
            NotifyPanChanged();
        }

        /// <summary>
        /// 縮放或視窗大小改變後讓目前的畫面維持在合法範圍；沒有自由平移量時不做事。
        /// </summary>
        protected virtual void ClampCurrentView()
        {
            if (_panOffset == Point.Empty) return;

            SetScreenOffset(GetScreenOffset());
        }

        // 往 direction 方向換到下一個級距（目前不在級距上時，先到最近的級距）。
        private void StepZoom(int direction, Point? anchor)
        {
            var current = ZoomPercent;
            var target = direction > 0 ? ZoomSteps[ZoomSteps.Length - 1] : ZoomSteps[0];
            if (direction > 0)
            {
                foreach (var step in ZoomSteps)
                {
                    if (step > current)
                    {
                        target = step;
                        break;
                    }
                }
            }
            else
            {
                for (var i = ZoomSteps.Length - 1; i >= 0; i--)
                {
                    if (ZoomSteps[i] < current)
                    {
                        target = ZoomSteps[i];
                        break;
                    }
                }
            }

            SetZoomPercent(target, anchor);
        }

        // 縮放前後 anchor 所指的圖上座標不變；anchor 為 null 時用可視區中心。
        private void SetZoomPercent(int percent, Point? anchor)
        {
            var newFactor = percent / 100f;
            if (Math.Abs(newFactor - _zoomFactor) < 0.0001f) return;

            var oldFactor = _zoomFactor;
            var client = ClientSize;
            var screenAnchor = anchor ?? new Point(client.Width / 2, client.Height / 2);

            // 錨點在圖外（例如小圖旁的空白）時，改用圖邊上最近的點，避免縮放時圖被拉走。
            PointF? anchorImage = null;
            if (_image != null)
            {
                var p = ToImagePoint(screenAnchor);
                var size = _image.Image.Size;
                var clamped = new PointF(Math.Max(0f, Math.Min(p.X, size.Width)), Math.Max(0f, Math.Min(p.Y, size.Height)));
                GetTransform(out var oldScale, out var oldOffset);
                screenAnchor = new Point(
                    (int)Math.Round((clamped.X * oldScale) + oldOffset.X),
                    (int)Math.Round((clamped.Y * oldScale) + oldOffset.Y));
                anchorImage = clamped;
            }

            _isZooming = true;
            try
            {
                ZoomFactor = newFactor;
            }
            finally
            {
                _isZooming = false;
            }

            if (anchorImage is PointF imagePoint)
            {
                var ratio = _zoomFactor / oldFactor;
                SetScreenOffset(new Point(
                    (int)Math.Round(screenAnchor.X - (imagePoint.X * ratio)),
                    (int)Math.Round(screenAnchor.Y - (imagePoint.Y * ratio))));
            }
            else
            {
                ClampCurrentView();
            }
        }

        /// <remarks>按住 Ctrl 時滾輪縮放（往上放大、往下縮小），以游標為錨點；沒按 Ctrl 照常捲動。</remarks>
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
            var direction = steps > 0 ? 1 : -1;
            for (var i = 0; i < Math.Abs(steps); i++) StepZoom(direction, e.Location);
        }
    }
}