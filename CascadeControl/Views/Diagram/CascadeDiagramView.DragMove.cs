using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 拖曳移動：按住節點拖到另一個節點上放開，節點（連同子孫）接到它的子節點最後面；按住的是多選的節點時移動整個多選。
    public partial class CascadeDiagramView
    {
        // 左鍵按在節點上、放開前的拖曳移動；沒有時為 null。
        private MovePress _move;

        private bool IsMoveDragging => _move != null && _move.Dragged;

        /// <summary>
        /// 左鍵按在節點上（沒按 Ctrl、不是唯讀）：開始一次可能的拖曳移動，對象是整個多選（按在多選的節點上時）或只有這個節點。
        /// 沒拖曳就放開時照一般點選處理。
        /// </summary>
        /// <returns>有開始時為 true。</returns>
        private bool TryBeginMovePress(MenuNode node, Point location)
        {
            if (!CanEdit) return false;

            _move = new MovePress(location, _marks.Contains(node) ? TopMostMarks() : new[] { node });
            Capture = true;
            return true;
        }

        /// <summary>
        /// 拖曳中：找出滑鼠下的節點當成放下的目標，換目標時重畫預覽。
        /// </summary>
        /// <remarks>
        /// 預覽是移動之後的整張圖，排版會變；畫的時候整張位移，讓目標留在原本的位置，所以目標一直在滑鼠底下，不會因為排版改變而跳來跳去。
        /// 找目標時看的是畫面上的那張圖（有預覽就是預覽）：滑到預覽裡半透明的節點上時目標不變，滑到空白處時沒有目標。
        /// </remarks>
        private void DragMovePress(Point location)
        {
            var move = _move;
            if (!move.Dragged)
            {
                var drag = SystemInformation.DragSize;
                if (Math.Abs(location.X - move.StartClient.X) <= drag.Width / 2 && Math.Abs(location.Y - move.StartClient.Y) <= drag.Height / 2) return;

                move.Dragged = true;
            }

            ScrollTowards(location);

            var point = ToImagePoint(location);
            var shown = move.Preview ?? _image;
            var offset = move.Preview == null ? PointF.Empty : move.Offset;
            var hit = shown.Nodes.FirstOrDefault(imageNode => Offset(imageNode.Bounds, offset).Contains(point));

            if (hit == null) SetDropTarget(null, RectangleF.Empty);
            else if (!move.IsMoved(hit.Node)) SetDropTarget(hit.Node, Offset(hit.Bounds, offset));
            else if (move.Preview == null) SetDropTarget(null, RectangleF.Empty);

            Cursor = move.Target != null ? Cursors.Hand : Cursors.No;
        }

        // 放下的目標用選取色（和多選同一個藍色）框起來，一眼看得出會接到哪裡。
        private void PaintDropTarget(Graphics graphics)
        {
            var move = _move;
            var target = move.Preview.Nodes.FirstOrDefault(imageNode => ReferenceEquals(imageNode.Node, move.Target));
            if (target == null) return;

            var bounds = ToClientRectangle(Offset(target.Bounds, move.Offset));
            using (var border = new Pen(SystemColors.Highlight, 2f))
            {
                graphics.DrawRectangle(border, Rectangle.Inflate(bounds, 2, 2));
            }
        }

        private static RectangleF Offset(RectangleF bounds, PointF offset) => new RectangleF(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height);

        // 換放下的目標；shownBounds 是目標現在畫在畫面上的位置（圖座標），預覽要對齊到這裡。
        private void SetDropTarget(MenuNode target, RectangleF shownBounds)
        {
            var move = _move;
            if (ReferenceEquals(move.Target, target)) return;

            move.Target = target;
            move.Preview?.Dispose();
            move.Preview = null;
            move.Offset = PointF.Empty;

            if (target != null)
            {
                var preview = RenderTreeImage(new TreeDrawer.MovePreview(target, move.Nodes));
                var targetNode = preview.Nodes.FirstOrDefault(imageNode => ReferenceEquals(imageNode.Node, target));
                if (targetNode == null)
                {
                    preview.Dispose();
                    move.Target = null;
                }
                else
                {
                    move.Preview = preview;
                    move.Offset = new PointF(shownBounds.X - targetNode.Bounds.X, shownBounds.Y - targetNode.Bounds.Y);
                }
            }

            Invalidate();
        }

        /// <summary>
        /// 結束拖曳移動：<paramref name="apply"/> 為 true 而且有拖曳到目標上時交給 <see cref="CascadeControl.State.CascadeState.MoveNodes(IReadOnlyList{MenuNode}, MenuNode)"/>；否則什麼都不改。
        /// </summary>
        private void EndMovePress(bool apply)
        {
            var move = _move;
            _move = null;
            move.Preview?.Dispose();
            if (Capture) Capture = false;
            Cursor = Cursors.Default;
            Invalidate();

            if (apply && move.Dragged && move.Target != null && CanEdit) _state.MoveNodes(move.Nodes, move.Target);
        }

        /// <summary>
        /// 一次拖曳移動：要移動哪些節點、目前要放到哪個節點底下，以及預覽圖。
        /// </summary>
        private sealed class MovePress
        {
            private readonly HashSet<MenuNode> _roots;

            public MovePress(Point startClient, IReadOnlyList<MenuNode> nodes)
            {
                StartClient = startClient;
                Nodes = nodes;
                _roots = new HashSet<MenuNode>(nodes);
            }

            /// <summary>按下時的控制項座標，判斷有沒有拖曳用。</summary>
            public Point StartClient { get; }

            /// <summary>要移動的節點（祖先也被多選的子孫不算），依圖上的順序。</summary>
            public IReadOnlyList<MenuNode> Nodes { get; }

            public bool Dragged { get; set; }

            /// <summary>放下的目標；沒有時為 null。</summary>
            public MenuNode Target { get; set; }

            /// <summary>移到 <see cref="Target"/> 底下之後的樣子；沒有目標時為 null。</summary>
            public TreeImage Preview { get; set; }

            /// <summary>預覽圖畫在原本的圖座標上要位移多少，目標才會留在原處。</summary>
            public PointF Offset { get; set; }

            /// <summary>節點在被移動的子樹裡（不能當目標）。</summary>
            public bool IsMoved(MenuNode node)
            {
                for (var current = node; current != null; current = current.Parent)
                {
                    if (_roots.Contains(current)) return true;
                }

                return false;
            }
        }
    }
}
