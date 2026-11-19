using CascadeControl.Common;
using CascadeControlTests.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace CascadeControlTests.Common
{
    [TestClass]
    public class CsvFileTests
    {
        private static readonly string NL = Environment.NewLine;

        private static string Describe(string[][] rows) => TestTrees.Describe(rows);

        #region ParseRows

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void ParseRows_NullOrEmpty_ReturnsNoRows(string input)
        {
            Assert.IsTrue(CsvFile.ParseRows(input, out var data, out var lines));
            Assert.AreEqual(0, data.Length);
            Assert.AreEqual(0, lines.Length);
        }

        [TestMethod]
        public void ParseRows_TrimsCellsAndKeepsEmptyCells()
        {
            CsvFile.ParseRows(" a , b ,, c ", out var data);

            CollectionAssert.AreEqual(new[] { "a", "b", "", "c" }, data.Single());
        }

        [TestMethod]
        public void ParseRows_QuotedFields_KeepCommasEscapedQuotesAndNewlines()
        {
            CsvFile.ParseRows("\"a,b\",\"say \"\"hi\"\"\",\"x\r\ny\"\nnext", out var data);

            Assert.AreEqual(2, data.Length);
            CollectionAssert.AreEqual(new[] { "a,b", "say \"hi\"", "x\r\ny" }, data[0]);
            CollectionAssert.AreEqual(new[] { "next" }, data[1]);
        }

        [TestMethod]
        public void ParseRows_SkipsBlankRowsIncludingWhitespaceOnlyLastRow()
        {
            CsvFile.ParseRows("a\n\n , \nb\n   ", out var data);

            Assert.AreEqual("a / b", Describe(data));
        }

        [DataTestMethod]
        [DataRow("\r\n", DisplayName = "CRLF")]
        [DataRow("\n", DisplayName = "LF")]
        [DataRow("\r", DisplayName = "CR")]
        public void ParseRows_LineNumbers_CountEveryNewlineStyle(string nl)
        {
            var input = string.Join(nl, "a", "", "b", " ", "", "c");

            CsvFile.ParseRows(input, out var data, out var lines);

            Assert.AreEqual("a / b / c", Describe(data));
            CollectionAssert.AreEqual(new[] { 1, 3, 6 }, lines);
        }

        [DataTestMethod]
        [DataRow("\r\n", DisplayName = "CRLF")]
        [DataRow("\n", DisplayName = "LF")]
        [DataRow("\r", DisplayName = "CR")]
        public void ParseRows_LineNumbers_CountNewlinesInsideQuotes(string nl)
        {
            var input = $"a{nl}\"one{nl}two{nl}three\",x{nl}b";

            CsvFile.ParseRows(input, out var data, out var lines);

            Assert.AreEqual(3, data.Length);
            CollectionAssert.AreEqual(new[] { 1, 2, 5 }, lines, "跨行的儲存格佔三行，下一列從第 5 行開始");
        }

        [TestMethod]
        public void ParseRows_TwoArgumentOverload_MatchesLineNumberOverload()
        {
            const string input = "a,b\n\n\"c\nd\",e\n";

            CsvFile.ParseRows(input, out var withoutLines);
            CsvFile.ParseRows(input, out var withLines, out _);

            Assert.AreEqual(Describe(withLines), Describe(withoutLines));
        }

        #endregion ParseRows

        #region FormatRowsWithHeader / WriteFileWithHeader

        [TestMethod]
        public void FormatRowsWithHeader_PadsAndTruncatesRowsToHeaderWidth()
        {
            var text = CsvFile.FormatRowsWithHeader(
                new[] { "H1", "H2", "H3" },
                new[] { new[] { "a" }, new[] { "a", "b", "c", "extra" } });

            Assert.AreEqual("H1,H2,H3" + NL + "a,," + NL + "a,b,c" + NL, text);
        }

        [TestMethod]
        public void FormatRowsWithHeader_NullData_WritesHeaderOnly()
        {
            Assert.AreEqual("H" + NL, CsvFile.FormatRowsWithHeader(new[] { "H" }, null));
        }

        [TestMethod]
        public void FormatRowsWithHeader_EscapesSpecialCharactersAndTrims()
        {
            var text = CsvFile.FormatRowsWithHeader(
                new[] { "H1", "H2", "H3", "H4" },
                new[] { new[] { " plain ", "a,b", "say \"hi\"", "x\ny" } });

            Assert.AreEqual("H1,H2,H3,H4" + NL + "plain,\"a,b\",\"say \"\"hi\"\"\",\"x\ny\"" + NL, text);
        }

        [TestMethod]
        public void FormatRowsThenParseRows_RoundTrips()
        {
            var header = new[] { "H1", "H2" };
            var data = new[] { new[] { "a,b", "say \"hi\"" }, new[] { "x\r\ny", "" } };

            CsvFile.ParseRows(CsvFile.FormatRowsWithHeader(header, data), out var back);

            Assert.AreEqual(Describe(new[] { header }.Concat(data).ToArray()), Describe(back));
        }

        [TestMethod]
        public void WriteFileWithHeader_WritesFormatWithBomAndReadsBack()
        {
            var path = Path.Combine(Path.GetTempPath(), "CsvFileTests_" + Guid.NewGuid().ToString("N") + ".csv");
            var header = new[] { "H1", "H2" };
            var data = new[] { new[] { "a", "b,c" } };

            try
            {
                var csv = new CsvFile();
                csv.WriteFileWithHeader(path, header, data);

                var bytes = File.ReadAllBytes(path);
                CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                Assert.AreEqual(CsvFile.FormatRowsWithHeader(header, data), File.ReadAllText(path));

                Assert.IsTrue(csv.ReadFileWithHeader(path, out var readHeader, out var readData));
                CollectionAssert.AreEqual(header, readHeader);
                Assert.AreEqual(Describe(data), Describe(readData));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ReadFile_MissingFile_Throws()
        {
            var path = Path.Combine(Path.GetTempPath(), "CsvFileTests_missing_" + Guid.NewGuid().ToString("N") + ".csv");

            Assert.ThrowsException<FileNotFoundException>(() => new CsvFile().ReadFile(path, out _));
        }

        #endregion FormatRowsWithHeader / WriteFileWithHeader
    }
}