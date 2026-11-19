using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Views
{
    /// <summary>
    /// <see cref="CascadeNavigator"/> 的導覽規則：選取深度，以及逐節點記住的「上次選的子節點」。
    /// </summary>
    [TestClass]
    public class CascadeNavigatorTests
    {
        // A > B > C > D（D 是葉節點），B 另有兄弟 B2，C 另有兄弟 C2。
        private CascadeNode _a, _b, _b2, _c, _c2, _d;

        [TestInitialize]
        public void Setup()
        {
            _d = new CascadeNode("D", "D");
            _c = new CascadeNode("C", "C", children: new[] { _d });
            _c2 = new CascadeNode("C2", "C2");
            _b = new CascadeNode("B", "B", children: new[] { _c, _c2 });
            _b2 = new CascadeNode("B2", "B2");
            _a = new CascadeNode("A", "A", children: new[] { _b, _b2 });
        }

        private CascadeNavigator WalkToD()
        {
            var nav = new CascadeNavigator();
            nav.SelectNodeAt(0, _a);
            nav.SelectNodeAt(1, _b);
            nav.SelectNodeAt(2, _c);
            nav.SelectNodeAt(3, _d);
            return nav;
        }

        private static string Names(CascadeNavigator nav) => string.Join(">", nav.SelectedPath.Select(i => i.Name));

        private static string PathWithTrailNames(CascadeNavigator nav) => string.Join(">", nav.PathWithTrail.Select(i => i.Name));

        [TestMethod]
        public void New_IsEmpty()
        {
            var nav = new CascadeNavigator();

            Assert.AreEqual(0, nav.SelectedDepth);
            Assert.IsNull(nav.SelectedNode);
            Assert.AreEqual(0, nav.ActiveColumnIndex);
            Assert.AreEqual(0, nav.PathWithTrail.Count);
        }

        [TestMethod]
        public void SelectNodeAt_ExtendsSelection()
        {
            var nav = WalkToD();

            Assert.AreEqual(4, nav.SelectedDepth);
            Assert.AreEqual("A>B>C>D", Names(nav));
            Assert.AreSame(_d, nav.SelectedNode);
            Assert.AreEqual(3, nav.ActiveColumnIndex);
        }

        [TestMethod]
        public void StepBack_KeepsDeeperPartAsTrail()
        {
            var nav = WalkToD();

            Assert.IsTrue(nav.StepBack());
            Assert.IsTrue(nav.StepBack());

            Assert.AreEqual("A>B", Names(nav));
            Assert.AreEqual("A>B>C>D", PathWithTrailNames(nav));
            Assert.AreSame(_c, nav.GetTrailNodeAt(2));
            Assert.AreSame(_d, nav.GetTrailNodeAt(3));
            Assert.IsNull(nav.GetTrailNodeAt(1), "選取範圍內的欄位沒有記憶項目。");
            Assert.IsNull(nav.GetTrailNodeAt(4));
        }

        [TestMethod]
        public void StepBack_StopsAtFirstColumn()
        {
            var nav = WalkToD();
            while (nav.StepBack()) { }

            Assert.AreEqual(1, nav.SelectedDepth);
            Assert.IsFalse(nav.StepBack());
            Assert.IsFalse(new CascadeNavigator().StepBack());
        }

        [TestMethod]
        public void StepForward_FollowsTrail()
        {
            var nav = WalkToD();
            nav.StepBack();
            nav.StepBack();

            Assert.IsTrue(nav.StepForward());
            Assert.AreEqual("A>B>C", Names(nav));
            Assert.IsTrue(nav.StepForward());
            Assert.AreEqual("A>B>C>D", Names(nav));
        }

        [TestMethod]
        public void StepForward_WithoutTrail_SelectsFirstChild()
        {
            var nav = new CascadeNavigator();
            nav.SelectNodeAt(0, _a);

            Assert.IsTrue(nav.StepForward());
            Assert.AreEqual("A>B", Names(nav));
        }

        [TestMethod]
        public void StepForward_AtLeafOrWithoutAnything_DoesNothing()
        {
            Assert.IsFalse(WalkToD().StepForward());
            Assert.IsFalse(new CascadeNavigator().StepForward());
        }

        [TestMethod]
        public void SelectNodeAt_SameNodeInPath_OnlyMovesDepth()
        {
            var nav = WalkToD();

            Assert.IsTrue(nav.SelectNodeAt(1, _b), "點選路徑上的同一項等同連按 ←。");
            Assert.AreEqual("A>B", Names(nav));
            Assert.AreEqual("A>B>C>D", PathWithTrailNames(nav));

            Assert.IsTrue(nav.SelectNodeAt(3, _d), "點選記憶中的同一項等同連按 →。");
            Assert.AreEqual("A>B>C>D", Names(nav));
        }

        [TestMethod]
        public void SelectNodeAt_SameNodeAtActiveColumn_ReturnsFalse()
        {
            Assert.IsFalse(WalkToD().SelectNodeAt(3, _d));
        }

        [TestMethod]
        public void SelectNodeAt_DifferentNode_HidesButKeepsOtherBranch()
        {
            var nav = WalkToD();
            nav.StepBack();
            nav.StepBack();

            Assert.IsTrue(nav.SelectNodeAt(2, _c2));
            Assert.AreEqual("A>B>C2", Names(nav));
            Assert.AreEqual("A>B>C2", PathWithTrailNames(nav), "C2 沒有走過的子路線，所以沒有記憶尾段。");

            // 切回 C：C 底下的 D 還記得。
            Assert.IsTrue(nav.SelectNodeAt(2, _c));
            Assert.AreEqual("A>B>C>D", PathWithTrailNames(nav));
            Assert.IsTrue(nav.StepForward());
            Assert.AreEqual("A>B>C>D", Names(nav));
        }

        [TestMethod]
        public void SelectNodeAt_InvalidArguments_Throw()
        {
            var nav = new CascadeNavigator();

            Assert.ThrowsException<ArgumentNullException>(() => nav.SelectNodeAt(0, null));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => nav.SelectNodeAt(1, _a));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => nav.SelectNodeAt(-1, _a));
        }

        /// <summary>
        /// 使用者情境：先走 Pretest 那條，再跳去看 BurnIn 那條，然後退回切換分支，→ → 要回到原本關注的 GetHWAddr。
        /// </summary>
        [TestMethod]
        public void Scenario_SwitchBranchAndComeBack()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("ActionStep", "ActionPretest", "RfStep1", "GetHWAddr|SetHWAddr");
            tree.ExpandAndAddPaths("ActionStep", "ActionMiddle", "Dummy");
            tree.ExpandAndAddPaths("ActionStep", "ActionBurnIn", "RfStep3", "WifiTestVerify");
            var nav = new CascadeNavigator();

            // a. ActionStep\ActionPretest\RfStep1\GetHWAddr
            var pretest = tree.GetLeaves().First(n => n.FullPathText.EndsWith("GetHWAddr"));
            nav.SelectPath(pretest.FullPath);

            // b. ActionStep\ActionBurnIn\RfStep3\WifiTestVerify：在第 2 層改選，再一路往下選
            var actionStep = tree.TopLevelNodes[0];
            var burnIn = actionStep.Children.Single(n => n.Name == "ActionBurnIn");
            var middle = actionStep.Children.Single(n => n.Name == "ActionMiddle");
            var pretestNode = actionStep.Children.Single(n => n.Name == "ActionPretest");
            nav.StepBack();
            nav.StepBack();
            nav.SelectNodeAt(1, burnIn);
            nav.StepForward();
            nav.StepForward();
            Assert.AreEqual("ActionStep>ActionBurnIn>RfStep3>WifiTestVerify", Names(nav));

            // c. ← ← 回到 ActionBurnIn，↑ ↑ 經過 ActionMiddle 回到 ActionPretest
            nav.StepBack();
            nav.StepBack();
            nav.SelectNodeAt(1, middle);
            nav.SelectNodeAt(1, pretestNode);
            Assert.AreEqual("ActionStep>ActionPretest>RfStep1>GetHWAddr", PathWithTrailNames(nav), "切回 Pretest 時記憶的路線要還在。");

            // → → 回到 GetHWAddr
            nav.StepForward();
            nav.StepForward();
            Assert.AreEqual("ActionStep>ActionPretest>RfStep1>GetHWAddr", Names(nav));

            // BurnIn 那條也還記得。
            nav.StepBack();
            nav.StepBack();
            nav.SelectNodeAt(1, burnIn);
            Assert.AreEqual("ActionStep>ActionBurnIn>RfStep3>WifiTestVerify", PathWithTrailNames(nav));
        }

        [TestMethod]
        public void ClearSelection_KeepsTrail()
        {
            var nav = WalkToD();

            Assert.IsTrue(nav.ClearSelection());
            Assert.AreEqual(0, nav.SelectedDepth);
            Assert.AreEqual("A>B>C>D", PathWithTrailNames(nav), "未選取時也能看到第一層起的記憶路線。");
            Assert.AreSame(_a, nav.GetTrailNodeAt(0));
            Assert.IsFalse(nav.ClearSelection(), "已經沒有選取時回傳 false。");

            Assert.IsTrue(nav.StepForward(), "未選取時 → 沿第一層的記憶走。");
            Assert.AreEqual("A", Names(nav));
        }

        [TestMethod]
        public void ClearTrail_KeepsSelectionPathOnly()
        {
            var nav = WalkToD();
            nav.StepBack();
            nav.StepBack();
            nav.SelectNodeAt(2, _c2);
            nav.SelectNodeAt(1, _b2);
            nav.SelectNodeAt(1, _b);

            nav.ClearTrail();

            Assert.AreEqual("A>B", Names(nav));
            Assert.AreEqual("A>B", PathWithTrailNames(nav), "B 底下記得的 C 被忘掉。");

            Assert.IsTrue(nav.StepForward());
            Assert.AreEqual("A>B>C", Names(nav), "沒有記憶時選第一個子節點。");
            nav.StepBack();
            Assert.AreEqual("A>B>C", PathWithTrailNames(nav), "選取路徑本身留在記憶裡，← 之後還能 → 回去。");
        }

        [TestMethod]
        public void Clear_RemovesSelectionAndTrail()
        {
            var nav = WalkToD();

            nav.Clear();

            Assert.AreEqual(0, nav.SelectedDepth);
            Assert.AreEqual(0, nav.PathWithTrail.Count);
            Assert.IsFalse(nav.StepForward());
        }

        [TestMethod]
        public void SelectPath_KeepsOtherBranches()
        {
            var nav = WalkToD();

            nav.SelectPath(new ICascadeNode[] { _a, _b2 });
            Assert.AreEqual("A>B2", Names(nav));

            nav.SelectNodeAt(1, _b);
            Assert.AreEqual("A>B>C>D", PathWithTrailNames(nav));

            nav.SelectPath(null);
            Assert.AreEqual(0, nav.SelectedDepth);

            Assert.ThrowsException<ArgumentException>(() => nav.SelectPath(new ICascadeNode[] { _a, null }));
        }

        [TestMethod]
        public void PathWithTrail_StopsOnCycle()
        {
            // 兩個節點互為子節點：記憶沿線往下查時不可無窮迴圈。
            var x = new CascadeNode("X", "X");
            var y = new CascadeNode("Y", "Y", children: new[] { x });
            x.Children.Add(y);

            var nav = new CascadeNavigator();
            nav.SelectPath(new ICascadeNode[] { x, y, x, y });
            nav.StepBack();
            nav.StepBack();
            nav.StepBack();

            Assert.AreEqual("X>Y", PathWithTrailNames(nav));
        }

        [TestMethod]
        public void ChildrenOf_Null_ReturnsEmpty()
        {
            Assert.AreEqual(0, CascadeNavigator.ChildrenOf(null).Count);
            Assert.AreEqual(2, CascadeNavigator.ChildrenOf(_a).Count);
        }
    }
}