using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Views.Columns;
using CascadeControl.Views.Diagram;
using CascadeControlTests.Views.Columns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using static CascadeControlTests.Views.Columns.CascadeColumnsViewEditingTests;

namespace CascadeControlTests.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView"/> 的編輯：快捷鍵與公開指令改的是共享的 <see cref="CascadeState"/>，綁同一份 State 的 <see cref="CascadeColumnsView"/> 跟著更新、共用復原歷史。
    /// </summary>
    /// <remarks>刪除會跳確認框，不在這裡測（State 層測過）。用到 Windows 剪貼簿的測試在 STA 執行緒上執行。</remarks>
    [TestClass]
    public class CascadeDiagramViewEditingTests
    {
        [TestMethod]
        public void ReadOnly_DefaultTrue_TreeChangingKeysPassThrough_CopyStillHandled() => RunOnSta(() =>
        {
            using (var f = new Fixture(readOnly: null))
            {
                Assert.IsTrue(f.View.ReadOnly, "預設唯讀");
                f.Select("B");
                var before = f.Shape();

                foreach (var keys in new[] { Keys.Control | Keys.X, Keys.Control | Keys.V, Keys.Control | Keys.Alt | Keys.V, Keys.Delete })
                {
                    Assert.IsFalse(f.Key(keys), $"唯讀時不攔 {keys}");
                }

                Assert.IsFalse(f.View.Cut());
                Assert.IsFalse(f.View.Delete(), "唯讀時不跳確認框，直接回傳 false");
                CollectionAssert.AreEqual(before, f.Shape());
                Assert.AreEqual(0, f.Edits.Count);

                Assert.IsTrue(f.Key(Keys.Control | Keys.C), "唯讀時也能複製");
                Assert.IsTrue(f.Key(Keys.Control | Keys.Shift | Keys.C));
            }
        });

        [TestMethod]
        public void InsertAndColumnsOnlyKeys_AreNotHandled()
        {
            using (var f = new Fixture())
            {
                f.Select("B");

                // 插入、建立子節點還沒做；Esc、Ctrl+A、Ctrl+Space 是 Columns 的多選與勾選。
                foreach (var keys in new[] { Keys.Control | Keys.I, Keys.Control | Keys.Shift | Keys.I, Keys.Control | Keys.N, Keys.Escape, Keys.Control | Keys.A, Keys.Control | Keys.Space })
                {
                    Assert.IsFalse(f.Key(keys), keys.ToString());
                }

                Assert.AreEqual(0, f.Edits.Count);
            }
        }

        [TestMethod]
        public void CopyPasteKeys_PasteAfterAndBefore_SelectPasted() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("A");
                Assert.IsTrue(f.Key(Keys.Control | Keys.C));
                f.RequireClipboard(f.View.Copy(), "Copy");

                f.Select("C");
                Assert.IsTrue(f.Key(Keys.Control | Keys.V));
                f.AssertShape("A", "A > A1", "A > A2", "B", "C", "A - 2", "A - 2 > A1", "A - 2 > A2");
                Assert.AreSame(f.Node("A - 2"), f.View.SelectedNode);
                f.AssertLastEdit(CascadeEditAction.Paste, f.Node("A - 2"));

                f.Select("B");
                Assert.IsTrue(f.Key(Keys.Control | Keys.Shift | Keys.V));
                f.AssertShape("A", "A > A1", "A > A2", "A - 3", "A - 3 > A1", "A - 3 > A2", "B", "C", "A - 2", "A - 2 > A1", "A - 2 > A2");
                Assert.AreSame(f.Node("A - 3"), f.View.SelectedNode);
                Assert.AreEqual(2, f.View.State.History.UndoCount);
            }
        });

        [TestMethod]
        public void PasteAsChild_OnlyIntoLeaf() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.RequireClipboard(f.View.Copy(), "Copy");

                f.Select("A");
                Assert.IsTrue(f.Key(Keys.Control | Keys.Alt | Keys.V), "按鍵照樣攔下");
                Assert.AreEqual(0, f.Edits.Count, "A 已經有子節點，不貼");

                f.Select("B");
                Assert.IsTrue(f.View.PasteAsChild());
                f.AssertShape("A", "A > A1", "A > A2", "B", "B > C", "C");
                Assert.AreSame(f.Node("B > C"), f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Paste, f.Node("B > C"));
            }
        });

        [TestMethod]
        public void Paste_NoSelection_AppendsToFirstLevel() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("B");
                f.RequireClipboard(f.View.Copy(), "Copy");
                f.View.State.ClearSelection();

                Assert.IsTrue(f.View.Paste(before: false));
                f.AssertShape("A", "A > A1", "A > A2", "B", "C", "B - 2");
            }
        });

        [TestMethod]
        public void CutInDiagram_UpdatesColumns_UndoInColumnsRestores() => RunOnSta(() =>
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(state: host.View.State))
            {
                host.View.ReadOnly = false;
                var a = f.Node("A");
                f.Select("A");

                Assert.IsTrue(f.Key(Keys.Control | Keys.X));
                f.RequireClipboard(f.Edits.Count == 1, "Cut");
                f.AssertShape("B", "C");
                f.AssertEdits(CascadeEditAction.Cut, a);
                Assert.AreSame(f.Node("B"), host.View.SelectedNode, "Columns 也選取下一個兄弟");
                CollectionAssert.AreEqual(new[] { "B", "C" }, host.Columns[0].Items.Cast<ICascadeNode>().Select(node => node.Name).ToArray(), "Columns 第一欄重建");

                Assert.IsTrue(host.View.Undo(), "Columns 復原 Diagram 的剪下");
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                Assert.AreSame(a, f.Node("A"), "回來的是同一個節點");
                Assert.AreSame(a, f.View.SelectedNode);
                f.AssertLastEdit(CascadeEditAction.Undo, a);

                Assert.IsTrue(f.Key(Keys.Control | Keys.Y), "Diagram 重做");
                f.AssertShape("B", "C");
                f.AssertLastEdit(CascadeEditAction.Redo, a);
            }
        });

        [TestMethod]
        public void CanUndo_ReadOnlyBlocksTreeStepsOnly() => RunOnSta(() =>
        {
            using (var f = new Fixture())
            {
                f.Select("C");
                f.RequireClipboard(f.View.Cut(), "Cut");
                Assert.IsTrue(f.View.CanUndo);

                f.View.ReadOnly = true;
                Assert.IsFalse(f.View.CanUndo, "那一步改了樹，唯讀時不能復原");
                Assert.IsTrue(f.Key(Keys.Control | Keys.Z), "復原鍵不改樹，唯讀時照樣攔");
                f.AssertShape("A", "A > A1", "A > A2", "B");

                // 勾選不改樹，唯讀時也能復原。
                ((CascadeCheckableState)f.View.State).CheckSet.Check(f.Node("B"));
                Assert.IsTrue(f.View.CanUndo);
                Assert.IsTrue(f.View.Undo());
                Assert.IsFalse(((CascadeCheckableState)f.View.State).CheckSet.IsChecked(f.Node("B")));
                Assert.IsFalse(f.View.CanUndo);
            }
        });

        [TestMethod]
        public void F2_OpensBoxOverSelectedNode_CommitRenamesThroughState_ColumnsFollow()
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(state: host.View.State))
            {
                var a1 = f.Node("A > A1");
                f.Select("A > A1");

                Assert.IsTrue(f.Key(Keys.F2));
                Assert.IsTrue(f.View.IsEditing);
                var box = f.EditBox();
                Assert.AreEqual("A1", box.Text, "編輯框一開始是原本的名稱");
                Assert.AreSame(a1, f.View.GetNodeAt(new System.Drawing.Point(box.Left + 2, box.Top + (box.Height / 2))), "編輯框蓋在節點方塊上");

                box.Text = "Z";
                f.View.EndEdit(commit: true);

                Assert.IsFalse(f.View.IsEditing);
                Assert.AreEqual(0, f.View.Controls.OfType<TextBox>().Count(), "編輯框已移除");
                f.AssertShape("A", "A > Z", "A > A2", "B", "C");
                Assert.AreSame(a1, f.Node("A > Z"), "改名不換節點");
                Assert.AreSame(a1, f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Rename, a1);
                Assert.AreEqual(1, f.View.State.History.UndoCount);
                CollectionAssert.AreEqual(new[] { "Z", "A2" }, host.Columns[1].Items.Cast<ICascadeNode>().Select(node => node.Name).ToArray(), "Columns 第二欄跟著改名");
            }
        }

        [TestMethod]
        public void EndEdit_CancelBlankSeparatorOrSameName_ChangesNothing()
        {
            using (var f = new Fixture())
            {
                var before = f.Shape();
                f.Select("B");

                foreach (var text in new[] { "X", "   ", "X|Y", "B" })
                {
                    Assert.IsTrue(f.View.BeginRename(), text);
                    f.EditBox().Text = text;
                    f.View.EndEdit(commit: text != "X");
                    Assert.IsFalse(f.View.IsEditing, text);
                }

                f.AssertShape(before);
                Assert.AreSame(f.Node("B"), f.View.SelectedNode);
                Assert.AreEqual(0, f.Edits.Count);
                Assert.AreEqual(0, f.View.State.History.UndoCount);
            }
        }

        [TestMethod]
        public void ReadOnly_RefusesRename_SwitchingToReadOnlyClosesBox()
        {
            using (var f = new Fixture(readOnly: true))
            {
                f.Select("B");
                Assert.IsFalse(f.Key(Keys.F2), "唯讀時不攔 F2");
                Assert.IsFalse(f.View.BeginRename());
                Assert.IsFalse(f.View.IsEditing);

                f.View.ReadOnly = false;
                Assert.IsTrue(f.View.BeginRename());
                f.EditBox().Text = "X";

                f.View.ReadOnly = true;
                Assert.IsFalse(f.View.IsEditing);
                Assert.AreEqual(0, f.View.Controls.OfType<TextBox>().Count());
                Assert.AreEqual(0, f.Edits.Count, "關掉時不套用");
            }
        }

        [TestMethod]
        public void SelectionChange_KeepsBox_RealignsOverNode_ClosesWhenNodeNoLongerDrawn()
        {
            using (var f = new Fixture())
            {
                var c = f.Node("C");
                f.Select("C");
                Assert.IsTrue(f.View.BeginRename());
                var top = f.EditBox().Top;

                // 選 A1 會展開 A，C 往下移；編輯框跟著移，對象還是 C。
                f.Select("A > A1");
                Assert.IsTrue(f.View.IsEditing);
                var box = f.EditBox();
                Assert.IsTrue(box.Top > top, "跟著節點往下移");
                Assert.AreSame(c, f.View.GetNodeAt(new System.Drawing.Point(box.Left + 2, box.Top + (box.Height / 2))));

                // 只畫選取的子樹時 C 不在圖上，編輯框直接關掉。
                f.View.DrawScope = TreeDrawScope.SelectedSubtree;
                Assert.IsFalse(f.View.IsEditing);
                Assert.AreEqual(0, f.Edits.Count);
            }
        }

        [TestMethod]
        public void TreeEditedByAnyView_ClosesBothBoxes_WithoutApplying() => RunOnSta(() =>
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(state: host.View.State))
            {
                host.View.ReadOnly = false;

                // Diagram 開著編輯框時，Columns 改了樹。
                f.Select("B");
                Assert.IsTrue(f.View.BeginRename());
                f.EditBox().Text = "X";
                host.View.TrySelectNode(f.Node("C"));
                Assert.IsTrue(f.View.IsEditing, "只是選取改變，編輯框留著");
                f.RequireClipboard(host.View.Cut(), "Columns Cut");
                Assert.IsFalse(f.View.IsEditing);
                Assert.AreEqual(0, f.View.Controls.OfType<TextBox>().Count());
                f.AssertShape("A", "A > A1", "A > A2", "B");

                // Columns 開著編輯框時，Diagram 改了樹（剪下選取的 B，也就是 Columns 正在改名的節點）。
                host.View.TrySelectNode(f.Node("B"));
                Assert.IsTrue(host.View.BeginRename());
                Assert.IsTrue(f.Key(Keys.Control | Keys.X));
                f.RequireClipboard(f.Edits.Count == 2, "Diagram Cut");
                Assert.IsFalse(host.View.IsEditing);
                f.AssertShape("A", "A > A1", "A > A2");
                Assert.AreEqual(CascadeEditAction.Cut, f.Edits.Last().Action);
                Assert.IsFalse(f.Edits.Any(e => e.Action == CascadeEditAction.Rename), "兩個編輯框都沒有套用");
            }
        });

        [TestMethod]
        public void ContextMenu_EditItemsFollowReadOnly_ShortcutTextFromTable()
        {
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000), ClientSize = new System.Drawing.Size(300, 200), ShowInTaskbar = false })
            using (var f = new Fixture())
            {
                f.View.Dock = DockStyle.Fill;
                form.Controls.Add(f.View);
                form.Show();
                f.Select("B");

                string[] OpenMenu()
                {
                    var menu = f.View.TreeContextMenu;
                    menu.Show(f.View, new System.Drawing.Point(10, 10));
                    var texts = menu.Items.Cast<ToolStripItem>()
                        .Select(item => item is ToolStripSeparator ? "-" : item.Text + (item is ToolStripMenuItem m && m.ShortcutKeyDisplayString != null ? $"({m.ShortcutKeyDisplayString})" : string.Empty))
                        .ToArray();
                    menu.Close();
                    return texts;
                }

                CollectionAssert.AreEqual(
                    new[] { "復原(Ctrl+Z)", "重做(Ctrl+Y)", "-", "重新命名(F2)", "-", "複製(Ctrl+C)", "複製(路徑)(Ctrl+Shift+C)", "剪下(Ctrl+X)", "貼上", "-", "刪除(Del)", "-", "範圍", "顯示" },
                    OpenMenu());

                f.View.ReadOnly = true;
                CollectionAssert.AreEqual(new[] { "複製(Ctrl+C)", "複製(路徑)(Ctrl+Shift+C)", "-", "範圍", "顯示" }, OpenMenu());

                f.View.State.ClearSelection();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, OpenMenu(), "沒有對象時只有顯示設定");
            }
        }

        /// <summary>
        /// 綁著 A &gt; A1、A &gt; A2、B、C 這棵樹的 <see cref="CascadeDiagramView"/>，並記下每一次 <see cref="CascadeState.TreeEdited"/>。
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            /// <param name="readOnly">null 表示不改預設值。</param>
            /// <param name="state">要共用的 State；null 表示用控制項自己的。</param>
            public Fixture(bool? readOnly = false, CascadeState state = null)
            {
                Tree.ExpandAndAddPaths("A", "A1|A2");
                Tree.ExpandAndAddPaths("B|C");

                if (state != null) View.State = state;
                if (readOnly.HasValue) View.ReadOnly = readOnly.Value;
                View.SetTree(Tree);
                View.State.TreeEdited += (sender, e) => Edits.Add(e);
            }

            public MenuTree Tree { get; } = new MenuTree();

            public CascadeDiagramView View { get; } = new CascadeDiagramView();

            public List<CascadeTreeEditedEventArgs> Edits { get; } = new List<CascadeTreeEditedEventArgs>();

            public bool Key(Keys keys) => ViewHost.SendCmdKey(View, keys);

            public TextBox EditBox() => View.Controls.OfType<TextBox>().Single();

            public string[] Shape() => Walk(Tree.Root).Select(node => node.FullPathText).ToArray();

            public void AssertShape(params string[] expected) => Assert.AreEqual(string.Join(" / ", expected), string.Join(" / ", Shape()));

            public MenuNode Node(string fullPathText) => Walk(Tree.Root).Single(node => node.FullPathText == fullPathText);

            public void Select(string fullPathText) => Assert.IsTrue(View.TrySelectNode(Node(fullPathText)), fullPathText);

            /// <summary>寫入剪貼簿失敗表示被其他程式佔用，記為 Inconclusive。</summary>
            public void RequireClipboard(bool succeeded, string command)
            {
                if (!succeeded) Assert.Inconclusive($"{command} 回傳 false：剪貼簿被其他程式佔用。");
            }

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

            public void Dispose() => View.Dispose();

            private static IEnumerable<MenuNode> Walk(MenuNode node) =>
                node.Children.SelectMany(child => new[] { child }.Concat(Walk(child)));
        }
    }
}
