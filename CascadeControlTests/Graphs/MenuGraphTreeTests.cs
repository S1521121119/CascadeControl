using CascadeControl.Graphs;
using CascadeControl.Trees;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Graphs
{
    /// <summary>
    /// <see cref="MenuGraph.FromTree(MenuTree)"/> 與 <see cref="MenuGraph.ToTree(MenuVertex, out System.Collections.Generic.IList{string})"/>。
    /// </summary>
    [TestClass]
    public class MenuGraphTreeTests
    {
        #region FromTree

        [TestMethod]
        public void FromTree_SameNamesMergeIntoOneVertex()
        {
            var graph = MenuGraph.FromTree(TestTrees.Template());

            Assert.AreEqual("Axis X,Axis Y,Axis Z", TestGraphs.Names(graph.V("Jog").Predecessors));
            Assert.AreEqual("Forward,Backward", TestGraphs.Names(graph.V("Jog").Successors));
            Assert.AreEqual("System,Motor,IO,Diagnostics", TestGraphs.Names(graph.Sources));
            Assert.IsTrue(graph.IsAcyclic());
        }

        [TestMethod]
        public void FromTree_VertexOrder_PreorderWithChildrenTogether()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("Main", "System|Motor");
            tree.AddPath("Main", "System", "Info");

            var graph = MenuGraph.FromTree(tree);

            Assert.AreEqual("Main,System,Motor,Info", TestGraphs.Names(graph.Vertices));
        }

        [TestMethod]
        public void FromTree_SharedVertex_ExposesOtherBranchSuccessors()
        {
            // 樹中只有 AxisX > Jog 有下一層；轉成圖後 AxisY 也能走到 Step。
            var tree = new MenuTree();
            tree.AddPath("Motor", "AxisX", "Jog", "Step");
            tree.AddPath("Motor", "AxisY", "Jog");

            var unfolded = MenuGraph.FromTree(tree).ToTree(out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(
                new[] { "Motor > AxisX > Jog > Step", "Motor > AxisY > Jog > Step" },
                TestTrees.LeafPaths(unfolded));
        }

        [TestMethod]
        public void FromTree_TopLevelNameUsedDeeper_IsNotASource()
        {
            var tree = new MenuTree();
            tree.AddPath("System", "Info");
            tree.AddPath("Main", "System");

            var graph = MenuGraph.FromTree(tree);

            Assert.AreEqual("Main", TestGraphs.Names(graph.Sources));
        }

        [TestMethod]
        public void FromTree_RepeatedNameOnPath_CreatesCycle()
        {
            var tree = new MenuTree();
            tree.AddPath("A", "B", "A");

            var graph = MenuGraph.FromTree(tree);

            Assert.IsFalse(graph.IsAcyclic());
            Assert.AreEqual(0, graph.Sources.Count());
        }

        [TestMethod]
        public void FromTree_InvalidArguments_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(() => MenuGraph.FromTree(null));

            var tree = new MenuTree();
            tree.Root.GetOrAddChild("A|B"); // 只有直接操作節點才造得出這種名稱
            Assert.ThrowsException<ArgumentException>(() => MenuGraph.FromTree(tree));
        }

        [TestMethod]
        public void FromTree_EmptyTree_IsEmptyGraph()
        {
            Assert.IsTrue(MenuGraph.FromTree(new MenuTree()).IsEmpty);
        }

        #endregion FromTree

        #region ToTree

        [TestMethod]
        public void ToTree_SharedVerticesAreCopiedUnderEachPredecessor()
        {
            var tree = TestGraphs.Small().ToTree(out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(
                new[] { "A > x", "A > m > 1", "A > m > 2", "A > n > 1", "A > n > 2", "Z" },
                TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void ToTree_FromStart()
        {
            var graph = TestGraphs.Small();

            var tree = graph.ToTree(graph.V("m"), out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(new[] { "m > 1", "m > 2" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void ToTree_Cycle_EdgeBackToPathIsSkipped()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "B");
            graph.AddEdge("B", "A");
            graph.AddEdge("B", "C");

            var tree = graph.ToTree(graph.V("A"), out var warnings);

            CollectionAssert.AreEqual(new[] { "A > B > C" }, TestTrees.LeafPaths(tree));
            Assert.AreEqual("B -> A 會形成迴圈，已略過。", warnings.Single());
        }

        [TestMethod]
        public void ToTree_SelfLoop_IsSkipped()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "A");
            graph.AddEdge("A", "B");

            // A 有自我迴圈，入度為 1，不算來源頂點，所以要指定起點。
            Assert.AreEqual(0, graph.Sources.Count());
            var tree = graph.ToTree(graph.V("A"), out var warnings);

            CollectionAssert.AreEqual(new[] { "A > B" }, TestTrees.LeafPaths(tree));
            Assert.AreEqual("A -> A 會形成迴圈，已略過。", warnings.Single());
        }

        [TestMethod]
        public void ToTree_VertexOnOtherBranch_IsNotACycle()
        {
            // 菱形：A → B → D、A → C → D；D 出現兩次不是環。
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("A", "B|C");
            graph.ExpandAndAddEdges("B|C", "D");

            var tree = graph.ToTree(out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(new[] { "A > B > D", "A > C > D" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void ToTree_NoSources_ReturnsEmptyTreeWithWarning()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "B");
            graph.AddEdge("B", "A");

            var tree = graph.ToTree(out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            StringAssert.Contains(warnings.Single(), "沒有來源頂點");
            Assert.IsTrue(new MenuGraph().ToTree(out warnings).IsEmpty);
            Assert.AreEqual(0, warnings.Count, "空圖不需要警告");
        }

        [TestMethod]
        public void ToTree_TooManyPaths_ReturnsEmptyTreeWithWarning()
        {
            // 17 層菱形：2^17 = 131,072 條路徑。
            var graph = new MenuGraph();
            for (var level = 0; level < 17; level++)
            {
                graph.ExpandAndAddEdges("L" + level, $"a{level}|b{level}");
                graph.ExpandAndAddEdges($"a{level}|b{level}", "L" + (level + 1));
            }

            var tree = graph.ToTree(out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            StringAssert.Contains(warnings.Single(), "已放棄整份內容");
        }

        [TestMethod]
        public void ToTree_ForeignStart_Throws()
        {
            var graph = TestGraphs.Small();

            Assert.ThrowsException<ArgumentNullException>(() => graph.ToTree(null, out _));
            Assert.ThrowsException<ArgumentException>(() => graph.ToTree(new MenuGraph().GetOrAddVertex("A"), out _));
        }

        #endregion ToTree

        #region 互轉性質

        [TestMethod]
        public void AcyclicGraph_ToTreeThenFromTree_RestoresReachablePart()
        {
            var sample = TestGraphs.Sample();

            var back = MenuGraph.FromTree(sample.ToTree(out var warnings));

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestGraphs.Fingerprint(sample), TestGraphs.Fingerprint(back));
        }

        [TestMethod]
        public void TreeWithUniqueNames_FromTreeThenToTree_RestoresTree()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("System", "Reboot|Shutdown");
            tree.ExpandAndAddPaths("System", "Info", "Version|Uptime");
            tree.ExpandAndAddPaths("Motor", "Home|Jog");

            var back = MenuGraph.FromTree(tree).ToTree(out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back));
        }

        #endregion 互轉性質
    }
}