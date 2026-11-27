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
        /// Ctrl+點節點切換它是否多選；取消時它多選的子孫一起取消。還沒有多選時，選取的節點也算進去（和 Columns 同一欄的 Ctrl+點選一樣），
        /// 所以選了一個節點再 Ctrl+點另一個，兩個都會多選。Ctrl+點選不觸發 <see cref="NodeMouseClick"/>，也不改選取。
        /// 不按 Ctrl 點節點或空白處、按 Esc 取消多選；被收合、隱藏或不在範圍內而沒畫出來的節點自動移出多選。
        /// </para>
        /// <para>
        /// 有多選時，<see cref="Copy"/> 複製多選的節點（父節點也在多選裡的子孫不重複複製），<see cref="CopyPath"/> 複製每個多選節點的路徑；
        /// <see cref="Cut"/>、<see cref="Delete"/> 剪下或刪除多選的節點（可以屬於不同的父節點，一次復原全部回來）；
        /// 右鍵點在多選的節點上時，功能表的「複製」、「複製(路徑)」、「剪下」、「刪除」也是。貼上的位置跟著多選，見 <see cref="Paste"/>。
        /// </para>
        /// <para>
        /// 不是唯讀時，按住節點拖到另一個節點上放開，節點（連同子孫）移到它的子節點最後面（<see cref="CascadeControl.State.CascadeState.MoveNodes(IReadOnlyList{MenuNode}, MenuNode, int)"/>）；
        /// 拖到兄弟之間或首尾邊緣時插入同層指定位置；按住 Ctrl 拖曳改為複製。按住的是多選的節點時移動或複製整個多選，否則只操作按住的那一個。
        /// 拖曳時會標示被按住的節點，預覽結果以半透明顯示在新位置（移動時原處調淡、虛線外框並畫紅叉，複製時原處維持原樣）。按 Esc 取消。
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

        /// <summary>
        /// 多選圖上畫出來的所有節點；被收合或不在 <see cref="DrawScope"/> 範圍內的節點不算。
        /// </summary>
        public void SelectAll() => SetMarks(_image?.Nodes.Select(imageNode => imageNode.Node) ?? NoMenuNodes);

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

        // Ctrl+點選：加入，或連同多選的子孫一起取消。以 SeedMarks 為起點。
        private void ToggleMark(MenuNode node)
        {
            var marks = SeedMarks();
            SetMarks(marks.Contains(node) ? marks.Where(mark => !IsSelfOrAncestor(node, mark)) : marks.Concat(new[] { node }));
        }

        // Ctrl 手勢的起點：有多選就是多選；沒有時是畫在圖上的選取節點（它本來就畫成選取色），和 Columns 的 SeedMarks 一樣。
        private List<MenuNode> SeedMarks()
        {
            if (_marks.Count > 0) return _marks.ToList();

            var selected = SelectedMenuNode;
            return FindImageNode(selected) != null ? new List<MenuNode> { selected } : new List<MenuNode>();
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

        // 貼上的位置：有多選時是多選的最後一個（貼在後面）或第一個（貼在前面），否則是選取的節點。
        private MenuNode PasteAnchor(bool before)
        {
            if (_marks.Count == 0) return SelectedMenuNode;

            var tops = TopMostMarks();
            return tops.Count == 0 ? SelectedMenuNode : before ? tops[0] : tops[tops.Count - 1];
        }

        // 任何綁同一份 State 的檢視改了樹：回到樹裡的節點不只一個時（貼上、復原刪除、重做貼上）全部多選，否則取消多選。
        // State 先重新整理（重畫）才觸發 TreeEdited，所以這時節點已經畫在圖上。
        private void State_TreeEdited(object sender, CascadeControl.State.CascadeTreeEditedEventArgs e)
        {
            var nodes = e.Nodes;
            SetMarks(nodes.Count > 1 && nodes.All(_state.IsInTree) ? nodes : NoMenuNodes);
        }

        // 複製、剪下、刪除、拖曳移動的對象：有多選時是多選（祖先也被多選的子孫跟著祖先走），否則是選取的節點。
        private IReadOnlyList<MenuNode> EditTargets() => _marks.Count > 0 ? TopMostMarks() : Single(SelectedMenuNode);

        // 右鍵功能表的對象：點在多選的節點上就是整個多選，否則只有那個節點。
        private IReadOnlyList<MenuNode> EditTargets(MenuNode target) => _marks.Contains(target) ? TopMostMarks() : Single(target);

        private IReadOnlyList<MenuNode> CopyPathTargets(MenuNode target) => _marks.Contains(target) ? MultiSelectedNodes : Single(target);

        /// <summary>
        /// 左鍵按下：按住 Ctrl 時不論按在哪裡、沒按時按在空白處，開始一次多選手勢；
        /// 沒按 Ctrl 按在節點上可能是拖曳移動（見 <see cref="TryBeginMovePress"/>），沒拖曳就放開是一般點選。
        /// </summary>
        /// <param name="location">控制項的用戶區座標。</param>
        /// <param name="control">是否按住 Ctrl。</param>
        internal void BeginMarkPress(Point location, bool control)
        {
            var node = GetNodeAt(location);
            if (node != null && !control)
            {
                // 可能是拖曳移動；沒拖曳就放開是一般點選。
                _press = null;
                TryBeginMovePress(node, location, startedWithControl: false);
                return;
            }

            if (node != null && control && TryBeginMovePress(node, location, startedWithControl: true))
            {
                _press = null;
                return;
            }

            _press = new MarkPress(ToImagePoint(location), location, node, control ? SeedMarks() : new List<MenuNode>(), control);
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
            if (_image == null) return;

            var client = ClientSize;
            var dx = location.X < 0 ? location.X : Math.Max(0, location.X - client.Width);
            var dy = location.Y < 0 ? location.Y : Math.Max(0, location.Y - client.Height);
            if (dx == 0 && dy == 0) return;

            GetTransform(out _, out var offset);
            var size = _image.Image.Size;

            var shiftX = 0;
            if (dx < 0 && offset.X < 0f) shiftX = Math.Min(-dx, (int)Math.Ceiling(-offset.X));
            else if (dx > 0 && offset.X + size.Width > client.Width) shiftX = -Math.Min(dx, (int)Math.Ceiling(offset.X + size.Width - client.Width));

            var shiftY = 0;
            if (dy < 0 && offset.Y < 0f) shiftY = Math.Min(-dy, (int)Math.Ceiling(-offset.Y));
            else if (dy > 0 && offset.Y + size.Height > client.Height) shiftY = -Math.Min(dy, (int)Math.Ceiling(offset.Y + size.Height - client.Height));

            if (shiftX != 0 || shiftY != 0) ShiftViewport(shiftX, shiftY);
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

            /// <summary>按住 Ctrl 時是按下前的多選（見 SeedMarks），框選範圍加在它上面；否則是空的。</summary>
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
