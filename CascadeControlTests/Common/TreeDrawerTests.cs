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

        #endregion CascadeTreeImageView

        #region 鍵盤

        private sealed class ProbeImageView : CascadeTreeImageView
        {
            public void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));
        }

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