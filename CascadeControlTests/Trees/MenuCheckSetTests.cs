using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Trees
{
    [TestClass]
    public class MenuCheckSetTests
    {
        #region 勾選與取消

        [TestMethod]
        public void Check_AddsAncestorsButNotSiblingsOrDescendants()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var jog = Find(set, "Motor", "Axis X", "Jog");

            Assert.IsTrue(set.Check(jog));

            AssertChecked(set, "Motor", "Motor > Axis X", "Motor > Axis X > Jog");
        }

        [TestMethod]
        public void Check_AlreadyChecked_ReturnsFalse()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var jog = Find(set, "Motor", "Axis X", "Jog");
            set.Check(jog);

            Assert.IsFalse(set.Check(jog));
            Assert.IsFalse(set.Check(jog.Parent), "祖先已經補勾過了");
            Assert.AreEqual(1, set.History.UndoCount, "沒有變化的操作不進歷史");
        }

        [TestMethod]
        public void Uncheck_RemovesDescendantsButKeepsAncestors()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.CheckSubtree(Find(set, "Motor", "Axis X"));
            set.Check(Find(set, "Motor", "Axis Y"));

            Assert.IsTrue(set.Uncheck(Find(set, "Motor", "Axis X")));

            AssertChecked(set, "Motor", "Motor > Axis Y");
            Assert.IsFalse(set.Uncheck(Find(set, "Motor", "Axis X")), "已經沒勾了");
        }

        [TestMethod]
        public void Toggle_SwitchesBetweenCheckAndUncheck()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var info = Find(set, "System", "Info");

            Assert.IsTrue(set.Toggle(info));
            AssertChecked(set, "System", "System > Info");

            Assert.IsTrue(set.Toggle(info));
            AssertChecked(set);
        }

        [TestMethod]
        public void Uncheck_UnchecksAncestorsLeftWithoutCheckedChildren()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System", "Info", "Version"));
            set.Check(Find(set, "System", "Reboot"));

            Assert.IsTrue(set.Uncheck(Find(set, "System", "Info", "Version")));
            AssertChecked(set, "System", "System > Reboot");

            Assert.IsTrue(set.Uncheck(Find(set, "System", "Reboot")));
            AssertChecked(set);

            set.History.Undo();
            AssertChecked(set, "System", "System > Reboot");
        }

        [TestMethod]
        public void CheckSubtree_ChecksNodeDescendantsAndAncestors()
        {
            var set = new MenuCheckSet(TestTrees.Template());

            Assert.IsTrue(set.CheckSubtree(Find(set, "System", "Info")));

            AssertChecked(set, "System", "System > Info", "System > Info > Version", "System > Info > Uptime", "System > Info > Memory");
            Assert.IsFalse(set.CheckSubtree(Find(set, "System", "Info")));
        }

        [TestMethod]
        public void CheckSiblings_ChecksWholeLevelAndAncestorsOnly()
        {
            var set = new MenuCheckSet(TestTrees.Template());

            Assert.IsTrue(set.CheckSiblings(Find(set, "System", "Reboot")));

            AssertChecked(set, "System", "System > Reboot", "System > Shutdown", "System > Info");
            Assert.IsFalse(set.CheckSiblings(Find(set, "System", "Info")), "已經全勾，沒有變化");
        }

        [TestMethod]
        public void CheckSiblings_KeepsDescendantsAsTheyWere()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System", "Info", "Version"));

            Assert.IsTrue(set.CheckSiblings(Find(set, "System", "Reboot")));

            AssertChecked(set, "System", "System > Reboot", "System > Shutdown", "System > Info", "System > Info > Version");
        }

        [TestMethod]
        public void CheckSiblingSubtrees_ChecksWholeLevelWithDescendants()
        {
            var set = new MenuCheckSet(TestTrees.Template());

            Assert.IsTrue(set.CheckSiblingSubtrees(Find(set, "System", "Reboot")));

            // System 底下的 Reboot、Shutdown、Info 三棵子樹，加上祖先 System。
            AssertChecked(set,
                "System", "System > Reboot", "System > Shutdown",
                "System > Info", "System > Info > Version", "System > Info > Uptime", "System > Info > Memory");
        }

        [TestMethod]
        public void UncheckSiblings_ClearsWholeLevelAndEmptiedParent()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.CheckSubtree(Find(set, "System"));
            set.Check(Find(set, "Motor"));

            Assert.IsTrue(set.UncheckSiblings(Find(set, "System", "Info")));

            AssertChecked(set, "Motor");
        }

        [TestMethod]
        public void CheckSiblingSubtrees_OnTopLevel_ChecksEverything()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);

            set.CheckSiblingSubtrees(tree.TopLevelNodes[0]);

            Assert.AreEqual(AllNodes(tree).Count, set.Count);
        }

        [TestMethod]
        public void CheckAll_AndUncheckAll()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);

            Assert.IsFalse(set.UncheckAll(), "本來就沒有勾選");
            Assert.IsTrue(set.CheckAll());
            Assert.AreEqual(AllNodes(tree).Count, set.Count);
            Assert.IsFalse(set.CheckAll());

            Assert.IsTrue(set.UncheckAll());
            Assert.AreEqual(0, set.Count);
        }

        [TestMethod]
        public void Mutations_RejectForeignNodesRootAndNull()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);
            var foreign = TestTrees.Template().TopLevelNodes[0];

            Assert.ThrowsException<ArgumentNullException>(() => set.Check(null));
            Assert.ThrowsException<ArgumentException>(() => set.Check(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.Uncheck(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.Toggle(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.CheckSubtree(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.CheckSiblings(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.CheckSiblingSubtrees(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.UncheckSiblings(foreign));
            Assert.ThrowsException<ArgumentException>(() => set.Check(tree.Root));
            Assert.ThrowsException<ArgumentNullException>(() => new MenuCheckSet(null));

            Assert.IsFalse(set.IsChecked(null), "查詢不丟例外");
            Assert.IsFalse(set.IsChecked(foreign));
            Assert.IsFalse(set.IsFullyChecked(foreign));
            Assert.IsFalse(set.Contains(foreign));
            Assert.IsFalse(set.Contains(tree.Root));
            Assert.IsTrue(set.Contains(tree.TopLevelNodes[0]));
        }

        #endregion 勾選與取消

        #region 不變式與查詢

        /// <summary>
        /// 任意一連串操作之後，每個勾選節點的父節點都是勾選的（或是樹根），而且 CheckedNodes 與 Count 一致。
        /// </summary>
        [TestMethod]
        public void AnySequenceOfOperations_KeepsSetUpwardClosed()
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var tree = TestTrees.Random(seed);
                var nodes = AllNodes(tree);
                var set = new MenuCheckSet(tree);
                var rng = new Random(seed);

                for (var step = 0; step < 60; step++)
                {
                    var node = nodes[rng.Next(nodes.Count)];
                    switch (rng.Next(10))
                    {
                        case 0: set.Check(node); break;
                        case 1: set.Uncheck(node); break;
                        case 2: set.Toggle(node); break;
                        case 3: set.CheckSubtree(node); break;
                        case 4: set.CheckSiblings(node); break;
                        case 5: set.UncheckSiblings(node); break;
                        case 6: set.History.Undo(); break;
                        case 7: set.History.Redo(); break;
                        case 8: set.CheckSiblingSubtrees(node); break;
                        default: set.Toggle(node); break;
                    }

                    var checkedNodes = set.CheckedNodes.ToList();
                    Assert.AreEqual(set.Count, checkedNodes.Count, $"seed {seed} step {step}");
                    foreach (var checkedNode in checkedNodes)
                    {
                        Assert.IsTrue(
                            ReferenceEquals(checkedNode.Parent, tree.Root) || set.IsChecked(checkedNode.Parent),
                            $"seed {seed} step {step}：{checkedNode.FullPathText} 的父節點沒有勾選");
                    }
                }
            }
        }

        [TestMethod]
        public void CheckedNodes_AreInTreeOrder()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);

            // 故意用和樹相反的順序勾選。
            set.Check(Find(set, "Motor", "Axis Y", "Speed"));
            set.Check(Find(set, "Motor", "Axis X", "Jog", "Backward"));
            set.Check(Find(set, "System", "Info"));

            var expected = AllNodes(tree).Where(set.IsChecked).Select(node => node.FullPathText).ToArray();
            CollectionAssert.AreEqual(expected, set.CheckedNodes.Select(node => node.FullPathText).ToArray());
            Assert.AreEqual("System", expected[0]);
        }

        [TestMethod]
        public void IsFullyChecked_OnlyWhenSelfAndAllDescendantsAreChecked()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var axisX = Find(set, "Motor", "Axis X");
            var jog = Find(set, "Motor", "Axis X", "Jog");

            Assert.IsFalse(set.IsFullyChecked(axisX), "沒勾選");

            set.CheckSubtree(axisX);
            Assert.IsTrue(set.IsFullyChecked(axisX));
            Assert.IsTrue(set.IsFullyChecked(jog));
            Assert.IsFalse(set.IsFullyChecked(axisX.Parent), "Motor 還有 Axis Y、Axis Z 沒勾");

            // 取消一個很深的葉節點，一路往上都不再是全勾；快取要跟著更新。
            set.Uncheck(Find(set, "Motor", "Axis X", "Jog", "Forward"));
            Assert.IsFalse(set.IsFullyChecked(axisX));
            Assert.IsFalse(set.IsFullyChecked(jog));
            Assert.IsTrue(set.IsFullyChecked(Find(set, "Motor", "Axis X", "Speed")));
            Assert.IsTrue(set.IsFullyChecked(Find(set, "Motor", "Axis X", "Home")), "葉節點勾了就是全勾");

            set.History.Undo();
            Assert.IsTrue(set.IsFullyChecked(axisX), "復原後快取也要更新");
        }

        #endregion 不變式與查詢

        #region 復原與重做

        [TestMethod]
        public void UndoRedo_RestoresEveryStep()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);
            var snapshots = new List<string[]> { Snapshot(set) };

            set.Check(Find(set, "Motor", "Axis X", "Jog"));
            snapshots.Add(Snapshot(set));
            set.CheckSiblingSubtrees(Find(set, "System", "Reboot"));
            snapshots.Add(Snapshot(set));
            set.Uncheck(Find(set, "Motor"));
            snapshots.Add(Snapshot(set));
            set.CheckAll();
            snapshots.Add(Snapshot(set));

            for (var i = snapshots.Count - 2; i >= 0; i--)
            {
                Assert.IsTrue(set.History.CanUndo);
                set.History.Undo();
                CollectionAssert.AreEqual(snapshots[i], Snapshot(set), $"復原到第 {i} 步");
            }

            Assert.IsFalse(set.History.CanUndo);
            Assert.IsNull(set.History.Undo(), "沒有步驟可以復原");

            for (var i = 1; i < snapshots.Count; i++)
            {
                Assert.IsTrue(set.History.CanRedo);
                set.History.Redo();
                CollectionAssert.AreEqual(snapshots[i], Snapshot(set), $"重做到第 {i} 步");
            }

            Assert.IsFalse(set.History.CanRedo);
            Assert.IsNull(set.History.Redo());
        }

        [TestMethod]
        public void UndoRedo_ReturnTargetNode()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var jog = Find(set, "Motor", "Axis X", "Jog");
            set.Check(jog);
            set.CheckAll();

            Assert.IsNull(set.History.Undo().Focus, "針對整棵樹的步驟沒有目標節點");
            Assert.AreSame(jog, set.History.Undo().Focus);
            Assert.AreSame(jog, set.History.Redo().Focus);
        }

        [TestMethod]
        public void NewOperation_ClearsRedo()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System"));
            set.Check(Find(set, "Motor"));
            set.History.Undo();
            Assert.IsTrue(set.History.CanRedo);

            set.Check(Find(set, "IO"));

            Assert.IsFalse(set.History.CanRedo);
            AssertChecked(set, "System", "IO");
        }

        [TestMethod]
        public void NoOpOperation_DoesNotClearRedoOrEnterHistory()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System"));
            set.Check(Find(set, "Motor"));
            set.History.Undo();

            Assert.IsFalse(set.Check(Find(set, "System")));

            Assert.AreEqual(1, set.History.UndoCount);
            Assert.AreEqual(1, set.History.RedoCount, "沒有變化的操作不可以清掉 redo");
        }

        [TestMethod]
        public void History_KeepsOnlyTheLatestFiftySteps()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths(TestTrees.Alternatives("N", 60));
            var set = new MenuCheckSet(tree);

            for (var i = 0; i < 51; i++) set.Check(tree.TopLevelNodes[i]);
            Assert.AreEqual(MenuHistory.DEFAULT_CAPACITY, set.History.UndoCount);

            var undone = 0;
            while (set.History.CanUndo)
            {
                set.History.Undo();
                undone++;
            }

            Assert.AreEqual(50, undone);
            AssertChecked(set, "N1");   // 最舊的那一步（勾選 N1）已經被丟掉，退不回去
        }

        [TestMethod]
        public void HistoryCapacity_ShrinkingDropsOldestSteps()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("A|B|C|D");
            var set = new MenuCheckSet(tree);
            foreach (var node in tree.TopLevelNodes) set.Check(node);

            set.History.Capacity = 2;
            Assert.AreEqual(2, set.History.UndoCount);

            set.History.Undo();
            set.History.Undo();
            AssertChecked(set, "A", "B");

            set.History.Capacity = 0;
            set.Check(tree.TopLevelNodes[3]);
            Assert.IsFalse(set.History.CanUndo, "容量為 0 時不保留歷史");
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => set.History.Capacity = -1);
        }

        [TestMethod]
        public void ClearHistory_KeepsCheckedState()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System"));
            set.Check(Find(set, "Motor"));
            set.History.Undo();

            set.History.Clear();

            Assert.IsFalse(set.History.CanUndo);
            Assert.IsFalse(set.History.CanRedo);
            AssertChecked(set, "System");
        }

        #endregion 復原與重做

        #region 事件

        [TestMethod]
        public void Changed_ReportsDifferenceTargetAndReason()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var events = new List<MenuCheckChangedEventArgs>();
            set.Changed += (sender, e) => events.Add(e);

            var jog = Find(set, "Motor", "Axis X", "Jog");
            set.Check(jog);
            set.Check(jog);                       // 沒有變化，不觸發
            set.Uncheck(jog.Parent);
            set.History.Undo();
            set.History.Redo();
            set.UncheckAll();                     // Uncheck 已經連同 Motor 一起取消，沒有變化，不觸發

            Assert.AreEqual(4, events.Count);

            CollectionAssert.AreEqual(new[] { "Motor", "Motor > Axis X", "Motor > Axis X > Jog" }, Paths(events[0].Added));
            Assert.AreEqual(0, events[0].Removed.Count);
            Assert.AreSame(jog, events[0].Target);
            Assert.AreEqual(MenuCheckChangeReason.User, events[0].Reason);

            CollectionAssert.AreEqual(new[] { "Motor > Axis X", "Motor > Axis X > Jog", "Motor" }, Paths(events[1].Removed));

            Assert.AreEqual(MenuCheckChangeReason.Undo, events[2].Reason);
            CollectionAssert.AreEqual(new[] { "Motor > Axis X", "Motor > Axis X > Jog", "Motor" }, Paths(events[2].Added));
            Assert.AreSame(jog.Parent, events[2].Target);

            Assert.AreEqual(MenuCheckChangeReason.Redo, events[3].Reason);
            CollectionAssert.AreEqual(new[] { "Motor > Axis X", "Motor > Axis X > Jog", "Motor" }, Paths(events[3].Removed));
        }

        #endregion 事件

        #region 輸出與還原

        [TestMethod]
        public void ToTree_ContainsOnlyCheckedNodesInOriginalOrder()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "Motor", "Axis Y", "Jog", "Backward"));
            set.Check(Find(set, "Motor", "Axis X"));      // 只勾到這一層
            set.CheckSubtree(Find(set, "System", "Info"));

            var result = set.ToTree();

            CollectionAssert.AreEqual(
                new[]
                {
                    "System > Info > Version", "System > Info > Uptime", "System > Info > Memory",
                    "Motor > Axis X",
                    "Motor > Axis Y > Jog > Backward",
                },
                TestTrees.LeafPaths(result));
        }

        [TestMethod]
        public void ToTree_NothingChecked_IsEmpty()
        {
            Assert.IsTrue(new MenuCheckSet(TestTrees.Template()).ToTree().IsEmpty);
        }

        [DataTestMethod]
        [DataRow(MenuRowStyle.Expanded)]
        [DataRow(MenuRowStyle.Compact)]
        [DataRow(MenuRowStyle.Outline)]
        public void ToTree_RoundTripsThroughRowsAndApply(MenuRowStyle style)
        {
            for (var seed = 0; seed < 25; seed++)
            {
                var tree = TestTrees.Random(seed);
                var nodes = AllNodes(tree);
                var rng = new Random(seed + 1000);

                var source = new MenuCheckSet(tree);
                for (var i = 0; i < 6; i++)
                {
                    var node = nodes[rng.Next(nodes.Count)];
                    if (rng.Next(2) == 0) source.Check(node);
                    else source.CheckSubtree(node);
                }

                var rows = source.ToTree().ToRows(style);
                var readBack = MenuTree.FromRows(rows, out IList<string> readWarnings);
                Assert.AreEqual(0, readWarnings.Count, $"seed {seed}：{string.Join(" / ", readWarnings)}");

                var restored = new MenuCheckSet(tree);
                restored.Apply(readBack, out IList<string> applyWarnings);

                Assert.AreEqual(0, applyWarnings.Count, $"seed {seed}：{string.Join(" / ", applyWarnings)}");
                CollectionAssert.AreEqual(Snapshot(source), Snapshot(restored), $"seed {seed}，{style}");
            }
        }

        /// <summary>
        /// 只勾到中間節點（下面沒有勾）時，Expanded 與 Compact 也要能分辨「勾到這一層」和「勾到更深」。
        /// </summary>
        [DataTestMethod]
        [DataRow(MenuRowStyle.Expanded)]
        [DataRow(MenuRowStyle.Compact)]
        [DataRow(MenuRowStyle.Outline)]
        public void ToTree_MiddleNodeCheckedAlone_RoundTrips(MenuRowStyle style)
        {
            var tree = TestTrees.Template();
            var source = new MenuCheckSet(tree);
            source.Check(Find(source, "Motor", "Axis X"));
            source.Check(Find(source, "Motor", "Axis Y", "Jog", "Forward"));

            var readBack = MenuTree.FromRows(source.ToTree().ToRows(style), out IList<string> _);
            var restored = new MenuCheckSet(tree);
            restored.Apply(readBack, out IList<string> warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(Snapshot(source), Snapshot(restored));
        }

        [TestMethod]
        public void Apply_ReplacesStateAndIsOneUndoStep()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.CheckSubtree(Find(set, "System"));
            var before = Snapshot(set);

            var subset = new MenuTree();
            subset.AddPath("Motor", "Axis Z", "Home");

            Assert.IsTrue(set.Apply(subset, out IList<string> warnings));

            Assert.AreEqual(0, warnings.Count);
            AssertChecked(set, "Motor", "Motor > Axis Z", "Motor > Axis Z > Home");
            Assert.AreEqual(2, set.History.UndoCount, "整個 Apply 算一步");

            Assert.IsNull(set.History.Undo().Focus, "Apply 針對整棵樹，沒有目標節點");
            CollectionAssert.AreEqual(before, Snapshot(set));

            Assert.IsFalse(new MenuCheckSet(TestTrees.Template()).Apply(new MenuTree(), out warnings), "空的套到空的，沒有變化");
            Assert.ThrowsException<ArgumentNullException>(() => set.Apply(null, out warnings));
        }

        [TestMethod]
        public void Apply_UnknownPaths_ProduceWarnings()
        {
            var set = new MenuCheckSet(TestTrees.Template());

            var subset = new MenuTree();
            subset.AddPath("Motor", "Axis X", "Home");
            subset.AddPath("Motor", "Axis W", "Home");
            subset.AddPath("Motor", "Axis W", "Jog");
            subset.AddPath("Nowhere");

            set.Apply(subset, out IList<string> warnings);

            AssertChecked(set, "Motor", "Motor > Axis X", "Motor > Axis X > Home");
            Assert.AreEqual(2, warnings.Count, string.Join(" / ", warnings));
            StringAssert.Contains(warnings[0], "Motor > Axis W");
            StringAssert.Contains(warnings[0], "2 個節點");
            StringAssert.Contains(warnings[1], "Nowhere");
        }

        #endregion 輸出與還原

        #region Reconcile

        [TestMethod]
        public void Reconcile_DropsDetachedNodesAndClearsHistory()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            var axisX = Find(set, "Motor", "Axis X");
            set.CheckSubtree(axisX);
            set.Check(Find(set, "System"));

            var events = new List<MenuCheckChangedEventArgs>();
            set.Changed += (sender, e) => events.Add(e);

            Assert.IsTrue(axisX.RemoveFromParent());
            Assert.IsTrue(set.Reconcile());

            AssertChecked(set, "System", "Motor");
            Assert.IsFalse(set.History.CanUndo, "歷史指向已經不在樹裡的節點，一律清除");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(MenuCheckChangeReason.Reconcile, events[0].Reason);
            Assert.IsTrue(events[0].Removed.Contains(axisX));

            Assert.IsFalse(set.Reconcile(), "再呼叫一次沒有變化");
        }

        [TestMethod]
        public void Reconcile_NoStructuralChange_StillClearsHistory()
        {
            var set = new MenuCheckSet(TestTrees.Template());
            set.Check(Find(set, "System"));

            Assert.IsFalse(set.Reconcile());

            Assert.IsFalse(set.History.CanUndo);
            AssertChecked(set, "System");
        }

        [TestMethod]
        public void Reconcile_NewChildUnderFullyCheckedNode_MakesItPartial()
        {
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree);
            var info = Find(set, "System", "Info");
            set.CheckSubtree(info);
            Assert.IsTrue(set.IsFullyChecked(info));

            tree.AddPath("System", "Info", "Serial");
            set.Reconcile();

            Assert.IsFalse(set.IsFullyChecked(info), "新加入的節點預設不勾選");
            Assert.IsTrue(set.IsChecked(info));
        }

        #endregion Reconcile

        private static MenuNode Find(MenuCheckSet set, params string[] names)
        {
            var node = set.Tree.Root;
            foreach (var name in names)
            {
                Assert.IsTrue(node.TryGetChild(name, out node), $"找不到 {string.Join(" > ", names)}");
            }

            return node;
        }

        // 整棵樹的所有節點，前序。
        private static List<MenuNode> AllNodes(MenuTree tree)
        {
            var nodes = new List<MenuNode>();
            var stack = new Stack<MenuNode>(tree.TopLevelNodes.Reverse());

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                nodes.Add(node);
                foreach (var child in node.Children.Reverse()) stack.Push(child);
            }

            return nodes;
        }

        private static string[] Snapshot(MenuCheckSet set) => set.CheckedNodes.Select(node => node.FullPathText).ToArray();

        private static string[] Paths(IEnumerable<MenuNode> nodes) => nodes.Select(node => node.FullPathText).ToArray();

        private static void AssertChecked(MenuCheckSet set, params string[] expectedPaths)
        {
            CollectionAssert.AreEquivalent(expectedPaths, Snapshot(set));
        }
    }
}