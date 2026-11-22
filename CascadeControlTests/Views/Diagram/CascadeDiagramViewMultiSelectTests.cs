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
        public void CtrlClick_WithoutMarks_StartsFromSelectedNode()
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                f.CtrlClick("C");
                f.AssertMarks("B", "C");
                Assert.AreSame(f.Node("B"), f.View.SelectedNode, "Ctrl+點選不改選取");

                f.View.ClearMultiSelection();
                f.CtrlClick("B");
                f.AssertMarks();

                f.Drag(f.BlankRightOf("A > A1", "A > A2"), f.LeftEdgeBelow("A > A2"), control: true);
                f.AssertMarks("A > A1", "A > A2", "B");
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
                f.CtrlClick("C");

                Assert.IsTrue(f.Key(Keys.Control | Keys.C));
                if (!f.View.Copy()) Assert.Inconclusive("剪貼簿被其他程式佔用。");

                var copied = CascadeClipboard.ReadNodes();
                CollectionAssert.AreEqual(new[] { "A", "B" }, copied.Select(node => node.Name).ToArray(), "A1 隨 A 一起複製，不重複");
                CollectionAssert.AreEqual(new[] { "A1", "A2" }, copied[0].Children.Select(node => node.Name).ToArray());
            }
        });

        [TestMethod]
        public void Paste_WithMarks_GoesAfterLastMark_PastedNodesBecomeMarks() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.CtrlClick("A > A1");
                f.CtrlClick("B");
                f.CtrlClick("C");
                f.RequireClipboard(f.View.Copy());

                Assert.IsTrue(f.Key(Keys.Control | Keys.V));
                f.AssertShape("A", "A > A1", "A > A2", "B", "A1", "B - 2", "C");
                f.AssertMarks("A1", "B - 2");
                Assert.AreSame(f.Node("A1"), f.View.SelectedNode);
            }
        });

        [TestMethod]
        public void PasteBefore_WithMarks_GoesBeforeFirstMark_SinglePasteClearsMarks() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                f.RequireClipboard(f.View.Copy());

                f.Select("C");
                f.CtrlClick("A > A2");
                f.CtrlClick("C");
                Assert.IsTrue(f.View.Paste(before: true));
                f.AssertShape("A", "A > A1", "A > B", "A > A2", "B", "C");
                f.AssertMarks();
            }
        });

        [TestMethod]
        public void PasteAsChild_WithMarks_OnlyIntoSingleMarkedLeaf() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                f.RequireClipboard(f.View.Copy());

                f.Select("C");
                f.CtrlClick("A > A1");
                f.CtrlClick("B");
                f.CtrlClick("C");
                Assert.IsFalse(f.View.PasteAsChild(), "多選兩個節點時不知道要貼進哪一個");

                f.CtrlClick("B");
                Assert.IsTrue(f.View.PasteAsChild());
                f.AssertShape("A", "A > A1", "A > A1 > B", "A > A2", "B", "C");
            }
        });

        [TestMethod]
        public void DragMarkedNodesOntoNode_MovesThemUnderIt_StaysMarked()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                f.CtrlClick("B");
                var a1 = f.Node("A > A1");
                var c = f.Center("C");

                f.View.Down(f.Center("A > A1"));
                f.View.DragTo(c);
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");

                f.View.Up(c);
                f.AssertShape("A", "A > A2", "C", "C > A1", "C > B");
                Assert.AreSame(a1, f.View.SelectedNode);
                f.AssertMarks("C > A1", "C > B");
                Assert.AreEqual(0, f.Clicks.Count, "拖曳不觸發 NodeMouseClick");
            }
        }

        [TestMethod]
        public void DragUnmarkedNode_MovesOnlyIt()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                var b = f.Node("B");
                var c = f.Center("C");

                f.View.Down(f.Center("B"));
                f.View.DragTo(c);
                f.View.Up(c);

                f.AssertShape("A", "A > A1", "A > A2", "C", "C > B");
                Assert.AreSame(b, f.View.SelectedNode);
                f.AssertMarks();
                Assert.AreEqual(0, f.Clicks.Count, "拖曳不觸發 NodeMouseClick");
            }
        }

        [TestMethod]
        public void PressOnUnmarkedNodeWithoutDragging_IsPlainClick()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                var b = f.Center("B");

                f.View.Down(b);
                f.View.Up(b);

                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                f.AssertMarks();
                Assert.AreEqual(1, f.Clicks.Count);
            }
        }

        [TestMethod]
        public void EscapeWhileDragging_CancelsMove_ReleaseIsNotAClick()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                f.CtrlClick("B");
                var c = f.Center("C");

                f.View.Down(f.Center("A > A1"));
                f.View.DragTo(c);
                Assert.IsTrue(f.Key(Keys.Escape));
                f.View.Up(c);

                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                f.AssertMarks("A > A1", "B");
                Assert.AreEqual(0, f.Clicks.Count);
            }
        }

        [TestMethod]
        public void DropOnMovedNodeOrBlank_ChangesNothing()
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A");
                f.CtrlClick("B");
                var a1 = f.Center("A > A1");
                var blank = f.BlankRightOf("A > A1", "A > A2");

                f.View.Down(f.Center("B"));
                f.View.DragTo(a1);
                f.View.Up(a1);

                f.View.Down(f.Center("B"));
                f.View.DragTo(blank);
                f.View.Up(blank);

                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
            }
        }

        [TestMethod]
        public void ReadOnly_PressOnMarkedNodeIsPlainClick()
        {
            using (var f = new Fixture())
            {
                f.View.ReadOnly = true;
                f.CtrlClick("B");
                var c = f.Center("C");

                f.View.Down(f.Center("B"));
                f.View.DragTo(c);
                f.View.Up(c);

                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                f.AssertMarks();
            }
        }

        [TestMethod]
        public void Cut_WithMarksFromDifferentParents_RemovesAll() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.CtrlClick("A > A1");
                f.CtrlClick("B");

                if (!f.Key(Keys.Control | Keys.X) || f.Tree.Root.Children.Count == 3) Assert.Inconclusive("剪貼簿被其他程式佔用。");

                f.AssertShape("A", "A > A2", "C");
                CollectionAssert.AreEqual(new[] { "A1", "B" }, CascadeClipboard.ReadNodes().Select(node => node.Name).ToArray());
            }
        });

        private sealed class ProbeView : CascadeDiagramView
        {
            public void Down(Point location) => BeginMarkPress(location, control: false);

            public void DragTo(Point location) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));

            public void Up(Point location)
            {
                var args = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
                OnMouseClick(args);
                OnMouseUp(args);
            }

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

        [TestMethod]
        public void SelectAll_MarksEveryDrawnNode_SkipsCollapsedChildren()
        {
            using (var f = new Fixture())
            {
                f.View.SelectAll();
                f.AssertMarks("A", "A > A1", "A > A2", "B", "C");
                Assert.AreEqual(1, f.MarkEvents);

                f.View.ClearMultiSelection();
                f.View.SetExpanded(f.Node("A"), false);
                f.View.SelectAll();
                f.AssertMarks("A", "B", "C");
            }
        }

        [TestMethod]
        public void ZoomFactor_ScalesDrawing_HitTestingFollows()
        {
            using (var f = new Fixture())
            {
                var normal = f.BoundsOf("B");
                var normalWidth = f.View.Image.Width;

                f.View.ZoomFactor = 2f;

                var zoomed = f.BoundsOf("B");
                Assert.AreEqual(normal.Width * 2, zoomed.Width, 2);
                Assert.AreEqual(normal.Height * 2, zoomed.Height, 2);
                Assert.AreEqual(normalWidth * 2, f.View.Image.Width, 2);
                Assert.AreSame(f.Node("B"), f.View.GetNodeAt(new Point(normal.Left * 2 + 1, normal.Top * 2 + 1)));
            }
        }

        [TestMethod]
        public void ZoomFactor_RejectsNonPositive()
        {
            using (var view = new CascadeDiagramView())
            {
                foreach (var value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => view.ZoomFactor = value, value.ToString());

                Assert.AreEqual(1f, view.ZoomFactor);
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
                View.MultiSelectionChanged += (sender, e) => MarkEvents++;
                View.NodeMouseClick += (sender, e) => Clicks.Add(e);
            }

            public MenuTree Tree { get; } = new MenuTree();

            public ProbeView View { get; } = new ProbeView();

            public int MarkEvents { get; private set; }

            public List<CascadeNodeMouseEventArgs> Clicks { get; } = new List<CascadeNodeMouseEventArgs>();

            public MenuNode Node(string fullPathText) => Walk(Tree.Root).Single(node => node.FullPathText == fullPathText);

            public void AssertShape(params string[] expected) =>
                Assert.AreEqual(string.Join(" / ", expected), string.Join(" / ", Walk(Tree.Root).Select(node => node.FullPathText)));

            public void RequireClipboard(bool succeeded)
            {
                if (!succeeded) Assert.Inconclusive("剪貼簿被其他程式佔用。");
            }

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
            public Rectangle BoundsOf(string fullPathText) => Bounds(fullPathText);

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
