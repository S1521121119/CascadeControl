using CascadeControl.Common;
using CascadeControl.Graphs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CascadeControlTests.Graphs
{
    [TestClass]
    public class MenuGraphNodeTests
    {
        [TestMethod]
        public void CreateTopLevelNodes_OnePerSource()
        {
            var nodes = TestGraphs.Small().CreateTopLevelNodes();

            Assert.AreEqual("<01>A [+] / <02>Z", string.Join(" / ", nodes.Select(DisplayTextOf)));
            Assert.IsTrue(nodes.All(node => node.Parent == null && node.Depth == 1));
            Assert.IsTrue(nodes[1].IsLeaf);
        }

        [TestMethod]
        public void SiblingNumber_DependsOnPredecessor()
        {
            // s 在 P 底下是第 2 項，在 Q 底下是第 1 項。
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("P", "a|s");
            graph.ExpandAndAddEdges("Q", "s");

            var top = graph.CreateTopLevelNodes();
            var underP = top[0].Children[1];
            var underQ = top[1].Children[0];

            Assert.AreSame(underP.Vertex, underQ.Vertex);
            Assert.AreEqual("<02>s", DisplayTextOf(underP));
            Assert.AreEqual("<01>s", DisplayTextOf(underQ));
            Assert.AreEqual("P > s", underP.FullPathText);
            Assert.AreEqual("Q > s", underQ.FullPathText);
            Assert.AreEqual(2, underP.Depth);
            Assert.AreSame(top[0], underP.Parent);
        }

        [TestMethod]
        public void DisplayText_MatchesMenuNodeFormat()
        {
            var nodes = TestGraphs.Small().CreateTopLevelNodes();
            var m = nodes[0].Children[1];

            Assert.AreEqual("<02>m [+]", DisplayTextOf(m));
            Assert.AreEqual("<01>1", DisplayTextOf(m.Children[0]));
            Assert.AreEqual("m", m.ToString());
        }

        private static string DisplayTextOf(ICascadeNode node) => node.DisplayText;

        [TestMethod]
        public void Children_AreCreatedOnceAndCached()
        {
            var a = TestGraphs.Small().CreateTopLevelNodes()[0];

            Assert.AreSame(a.Children, a.Children);
            Assert.AreSame(a.Children, ((ICascadeNode)a).Children);
        }

        [TestMethod]
        public void Cycle_CanBeBrowsedAndIsMarked()
        {
            var graph = new MenuGraph();
            graph.AddEdge("S", "A");
            graph.AddEdge("A", "B");
            graph.AddEdge("B", "A");

            var s = graph.CreateTopLevelNodes().Single();
            var a = s.Children.Single();
            var b = a.Children.Single();
            var again = b.Children.Single();

            Assert.IsFalse(a.IsRepeatedOnPath);
            Assert.IsFalse(b.IsRepeatedOnPath);
            Assert.IsTrue(again.IsRepeatedOnPath, "A 已經在路徑上");
            Assert.AreEqual("S > A > B > A", again.FullPathText);
            Assert.AreEqual(1, again.Children.Count, "仍然可以繼續展開，由呼叫端決定要不要停");
            Assert.AreEqual(4, again.Depth);
        }

        [TestMethod]
        public void FullPath_FromTopToSelf()
        {
            var leaf = TestGraphs.Small().CreateTopLevelNodes()[0].Children[2].Children[1];

            Assert.AreEqual("A,n,2", string.Join(",", leaf.FullPath.Select(node => node.Name)));
        }
    }
}