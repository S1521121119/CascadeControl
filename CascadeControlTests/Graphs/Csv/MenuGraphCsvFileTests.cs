using CascadeControl.Graphs;
using CascadeControl.Graphs.Csv;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CascadeControlTests.Graphs.Csv
{
    [TestClass]
    public class MenuGraphCsvFileTests
    {
        private string _directory;

        [TestInitialize]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "CascadeControlTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TestCleanup]
        public void DeleteDirectory()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private string PathOf(params string[] parts) => Path.Combine(new[] { _directory }.Concat(parts).ToArray());

        #region Load

        [TestMethod]
        public void Load_MissingFile_ReturnsEmptyGraphAndWritesTemplate()
        {
            var path = PathOf("graph.csv");

            var graph = MenuGraphCsvFile.Load(path, out var warnings);

            Assert.IsTrue(graph.IsEmpty);
            StringAssert.Contains(warnings.Single(), "已建立空白範本");
            Assert.AreEqual("From,To", File.ReadAllText(path).Trim());
        }

        [TestMethod]
        public void Load_TemplateFailure_IsReportedAsWarning()
        {
            // 該放目錄的位置已經有同名的檔案，範本寫不出去，但不能丟例外。
            File.WriteAllText(PathOf("blocker"), string.Empty);
            var graph = MenuGraphCsvFile.Load(PathOf("blocker", "graph.csv"), out var warnings);

            Assert.IsTrue(graph.IsEmpty);
            Assert.IsTrue(warnings.Any(w => w.StartsWith("建立範本失敗")), string.Join(Environment.NewLine, warnings));
        }

        [TestMethod]
        public void Load_FileWithHeader_ParsesWithoutWarnings()
        {
            var path = PathOf("graph.csv");
            File.WriteAllText(path, TestGraphs.SampleText(), new UTF8Encoding(true));

            var graph = MenuGraphCsvFile.Load(path, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(610, graph.EdgeCount);
        }

        [TestMethod]
        public void Load_FileWithoutHeader_WarnsAndKeepsData()
        {
            var path = PathOf("graph.csv");
            File.WriteAllText(path, "A,B\n");

            var graph = MenuGraphCsvFile.Load(path, out var warnings);

            Assert.AreEqual(1, graph.EdgeCount);
            StringAssert.StartsWith(warnings.Single(), "第一列不是表頭");
        }

        [TestMethod]
        public void Load_EmptyFile_Warns()
        {
            var path = PathOf("graph.csv");
            File.WriteAllText(path, "  \r\n");

            var graph = MenuGraphCsvFile.Load(path, out var warnings);

            Assert.IsTrue(graph.IsEmpty);
            StringAssert.StartsWith(warnings.Single(), "圖檔是空的");
        }

        [TestMethod]
        public void Load_LockedFile_IsReportedAsWarning()
        {
            var path = PathOf("graph.csv");
            File.WriteAllText(path, "From,To\nA,B\n");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var graph = MenuGraphCsvFile.Load(path, out var warnings);

                Assert.IsTrue(graph.IsEmpty);
                StringAssert.StartsWith(warnings.Single(), "圖檔讀取失敗");
            }
        }

        #endregion Load

        #region Save

        [TestMethod]
        public void Save_CreatesDirectoryAndWritesBom()
        {
            var path = PathOf("sub", "graph.csv");

            MenuGraphCsvFile.Save(TestGraphs.Small(), path, MenuGraphRowStyle.Outline);

            var bytes = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

            var back = MenuGraphCsvFile.Load(path, out var warnings);
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestGraphs.Fingerprint(TestGraphs.Small()), TestGraphs.Fingerprint(back));
        }

        [TestMethod]
        public void Save_InvalidGraph_WritesNothing()
        {
            var graph = new MenuGraph();
            graph.GetOrAddVertex(" A ");
            var path = PathOf("sub", "graph.csv");

            Assert.ThrowsException<ArgumentException>(() => MenuGraphCsvFile.Save(graph, path));
            Assert.IsFalse(Directory.Exists(PathOf("sub")), "寧可整個不寫，也不要留下空目錄");
        }

        #endregion Save
    }
}