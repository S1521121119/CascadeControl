using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControl.Views.Diagram;
using CascadeControlTests.Views.Columns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static CascadeControlTests.Views.Columns.CascadeColumnsViewEditingTests;

namespace CascadeControlTests.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView"/> 的多選：框選、Ctrl 加選與取消（取消父節點時子孫一起取消）、Esc、複製多選。
    /// </summary>
    /// <remarks>Ctrl 無法從測試按住，按下這一步直接呼叫 <c>BeginMarkPress</c>，拖曳與放開走真正的滑鼠事件。</remarks>
    [TestClass]
    public class CascadeDiagramViewMultiSelectTests
    {
        [TestMethod]
        public void DragFromBlank_MarksNodesTouchedByBand_NoNodeClick()
        {
            using (var f = new Fixture())
            {
                f.Drag(f.BlankRightOf("A > A1", "A > A2"), f.LeftEdgeBelow("A > A2"), control: false);

                f.AssertMarks("A > A1", "A > A2");
                Assert.AreEqual(1, f.MarkEvents);
                Assert.AreEqual(0, f.Clicks.Count, "框選不觸發 NodeMouseClick");
            }
        }

        [TestMethod]
        public void PlainDragReplaces_CtrlDragAdds()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("B");
                f.Drag(f.BlankRightOf("A > A1", "A > A2"), f.LeftEdgeBelow("A > A2"), control: false);
                f.AssertMarks("A > A1", "A > A2");

                f.CtrlClick("B");
                f.AssertMarks("A > A1", "A > A2", "B");

                f.Drag(f.BlankRightOf("A > A1", "A > A2"), f.LeftEdgeBelow("A > A2"), control: true);
                f.AssertMarks("A > A1", "A > A2", "B");
            }
        }

        [TestMethod]
        public void CtrlClick_TogglesNode_UnmarkingParentAlsoUnmarksMarkedDescendants()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A");
                f.CtrlClick("A > A1");
                f.CtrlClick("A > A2");
                f.CtrlClick("B");
                f.AssertMarks("A", "A > A1", "A > A2", "B");

                f.CtrlClick("A > A2");
                f.AssertMarks("A", "A > A1", "B");

                f.CtrlClick("A");
                f.AssertMarks("B");
                Assert.AreEqual(0, f.Clicks.Count, "Ctrl+點選不觸發 NodeMouseClick");
            }
        }

        [TestMethod]
        public void PlainClickOnNodeOrBlank_ClearsMarks()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("B");
                f.Click(f.Center("C"));
                f.AssertMarks();
                Assert.AreEqual(1, f.Clicks.Count, "一般點選照常觸發 NodeMouseClick");

                f.CtrlClick("B");
                f.Click(f.BlankRightOf("A > A1", "A > A2"));
                f.AssertMarks();
            }
        }

        [TestMethod]
        public void Escape_ClearsMarks_PassesThroughWithoutMarks()
        {
            using (var f = new Fixture())
            {
                Assert.IsFalse(f.Key(Keys.Escape), "沒有多選時 Esc 交給表單");

                f.CtrlClick("B");
                Assert.IsTrue(f.Key(Keys.Escape));
                f.AssertMarks();
            }
        }

        [TestMethod]
        public void CollapsingHidesMarkedChildren_TheyLeaveTheMarks()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                f.CtrlClick("B");

                f.View.SetExpanded(f.Node("A"), false);
                f.AssertMarks("B");
            }
        }

        [TestMethod]
        public void Copy_WithMarks_CopiesTopMostMarkedSubtreesInDiagramOrder() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.CtrlClick("B");
                f.CtrlClick("A > A1");
                f.CtrlClick("A");

                Assert.IsTrue(f.Key(Keys.Control | Keys.C));
                if (!f.View.Copy()) Assert.Inconclusive("剪貼簿被其他程式佔用。");

                var copied = CascadeClipboard.ReadNodes();
                CollectionAssert.AreEqual(new[] { "A", "B" }, copied.Select(node => node.Name).ToArray(), "A1 隨 A 一起複製，不重複");
                CollectionAssert.AreEqual(new[] { "A1", "A2" }, copied[0].Children.Select(node => node.Name).ToArray());
            }
        });

        private sealed class ProbeView : CascadeDiagramView
        {
            public void MouseDownUp(Point location, bool control, Point? dragTo)
            {
                BeginMarkPress(location, control);
                if (dragTo.HasValue) OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, dragTo.Value.X, dragTo.Value.Y, 0));

                var up = dragTo ?? location;
                var args = new MouseEventArgs(MouseButtons.Left, 1, up.X, up.Y, 0);
                OnMouseClick(args);
                OnMouseUp(args);
            }
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                Tree.ExpandAndAddPaths("A", "A1|A2");
                Tree.AddPath(new[] { "B" });
                Tree.AddPath(new[] { "C" });

                View.Size = new Size(600, 400);
                View.SetTree(Tree);
                View.SetExpanded(Node("A"), true);
                View.MultiSelectionChanged += (sender, e) => MarkEvents++;
                View.NodeMouseClick += (sender, e) => Clicks.Add(e);
            }

            public MenuTree Tree { get; } = new MenuTree();

            public ProbeView View { get; } = new ProbeView();

            public int MarkEvents { get; private set; }

            public List<CascadeNodeMouseEventArgs> Clicks { get; } = new List<CascadeNodeMouseEventArgs>();

            public MenuNode Node(string fullPathText) => Walk(Tree.Root).Single(node => node.FullPathText == fullPathText);

            public void Select(string fullPathText) => Assert.IsTrue(View.TrySelectNode(Node(fullPathText)));

            public bool Key(Keys keys) => ViewHost.SendCmdKey(View, keys);

            public void CtrlClick(string fullPathText) => View.MouseDownUp(Center(fullPathText), control: true, dragTo: null);

            public void Click(Point location) => View.MouseDownUp(location, control: false, dragTo: null);

            public void Drag(Point from, Point to, bool control) => View.MouseDownUp(from, control, to);

            public void AssertMarks(params string[] expected) =>
                Assert.AreEqual(string.Join(" / ", expected), string.Join(" / ", View.MultiSelectedNodes.Select(node => node.FullPathText)));

            public Point Center(string fullPathText)
            {
                var b = Bounds(fullPathText);
                return new Point(b.Left + (b.Width / 2), b.Top + (b.Height / 2));
            }

            // 兩個上下相鄰的葉節點右邊的空白處，高度在第一個上方一點。
            public Point BlankRightOf(string upper, string lower)
            {
                var top = Bounds(upper);
                var point = new Point(Math.Max(top.Right, Bounds(lower).Right) + 6, top.Top - 3);
                Assert.IsNull(View.GetNodeAt(point), "起點要在空白處");
                return point;
            }

            public Point LeftEdgeBelow(string fullPathText)
            {
                var b = Bounds(fullPathText);
                return new Point(b.Left + 4, b.Bottom + 3);
            }

            // 逐點用 GetNodeAt 找出節點方塊的範圍。
            private Rectangle Bounds(string fullPathText)
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
