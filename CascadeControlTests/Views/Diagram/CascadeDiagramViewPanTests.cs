using CascadeControl.Trees;
using CascadeControl.Views.Diagram;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView"/> 的平移擴充點（<see cref="CascadeDiagramView.PanOffset"/>、<see cref="CascadeDiagramView.GetTransform"/>、
    /// <see cref="CascadeDiagramView.ShiftViewport"/>、<see cref="CascadeDiagramView.NotifyPanChanged"/>、<see cref="CascadeDiagramView.OnImageUpdated"/>）契約測試。
    /// </summary>
    [TestClass]
    public class CascadeDiagramViewPanTests
    {
        [TestMethod]
        public void PanOffset_ShiftsTransformAndHitTesting()
        {
            using (var f = new Fixture())
            {
                var beforeCenter = f.Center("B");
                var beforeExpander = f.FindExpanderPoint("A");
                var beforeImagePoint = f.View.ClientToImage(beforeCenter);
                var beforeOffset = f.View.CurrentOffset();

                var delta = new Point(120, -40);
                f.View.SetPan(delta);

                var afterOffset = f.View.CurrentOffset();
                Assert.AreEqual(beforeOffset.X + delta.X, afterOffset.X, 0.01f);
                Assert.AreEqual(beforeOffset.Y + delta.Y, afterOffset.Y, 0.01f);

                var shiftedCenter = new Point(beforeCenter.X + delta.X, beforeCenter.Y + delta.Y);
                var shiftedExpander = new Point(beforeExpander.X + delta.X, beforeExpander.Y + delta.Y);
                Assert.AreSame(f.Node("B"), f.View.GetNodeAt(shiftedCenter));
                Assert.IsNull(f.View.GetNodeAt(beforeCenter));
                Assert.AreSame(f.Node("A"), f.View.GetExpanderAt(shiftedExpander));

                var afterImagePoint = f.View.ClientToImage(shiftedCenter);
                Assert.AreEqual(beforeImagePoint.X, afterImagePoint.X, 0.01f);
                Assert.AreEqual(beforeImagePoint.Y, afterImagePoint.Y, 0.01f);

            }
        }

        [TestMethod]
        public void PanOffset_AlignsInlineEditBox_OnNotifyPanChanged()
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                Assert.IsTrue(f.View.BeginRename());

                var box = f.View.Controls.OfType<TextBox>().Single();
                var before = box.Location;

                f.View.SetPan(new Point(90, 30));

                Assert.AreEqual(new Point(before.X + 90, before.Y + 30), box.Location);
                Assert.AreSame(f.Node("B"), f.View.GetNodeAt(new Point(box.Left + 2, box.Top + (box.Height / 2))));
            }
        }

        [TestMethod]
        public void EnsureVisible_WithPanOffset_KeepsVisibleNodeStill_ShiftsOffscreenNodeIntoView()
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                f.View.SetPan(new Point(200, 0));
                f.View.Shifts.Clear();

                // C 已經在視窗範圍內，選取時不應把已平移的畫面拉回原位。
                f.Select("C");
                Assert.AreEqual(0, f.View.Shifts.Count);
                Assert.AreEqual(new Point(200, 0), f.View.Pan);

                // 把 A 移出左邊界，選取 A 後應透過 ShiftViewport 帶回視窗內。
                f.View.SetPan(new Point(-400, 0));
                f.View.Shifts.Clear();
                f.Select("A");
                Assert.AreEqual(1, f.View.Shifts.Count);
                Assert.IsTrue(f.View.Shifts[0].X > 0, "A 在左邊界外，應往右移回視窗內");
                var aBounds = f.BoundsOf("A");
                Assert.IsTrue(aBounds.Left >= 0 && aBounds.Right <= f.View.ClientSize.Width);

                // 把 C 移出右邊界，選取 C 後應往左移回視窗內。
                f.View.SetPan(new Point(650, 0));
                f.View.Shifts.Clear();
                f.Select("C");
                Assert.AreEqual(1, f.View.Shifts.Count);
                Assert.IsTrue(f.View.Shifts[0].X < 0, "C 在右邊界外，應往左移回視窗內");
                var cBounds = f.BoundsOf("C");
                Assert.IsTrue(cBounds.Left >= 0 && cBounds.Right <= f.View.ClientSize.Width);
            }
        }

        [TestMethod]
        public void ScrollTowards_WithPanOffset_DoesNotShiftWhenFullyVisible_RecoversOffscreenEdge()
        {
            using (var f = new Fixture())
            {
                // 小圖完全在視窗內：框選拖到視窗右外側時不應把圖面移走。
                var blank = f.BlankRightOf("A > A1", "A > A2");
                f.View.Shifts.Clear();
                f.View.BeginMarkPress(blank, control: false);
                f.View.DragTo(new Point(f.View.ClientSize.Width + 40, blank.Y));
                f.View.Up(new Point(f.View.ClientSize.Width + 40, blank.Y));
                Assert.AreEqual(0, f.View.Shifts.Count);

                // 把圖面往左移出 80px：框選往左外側拖 20px 時應捲回 +20px；再拖 200px 時只補齊剩餘 60px，停在左邊界。
                f.View.SetPan(new Point(-80, 0));
                f.View.Shifts.Clear();
                var start = new Point(250, 20);
                f.View.BeginMarkPress(start, control: false);
                f.View.DragTo(new Point(-20, 20));
                Assert.AreEqual(1, f.View.Shifts.Count);
                Assert.AreEqual(20, f.View.Shifts[0].X);

                f.View.DragTo(new Point(-200, 20));
                Assert.AreEqual(2, f.View.Shifts.Count);
                Assert.AreEqual(60, f.View.Shifts[1].X);
                Assert.AreEqual(0, f.View.Pan.X, "停在圖面左邊界，不再往空白處捲");
                f.View.Up(new Point(-200, 20));
            }
        }

        [TestMethod]
        public void OnImageUpdated_FiresOnSetTreeRedrawAndZoomFactor()
        {
            using (var f = new Fixture())
            {
                var before = f.View.ImageUpdatedCount;
                f.View.Redraw();
                Assert.AreEqual(before + 1, f.View.ImageUpdatedCount);

                f.View.ZoomFactor = 1.5f;
                Assert.AreEqual(before + 2, f.View.ImageUpdatedCount);

                var nextTree = new MenuTree();
                nextTree.AddPath("Root");
                f.View.SetTree(nextTree);
                Assert.IsTrue(f.View.ImageUpdatedCount >= before + 3);
            }
        }

        private sealed class PanProbeView : CascadeDiagramView
        {
            public Point Pan { get; private set; }

            public List<Point> Shifts { get; } = new List<Point>();

            public int ImageUpdatedCount { get; private set; }

            protected override Point PanOffset => Pan;

            public void SetPan(Point pan)
            {
                Pan = pan;
                NotifyPanChanged();
            }

            public PointF CurrentOffset()
            {
                GetTransform(out _, out var offset);
                return offset;
            }

            public PointF ClientToImage(Point location) => ToImagePoint(location);

            public void DragTo(Point location) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));

            public void Up(Point location)
            {
                var args = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
                OnMouseClick(args);
                OnMouseUp(args);
            }

            protected override void ShiftViewport(int dx, int dy)
            {
                Shifts.Add(new Point(dx, dy));
                Pan = new Point(Pan.X + dx, Pan.Y + dy);
                NotifyPanChanged();
            }

            protected override void OnImageUpdated()
            {
                ImageUpdatedCount++;
            }
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                Tree.ExpandAndAddPaths("A", "A1|A2");
                Tree.AddPath(new[] { "B" });
                Tree.AddPath(new[] { "C" });

                View.ReadOnly = false;
                View.Size = new Size(600, 400);
                View.SetTree(Tree);
                View.SetExpanded(Node("A"), true);
            }

            public MenuTree Tree { get; } = new MenuTree();

            public PanProbeView View { get; } = new PanProbeView();

            public MenuNode Node(string fullPathText) => Walk(Tree.Root).Single(node => node.FullPathText == fullPathText);

            public void Select(string fullPathText) => Assert.IsTrue(View.TrySelectNode(Node(fullPathText)));

            public Point Center(string fullPathText)
            {
                var b = BoundsOf(fullPathText);
                return new Point(b.Left + (b.Width / 2), b.Top + (b.Height / 2));
            }

            public Point BlankRightOf(string upper, string lower)
            {
                var top = BoundsOf(upper);
                var point = new Point(Math.Max(top.Right, BoundsOf(lower).Right) + 6, top.Top - 3);
                Assert.IsNull(View.GetNodeAt(point), "起點要在空白處");
                return point;
            }

            public Point FindExpanderPoint(string fullPathText)
            {
                var node = Node(fullPathText);
                for (var y = 0; y < 400; y++)
                {
                    for (var x = 0; x < 600; x++)
                    {
                        var point = new Point(x, y);
                        if (ReferenceEquals(View.GetExpanderAt(point), node)) return point;
                    }
                }

                Assert.Fail($"{fullPathText} 沒有畫出展開鈕");
                return Point.Empty;
            }

            public Rectangle BoundsOf(string fullPathText)
            {
                var node = Node(fullPathText);
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                for (var y = 0; y < 400; y++)
                {
                    for (var x = 0; x < 600; x++)
                    {
                        if (!ReferenceEquals(View.GetNodeAt(new Point(x, y)), node)) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }

                Assert.IsTrue(left <= right, $"{fullPathText} 沒有畫在圖上");
                return Rectangle.FromLTRB(left, top, right, bottom);
            }

            public void Dispose() => View.Dispose();

            private static IEnumerable<MenuNode> Walk(MenuNode node) =>
                node.Children.SelectMany(child => new[] { child }.Concat(Walk(child)));
        }
    }
}
