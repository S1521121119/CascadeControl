using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Trees.Csv
{
    [TestClass]
    public class MenuTreeCsvTextTests
    {
        private static readonly string NL = Environment.NewLine;

        #region Header

        [TestMethod]
        public void CreateHeader_ProducesLevelColumns()
        {
            CollectionAssert.AreEqual(new[] { "Level1", "Level2", "Level3" }, MenuTreeCsvText.CreateHeader(3));
            CollectionAssert.AreEqual(new[] { "Level1" }, MenuTreeCsvText.CreateHeader(0));
            CollectionAssert.AreEqual(new[] { "Level1" }, MenuTreeCsvText.CreateHeader(-5));
        }

        #endregion Header

        #region Parse

        [TestMethod]
        public void Parse_HandWrittenText_IgnoresIndentBlankLinesAndSpaces()
        {
            var tree = MenuTreeCsvText.Parse(@"
                System, Reboot|Shutdown

                Motor,  Axis X|Axis Y, Home
            ", out var warnings);

            Assert.AreEqual(0, warnings.Count);
            CollectionAssert.AreEqual(
                new[] { "System > Reboot", "System > Shutdown", "Motor > Axis X > Home", "Motor > Axis Y > Home" },
                TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void Parse_HeaderIsOptionalAndCaseInsensitive()
        {
            var withoutHeader = MenuTreeCsvText.Parse("A,B\nA,C", out var w1);
            var withHeader = MenuTreeCsvText.Parse("level1,LEVEL2\nA,B\nA,C", out var w2);

            Assert.AreEqual(0, w1.Count);
            Assert.AreEqual(0, w2.Count);
            Assert.AreEqual(TestTrees.Fingerprint(withoutHeader), TestTrees.Fingerprint(withHeader));
        }

        [TestMethod]
        public void Parse_HeaderAfterLeadingBlankLines_IsStillSkipped()
        {
            var tree = MenuTreeCsvText.Parse("\n\n   \nLevel1\nA", out var warnings);

            CollectionAssert.AreEqual(new[] { "A" }, TestTrees.LeafPaths(tree));
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Parse_OnlyFirstRowCanBeHeader()
        {
            var tree = MenuTreeCsvText.Parse("A\nLevel1", out _);

            CollectionAssert.AreEqual(new[] { "A", "Level1" }, TestTrees.LeafPaths(tree));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("  \r\n \n")]
        [DataRow("Level1,Level2")]
        public void Parse_NothingToRead_ReturnsEmptyTree(string text)
        {
            var tree = MenuTreeCsvText.Parse(text, out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Parse_WarningsUsePhysicalLineNumbers()
        {
            // 第 1、2、4、8 行是空行；第 6～7 行是同一個跨行的儲存格。
            var text = "\r\n\r\nLevel1\r\n\r\nA,,C\r\n\"multi\r\nline\",X|\r\n\r\nB, |Q\n";

            var tree = MenuTreeCsvText.Parse(text, out var warnings);

            CollectionAssert.AreEqual(new[] { "A" }, TestTrees.LeafPaths(tree));
            Assert.IsTrue(warnings.Any(w => w.StartsWith("第 5 列：第 2 層是空的")), string.Join(NL, warnings));
            Assert.IsTrue(warnings.Any(w => w.StartsWith("第 6 列：第 2 欄 'X|'")), string.Join(NL, warnings));
            Assert.IsTrue(warnings.Any(w => w.StartsWith("第 9 列：第 2 欄 '|Q'")), string.Join(NL, warnings));
        }

        [TestMethod]
        public void Parse_WithoutHeader_WarnsOnlyWhenAsked()
        {
            MenuTreeCsvText.Parse("A,B", out var quiet);
            var tree = MenuTreeCsvText.Parse("A,B", true, out var loud);

            Assert.AreEqual(0, quiet.Count);
            StringAssert.Contains(loud.Single(), "第一列不是表頭");
            CollectionAssert.AreEqual(new[] { "A > B" }, TestTrees.LeafPaths(tree), "不是表頭的第一列要當成資料");
        }

        [TestMethod]
        public void Parse_QuotedCells_KeepCommasQuotesAndNewlines()
        {
            var tree = MenuTreeCsvText.Parse("\"Say \"\"hi\"\", ok\",\"line1\nline2\"", out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual("Say \"hi\", ok > line1\nline2", tree.GetLeaves().Single().FullPathText);
        }

        #endregion Parse

        #region Format

        [TestMethod]
        public void Format_EmptyTree_IsHeaderOnly()
        {
            Assert.AreEqual("Level1" + NL, MenuTreeCsvText.Format(new MenuTree()));
            Assert.AreEqual("Level1" + NL, MenuTreeCsvText.Format(new MenuTree(), MenuRowStyle.Compact));
        }

        [TestMethod]
        public void Format_Expanded_IsOneRowPerLeafPaddedToHeight()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("System", "Reboot|Shutdown");
            tree.AddPath("System", "Info", "Version");

            var expected = "Level1,Level2,Level3" + NL
                + "System,Reboot," + NL
                + "System,Shutdown," + NL
                + "System,Info,Version" + NL;

            Assert.AreEqual(expected, MenuTreeCsvText.Format(tree));
        }

        [TestMethod]
        public void Format_Compact_MergesSiblings()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("System", "Reboot|Shutdown");
            tree.AddPath("System", "Info", "Version");

            var expected = "Level1,Level2,Level3" + NL
                + "System,Reboot|Shutdown," + NL
                + "System,Info,Version" + NL;

            Assert.AreEqual(expected, MenuTreeCsvText.Format(tree, MenuRowStyle.Compact));
        }

        [TestMethod]
        public void Format_EscapesFieldsPerRfc4180_AndParsesBack()
        {
            var tree = new MenuTree();
            tree.AddPath("Say \"hi\", ok", "line1\nline2");

            var text = MenuTreeCsvText.Format(tree, MenuRowStyle.Compact);
            var back = MenuTreeCsvText.Parse(text, out var warnings);

            StringAssert.Contains(text, "\"Say \"\"hi\"\", ok\"");
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back));
        }

        [TestMethod]
        public void Format_NullTree_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => MenuTreeCsvText.Format(null));
        }

        [DataTestMethod]
        [DataRow("A|B", DisplayName = "名稱含分隔字元")]
        [DataRow(" A", DisplayName = "名稱前有空白")]
        [DataRow("A\t", DisplayName = "名稱後有空白")]
        public void Format_NameThatCannotRoundTrip_Throws(string name)
        {
            var tree = new MenuTree();
            tree.AddPath("Root").GetOrAddChild(name); // AddPath 會擋下 |，改由節點直接加入

            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree));
            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree, MenuRowStyle.Compact));
        }

        [TestMethod]
        public void Format_MoreLeavesThanLimit_Throws()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths(
                TestTrees.Alternatives("p", 3), TestTrees.Alternatives("q", 5),
                TestTrees.Alternatives("r", 17), TestTrees.Alternatives("s", 257));
            MenuTreeCsvText.Format(tree, MenuRowStyle.Compact);   // 正好在上限，可以寫出

            tree.AddPath("extra");

            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree));
            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree, MenuRowStyle.Compact));
            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree, MenuRowStyle.Outline));
        }

        [TestMethod]
        public void Format_OutlineExpandsBeyondLimit_ThrowsWhileOtherStylesFit()
        {
            // 40,000 個葉節點在上限內，但大綱還要再加上 40,000 個中間節點那一列，展開後是 80,000 條。
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("Root", TestTrees.Alternatives("c", 40000), "leaf");

            MenuTreeCsvText.Format(tree);
            MenuTreeCsvText.Format(tree, MenuRowStyle.Compact);
            Assert.ThrowsException<ArgumentException>(() => MenuTreeCsvText.Format(tree, MenuRowStyle.Outline));
        }

        [TestMethod]
        public void Format_Outline_ReproducesHandWrittenFile()
        {
            // 手寫的大綱：同一層的兄弟擠在同一格，有下一層的再用自己的一列往下展開，RfStep1、RfStep2 的子樹相同所以共用一列。
            var handWritten = string.Join(NL,
                "Level1,Level2,Level3,Level4",
                "OpenStep,Init|Connect|Error,,",
                "ActionStep,Init|ActionPretest|CheckBurnInStart|ActionBurnIn|Last,,",
                "ActionStep,ActionPretest,Init|RfStep1|RfStep2|Analysis,",
                "ActionStep,ActionPretest,RfStep1|RfStep2,Init|GetHWAddr|Last",
                "ActionStep,ActionBurnIn,Init|RfStep1|Stop,",
                "ActionStep,ActionBurnIn,RfStep1,Init|WifiTestVerify|Last",
                "CloseStep,Init|Last,,") + NL;

            var tree = MenuTreeCsvText.Parse(handWritten, out var warnings);

            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(handWritten, MenuTreeCsvText.Format(tree, MenuRowStyle.Outline));
            Assert.AreEqual(handWritten, MenuTreeCsvText.Format(MenuTreeCsvText.Parse(MenuTreeCsvText.Format(tree), out _), MenuRowStyle.Outline),
                "由展開形式轉回大綱，要和手寫的一模一樣");
        }

        [TestMethod]
        public void FormatThenParse_RoundTripsInAllStyles()
        {
            foreach (var tree in Enumerable.Range(0, 200).Select(TestTrees.Random).Concat(new[] { TestTrees.Template() }))
            {
                var fingerprint = TestTrees.Fingerprint(tree);

                foreach (var style in new[] { MenuRowStyle.Expanded, MenuRowStyle.Compact, MenuRowStyle.Outline })
                {
                    var back = MenuTreeCsvText.Parse(MenuTreeCsvText.Format(tree, style), out var warnings);

                    Assert.AreEqual(0, warnings.Count);
                    Assert.AreEqual(fingerprint, TestTrees.Fingerprint(back), $"style={style}");
                }
            }
        }

        #endregion Format

        #region Sort

        [TestMethod]
        public void NormalizeRows_GroupsByFirstAppearanceAtEveryLevel()
        {
            var rows = new[] { new[] { "A", "B" }, new[] { "C", "X" }, new[] { "B", "Clea" }, new[] { "A", "X" } };

            var sorted = MenuTreeCsvText.NormalizeRows(rows, out var warnings);

            Assert.AreEqual("A,B / A,X / C,X / B,Clea", TestTrees.Describe(sorted));
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void NormalizeRows_DropsDuplicatesAndLeafThatWasExtended()
        {
            var rows = new[] { new[] { "A", "B", "C" }, new[] { "A", "B", "C", "Set" }, new[] { "A", "B", "C" } };

            Assert.AreEqual("A,B,C,Set", TestTrees.Describe(MenuTreeCsvText.NormalizeRows(rows, out _)));
        }

        [TestMethod]
        public void NormalizeRows_ExpandsAlternativesByDefault_AndCompactsWhenAsked()
        {
            var rows = new[] { new[] { "A", "x|y" }, new[] { "A", "z" } };

            Assert.AreEqual("A,x / A,y / A,z", TestTrees.Describe(MenuTreeCsvText.NormalizeRows(rows, out _)));
            Assert.AreEqual("A,x|y|z", TestTrees.Describe(MenuTreeCsvText.NormalizeRows(rows, MenuRowStyle.Compact, out _)));
        }

        [TestMethod]
        public void NormalizeRows_NullRows_ReturnsNothing()
        {
            Assert.AreEqual(0, MenuTreeCsvText.NormalizeRows(null, out var warnings).Length);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void NormalizeRows_Compact_IsIdempotentOnRandomInput()
        {
            var rng = new Random(42);

            for (var k = 0; k < 500; k++)
            {
                var rows = Enumerable.Range(0, rng.Next(1, 15)).Select(_ =>
                    Enumerable.Range(0, rng.Next(1, 4)).Select(__ =>
                        string.Join("|", Enumerable.Range(0, rng.Next(1, 3)).Select(___ => "xyz"[rng.Next(3)].ToString()).Distinct()))
                    .ToArray()).ToArray();

                var once = MenuTreeCsvText.NormalizeRows(rows, MenuRowStyle.Compact, out _);
                var twice = MenuTreeCsvText.NormalizeRows(once, MenuRowStyle.Compact, out _);

                Assert.AreEqual(TestTrees.Describe(once), TestTrees.Describe(twice), $"case {k}");
                Assert.AreEqual(
                    TestTrees.Describe(MenuTreeCsvText.NormalizeRows(rows, out _)),
                    TestTrees.Describe(MenuTreeCsvText.NormalizeRows(once, out _)),
                    $"case {k}：壓縮後應該是同一棵樹");
            }
        }

        #endregion Sort
    }
}