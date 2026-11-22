using CascadeControl.State;
using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.State
{
    /// <summary>
    /// <see cref="CascadeState"/> 的編輯方法：改完的樹形、選取、<see cref="CascadeState.TreeEdited"/> 與 <see cref="CascadeState.History"/>。不需要 WinForms。
    /// </summary>
    [TestClass]
    public class CascadeStateEditingTests
    {
        [TestMethod]
        public void InsertNode_UsesUniqueName_SelectsIt_AndCanBeUndone()
        {
            var f = new Fixture();

            var inserted = f.State.InsertNode(f.Tree.Root, 1, "B");

            Assert.AreEqual("B - 2", inserted.Name);
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B - 2", "B", "C" }, f.Paths());
            Assert.AreSame(inserted, f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Insert, inserted);
            Assert.AreEqual(1, f.State.History.UndoCount);

            Assert.IsNotNull(f.State.Undo());
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B", "C" }, f.Paths());
        }

        [TestMethod]
        public void EditMethods_TargetOutsideTree_DoNothing()
        {
            var f = new Fixture();
            var stranger = new MenuTree().AddPath(new[] { "X" });

            Assert.IsNull(f.State.InsertNode(stranger, 0, "N"));
            Assert.IsNull(f.State.CreateChild(stranger, "N"));
            Assert.IsNull(f.State.RenameNode(stranger, "N"));
            Assert.AreEqual(0, f.State.PasteNodes(stranger, 0, new[] { f.Node("B") }).Count);
            Assert.IsFalse(f.State.DeleteNodes(new[] { stranger }));
            Assert.IsFalse(f.State.CutNodes(new[] { stranger }));

            Assert.AreEqual(0, f.Edits.Count);
            Assert.AreEqual(0, f.State.History.UndoCount);
        }

        [TestMethod]
        public void EditMethods_WithoutTree_DoNothing()
        {
            var f = new Fixture();
            var b = f.Node("B");
            f.State.SetTopLevelNodes(f.Tree.TopLevelNodes);

            Assert.IsNull(f.State.InsertNode(f.Tree.Root, 0, "N"));
            Assert.IsNull(f.State.RenameNode(b, "N"));
            Assert.IsFalse(f.State.DeleteNodes(new[] { b }));
            Assert.AreEqual(0, f.Edits.Count);
        }

        [TestMethod]
        public void CreateChild_OnlyOnLeaf()
        {
            var f = new Fixture();

            Assert.IsNull(f.State.CreateChild(f.Node("A"), "N"), "已經有子節點");

            var child = f.State.CreateChild(f.Node("B"), "N");
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B", "B/N", "C" }, f.Paths());
            Assert.AreSame(child, f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.CreateChild, child);
        }

        [TestMethod]
        public void RenameNode_SameName_IsNoEdit_ClashingName_GetsSuffix()
        {
            var f = new Fixture();
            var b = f.Node("B");

            Assert.IsNull(f.State.RenameNode(b, "B"));
            Assert.AreEqual(0, f.Edits.Count);
            Assert.AreEqual(0, f.State.History.UndoCount);

            var renamed = f.State.RenameNode(b, "C");
            Assert.AreSame(b, renamed);
            Assert.AreEqual("C - 2", renamed.Name);
            Assert.AreSame(renamed, f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Rename, renamed);

            Assert.IsNotNull(f.State.Undo());
            Assert.AreEqual("B", b.Name);
        }

        [TestMethod]
        public void PasteNodes_CopiesSubtreesInOrder_SelectsFirst()
        {
            var f = new Fixture();
            var a = f.Node("A");
            var c = f.Node("C");

            var pasted = f.State.PasteNodes(f.Tree.Root, 3, new[] { a, c });

            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B", "C", "A - 2", "A - 2/A1", "A - 2/A2", "C - 2" }, f.Paths());
            Assert.AreEqual(2, pasted.Count);
            Assert.AreNotSame(a, pasted[0], "貼上的是複本");
            Assert.AreSame(pasted[0], f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Paste, pasted.ToArray());
            Assert.AreEqual(1, f.State.History.UndoCount, "貼上多個算一步");
        }

        [TestMethod]
        public void DeleteNodes_SelectsNextSibling_ThenPrevious_ThenParent()
        {
            var f = new Fixture();
            var a1 = f.Node("A/A1");
            var a2 = f.Node("A/A2");

            Assert.IsTrue(f.State.DeleteNodes(new[] { a1 }));
            Assert.AreSame(a2, f.State.SelectedNode, "下一個兄弟");
            f.AssertSingleEdit(CascadeEditAction.Delete, a1);

            f.Edits.Clear();
            Assert.IsTrue(f.State.DeleteNodes(new[] { a2 }));
            Assert.AreSame(f.Node("A"), f.State.SelectedNode, "一個都不剩時選父節點");

            Assert.IsTrue(f.State.DeleteNodes(new[] { f.Node("C") }));
            Assert.AreSame(f.Node("B"), f.State.SelectedNode, "沒有下一個時選前一個");
            CollectionAssert.AreEqual(new[] { "A", "B" }, f.Paths());
        }

        [TestMethod]
        public void DeleteNodes_FromDifferentParents_ReturnsFalse()
        {
            var f = new Fixture();

            Assert.IsFalse(f.State.DeleteNodes(new[] { f.Node("A/A1"), f.Node("B") }));
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B", "C" }, f.Paths());
            Assert.AreEqual(0, f.Edits.Count);
        }

        [TestMethod]
        public void CutNodes_RemovesLikeDelete_WithCutAction()
        {
            var f = new Fixture();
            var b = f.Node("B");
            var c = f.Node("C");

            Assert.IsTrue(f.State.CutNodes(new[] { b, c }));

            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2" }, f.Paths());
            Assert.AreSame(f.Node("A"), f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Cut, b, c);
        }

        [TestMethod]
        public void DeleteNodes_UnchecksRemovedSubtree_InTheSameUndoStep()
        {
            var f = new Fixture(checkable: true);
            var checkSet = ((CascadeCheckableState)f.State).CheckSet;
            var a = f.Node("A");
            var a1 = f.Node("A/A1");
            checkSet.Check(a1);
            checkSet.Check(f.Node("B"));
            var stepsBefore = f.State.History.UndoCount;

            Assert.IsTrue(f.State.DeleteNodes(new[] { a }));

            Assert.IsFalse(checkSet.IsChecked(a1), "被刪的子孫也取消勾選");
            Assert.IsTrue(checkSet.IsChecked(f.Node("B")), "其他節點不受影響");
            Assert.AreEqual(stepsBefore + 1, f.State.History.UndoCount, "刪除與取消勾選算同一步");

            Assert.IsNotNull(f.State.Undo());
            Assert.AreSame(a, f.Node("A"));
            Assert.IsTrue(checkSet.IsChecked(a1), "復原時勾選一起回來");
        }

        [TestMethod]
        public void UndoRedo_TreeStep_RestoresNodes_SelectsFocus_RaisesEvent()
        {
            var f = new Fixture();
            var b = f.Node("B");
            var c = f.Node("C");
            f.State.DeleteNodes(new[] { b, c });
            f.Edits.Clear();

            var undo = f.State.Undo();

            Assert.IsTrue(undo.TreeChanged);
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2", "B", "C" }, f.Paths());
            Assert.AreSame(undo.Focus, f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Undo, b, c);

            f.Edits.Clear();
            var redo = f.State.Redo();

            Assert.IsTrue(redo.TreeChanged);
            CollectionAssert.AreEqual(new[] { "A", "A/A1", "A/A2" }, f.Paths());
            Assert.AreSame(redo.Focus, f.State.SelectedNode);
            f.AssertSingleEdit(CascadeEditAction.Redo, b, c);
        }

        [TestMethod]
        public void Undo_CheckOnlyStep_SelectsTarget_WithoutTreeEdited()
        {
            var f = new Fixture(checkable: true);
            var checkSet = ((CascadeCheckableState)f.State).CheckSet;
            var a2 = f.Node("A/A2");
            checkSet.Check(a2);

            var result = f.State.Undo();

            Assert.IsFalse(result.TreeChanged);
            Assert.IsFalse(checkSet.IsChecked(a2));
            Assert.AreSame(a2, f.State.SelectedNode);
            Assert.AreEqual(0, f.Edits.Count);
        }

        [TestMethod]
        public void UndoRedo_NothingToReplay_ReturnsNull()
        {
            var f = new Fixture();

            Assert.IsNull(f.State.Undo());
            Assert.IsNull(f.State.Redo());
            Assert.AreEqual(0, f.Edits.Count);
        }

        [TestMethod]
        public void TreeEdited_RaisedAfterSelectionUpdated()
        {
            var f = new Fixture();
            var order = new List<string>();
            f.State.SelectedPathChanged += (sender, e) => order.Add("SelectedPathChanged");
            f.State.TreeEdited += (sender, e) => order.Add("TreeEdited:" + f.State.SelectedPathText);

            f.State.InsertNode(f.Tree.Root, 0, "N");

            CollectionAssert.AreEqual(new[] { "SelectedPathChanged", "TreeEdited:N" }, order);
        }

        /// <summary>
        /// 綁著 A &gt; A1、A &gt; A2、B、C 這棵樹的 <see cref="CascadeState"/>，並記下每一次 <see cref="CascadeState.TreeEdited"/>。
        /// </summary>
        private sealed class Fixture
        {
            public Fixture(bool checkable = false)
            {
                Tree = new MenuTree();
                Tree.AddPath(new[] { "A", "A1" });
                Tree.AddPath(new[] { "A", "A2" });
                Tree.AddPath(new[] { "B" });
                Tree.AddPath(new[] { "C" });

                State = checkable ? new CascadeCheckableState() : new CascadeState();
                State.SetTree(Tree);
                State.TreeEdited += (sender, e) => Edits.Add(e);
            }

            public MenuTree Tree { get; }

            public CascadeState State { get; }

            public List<CascadeTreeEditedEventArgs> Edits { get; } = new List<CascadeTreeEditedEventArgs>();

            /// <summary>以 / 分隔的路徑找節點。</summary>
            public MenuNode Node(string path)
            {
                var node = Tree.Root;
                foreach (var name in path.Split('/')) node = node.Children.Single(child => child.Name == name);
                return node;
            }

            /// <summary>樹裡全部節點的路徑，以 / 分隔，依前序排列。</summary>
            public string[] Paths()
            {
                var paths = new List<string>();
                void Walk(MenuNode node, string prefix)
                {
                    foreach (var child in node.Children)
                    {
                        var path = prefix + child.Name;
                        paths.Add(path);
                        Walk(child, path + "/");
                    }
                }

                Walk(Tree.Root, string.Empty);
                return paths.ToArray();
            }

            public void AssertSingleEdit(CascadeEditAction action, params MenuNode[] nodes)
            {
                Assert.AreEqual(1, Edits.Count, "TreeEdited 次數");
                Assert.AreEqual(action, Edits[0].Action);
                CollectionAssert.AreEqual(nodes, Edits[0].Nodes.ToArray());
            }
        }
    }
}
