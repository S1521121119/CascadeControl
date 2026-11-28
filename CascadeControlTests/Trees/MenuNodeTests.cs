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
        public void CascadeNodeView_UsesName()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("A", "A1");
            ICascadeNode branch = leaf.Parent;

            Assert.AreEqual("A", branch.Name);
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

        [TestMethod]
        public void Descendants_PreOrderInInsertionOrder_ExcludesSelf()
        {
            var tree = new MenuTree();
            tree.AddPath("B", "B1");
            tree.AddPath("A", "A1", "A11");
            tree.AddPath("A", "A2");
            tree.AddPath("B", "B2");

            CollectionAssert.AreEqual(
                new[] { "B", "B1", "B2", "A", "A1", "A11", "A2" },
                tree.Root.Descendants().Select(n => n.Name).ToArray());

            var a = tree.Root.Children[1];
            CollectionAssert.AreEqual(new[] { "A1", "A11", "A2" }, a.Descendants().Select(n => n.Name).ToArray());
            Assert.IsFalse(a.Children[1].Descendants().Any(), "葉節點沒有子孫");
        }

        [TestMethod]
        public void IsDescendantOf_SelfAncestorUnrelatedDetachedAndNull()
        {
            var tree = new MenuTree();
            var a11 = tree.AddPath("A", "A1", "A11");
            var a1 = a11.Parent;
            var a = a1.Parent;
            var b = tree.AddPath("B");

            Assert.IsTrue(a11.IsDescendantOf(a1));
            Assert.IsTrue(a11.IsDescendantOf(a));
            Assert.IsTrue(a11.IsDescendantOf(tree.Root));
            Assert.IsFalse(a11.IsDescendantOf(a11), "自己不算");
            Assert.IsFalse(tree.Root.IsDescendantOf(tree.Root), "樹根自己也不算");
            Assert.IsFalse(a.IsDescendantOf(a11), "子孫不是祖先");
            Assert.IsFalse(a11.IsDescendantOf(b), "無關的節點");
            Assert.IsFalse(a11.IsDescendantOf(null));

            a1.RemoveFromParent();

            Assert.IsTrue(a11.IsDescendantOf(a1), "脫離的子樹內部關係不變");
            Assert.IsFalse(a11.IsDescendantOf(tree.Root), "脫離之後不在樹裡");
            Assert.IsFalse(a1.IsDescendantOf(a));
        }
    }
}