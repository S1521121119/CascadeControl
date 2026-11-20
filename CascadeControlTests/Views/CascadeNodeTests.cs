using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Views
{
    /// <summary>
    /// Views 命名空間中不需要視窗的部分：<see cref="CascadeNode"/> 與 <see cref="CascadeNodeEventArgs"/>。
    /// <see cref="CascadeView"/> 本身要建立 ListBox 與視窗訊息，不在單元測試範圍內。
    /// </summary>
    [TestClass]
    public class CascadeNodeTests
    {
        #region CascadeNode

        [TestMethod]
        public void CascadeNode_StoresValuesAndChildren()
        {
            var tag = new object();
            var child = new CascadeNode("Child", "<child>");
            var item = new CascadeNode("Name", "Text", tag, new[] { child });

            Assert.AreEqual("Name", item.Name);
            Assert.AreEqual("Text", item.DisplayText);
            Assert.AreSame(tag, item.Tag);
            Assert.AreSame(child, item.Children.Single());
            Assert.AreSame(child, ((ICascadeNode)item).Children.Single());
            Assert.AreEqual("Name", item.ToString());
        }

        [TestMethod]
        public void CascadeNode_NullArguments_BecomeEmpty()
        {
            var item = new CascadeNode(null, null);

            Assert.AreEqual(string.Empty, item.Name);
            Assert.AreEqual(string.Empty, item.DisplayText);
            Assert.IsNull(item.Tag);
            Assert.AreEqual(0, item.Children.Count);
        }

        [TestMethod]
        public void CascadeNode_ChildrenAreEditable()
        {
            var item = new CascadeNode("A", "A");
            item.Children.Add(new CascadeNode("B", "B"));

            Assert.AreEqual(1, ((ICascadeNode)item).Children.Count);
        }

        #endregion CascadeNode

        #region CascadeNodeEventArgs

        [TestMethod]
        public void EventArgs_NoSelection_ExposesTopLevelNodes()
        {
            var roots = new[] { new CascadeNode("A", "A"), new CascadeNode("B", "B") };

            var args = new CascadeNodeEventArgs(null, roots);

            Assert.AreEqual(0, args.Path.Count);
            Assert.IsNull(args.Node);
            Assert.IsNull(args.Parent);
            Assert.AreEqual(-1, args.ColumnIndex);
            Assert.IsFalse(args.IsLeaf);
            CollectionAssert.AreEqual(roots, args.Children.ToArray());
        }

        [TestMethod]
        public void EventArgs_NoSelectionAndNoTopLevelNodes_ChildrenIsEmptyNotNull()
        {
            var args = new CascadeNodeEventArgs(new ICascadeNode[0]);

            Assert.IsNotNull(args.Children);
            Assert.AreEqual(0, args.Children.Count);
        }

        [TestMethod]
        public void EventArgs_PathIntoBranch()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("Motor", "Axis X", "Home");
            var axis = leaf.Parent;

            var args = new CascadeNodeEventArgs(new ICascadeNode[] { axis.Parent, axis }, tree.TopLevelNodes);

            Assert.AreSame(axis, args.Node);
            Assert.AreSame(axis.Parent, args.Parent);
            Assert.AreEqual(1, args.ColumnIndex);
            Assert.IsFalse(args.IsLeaf);
            Assert.AreSame(leaf, args.Children.Single(), "選取了節點時，Children 是它的子節點而不是第一欄");
        }

        [TestMethod]
        public void EventArgs_PathToLeaf()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("System", "Reboot");

            var args = new CascadeNodeEventArgs(leaf.FullPath);

            Assert.AreSame(leaf, args.Node);
            Assert.AreEqual(1, args.ColumnIndex);
            Assert.IsTrue(args.IsLeaf);
            Assert.AreEqual(0, args.Children.Count);
        }

        [TestMethod]
        public void EventArgs_NodeWithNullChildren_IsTreatedAsLeaf()
        {
            var args = new CascadeNodeEventArgs(new ICascadeNode[] { new NullChildrenNode() });

            Assert.IsNotNull(args.Children);
            Assert.IsTrue(args.IsLeaf);
        }

        [TestMethod]
        public void EventArgs_PathIsSnapshot()
        {
            var path = new List<ICascadeNode> { new CascadeNode("A", "A") };
            var args = new CascadeNodeEventArgs(path);

            path.Add(new CascadeNode("B", "B"));

            Assert.AreEqual(1, args.Path.Count);
        }

        [TestMethod]
        public void EventArgs_NullStepInPath_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                new CascadeNodeEventArgs(new ICascadeNode[] { new CascadeNode("A", "A"), null }));
        }

        [TestMethod]
        public void EventArgs_PathText_FormatsWithSeparator()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("Motor", "Axis X", "Home");

            var defaultArgs = new CascadeNodeEventArgs(leaf.FullPath);
            Assert.AreEqual("Motor => Axis X => Home", defaultArgs.PathText);

            var customArgs = new CascadeNodeEventArgs(leaf.FullPath, null, " / ");
            Assert.AreEqual("Motor / Axis X / Home", customArgs.PathText);

            var emptyArgs = new CascadeNodeEventArgs(null);
            Assert.AreEqual(string.Empty, emptyArgs.PathText);
        }

        [TestMethod]
        public void MouseEventArgs_InheritsNodeEventArgsAndRejectsEmptyPath()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("System", "Reboot");
            var bounds = new System.Drawing.Rectangle(10, 20, 80, 18);
            var location = new System.Drawing.Point(15, 25);
            var screen = new System.Drawing.Point(115, 125);

            var args = new CascadeNodeMouseEventArgs(leaf.FullPath, " / ", System.Windows.Forms.MouseButtons.Left, location, bounds, screen);

            Assert.AreSame(leaf, args.Node);
            Assert.AreEqual(1, args.ColumnIndex);
            Assert.AreEqual("System / Reboot", args.PathText);
            Assert.IsTrue(args.IsLeaf);
            Assert.AreEqual(System.Windows.Forms.MouseButtons.Left, args.Button);
            Assert.AreEqual(location, args.Location);
            Assert.AreEqual(bounds, args.Bounds);
            Assert.AreEqual(screen, args.ScreenLocation);

            Assert.ThrowsException<ArgumentException>(() =>
                new CascadeNodeMouseEventArgs(new ICascadeNode[0], " / ", System.Windows.Forms.MouseButtons.Left, location, bounds, screen));
        }

        private sealed class NullChildrenNode : ICascadeNode
        {
            public string Name => "X";
            public string DisplayText => "X";
            public IReadOnlyList<ICascadeNode> Children => null;
        }

        #endregion CascadeNodeEventArgs
    }
}