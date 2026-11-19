using CascadeControl.Graphs;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CascadeControlTests.Graphs
{
    /// <summary>
    /// <see cref="MenuGraph.ToRows(MenuGraphRowStyle)"/> 的三種寫法與讀回保證。
    /// </summary>
    [TestClass]
    public class MenuGraphRowsTests
    {
        private static readonly MenuGraphRowStyle[] AllStyles =
            { MenuGraphRowStyle.Expanded, MenuGraphRowStyle.Compact, MenuGraphRowStyle.Outline };

        [TestMethod]
        public void SmallGraph_Expanded()
        {
            Assert.AreEqual("A,x / A,m / A,n / m,1 / m,2 / n,1 / n,2 / Z,",
                TestTrees.Describe(TestGraphs.Small().ToRows(MenuGraphRowStyle.Expanded)));
        }

        [TestMethod]
        public void SmallGraph_Outline()
        {
            Assert.AreEqual("A,x|m|n / m,1|2 / n,1|2 / Z,",
                TestTrees.Describe(TestGraphs.Small().ToRows(MenuGraphRowStyle.Outline)));
        }

        [TestMethod]
        public void SmallGraph_Compact()
        {
            Assert.AreEqual("A,x|m|n / m|n,1|2 / Z,",
                TestTrees.Describe(TestGraphs.Small().ToRows(MenuGraphRowStyle.Compact)));
        }

        [TestMethod]
        public void ToRows_DefaultIsExpanded()
        {
            var graph = TestGraphs.Small();

            Assert.AreEqual(TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Expanded)), TestTrees.Describe(graph.ToRows()));
        }

        [TestMethod]
        public void Compact_GroupsNonAdjacentVerticesWithSameSuccessors()
        {
            // p 與 r 的後繼相同，中間隔著 q。
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("S", "p|q|r");
            graph.ExpandAndAddEdges("p", "1|2");
            graph.ExpandAndAddEdges("q", "3");
            graph.ExpandAndAddEdges("r", "1|2");

            Assert.AreEqual("S,p|q|r / p|r,1|2 / q,3", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Compact)));
        }

        [TestMethod]
        public void Compact_DifferentSuccessorOrder_IsNotGrouped()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("p", "1|2");
            graph.ExpandAndAddEdges("q", "2|1");

            Assert.AreEqual("p,1|2 / q,2|1", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Compact)));
        }

        [TestMethod]
        public void Compact_SourcesAreNeverMerged()
        {
            // Z、W 都是孤立頂點、S1、S2 的後繼相同；合併會讓來源頂點的順序在讀回時改變。
            var graph = new MenuGraph();
            graph.GetOrAddVertex("Z");
            graph.ExpandAndAddEdges("S1", "x");
            graph.GetOrAddVertex("W");
            graph.ExpandAndAddEdges("S2", "x");

            Assert.AreEqual("Z, / S1,x / W, / S2,x", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Compact)));
        }

        [TestMethod]
        public void Compact_DoesNotMergeAcrossStarts()
        {
            // v0 從 S1 走到、v9 從 S2 走到，後繼都是 t；併在一起會讓 S2 那一列之前就出現 v9。
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("S1", "v0");
            graph.ExpandAndAddEdges("S2", "v9");
            graph.ExpandAndAddEdges("v0|v9", "t");

            Assert.AreEqual("S1,v0 / v0,t / S2,v9 / v9,t", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Compact)));
        }

        [TestMethod]
        public void RowOrder_DepthFirstFromSources()
        {
            // 頂點順序是 B、A、C，但 A 是唯一的來源頂點，所以從 A 開始走。
            var graph = new MenuGraph();
            graph.AddEdge("B", "C");
            graph.AddEdge("A", "B");

            Assert.AreEqual("A,B / B,C", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Expanded)));
        }

        [TestMethod]
        public void RowOrder_CycleWithoutSources_StartsFromVertexOrder()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "B");
            graph.AddEdge("B", "A");

            Assert.AreEqual("A,B / B,A", TestTrees.Describe(graph.ToRows(MenuGraphRowStyle.Expanded)));
        }

        [TestMethod]
        public void SelfLoop_IsWrittenAsOwnEdge()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "A");

            foreach (var style in AllStyles)
                Assert.AreEqual("A,A", TestTrees.Describe(graph.ToRows(style)), style.ToString());
        }

        [TestMethod]
        public void EmptyGraph_HasNoRows()
        {
            foreach (var style in AllStyles) Assert.AreEqual(0, new MenuGraph().ToRows(style).Length);
        }

        /// <summary>
        /// 驗收條件：範例以 Compact 寫出，必須和範例原文逐列相同。
        /// </summary>
        [TestMethod]
        public void Sample_Compact_MatchesSampleRows()
        {
            var rows = TestGraphs.Sample().ToRows(MenuGraphRowStyle.Compact);

            Assert.AreEqual(TestTrees.Describe(TestGraphs.SampleRows()), TestTrees.Describe(rows));
        }

        /// <summary>
        /// 驗收條件：三種寫法讀回後，頂點、邊、後繼順序、來源頂點順序都相同，而且列與列不重疊。
        /// </summary>
        [TestMethod]
        public void RandomGraphs_RoundTripAndRowsDoNotOverlap()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                var graph = TestGraphs.Random(seed);

                foreach (var style in AllStyles)
                {
                    var rows = graph.ToRows(style);
                    var back = MenuGraph.FromRows(rows, out var warnings);

                    Assert.AreEqual(0, warnings.Count, $"seed {seed}, {style}");
                    Assert.AreEqual(TestGraphs.Fingerprint(graph), TestGraphs.Fingerprint(back), $"seed {seed}, {style}");
                    Assert.AreEqual(graph.EdgeCount, back.EdgeCount, $"seed {seed}, {style}");
                    Assert.AreEqual(graph.EdgeCount, rows.Sum(TestGraphs.EdgesOf), $"seed {seed}, {style}：列與列重疊");
                }
            }
        }

        /// <summary>
        /// 驗收條件：已整理過的結果再整理一次不變。
        /// </summary>
        [TestMethod]
        public void RandomGraphs_IsIdempotent()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                foreach (var style in AllStyles)
                {
                    var once = TestGraphs.Random(seed).ToRows(style);
                    var twice = MenuGraph.FromRows(once, out _).ToRows(style);

                    Assert.AreEqual(TestTrees.Describe(once), TestTrees.Describe(twice), $"seed {seed}, {style}");
                }
            }
        }

        [TestMethod]
        public void RandomGraphs_AreVaried()
        {
            var seeds = Enumerable.Range(0, 1000).Select(TestGraphs.Random).ToList();

            Assert.IsTrue(seeds.Count(graph => !graph.IsAcyclic()) > 100, "要有足夠多含環的圖");
            Assert.IsTrue(seeds.Count(graph => graph.Vertices.Any(vertex => vertex.IsConvergent)) > 300, "要有足夠多匯流的圖");
            Assert.IsTrue(seeds.Count(graph => graph.ToRows(MenuGraphRowStyle.Compact).Length < graph.ToRows(MenuGraphRowStyle.Outline).Length) > 100,
                "要有足夠多可以壓縮的圖");
        }
    }
}