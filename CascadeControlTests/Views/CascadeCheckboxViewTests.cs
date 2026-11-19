using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views
{
    [TestClass]
    public class CascadeCheckboxViewTests
    {
        private sealed class ProbeView : CascadeCheckboxView
        {
            public void Draw(CascadeDrawNodeEventArgs e) => OnDrawNode(e);
        }

        #region CheckSet

        [TestMethod]
        public void SetTree_CreatesFreshCheckSetForThatTree()
        {
            using (var view = new CascadeCheckboxView())
            {
                Assert.IsNull(view.CheckSet);

                var tree = TestTrees.Template();
                CascadeView asBase = view;
                asBase.SetTree(tree);   // 以基底型別呼叫也要建立 CheckSet

                Assert.AreSame(tree, view.CheckSet.Tree);
                var first = view.CheckSet;
                first.CheckAll();

                view.SetTree(tree);
                Assert.AreNotSame(first, view.CheckSet, "每次 SetTree 都換一份新的");
                Assert.AreEqual(0, view.CheckSet.Count);

                view.SetTree(null);
                Assert.IsNull(view.CheckSet);
            }
        }

        [TestMethod]
        public void CheckedChanged_ForwardsOnlyTheCurrentCheckSet()
        {
            using (var view = new CascadeCheckboxView())
            {
                var tree = TestTrees.Template();
                view.SetTree(tree);
                var old = view.CheckSet;

                var events = new List<MenuCheckChangedEventArgs>();
                view.CheckedChanged += (sender, e) => events.Add(e);

                old.Check(tree.TopLevelNodes[0]);
                Assert.AreEqual(1, events.Count);
                Assert.AreSame(tree.TopLevelNodes[0], events[0].Target);

                var replacement = new MenuCheckSet(tree);
                view.CheckSet = replacement;
                Assert.AreEqual(1, events.Count, "換上另一份不觸發");

                old.Check(tree.TopLevelNodes[1]);
                Assert.AreEqual(1, events.Count, "舊的那一份已經取消訂閱");

                replacement.Check(tree.TopLevelNodes[1]);
                Assert.AreEqual(2, events.Count);
            }
        }

        #endregion CheckSet

        #region 滑鼠

        [TestMethod]
        public void ClickOnCheckBox_TogglesWithoutSelecting_ClickOnTextOnlySelects()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var column = host.Columns[0];

                var row = column.GetItemRectangle(1);
                var box = host.View.GetCheckBoxBounds(row);
                Assert.IsTrue(row.Contains(box), "勾選框在那一列裡面");

                ViewHost.SendLeftClick(column, Center(box));
                Assert.IsTrue(host.View.CheckSet.IsChecked(tree.TopLevelNodes[1]));
                Assert.IsNull(host.View.SelectedNode, "點勾選框不改選取");

                ViewHost.SendLeftClick(column, Center(box));
                Assert.IsFalse(host.View.CheckSet.IsChecked(tree.TopLevelNodes[1]), "再點一次取消");

                var textPoint = new Point(box.Right + 20, row.Top + (row.Height / 2));
                ViewHost.SendLeftClick(column, textPoint);
                Assert.AreEqual(0, host.View.CheckSet.Count, "點文字不切換");
                Assert.AreSame(tree.TopLevelNodes[1], host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void ClickOnCheckBox_InNextColumn_DoesNotSlide()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                host.View.ColumnLayoutMode = CascadeColumnLayoutMode.Sliding;
                host.View.DisplayColumnCount = 3;

                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var axisX = tree.TopLevelNodes[1].Children[0];
                var jog = axisX.Children[1];
                Assert.IsTrue(host.View.TrySelectNode(axisX));

                var columns = host.Columns;
                Assert.AreEqual(3, columns.Length);
                Assert.IsTrue(columns[0].Visible, "停在第二層時第一欄還看得到");
                var location = columns[2].Location;

                var row = columns[2].GetItemRectangle(1);
                ViewHost.SendLeftClick(columns[2], Center(host.View.GetCheckBoxBounds(row)));

                Assert.IsTrue(host.View.CheckSet.IsChecked(jog), "勾到的是游標下的那一個");
                Assert.AreSame(axisX, host.View.SelectedNode, "選取沒有跳過去");
                Assert.AreEqual(3, host.Columns.Length);
                Assert.IsTrue(columns[0].Visible, "畫面沒有滑動");
                Assert.AreEqual(location, columns[2].Location);

                ViewHost.SendLeftClick(columns[2], new Point(row.Left + 40, row.Top + (row.Height / 2)));
                Assert.AreSame(jog, host.View.SelectedNode, "點文字照常選取");
                Assert.IsFalse(columns[0].Visible, "選到第三層後畫面滑動");
            }
        }

        [TestMethod]
        public void ShiftClickOnCheckBox_ChecksSubtree_AgainUnchecks()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var set = host.View.CheckSet;
                var motor = tree.TopLevelNodes[1];
                var column = host.Columns[0];
                var box = Center(host.View.GetCheckBoxBounds(column.GetItemRectangle(1)));

                set.Check(motor);
                using (new ShiftDown())
                {
                    ViewHost.SendLeftClick(column, box);
                    Assert.IsTrue(set.IsFullyChecked(motor), "部分勾選時 Shift＋點會全勾");

                    ViewHost.SendLeftClick(column, box);
                    Assert.IsFalse(set.IsChecked(motor), "已經全勾時再點就取消");
                }

                Assert.IsNull(host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void DragOffCheckBox_OrDoubleClick_BehaveLikeACheckBox()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var activated = 0;
                host.View.NodeActivated += (sender, e) => activated++;

                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var set = host.View.CheckSet;
                var column = host.Columns[0];
                var box0 = Center(host.View.GetCheckBoxBounds(column.GetItemRectangle(0)));
                var box1 = Center(host.View.GetCheckBoxBounds(column.GetItemRectangle(1)));

                ViewHost.SendLeftDrag(column, box0, box1);
                Assert.AreEqual(0, set.Count, "按下與放開不在同一個勾選框就不算");
                Assert.IsNull(host.View.SelectedNode, "拖曳時 ListBox 也沒有跟著改選");

                ViewHost.SendLeftDoubleClick(column, box0);
                Assert.AreEqual(0, set.Count, "快速點兩下是切換兩次");
                Assert.AreEqual(2, set.History.UndoCount, "兩次都有記進復原歷史");
                Assert.AreEqual(0, activated, "勾選框上雙擊不觸發 NodeActivated");
                Assert.IsNull(host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void RowHeight_IsTallEnoughForCheckBox()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                host.View.Font = new Font("Microsoft Sans Serif", 6f);
                host.View.SetTree(TestTrees.Template());

                var row = host.Columns[0].GetItemRectangle(0);
                var box = host.View.GetCheckBoxBounds(row);

                Assert.IsTrue(box.Top > row.Top && box.Bottom < row.Bottom, $"列 {row}，勾選框 {box}");
            }
        }

        [TestMethod]
        public void RightClickOnNode_ShowsNodeMenu_WithoutSelecting()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var set = host.View.CheckSet;
                var menu = host.View.NodeContextMenu;
                var system = tree.TopLevelNodes[0];

                RightClickRow(host, 0, 0);
                Assert.IsTrue(menu.Visible, "右鍵點節點要開啟節點功能表");
                Assert.IsFalse(host.View.ColumnContextMenu.Visible);
                Assert.IsNull(host.View.SelectedNode, "右鍵不改選取");
                Assert.AreSame(system, host.View.ContextMenuNode);

                // 勾選項目在最前面；預設唯讀，基底只剩「復原」、「重做」（可以復原勾選）與「複製」、「複製(路徑)」。
                var available = menu.Items.Cast<ToolStripItem>().Where(item => item.Available && !(item is ToolStripSeparator)).Select(item => item.Text).ToList();
                CollectionAssert.AreEqual(new[] { "勾選(含所有子節點)", "復原", "重做", "複製", "複製(路徑)" }, available);

                menu.Items[0].PerformClick();
                Assert.IsTrue(set.IsFullyChecked(system));
                Assert.IsFalse(set.IsChecked(tree.TopLevelNodes[1]));

                menu.Close();
            }
        }

        [TestMethod]
        public void RightClickOnBlankArea_ShowsColumnMenu_ItemsActOnWholeColumn()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var set = host.View.CheckSet;
                var menu = host.View.ColumnContextMenu;
                var system = tree.TopLevelNodes[0];
                set.CheckSubtree(system);

                RightClickBlank(host, 0);
                Assert.IsTrue(menu.Visible, "右鍵點空白處要開啟欄位功能表");
                Assert.IsFalse(host.View.NodeContextMenu.Visible);
                Assert.IsNull(host.View.SelectedNode);
                Assert.AreSame(system, host.View.ContextMenuNode, "代表那一欄的是第一個節點");

                // 項目順序：全部勾選、全部勾選(含子節點)、分隔線、全部取消，之後是基底的項目（唯讀時只有「復原」、「重做」、「全選」）。
                var available = menu.Items.Cast<ToolStripItem>().Where(item => item.Available && !(item is ToolStripSeparator)).Select(item => item.Text).ToList();
                CollectionAssert.AreEqual(
                    new[] { "全部勾選", "全部勾選(含子節點)", "全部取消", "復原", "重做", "全選" },
                    available);

                menu.Items[0].PerformClick();
                Assert.IsTrue(tree.TopLevelNodes.All(set.IsChecked), "第一欄全部都勾了");
                Assert.IsTrue(set.IsFullyChecked(system), "原本已勾的子節點維持原狀");
                Assert.IsFalse(set.IsFullyChecked(tree.TopLevelNodes[1]), "其他節點的子節點沒有一起勾");

                menu.Items[1].PerformClick();
                Assert.IsTrue(tree.TopLevelNodes.All(set.IsFullyChecked), "第一欄全部（含子節點）都勾了");

                menu.Items[3].PerformClick();
                Assert.AreEqual(0, set.Count, "全部取消連同子節點");

                menu.Close();
            }
        }

        [TestMethod]
        public void RightClick_WithoutCheckSet_ShowsOnlyEditItems()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                host.View.SetTree(TestTrees.Template());
                host.View.CheckSet = null;

                RightClickRow(host, 0, 0);
                Assert.IsTrue(host.View.NodeContextMenu.Visible);
                CollectionAssert.AreEqual(new[] { "複製", "複製(路徑)" }, AvailableTexts(host.View.NodeContextMenu), "唯讀又沒有勾選時，沒有東西可以復原，只剩「複製」與「複製(路徑)」");
                host.View.NodeContextMenu.Close();

                RightClickBlank(host, 0);
                Assert.IsTrue(host.View.ColumnContextMenu.Visible);
                CollectionAssert.AreEqual(new[] { "全選" }, AvailableTexts(host.View.ColumnContextMenu));
                host.View.ColumnContextMenu.Close();

                Assert.IsNull(host.View.SelectedNode);
            }
        }

        private static List<string> AvailableTexts(ContextMenuStrip menu) =>
            menu.Items.Cast<ToolStripItem>().Where(item => item.Available && !(item is ToolStripSeparator)).Select(item => item.Text).ToList();

        #endregion 滑鼠

        #region 鍵盤

        [TestMethod]
        public void Space_TogglesSelectedNode()
        {
            using (var host = FocusedHost(out var tree))
            {
                var motor = tree.TopLevelNodes[1];
                Assert.IsTrue(host.View.TrySelectNode(motor));

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Space));
                Assert.IsTrue(host.View.CheckSet.IsChecked(motor));

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Space));
                Assert.IsFalse(host.View.CheckSet.IsChecked(motor));
            }
        }

        [TestMethod]
        public void ShiftSpace_ChecksSubtree_AgainUnchecks()
        {
            using (var host = FocusedHost(out var tree))
            {
                var set = host.View.CheckSet;
                var motor = tree.TopLevelNodes[1];
                Assert.IsTrue(host.View.TrySelectNode(motor));
                set.Check(motor);

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Shift | Keys.Space));
                Assert.IsTrue(set.IsFullyChecked(motor), "部分勾選時 Shift+Space 會全勾");

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Shift | Keys.Space));
                Assert.IsFalse(set.IsChecked(motor), "已經全勾時再按就取消");
                Assert.AreSame(motor, host.View.SelectedNode);
            }
        }

        [TestMethod]
        public void Space_WithoutSelection_IsSwallowedAndChangesNothing()
        {
            using (var host = FocusedHost(out var _))
            {
                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Space));
                Assert.AreEqual(0, host.View.CheckSet.Count);
            }
        }

        [TestMethod]
        public void UndoRedoKeys_RestoreStateAndJumpToTarget()
        {
            using (var host = FocusedHost(out var tree))
            {
                var set = host.View.CheckSet;
                var jog = tree.TopLevelNodes[1].Children[0].Children[1];   // Motor > Axis X > Jog
                set.Check(jog);
                host.View.TrySelectNode(tree.TopLevelNodes[0]);

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Control | Keys.Z));
                Assert.AreEqual(0, set.Count);
                Assert.AreSame(jog, host.View.SelectedNode, "復原後選取跳到那一步的節點");

                host.View.TrySelectNode(tree.TopLevelNodes[0]);
                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Control | Keys.Y));
                Assert.IsTrue(set.IsChecked(jog));
                Assert.AreSame(jog, host.View.SelectedNode);

                ViewHost.SendCmdKey(host.View, Keys.Control | Keys.Z);
                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Control | Keys.Shift | Keys.Z), "Ctrl+Shift+Z 也是重做");
                Assert.IsTrue(set.IsChecked(jog));

                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Control | Keys.Y), "沒有步驟可以重做時仍然攔下");
            }
        }

        [TestMethod]
        public void Keys_NotFocusedOrNoCheckSet_AreNotHandled()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                host.View.SetTree(TestTrees.Template());
                host.View.TrySelectNode(host.View.CheckSet.Tree.TopLevelNodes[0]);

                // 焦點不在欄位上：不攔，勾選也不變。
                Assert.IsFalse(ViewHost.SendCmdKey(host.View, Keys.Space));
                Assert.AreEqual(0, host.View.CheckSet.Count);
            }

            using (var host = FocusedHost(out var _))
            {
                host.View.CheckSet = null;
                Assert.IsFalse(ViewHost.SendCmdKey(host.View, Keys.Space), "沒有 CheckSet 時不攔");
                Assert.IsTrue(ViewHost.SendCmdKey(host.View, Keys.Escape), "基底的 Esc 仍然有效");
            }
        }

        #endregion 鍵盤

        #region 復原與重做

        [TestMethod]
        public void Undo_ChecksAndRenames_FollowOneTimeline()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.ReadOnly = false;
                host.View.SetTree(tree);
                var set = host.View.CheckSet;
                var reboot = tree.TopLevelNodes[0].Children[0];
                var motor = tree.TopLevelNodes[1];
                var edits = new List<CascadeTreeEditedEventArgs>();
                host.View.TreeEdited += (sender, e) => edits.Add(e);

                set.Check(reboot);                                      // 1. 勾選
                Assert.IsTrue(host.View.TrySelectNode(reboot));
                Assert.IsTrue(host.View.BeginRename());
                EditBox(host).Text = "Restart";
                host.View.EndEdit(commit: true);                        // 2. 改名
                set.Check(motor);                                       // 3. 勾選

                Assert.AreEqual("Restart", reboot.Name);
                Assert.AreSame(host.View.History, set.History, "勾選和樹的編輯記在同一份歷史");
                Assert.AreEqual(3, host.View.History.UndoCount);

                Assert.IsTrue(host.View.Undo());
                Assert.IsFalse(set.IsChecked(motor));
                Assert.AreEqual("Restart", reboot.Name, "改名比較早，還沒退回");
                Assert.AreEqual(1, edits.Count, "只改勾選的步驟不觸發 TreeEdited");

                Assert.IsTrue(host.View.Undo());
                Assert.AreEqual("Reboot", reboot.Name);
                Assert.AreEqual(CascadeEditAction.Undo, edits.Last().Action);
                Assert.AreSame(reboot, host.View.SelectedNode, "選取跳到改名的節點");
                Assert.IsTrue(set.IsChecked(reboot), "改名不換節點，勾選維持");

                host.View.ReadOnly = true;
                Assert.IsFalse(host.View.CanRedo, "下一個重做是改名，唯讀時不行");
                Assert.IsFalse(host.View.Redo());
                Assert.AreEqual("Reboot", reboot.Name);
                Assert.IsTrue(host.View.CanUndo, "下一個復原是勾選，唯讀也可以");
                Assert.IsTrue(host.View.Undo());
                Assert.AreEqual(0, set.Count);
            }
        }

        [TestMethod]
        public void Undo_Insert_RemovesNode_RedoBringsSameNodeBack()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.ReadOnly = false;
                host.View.SetTree(tree);
                var system = tree.TopLevelNodes[0];

                Assert.IsTrue(host.View.TrySelectNode(system.Children[0]));
                Assert.IsTrue(host.View.BeginInsert(before: false));
                EditBox(host).Text = "Halt";
                host.View.EndEdit(commit: true);

                var halt = system.Children[1];
                Assert.AreEqual("Halt", halt.Name);

                Assert.IsTrue(host.View.Undo());
                Assert.IsNull(halt.Parent);
                CollectionAssert.AreEqual(new[] { "Reboot", "Shutdown", "Info" }, system.Children.Select(node => node.Name).ToList());
                Assert.AreSame(system.Children[1], host.View.SelectedNode, "選取跳到原位置的下一個兄弟");

                Assert.IsTrue(host.View.Redo());
                Assert.AreSame(halt, system.Children[1], "重做接回的是同一個節點");
                Assert.AreSame(halt, host.View.SelectedNode);
                Assert.IsTrue(host.View.CheckSet.Contains(halt), "接回後有勾選框");
            }
        }

        [TestMethod]
        public void CreateChildAndPasteAsChild_OnlyOnLeaf()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.ReadOnly = false;
                host.View.SetTree(tree);
                var system = tree.TopLevelNodes[0];
                var reboot = system.Children[0];

                // 已經有子節點：建立子節點與作為子節點貼上都不做事，功能表項目停用。
                Assert.IsTrue(host.View.TrySelectNode(system));
                Assert.IsFalse(host.View.BeginCreateChild());
                Assert.IsFalse(host.View.IsEditing);
                Assert.IsFalse(host.View.PasteAsChild());

                RightClickRow(host, 0, 0);
                var menu = host.View.NodeContextMenu;
                var createChild = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "建立子節點");
                var pasteItem = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "貼上");
                var pasteChild = pasteItem.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "作為子節點");
                Assert.IsTrue(createChild.Available);
                Assert.IsFalse(createChild.Enabled, "System 有子節點");
                Assert.IsFalse(pasteChild.Enabled);
                menu.Close();

                // 葉節點：長出下一欄，建立第一個子節點；復原後回到葉節點並選取它。
                Assert.IsTrue(host.View.TrySelectNode(reboot));
                Assert.IsTrue(host.View.BeginCreateChild());
                EditBox(host).Text = "Now";
                host.View.EndEdit(commit: true);

                Assert.AreEqual("Now", reboot.Children.Single().Name);
                Assert.AreSame(reboot.Children[0], host.View.SelectedNode);

                Assert.IsTrue(host.View.Undo());
                Assert.IsTrue(reboot.IsLeaf);
                Assert.AreSame(reboot, host.View.SelectedNode, "子節點一個都不剩時選父節點");
            }
        }

        [TestMethod]
        public void SetTreeOrNewCheckSet_ClearsHistory()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                host.View.SetTree(TestTrees.Template());
                host.View.CheckSet.Check(host.View.Tree.TopLevelNodes[0]);
                Assert.IsTrue(host.View.CanUndo);

                host.View.SetTree(TestTrees.Template());
                Assert.IsFalse(host.View.History.CanUndo, "換樹之後舊步驟沒有意義");

                host.View.CheckSet.Check(host.View.Tree.TopLevelNodes[0]);
                host.View.CheckSet = new MenuCheckSet(host.View.Tree);
                Assert.IsFalse(host.View.History.CanUndo);
                Assert.AreSame(host.View.History, host.View.CheckSet.History);
            }
        }

        [TestMethod]
        public void ContextMenu_UndoItem_EnabledByHistory_AndUndoes()
        {
            using (var host = new ViewHost<CascadeCheckboxView>())
            {
                var tree = TestTrees.Template();
                host.View.SetTree(tree);
                var menu = host.View.NodeContextMenu;
                var undo = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "復原");

                RightClickRow(host, 0, 0);
                Assert.IsTrue(undo.Available);
                Assert.IsFalse(undo.Enabled, "還沒有步驟");
                menu.Close();

                host.View.CheckSet.Check(tree.TopLevelNodes[1]);
                RightClickRow(host, 0, 0);
                Assert.IsTrue(undo.Enabled);

                undo.PerformClick();
                menu.Close();
                Application.DoEvents();   // 功能表動作等功能表關掉之後才執行

                Assert.AreEqual(0, host.View.CheckSet.Count);
            }
        }

        private static TextBox EditBox(ViewHost<CascadeCheckboxView> host) =>
            host.Columns.SelectMany(column => column.Controls.Cast<Control>()).SelectMany(frame => frame.Controls.OfType<TextBox>()).Single();

        #endregion 復原與重做

        #region 繪製

        [TestMethod]
        public void Draw_UncheckedCheckedAndPartial_LookDifferent()
        {
            using (var view = new ProbeView())
            {
                var tree = TestTrees.Template();
                view.SetTree(tree);
                var motor = tree.TopLevelNodes[1];

                var uncheckedPixels = Render(view, motor);

                view.CheckSet.Check(motor);
                var partialPixels = Render(view, motor);

                view.CheckSet.CheckSubtree(motor);
                var checkedPixels = Render(view, motor);

                CollectionAssert.AreNotEqual(uncheckedPixels, partialPixels);
                CollectionAssert.AreNotEqual(uncheckedPixels, checkedPixels);
                CollectionAssert.AreNotEqual(partialPixels, checkedPixels, "部分勾選和全勾要看得出差別");
            }
        }

        [TestMethod]
        public void Draw_NodeOutsideCheckSet_LooksLikeBaseView()
        {
            using (var view = new ProbeView())
            using (var plain = new PlainProbeView())
            {
                view.SetTree(TestTrees.Template());
                var foreign = TestTrees.Template().TopLevelNodes[1];

                CollectionAssert.AreEqual(Render(plain, foreign), Render(view, foreign), "不屬於 CheckSet 的節點沒有勾選框");

                view.CheckSet = null;
                CollectionAssert.AreEqual(Render(plain, foreign), Render(view, foreign), "沒有 CheckSet 時也沒有勾選框");
            }
        }

        [TestMethod]
        public void Draw_HandledBySubscriber_DrawsNothing()
        {
            using (var view = new ProbeView())
            {
                var tree = TestTrees.Template();
                view.SetTree(tree);
                view.DrawNode += (sender, e) => e.Handled = true;

                var pixels = Render(view, tree.TopLevelNodes[0]);

                Assert.IsTrue(pixels.All(pixel => pixel == Color.Magenta.ToArgb()), "訂閱端自己畫的時候，勾選框也不畫");
            }
        }

        private sealed class PlainProbeView : CascadeView
        {
            public void Draw(CascadeDrawNodeEventArgs e) => OnDrawNode(e);
        }

        private static int[] Render(CascadeView view, ICascadeNode node)
        {
            using (var bitmap = new Bitmap(120, 20))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Magenta);

                var args = new CascadeDrawNodeEventArgs(
                    g, view.Font, new Rectangle(0, 0, 120, 20), node, -1,
                    isSelected: false, isFocusedColumn: false, isEnabled: true);

                if (view is ProbeView probe) probe.Draw(args);
                else ((PlainProbeView)view).Draw(args);

                g.Flush();

                var pixels = new int[bitmap.Width * bitmap.Height];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++) pixels[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y).ToArgb();
                }

                return pixels;
            }
        }

        #endregion 繪製

        private static Point Center(Rectangle bounds) => new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));

        private static void RightClickRow(ViewHost<CascadeCheckboxView> host, int columnIndex, int rowIndex)
        {
            var column = host.Columns[columnIndex];
            var row = column.GetItemRectangle(rowIndex);
            ViewHost.RaiseMouseUp(column, MouseButtons.Right, new Point(row.Left + 40, row.Top + (row.Height / 2)));
        }

        private static void RightClickBlank(ViewHost<CascadeCheckboxView> host, int columnIndex)
        {
            var column = host.Columns[columnIndex];
            ViewHost.RaiseMouseUp(column, MouseButtons.Right, new Point(40, column.ClientSize.Height - 2));
        }

        /// <summary>
        /// 在這個執行緒的鍵盤狀態裡按住 Shift（<see cref="Control.ModifierKeys"/> 讀的就是它），Dispose 時還原。
        /// </summary>
        private sealed class ShiftDown : System.IDisposable
        {
            private const int VK_SHIFT = 0x10;
            private readonly byte[] _original = new byte[256];

            public ShiftDown()
            {
                GetKeyboardState(_original);
                var state = (byte[])_original.Clone();
                state[VK_SHIFT] = 0x80;
                SetKeyboardState(state);
            }

            public void Dispose() => SetKeyboardState(_original);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool GetKeyboardState(byte[] keyState);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool SetKeyboardState(byte[] keyState);
        }

        // 顯示範本樹並把鍵盤焦點放進第一欄；測試環境拿不到焦點時，測試標成 Inconclusive。
        private static ViewHost<CascadeCheckboxView> FocusedHost(out MenuTree tree)
        {
            var host = new ViewHost<CascadeCheckboxView>();
            tree = TestTrees.Template();
            host.View.SetTree(tree);

            host.Form.Activate();
            host.View.FocusActiveColumn();
            Application.DoEvents();

            if (!host.Columns[0].Focused)
            {
                host.Dispose();
                Assert.Inconclusive("測試環境拿不到鍵盤焦點（例如沒有互動式桌面）。");
            }

            return host;
        }
    }
}