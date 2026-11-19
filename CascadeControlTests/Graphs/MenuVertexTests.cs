using CascadeControl.Graphs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace CascadeControlTests.Graphs
{
    [TestClass]
    public class MenuVertexTests
    {
        [TestMethod]
        public void DegreesAndFlags()
        {
            var graph = TestGraphs.Small();

            var a = graph.V("A");
            Assert.AreEqual(0, a.InDegree);
            Assert.AreEqual(3, a.OutDegree);
            Assert.IsTrue(a.IsSource);
            Assert.IsFalse(a.IsSink);
            Assert.IsFalse(a.IsConvergent);

            var one = graph.V("1");
            Assert.AreEqual("m,n", TestGraphs.Names(one.Predecessors));
            Assert.IsTrue(one.IsConvergent);
            Assert.IsTrue(one.IsSink);

            var z = graph.V("Z");
            Assert.IsTrue(z.IsSource && z.IsSink, "孤立頂點同時是來源與終點");
        }

        [TestMethod]
        public void HasSuccessorAndPredecessor()
        {
            var graph = TestGraphs.Small();
            var a = graph.V("A");
            var m = graph.V("m");

            Assert.IsTrue(a.HasSuccessor(m));
            Assert.IsFalse(m.HasSuccessor(a), "有向邊");
            Assert.IsTrue(m.HasPredecessor(a));
            Assert.IsFalse(a.HasSuccessor(null));
            Assert.IsFalse(a.HasPredecessor(null));
        }

        [TestMethod]
        public void HasLoop()
        {
            var graph = new MenuGraph();
            graph.AddEdge("A", "A");
            graph.AddEdge("A", "B");

            Assert.IsTrue(graph.V("A").HasLoop);
            Assert.IsFalse(graph.V("B").HasLoop);
            Assert.AreEqual(1, graph.V("A").InDegree, "自我迴圈也算一條入邊");
        }

        [TestMethod]
        public void Rename_NewName_RenamesInPlace()
        {
            var graph = TestGraphs.Small();
            var m = graph.V("m");

            var result = m.Rename("  mm  ");

            Assert.AreSame(m, result);
            Assert.AreEqual("mm", m.Name, "改名會先 trim");
            Assert.IsFalse(graph.ContainsVertex("m"));
            Assert.AreSame(m, graph.V("mm"));
            Assert.AreEqual("1,2", TestGraphs.Names(m.Successors), "邊不受影響");
        }

        [TestMethod]
        public void Rename_SameName_ReturnsSelf()
        {
            var graph = TestGraphs.Small();
            var m = graph.V("m");

            Assert.AreSame(m, m.Rename("m"));
        }

        [TestMethod]
        public void Rename_ExistingName_MergesEdgesIntoExistingVertex()
        {
            var graph = new MenuGraph();
            graph.AddEdge("p", "a");
            graph.AddEdge("a", "x");
            graph.AddEdge("b", "y");
            graph.AddEdge("a", "b");
            var a = graph.V("a");
            var b = graph.V("b");

            var result = a.Rename("b");

            Assert.AreSame(b, result);
            Assert.IsNull(a.Graph, "被合併的頂點已經從圖中移除");
            Assert.IsFalse(graph.ContainsVertex("a"));
            Assert.AreEqual("y,x,b", TestGraphs.Names(b.Successors), "a 的出邊接在 b 原本的出邊之後；a → b 變成 b 的自我迴圈");
            Assert.AreEqual("b,p", TestGraphs.Names(b.Predecessors), "原本的 a 拿掉後，依序是自我迴圈 b、再轉接過來的 p");
            Assert.AreEqual(4, graph.EdgeCount, "p→b、b→y、b→x、b→b");
        }

        [TestMethod]
        public void Rename_ExistingName_DuplicateEdgesAreMerged()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("a|b", "x");

            graph.V("a").Rename("b");

            Assert.AreEqual(1, graph.EdgeCount);
            Assert.AreEqual("b", TestGraphs.Names(graph.V("x").Predecessors));
        }

        [TestMethod]
        public void Rename_InvalidName_Throws()
        {
            var vertex = new MenuGraph().GetOrAddVertex("A");

            Assert.ThrowsException<ArgumentException>(() => vertex.Rename(null));
            Assert.ThrowsException<ArgumentException>(() => vertex.Rename("  "));
            Assert.ThrowsException<ArgumentException>(() => vertex.Rename("A|B"));
        }

        [TestMethod]
        public void Rename_RemovedVertex_Throws()
        {
            var graph = new MenuGraph();
            var vertex = graph.GetOrAddVertex("A");
            graph.RemoveVertex(vertex);

            Assert.ThrowsException<InvalidOperationException>(() => vertex.Rename("B"));
        }
    }
}