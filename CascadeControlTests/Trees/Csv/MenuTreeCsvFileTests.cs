using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CascadeControlTests.Trees.Csv
{
    [TestClass]
    public class MenuTreeCsvFileTests
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
        public void Load_MissingFile_ReturnsEmptyTreeAndWritesTemplate()
        {
            var path = PathOf("menu.csv");

            var tree = MenuTreeCsvFile.Load(path, out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            StringAssert.Contains(warnings.Single(), "已建立空白範本");
            Assert.AreEqual("Level1", File.ReadAllText(path).Trim());
        }

        [TestMethod]
        public void Load_TemplateFailure_IsReportedAsWarning()
        {
            // 該放目錄的位置已經有同名的檔案，範本寫不出去，但不能丟例外。
            File.WriteAllText(PathOf("blocker"), string.Empty);
            var tree = MenuTreeCsvFile.Load(PathOf("blocker", "menu.csv"), out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            Assert.IsTrue(warnings.Any(w => w.StartsWith("建立範本失敗")), string.Join(Environment.NewLine, warnings));
        }

        [TestMethod]
        public void Load_FileWithHeader_ParsesWithoutWarnings()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, "Level1,Level2\r\nSystem,Reboot|Shutdown\r\n", new UTF8Encoding(true));

            var tree = MenuTreeCsvFile.Load(path, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(new[] { "System > Reboot", "System > Shutdown" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void Load_FileWithoutHeader_WarnsAndKeepsFirstRow()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, "System,Reboot\r\n");

            var tree = MenuTreeCsvFile.Load(path, out var warnings);

            StringAssert.Contains(warnings.Single(), "第一列不是表頭");
            CollectionAssert.AreEqual(new[] { "System > Reboot" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void Load_EmptyFile_Warns()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, " \r\n");

            var tree = MenuTreeCsvFile.Load(path, out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            StringAssert.Contains(warnings.Single(), "選單檔是空的");
        }

        [TestMethod]
        public void Load_WarningsUseFileLineNumbers()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, "Level1,Level2,Level3\r\n\r\nA,B\r\n\r\nA,,C\r\n");

            MenuTreeCsvFile.Load(path, out var warnings);

            StringAssert.StartsWith(warnings.Single(), "第 5 列：");
        }

        [TestMethod]
        public void Load_LockedFile_ReturnsEmptyTreeWithWarning()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, "Level1\r\nA\r\n");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var tree = MenuTreeCsvFile.Load(path, out var warnings);

                Assert.IsTrue(tree.IsEmpty);
                StringAssert.StartsWith(warnings.Single(), "選單檔讀取失敗");
            }
        }

        #endregion Load

        #region Save

        [TestMethod]
        public void Save_WritesUtf8WithBom()
        {
            var path = PathOf("menu.csv");

            MenuTreeCsvFile.Save(TestTrees.Template(), path);

            var bytes = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }

        [TestMethod]
        public void Save_ContentMatchesFormat()
        {
            var tree = TestTrees.Template();
            var expanded = PathOf("expanded.csv");
            var compact = PathOf("compact.csv");

            MenuTreeCsvFile.Save(tree, expanded);
            MenuTreeCsvFile.Save(tree, compact, MenuRowStyle.Compact);

            Assert.AreEqual(MenuTreeCsvText.Format(tree), File.ReadAllText(expanded));
            Assert.AreEqual(MenuTreeCsvText.Format(tree, MenuRowStyle.Compact), File.ReadAllText(compact));
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsInAllStyles()
        {
            var tree = TestTrees.Template();

            foreach (var style in new[] { MenuRowStyle.Expanded, MenuRowStyle.Compact, MenuRowStyle.Outline })
            {
                var path = PathOf($"menu_{style}.csv");
                MenuTreeCsvFile.Save(tree, path, style);

                var back = MenuTreeCsvFile.Load(path, out var warnings);

                Assert.AreEqual(0, warnings.Count, $"style={style}");
                Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back), $"style={style}");
            }
        }

        [TestMethod]
        public void Save_CreatesMissingDirectoriesAndOverwrites()
        {
            var path = PathOf("a", "b", "menu.csv");
            var first = new MenuTree();
            first.AddPath("Old");
            var second = new MenuTree();
            second.AddPath("New");

            MenuTreeCsvFile.Save(first, path);
            MenuTreeCsvFile.Save(second, path);

            CollectionAssert.AreEqual(new[] { "New" }, TestTrees.LeafPaths(MenuTreeCsvFile.Load(path, out _)));
        }

        [TestMethod]
        public void Save_InvalidTree_ThrowsBeforeTouchingDisk()
        {
            var tree = new MenuTree();
            tree.Root.GetOrAddChild("x|y"); // AddPath 會擋下 |，只有直接操作節點才造得出這種名稱
            var path = PathOf("sub", "menu.csv");

            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvFile.Save(tree, path));
            Assert.IsFalse(Directory.Exists(PathOf("sub")), "不該留下空目錄");
        }

        [TestMethod]
        public void Save_InvalidTree_KeepsExistingFile()
        {
            var path = PathOf("menu.csv");
            File.WriteAllText(path, "keep me");
            var tree = new MenuTree();
            tree.AddPath(" padded");

            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvFile.Save(tree, path, MenuRowStyle.Compact));
            Assert.AreEqual("keep me", File.ReadAllText(path));
        }

        [TestMethod]
        public void Save_NullTree_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => MenuTreeCsvFile.Save(null, PathOf("menu.csv")));
        }

        #endregion Save
    }
}