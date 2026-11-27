using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControl.Views.Columns;
using CascadeControl.Views.Diagram;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Diagram
{
    [TestClass]
    public class TreeDrawerTests
    {
        private static MenuNode Find(MenuTree tree, params string[] names)
        {
            var node = tree.Root;
            foreach (var name in names) node = node.Children.Single(child => child.Name == name);
            return node;
        }

        private static System.Collections.Generic.IEnumerable<MenuNode> Descendants(MenuNode node)
        {
            return node.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
        }

        private static PointF Center(RectangleF bounds) => new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

        #region TreeDrawer

        [TestMethod]
        public void MovePreview_KeepsMovedNodesInPlace_AddsCopyUnderTarget()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("A", "A1|A2");
            tree.AddPath(new[] { "B" });
            tree.AddPath(new[] { "C" });
            var a1 = Find(tree, "A", "A1");
            var c = Find(tree, "C");

            using (var plain = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont))
            using (var preview = TreeDrawer.RenderImageWithPlaceholder(tree, SystemFonts.DefaultFont, null, CascadeDiagramDrawScope.WholeTree,
                CascadeDiagramUncheckedDisplay.Dimmed, null, null, null, new TreeDrawer.MovePreview(c, new[] { a1 })))
            {
                Assert.AreEqual(2, preview.Nodes.Count(node => ReferenceEquals(node.Node, a1)), "原處一份、目標底下一份");

                // 目標之前的節點（包括會被移走的 A1）位置不變，版面不會因為少了節點而跳動。
                foreach (var path in new[] { new[] { "A" }, new[] { "A", "A1" }, new[] { "A", "A2" }, new[] { "B" } })
                {
                    var node = Find(tree, path);
                    var name = node.FullPathText;
                    Assert.AreEqual(plain.Nodes.First(n => ReferenceEquals(n.Node, node)).Bounds, preview.Nodes.First(n => ReferenceEquals(n.Node, node)).Bounds, name);
                }
            }
        }

        private static bool HasRedPixel(Bitmap bitmap, RectangleF area)
        {
            var left = System.Math.Max(0, (int)System.Math.Floor(area.Left));
            var top = System.Math.Max(0, (int)System.Math.Floor(area.Top));
            var right = System.Math.Min(bitmap.Width - 1, (int)System.Math.Ceiling(area.Right));
            var bottom = System.Math.Min(bitmap.Height - 1, (int)System.Math.Ceiling(area.Bottom));

            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > 210 && pixel.G < 80 && pixel.B < 80) return true;
                }
            }

            return false;
        }

        [TestMethod]
        public void MovePreview_MoveDrawsRedCrossOnLeaving_CopyLeavesOriginalUntouched()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("A", "A1|A2");
            tree.AddPath(new[] { "B" });
            tree.AddPath(new[] { "C" });
            var a1 = Find(tree, "A", "A1");
            var c = Find(tree, "C");

            using (var move = TreeDrawer.RenderImageWithPlaceholder(tree, SystemFonts.DefaultFont, null, CascadeDiagramDrawScope.WholeTree,
                CascadeDiagramUncheckedDisplay.Dimmed, a1, null, null, new TreeDrawer.MovePreview(c, 0, new[] { a1 }, isCopy: false, focusedNode: a1)))
            {
                var leaving = move.Nodes.Single(n => ReferenceEquals(n.Node, a1) && n.IsLeaving);
                var ghost = move.Nodes.Single(n => ReferenceEquals(n.Node, a1) && n.IsGhost);

                Assert.IsTrue(HasRedPixel(move.Image, leaving.Bounds), "移動預覽時離開的原節點要畫紅叉");

                var leavingOuterTop = new RectangleF(leaving.Bounds.Left, leaving.Bounds.Top - 5f, leaving.Bounds.Width, 4f);
                Assert.IsFalse(HasRedPixel(move.Image, leavingOuterTop), "離開的原節點外圍不畫紅色選取框");

                var topBorderBand = new RectangleF(ghost.Bounds.Left, ghost.Bounds.Top - 5f, ghost.Bounds.Width, 4f);
                Assert.IsTrue(HasRedPixel(move.Image, topBorderBand), "焦點節點的預覽複本外圍要有紅色選取框");
            }

            using (var copy = TreeDrawer.RenderImageWithPlaceholder(tree, SystemFonts.DefaultFont, null, CascadeDiagramDrawScope.WholeTree,
                CascadeDiagramUncheckedDisplay.Dimmed, a1, null, null, new TreeDrawer.MovePreview(c, 0, new[] { a1 }, isCopy: true, focusedNode: a1)))
            {
                Assert.IsFalse(copy.Nodes.Any(n => n.IsLeaving), "複製模式不標示任何節點為即將離開");
                var original = copy.Nodes.Single(n => ReferenceEquals(n.Node, a1) && !n.IsGhost);
                var ghost = copy.Nodes.Single(n => ReferenceEquals(n.Node, a1) && n.IsGhost);

                Assert.IsFalse(HasRedPixel(copy.Image, original.Bounds), "複製模式原節點不畫紅叉");
                var ghostBorderBand = new RectangleF(ghost.Bounds.Left, ghost.Bounds.Top - 5f, ghost.Bounds.Width, 4f);
                Assert.IsTrue(HasRedPixel(copy.Image, ghostBorderBand), "複製模式焦點複本外圍有紅色選取框");
            }
        }

        [TestMethod]
        public void MovePreview_SiblingInsertIndex_PlacesGhostBetweenSiblings()
        {
            var tree = new MenuTree();
            tree.AddPath(new[] { "A" });
            tree.AddPath(new[] { "B" });
            tree.AddPath(new[] { "C" });
            var a = Find(tree, "A");
            var b = Find(tree, "B");
            var c = Find(tree, "C");

            using (var preview = TreeDrawer.RenderImageWithPlaceholder(tree, SystemFonts.DefaultFont, null, CascadeDiagramDrawScope.WholeTree,
                CascadeDiagramUncheckedDisplay.Dimmed, c, null, null, new TreeDrawer.MovePreview(tree.Root, 1, new[] { c }, isCopy: false, focusedNode: c)))
            {
                var aNode = preview.Nodes.Single(n => ReferenceEquals(n.Node, a));
                var bNode = preview.Nodes.Single(n => ReferenceEquals(n.Node, b));
                var ghostC = preview.Nodes.Single(n => ReferenceEquals(n.Node, c) && n.IsGhost);
                var leavingC = preview.Nodes.Single(n => ReferenceEquals(n.Node, c) && n.IsLeaving);

                Assert.IsTrue(aNode.Bounds.Bottom < ghostC.Bounds.Top && ghostC.Bounds.Bottom < bNode.Bounds.Top, "ghost 插在 A 與 B 之間");
                Assert.IsTrue(bNode.Bounds.Bottom < leavingC.Bounds.Top, "leaving 原位保留在 B 下方");
            }
        }

        [TestMethod]
        public void RenderImage_WithoutIsExpanded_DrawsEveryNodeExpanded()
        {
            var tree = TestTrees.Template();
            using (var image = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont))
            {
                Assert.AreEqual(Descendants(tree.Root).Count(), image.Nodes.Count);
                Assert.IsTrue(image.Nodes.All(node => node.IsExpanded));
            }
        }

        [TestMethod]
        public void RenderImage_CollapsedNode_HidesDescendantsAndKeepsExpander()
        {
            var tree = TestTrees.Template();
            var motor = Find(tree, "Motor");

            using (var image = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont, isExpanded: node => node != motor))
            {
                var drawn = image.Nodes.Select(node => node.Node).ToList();
                Assert.IsTrue(drawn.Contains(motor));
                Assert.IsFalse(Descendants(motor).Any(drawn.Contains), "收合節點的子孫不畫");
                Assert.IsTrue(drawn.Contains(Find(tree, "System", "Info", "Version")), "其他節點照畫");

                var motorImage = image.Nodes.Single(node => node.Node == motor);
                Assert.IsFalse(motorImage.IsExpanded);
                Assert.IsFalse(motorImage.ExpanderBounds.IsEmpty);
                Assert.IsTrue(motorImage.Bounds.Contains(motorImage.ExpanderBounds), "展開鈕在方塊裡");
            }
        }

        [TestMethod]
        public void GetExpanderAt_FindsExpanderOwnerAndIgnoresLeaves()
        {
            var tree = TestTrees.Template();

            using (var image = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont))
            {
                var system = image.Nodes.Single(node => node.Node == Find(tree, "System"));
                Assert.AreSame(system.Node, image.GetExpanderAt(Center(system.ExpanderBounds)));
                Assert.IsNull(image.GetExpanderAt(new PointF(system.Bounds.Left + 1f, system.Bounds.Top + 1f)), "方塊的其他位置不算");

                var leaf = image.Nodes.Single(node => node.Node == Find(tree, "System", "Reboot"));
                Assert.IsTrue(leaf.ExpanderBounds.IsEmpty);
            }
        }

        [TestMethod]
        public void RenderImage_ShowIndex_WidensNodeBoundsOnlyWhenEnabled()
        {
            var tree = TestTrees.Template();
            var system = Find(tree, "System");

            using (var defaultImage = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont))
            using (var hiddenIndex = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont, showIndex: false))
            using (var shownIndex = TreeDrawer.RenderImage(tree, SystemFonts.DefaultFont, showIndex: true))
            {
                var defaultBounds = defaultImage.Nodes.Single(node => ReferenceEquals(node.Node, system)).Bounds;
                var hiddenBounds = hiddenIndex.Nodes.Single(node => ReferenceEquals(node.Node, system)).Bounds;
                var shownBounds = shownIndex.Nodes.Single(node => ReferenceEquals(node.Node, system)).Bounds;

                Assert.AreEqual(hiddenBounds, defaultBounds, "預設不顯示編號");
                Assert.IsTrue(shownBounds.Width > defaultBounds.Width, "showIndex: true 時節點方塊要容納編號而變寬");
                Assert.IsTrue(shownIndex.Image.Width > defaultImage.Image.Width);
            }
        }

        #endregion TreeDrawer

        #region CascadeDiagramView

        [TestMethod]
        public void IsExpanded_WithoutCheckInfo_DefaultsToCollapsed()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new CascadeDiagramView { State = view.State })
            {
                view.SetTree(tree);
                Assert.IsFalse(imageView.IsExpanded(Find(tree, "Motor")));

                using (var image = TreeDrawer.RenderImage(tree, imageView.Font, isExpanded: imageView.IsExpanded))
                {
                    CollectionAssert.AreEquivalent(tree.TopLevelNodes.ToList(), image.Nodes.Select(node => node.Node).ToList(), "只畫第一層");
                }
            }
        }

        [TestMethod]
        public void IsExpanded_FollowsCheckedChildren()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView { CheckBoxes = true })
            using (var imageView = new CascadeDiagramView { State = view.State, ShowChecks = true })
            {
                view.SetTree(tree);
                var motor = Find(tree, "Motor");
                var axisX = Find(tree, "Motor", "Axis X");

                view.CheckSet.Check(axisX);
                Assert.IsTrue(imageView.IsExpanded(motor), "勾了子節點，父節點展開");
                Assert.IsFalse(imageView.IsExpanded(axisX), "只勾自己，不展開");

                view.CheckSet.Check(Find(tree, "Motor", "Axis X", "Home"));
                Assert.IsTrue(imageView.IsExpanded(axisX), "之後勾了子節點，沒點過的節點跟著展開");
            }
        }

        [TestMethod]
        public void SetExpanded_OverridesDefaultUntilTreeChanges()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView { CheckBoxes = true })
            using (var imageView = new CascadeDiagramView { State = view.State, ShowChecks = true })
            {
                view.SetTree(tree);
                var motor = Find(tree, "Motor");

                imageView.SetExpanded(motor, true);
                Assert.IsTrue(imageView.IsExpanded(motor));

                view.CheckSet.Check(Find(tree, "Motor", "Axis X"));
                imageView.SetExpanded(motor, false);
                Assert.IsFalse(imageView.IsExpanded(motor), "點過的節點不再依勾選決定");

                var system = Find(tree, "System");
                imageView.SetExpanded(system, true);

                view.SetTree(TestTrees.Template());
                Assert.IsFalse(imageView.IsExpanded(system), "換樹後點過的設定被清掉");
            }
        }

        [TestMethod]
        public void SelectionFromColumnsView_ExpandsAncestorsSoSelectedNodeIsDrawn()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new CascadeDiagramView { State = view.State })
            {
                view.SetTree(tree);
                var home = Find(tree, "Motor", "Axis Y", "Home");
                imageView.SetExpanded(Find(tree, "Motor"), false);

                Assert.IsTrue(view.TrySelectPath(home.FullPath));

                Assert.IsFalse(LayoutNodeOf(imageView, tree, home).Bounds.IsEmpty, "選取的節點要畫出來，使用者收合過的祖先也要展開");
                Assert.IsFalse(imageView.IsExpanded(Find(tree, "Motor", "Axis X")), "不在選取路徑上的節點維持收合");

                view.TrySelectPath(Find(tree, "System", "Reboot").FullPath);
                Assert.IsTrue(imageView.IsExpanded(Find(tree, "Motor", "Axis Y")), "選取離開後維持展開，和點過展開鈕一樣");
            }
        }

        [TestMethod]
        public void SelectionFromColumnsView_ScrollsSelectedNodeIntoView()
        {
            var tree = TestTrees.Template();
            using (var form = new Form { ShowInTaskbar = false })
            using (var view = new CascadeColumnsView())
            using (var imageView = new CascadeDiagramView { State = view.State, Size = new Size(120, 120) })
            {
                form.Controls.Add(imageView);
                form.Show();
                view.SetTree(tree);
                Assert.AreEqual(Point.Empty, imageView.AutoScrollPosition);

                view.TrySelectPath(Find(tree, "Diagnostics", "Sensor", "Temperature", "Zone B", "Read", "Calibrated").FullPath);

                Assert.AreNotEqual(Point.Empty, imageView.AutoScrollPosition, "從 CascadeColumnsView 切換選取，圖也要捲到選取的節點");
            }
        }

        private sealed class ProbeImageView : CascadeDiagramView
        {
            public void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));

            public KeyEventArgs PressKey(Keys key)
            {
                var args = new KeyEventArgs(key);
                OnKeyDown(args);
                return args;
            }

            public void PressChar(char keyChar) => OnKeyPress(new KeyPressEventArgs(keyChar));

            public void ClickAt(Point location, MouseButtons button = MouseButtons.Left) =>
                OnMouseClick(new MouseEventArgs(button, 1, location.X, location.Y, 0));

            public void DoubleClickAt(Point location, MouseButtons button = MouseButtons.Left) =>
                OnMouseDoubleClick(new MouseEventArgs(button, 2, location.X, location.Y, 0));

            public PointF ImagePointOf(Point clientLocation) => ToImagePoint(clientLocation);
        }

        private static Point RoundPoint(PointF point) => new Point((int)System.Math.Round(point.X), (int)System.Math.Round(point.Y));

        private static TreeImageNode LayoutNodeOf(CascadeDiagramView imageView, MenuTree tree, MenuNode target)
        {
            var checkSet = (imageView.State as CascadeCheckableState)?.CheckSet;
            var isChecked = checkSet == null ? (System.Func<MenuNode, bool>)null : checkSet.IsChecked;
            using (var layout = TreeDrawer.RenderImage(tree, imageView.Font, isChecked, imageView.DrawScope, imageView.UncheckedDisplay, imageView.SelectedNode as MenuNode, imageView.IsExpanded, imageView.ShowIndex))
            {
                return layout.Nodes.Single(node => ReferenceEquals(node.Node, target));
            }
        }

        [TestMethod]
        public void SharedStateFromColumnsView_ShowChecksOff_DrawsLevelColorsNotDimmed()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new CascadeDiagramView { State = view.State })
            {
                view.SetTree(tree);

                var bounds = LayoutNodeOf(imageView, tree, Find(tree, "Motor")).Bounds;
                var back = ((Bitmap)imageView.Image).GetPixel((int)(bounds.Left + 4), (int)bounds.Top + (int)(bounds.Height / 2));

                Assert.AreNotEqual(Color.White.ToArgb(), back.ToArgb(), "沒開 ShowChecks 時節點要有層的底色，不是沒勾的白底");
            }
        }

        [TestMethod]
        public void Standalone_ShowChecks_DrawsChecksSetThroughDefaultState()
        {
            var tree = TestTrees.Template();
            using (var imageView = new CascadeDiagramView { ShowChecks = true })
            {
                imageView.SetTree(tree);
                var motor = Find(tree, "Motor");
                Color BackOfMotor()
                {
                    var bounds = LayoutNodeOf(imageView, tree, motor).Bounds;
                    return ((Bitmap)imageView.Image).GetPixel((int)(bounds.Left + 4), (int)bounds.Top + (int)(bounds.Height / 2));
                }

                Assert.AreEqual(Color.White.ToArgb(), BackOfMotor().ToArgb(), "沒勾的節點是白底");
                Assert.IsFalse(imageView.IsExpanded(motor));

                var checkSet = ((CascadeCheckableState)imageView.State).CheckSet;
                checkSet.Check(Find(tree, "Motor", "Axis X"));

                Assert.AreNotEqual(Color.White.ToArgb(), BackOfMotor().ToArgb(), "勾選後畫成層的底色");
                Assert.IsTrue(imageView.IsExpanded(motor), "勾了子節點，父節點展開");
            }
        }

        [TestMethod]
        public void State_SwapNullGuardAndDispose_DetachesOldSubscriptionsAndRedraws()
        {
            var firstTree = TestTrees.Template();
            var secondTree = TestTrees.Template();
            var firstState = new CascadeCheckableState();
            var secondState = new CascadeState();
            firstState.SetTree(firstTree);
            secondState.SetTree(secondTree);

            using (var imageView = new CascadeDiagramView { State = firstState, ShowChecks = true })
            {
                Assert.ThrowsException<System.ArgumentNullException>(() => imageView.State = null);

                var beforeCheck = imageView.Image;
                Assert.IsTrue(beforeCheck.Width > 1 && beforeCheck.Height > 1);

                firstState.CheckSet.Check(Find(firstTree, "Motor", "Axis X"));
                var afterCheck = imageView.Image;
                Assert.AreNotSame(beforeCheck, afterCheck, "勾選改變要自動重畫");

                firstState.CheckSet = new MenuCheckSet(firstTree);
                var afterCheckSetSwap = imageView.Image;
                Assert.AreNotSame(afterCheck, afterCheckSetSwap, "換上另一份 CheckSet 要自動重畫");

                imageView.State = secondState;
                var afterSwap = imageView.Image;
                Assert.AreNotSame(afterCheckSetSwap, afterSwap);

                firstState.TrySelectNode(Find(firstTree, "System"));
                firstState.CheckSet.Check(Find(firstTree, "System"));
                Assert.AreSame(afterSwap, imageView.Image, "換掉 State 後，舊 State 的選取與勾選不再觸發重畫");

                secondState.TrySelectNode(Find(secondTree, "Motor"));
                Assert.AreNotSame(afterSwap, imageView.Image, "新 State 的選取改變會重畫");
            }

            // imageView 已 Dispose，繼續操作 secondState 不應出錯。
            Assert.IsTrue(secondState.TrySelectNode(Find(secondTree, "System")));
        }

        [TestMethod]
        public void Standalone_SetTreeNavigateAndClear_WorksWithoutCascadeView()
        {
            var tree = TestTrees.Template();
            using (var imageView = new ProbeImageView())
            {
                var pathEvents = 0;
                imageView.SelectedPathChanged += (sender, e) => pathEvents++;

                imageView.SetTree(tree);
                Assert.AreSame(tree, imageView.Tree);
                Assert.IsTrue(imageView.Image.Width > 1 && imageView.Image.Height > 1);
                Assert.AreEqual(1, pathEvents);

                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "System"), imageView.SelectedNode);
                Assert.IsFalse(imageView.IsLeafSelected);

                imageView.Press(Keys.Space);
                imageView.Press(Keys.Right);
                var reboot = Find(tree, "System", "Reboot");
                Assert.AreSame(reboot, imageView.SelectedNode);
                Assert.IsTrue(imageView.IsLeafSelected);
                Assert.AreEqual("System => Reboot", imageView.SelectedPathText);
                CollectionAssert.AreEqual(reboot.FullPath.Cast<ICascadeNode>().ToList(), imageView.SelectedPath.ToList());

                Assert.IsTrue(imageView.StepBack());
                Assert.AreSame(Find(tree, "System"), imageView.SelectedNode);
                Assert.IsTrue(imageView.StepForward());
                Assert.AreSame(reboot, imageView.SelectedNode);

                imageView.Clear();
                Assert.IsNull(imageView.Tree);
                Assert.IsNull(imageView.SelectedNode);
                Assert.AreEqual(new Size(1, 1), imageView.Image.Size);
            }
        }

        [TestMethod]
        public void SharedCheckableState_SharesPathSeparatorAndAutoRedrawsOnCheckAndEdit()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView { CheckBoxes = true, ReadOnly = false })
            using (var imageView = new CascadeDiagramView { State = view.State })
            {
                view.PathSeparator = "\\";
                Assert.AreEqual("\\", imageView.PathSeparator, "共享 State 時 PathSeparator 兩邊一致");

                view.SetTree(tree);
                var reboot = Find(tree, "System", "Reboot");
                view.TrySelectNode(reboot);
                Assert.AreEqual("System\\Reboot", imageView.SelectedPathText);

                imageView.SetExpanded(Find(tree, "System"), true);
                var beforeEdit = imageView.Image;
                reboot.Rename("Restart");
                view.RefreshColumns();
                Assert.AreNotSame(beforeEdit, imageView.Image, "RefreshColumns 透過 State 自動重畫樹圖");
            }
        }

        [TestMethod]
        public void MouseClickAndDoubleClick_OnNodeAndBlank_RaiseEventsWithPathSeparator()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                Assert.AreEqual(" => ", view.PathSeparator);
                Assert.AreEqual(" => ", imageView.PathSeparator);

                imageView.PathSeparator = " / ";
                view.SetTree(tree);
                var motor = Find(tree, "Motor");
                var axisX = Find(tree, "Motor", "Axis X");
                imageView.SetExpanded(motor, true);

                var layout = LayoutNodeOf(imageView, tree, axisX);
                var clickPoint = RoundPoint(new PointF(layout.Bounds.Left + 4f, layout.Bounds.Top + (layout.Bounds.Height / 2f)));
                Assert.AreSame(axisX, imageView.GetNodeAt(clickPoint));
                Assert.IsNull(imageView.GetExpanderAt(clickPoint));

                var clicks = new System.Collections.Generic.List<CascadeNodeMouseEventArgs>();
                var doubleClicks = new System.Collections.Generic.List<CascadeNodeMouseEventArgs>();
                imageView.NodeMouseClick += (sender, e) => clicks.Add(e);
                imageView.NodeMouseDoubleClick += (sender, e) => doubleClicks.Add(e);

                imageView.ClickAt(clickPoint, MouseButtons.Left);
                imageView.ClickAt(clickPoint, MouseButtons.Right);
                imageView.DoubleClickAt(clickPoint, MouseButtons.Left);
                imageView.ClickAt(new Point(0, 0), MouseButtons.Left);

                Assert.AreEqual(2, clicks.Count, "空白處點擊不觸發 NodeMouseClick");
                Assert.AreSame(axisX, clicks[0].Node);
                Assert.AreEqual(1, clicks[0].ColumnIndex);
                Assert.AreEqual("Motor / Axis X", clicks[0].PathText);
                CollectionAssert.AreEqual(axisX.FullPath.Cast<ICascadeNode>().ToList(), clicks[0].Path.ToList());
                Assert.AreEqual(MouseButtons.Left, clicks[0].Button);
                Assert.AreEqual(clickPoint, clicks[0].Location);
                Assert.IsTrue(clicks[0].Bounds.Contains(clickPoint), "Bounds 為被點節點的用戶區矩形");
                Assert.AreEqual(MouseButtons.Right, clicks[1].Button);

                Assert.AreEqual(1, doubleClicks.Count);
                Assert.AreSame(axisX, doubleClicks[0].Node);
                Assert.AreEqual("Motor / Axis X", doubleClicks[0].PathText);
                Assert.AreEqual(MouseButtons.Left, doubleClicks[0].Button);
            }
        }

        [TestMethod]
        public void MouseClickAndDoubleClick_OnExpander_ToggleExpansionWithoutRaisingNodeEvents()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                var motor = Find(tree, "Motor");

                var expanderPoint = RoundPoint(Center(LayoutNodeOf(imageView, tree, motor).ExpanderBounds));
                Assert.AreSame(motor, imageView.GetExpanderAt(expanderPoint));

                var clicks = 0;
                var doubleClicks = 0;
                imageView.NodeMouseClick += (sender, e) => clicks++;
                imageView.NodeMouseDoubleClick += (sender, e) => doubleClicks++;

                imageView.ClickAt(expanderPoint, MouseButtons.Left);
                Assert.IsTrue(imageView.IsExpanded(motor), "左鍵點展開鈕會展開");
                Assert.AreEqual(0, clicks, "點展開鈕不觸發 NodeMouseClick");

                var expandedExpanderPoint = RoundPoint(Center(LayoutNodeOf(imageView, tree, motor).ExpanderBounds));
                Assert.AreSame(motor, imageView.GetExpanderAt(expandedExpanderPoint));

                imageView.DoubleClickAt(expandedExpanderPoint, MouseButtons.Left);
                Assert.IsFalse(imageView.IsExpanded(motor), "雙擊展開鈕再切換一次");
                Assert.AreEqual(0, doubleClicks, "雙擊展開鈕不觸發 NodeMouseDoubleClick");
            }
        }

        [TestMethod]
        public void TreeContextMenu_ShowsUncheckedDisplayOnlyForCheckableState_AndUpdatesSettings()
        {
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ClientSize = new Size(300, 200), ShowInTaskbar = false })
            using (var imageView = new CascadeDiagramView { Dock = DockStyle.Fill })
            {
                form.Controls.Add(imageView);
                form.Show();

                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var plainItems = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                imageView.TreeContextMenu.Close();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, plainItems);

                imageView.State = new CascadeCheckableState();
                imageView.ShowChecks = true;
                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var checkboxItems = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                CollectionAssert.AreEqual(new[] { "範圍", "未勾選節點", "顯示" }, checkboxItems);

                var settingsChanged = 0;
                imageView.DrawSettingsChanged += (sender, e) => settingsChanged++;

                var uncheckedMenu = (ToolStripMenuItem)imageView.TreeContextMenu.Items[1];
                var hideChoice = uncheckedMenu.DropDownItems.Cast<ToolStripMenuItem>().Single(item => item.Text == "隱藏");
                hideChoice.PerformClick();
                imageView.TreeContextMenu.Close();

                Assert.AreEqual(CascadeDiagramUncheckedDisplay.Hidden, imageView.UncheckedDisplay);
                Assert.AreEqual(1, settingsChanged);
            }
        }

        [TestMethod]
        public void AllThreeControls_ImplementICascadeView_AndShareUniformEventHandler()
        {
            var tree = TestTrees.Template();
            var motor = Find(tree, "Motor");

            using (var cascade = new CascadeColumnsView())
            using (var checkbox = new CascadeColumnsView { CheckBoxes = true })
            using (var image = new ProbeImageView())
            {
                ICascadeView[] views = { cascade, checkbox, image };
                var selectedPaths = new System.Collections.Generic.List<string>();
                var activatedPaths = new System.Collections.Generic.List<string>();

                foreach (var view in views)
                {
                    view.SelectedPathChanged += (sender, e) => selectedPaths.Add(e.PathText);
                    view.NodeActivated += (sender, e) => activatedPaths.Add(e.PathText);
                    view.SetTree(tree);
                    Assert.IsTrue(view.TrySelectNode(motor));
                    Assert.AreSame(motor, view.SelectedNode);
                }

                Assert.AreEqual(6, selectedPaths.Count);
                image.Press(Keys.Enter);
                CollectionAssert.AreEqual(new[] { "Motor" }, activatedPaths);
            }
        }

        [TestMethod]
        public void ActualSize_ShorterThanClient_LeftAlignedAndVerticallyCentered_UpdatesOnResize()
        {
            var tree = TestTrees.Template();
            var motor = Find(tree, "Motor");
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false })
            using (var imageView = new CascadeDiagramView { Dock = DockStyle.Fill })
            {
                form.Controls.Add(imageView);
                imageView.SetTree(tree);

                var imageSize = imageView.Image.Size;
                form.ClientSize = new Size(imageSize.Width + 200, (imageSize.Height * 3) + 1);
                form.Show();

                var offsetY = (int)System.Math.Floor((imageView.ClientSize.Height - imageSize.Height) / 2f);
                Assert.AreEqual(imageSize.Height, offsetY);

                var motorLayout = LayoutNodeOf(imageView, tree, motor);
                var sampleX = (int)(motorLayout.Bounds.Left + 4f);
                var sampleImageY = (int)(motorLayout.Bounds.Top + (motorLayout.Bounds.Height / 2f));
                var expanderCenter = RoundPoint(Center(motorLayout.ExpanderBounds));

                Assert.IsNull(imageView.GetNodeAt(new Point(sampleX, sampleImageY)), "未置中的原位應為空白");
                Assert.AreSame(motor, imageView.GetNodeAt(new Point(sampleX, sampleImageY + offsetY)));
                Assert.IsNull(imageView.GetExpanderAt(expanderCenter), "未置中的展開鈕原位不應命中");
                Assert.AreSame(motor, imageView.GetExpanderAt(new Point(expanderCenter.X, expanderCenter.Y + offsetY)));

                using (var rendered = new Bitmap(imageView.ClientSize.Width, imageView.ClientSize.Height))
                {
                    imageView.DrawToBitmap(rendered, new Rectangle(Point.Empty, imageView.ClientSize));
                    Assert.AreEqual(imageView.BackColor.ToArgb(), rendered.GetPixel(sampleX, sampleImageY).ToArgb(), "上方空白區域應維持背景色");
                    Assert.AreNotEqual(imageView.BackColor.ToArgb(), rendered.GetPixel(sampleX, sampleImageY + offsetY).ToArgb(), "置中位置應畫出節點底色");
                }

                form.ClientSize = new Size(imageSize.Width + 200, (imageSize.Height * 5) + 1);
                var resizedOffsetY = (int)System.Math.Floor((imageView.ClientSize.Height - imageSize.Height) / 2f);
                Assert.AreEqual(imageSize.Height * 2, resizedOffsetY);
                Assert.IsNull(imageView.GetNodeAt(new Point(sampleX, sampleImageY + offsetY)), "調整大小後舊置中位置不應命中");
                Assert.AreSame(motor, imageView.GetNodeAt(new Point(sampleX, sampleImageY + resizedOffsetY)));
                Assert.AreSame(motor, imageView.GetExpanderAt(new Point(expanderCenter.X, expanderCenter.Y + resizedOffsetY)));
            }
        }

        [TestMethod]
        public void ActualSize_TallerThanClient_TopAlignedAndVerticalScrollWorks()
        {
            var tree = new MenuTree();
            for (var i = 1; i <= 16; i++) tree.AddPath(new[] { $"Item{i}" });

            var first = Find(tree, "Item1");
            var last = Find(tree, "Item16");
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false })
            using (var imageView = new CascadeDiagramView { Dock = DockStyle.Fill })
            {
                form.Controls.Add(imageView);
                imageView.SetTree(tree);
                Assert.IsTrue(imageView.Image.Height > 120);

                form.ClientSize = new Size(imageView.Image.Width + 60, 120);
                form.Show();

                var firstLayout = LayoutNodeOf(imageView, tree, first);
                var firstPoint = RoundPoint(new PointF(firstLayout.Bounds.Left + 4f, firstLayout.Bounds.Top + (firstLayout.Bounds.Height / 2f)));
                Assert.AreEqual(Point.Empty, imageView.AutoScrollPosition);
                Assert.AreSame(first, imageView.GetNodeAt(firstPoint), "圖高於可視區域時靠上對齊");

                Assert.IsTrue(imageView.TrySelectNode(last));
                Assert.IsTrue(imageView.AutoScrollPosition.Y < 0, "選取下方節點應觸發垂直捲動");

                var lastLayout = LayoutNodeOf(imageView, tree, last);
                var scrolledLastPoint = new Point(
                    (int)System.Math.Round(lastLayout.Bounds.Left + 4f) + imageView.AutoScrollPosition.X,
                    (int)System.Math.Round(lastLayout.Bounds.Top + (lastLayout.Bounds.Height / 2f)) + imageView.AutoScrollPosition.Y);
                Assert.AreSame(last, imageView.GetNodeAt(scrolledLastPoint));
            }
        }

        [TestMethod]
        public void BeginRename_AlignsEditBoxWithVerticallyCenteredNode_AndRepositionsOnResize()
        {
            var tree = new MenuTree();
            tree.AddPath(new[] { "Alpha" });
            tree.AddPath(new[] { "Beta" });
            var beta = Find(tree, "Beta");

            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false })
            using (var imageView = new CascadeDiagramView { Dock = DockStyle.Fill, ReadOnly = false })
            {
                form.Controls.Add(imageView);
                imageView.SetTree(tree);
                var imageSize = imageView.Image.Size;
                form.ClientSize = new Size(imageSize.Width + 200, imageSize.Height + 160);
                form.Show();

                Assert.IsTrue(imageView.TrySelectNode(beta));
                Assert.IsTrue(imageView.BeginRename());

                var editBox = imageView.Controls.OfType<TextBox>().Single();
                var betaBounds = LayoutNodeOf(imageView, tree, beta).Bounds;
                var initialOffsetY = (int)System.Math.Floor((imageView.ClientSize.Height - imageSize.Height) / 2f);

                Assert.AreEqual(betaBounds.Left, editBox.Left, 1);
                Assert.AreEqual(betaBounds.Top + (betaBounds.Height / 2f) + initialOffsetY, editBox.Top + (editBox.Height / 2f), 2);

                form.ClientSize = new Size(imageSize.Width + 200, imageSize.Height + 260);
                var resizedOffsetY = (int)System.Math.Floor((imageView.ClientSize.Height - imageSize.Height) / 2f);

                Assert.IsTrue(imageView.IsEditing);
                Assert.AreEqual(betaBounds.Left, editBox.Left, 1);
                Assert.AreEqual(betaBounds.Top + (betaBounds.Height / 2f) + resizedOffsetY, editBox.Top + (editBox.Height / 2f), 2);
            }
        }

        [TestMethod]
        public void ToImagePoint_ReflectsVerticalCenteringInActualSize()
        {
            var tree = new MenuTree();
            tree.AddPath(new[] { "L1", "L2", "L3", "L4", "L5" });

            using (var imageView = new ProbeImageView())
            {
                imageView.SetTree(tree);
                imageView.ExpandAll(Find(tree, "L1"));

                var imageSize = imageView.Image.Size;
                imageView.Size = new Size(imageSize.Width + 100, imageSize.Height + 101);
                var actualOffsetY = (int)System.Math.Floor((imageView.ClientSize.Height - imageSize.Height) / 2f);
                var actualPoint = imageView.ImagePointOf(new Point(24, 18 + actualOffsetY));
                Assert.AreEqual(24f, actualPoint.X, 0.01f);
                Assert.AreEqual(18f, actualPoint.Y, 0.01f);

            }
        }

        #endregion CascadeDiagramView

        #region 鍵盤

        [TestMethod]
        public void Down_CrossesParentsAndSkipsCollapsedChildren()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                imageView.SetExpanded(Find(tree, "System"), true);
                imageView.SetExpanded(Find(tree, "IO"), true);
                view.TrySelectNode(Find(tree, "System", "Info"));

                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "IO", "Input"), view.SelectedNode, "Motor 收合，跳過它的子節點");

                imageView.Press(Keys.Up);
                Assert.AreSame(Find(tree, "System", "Info"), view.SelectedNode);

                imageView.Press(Keys.Up);
                imageView.Press(Keys.Up);
                imageView.Press(Keys.Up);
                Assert.AreSame(Find(tree, "System", "Reboot"), view.SelectedNode, "到頭不動");
            }
        }

        [TestMethod]
        public void Down_WithoutSelection_SelectsFirstDrawnNode()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "System"), view.SelectedNode);
            }
        }

        [TestMethod]
        public void Right_OnCollapsedNode_ExpandsAndEntersByDefault()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                Assert.IsTrue(imageView.StepIntoCollapsed, "預設為 true");
                view.SetTree(tree);
                var motor = Find(tree, "Motor");
                view.TrySelectNode(motor);

                imageView.Press(Keys.Right);
                Assert.IsTrue(imageView.IsExpanded(motor), "→ 自動展開");
                Assert.AreSame(Find(tree, "Motor", "Axis X"), view.SelectedNode);
            }
        }

        [TestMethod]
        public void Right_OnCollapsedNode_StepIntoCollapsedOff_DoesNothingUntilExpanded()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State, StepIntoCollapsed = false })
            {
                view.SetTree(tree);
                var motor = Find(tree, "Motor");
                view.TrySelectNode(motor);

                imageView.Press(Keys.Right);
                Assert.IsFalse(imageView.IsExpanded(motor), "→ 不會自動展開");
                Assert.AreSame(motor, view.SelectedNode, "[+] 的節點按 → 進不去");

                imageView.Press(Keys.Space);
                imageView.Press(Keys.Right);
                Assert.AreSame(Find(tree, "Motor", "Axis X"), view.SelectedNode, "展開後才進得去");
            }
        }

        [TestMethod]
        public void Enter_RaisesNodeActivatedForDrawnSelection()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                var activated = new System.Collections.Generic.List<CascadeNodeEventArgs>();
                imageView.NodeActivated += (sender, e) => activated.Add(e);

                imageView.Press(Keys.Enter);
                Assert.AreEqual(0, activated.Count, "沒有選取時不觸發");

                var motor = Find(tree, "Motor");
                view.TrySelectNode(motor);
                imageView.Press(Keys.Enter);
                Assert.AreEqual(1, activated.Count);
                Assert.AreSame(motor, activated[0].Node);
                Assert.AreEqual("Motor", activated[0].PathText);

                view.TrySelectNode(Find(tree, "Motor", "Axis X"));
                imageView.SetExpanded(motor, false);
                imageView.Press(Keys.Enter);
                Assert.AreEqual(1, activated.Count, "選取被收合藏起來時不觸發");
            }
        }

        [TestMethod]
        public void LeftThenRight_ReturnsToRememberedChild()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                imageView.SetExpanded(Find(tree, "Motor"), true);
                var axisY = Find(tree, "Motor", "Axis Y");
                view.TrySelectNode(axisY);

                imageView.Press(Keys.Left);
                Assert.AreSame(Find(tree, "Motor"), view.SelectedNode);

                imageView.Press(Keys.Left);
                Assert.AreSame(Find(tree, "Motor"), view.SelectedNode, "第一層按 ← 不動");

                imageView.Press(Keys.Right);
                Assert.AreSame(axisY, view.SelectedNode, "和 CascadeColumnsView 一樣走回記憶的子節點");
            }
        }

        [TestMethod]
        public void Space_TogglesSelectedNode()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                var system = Find(tree, "System");
                view.TrySelectNode(system);

                imageView.Press(Keys.Space);
                Assert.IsTrue(imageView.IsExpanded(system));

                imageView.Press(Keys.Space);
                Assert.IsFalse(imageView.IsExpanded(system));
            }
        }

        [TestMethod]
        public void StepBackAndStepForward_ShareCascadeViewMemory()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeColumnsView())
            {
                view.SetTree(tree);
                Assert.IsFalse(view.StepBack(), "未選取時不動");

                var home = Find(tree, "Motor", "Axis Z", "Home");
                view.TrySelectNode(home);

                Assert.IsTrue(view.StepBack());
                Assert.IsTrue(view.StepBack());
                Assert.AreSame(Find(tree, "Motor"), view.SelectedNode);
                Assert.IsFalse(view.StepBack(), "第一層不動");

                Assert.IsTrue(view.StepForward());
                Assert.IsTrue(view.StepForward());
                Assert.AreSame(home, view.SelectedNode);
            }
        }

        [TestMethod]
        public void DigitJump_MultiDigitAndFallback_MatchesColumnsBehavior()
        {
            var tree = new MenuTree();
            for (var i = 1; i <= 24; i++) tree.AddPath(new[] { "Root", $"N{i}" });

            using (var imageView = new ProbeImageView { ShowIndex = true })
            {
                imageView.SetTree(tree);
                var root = Find(tree, "Root");
                imageView.SetExpanded(root, true);

                imageView.TrySelectNode(Find(tree, "Root", "N5"));
                imageView.SelectAll();
                imageView.Press(Keys.D2);
                Assert.AreSame(Find(tree, "Root", "N2"), imageView.SelectedNode);
                Assert.AreEqual(0, imageView.MultiSelectedNodes.Count, "數字跳轉清除多選");

                imageView.Press(Keys.D4);
                Assert.AreSame(Find(tree, "Root", "N24"), imageView.SelectedNode);

                imageView.Press(Keys.D3);
                Assert.AreSame(Find(tree, "Root", "N3"), imageView.SelectedNode);

                imageView.TrySelectNode(Find(tree, "Root", "N5"));
                imageView.Press(Keys.NumPad3);
                Assert.AreSame(Find(tree, "Root", "N3"), imageView.SelectedNode);

                imageView.Press(Keys.NumPad1);
                Assert.AreSame(Find(tree, "Root", "N1"), imageView.SelectedNode);

                imageView.TrySelectNode(Find(tree, "Root", "N5"));
                imageView.Press(Keys.D1);
                Assert.AreSame(Find(tree, "Root", "N1"), imageView.SelectedNode);
                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "Root", "N2"), imageView.SelectedNode);
                imageView.Press(Keys.D4);
                Assert.AreSame(Find(tree, "Root", "N4"), imageView.SelectedNode);
            }
        }

        [TestMethod]
        public void DigitJump_NoSelectionJumpsTopLevel_SkipsUndrawnSiblings()
        {
            var tree = TestTrees.Template();
            using (var imageView = new ProbeImageView { ShowChecks = true, ShowIndex = true, UncheckedDisplay = CascadeDiagramUncheckedDisplay.Hidden })
            {
                imageView.SetTree(tree);
                var checkSet = ((CascadeCheckableState)imageView.State).CheckSet;
                var system = Find(tree, "System");
                var io = Find(tree, "IO");
                checkSet.Check(system);
                checkSet.Check(io);

                imageView.Press(Keys.D3);
                Assert.AreSame(io, imageView.SelectedNode);

                imageView.Press(Keys.D2);
                Assert.AreSame(io, imageView.SelectedNode, "隱藏的兄弟不跳轉");

                imageView.Press(Keys.D1);
                Assert.AreSame(system, imageView.SelectedNode);
            }
        }

        [TestMethod]
        public void ShowIndex_DefaultsFalse_IgnoresDigitKeysAndDrawsNarrowerNodes()
        {
            var tree = TestTrees.Template();
            var system = Find(tree, "System");
            using (var imageView = new ProbeImageView())
            {
                Assert.IsFalse(imageView.ShowIndex, "ShowIndex 預設為 false");
                imageView.SetTree(tree);

                var topDigit = imageView.PressKey(Keys.D2);
                Assert.IsNull(imageView.SelectedNode);
                Assert.IsFalse(topDigit.Handled);
                Assert.IsFalse(topDigit.SuppressKeyPress);

                imageView.TrySelectNode(system);
                var padDigit = imageView.PressKey(Keys.NumPad2);
                Assert.AreSame(system, imageView.SelectedNode);
                Assert.IsFalse(padDigit.Handled);
                Assert.IsFalse(padDigit.SuppressKeyPress);

                var hiddenImageWidth = imageView.Image.Width;
                var hiddenNodeWidth = LayoutNodeOf(imageView, tree, system).Bounds.Width;

                imageView.ShowIndex = true;
                var shownImageWidth = imageView.Image.Width;
                var shownNodeWidth = LayoutNodeOf(imageView, tree, system).Bounds.Width;

                Assert.IsTrue(hiddenNodeWidth < shownNodeWidth, "ShowIndex 關閉時節點較窄");
                Assert.IsTrue(hiddenImageWidth < shownImageWidth);
            }
        }

        [TestMethod]
        public void ShowIndex_Toggle_RaisesDrawSettingsChanged_WidensBounds_AndResetsDigitBuffer()
        {
            var tree = new MenuTree();
            for (var i = 1; i <= 15; i++) tree.AddPath(new[] { "Root", $"N{i}" });
            var root = Find(tree, "Root");
            var n1 = Find(tree, "Root", "N1");
            var n2 = Find(tree, "Root", "N2");
            var n5 = Find(tree, "Root", "N5");

            using (var imageView = new ProbeImageView())
            {
                var settingsChanged = 0;
                imageView.DrawSettingsChanged += (sender, e) => settingsChanged++;

                imageView.SetTree(tree);
                imageView.SetExpanded(root, true);
                imageView.TrySelectNode(n5);

                var narrowImageWidth = imageView.Image.Width;
                var narrowNodeWidth = LayoutNodeOf(imageView, tree, n1).Bounds.Width;

                imageView.ShowIndex = false;
                Assert.AreEqual(0, settingsChanged, "值未改變不觸發 DrawSettingsChanged");

                imageView.ShowIndex = true;
                Assert.AreEqual(1, settingsChanged);
                Assert.IsTrue(imageView.Image.Width > narrowImageWidth);
                Assert.IsTrue(LayoutNodeOf(imageView, tree, n1).Bounds.Width > narrowNodeWidth);

                var firstDigit = imageView.PressKey(Keys.D1);
                Assert.IsTrue(firstDigit.Handled);
                Assert.AreSame(n1, imageView.SelectedNode);

                imageView.ShowIndex = false;
                Assert.AreEqual(2, settingsChanged);
                Assert.AreEqual(narrowImageWidth, imageView.Image.Width);
                Assert.AreEqual(narrowNodeWidth, LayoutNodeOf(imageView, tree, n1).Bounds.Width);

                var ignoredDigit = imageView.PressKey(Keys.D2);
                Assert.IsFalse(ignoredDigit.Handled);
                Assert.AreSame(n1, imageView.SelectedNode);

                imageView.ShowIndex = true;
                Assert.AreEqual(3, settingsChanged);
                imageView.Press(Keys.D2);
                Assert.AreSame(n2, imageView.SelectedNode, "關閉 ShowIndex 時已清空前次緩衝的 1，重開後按 2 應跳到第 2 項而非第 12 項");
            }
        }

        [TestMethod]
        public void ShowChildCount_DefaultsFalse_WidensParentsOnlyAndMatchesExplicitLabel()
        {
            var tree = new MenuTree();
            tree.AddPath(new[] { "Motor", "Axis X", "Home" });
            tree.AddPath(new[] { "Motor", "Axis X", "Speed" });
            tree.AddPath(new[] { "Motor", "Axis X", "Jog", "Plus" });
            tree.AddPath(new[] { "Motor", "Axis X", "Jog", "Minus" });
            tree.AddPath(new[] { "Motor", "Axis Y", "Home" });
            tree.AddPath(new[] { "Motor", "Axis Y", "Speed" });
            tree.AddPath(new[] { "Motor", "Axis Y", "Jog" });
            tree.AddPath(new[] { "Motor", "Axis Z", "Home" });
            tree.AddPath(new[] { "Motor", "Axis Z", "Speed" });
            tree.AddPath(new[] { "Motor", "Axis Z", "Jog" });

            var expected = new MenuTree();
            expected.AddPath(new[] { "Motor (3)", "Axis X (3)", "Home" });
            expected.AddPath(new[] { "Motor (3)", "Axis X (3)", "Speed" });
            expected.AddPath(new[] { "Motor (3)", "Axis X (3)", "Jog (2)", "Plus" });
            expected.AddPath(new[] { "Motor (3)", "Axis X (3)", "Jog (2)", "Minus" });
            expected.AddPath(new[] { "Motor (3)", "Axis Y (3)", "Home" });
            expected.AddPath(new[] { "Motor (3)", "Axis Y (3)", "Speed" });
            expected.AddPath(new[] { "Motor (3)", "Axis Y (3)", "Jog" });
            expected.AddPath(new[] { "Motor (3)", "Axis Z (3)", "Home" });
            expected.AddPath(new[] { "Motor (3)", "Axis Z (3)", "Speed" });
            expected.AddPath(new[] { "Motor (3)", "Axis Z (3)", "Jog" });

            var motor = Find(tree, "Motor");
            var axisX = Find(tree, "Motor", "Axis X");
            var plus = Find(tree, "Motor", "Axis X", "Jog", "Plus");

            using (var imageView = new ProbeImageView())
            {
                Assert.IsFalse(imageView.ShowChildCount, "ShowChildCount 預設為 false");
                var settingsChanged = 0;
                imageView.DrawSettingsChanged += (sender, e) => settingsChanged++;

                imageView.SetTree(tree);
                imageView.ExpandAll(motor);

                var baseMotorWidth = LayoutNodeOf(imageView, tree, motor).Bounds.Width;
                var baseAxisWidth = LayoutNodeOf(imageView, tree, axisX).Bounds.Width;
                var baseLeafWidth = LayoutNodeOf(imageView, tree, plus).Bounds.Width;

                imageView.ShowChildCount = false;
                Assert.AreEqual(0, settingsChanged, "值未改變不觸發 DrawSettingsChanged");

                imageView.ShowChildCount = true;
                Assert.AreEqual(1, settingsChanged);
                Assert.IsTrue(LayoutNodeOf(imageView, tree, motor).Bounds.Width > baseMotorWidth, "有子節點時方塊變寬以容納 (3)");
                Assert.IsTrue(LayoutNodeOf(imageView, tree, axisX).Bounds.Width > baseAxisWidth, "Axis X 有子節點時方塊變寬");
                Assert.AreEqual(baseLeafWidth, LayoutNodeOf(imageView, tree, plus).Bounds.Width, "葉節點（0 個子節點）不顯示數量，寬度不變");

                using (var actual = TreeDrawer.RenderImage(tree, imageView.Font, showChildCount: true))
                using (var reference = TreeDrawer.RenderImage(expected, imageView.Font, showChildCount: false))
                {
                    Assert.AreEqual(reference.Image.Size, actual.Image.Size, "帶子節點數量的圖面大小應與明確加上 (數量) 的名稱完全一致");
                    for (var i = 0; i < reference.Nodes.Count; i++)
                    {
                        Assert.AreEqual(reference.Nodes[i].Bounds, actual.Nodes[i].Bounds, reference.Nodes[i].Node.FullPathText);
                    }
                }

                // 收合時仍維持相同方塊寬度（依原樹直屬子節點數顯示 (3)）。
                imageView.SetExpanded(motor, false);
                Assert.IsTrue(LayoutNodeOf(imageView, tree, motor).Bounds.Width > baseMotorWidth, "收合時仍顯示子節點數量");

                imageView.ShowChildCount = false;
                Assert.AreEqual(2, settingsChanged);
                Assert.AreEqual(baseMotorWidth, LayoutNodeOf(imageView, tree, motor).Bounds.Width);
            }
        }

        [TestMethod]
        public void ExpandAllAndCollapseAll_ToggleEntireSubtree_NullThrows_LeafNoOp()
        {
            var tree = TestTrees.Template();
            using (var imageView = new ProbeImageView())
            {
                imageView.SetTree(tree);
                Assert.ThrowsException<System.ArgumentNullException>(() => imageView.ExpandAll(null));
                Assert.ThrowsException<System.ArgumentNullException>(() => imageView.CollapseAll(null));

                var diagnostics = Find(tree, "Diagnostics");
                var sensor = Find(tree, "Diagnostics", "Sensor");
                var temp = Find(tree, "Diagnostics", "Sensor", "Temperature");
                var zoneB = Find(tree, "Diagnostics", "Sensor", "Temperature", "Zone B");
                var read = Find(tree, "Diagnostics", "Sensor", "Temperature", "Zone B", "Read");
                var motor = Find(tree, "Motor");

                imageView.ExpandAll(diagnostics);
                foreach (var branch in new[] { diagnostics, sensor, temp, zoneB, read })
                    Assert.IsTrue(imageView.IsExpanded(branch), branch.FullPathText);
                Assert.IsFalse(imageView.IsExpanded(motor), "其他分支不受影響");

                imageView.CollapseAll(diagnostics);
                foreach (var branch in new[] { diagnostics, sensor, temp, zoneB, read })
                    Assert.IsFalse(imageView.IsExpanded(branch), branch.FullPathText);

                var beforeLeaf = imageView.Image;
                imageView.ExpandAll(Find(tree, "System", "Reboot"));
                Assert.AreSame(beforeLeaf, imageView.Image, "葉節點不重畫");
            }
        }

        [TestMethod]
        public void MultiplyKey_ExpandsSelectedNodeAndDescendants()
        {
            var tree = TestTrees.Template();
            using (var imageView = new ProbeImageView())
            {
                imageView.SetTree(tree);
                var motor = Find(tree, "Motor");
                var axisX = Find(tree, "Motor", "Axis X");
                var axisY = Find(tree, "Motor", "Axis Y");
                imageView.TrySelectNode(motor);

                imageView.Press(Keys.Multiply);
                Assert.IsTrue(imageView.IsExpanded(motor));
                Assert.IsTrue(imageView.IsExpanded(axisX));
                Assert.IsTrue(imageView.IsExpanded(axisY));

                imageView.CollapseAll(motor);
                imageView.PressChar('*');
                Assert.IsTrue(imageView.IsExpanded(motor));
                Assert.IsTrue(imageView.IsExpanded(axisX));
                Assert.IsTrue(imageView.IsExpanded(axisY));
            }
        }

        #endregion 鍵盤
    }
}