using CascadeControl.Graphs;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Graphs
{
    [TestClass]
    public class MenuGraphTests
    {
        #region 頂點

        [TestMethod]
        public void GetOrAddVertex_SameName_ReturnsSameVertex()
        {
            var graph = new MenuGraph();

            var first = graph.GetOrAddVertex("A");
            graph.GetOrAddVertex("B");

            Assert.AreSame(first, graph.GetOrAddVertex("A"));
            Assert.AreSame(graph, first.Graph);
            Assert.AreEqual("A,B", TestGraphs.Names(graph.Vertices));
            Assert.AreEqual(2, graph.VertexCount);
        }

        [TestMethod]
        public void GetOrAddVertex_IsCaseSensitiveAndDoesNotTrim()
        {
            var graph = new MenuGraph();
            graph.GetOrAddVertex("Reboot");
            graph.GetOrAddVertex("reboot");
            graph.GetOrAddVertex(" Reboot ");

            Assert.AreEqual(3, graph.VertexCount);
        }

        [TestMethod]
        public void GetOrAddVertex_InvalidName_Throws()
        {
            var graph = new MenuGraph();

            Assert.ThrowsException<ArgumentException>(() => graph.GetOrAddVertex(null));
            Assert.ThrowsException<ArgumentException>(() => graph.GetOrAddVertex(" "));
            Assert.ThrowsException<ArgumentException>(() => graph.GetOrAddVertex("A|B"));
            Assert.IsTrue(graph.IsEmpty);
        }

        [TestMethod]
        public void TryGetVertexAndContainsVertex()
        {
            var graph = TestGraphs.Small();

            Assert.IsTrue(graph.TryGetVertex("m", out var m));
            Assert.AreEqual("m", m.Name);
            Assert.IsFalse(graph.TryGetVertex("missing", out var missing));
            Assert.IsNull(missing);
            Assert.IsFalse(graph.TryGetVertex(null, out _));
            Assert.IsTrue(graph.ContainsVertex("Z"));
            Assert.IsFalse(graph.ContainsVertex(null));
        }

        [TestMethod]
        public void RemoveVertex_RemovesIncidentEdgesOnBothSides()
        {
            var graph = TestGraphs.Small();
            var m = graph.V("m");

            Assert.IsTrue(graph.RemoveVertex(m));

            Assert.IsNull(m.Graph);
            Assert.IsFalse(graph.ContainsVertex("m"));
            Assert.AreEqual("x,n", TestGraphs.Names(graph.V("A").Successors));
            Assert.AreEqual("n", TestGraphs.Names(graph.V("1").Predecessors));
            Assert.AreEqual(4, graph.EdgeCount, "A→x、A→n、n→1、n→2");
            Assert.IsFalse(graph.RemoveVertex(m), "已經移除");
            Assert.IsFalse(graph.RemoveVertex(null));

            Assert.AreNotSame(m, graph.GetOrAddVertex("m"), "名稱可以再用，但會是新的頂點");
        }

        [TestMethod]
        public void RemoveVertex_WithSelfLoop()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "A");
            graph.AddEdge("A", "B");

            graph.RemoveVertex(graph.V("A"));

            Assert.AreEqual(0, graph.EdgeCount);
            Assert.AreEqual(0, graph.V("B").InDegree);
        }

        [TestMethod]
        public void RemoveVertex_FromAnotherGraph_ReturnsFalse()
        {
            var other = new MenuGraph().GetOrAddVertex("A");
            var graph = new MenuGraph();
            graph.GetOrAddVertex("A");

            Assert.IsFalse(graph.RemoveVertex(other));
            Assert.AreEqual(1, graph.VertexCount);
        }

        #endregion 頂點

        #region 邊

        [TestMethod]
        public void AddEdge_Duplicate_IsMergedAndKeepsFirstOrder()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "x");
            graph.AddEdge("A", "y");
            graph.AddEdge("A", "x");

            Assert.AreEqual(2, graph.EdgeCount);
            Assert.AreEqual("x,y", TestGraphs.Names(graph.V("A").Successors));
        }

        [TestMethod]
        public void AddEdge_ReturnsTheEdge()
        {
            var graph = new MenuGraph();

            var edge = graph.AddEdge("A", "B");

            Assert.AreEqual("A", edge.From.Name);
            Assert.AreEqual("B", edge.To.Name);
            Assert.AreEqual(edge, graph.AddEdge("A", "B"));
        }

        [TestMethod]
        public void AddEdge_ForeignOrNullVertex_Throws()
        {
            var graph = new MenuGraph();
            var mine = graph.GetOrAddVertex("A");
            var foreign = new MenuGraph().GetOrAddVertex("B");

            Assert.ThrowsException<ArgumentException>(() => graph.AddEdge(mine, foreign));
            Assert.ThrowsException<ArgumentException>(() => graph.AddEdge(foreign, mine));
            Assert.ThrowsException<ArgumentNullException>(() => graph.AddEdge(mine, null));
            Assert.AreEqual(0, graph.EdgeCount);
        }

        [TestMethod]
        public void AddEdge_ByName_InvalidSecondName_AddsNothing()
        {
            var graph = new MenuGraph();

            Assert.ThrowsException<ArgumentException>(() => graph.AddEdge("A", "B|C"));
            Assert.IsTrue(graph.IsEmpty, "先檢查完兩個名稱才動圖");
        }

        [TestMethod]
        public void RemoveEdge_KeepsVertices()
        {
            var graph = TestGraphs.Small();

            Assert.IsTrue(graph.RemoveEdge(graph.V("m"), graph.V("1")));

            Assert.AreEqual(6, graph.EdgeCount);
            Assert.AreEqual("2", TestGraphs.Names(graph.V("m").Successors));
            Assert.AreEqual("n", TestGraphs.Names(graph.V("1").Predecessors));
            Assert.IsTrue(graph.ContainsVertex("1"));
            Assert.IsFalse(graph.RemoveEdge(graph.V("m"), graph.V("1")), "已經移除");
            Assert.IsFalse(graph.RemoveEdge(graph.V("1"), graph.V("m")), "沒有反方向的邊");
            Assert.IsFalse(graph.RemoveEdge(null, graph.V("m")));
        }

        [TestMethod]
        public void IsAdjacent_IsDirected()
        {
            var graph = TestGraphs.Small();

            Assert.IsTrue(graph.IsAdjacent(graph.V("A"), graph.V("m")));
            Assert.IsFalse(graph.IsAdjacent(graph.V("m"), graph.V("A")));
            Assert.IsFalse(graph.IsAdjacent(graph.V("A"), null));
            Assert.IsFalse(graph.IsAdjacent(graph.V("A"), new MenuGraph().GetOrAddVertex("m")));
        }

        [TestMethod]
        public void ExpandAndAddEdges_CartesianProductRightFastest()
        {
            var graph = new MenuGraph();

            var edges = graph.ExpandAndAddEdges(" Info | Status ", "Version|Uptime");

            Assert.AreEqual("Info -> Version / Info -> Uptime / Status -> Version / Status -> Uptime",
                string.Join(" / ", edges.Select(edge => edge.ToString())));
            Assert.AreEqual("Info,Status,Version,Uptime", TestGraphs.Names(graph.Vertices), "先左格、再右格");
        }

        [TestMethod]
        public void ExpandAndAddEdges_SingleNamesAreTrimmed()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("  A ", " B  ");

            Assert.AreEqual("A,B", TestGraphs.Names(graph.Vertices));
        }

        [DataTestMethod]
        [DataRow("A||B", "x")]
        [DataRow("A", "|x")]
        [DataRow(" ", "x")]
        [DataRow("A", null)]
        public void ExpandAndAddEdges_Invalid_ThrowsAndAddsNothing(string fromCell, string toCell)
        {
            var graph = new MenuGraph();

            Assert.ThrowsException<ArgumentException>(() => graph.ExpandAndAddEdges(fromCell, toCell));
            Assert.IsTrue(graph.IsEmpty);
        }

        [TestMethod]
        public void ExpandAndAddEdges_OverLimit_Throws()
        {
            var graph = new MenuGraph();

            Assert.ThrowsException<ArgumentException>(
                () => graph.ExpandAndAddEdges(TestTrees.Alternatives("a", 256), TestTrees.Alternatives("b", 256)));
            Assert.IsTrue(graph.IsEmpty);
        }

        [TestMethod]
        public void SourcesSinksAndEdges()
        {
            var graph = TestGraphs.Small();

            Assert.AreEqual("A,Z", TestGraphs.Names(graph.Sources));
            Assert.AreEqual("x,1,2,Z", TestGraphs.Names(graph.Sinks));
            Assert.AreEqual(7, graph.EdgeCount);
            Assert.AreEqual("A -> x / A -> m / A -> n / m -> 1 / m -> 2 / n -> 1 / n -> 2",
                string.Join(" / ", graph.Edges.Select(edge => edge.ToString())));
        }

        #endregion 邊

        #region 走訪

        [TestMethod]
        public void GetDistances_ShortestHopCount()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("A", "B|C");
            graph.AddEdge("B", "D");
            graph.AddEdge("D", "C");
            graph.AddEdge("D", "A");

            var distances = graph.GetDistances(graph.V("A"));

            Assert.AreEqual(0, distances[graph.V("A")]);
            Assert.AreEqual(1, distances[graph.V("B")]);
            Assert.AreEqual(1, distances[graph.V("C")], "A → C 比 A → B → D → C 短");
            Assert.AreEqual(2, distances[graph.V("D")]);
            Assert.AreEqual(4, distances.Count);
        }

        [TestMethod]
        public void GetDistances_MaxDistanceAndUnreachable()
        {
            var graph = TestGraphs.Small();

            var distances = graph.GetDistances(graph.V("A"), 1);

            Assert.AreEqual(4, distances.Count, "A、x、m、n");
            Assert.IsFalse(distances.ContainsKey(graph.V("1")));
            Assert.IsFalse(graph.GetDistances(graph.V("A")).ContainsKey(graph.V("Z")), "孤立頂點走不到");
            Assert.AreEqual(1, graph.GetDistances(graph.V("A"), 0).Count);
        }

        [TestMethod]
        public void GetDistances_InvalidArguments_Throw()
        {
            var graph = TestGraphs.Small();

            Assert.ThrowsException<ArgumentNullException>(() => graph.GetDistances(null));
            Assert.ThrowsException<ArgumentException>(() => graph.GetDistances(new MenuGraph().GetOrAddVertex("A")));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => graph.GetDistances(graph.V("A"), -1));
        }

        [TestMethod]
        public void IsAcyclic()
        {
            Assert.IsTrue(new MenuGraph().IsAcyclic());
            Assert.IsTrue(TestGraphs.Small().IsAcyclic());
            Assert.IsTrue(TestGraphs.Sample().IsAcyclic());

            var loop = new MenuGraph();
            loop.AddEdge("A", "A");
            Assert.IsFalse(loop.IsAcyclic(), "自我迴圈也算環");

            var cycle = new MenuGraph();
            cycle.AddEdge("S", "A");
            cycle.AddEdge("A", "B");
            cycle.AddEdge("B", "A");
            Assert.IsFalse(cycle.IsAcyclic());
        }

        #endregion 走訪

        #region FromRows

        [TestMethod]
        public void FromRows_Sample_MatchesDesignNumbers()
        {
            var graph = MenuGraph.FromRows(TestGraphs.SampleRows(), out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(346, graph.VertexCount);
            Assert.AreEqual(610, graph.EdgeCount);
            Assert.IsTrue(graph.IsAcyclic());
            Assert.AreEqual("Main", TestGraphs.Names(graph.Sources));
            Assert.AreEqual("AxisX,AxisY", TestGraphs.Names(graph.V("Jog").Predecessors));
            Assert.AreEqual("Bank0,Bank1", TestGraphs.Names(graph.V("Ch1").Predecessors));
            Assert.AreEqual("Band2G,Band5G", TestGraphs.Names(graph.V("WifiCh1").Predecessors));
        }

        [TestMethod]
        public void FromRows_VertexOrder_FromThenTo()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { "b|a", "d|c" },
                new[] { "c", "e" },
            }, out _);

            Assert.AreEqual("b,a,d,c,e", TestGraphs.Names(graph.Vertices));
        }

        [TestMethod]
        public void FromRows_TrimsCells()
        {
            var graph = MenuGraph.FromRows(new[] { new[] { " A ", " B " } }, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("A,B", TestGraphs.Names(graph.Vertices));
        }

        [TestMethod]
        public void FromRows_EmptyTo_DeclaresIsolatedVertices()
        {
            var graph = MenuGraph.FromRows(new[] { new[] { "Z|W", "" }, new[] { "Y" } }, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("Z,W,Y", TestGraphs.Names(graph.Vertices));
            Assert.AreEqual(0, graph.EdgeCount);
        }

        [TestMethod]
        public void FromRows_EmptyFrom_SkipsRow()
        {
            var graph = MenuGraph.FromRows(new[] { new[] { "", "B" }, new[] { "A", "B" } }, out var warnings);

            Assert.AreEqual("A,B", TestGraphs.Names(graph.Vertices));
            Assert.AreEqual("第 1 列：From 是空的，整列已跳過。", warnings.Single());
        }

        [TestMethod]
        public void FromRows_ExtraColumns_AreIgnoredWithWarning()
        {
            var graph = MenuGraph.FromRows(new[] { new[] { "A", "B", "C" }, new[] { "D", "E", "", " " } }, out var warnings);

            Assert.AreEqual("A,B,D,E", TestGraphs.Names(graph.Vertices), "第 3 欄不會解讀成 B → C");
            Assert.AreEqual(2, graph.EdgeCount);
            StringAssert.Contains(warnings.Single(), "第 1 列");
            StringAssert.Contains(warnings.Single(), "第 3 欄之後");
        }

        [TestMethod]
        public void FromRows_MalformedSeparator_SkipsThatRowOnly()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { "A||B", "x" },
                new[] { "C", "y|" },
                new[] { "D", "z" },
            }, out var warnings);

            Assert.AreEqual("D,z", TestGraphs.Names(graph.Vertices));
            Assert.AreEqual(2, warnings.Count);
            StringAssert.Contains(warnings[0], "第 1 列：第 1 欄 'A||B'");
            StringAssert.Contains(warnings[1], "第 2 列：第 2 欄 'y|'");
        }

        [TestMethod]
        public void FromRows_DuplicateEdgesAndSelfLoop_NoWarnings()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { "A", "B" },
                new[] { "A", "B|A" },
            }, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(2, graph.EdgeCount);
            Assert.IsTrue(graph.V("A").HasLoop);
        }

        [TestMethod]
        public void FromRows_UsesSourceLineNumbers()
        {
            MenuGraph.FromRows(new[] { new[] { "", "B" } }, new[] { 42 }, out var warnings);

            StringAssert.StartsWith(warnings.Single(), "第 42 列：");
        }

        [TestMethod]
        public void FromRows_LineNumberCountMismatch_Throws()
        {
            Assert.ThrowsException<ArgumentException>(
                () => MenuGraph.FromRows(new[] { new[] { "A", "B" } }, new[] { 1, 2 }, out _));
        }

        [TestMethod]
        public void FromRows_NullInput_IsEmpty()
        {
            Assert.IsTrue(MenuGraph.FromRows(null, out var warnings).IsEmpty);
            Assert.AreEqual(0, warnings.Count);

            Assert.IsTrue(MenuGraph.FromRows(new string[][] { null, new string[0] }, out warnings).IsEmpty);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void FromRows_SingleRowOverLimit_SkipsThatRowOnly()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { TestTrees.Alternatives("a", 256), TestTrees.Alternatives("b", 256) },   // 65,536
                new[] { "ok", "fine" },
            }, out var warnings);

            Assert.AreEqual("ok,fine", TestGraphs.Names(graph.Vertices));
            StringAssert.Contains(warnings.Single(), "65536");
        }

        [TestMethod]
        public void FromRows_TotalOverLimit_AbandonsEverything()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { "x", TestTrees.Alternatives("a", 40000) },
                new[] { "y", TestTrees.Alternatives("b", 30000) },
            }, out var warnings);

            Assert.IsTrue(graph.IsEmpty);
            StringAssert.Contains(warnings.Single(), "已放棄整份內容");
        }

        [TestMethod]
        public void FromRows_TotalExactlyAtLimit_IsAccepted()
        {
            var graph = MenuGraph.FromRows(new[]
            {
                new[] { "x", TestTrees.Alternatives("a", 40000) },
                new[] { "y", TestTrees.Alternatives("b", 25535) },
            }, out var warnings);

            Assert.AreEqual(MenuGraph.MAX_EDGES, graph.EdgeCount);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void FromRows_DuplicateWarnings_AreMerged()
        {
            MenuGraph.FromRows(new[] { new[] { "", "B" } }, new[] { 3 }, out var first);
            MenuGraph.FromRows(new[] { new[] { "", "B" }, new[] { "", "C" } }, new[] { 3, 3 }, out var merged);

            CollectionAssert.AreEqual(first.ToArray(), merged.ToArray());
        }

        [TestMethod]
        public void ToRows_UndefinedStyle_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MenuGraph().ToRows((MenuGraphRowStyle)99));
        }

        #endregion FromRows
    }
}