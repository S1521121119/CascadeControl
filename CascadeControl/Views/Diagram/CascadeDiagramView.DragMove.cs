using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    public partial class CascadeDiagramView
    {
        private MovePress _move;

        private bool IsMoveDragging => _move != null && _move.Dragged;

        internal virtual bool IsControlKeyDown => (ModifierKeys & Keys.Control) != 0;

        private bool TryBeginMovePress(MenuNode node, Point location, bool startedWithControl)
        {
            if (!CanEdit) return false;

            _move = new MovePress(location, node, _marks.Contains(node) ? TopMostMarks() : new[] { node }, startedWithControl);
            Capture = true;
            return true;
        }

        private void DragMovePress(Point location)
        {
            var move = _move;
            if (!move.Dragged)
            {
                var drag = SystemInformation.DragSize;
                if (Math.Abs(location.X - move.StartClient.X) <= drag.Width / 2 && Math.Abs(location.Y - move.StartClient.Y) <= drag.Height / 2) return;

                move.Dragged = true;
                if (!_marks.Contains(move.PressedNode)) ClearMultiSelection();
                if (_drawScope == CascadeDiagramDrawScope.WholeTree) _state.TrySelectNode(move.PressedNode);
            }

            move.LastClient = location;
            ScrollTowards(location);
            UpdateMoveDropTarget(location);
        }

        private void UpdateMoveDropTarget(Point location)
        {
            var move = _move;
            if (move == null || !move.Dragged || _image == null) return;

            var isCopy = IsControlKeyDown;
            var point = ToImagePoint(location);
            var shown = move.Preview ?? _image;
            var offset = move.Preview == null ? PointF.Empty : move.Offset;

            var hit = shown.Nodes.FirstOrDefault(imageNode => Offset(imageNode.Bounds, offset).Contains(point));
            if (hit != null)
            {
                if (hit.IsGhost)
                {
                    if (move.Destination != null && move.Destination.IsCopy != isCopy)
                    {
                        var updated = ValidateDestination(move, move.Destination.WithCopy(isCopy));
                        if (updated != null)
                        {
                            var anchorNode = FindStationaryNode(shown, updated.Anchor);
                            SetDropDestination(updated, anchorNode != null ? Offset(anchorNode.Bounds, offset) : RectangleF.Empty);
                        }
                        else
                        {
                            SetDropDestination(null, RectangleF.Empty);
                        }
                    }

                    Cursor = move.Destination != null ? Cursors.Hand : Cursors.No;
                    return;
                }

                if (hit.IsLeaving)
                {
                    SetDropDestination(null, RectangleF.Empty);
                    Cursor = Cursors.No;
                    return;
                }

                var bounds = Offset(hit.Bounds, offset);
                var candidate = BuildNodeHitDestination(shown, hit.Node, bounds, point, isCopy);
                var valid = ValidateDestination(move, candidate);
                SetDropDestination(valid, valid != null ? bounds : RectangleF.Empty);
                Cursor = move.Destination != null ? Cursors.Hand : Cursors.No;
                return;
            }

            if (TryFindSiblingGapDestination(shown, offset, point, isCopy, out var gapDestination, out var gapAnchorBounds))
            {
                var valid = ValidateDestination(move, gapDestination);
                SetDropDestination(valid, valid != null ? gapAnchorBounds : RectangleF.Empty);
            }
            else
            {
                SetDropDestination(null, RectangleF.Empty);
            }

            Cursor = move.Destination != null ? Cursors.Hand : Cursors.No;
        }

        private DropDestination BuildNodeHitDestination(TreeImage shown, MenuNode hitNode, RectangleF bounds, PointF point, bool isCopy)
        {
            var edgeMargin = bounds.Height * 0.2f;
            if (CanInsertSibling(shown, hitNode))
            {
                if (point.Y < bounds.Top + edgeMargin)
                    return new DropDestination(DropKind.InsertSibling, hitNode.Parent, hitNode.SiblingNumber - 1, hitNode, isCopy);

                if (point.Y > bounds.Bottom - edgeMargin)
                    return new DropDestination(DropKind.InsertSibling, hitNode.Parent, hitNode.SiblingNumber, hitNode, isCopy);
            }

            return new DropDestination(DropKind.AsChild, hitNode, hitNode.Children.Count, hitNode, isCopy);
        }

        private bool TryFindSiblingGapDestination(TreeImage shown, PointF offset, PointF point, bool isCopy, out DropDestination destination, out RectangleF anchorBounds)
        {
            destination = null;
            anchorBounds = RectangleF.Empty;

            TreeImageNode above = null;
            RectangleF aboveRect = RectangleF.Empty;
            TreeImageNode below = null;
            RectangleF belowRect = RectangleF.Empty;

            foreach (var imageNode in shown.Nodes)
            {
                if (imageNode.IsGhost || imageNode.IsLeaving) continue;

                var rect = Offset(imageNode.Bounds, offset);
                if (point.X < rect.Left || point.X > rect.Right) continue;

                if (rect.Bottom <= point.Y)
                {
                    if (above == null || rect.Bottom > aboveRect.Bottom)
                    {
                        above = imageNode;
                        aboveRect = rect;
                    }
                }
                else if (rect.Top >= point.Y)
                {
                    if (below == null || rect.Top < belowRect.Top)
                    {
                        below = imageNode;
                        belowRect = rect;
                    }
                }
            }

            if (above != null && below != null && ReferenceEquals(above.Node.Parent, below.Node.Parent) && CanInsertSibling(shown, below.Node))
            {
                destination = new DropDestination(DropKind.InsertSibling, above.Node.Parent, below.Node.SiblingNumber - 1, above.Node, isCopy);
                anchorBounds = aboveRect;
                return true;
            }

            var aboveDist = above != null ? point.Y - aboveRect.Bottom : float.MaxValue;
            var belowDist = below != null ? belowRect.Top - point.Y : float.MaxValue;
            var aboveOk = above != null && CanInsertSibling(shown, above.Node) && aboveDist <= aboveRect.Height * 0.5f;
            var belowOk = below != null && CanInsertSibling(shown, below.Node) && belowDist <= belowRect.Height * 0.5f;

            if (aboveOk && (!belowOk || aboveDist <= belowDist))
            {
                destination = new DropDestination(DropKind.InsertSibling, above.Node.Parent, above.Node.SiblingNumber, above.Node, isCopy);
                anchorBounds = aboveRect;
                return true;
            }

            if (belowOk)
            {
                destination = new DropDestination(DropKind.InsertSibling, below.Node.Parent, below.Node.SiblingNumber - 1, below.Node, isCopy);
                anchorBounds = belowRect;
                return true;
            }

            return false;
        }

        private bool CanInsertSibling(TreeImage shown, MenuNode node) =>
            node != null
            && node.Parent != null
            && !(_drawScope == CascadeDiagramDrawScope.SelectedSubtree
                 && shown.Nodes.Count > 0
                 && ReferenceEquals(node, shown.Nodes[0].Node));

        private static DropDestination ValidateDestination(MovePress move, DropDestination candidate)
        {
            if (candidate == null || candidate.Parent == null) return null;
            if (move.IsMoved(candidate.Parent)) return null;
            if (candidate.Kind == DropKind.AsChild && move.IsMoved(candidate.Anchor)) return null;
            if (!candidate.IsCopy && CascadeState.IsNoOpMove(move.Nodes, candidate.Parent, candidate.Position)) return null;

            return candidate;
        }

        private static TreeImageNode FindStationaryNode(TreeImage image, MenuNode node) =>
            image == null || node == null
                ? null
                : image.Nodes.FirstOrDefault(imageNode => !imageNode.IsGhost && ReferenceEquals(imageNode.Node, node));

        private void PaintDropTarget(Graphics graphics)
        {
            var move = _move;
            var destination = move.Destination;
            if (destination == null || destination.Kind != DropKind.AsChild) return;

            var target = FindStationaryNode(move.Preview, destination.Anchor);
            if (target == null) return;

            var bounds = ToClientRectangle(Offset(target.Bounds, move.Offset));
            using (var border = new Pen(SystemColors.Highlight, 2f))
            {
                graphics.DrawRectangle(border, Rectangle.Inflate(bounds, 2, 2));
            }
        }

        private static RectangleF Offset(RectangleF bounds, PointF offset) => new RectangleF(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height);

        private void SetDropDestination(DropDestination destination, RectangleF anchorBounds)
        {
            var move = _move;
            if (Equals(move.Destination, destination)) return;

            move.Destination = destination;
            move.Preview?.Dispose();
            move.Preview = null;
            move.Offset = PointF.Empty;

            if (destination != null)
            {
                var preview = RenderTreeImage(new TreeDrawer.MovePreview(
                    destination.Parent,
                    destination.Position,
                    move.Nodes,
                    destination.IsCopy,
                    move.PressedNode));
                var anchorNode = FindStationaryNode(preview, destination.Anchor);
                if (anchorNode == null)
                {
                    preview.Dispose();
                    move.Destination = null;
                }
                else
                {
                    move.Preview = preview;
                    move.Offset = new PointF(anchorBounds.X - anchorNode.Bounds.X, anchorBounds.Y - anchorNode.Bounds.Y);
                }
            }

            Invalidate();
        }

        private void EndMovePress(bool apply)
        {
            var move = _move;
            _move = null;
            move.Preview?.Dispose();
            if (Capture) Capture = false;
            Cursor = Cursors.Default;
            Invalidate();

            if (!apply) return;

            if (!move.Dragged)
            {
                if (move.StartedWithControl) ToggleMark(move.PressedNode);
                return;
            }

            if (move.Destination == null || !CanEdit) return;

            var isCopy = IsControlKeyDown;
            var destination = isCopy == move.Destination.IsCopy
                ? move.Destination
                : ValidateDestination(move, move.Destination.WithCopy(isCopy));
            if (destination == null) return;

            if (destination.IsCopy)
                _state.PasteNodes(destination.Parent, destination.Position, move.Nodes, move.PressedNode);
            else
                _state.MoveNodes(move.Nodes, destination.Parent, destination.Position, move.PressedNode);
        }

        private enum DropKind
        {
            AsChild,
            InsertSibling,
        }

        private sealed class DropDestination : IEquatable<DropDestination>
        {
            public DropDestination(DropKind kind, MenuNode parent, int position, MenuNode anchor, bool isCopy)
            {
                Kind = kind;
                Parent = parent;
                Position = position;
                Anchor = anchor;
                IsCopy = isCopy;
            }

            public DropKind Kind { get; }

            public MenuNode Parent { get; }

            public int Position { get; }

            public MenuNode Anchor { get; }

            public bool IsCopy { get; }

            public DropDestination WithCopy(bool isCopy) =>
                IsCopy == isCopy ? this : new DropDestination(Kind, Parent, Position, Anchor, isCopy);

            public bool Equals(DropDestination other) =>
                other != null
                && Kind == other.Kind
                && ReferenceEquals(Parent, other.Parent)
                && Position == other.Position
                && IsCopy == other.IsCopy;

            public override bool Equals(object obj) => Equals(obj as DropDestination);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = (int)Kind;
                    hash = (hash * 397) ^ (Parent != null ? RuntimeHelpers.GetHashCode(Parent) : 0);
                    hash = (hash * 397) ^ Position;
                    hash = (hash * 397) ^ (IsCopy ? 1 : 0);
                    return hash;
                }
            }
        }

        private sealed class MovePress
        {
            private readonly HashSet<MenuNode> _roots;

            public MovePress(Point startClient, MenuNode pressedNode, IReadOnlyList<MenuNode> nodes, bool startedWithControl)
            {
                StartClient = startClient;
                LastClient = startClient;
                PressedNode = pressedNode;
                Nodes = nodes;
                StartedWithControl = startedWithControl;
                _roots = new HashSet<MenuNode>(nodes);
            }

            public Point StartClient { get; }

            public Point LastClient { get; set; }

            public MenuNode PressedNode { get; }

            public IReadOnlyList<MenuNode> Nodes { get; }

            public bool StartedWithControl { get; }

            public bool Dragged { get; set; }

            public DropDestination Destination { get; set; }

            public TreeImage Preview { get; set; }

            public PointF Offset { get; set; }

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