using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 多選：拖曳框選、Ctrl+點選切換，配色和 Columns 的選取一樣用系統選取色（藍）。多選決定 Copy / CopyPath 的對象。
    public partial class CascadeDiagramView
    {
        // 多選的節點；只留畫在圖上的（見 PruneMarks）。
        private readonly HashSet<MenuNode> _marks = new HashSet<MenuNode>();

        // 左鍵按下後、放開前的多選手勢；沒有時為 null。
        private MarkPress _press;

        /// <summary>
        /// 多選的節點，依圖上的前序排列（父節點在子節點之前）；沒有多選時為空集合。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 從空白處拖曳、或按住 Ctrl 拖曳是框選：方塊碰到框的節點都選進來，按住 Ctrl 時加進原本的多選，否則取代。
        /// Ctrl+點節點切換它是否多選；取消時它多選的子孫一起取消。Ctrl+點選不觸發 <see cref="NodeMouseClick"/>。
        /// 不按 Ctrl 點節點或空白處、按 Esc 取消多選；被收合、隱藏或不在範圍內而沒畫出來的節點自動移出多選。
        /// </para>
        /// <para>
        /// 有多選時，<see cref="Copy"/> 複製多選的節點（父節點也在多選裡的子孫不重複複製），<see cref="CopyPath"/> 複製每個多選節點的路徑；
        /// 右鍵點在多選的節點上時，功能表的「複製」、「複製(路徑)」也是。剪下、刪除、貼上仍然針對單一節點。
        /// </para>
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<MenuNode> MultiSelectedNodes =>
            _marks.Count == 0 || _image == null ? NoMenuNodes : _image.Nodes.Select(imageNode => imageNode.Node).Where(_marks.Contains).ToList();

        /// <summary>
        /// <see cref="MultiSelectedNodes"/> 改變後觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler MultiSelectionChanged;

        /// <summary>
        /// 觸發 <see cref="MultiSelectionChanged"/>。
        /// </summary>
        protected virtual void OnMultiSelectionChanged(EventArgs e) => MultiSelectionChanged?.Invoke(this, e);

        /// <summary>
        /// 取消多選；本來就沒有多選時不做任何事。
        /// </summary>
        public void ClearMultiSelection() => SetMarks(NoMenuNodes);

        private void SetMarks(IEnumerable<MenuNode> nodes)
        {
            var next = new HashSet<MenuNode>(nodes);
            if (_marks.SetEquals(next)) return;

            _marks.Clear();
            _marks.UnionWith(next);
            Invalidate();
            OnMultiSelectionChanged(EventArgs.Empty);
        }

        // 圖換了之後只留還畫在圖上的節點。
        private void PruneMarks()
        {
            if (_marks.Count > 0) SetMarks(MultiSelectedNodes);
        }

        // Ctrl+點選：加入，或連同多選的子孫一起取消。
        private void ToggleMark(MenuNode node)
        {
            if (!_marks.Contains(node))
            {
                SetMarks(_marks.Concat(new[] { node }));
                return;
            }

            SetMarks(_marks.Where(mark => !IsSelfOrAncestor(node, mark)));
        }

        private static bool IsSelfOrAncestor(MenuNode ancestor, MenuNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }

            return false;
        }

        // 複製的對象：多選裡祖先沒被多選的節點（祖先複製時已經帶著子孫）。
        private IReadOnlyList<MenuNode> TopMostMarks() =>
            MultiSelectedNodes.Where(node => !_marks.Any(mark => !ReferenceEquals(mark, node) && IsSelfOrAncestor(mark, node))).ToList();

        // 右鍵功能表的複製對象：點在多選的節點上就是整個多選，否則只有那個節點。
        private IReadOnlyList<MenuNode> CopyTargets(MenuNode target) => _marks.Contains(target) ? TopMostMarks() : Single(target);

        private IReadOnlyList<MenuNode> CopyPathTargets(MenuNode target) => _marks.Contains(target) ? MultiSelectedNodes : Single(target);

        /// <summary>
        /// 左鍵按下：按住 Ctrl 時不論按在哪裡、沒按時按在空白處，開始一次多選手勢；按在節點上又沒按 Ctrl 時是一般點選，不在這裡處理。
        /// </summary>
        /// <param name="location">控制項的用戶區座標。</param>
        /// <param name="control">是否按住 Ctrl。</param>
        internal void BeginMarkPress(Point location, bool control)
        {
            var node = GetNodeAt(location);
            if (node != null && !control)
            {
                _press = null;
                return;
            }

            _press = new MarkPress(ToImagePoint(location), location, node, control ? _marks.ToList() : new List<MenuNode>(), control);
            Capture = true;
        }

        // 拖曳超過系統的拖曳距離才算框選；拖到控制項外面時往那個方向捲動。
        private void DragMarkPress(Point location)
        {
            var press = _press;
            if (!press.Dragged)
            {
                var drag = SystemInformation.DragSize;
                if (Math.Abs(location.X - press.StartClient.X) <= drag.Width / 2 && Math.Abs(location.Y - press.StartClient.Y) <= drag.Height / 2) return;

                press.Dragged = true;
            }

            ScrollTowards(location);
            press.Current = ToImagePoint(location);

            var band = press.ImageBand;
            var hits = _image.Nodes
                .Where(imageNode => !ReferenceEquals(imageNode.Node, _edit?.Placeholder?.Node) && imageNode.Bounds.IntersectsWith(band))
                .Select(imageNode => imageNode.Node);
            SetMarks(press.BaseMarks.Concat(hits));
            Invalidate();
        }

        private void ScrollTowards(Point location)
        {
            if (_sizeMode != CascadeDiagramSizeMode.ActualSize) return;

            var client = ClientSize;
            var dx = location.X < 0 ? location.X : Math.Max(0, location.X - client.Width);
            var dy = location.Y < 0 ? location.Y : Math.Max(0, location.Y - client.Height);
            if (dx == 0 && dy == 0) return;

            AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X + dx), Math.Max(0, -AutoScrollPosition.Y + dy));
        }

        // 放開左鍵：拖曳過的多選已經在拖曳中算好；沒拖曳時 Ctrl+點節點切換它，沒按 Ctrl 點空白處取消多選。
        private void EndMarkPress()
        {
            var press = _press;
            _press = null;
            if (Capture) Capture = false;

            if (press.Dragged) Invalidate();
            else if (press.Control && press.StartNode != null) ToggleMark(press.StartNode);
            else if (!press.Control) ClearMultiSelection();
        }

        // 多選的節點蓋一層半透明的選取色、外框是實心的選取色；拖曳中的框選範圍同樣配色，底色更淡。
        private void PaintMarks(Graphics graphics)
        {
            var highlight = SystemColors.Highlight;

            if (_marks.Count > 0)
            {
                using (var fill = new SolidBrush(Color.FromArgb(MARK_FILL_ALPHA, highlight)))
                using (var border = new Pen(highlight, 2f))
                {
                    foreach (var node in MultiSelectedNodes)
                    {
                        var bounds = GetClientBoundsOf(node);
                        graphics.FillRectangle(fill, bounds);
                        graphics.DrawRectangle(border, Rectangle.Inflate(bounds, 1, 1));
                    }
                }
            }

            if (_press == null || !_press.Dragged) return;

            var band = ToClientRectangle(_press.ImageBand);
            using (var fill = new SolidBrush(Color.FromArgb(BAND_FILL_ALPHA, highlight)))
            using (var border = new Pen(highlight))
            {
                graphics.FillRectangle(fill, band);
                graphics.DrawRectangle(border, band);
            }
        }

        private const int MARK_FILL_ALPHA = 80;
        private const int BAND_FILL_ALPHA = 40;

        private Rectangle ToClientRectangle(RectangleF imageBounds)
        {
            GetTransform(out var scale, out var offset);
            return Rectangle.Round(new RectangleF(
                (imageBounds.X * scale) + offset.X,
                (imageBounds.Y * scale) + offset.Y,
                imageBounds.Width * scale,
                imageBounds.Height * scale));
        }

        /// <summary>
        /// 一次左鍵按壓的多選手勢。起點記在圖座標上，拖到邊緣自動捲動時起點跟著圖一起移動。
        /// </summary>
        private sealed class MarkPress
        {
            public MarkPress(PointF start, Point startClient, MenuNode startNode, List<MenuNode> baseMarks, bool control)
            {
                Start = start;
                Current = start;
                StartClient = startClient;
                StartNode = startNode;
                BaseMarks = baseMarks;
                Control = control;
            }

            public PointF Start { get; }

            public PointF Current { get; set; }

            /// <summary>按下時的控制項座標，判斷有沒有拖曳用。</summary>
            public Point StartClient { get; }

            /// <summary>按下的節點；按在空白處時為 null。</summary>
            public MenuNode StartNode { get; }

            /// <summary>按住 Ctrl 時是按下前的多選，框選範圍加在它上面；否則是空的。</summary>
            public List<MenuNode> BaseMarks { get; }

            public bool Control { get; }

            public bool Dragged { get; set; }

            /// <summary>框選範圍，圖座標。</summary>
            public RectangleF ImageBand => RectangleF.FromLTRB(
                Math.Min(Start.X, Current.X),
                Math.Min(Start.Y, Current.Y),
                Math.Max(Start.X, Current.X),
                Math.Max(Start.Y, Current.Y));
        }
    }
}
