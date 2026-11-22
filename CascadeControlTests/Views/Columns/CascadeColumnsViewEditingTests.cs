using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Views.Columns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Columns
{
    /// <summary>
    /// 透過 <see cref="CascadeColumnsView"/> 的公開 API 釘住編輯之後的樹形、選取，以及 <see cref="CascadeColumnsView.TreeEdited"/> 的內容。
    /// </summary>
    /// <remarks>刪除會跳確認框，不在這裡測。用到 Windows 剪貼簿的測試在 STA 執行緒上執行。</remarks>
    [TestClass]
    public class CascadeColumnsViewEditingTests
    {
        #region 插入與建立子節點

        [TestMethod]
        public void InsertAfter_AddsNodeAfterSelected_SelectsIt()
        {
            using (var f = new Fixture())
            {
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginInsert(before: false));
                Assert.IsTrue(f.View.IsEditing);
                f.Commit("X");

                f.AssertShape("A", "A > A1", "A > X", "A > A2", "B", "C");
                Assert.AreSame(f.Node("A > X"), f.View.SelectedNode);
                Assert.IsFalse(f.View.IsEditing);
                f.AssertEdits(CascadeEditAction.Insert, f.Node("A > X"));
                Assert.AreEqual(1, f.View.History.UndoCount);
            }
        }

        [TestMethod]
        public void InsertBefore_AddsNodeBeforeSelected()
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                Assert.IsTrue(f.View.BeginInsert(before: true));
                f.Commit("X");

                f.AssertShape("A", "A > A1", "A > A2", "X", "B", "C");
                Assert.AreSame(f.Node("X"), f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Insert, f.Node("X"));
            }
        }

        [TestMethod]
        public void Insert_TakenName_GetsSuffix()
        {
            using (var f = new Fixture())
            {
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.Commit("  A2  ");

                // 前後空白去掉；同一層已有 A2，所以加後綴。
                f.AssertShape("A", "A > A1", "A > A2 - 2", "A > A2", "B", "C");
                Assert.AreSame(f.Node("A > A2 - 2"), f.View.SelectedNode);
            }
        }

        [TestMethod]
        public void EndEdit_CancelBlankOrSeparator_ChangesNothing()
        {
            using (var f = new Fixture())
            {
                var before = f.Shape();

                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.EditBox().Text = "X";
                f.View.EndEdit(commit: false);

                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.Commit("   ");

                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.Commit("X|Y");

                f.AssertShape(before);
                Assert.IsFalse(f.View.IsEditing);
                Assert.AreSame(f.Node("A > A1"), f.View.SelectedNode);
                Assert.AreEqual(0, f.Edits.Count);
                Assert.AreEqual(0, f.View.History.UndoCount);
            }
        }

        [TestMethod]
        public void CreateChild_OnLeaf_AddsFirstChild_SelectsIt()
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                Assert.IsTrue(f.View.BeginCreateChild());
                f.Commit("B1");

                f.AssertShape("A", "A > A1", "A > A2", "B", "B > B1", "C");
                Assert.AreSame(f.Node("B > B1"), f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.CreateChild, f.Node("B > B1"));
            }
        }

        [TestMethod]
        public void CreateChild_OnBranch_DoesNotOpenEditor()
        {
            using (var f = new Fixture())
            {
                f.Select("A");
                Assert.IsFalse(f.View.BeginCreateChild());
                Assert.IsFalse(f.View.IsEditing);
            }
        }

        #endregion 插入與建立子節點

        #region 改名

        [TestMethod]
        public void Rename_KeepsSameNode_SelectsIt()
        {
            using (var f = new Fixture())
            {
                var a1 = f.Node("A > A1");
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginRename());
                Assert.AreEqual("A1", f.EditBox().Text, "編輯框一開始是原本的名稱");
                f.Commit("Z");

                f.AssertShape("A", "A > Z", "A > A2", "B", "C");
                Assert.AreSame(a1, f.Node("A > Z"), "改名不換節點");
                Assert.AreSame(a1, f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Rename, a1);
            }
        }

        [TestMethod]
        public void Rename_ToSiblingName_GetsSuffix()
        {
            using (var f = new Fixture())
            {
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginRename());
                f.Commit("A2");

                f.AssertShape("A", "A > A2 - 2", "A > A2", "B", "C");
            }
        }

        [TestMethod]
        public void Rename_ToSameName_RaisesNothing()
        {
            using (var f = new Fixture())
            {
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginRename());
                f.Commit("A1");

                Assert.AreSame(f.Node("A > A1"), f.View.SelectedNode);
                Assert.AreEqual(0, f.Edits.Count);
                Assert.AreEqual(0, f.View.History.UndoCount);
            }
        }

        #endregion 改名

        #region 貼上與剪下

        [TestMethod]
        public void PasteAfter_CopiesSubtreeAsNewNodes_WithSuffix() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var a = f.Node("A");
                f.Select("A");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("C");
                Assert.IsTrue(f.View.Paste(before: false));

                f.AssertShape("A", "A > A1", "A > A2", "B", "C", "A - 2", "A - 2 > A1", "A - 2 > A2");
                var pasted = f.Node("A - 2");
                Assert.AreNotSame(a, pasted);
                Assert.AreSame(pasted, f.View.SelectedNode);
                Assert.AreEqual(0, f.View.MultiSelectedNodes.Count, "只貼一個不多選");
                f.AssertEdits(CascadeEditAction.Paste, pasted);
                Assert.AreEqual(1, f.View.History.UndoCount);
            }
        });

        [TestMethod]
        public void PasteBefore_InsertsBeforeSelected() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("A > A1");
                Assert.IsTrue(f.View.Paste(before: true));

                f.AssertShape("A", "A > C", "A > A1", "A > A2", "B", "C");
                Assert.AreSame(f.Node("A > C"), f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Paste, f.Node("A > C"));
            }
        });

        [TestMethod]
        public void PasteAsChild_MultipleNodes_KeepsOrder_MultiSelectsThem() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("A");
                Assert.IsTrue(f.View.SelectAllInColumn(1));
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("B");
                Assert.IsTrue(f.View.PasteAsChild());

                f.AssertShape("A", "A > A1", "A > A2", "B", "B > A1", "B > A2", "C");
                var pasted = new[] { f.Node("B > A1"), f.Node("B > A2") };
                Assert.AreSame(pasted[0], f.View.SelectedNode, "選取第一個貼上的節點");
                CollectionAssert.AreEqual(pasted, f.View.MultiSelectedNodes.ToArray(), "貼了多個時全部多選");
                f.AssertEdits(CascadeEditAction.Paste, pasted);
            }
        });

        [TestMethod]
        public void PasteAsChild_OnBranch_DoesNothing() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var before = f.Shape();
                f.Select("C");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("A");
                Assert.IsFalse(f.View.PasteAsChild());

                f.AssertShape(before);
                Assert.AreEqual(0, f.Edits.Count);
            }
        });

        [TestMethod]
        public void Cut_SelectsNextSibling_AndCanBePastedBack() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var a1 = f.Node("A > A1");
                f.Select("A > A1");
                f.RequireClipboard(f.View.Cut(), "Cut");

                f.AssertShape("A", "A > A2", "B", "C");
                Assert.IsNull(a1.Parent, "剪下的節點離開樹");
                Assert.AreSame(f.Node("A > A2"), f.View.SelectedNode, "選取原位置的下一個兄弟");
                f.AssertEdits(CascadeEditAction.Cut, a1);

                f.Select("C");
                Assert.IsTrue(f.View.Paste(before: false));
                f.AssertShape("A", "A > A2", "B", "C", "A1");
            }
        });

        [TestMethod]
        public void Cut_LastSibling_SelectsPrevious() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.RequireClipboard(f.View.Cut(), "Cut");

                f.AssertShape("A", "A > A1", "A > A2", "B");
                Assert.AreSame(f.Node("B"), f.View.SelectedNode);
            }
        });

        [TestMethod]
        public void Cut_AllChildren_SelectsParent() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var children = new[] { f.Node("A > A1"), f.Node("A > A2") };
                f.Select("A");
                Assert.IsTrue(f.View.SelectAllInColumn(1));
                f.RequireClipboard(f.View.Cut(), "Cut");

                f.AssertShape("A", "B", "C");
                Assert.AreSame(f.Node("A"), f.View.SelectedNode, "一個都不剩時選父節點");
                Assert.AreEqual(0, f.View.MultiSelectedNodes.Count);
                f.AssertEdits(CascadeEditAction.Cut, children);
            }
        });

        #endregion 貼上與剪下

        #region 復原與重做

        [TestMethod]
        public void UndoRedo_Rename_RestoresNameAndSelection()
        {
            using (var f = new Fixture())
            {
                var a1 = f.Node("A > A1");
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginRename());
                f.Commit("Z");
                f.Select("C");

                Assert.IsTrue(f.View.Undo());
                Assert.AreEqual("A1", a1.Name);
                Assert.AreSame(a1, f.View.SelectedNode, "選取跳到那一步的節點");
                f.AssertLastEdit(CascadeEditAction.Undo, a1);

                Assert.IsTrue(f.View.Redo());
                Assert.AreEqual("Z", a1.Name);
                Assert.AreSame(a1, f.View.SelectedNode);
                f.AssertLastEdit(CascadeEditAction.Redo, a1);
            }
        }

        [TestMethod]
        public void UndoRedo_CutOfSeveral_RestoresSameNodes_MultiSelected() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var children = new[] { f.Node("A > A1"), f.Node("A > A2") };
                f.Select("A");
                Assert.IsTrue(f.View.SelectAllInColumn(1));
                f.RequireClipboard(f.View.Cut(), "Cut");

                Assert.IsTrue(f.View.Undo());
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                CollectionAssert.AreEqual(children, f.Node("A").Children.ToArray(), "接回的是同一批節點");
                Assert.AreSame(children[0], f.View.SelectedNode);
                CollectionAssert.AreEqual(children, f.View.MultiSelectedNodes.ToArray(), "回來不只一個時全部多選");
                f.AssertLastEdit(CascadeEditAction.Undo, children);

                Assert.IsTrue(f.View.Redo());
                f.AssertShape("A", "B", "C");
                Assert.AreSame(f.Node("A"), f.View.SelectedNode);
                Assert.AreEqual(0, f.View.MultiSelectedNodes.Count);
                f.AssertLastEdit(CascadeEditAction.Redo, children);
            }
        });

        [TestMethod]
        public void UndoRedo_Paste_RemovesAndReturnsSameNodes() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("A > A1");
                Assert.IsTrue(f.View.Paste(before: false));
                var pasted = f.Node("A > C");

                Assert.IsTrue(f.View.Undo());
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                Assert.IsNull(pasted.Parent);
                Assert.AreSame(f.Node("A > A2"), f.View.SelectedNode, "選取原位置的下一個兄弟");
                f.AssertLastEdit(CascadeEditAction.Undo, pasted);

                Assert.IsTrue(f.View.Redo());
                Assert.AreSame(pasted, f.Node("A > C"));
                Assert.AreSame(pasted, f.View.SelectedNode);
                f.AssertLastEdit(CascadeEditAction.Redo, pasted);
            }
        });

        [TestMethod]
        public void UndoAll_ThenRedoAll_WalksOneTimeline() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                var original = f.Shape();

                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.Commit("X");
                Assert.IsTrue(f.View.BeginRename());
                f.Commit("Y");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.Select("B");
                Assert.IsTrue(f.View.PasteAsChild(), "PasteAsChild");
                f.Select("C");
                f.RequireClipboard(f.View.Cut(), "Cut");

                f.AssertShape("A", "A > A1", "A > Y", "A > A2", "B", "B > Y");
                var edited = f.Shape();
                CollectionAssert.AreEqual(
                    new[] { CascadeEditAction.Insert, CascadeEditAction.Rename, CascadeEditAction.Paste, CascadeEditAction.Cut },
                    f.Edits.Select(e => e.Action).ToArray());
                Assert.AreEqual(4, f.View.History.UndoCount);

                for (var i = 0; i < 4; i++) Assert.IsTrue(f.View.Undo());
                Assert.IsFalse(f.View.CanUndo);
                Assert.IsFalse(f.View.Undo());
                f.AssertShape(original);

                for (var i = 0; i < 4; i++) Assert.IsTrue(f.View.Redo());
                Assert.IsFalse(f.View.CanRedo);
                Assert.IsFalse(f.View.Redo());
                f.AssertShape(edited);
            }
        });

        [TestMethod]
        public void NewEdit_AfterUndo_DropsRedo()
        {
            using (var f = new Fixture())
            {
                f.Select("A > A1");
                Assert.IsTrue(f.View.BeginRename());
                f.Commit("Z");
                Assert.IsTrue(f.View.Undo());
                Assert.IsTrue(f.View.CanRedo);

                Assert.IsTrue(f.View.BeginRename());
                f.Commit("W");
                Assert.IsFalse(f.View.CanRedo);
                f.AssertShape("A", "A > W", "A > A2", "B", "C");
            }
        }

        #endregion 復原與重做

        #region 唯讀

        [TestMethod]
        public void ReadOnlyByDefault_RefusesEveryEdit_ButCopies() => RunOnSta(() =>
        {
            using (var f = new Fixture(readOnly: true))
            {
                var before = f.Shape();
                f.Select("A > A1");

                Assert.IsFalse(f.View.BeginInsert(before: false));
                Assert.IsFalse(f.View.BeginRename());
                f.RequireClipboard(f.View.Copy(), "Copy");   // 唯讀也可以複製
                Assert.IsFalse(f.View.Paste(before: false));
                Assert.IsFalse(f.View.Cut());
                f.Select("B");
                Assert.IsFalse(f.View.BeginCreateChild());
                Assert.IsFalse(f.View.PasteAsChild());

                Assert.IsFalse(f.View.IsEditing);
                f.AssertShape(before);
                Assert.AreEqual(0, f.Edits.Count);
            }
        });

        #endregion 唯讀

        /// <summary>
        /// 在 STA 執行緒上執行 <paramref name="body"/>：Windows 剪貼簿只能在 STA 存取，MTA 上複製會靜靜地失敗。
        /// </summary>
        private static void RunOnSta(Action body)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>
        /// 顯示在螢幕外的 <see cref="CascadeColumnsView"/>，綁著 A &gt; A1、A &gt; A2、B、C 這棵樹，並記下每一次 <see cref="CascadeColumnsView.TreeEdited"/>。
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            private readonly ViewHost<CascadeColumnsView> _host = new ViewHost<CascadeColumnsView>();

            public Fixture(bool readOnly = false)
            {
                Tree.ExpandAndAddPaths("A", "A1|A2");
                Tree.ExpandAndAddPaths("B|C");

                Assert.IsTrue(View.ReadOnly, "預設唯讀");
                View.ReadOnly = readOnly;
                View.SetTree(Tree);
                View.TreeEdited += (sender, e) => Edits.Add(e);
            }

            public MenuTree Tree { get; } = new MenuTree();

            public CascadeColumnsView View => _host.View;

            public List<CascadeTreeEditedEventArgs> Edits { get; } = new List<CascadeTreeEditedEventArgs>();

            /// <summary>樹裡全部節點的 <see cref="MenuNode.FullPathText"/>，依前序（父節點在子節點前、兄弟依順序）排列。</summary>
            public string[] Shape() => Walk(Tree.Root).Select(node => node.FullPathText).ToArray();

            public void AssertShape(params string[] expected) => Assert.AreEqual(string.Join(" / ", expected), string.Join(" / ", Shape()));

            public MenuNode Node(string fullPathText) => Walk(Tree.Root).Single(node => node.FullPathText == fullPathText);

            public void Select(string fullPathText) => Assert.IsTrue(View.TrySelectNode(Node(fullPathText)), fullPathText);

            public TextBox EditBox() =>
                _host.Columns.SelectMany(column => column.Controls.Cast<Control>()).SelectMany(frame => frame.Controls.OfType<TextBox>()).Single();

            /// <summary>在開著的編輯框輸入 <paramref name="text"/>，然後 <see cref="CascadeColumnsView.EndEdit"/>(commit: true)。</summary>
            public void Commit(string text)
            {
                EditBox().Text = text;
                View.EndEdit(commit: true);
            }

            /// <summary>
            /// 寫入剪貼簿的指令（<see cref="CascadeColumnsView.Copy"/>、<see cref="CascadeColumnsView.Cut"/>）要成功，測試才有意義。
            /// 回傳 false 表示剪貼簿被其他程式（例如 Windows 剪貼簿歷程）佔用超過重試時間，這時記為 Inconclusive，不算失敗。
            /// </summary>
            public void RequireClipboard(bool succeeded, string command)
            {
                if (!succeeded) Assert.Inconclusive($"{command} 回傳 false：剪貼簿被其他程式佔用。");
            }

            /// <summary>到目前為止只觸發過一次 <see cref="CascadeColumnsView.TreeEdited"/>，內容如指定。</summary>
            public void AssertEdits(CascadeEditAction action, params MenuNode[] nodes)
            {
                Assert.AreEqual(1, Edits.Count);
                AssertLastEdit(action, nodes);
            }

            public void AssertLastEdit(CascadeEditAction action, params MenuNode[] nodes)
            {
                var last = Edits.Last();
                Assert.AreEqual(action, last.Action);
                CollectionAssert.AreEqual(nodes, last.Nodes.ToArray());
            }

            public void Dispose() => _host.Dispose();

            private static IEnumerable<MenuNode> Walk(MenuNode node) =>
                node.Children.SelectMany(child => new[] { child }.Concat(Walk(child)));
        }
    }
}