using CascadeControl.Common;
using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Trees
{
    [TestClass]
    public class MenuTreeTests
    {
        #region AddPath

        [TestMethod]
        public void AddPath_KeepsFirstInsertionOrderAndMergesDuplicates()
        {
            var tree = new MenuTree();
            tree.AddPath("B", "X");
            tree.AddPath("A", "Y");
            tree.AddPath("B", "Z");
            tree.AddPath("B", "X");

            CollectionAssert.AreEqual(new[] { "B > X", "B > Z", "A > Y" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void AddPath_ExtendingLeaf_TurnsItIntoBranch()
        {
            var tree = new MenuTree();
            var leaf = tree.AddPath("A", "B");
            tree.AddPath("A", "B", "C");

            Assert.IsFalse(leaf.IsLeaf);
            CollectionAssert.AreEqual(new[] { "A > B > C" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void AddPath_DoesNotTrimNames()
        {
            var tree = new MenuTree();
            Assert.AreEqual(" A ", tree.AddPath(" A ").Name);
        }

        [TestMethod]
        public void AddPath_InvalidInput_ThrowsWithoutTouchingTree()
        {
            var tree = new MenuTree();

            Assert.ThrowsException<ArgumentNullException>(() => tree.AddPath((string[])null));
            Assert.ThrowsException<ArgumentException>(() => tree.AddPath());
            Assert.ThrowsException<ArgumentException>(() => tree.AddPath("A", " "));
            Assert.ThrowsException<ArgumentException>(() => tree.AddPath("A", null));
            Assert.ThrowsException<ArgumentException>(() => tree.AddPath("Motor", "Axis X|Axis Y", "Home"));
            Assert.IsTrue(tree.IsEmpty, "寧可不加也不要留下半條路徑");
        }

        [TestMethod]
        public void AddPath_NameWithSeparator_ExplainsExpandAndAddPaths()
        {
            var tree = new MenuTree();

            var ex = Assert.ThrowsException<ArgumentException>(() => tree.AddPath("Motor", "Axis X|Axis Y"));

            StringAssert.Contains(ex.Message, "第 2 層");
            StringAssert.Contains(ex.Message, "ExpandAndAddPaths");
        }

        #endregion AddPath

        #region ExpandAndAddPaths

        [TestMethod]
        public void ExpandAndAddPaths_ExpandsCartesianProductWithRightmostFastest()
        {
            var tree = new MenuTree();

            var ends = tree.ExpandAndAddPaths("Motor", "Axis X|Axis Y", "Home|Jog");

            CollectionAssert.AreEqual(
                new[] { "Motor > Axis X > Home", "Motor > Axis X > Jog", "Motor > Axis Y > Home", "Motor > Axis Y > Jog" },
                ends.Select(n => n.FullPathText).ToArray());
            CollectionAssert.AreEqual(ends.ToArray(), tree.GetLeaves().ToArray());
        }

        [TestMethod]
        public void ExpandAndAddPaths_TrimsEveryName()
        {
            var tree = new MenuTree();

            tree.ExpandAndAddPaths(" Motor ", " Axis X | Axis Y ");

            CollectionAssert.AreEqual(new[] { "Motor > Axis X", "Motor > Axis Y" }, TestTrees.LeafPaths(tree));
        }

        [TestMethod]
        public void ExpandAndAddPaths_WithoutSeparator_BehavesLikeAddPath()
        {
            var tree = new MenuTree();

            var ends = tree.ExpandAndAddPaths(new List<string> { "A", "B" });

            Assert.AreEqual("A > B", ends.Single().FullPathText);
        }

        [DataTestMethod]
        [DataRow("A||B")]
        [DataRow("|A")]
        [DataRow("A|")]
        [DataRow("A| |B")]
        [DataRow(" ")]
        public void ExpandAndAddPaths_InvalidCell_ThrowsWithoutTouchingTree(string cell)
        {
            var tree = new MenuTree();

            Assert.ThrowsException<ArgumentException>(() => tree.ExpandAndAddPaths("Root", "X|Y", cell));
            Assert.IsTrue(tree.IsEmpty);
        }

        [TestMethod]
        public void ExpandAndAddPaths_NullOrEmpty_Throws()
        {
            var tree = new MenuTree();

            Assert.ThrowsException<ArgumentNullException>(() => tree.ExpandAndAddPaths((string[])null));
            Assert.ThrowsException<ArgumentException>(() => tree.ExpandAndAddPaths());
            Assert.ThrowsException<ArgumentException>(() => tree.ExpandAndAddPaths("A", null));
        }

        [TestMethod]
        public void ExpandAndAddPaths_OverLimit_ThrowsWithoutTouchingTree()
        {
            var tree = new MenuTree();

            // 256 × 256 = 65,536，比上限多 1。
            Assert.ThrowsException<ArgumentException>(() =>
                tree.ExpandAndAddPaths(TestTrees.Alternatives("a", 256), TestTrees.Alternatives("b", 256)));
            Assert.IsTrue(tree.IsEmpty);
        }

        [TestMethod]
        public void ExpandAndAddPaths_ExactlyAtLimit_Succeeds()
        {
            var tree = new MenuTree();

            // 3 × 5 × 17 × 257 = 65,535。
            var ends = tree.ExpandAndAddPaths(
                TestTrees.Alternatives("p", 3), TestTrees.Alternatives("q", 5),
                TestTrees.Alternatives("r", 17), TestTrees.Alternatives("s", 257));

            Assert.AreEqual(MultiNameCellSyntax.MAX_EXPANDED_ROWS, ends.Count);
            Assert.AreEqual(MultiNameCellSyntax.MAX_EXPANDED_ROWS, tree.GetLeaves().Count());
        }

        #endregion ExpandAndAddPaths

        #region 查詢

        [TestMethod]
        public void EmptyTree_Queries()
        {
            var tree = new MenuTree();

            Assert.IsTrue(tree.IsEmpty);
            Assert.AreEqual(0, tree.Height);
            Assert.AreEqual(0, tree.GetLeaves().Count());
            Assert.AreEqual(0, tree.ToRows().Length);
            Assert.AreEqual(0, tree.ToRows(MenuRowStyle.Compact).Length);
        }

        [TestMethod]
        public void HeightAndTopLevelNodes()
        {
            var tree = TestTrees.Template();

            Assert.AreEqual(6, tree.Height);
            CollectionAssert.AreEqual(new[] { "System", "Motor", "IO", "Diagnostics" }, tree.TopLevelNodes.Select(n => n.Name).ToArray());
            Assert.AreEqual(77, tree.GetLeaves().Count());
        }

        [TestMethod]
        public void ToRows_PadsEveryRowToHeight()
        {
            var tree = new MenuTree();
            tree.AddPath("A");
            tree.AddPath("B", "C", "D");

            Assert.AreEqual("A,, / B,C,D", TestTrees.Describe(tree.ToRows()));
            Assert.IsTrue(tree.ToRows(MenuRowStyle.Compact).All(row => row.Length == 3));
        }

        #endregion 查詢

        #region FromRows

        [TestMethod]
        public void FromRows_NullRows_ReturnsEmptyTreeWithoutWarnings()
        {
            var tree = MenuTree.FromRows(null, out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void FromRows_TrimsCellsAndIgnoresTrailingBlanks()
        {
            var tree = MenuTree.FromRows(new[] { new[] { " A ", " B ", "", " " }, null, new string[0] }, out var warnings);

            CollectionAssert.AreEqual(new[] { "A > B" }, TestTrees.LeafPaths(tree));
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void FromRows_Gap_KeepsPrefixAndWarnsWithLineNumber()
        {
            var tree = MenuTree.FromRows(new[] { new[] { "A", "", "C" } }, new[] { 7 }, out var warnings);

            CollectionAssert.AreEqual(new[] { "A" }, TestTrees.LeafPaths(tree));
            StringAssert.StartsWith(warnings.Single(), "第 7 列：第 2 層是空的");
        }

        [TestMethod]
        public void FromRows_WithoutLineNumbers_UsesSequenceNumbers()
        {
            MenuTree.FromRows(new[] { new[] { "A" }, new[] { "B", "", "C" } }, out var warnings);

            StringAssert.StartsWith(warnings.Single(), "第 2 列：");
        }

        [TestMethod]
        public void FromRows_LineNumberCountMismatch_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                MenuTree.FromRows(new[] { new[] { "A" } }, new[] { 1, 2 }, out _));
        }

        [TestMethod]
        public void FromRows_ExpandsAlternatives()
        {
            var tree = MenuTree.FromRows(new[] { new[] { "Device", "Info|Status", "Version|Uptime" } }, out var warnings);

            CollectionAssert.AreEqual(
                new[] { "Device > Info > Version", "Device > Info > Uptime", "Device > Status > Version", "Device > Status > Uptime" },
                TestTrees.LeafPaths(tree));
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void FromRows_WarningRepeatedByExpansion_IsReportedOnce()
        {
            // 這一列展開成 3 條，每一條都缺第 3 層。
            MenuTree.FromRows(new[] { new[] { "A", "x|y|z", "", "D" } }, out var warnings);

            Assert.AreEqual(1, warnings.Count);
        }

        [TestMethod]
        public void FromRows_InvalidAlternative_SkipsOnlyThatRow()
        {
            var tree = MenuTree.FromRows(new[] { new[] { "A", "x||y" }, new[] { "B" } }, out var warnings);

            CollectionAssert.AreEqual(new[] { "B" }, TestTrees.LeafPaths(tree));
            StringAssert.Contains(warnings.Single(), "整列已跳過");
        }

        #endregion FromRows
    }
}