using CascadeControl.Graphs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace CascadeControlTests.Graphs
{
    [TestClass]
    public class MenuEdgeTests
    {
        [TestMethod]
        public void Equality_UsesVertexReferences()
        {
            var graph = new MenuGraph();
            var a = graph.GetOrAddVertex("A");
            var b = graph.GetOrAddVertex("B");

            var edge = new MenuEdge(a, b);

            Assert.AreEqual(edge, new MenuEdge(a, b));
            Assert.IsTrue(edge == new MenuEdge(a, b));
            Assert.IsTrue(edge != new MenuEdge(b, a), "有向邊，反方向是另一條邊");
            Assert.AreEqual(edge.GetHashCode(), new MenuEdge(a, b).GetHashCode());
            Assert.IsFalse(edge.Equals("A -> B"));
        }

        [TestMethod]
        public void Equality_SameNamesInDifferentGraphs_AreDifferentEdges()
        {
            var first = new MenuGraph();
            var second = new MenuGraph();

            Assert.AreNotEqual(first.AddEdge("A", "B"), second.AddEdge("A", "B"));
        }

        [TestMethod]
        public void NamesContainingArrow_DoNotCollide()
        {
            // 用字串 "From->To" 當鍵時，這兩條邊會撞成同一個鍵。
            var graph = new MenuGraph();
            var first = graph.AddEdge("a->b", "c");
            var second = graph.AddEdge("a", "b->c");

            Assert.AreEqual(first.From.Name + "->" + first.To.Name, second.From.Name + "->" + second.To.Name, "字串鍵會碰撞");
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(2, graph.EdgeCount);
        }

        [TestMethod]
        public void IsLoop_AndIsConvergent()
        {
            var graph = new MenuGraph();
            var loop = graph.AddEdge("A", "A");
            var single = graph.AddEdge("A", "B");
            var shared = graph.AddEdge("C", "B");

            Assert.IsTrue(loop.IsLoop);
            Assert.IsFalse(single.IsLoop);
            Assert.IsTrue(single.IsConvergent, "B 有 A、C 兩個前驅");
            Assert.IsTrue(shared.IsConvergent);
            Assert.IsFalse(loop.IsConvergent, "A 只有自己一個前驅");
        }

        [TestMethod]
        public void Constructor_NullVertex_Throws()
        {
            var vertex = new MenuGraph().GetOrAddVertex("A");

            Assert.ThrowsException<ArgumentNullException>(() => new MenuEdge(null, vertex));
            Assert.ThrowsException<ArgumentNullException>(() => new MenuEdge(vertex, null));
        }

        [TestMethod]
        public void Default_DoesNotThrow()
        {
            var edge = default(MenuEdge);

            Assert.IsFalse(edge.IsLoop);
            Assert.IsFalse(edge.IsConvergent);
            Assert.AreEqual(" -> ", edge.ToString());
        }
    }
}