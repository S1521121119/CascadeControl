using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Trees
{
    /// <summary>
    /// <see cref="MenuHistory"/>：樹的編輯與勾選排在同一條時間軸上，復原時接回的是同一批節點物件。
    /// 控制項記錄步驟的方式（先改樹，再 Record*）在這裡直接模擬。
    /// </summary>
    [TestClass]
    public class MenuHistoryTests
    {
        #region 樹的編輯

        [TestMethod]
        public void Insert_UndoRemovesSameNode_RedoPutsItBack()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var motor = Find(tree, "Motor");
            var before = TestTrees.Fingerprint(tree);

            var added = motor.InsertChildAt(1, new MenuNode("Axis W"));
            history.RecordInsert(tree, motor, new[] { added });
            var after = TestTrees.Fingerprint(tree);

            var undo = history.Undo();
            Assert.AreEqual(before, TestTrees.Fingerprint(tree));
            Assert.IsNull(added.Parent);
            Assert.IsTrue(undo.TreeChanged);
            CollectionAssert.AreEqual(new[] { added }, undo.Nodes.ToList());
            Assert.AreSame(Find(tree, "Motor", "Axis Y"), undo.Focus, "選原位置的下一個兄弟");

            var redo = history.Redo();
            Assert.AreEqual(after, TestTrees.Fingerprint(tree));
            Assert.AreSame(added, motor.Children[1], "接回的是同一個節點物件");
            Assert.AreSame(added, redo.Focus);
        }

        [TestMethod]
        public void Remove_SeveralSiblings_UndoRestoresPositionsAndSubtrees()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var motor = Find(tree, "Motor");
            var axisX = Find(tree, "Motor", "Axis X");
            var axisZ = Find(tree, "Motor", "Axis Z");
            var before = TestTrees.Fingerprint(tree);

            var removed = new[] { axisX, axisZ }.Select(node => new KeyValuePair<int, MenuNode>(node.SiblingNumber - 1, node)).ToList();
            axisZ.RemoveFromParent();
            axisX.RemoveFromParent();
            history.RecordRemove(tree, motor, removed);

            var undo = history.Undo();
            Assert.AreEqual(before, TestTrees.Fingerprint(tree), "位置、序號與子樹都回到原狀");
            Assert.AreSame(axisX, motor.Children[0]);
            Assert.AreSame(axisZ, motor.Children[2]);
            Assert.AreEqual(3, axisZ.SiblingNumber);
            Assert.AreEqual(4, Find(tree, "Motor", "Axis Z", "Jog", "Forward").Depth, "接回後深度重新計算");
            Assert.AreSame(axisX, undo.Focus, "選第一個回來的節點");
            CollectionAssert.AreEqual(new[] { axisX, axisZ }, undo.Nodes.ToList(), "依位置排列");

            var redo = history.Redo();
            CollectionAssert.AreEqual(new[] { "Axis Y" }, motor.Children.Select(node => node.Name).ToList());
            Assert.AreSame(motor.Children[0], redo.Focus);
        }

        [TestMethod]
        public void Remove_LastChildren_FocusFallsBackToParentOrNothing()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var info = Find(tree, "System", "Info");
            var children = info.Children.Select(node => new KeyValuePair<int, MenuNode>(node.SiblingNumber - 1, node)).ToList();

            foreach (var entry in children) entry.Value.RemoveFromParent();
            history.RecordRemove(tree, info, children);

            history.Undo();
            Assert.AreEqual(3, info.Children.Count);
            Assert.AreSame(info, history.Redo().Focus, "一個子節點都不剩時選父節點");

            var top = new MenuTree();
            top.AddPath("Only");
            var only = top.TopLevelNodes[0];
            only.RemoveFromParent();
            history.Clear();
            history.RecordRemove(top, top.Root, new[] { new KeyValuePair<int, MenuNode>(0, only) });
            history.Undo();
            Assert.IsNull(history.Redo().Focus, "第一層全部移除時沒有可以選的節點");
        }

        [TestMethod]
        public void Rename_UndoRedoSwitchNames()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var reboot = Find(tree, "System", "Reboot");

            reboot.Rename("Restart");
            history.RecordRename(tree, reboot, "Reboot");

            var undo = history.Undo();
            Assert.AreEqual("Reboot", reboot.Name);
            Assert.IsTrue(Find(tree, "System").ContainsChild("Reboot"));
            Assert.AreSame(reboot, undo.Focus);

            history.Redo();
            Assert.AreEqual("Restart", reboot.Name);

            history.RecordRename(tree, reboot, "Restart");
            Assert.AreEqual(1, history.UndoCount, "名稱沒有變不算一步");
        }

        #endregion 樹的編輯

        #region 和勾選共用時間軸

        [TestMethod]
        public void ChecksAndTreeEdits_UndoInTheOrderTheyHappened()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var set = new MenuCheckSet(tree) { History = history };
            var motor = Find(tree, "Motor");
            var reboot = Find(tree, "System", "Reboot");

            set.Check(reboot);                                  // 1. 勾選
            reboot.Rename("Restart");                           // 2. 改名
            history.RecordRename(tree, reboot, "Reboot");
            set.Check(motor);                                   // 3. 勾選

            Assert.AreEqual(3, history.UndoCount);

            Assert.IsFalse(history.Undo().TreeChanged);
            Assert.IsFalse(set.IsChecked(motor));
            Assert.AreEqual("Restart", reboot.Name, "改名在勾選之前，還沒退回");

            Assert.IsTrue(history.Undo().TreeChanged);
            Assert.AreEqual("Reboot", reboot.Name);
            Assert.IsTrue(set.IsChecked(reboot), "改名沒有換節點，勾選維持");

            Assert.AreSame(reboot, history.Undo().Focus);
            Assert.AreEqual(0, set.Count);
        }

        [TestMethod]
        public void Group_RemoveWithUncheck_IsOneStepAndRestoresChecks()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var set = new MenuCheckSet(tree) { History = history };
            var motor = Find(tree, "Motor");
            var axisX = Find(tree, "Motor", "Axis X");
            set.CheckSubtree(axisX);
            var checkedBefore = set.CheckedNodes.ToList();

            // 控制項刪除節點時的順序：移除、記錄、取消被移除節點的勾選，全部在同一組裡。
            using (history.BeginGroup())
            {
                var removed = new[] { new KeyValuePair<int, MenuNode>(axisX.SiblingNumber - 1, axisX) };
                axisX.RemoveFromParent();
                history.RecordRemove(tree, motor, removed);
                Assert.IsTrue(set.UncheckRemoved(new[] { axisX }));
            }

            Assert.AreEqual(2, history.UndoCount, "勾選一步，刪除連同取消勾選一步");
            CollectionAssert.AreEqual(new[] { "Motor" }, set.CheckedNodes.Select(node => node.FullPathText).ToList());
            Assert.IsFalse(set.IsChecked(axisX), "被移除的節點不再算勾選");

            var undo = history.Undo();
            Assert.IsTrue(undo.TreeChanged);
            Assert.AreSame(axisX, undo.Focus);
            CollectionAssert.AreEqual(checkedBefore, set.CheckedNodes.ToList(), "勾選和節點一起回來");

            history.Redo();
            Assert.IsNull(axisX.Parent);
            Assert.IsFalse(set.IsChecked(axisX));
        }

        [TestMethod]
        public void CheckSet_OwnHistoryByDefault()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            Assert.IsNotNull(set.History);

            set.Check(set.Tree.TopLevelNodes[0]);
            Assert.AreEqual(1, set.History.UndoCount);
            Assert.ThrowsException<ArgumentNullException>(() => set.History = null);
        }

        #endregion 和勾選共用時間軸

        #region 容量、清除與對不上的樹

        [TestMethod]
        public void Capacity_KeepsLatestFiftyByDefault_AndShrinksImmediately()
        {
            var tree = new MenuTree();
            var history = new MenuHistory();
            Assert.AreEqual(MenuHistory.DEFAULT_CAPACITY, history.Capacity);
            Assert.AreEqual(50, MenuHistory.DEFAULT_CAPACITY);

            for (var i = 0; i < 55; i++)
            {
                var node = tree.Root.InsertChildAt(i, new MenuNode("N" + i));
                history.RecordInsert(tree, tree.Root, new[] { node });
            }

            Assert.AreEqual(50, history.UndoCount);
            while (history.CanUndo) history.Undo();
            Assert.AreEqual(5, tree.TopLevelNodes.Count, "最舊的 5 步已經丟掉，退不回去");

            history.Capacity = 2;
            Assert.AreEqual(2, history.RedoCount);
            history.Capacity = 0;
            Assert.IsFalse(history.CanRedo);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => history.Capacity = -1);
        }

        [TestMethod]
        public void NewStep_ClearsRedo()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var set = new MenuCheckSet(tree) { History = history };
            set.Check(Find(tree, "System"));
            history.Undo();
            Assert.IsTrue(history.CanRedo);

            var node = tree.Root.InsertChildAt(0, new MenuNode("New"));
            history.RecordInsert(tree, tree.Root, new[] { node });

            Assert.IsFalse(history.CanRedo, "改樹也算新的操作，重做清掉");
        }

        [TestMethod]
        public void TreeChangedOutsideHistory_UndoClearsEverythingAndReturnsNull()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var set = new MenuCheckSet(tree) { History = history };
            set.Check(Find(tree, "System"));

            var axisX = Find(tree, "Motor", "Axis X");
            var removed = new[] { new KeyValuePair<int, MenuNode>(0, axisX) };
            axisX.RemoveFromParent();
            history.RecordRemove(tree, Find(tree, "Motor"), removed);

            // 沒有經過記錄就在原位置放了一個同名節點：接不回去了。
            Find(tree, "Motor").InsertChildAt(0, new MenuNode("Axis X"));

            Assert.IsNull(history.Undo());
            Assert.IsFalse(history.CanUndo, "整份歷史都清掉，連更早的勾選步驟也不留");
            Assert.IsNull(axisX.Parent);
            Assert.IsTrue(set.IsChecked(Find(tree, "System")), "清除歷史不改勾選");
        }

        [TestMethod]
        public void Reconcile_ClearsSharedHistory()
        {
            var tree = TestTrees.Template();
            var history = new MenuHistory();
            var set = new MenuCheckSet(tree) { History = history };
            var node = tree.Root.InsertChildAt(0, new MenuNode("New"));
            history.RecordInsert(tree, tree.Root, new[] { node });

            set.Reconcile();

            Assert.IsFalse(history.CanUndo, "在程式裡直接改樹之後呼叫 Reconcile，樹的步驟也一起清掉");
        }

        #endregion 容量、清除與對不上的樹

        private static MenuNode Find(MenuTree tree, params string[] names)
        {
            var node = tree.Root;
            foreach (var name in names)
            {
                Assert.IsTrue(node.TryGetChild(name, out node), $"找不到 {string.Join(" > ", names)}");
            }

            return node;
        }
    }
}