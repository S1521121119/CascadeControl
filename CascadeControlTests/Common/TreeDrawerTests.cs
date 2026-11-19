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
            using (var imageView = new CascadeTreeImageView { View = view })
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
            using (var imageView = new CascadeTreeImageView { View = view })
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
            using (var imageView = new CascadeTreeImageView { View = view })
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
            var checkSet = (imageView.View as ICascadeCheckableView)?.CheckSet;
            var isChecked = checkSet == null ? (System.Func<MenuNode, bool>)null : checkSet.IsChecked;
            using (var layout = TreeDrawer.RenderImage(tree, imageView.Font, isChecked, imageView.DrawScope, imageView.UncheckedDisplay, imageView.View?.SelectedNode as MenuNode, imageView.IsExpanded))
            {
                return layout.Nodes.Single(node => ReferenceEquals(node.Node, target));
            }
        }

        [TestMethod]
        public void View_ReplaceAndDispose_DetachesOldSubscriptionsAndClearsImage()
        {
            var firstTree = TestTrees.Template();
            var secondTree = TestTrees.Template();

            using (var first = new CascadeCheckboxView())
            using (var second = new CascadeView())
            using (var imageView = new CascadeTreeImageView { View = first })
            {
                first.SetTree(firstTree);
                second.SetTree(secondTree);

                var beforeCheck = imageView.Image;
                Assert.IsTrue(beforeCheck.Width > 1 && beforeCheck.Height > 1);

                first.CheckSet.Check(Find(firstTree, "Motor", "Axis X"));
                var afterCheck = imageView.Image;
                Assert.AreNotSame(beforeCheck, afterCheck, "勾選改變要自動重畫");

                imageView.View = second;
                var afterSwap = imageView.Image;
                Assert.AreNotSame(afterCheck, afterSwap);

                first.TrySelectNode(Find(firstTree, "System"));
                first.CheckSet.Check(Find(firstTree, "System"));
                Assert.AreSame(afterSwap, imageView.Image, "換掉 View 後，舊 View 的選取與勾選不再觸發重畫");

                second.TrySelectNode(Find(secondTree, "Motor"));
                Assert.AreNotSame(afterSwap, imageView.Image, "新 View 的選取改變會重畫");

                second.Dispose();
                Assert.IsNull(imageView.View, "View 釋放後自動解除綁定");
                Assert.AreEqual(new Size(1, 1), imageView.Image.Size, "解除綁定後畫成 1x1 空白圖");
            }
        }

        [TestMethod]
        public void MouseClickAndDoubleClick_OnNodeAndBlank_RaiseEventsWithPathSeparator()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
            using (var imageView = new ProbeImageView { View = view })
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

                var clicks = new System.Collections.Generic.List<CascadeTreeImageNodeMouseEventArgs>();
                var doubleClicks = new System.Collections.Generic.List<CascadeTreeImageNodeMouseEventArgs>();
                imageView.NodeMouseClick += (sender, e) => clicks.Add(e);
                imageView.NodeMouseDoubleClick += (sender, e) => doubleClicks.Add(e);

                imageView.ClickAt(clickPoint, MouseButtons.Left);
                imageView.ClickAt(clickPoint, MouseButtons.Right);
                imageView.DoubleClickAt(clickPoint, MouseButtons.Left);
                imageView.ClickAt(new Point(0, 0), MouseButtons.Left);

                Assert.AreEqual(2, clicks.Count, "空白處點擊不觸發 NodeMouseClick");
                Assert.AreSame(axisX, clicks[0].Node);
                Assert.AreEqual("Motor / Axis X", clicks[0].PathText);
                CollectionAssert.AreEqual(axisX.FullPath.ToList(), clicks[0].Path.ToList());
                Assert.AreEqual(MouseButtons.Left, clicks[0].Button);
                Assert.AreEqual(clickPoint, clicks[0].Location);
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
            using (var imageView = new ProbeImageView { View = view })
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
        public void TreeContextMenu_ShowsUncheckedDisplayOnlyForCheckableView_AndUpdatesSettings()
        {
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ClientSize = new Size(300, 200), ShowInTaskbar = false })
            using (var plainView = new CascadeView())
            using (var checkboxView = new CascadeCheckboxView())
            using (var imageView = new CascadeTreeImageView { Dock = DockStyle.Fill, View = plainView })
            {
                form.Controls.Add(imageView);
                form.Show();

                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var plainItems = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                imageView.TreeContextMenu.Close();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, plainItems);

                imageView.View = checkboxView;
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
        public void View_BindsPureCSharpICascadeView_RendersNavigatesAndHidesUncheckedMenu()
        {
            var tree = TestTrees.Template();
            var fake = new FakeCascadeView();
            fake.SetTree(tree);

            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ClientSize = new Size(300, 200), ShowInTaskbar = false })
            using (var imageView = new ProbeImageView { Dock = DockStyle.Fill, View = fake })
            {
                form.Controls.Add(imageView);
                form.Show();

                Assert.IsTrue(imageView.Image.Width > 1 && imageView.Image.Height > 1);

                var beforeSelect = imageView.Image;
                imageView.Press(Keys.Down);
                Assert.AreSame(Find(tree, "System"), fake.SelectedNode);
                Assert.AreNotSame(beforeSelect, imageView.Image, "純 C# ICascadeView 觸發 SelectedPathChanged 後自動重畫");

                imageView.Press(Keys.Space);
                imageView.Press(Keys.Right);
                Assert.AreSame(Find(tree, "System", "Reboot"), fake.SelectedNode);

                imageView.Press(Keys.Left);
                Assert.AreSame(Find(tree, "System"), fake.SelectedNode);

                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var items = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                imageView.TreeContextMenu.Close();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, items);
            }
        }

        [TestMethod]
        public void View_BindsPureCSharpICascadeCheckableView_TracksChecksAndShowsUncheckedMenu()
        {
            var tree = TestTrees.Template();
            var fake = new FakeCheckableCascadeView();
            fake.SetTree(tree);
            var motor = Find(tree, "Motor");
            var axisX = Find(tree, "Motor", "Axis X");

            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ClientSize = new Size(300, 200), ShowInTaskbar = false })
            using (var imageView = new CascadeTreeImageView { Dock = DockStyle.Fill, View = fake })
            {
                form.Controls.Add(imageView);
                form.Show();

                Assert.IsFalse(imageView.IsExpanded(motor));
                var beforeCheck = imageView.Image;

                fake.CheckSet.Check(axisX);
                Assert.IsTrue(imageView.IsExpanded(motor), "純 C# ICascadeCheckableView 的 CheckSet 驅動展開狀態");
                Assert.AreNotSame(beforeCheck, imageView.Image, "純 C# ICascadeCheckableView 的 CheckedChanged 觸發重畫");

                imageView.TreeContextMenu.Show(imageView, new Point(10, 10));
                var items = imageView.TreeContextMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToList();
                imageView.TreeContextMenu.Close();
                CollectionAssert.AreEqual(new[] { "範圍", "未勾選節點", "顯示" }, items);

                imageView.View = null;
                var afterDetach = imageView.Image;
                fake.CheckSet.Uncheck(axisX);
                Assert.AreSame(afterDetach, imageView.Image, "解除綁定後不再重畫");
            }
        }

        [TestMethod]
        public void ViewProperty_UsesReferenceConverter_AndListsICascadeViewComponents()
        {
            var property = System.ComponentModel.TypeDescriptor.GetProperties(typeof(CascadeTreeImageView))["View"];
            Assert.IsNotNull(property);
            Assert.IsInstanceOfType(property.Converter, typeof(System.ComponentModel.ReferenceConverter));

            using (var plain = new CascadeView())
            using (var checkable = new CascadeCheckboxView())
            using (var unrelated = new Button())
            {
                var context = new ReferenceServiceContext(plain, checkable, unrelated);
                Assert.IsTrue(property.Converter.GetStandardValuesSupported(context));

                var values = property.Converter.GetStandardValues(context).Cast<object>().ToList();
                Assert.IsTrue(values.Contains(plain), "設計工具下拉清單應包含 CascadeView");
                Assert.IsTrue(values.Contains(checkable), "設計工具下拉清單應包含 CascadeCheckboxView");
                Assert.IsFalse(values.Contains(unrelated), "不實作 ICascadeView 的控制項不可出現在清單中");
            }
        }

        private class FakeCascadeView : ICascadeView
        {
            private MenuNode _selected;

            public MenuTree Tree { get; private set; }

            public ICascadeNode SelectedNode => _selected;

            public System.Collections.Generic.IReadOnlyList<ICascadeNode> SelectedPath =>
                _selected == null ? (System.Collections.Generic.IReadOnlyList<ICascadeNode>)new ICascadeNode[0] : _selected.FullPath.Cast<ICascadeNode>().ToList();

            public string SelectedPathText => string.Join(PathSeparator, SelectedPath.Select(node => node.Name));

            public bool IsLeafSelected => _selected != null && _selected.IsLeaf;

            public string PathSeparator { get; set; } = " => ";

            public event System.EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

            public event System.EventHandler<CascadeNodeEventArgs> NodeActivated;

            public virtual void SetTree(MenuTree tree)
            {
                Tree = tree;
                _selected = null;
                RaiseSelectedPathChanged();
            }

            public void Clear() => SetTree(null);

            public bool TrySelectNode(MenuNode node)
            {
                if (node == null) return false;

                _selected = node;
                RaiseSelectedPathChanged();
                return true;
            }

            public bool TrySelectPath(System.Collections.Generic.IEnumerable<ICascadeNode> path)
            {
                var last = path?.LastOrDefault() as MenuNode;
                return TrySelectNode(last);
            }

            public bool StepBack()
            {
                if (_selected == null || _selected.Parent == null || ReferenceEquals(_selected.Parent, Tree?.Root)) return false;

                _selected = _selected.Parent;
                RaiseSelectedPathChanged();
                return true;
            }

            public bool StepForward()
            {
                if (_selected == null || _selected.Children.Count == 0) return false;

                _selected = _selected.Children[0];
                RaiseSelectedPathChanged();
                return true;
            }

            protected void RaiseSelectedPathChanged()
            {
                SelectedPathChanged?.Invoke(this, new CascadeNodeEventArgs(SelectedPath, _selected == null ? Tree?.TopLevelNodes : null));
            }

            protected void RaiseNodeActivated()
            {
                NodeActivated?.Invoke(this, new CascadeNodeEventArgs(SelectedPath, _selected == null ? Tree?.TopLevelNodes : null));
            }
        }

        private sealed class FakeCheckableCascadeView : FakeCascadeView, ICascadeCheckableView
        {
            private MenuCheckSet _checkSet;

            public MenuCheckSet CheckSet
            {
                get => _checkSet;
                set
                {
                    if (_checkSet != null) _checkSet.Changed -= OnCheckSetChanged;
                    _checkSet = value;
                    if (_checkSet != null) _checkSet.Changed += OnCheckSetChanged;
                }
            }

            public event System.EventHandler<MenuCheckChangedEventArgs> CheckedChanged;

            public override void SetTree(MenuTree tree)
            {
                CheckSet = tree == null ? null : new MenuCheckSet(tree);
                base.SetTree(tree);
            }

            private void OnCheckSetChanged(object sender, MenuCheckChangedEventArgs e) => CheckedChanged?.Invoke(this, e);
        }

        private sealed class ReferenceServiceContext : System.ComponentModel.ITypeDescriptorContext, System.ComponentModel.Design.IReferenceService
        {
            private readonly object[] _components;

            public ReferenceServiceContext(params object[] components)
            {
                _components = components;
            }

            public System.ComponentModel.IContainer Container => null;
            public object Instance => null;
            public System.ComponentModel.PropertyDescriptor PropertyDescriptor => null;
            public bool OnComponentChanging() => true;
            public void OnComponentChanged() { }
            public object GetService(System.Type serviceType) => serviceType == typeof(System.ComponentModel.Design.IReferenceService) ? this : null;

            public object[] GetReferences() => _components;
            public object[] GetReferences(System.Type baseType) => _components.Where(baseType.IsInstanceOfType).ToArray();
            public object GetReference(string name) => null;
            public string GetName(object reference) => reference?.GetType().Name;
            public System.ComponentModel.IComponent GetComponent(object reference) => reference as System.ComponentModel.IComponent;
        }

        #endregion CascadeTreeImageView

        #region 鍵盤

        [TestMethod]
        public void Down_CrossesParentsAndSkipsCollapsedChildren()
        {
            var tree = TestTrees.Template();
            using (var view = new CascadeView())
            using (var imageView = new ProbeImageView { View = view })
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
            using (var imageView = new ProbeImageView { View = view })
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
            using (var imageView = new ProbeImageView { View = view })
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
            using (var imageView = new ProbeImageView { View = view })
            {
                view.SetTree(tree);
                var activated = new System.Collections.Generic.List<CascadeTreeImageNodeEventArgs>();
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
            using (var imageView = new ProbeImageView { View = view })
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
            using (var imageView = new ProbeImageView { View = view })
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