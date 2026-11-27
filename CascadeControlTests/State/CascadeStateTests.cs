using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.State
{
    [TestClass]
    public class CascadeStateTests
    {
        [TestMethod]
        public void SetTreeAndClear_UpdatesTreeTopLevelNodesAndNotifiesInOrder()
        {
            var state = new CascadeState();
            var tree = TestTrees.Template();
            var sequence = new List<string>();
            var events = new List<CascadeNodeEventArgs>();

            state.Invalidated += kind => sequence.Add($"Invalidated:{kind}");
            state.SelectedPathChanged += (sender, e) =>
            {
                sequence.Add("SelectedPathChanged");
                events.Add(e);
            };

            state.SetTree(tree);

            Assert.AreSame(tree, state.Tree);
            CollectionAssert.AreEqual(tree.TopLevelNodes.ToList(), state.TopLevelNodes.ToList());
            Assert.IsNull(state.SelectedNode);
            Assert.AreEqual(0, state.SelectedPath.Count);
            Assert.AreEqual(string.Empty, state.SelectedPathText);
            Assert.IsFalse(state.IsLeafSelected);
            Assert.AreEqual(0, state.ActiveColumnIndex);
            CollectionAssert.AreEqual(new[] { "Invalidated:Reset", "SelectedPathChanged" }, sequence);
            Assert.IsNull(events[0].Node);
            CollectionAssert.AreEqual(tree.TopLevelNodes.ToList(), events[0].Children.ToList());

            state.Clear();
            Assert.IsNull(state.Tree);
            Assert.AreEqual(0, state.TopLevelNodes.Count);
            Assert.AreEqual(2, events.Count);
        }

        [TestMethod]
        public void SetTopLevelNodes_ClearsTreeAndSupportsCustomNodes()
        {
            var state = new CascadeState();
            var child = new CascadeNode("Child", "Child");
            var root = new CascadeNode("Root", "Root", children: new[] { child });

            state.SetTree(TestTrees.Template());
            state.SetTopLevelNodes(new ICascadeNode[] { root });

            Assert.IsNull(state.Tree);
            CollectionAssert.AreEqual(new ICascadeNode[] { root }, state.TopLevelNodes.ToList());
            Assert.IsTrue(state.TrySelectPath(new ICascadeNode[] { root, child }));
            Assert.AreSame(child, state.SelectedNode);
            Assert.IsTrue(state.IsLeafSelected);
            Assert.AreEqual("Root => Child", state.SelectedPathText);
        }

        [TestMethod]
        public void TrySelectNodeAndPath_HandlesFullPartialAndNullInputs()
        {
            var state = new CascadeState { PathSeparator = " / " };
            var tree = TestTrees.Template();
            state.SetTree(tree);

            var motor = tree.TopLevelNodes[1];
            var axisX = motor.Children[0];
            var home = axisX.Children[0];

            Assert.IsTrue(state.TrySelectNode(home));
            Assert.AreSame(home, state.SelectedNode);
            Assert.AreEqual("Motor / Axis X / Home", state.SelectedPathText);
            Assert.IsTrue(state.IsLeafSelected);
            Assert.AreEqual(2, state.ActiveColumnIndex);

            var missing = new CascadeNode("Missing", "Missing");
            Assert.IsFalse(state.TrySelectPath(new ICascadeNode[] { motor, missing }));
            Assert.AreSame(motor, state.SelectedNode, "中途找不到時停在已比對成功的那一段");
            Assert.IsFalse(state.IsLeafSelected);
            Assert.AreEqual(0, state.ActiveColumnIndex);

            Assert.ThrowsException<ArgumentNullException>(() => state.TrySelectNode(null));
            Assert.ThrowsException<ArgumentNullException>(() => state.TrySelectPath(null));
        }

        [TestMethod]
        public void StepBackStepForwardAndClearTrail_FollowTrailMemoryRules()
        {
            var state = new CascadeState();
            var tree = TestTrees.Template();
            state.SetTree(tree);

            var motor = tree.TopLevelNodes[1];
            var axisY = motor.Children[1];
            state.TrySelectNode(axisY);

            var pathEvents = 0;
            var invalidations = new List<CascadeStateInvalidation>();
            state.SelectedPathChanged += (sender, e) => pathEvents++;
            state.Invalidated += invalidations.Add;

            Assert.IsTrue(state.StepBack());
            Assert.AreSame(motor, state.SelectedNode);
            Assert.IsFalse(state.StepBack(), "第一層再退回不動作也不觸發事件");
            Assert.AreEqual(1, pathEvents);

            Assert.IsTrue(state.StepForward());
            Assert.AreSame(axisY, state.SelectedNode, "沿記憶走回上次的子節點");
            Assert.AreEqual(2, pathEvents);

            Assert.IsTrue(state.StepBack());
            state.ClearTrail();
            Assert.AreEqual(3, pathEvents, "ClearTrail 不觸發 SelectedPathChanged");
            Assert.AreEqual(CascadeStateInvalidation.TrailCleared, invalidations.Last());

            Assert.IsTrue(state.StepForward());
            Assert.AreSame(motor.Children[0], state.SelectedNode, "清除記憶後前進選第一個子節點");
        }

        [TestMethod]
        public void Refresh_ReReadsSourceRotatesKeyAndPreservesOrReplacesSelection()
        {
            var state = new CascadeState();
            var tree = TestTrees.Template();
            state.SetTree(tree);

            var system = tree.TopLevelNodes[0];
            var reboot = system.Children[0];
            state.TrySelectNode(reboot);

            var countBefore = state.TopLevelNodes.Count;
            var keyBefore = state.TopLevelKey;
            var invalidations = new List<CascadeStateInvalidation>();
            var events = new List<CascadeNodeEventArgs>();
            state.Invalidated += invalidations.Add;
            state.SelectedPathChanged += (sender, e) => events.Add(e);

            tree.AddPath("Extra");
            state.Refresh();

            Assert.AreNotSame(keyBefore, state.TopLevelKey);
            Assert.AreEqual(countBefore + 1, state.TopLevelNodes.Count);
            Assert.AreEqual("Extra", state.TopLevelNodes[state.TopLevelNodes.Count - 1].Name);
            Assert.AreSame(reboot, state.SelectedNode);
            CollectionAssert.AreEqual(new[] { CascadeStateInvalidation.Refreshed }, invalidations);
            Assert.AreEqual(1, events.Count);

            state.Refresh(new ICascadeNode[0]);
            Assert.IsNull(state.SelectedNode);
            Assert.AreEqual(2, events.Count);
        }

        [TestMethod]
        public void CheckableState_SetTreeAtomicallyReplacesCheckSetBeforeSelectedPathChanged()
        {
            var state = new CascadeCheckableState();
            var tree = TestTrees.Template();

            MenuCheckSet checkSetDuringEvent = null;
            var checkSetChanges = 0;
            var checkedEvents = 0;
            state.CheckSetChanged += (sender, e) => checkSetChanges++;
            state.CheckedChanged += (sender, e) => checkedEvents++;
            state.SelectedPathChanged += (sender, e) => checkSetDuringEvent = state.CheckSet;

            state.SetTree(tree);

            Assert.IsNotNull(state.CheckSet);
            Assert.AreSame(tree, state.CheckSet.Tree);
            Assert.AreSame(state.CheckSet, checkSetDuringEvent, "SelectedPathChanged 觸發時 CheckSet 已換好");
            Assert.AreEqual(1, checkSetChanges);
            Assert.AreEqual(0, checkedEvents, "更換 CheckSet 不觸發 CheckedChanged");

            var firstSet = state.CheckSet;
            firstSet.Check(tree.TopLevelNodes[0]);
            Assert.AreEqual(1, checkedEvents);

            var replacement = new MenuCheckSet(tree);
            state.CheckSet = replacement;
            Assert.AreEqual(2, checkSetChanges);
            Assert.AreEqual(1, checkedEvents, "換上另一份不觸發 CheckedChanged");

            firstSet.Check(tree.TopLevelNodes[1]);
            Assert.AreEqual(1, checkedEvents, "舊的 CheckSet 已取消訂閱");

            replacement.Check(tree.TopLevelNodes[1]);
            Assert.AreEqual(2, checkedEvents);

            state.SetTopLevelNodes(new ICascadeNode[0]);
            Assert.IsNull(state.CheckSet);
            Assert.AreEqual(3, checkSetChanges);
        }

        [TestMethod]
        public void History_ClearedBySetTreeSetTopLevelNodesAndClear()
        {
            var state = new CascadeState();
            var tree = TestTrees.Template();
            var set = new MenuCheckSet(tree) { History = state.History };

            state.SetTree(tree);
            set.Check(tree.TopLevelNodes[0]);
            state.SetTree(tree);
            Assert.IsFalse(state.History.CanUndo, "SetTree 清空");

            set.Check(tree.TopLevelNodes[1]);
            state.SetTopLevelNodes(tree.TopLevelNodes);
            Assert.IsFalse(state.History.CanUndo, "SetTopLevelNodes 清空");

            set.Check(tree.TopLevelNodes[2]);
            state.Clear();
            Assert.IsFalse(state.History.CanUndo, "Clear 清空");

            set.Uncheck(tree.TopLevelNodes[2]);
            state.Refresh();
            Assert.IsTrue(state.History.CanUndo, "Refresh 是編輯之後的重整，不清空");
        }

        [TestMethod]
        public void CheckableState_CheckSetRecordsIntoStateHistory_AndReleasesTheReplacedOne()
        {
            var state = new CascadeCheckableState();
            var tree = TestTrees.Template();
            state.SetTree(tree);
            var firstSet = state.CheckSet;
            Assert.AreSame(state.History, firstSet.History);

            firstSet.Check(tree.TopLevelNodes[0]);
            Assert.AreEqual(1, state.History.UndoCount);

            var replacement = new MenuCheckSet(tree);
            state.CheckSet = replacement;
            Assert.AreSame(state.History, replacement.History);
            Assert.AreNotSame(state.History, firstSet.History, "換下來的那一份改回自己的歷史");
            Assert.IsFalse(state.History.CanUndo, "舊步驟作用在換下來的那一份，換的時候清空");

            firstSet.Check(tree.TopLevelNodes[1]);
            Assert.IsFalse(state.History.CanUndo, "換下來的那一份不再記進 State 的歷史");

            state.CheckSet = null;
            Assert.AreNotSame(state.History, replacement.History);
        }
    }
}