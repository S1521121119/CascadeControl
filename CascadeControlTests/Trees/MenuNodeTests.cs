using CascadeControl.Common;
using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CascadeControlTests.Trees
{
    [TestClass]
    public class MenuNodeTests
    {
        [TestMethod]
        public void GetOrAddChild_SameName_ReturnsSameNode()
        {
            var root = new MenuNode(string.Empty);

            var first = root.GetOrAddChild("A");
            var second = root.GetOrAddChild("A");

            Assert.AreSame(first, second);
            Assert.AreEqual(1, root.Children.Count);
        }

        [TestMethod]
        public void GetOrAddChild_SetsParentDepthAndSiblingNumberInInsertionOrder()
        {
            var root = new MenuNode(string.Empty);
            var a = root.GetOrAddChild("A");
            var b = root.GetOrAddChild("B");
            var a1 = a.GetOrAddChild("A1");

            Assert.AreSame(root, a.Parent);
            Assert.AreSame(a, a1.Parent);
            Assert.AreEqual(1, a.Depth);
            Assert.AreEqual(2, a1.Depth);
            Assert.AreEqual(1, a.SiblingNumber);
            Assert.AreEqual(2, b.SiblingNumber);
            Assert.AreEqual(1, a1.SiblingNumber, "序號只在同一層的兄弟之間計算");
            CollectionAssert.AreEqual(new[] { a, b }, root.Children.ToArray());
        }

        [TestMethod]
        public void Names_AreCaseSensitive()
        {
            var root = new MenuNode(string.Empty);
            root.GetOrAddChild("Reboot");
            root.GetOrAddChild("reboot");

            Assert.AreEqual(2, root.Children.Count);
        }

        [TestMethod]
        public void Constructor_NullName_BecomesEmpty()
        {
            Assert.AreEqual(string.Empty, new MenuNode(null).Name);
        }

        [TestMethod]
        public void TryGetChild_FindsOnlyExistingChildren()
        {
            var root = new MenuNode(string.Empty);
            var a = root.GetOrAddChild("A");

            Assert.IsTrue(root.TryGetChild("A", out var found));
            Assert.AreSame(a, found);
            Assert.IsFalse(root.TryGetChild("B", out var missing));
            Assert.IsNull(missing);
            Assert.IsFalse(root.TryGetChild(null, out _));
        }

        [TestMethod]
        public void DisplayText_LeafAndBranchFormats()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("A", "A1");
            var branch = leaf.Parent;

            Assert.AreEqual("<01>A1", leaf.DisplayText);
            Assert.AreEqual("<01>A [+]", branch.DisplayText);
            Assert.AreEqual(string.Empty, tree.Root.DisplayText);
        }

        [TestMethod]
        public void DisplayText_RefreshesWhenLeafGainsChildren()
        {
            var tree = new MenuTree();
            var node = tree.AddPath("A");
            Assert.AreEqual("<01>A", node.DisplayText, "先讀一次，讓顯示文字被快取");

            tree.AddPath("A", "A1");

            Assert.AreEqual("<01>A [+]", node.DisplayText);
        }

        [TestMethod]
        public void DisplayText_SiblingNumberIsTwoDigits()
        {
            var tree = new MenuTree();
            var nodes = tree.ExpandAndAddPaths(TestTrees.Alternatives("N", 12));

            Assert.AreEqual("<12>N12", nodes[11].DisplayText);
        }

        [TestMethod]
        public void FullPathTextAndFullPath_ExcludeRoot()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("Motor", "Axis X", "Home");

            Assert.AreEqual("Motor > Axis X > Home", leaf.FullPathText);
            CollectionAssert.AreEqual(new[] { "Motor", "Axis X", "Home" }, leaf.FullPath.Select(n => n.Name).ToArray());
            Assert.AreEqual(0, tree.Root.FullPath.Count);
            Assert.AreEqual(string.Empty, tree.Root.FullPathText);
        }

        [TestMethod]
        public void CascadeNodeView_UsesNameAndDisplayText()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("A", "A1");
            ICascadeNode branch = leaf.Parent;

            Assert.AreEqual("A", branch.Name);
            Assert.AreEqual("<01>A [+]", branch.DisplayText);
            Assert.AreSame(leaf, branch.Children.Single());
            Assert.AreEqual("A", branch.ToString());
        }

        [TestMethod]
        public void IsLeaf_ReflectsChildren()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("A", "A1");

            Assert.IsTrue(leaf.IsLeaf);
            Assert.IsFalse(leaf.Parent.IsLeaf);
        }
    }
}