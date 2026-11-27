using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views.Columns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views.Columns
{
    /// <summary>
    /// 同一欄內比照檔案總管的多選：Ctrl / Shift / Ctrl+Shift 點選與方向鍵、Ctrl+Space，以及焦點跟著移動。
    /// </summary>
    /// <remarks>
    /// 測試資料是 &lt;01&gt;Info ～ &lt;08&gt;About 八個頂層節點，每一個底下都有一個子節點（名稱是父節點加上 "-1"），
    /// 用來確認焦點移動後右邊的欄位換成焦點節點的子節點。
    /// </remarks>
    [TestClass]
    public class CascadeColumnsViewMultiSelectTests
    {
        private static readonly string[] Names = { "Info", "Status", "Config", "Power", "Clock", "Watchdog", "License", "About" };

        #region Ctrl+點選

        [TestMethod]
        public void CtrlClick_StillTogglesSingleNode()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 1));

                CollectionAssert.AreEqual(new[] { "Status", "Watchdog" }, MarkedNames(host.View));
            }
        }

        [TestMethod]
        public void CtrlClick_MovesFocus_ShowsItsChildren_KeepsMarks()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendLeftClick(column, RowPoint(column, 1));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));

                CollectionAssert.AreEqual(new[] { "Status", "Watchdog" }, MarkedNames(host.View), "第一次 Ctrl 把原本選取的 <02> 也算進去");
                Assert.AreEqual("Watchdog", host.View.SelectedNode.Name, "焦點跟著 Ctrl+點選移動");
                Assert.AreEqual("Watchdog-1", ((ICascadeNode)host.Columns[1].Items[0]).Name, "右邊欄位是焦點節點的子節點");
            }
        }

        [TestMethod]
        public void CtrlClick_TogglingOffLastMark_KeepsAnchor()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                Assert.AreEqual(0, host.View.MultiSelectedNodes.Count);

                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 1), shift: true);

                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog" }, MarkedNames(host.View),
                    "多選變成 0 個後錨點仍是 <06>");
            }
        }

        #endregion Ctrl+點選

        #region Shift / Ctrl+Shift 點選

        [TestMethod]
        public void CtrlShiftClick_AfterPlainClick_SelectsRangeFromSelectedNode()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendLeftClick(column, RowPoint(column, 1));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5), shift: true);

                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog" }, MarkedNames(host.View),
                    "先點 <02>，再 Ctrl+Shift+點 <06>，要選 <02>～<06>");
                Assert.AreEqual("Watchdog", host.View.SelectedNode.Name);
            }
        }

        [TestMethod]
        public void CtrlShiftClick_AgainKeepsAnchor()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendLeftClick(column, RowPoint(column, 1));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5), shift: true);
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 7), shift: true);

                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog", "License", "About" }, MarkedNames(host.View),
                    "錨點仍是 <02>，範圍延伸到 <08>，原本的多選保留");
            }
        }

        [TestMethod]
        public void CtrlShiftClick_UsesLastCtrlClickAsAnchor()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 1), shift: true);

                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog" }, MarkedNames(host.View),
                    "Ctrl+點 <06>，再 Ctrl+Shift+點 <02>，要選 <02>～<06>");
            }
        }

        [TestMethod]
        public void ShiftClick_SelectsRange_AndShiftClickAgainResizesIt()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendLeftClick(column, RowPoint(column, 1));
                ViewHost.SendShiftLeftClick(column, RowPoint(column, 5));
                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog" }, MarkedNames(host.View));
                Assert.AreEqual("Watchdog", host.View.SelectedNode.Name);

                ViewHost.SendShiftLeftClick(column, RowPoint(column, 3));
                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power" }, MarkedNames(host.View), "錨點不動，範圍改成 <02>～<04>");
            }
        }

        [TestMethod]
        public void ShiftClick_ReplacesEarlierMarks()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 1));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                ViewHost.SendShiftLeftClick(column, RowPoint(column, 7));

                CollectionAssert.AreEqual(new[] { "Watchdog", "License", "About" }, MarkedNames(host.View),
                    "錨點是最後 Ctrl+點的 <06>，Shift 取代原本的 <02>");
            }
        }

        [TestMethod]
        public void CtrlShiftClick_AfterShiftRange_MergesTwoRanges()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];

                ViewHost.SendLeftClick(column, RowPoint(column, 0));
                ViewHost.SendShiftLeftClick(column, RowPoint(column, 1));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 5));
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 7), shift: true);

                CollectionAssert.AreEqual(new[] { "Info", "Status", "Watchdog", "License", "About" }, MarkedNames(host.View));
            }
        }

        #endregion Shift / Ctrl+Shift 點選

        #region 方向鍵與 Ctrl+Space

        [TestMethod]
        public void ShiftArrows_SelectRangeFromAnchor()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];
                ViewHost.SendLeftClick(column, RowPoint(column, 1));

                for (var i = 0; i < 4; i++) Assert.IsTrue(ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.Down));
                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock", "Watchdog" }, MarkedNames(host.View));
                Assert.AreEqual("Watchdog", host.View.SelectedNode.Name);

                ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.Up);
                CollectionAssert.AreEqual(new[] { "Status", "Config", "Power", "Clock" }, MarkedNames(host.View));

                ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.End);
                CollectionAssert.AreEqual(Names.Skip(1).ToArray(), MarkedNames(host.View));

                ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.Home);
                CollectionAssert.AreEqual(new[] { "Info", "Status" }, MarkedNames(host.View), "範圍跨到錨點的另一邊");
            }
        }

        [TestMethod]
        public void PlainArrow_AfterShiftRange_ClearsMarks()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];
                ViewHost.SendLeftClick(column, RowPoint(column, 1));
                ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.Down);
                ViewHost.RaiseKeyDown(column, Keys.Shift | Keys.Down);
                Assert.AreEqual(3, host.View.MultiSelectedNodes.Count);

                ViewHost.SendKey(host.Columns[0], Keys.Down);

                Assert.AreEqual(0, host.View.MultiSelectedNodes.Count);
                Assert.AreEqual("Clock", host.View.SelectedNode.Name, "焦點原本在 <04>，一般 ↓ 到 <05>");

                ViewHost.RaiseKeyDown(host.Columns[0], Keys.Shift | Keys.Down);
                CollectionAssert.AreEqual(new[] { "Clock", "Watchdog" }, MarkedNames(host.View), "一般 ↓ 把錨點移到 <05>");
            }
        }

        [TestMethod]
        public void CtrlArrows_MoveFocusOnly_CtrlSpaceToggles()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];
                ViewHost.SendCtrlLeftClick(column, RowPoint(column, 1));

                ViewHost.RaiseKeyDown(column, Keys.Control | Keys.Down);
                ViewHost.RaiseKeyDown(column, Keys.Control | Keys.Down);
                CollectionAssert.AreEqual(new[] { "Status" }, MarkedNames(host.View), "Ctrl+↓ 不改多選");
                Assert.AreEqual("Power", host.View.SelectedNode.Name);

                Assert.IsTrue(ViewHost.SendCmdKey(column, Keys.Control | Keys.Space));
                CollectionAssert.AreEqual(new[] { "Status", "Power" }, MarkedNames(host.View));

                ViewHost.SendCmdKey(column, Keys.Control | Keys.Space);
                CollectionAssert.AreEqual(new[] { "Status" }, MarkedNames(host.View));
            }
        }

        [TestMethod]
        public void CtrlArrow_FromPlainSelection_KeepsOriginalNodeSelected()
        {
            using (var host = CreateHost())
            {
                var column = host.Columns[0];
                ViewHost.SendLeftClick(column, RowPoint(column, 1));

                ViewHost.RaiseKeyDown(column, Keys.Control | Keys.Down);

                CollectionAssert.AreEqual(new[] { "Status" }, MarkedNames(host.View), "原本選取的 <02> 留下來變成多選");
                Assert.AreEqual("Config", host.View.SelectedNode.Name);
            }
        }

        [TestMethod]
        public void CheckboxView_SpaceStillChecks_CtrlSpaceOnlyMarks()
        {
            using (var host = new ViewHost<CheckBoxColumnsView>())
            {
                host.View.SetTree(EightItems());
                var column = host.Columns[0];

                // 列的左邊是勾選框，點右邊才是選取。
                var row = column.GetItemRectangle(1);
                ViewHost.SendLeftClick(column, new Point(row.Right - 5, row.Top + 2));
                ViewHost.RaiseKeyDown(column, Keys.Control | Keys.Down);

                var checkedBefore = host.View.CheckSet.IsChecked((MenuNode)host.View.SelectedNode);
                ViewHost.SendCmdKey(column, Keys.Control | Keys.Space);
                Assert.AreEqual(checkedBefore, host.View.CheckSet.IsChecked((MenuNode)host.View.SelectedNode), "Ctrl+Space 不改勾選");
                CollectionAssert.AreEqual(new[] { "Status", "Config" }, MarkedNames(host.View));

                ViewHost.SendCmdKey(column, Keys.Space);
                Assert.AreNotEqual(checkedBefore, host.View.CheckSet.IsChecked((MenuNode)host.View.SelectedNode), "Space 仍切換勾選");
            }
        }

        #endregion 方向鍵與 Ctrl+Space

        [TestMethod]
        public void ShouldDrawFocusCue_FocusRowInFocusedColumn_RegardlessOfMarks()
        {
            Assert.IsTrue(CascadeColumnsView.ShouldDrawFocusCue(handled: false, isFocusRow: true, isFocusedColumn: true));
            Assert.IsFalse(CascadeColumnsView.ShouldDrawFocusCue(handled: true, isFocusRow: true, isFocusedColumn: true));
            Assert.IsFalse(CascadeColumnsView.ShouldDrawFocusCue(handled: false, isFocusRow: false, isFocusedColumn: true));
            Assert.IsFalse(CascadeColumnsView.ShouldDrawFocusCue(handled: false, isFocusRow: true, isFocusedColumn: false));
        }

        private static ViewHost<CascadeColumnsView> CreateHost()
        {
            var host = new ViewHost<CascadeColumnsView>();
            host.View.SetTree(EightItems());
            return host;
        }

        private static MenuTree EightItems()
        {
            var tree = new MenuTree();
            foreach (var name in Names) tree.ExpandAndAddPaths(name, name + "-1");
            return tree;
        }

        private static Point RowPoint(ListBox column, int index)
        {
            var row = column.GetItemRectangle(index);
            return new Point(row.Left + 5, row.Top + 2);
        }

        private static string[] MarkedNames(CascadeColumnsView view) => view.MultiSelectedNodes.Select(node => node.Name).ToArray();
    }
}