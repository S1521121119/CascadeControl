using CascadeControl.Graphs;
using CascadeControl.Graphs.Csv;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Graphs.Csv
{
    [TestClass]
    public class MenuGraphCsvTextTests
    {
        private static readonly MenuGraphRowStyle[] AllStyles =
            { MenuGraphRowStyle.Expanded, MenuGraphRowStyle.Compact, MenuGraphRowStyle.Outline };

        #region Parse

        [TestMethod]
        public void Parse_WithHeader_SkipsHeader()
        {
            var graph = MenuGraphCsvText.Parse("From,To\nA,B|C\n", out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("A,B,C", TestGraphs.Names(graph.Vertices));
        }

        [TestMethod]
        public void Parse_HeaderIsCaseInsensitive()
        {
            var graph = MenuGraphCsvText.Parse("from,to\nA,B\n", out _);

            Assert.IsFalse(graph.ContainsVertex("from"));
        }

        [TestMethod]
        public void Parse_WithoutHeader_AllRowsAreData()
        {
            var graph = MenuGraphCsvText.Parse("A,B\n", out var warnings);

            Assert.AreEqual(0, warnings.Count, "讀文字時表頭可有可無");
            Assert.AreEqual(1, graph.EdgeCount);
        }

        [TestMethod]
        public void Parse_IndentedTextWithBlankLines()
        {
            var graph = MenuGraphCsvText.Parse(@"
                From,        To
                Main,        System|Motor

                Info|Status, Version|Uptime
            ", out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("Main,System,Motor,Info,Status,Version,Uptime", TestGraphs.Names(graph.Vertices));
        }

        [TestMethod]
        public void Parse_WarningsUseActualLineNumbers()
        {
            MenuGraphCsvText.Parse("From,To\n\nA,B\n,C\n", out var warnings);

            StringAssert.StartsWith(warnings.Single(), "第 4 列：");
        }

        [TestMethod]
        public void Parse_NullOrEmpty_IsEmptyGraph()
        {
            Assert.IsTrue(MenuGraphCsvText.Parse(null, out var warnings).IsEmpty);
            Assert.AreEqual(0, warnings.Count);
            Assert.IsTrue(MenuGraphCsvText.Parse(string.Empty, out _).IsEmpty);
        }

        [TestMethod]
        public void Parse_SampleText_MatchesDesignNumbers()
        {
            var graph = MenuGraphCsvText.Parse(TestGraphs.SampleText(), out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(346, graph.VertexCount);
            Assert.AreEqual(610, graph.EdgeCount);
        }

        #endregion Parse

        #region Format

        [TestMethod]
        public void Format_EmptyGraph_HeaderOnly()
        {
            Assert.AreEqual("From,To" + Environment.NewLine, MenuGraphCsvText.Format(new MenuGraph()));
        }

        [TestMethod]
        public void Format_SmallGraph()
        {
            var nl = Environment.NewLine;

            Assert.AreEqual($"From,To{nl}A,x|m|n{nl}m|n,1|2{nl}Z,{nl}",
                MenuGraphCsvText.Format(TestGraphs.Small(), MenuGraphRowStyle.Compact));
        }

        [TestMethod]
        public void Format_SampleCompact_EqualsSampleText()
        {
            var graph = MenuGraphCsvText.Parse(TestGraphs.SampleText(), out _);

            Assert.AreEqual(TestGraphs.SampleText(), MenuGraphCsvText.Format(graph, MenuGraphRowStyle.Compact));
        }

        [TestMethod]
        public void Format_NameWithCommaAndQuote_RoundTrips()
        {
            var graph = new MenuGraph();
            graph.AddEdge("Set \"A\", fast", "Done");

            var back = MenuGraphCsvText.Parse(MenuGraphCsvText.Format(graph), out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestGraphs.Fingerprint(graph), TestGraphs.Fingerprint(back));
        }

        [TestMethod]
        public void Format_VertexNamedFrom_IsNotMistakenForHeader()
        {
            var graph = new MenuGraph();
            graph.AddEdge("From", "To");

            var back = MenuGraphCsvText.Parse(MenuGraphCsvText.Format(graph), out _);

            Assert.AreEqual(1, back.EdgeCount);
        }

        [TestMethod]
        public void Format_NameWithSurroundingSpaces_Throws()
        {
            var graph = new MenuGraph();
            graph.GetOrAddVertex(" A ");

            Assert.ThrowsException<ArgumentException>(() => MenuGraphCsvText.Format(graph));
        }

        [TestMethod]
        public void Format_TooManyEdges_Throws()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("x", TestTrees.Alternatives("a", 40000));
            graph.ExpandAndAddEdges("y", TestTrees.Alternatives("b", 30000));

            Assert.ThrowsException<ArgumentException>(() => MenuGraphCsvText.Format(graph));
        }

        [TestMethod]
        public void Format_Null_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => MenuGraphCsvText.Format(null));
        }

        /// <summary>
        /// 驗收條件：任何一種寫法寫出的文字，讀回後都符合讀回保證。
        /// </summary>
        [TestMethod]
        public void RandomGraphs_TextRoundTrip()
        {
            for (var seed = 0; seed < 300; seed++)
            {
                var graph = TestGraphs.Random(seed);

                foreach (var style in AllStyles)
                {
                    var back = MenuGraphCsvText.Parse(MenuGraphCsvText.Format(graph, style), out var warnings);

                    Assert.AreEqual(0, warnings.Count, $"seed {seed}, {style}");
                    Assert.AreEqual(TestGraphs.Fingerprint(graph), TestGraphs.Fingerprint(back), $"seed {seed}, {style}");
                }
            }
        }

        #endregion Format

        #region NormalizeRows

        [TestMethod]
        public void NormalizeRows_MergesDuplicatesAndExpands()
        {
            var rows = MenuGraphCsvText.NormalizeRows(new[]
            {
                new[] { "A", "B|C" },
                new[] { "A", "B" },
                new[] { "D", "" },
            }, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("A,B / A,C / D,", TestTrees.Describe(rows));
        }

        [TestMethod]
        public void NormalizeRows_WithStyle()
        {
            var rows = MenuGraphCsvText.NormalizeRows(new[]
            {
                new[] { "m", "1" },
                new[] { "n", "1|2" },
                new[] { "m", "2" },
                new[] { "S", "m|n" },
            }, MenuGraphRowStyle.Compact, out _);

            Assert.AreEqual("S,m|n / m|n,1|2", TestTrees.Describe(rows));
        }

        #endregion NormalizeRows

        [TestMethod]
        public void CreateHeader()
        {
            CollectionAssert.AreEqual(new[] { "From", "To" }, MenuGraphCsvText.CreateHeader());
        }
    }
}