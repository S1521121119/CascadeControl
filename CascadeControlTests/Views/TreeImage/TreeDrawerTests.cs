using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Common
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

        #endregion TreeDrawer

        #region CascadeTreeImageView

        [TestMethod]
        public void IsExpanded_WithoutCheckInfo_DefaultsToCollapsed()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
            using (var imageView = new CascadeTreeImageView { State = view.State })
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
            using (var view = new CascadeCheckboxView())
            using (var imageView = new CascadeTreeImageView { State = view.State })
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
            using (var view = new CascadeCheckboxView())
            using (var imageView = new CascadeTreeImageView { State = view.State })
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

        private sealed class ProbeImageView : CascadeTreeImageView
        {
            public void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));

            public void ClickAt(Point location, MouseButtons button = MouseButtons.Left) =>
                OnMouseClick(new MouseEventArgs(button, 1, location.X, location.Y, 0));

            public void DoubleClickAt(Point location, MouseButtons button = MouseButtons.Left) =>
                OnMouseDoubleClick(new MouseEventArgs(button, 2, location.X, location.Y, 0));
        }

        private static Point RoundPoint(PointF point) => new Point((int)System.Math.Round(point.X), (int)System.Math.Round(point.Y));

        private static TreeImageNode LayoutNodeOf(CascadeTreeImageView imageView, MenuTree tree, MenuNode target)
        {
            var checkSet = (imageView.State as CascadeCheckableState)?.CheckSet;
            var isChecked = checkSet == null ? (System.Func<MenuNode, bool>)null : checkSet.IsChecked;
            using (var layout = TreeDrawer.RenderImage(tree, imageView.Font, isChecked, imageView.DrawScope, imageView.UncheckedDisplay, imageView.SelectedNode as MenuNode, imageView.IsExpanded))
            {
                return layout.Nodes.Single(node => ReferenceEquals(node.Node, target));
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

            using (var imageView = new CascadeTreeImageView { State = firstState })
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
            using (var view = new CascadeCheckboxView { ReadOnly = false })
            using (var imageView = new CascadeTreeImageView { State = view.State })
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
            using (var view = new CascadeView())
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
            using (var view = new CascadeView())
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
            using (var imageView = new CascadeTreeImageView { Dock = DockStyle.Fill })
            {
                form.Controls.Add(imageView);
                form.Show();

                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var plainItems = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                imageView.TreeContextMenu.Close();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, plainItems);

                imageView.State = new CascadeCheckableState();
                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var checkboxItems = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                CollectionAssert.AreEqual(new[] { "範圍", "未勾選節點", "顯示" }, checkboxItems);

                var settingsChanged = 0;
                imageView.DrawSettingsChanged += (sender, e) => settingsChanged++;

                var uncheckedMenu = (ToolStripMenuItem)imageView.TreeContextMenu.Items[1];
                var hideChoice = uncheckedMenu.DropDownItems.Cast<ToolStripMenuItem>().Single(item => item.Text == "隱藏");
                hideChoice.PerformClick();
                imageView.TreeContextMenu.Close();

                Assert.AreEqual(UncheckedNodeDisplay.Hidden, imageView.UncheckedDisplay);
                Assert.AreEqual(1, settingsChanged);
            }
        }

        [TestMethod]
        public void AllThreeControls_ImplementICascadeView_AndShareUniformEventHandler()
        {
            var tree = TestTrees.Template();
            var motor = Find(tree, "Motor");

            using (var cascade = new CascadeView())
            using (var checkbox = new CascadeCheckboxView())
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

        #endregion CascadeTreeImageView

        #region 鍵盤

        [TestMethod]
        public void Down_CrossesParentsAndSkipsCollapsedChildren()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
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
            using (var view = new CascadeView())
            using (var imageView = new ProbeImageView { State = view.State })
            {
                view.SetTree(tree);
                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "System"), view.SelectedNode);
            }
        }

        [TestMethod]
        public void Right_OnCollapsedNode_DoesNothingUntilExpanded()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
            using (var imageView = new ProbeImageView { State = view.State })
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
            using (var view = new CascadeView())
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
            using (var view = new CascadeView())
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
                Assert.AreSame(axisY, view.SelectedNode, "和 CascadeView 一樣走回記憶的子節點");
            }
        }

        [TestMethod]
        public void Space_TogglesSelectedNode()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
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
            using (var view = new CascadeView())
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

        #endregion 鍵盤
    }
}