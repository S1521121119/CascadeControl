using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControl.Views;
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
        public void ColumnsOnlyKeys_AreNotHandled()
        {
            using (var f = new Fixture())
            {
                f.Select("B");

                // Esc、Ctrl+A、Ctrl+Space 是 Columns 的多選與勾選。
                foreach (var keys in new[] { Keys.Escape, Keys.Control | Keys.A, Keys.Control | Keys.Space })
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
        public void EditCommandsThroughICascadeView_WorkOnEitherView_ShareOneHistory()
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(readOnly: null, state: host.View.State))
            {
                // 工具列只認得介面：哪個檢視有焦點就呼叫哪一個。
                ICascadeView diagram = f.View;
                ICascadeView columns = host.View;
                diagram.ReadOnly = false;
                columns.ReadOnly = false;
                f.Select("B");

                Assert.IsTrue(diagram.BeginInsert(before: false));
                Assert.IsTrue(diagram.IsEditing);
                f.EditBox().Text = "N";
                diagram.EndEdit(commit: true);
                Assert.IsFalse(diagram.IsEditing);
                f.AssertShape("A", "A > A1", "A > A2", "B", "N", "C");
                Assert.AreSame(f.Node("N"), columns.SelectedNode, "Columns 跟著選取新節點");

                Assert.IsTrue(columns.CanUndo, "兩個檢視共用同一份歷史");
                Assert.IsTrue(columns.Undo());
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                Assert.IsTrue(diagram.CanRedo);
                Assert.IsTrue(diagram.Redo());
                f.AssertShape("A", "A > A1", "A > A2", "B", "N", "C");

                columns.ReadOnly = true;
                Assert.IsFalse(columns.CanUndo, "唯讀只影響自己");
                Assert.IsTrue(diagram.CanUndo);
            }
        }

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
                f.View.DrawScope = CascadeDiagramDrawScope.SelectedSubtree;
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
        public void InsertKeys_OpenBoxOverPlaceholder_CommitInsertsThroughState_ColumnsFollow()
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(state: host.View.State))
            {
                var b = f.Node("B");
                var c = f.Node("C");
                f.Select("B");

                Assert.IsTrue(f.Key(Keys.Control | Keys.I));
                Assert.IsTrue(f.View.IsEditing);
                var box = f.EditBox();
                Assert.AreEqual(string.Empty, box.Text, "新節點的編輯框一開始是空的");
                var x = box.Left + 2;
                Assert.IsTrue(f.Bounds(b).Bottom <= box.Top && box.Bottom <= f.Bounds(c).Top, "空白方塊畫在 B 與 C 之間");
                Assert.IsNull(f.View.GetNodeAt(new System.Drawing.Point(x, box.Top + (box.Height / 2))), "點到空白方塊不算點到節點");
                f.AssertShape("A", "A > A1", "A > A2", "B", "C");
                Assert.AreEqual(0, f.Edits.Count, "確認之前不改樹");
                Assert.AreEqual(0, f.View.State.History.UndoCount);

                box.Text = "N";
                f.View.EndEdit(commit: true);

                Assert.IsFalse(f.View.IsEditing);
                f.AssertShape("A", "A > A1", "A > A2", "B", "N", "C");
                var n = f.Node("N");
                Assert.AreSame(n, f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.Insert, n);
                Assert.AreEqual(1, f.View.State.History.UndoCount);
                CollectionAssert.AreEqual(new[] { "A", "B", "N", "C" }, host.Columns[0].Items.Cast<ICascadeNode>().Select(node => node.Name).ToArray(), "Columns 第一欄跟著更新");

                // Ctrl+Shift+I 插在前面。
                Assert.IsTrue(f.Key(Keys.Control | Keys.Shift | Keys.I));
                f.EditBox().Text = "M";
                f.View.EndEdit(commit: true);
                f.AssertShape("A", "A > A1", "A > A2", "B", "M", "N", "C");
                f.AssertLastEdit(CascadeEditAction.Insert, f.Node("M"));
            }
        }

        [TestMethod]
        public void CreateChildKey_OnlyForLeaf_PlaceholderUnderLeaf_CommitCreatesFirstChild()
        {
            using (var f = new Fixture())
            {
                f.Select("A");
                Assert.IsTrue(f.Key(Keys.Control | Keys.N), "會攔，但 A 已經有子節點");
                Assert.IsFalse(f.View.IsEditing);

                var b = f.Node("B");
                f.Select("B");
                Assert.IsTrue(f.Key(Keys.Control | Keys.N));
                Assert.IsTrue(f.View.IsEditing);
                var box = f.EditBox();
                Assert.IsTrue(box.Left > f.Bounds(b).Right, "空白方塊在 B 的下一欄");

                box.Text = "B1";
                f.View.EndEdit(commit: true);

                f.AssertShape("A", "A > A1", "A > A2", "B", "B > B1", "C");
                var b1 = f.Node("B > B1");
                Assert.AreSame(b1, f.View.SelectedNode);
                f.AssertEdits(CascadeEditAction.CreateChild, b1);
            }
        }

        [TestMethod]
        public void CancelInsertOrCreateChild_TreeHistoryAndPlaceholderUnchanged()
        {
            using (var f = new Fixture())
            {
                // 先留一步可以重做的歷史：取消插入之後它還要在。
                f.Select("B");
                Assert.IsTrue(f.View.BeginRename());
                f.EditBox().Text = "X";
                f.View.EndEdit(commit: true);
                Assert.IsTrue(f.View.Undo());
                var before = f.Shape();
                var edits = f.Edits.Count;

                var c = f.Node("C");
                var cBounds = f.Bounds(c);
                foreach (var begin in new Func<bool>[] { () => f.View.BeginInsert(before: false), () => f.View.BeginInsert(before: true), f.View.BeginCreateChild })
                {
                    foreach (var text in new[] { "N", "   ", "N|M" })
                    {
                        f.Select("B");
                        Assert.IsTrue(begin(), text);
                        var box = f.EditBox();
                        box.Text = text;
                        f.View.EndEdit(commit: text != "N");

                        Assert.IsFalse(f.View.IsEditing, text);
                        Assert.AreEqual(0, f.View.Controls.OfType<TextBox>().Count(), text);
                        f.AssertShape(before);
                        Assert.AreEqual(edits, f.Edits.Count, text);
                        Assert.AreEqual(0, f.View.State.History.UndoCount, text);
                        Assert.AreEqual(1, f.View.State.History.RedoCount, "取消不會清掉重做");
                        Assert.AreEqual(cBounds, f.Bounds(c), "空白方塊已拿掉，C 在原位");
                    }
                }

                Assert.IsTrue(f.View.Redo(), "原本的重做照樣能用");
                f.AssertShape("A", "A > A1", "A > A2", "X", "C");
            }
        }

        [TestMethod]
        public void Insert_RefusedWhenReadOnlyOrSelectedSubtree_TopLevelOfSingleRootTree()
        {
            using (var f = new Fixture(readOnly: true))
            {
                f.Select("B");
                foreach (var keys in new[] { Keys.Control | Keys.I, Keys.Control | Keys.Shift | Keys.I, Keys.Control | Keys.N })
                {
                    Assert.IsFalse(f.Key(keys), "唯讀時不攔 " + keys);
                }

                f.View.ReadOnly = false;
                f.View.DrawScope = CascadeDiagramDrawScope.SelectedSubtree;
                Assert.IsFalse(f.View.BeginInsert(before: false), "只畫選取的子樹時，起點旁邊沒有位置");
                Assert.IsFalse(f.View.IsEditing);
                Assert.IsTrue(f.View.BeginCreateChild(), "子節點畫得出來");
                f.View.EndEdit(commit: false);
            }

            using (var view = new CascadeDiagramView { ReadOnly = false })
            {
                var tree = new MenuTree();
                tree.ExpandAndAddPaths("A", "A1");
                view.SetTree(tree);
                Assert.IsTrue(view.TrySelectNode(tree.TopLevelNodes[0]));

                Assert.IsTrue(view.BeginInsert(before: false));
                view.Controls.OfType<TextBox>().Single().Text = "B";
                view.EndEdit(commit: true);

                CollectionAssert.AreEqual(new[] { "A", "B" }, tree.TopLevelNodes.Select(node => node.Name).ToArray());
            }
        }

        [TestMethod]
        public void TreeEditedWhileInserting_ClosesBoxAndRemovesPlaceholder_WithoutApplying()
        {
            using (var host = new ViewHost<CascadeColumnsView>())
            using (var f = new Fixture(state: host.View.State))
            {
                host.View.ReadOnly = false;
                var c = f.Node("C");
                var cBounds = f.Bounds(c);

                f.Select("B");
                Assert.IsTrue(f.View.BeginInsert(before: false));
                f.EditBox().Text = "N";
                Assert.AreNotEqual(cBounds, f.Bounds(c), "空白方塊把 C 往下推");

                // 另一個檢視改了樹：Diagram 的編輯框關掉、不套用，空白方塊拿掉。
                Assert.IsTrue(host.View.BeginRename());
                Assert.IsNotNull(host.View.State.RenameNode(f.Node("A"), "Z"));
                Assert.IsFalse(host.View.IsEditing, "Columns 自己的編輯框也關掉");

                Assert.IsFalse(f.View.IsEditing);
                Assert.AreEqual(0, f.View.Controls.OfType<TextBox>().Count());
                Assert.IsFalse(f.Edits.Any(e => e.Action == CascadeEditAction.Insert));
                f.AssertShape("Z", "Z > A1", "Z > A2", "B", "C");
                Assert.AreEqual(cBounds.Top, f.Bounds(c).Top, "C 回到原位（改名後欄寬可能不同，只比高度）");
            }
        }

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
                    new[] { "復原(Ctrl+Z)", "重做(Ctrl+Y)", "-", "插入", "建立子節點(Ctrl+N)", "重新命名(F2)", "-", "複製(Ctrl+C)", "複製(路徑)(Ctrl+Shift+C)", "剪下(Ctrl+X)", "貼上", "-", "刪除(Del)", "-", "全部展開(*)", "全部摺疊", "-", "範圍", "顯示" },
                    OpenMenu());

                f.View.TreeContextMenu.Show(f.View, new System.Drawing.Point(10, 10));
                Assert.IsFalse(f.View.TreeContextMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == "全部展開").Enabled, "葉節點停用全部展開");
                Assert.IsFalse(f.View.TreeContextMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == "全部摺疊").Enabled, "葉節點停用全部摺疊");
                f.View.TreeContextMenu.Close();

                f.View.ReadOnly = true;
                CollectionAssert.AreEqual(new[] { "複製(Ctrl+C)", "複製(路徑)(Ctrl+Shift+C)", "-", "全部展開(*)", "全部摺疊", "-", "範圍", "顯示" }, OpenMenu());

                f.View.State.ClearSelection();
                CollectionAssert.AreEqual(new[] { "範圍", "顯示" }, OpenMenu(), "沒有對象時只有顯示設定");
            }
        }

        [TestMethod]
        public void ContextMenu_ExpandAllAndCollapseAll_WorkOnBranchAndReadOnly()
        {
            using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000), ClientSize = new System.Drawing.Size(300, 200), ShowInTaskbar = false })
            using (var f = new Fixture(readOnly: true))
            {
                f.Tree.AddPath(new[] { "A", "A1", "A1a", "A1a1" });
                f.View.Redraw();
                f.View.Dock = DockStyle.Fill;
                form.Controls.Add(f.View);
                form.Show();

                var a = f.Node("A");
                var a1 = f.Node("A > A1");
                var a1a = f.Node("A > A1 > A1a");
                f.Select("A");

                ToolStripMenuItem FindItem(string text) => f.View.TreeContextMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == text);

                f.View.TreeContextMenu.Show(f.View, new System.Drawing.Point(10, 10));
                var expandItem = FindItem("全部展開");
                var collapseItem = FindItem("全部摺疊");
                Assert.IsTrue(expandItem.Enabled);
                Assert.IsTrue(collapseItem.Enabled);
                expandItem.PerformClick();
                f.View.TreeContextMenu.Close();

                Assert.IsTrue(f.View.IsExpanded(a));
                Assert.IsTrue(f.View.IsExpanded(a1));
                Assert.IsTrue(f.View.IsExpanded(a1a));

                f.View.TreeContextMenu.Show(f.View, new System.Drawing.Point(10, 10));
                FindItem("全部摺疊").PerformClick();
                f.View.TreeContextMenu.Close();

                Assert.IsFalse(f.View.IsExpanded(a));
                Assert.IsFalse(f.View.IsExpanded(a1));
                Assert.IsFalse(f.View.IsExpanded(a1a));
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

                // 夠大，不用捲動：Bounds 只在控制項範圍內找。
                View.Size = new System.Drawing.Size(600, 600);
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

            /// <summary>節點方塊在控制項上的範圍：逐點用 <see cref="CascadeDiagramView.GetNodeAt"/> 找出來；沒有畫在圖上時為空。</summary>
            public System.Drawing.Rectangle Bounds(MenuNode node)
            {
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                for (var y = 0; y < 600; y++)
                {
                    for (var x = 0; x < 600; x++)
                    {
                        if (!ReferenceEquals(View.GetNodeAt(new System.Drawing.Point(x, y)), node)) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }

                return left > right ? System.Drawing.Rectangle.Empty : System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
            }

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
