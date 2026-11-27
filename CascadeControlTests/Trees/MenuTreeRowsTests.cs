using CascadeControl.Common;
using CascadeControl.Trees;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Trees
{
    /// <summary>
    /// 樹的資料列：<see cref="MenuTree.FromRows(System.Collections.Generic.IEnumerable{string[]}, out System.Collections.Generic.IList{string})"/> 的展開上限，
    /// 以及 <see cref="MenuTree.ToRows(MenuRowStyle)"/> 的壓縮與大綱寫法。
    /// </summary>
    [TestClass]
    public class MenuTreeRowsTests
    {
        #region 展開上限

        [TestMethod]
        public void FromRows_SingleRowOverLimit_SkipsThatRowOnly()
        {
            var rows = new[]
            {
                new[] { TestTrees.Alternatives("a", 256), TestTrees.Alternatives("b", 256) },   // 65,536
                new[] { "ok" },
            };

            var tree = MenuTree.FromRows(rows, out var warnings);

            CollectionAssert.AreEqual(new[] { "ok" }, TestTrees.LeafPaths(tree));
            StringAssert.Contains(warnings.Single(), "65536");
        }

        [TestMethod]
        public void FromRows_TotalOverLimit_AbandonsEverything()
        {
            var rows = new[]
            {
                new[] { "x", TestTrees.Alternatives("a", 40000) },
                new[] { "y", TestTrees.Alternatives("b", 30000) },
            };

            var tree = MenuTree.FromRows(rows, out var warnings);

            Assert.IsTrue(tree.IsEmpty);
            StringAssert.Contains(warnings.Single(), "已放棄整份內容");
        }

        [TestMethod]
        public void FromRows_TotalExactlyAtLimit_IsAccepted()
        {
            var rows = new[]
            {
                new[] { "x", TestTrees.Alternatives("a", 40000) },
                new[] { "y", TestTrees.Alternatives("b", 25535) },
            };

            var tree = MenuTree.FromRows(rows, out var warnings);

            Assert.AreEqual(CellExpansion.MAX_EXPANDED_ROWS, tree.GetLeaves().Count());
            Assert.AreEqual(0, warnings.Count);
        }

        #endregion 展開上限

        #region 壓縮（ToRows(MenuRowStyle.Compact)）

        [TestMethod]
        public void ToCompactRows_ProposalExample()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("Device", "Info|Status", "Version|Uptime|Memory");
            tree.AddPath("Device", "Reset");
            tree.AddPath("Network", "Ping");

            Assert.AreEqual(
                "Device,Info|Status,Version|Uptime|Memory / Device,Reset, / Network,Ping,",
                TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_AdjacentLeaves_MergeIntoOneCell()
        {
            var tree = new MenuTree();
            tree.AddPath("System", "Reboot");
            tree.AddPath("System", "Shutdown");
            tree.AddPath("System", "Info", "Version");

            Assert.AreEqual("System,Reboot|Shutdown, / System,Info,Version", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_IdenticalButNotAdjacent_AreNotMerged()
        {
            // A 與 C 的子樹相同，但中間隔著 B；合併會改變兄弟節點的順序。
            var tree = new MenuTree();
            tree.AddPath("A", "x");
            tree.AddPath("B", "y");
            tree.AddPath("C", "x");

            Assert.AreEqual("A,x / B,y / C,x", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_SubtreesDifferingInOrder_AreNotMerged()
        {
            var tree = new MenuTree();
            tree.AddPath("A", "x");
            tree.AddPath("A", "y");
            tree.AddPath("B", "y");
            tree.AddPath("B", "x");

            Assert.AreEqual("A,x|y / B,y|x", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_SubtreesDifferingDeepDown_AreNotMerged()
        {
            var tree = new MenuTree();
            tree.AddPath("A", "m", "1");
            tree.AddPath("B", "m", "2");

            Assert.AreEqual("A,m,1 / B,m,2", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_BranchWithSeveralChildGroups_RepeatsMergedPrefix()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("X|Y", "a|b");
            tree.ExpandAndAddPaths("X|Y", "c", "deep");

            Assert.AreEqual("X|Y,a|b, / X|Y,c,deep", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_LeafAndBranchSiblings_AreNotMerged()
        {
            var tree = new MenuTree();
            tree.AddPath("A");
            tree.AddPath("B", "x");

            Assert.AreEqual("A, / B,x", TestTrees.Describe(tree.ToRows(MenuRowStyle.Compact)));
        }

        [TestMethod]
        public void ToCompactRows_NameWithSeparator_Throws()
        {
            var tree = new MenuTree();
            tree.Root.GetOrAddChild("A|B"); // AddPath 會擋下 |，只有直接操作節點才造得出這種名稱

            Assert.ThrowsException<ArgumentException>(() => tree.ToRows(MenuRowStyle.Compact));
        }

        [TestMethod]
        public void ToCompactRows_DeepChain_DoesNotOverflowStack()
        {
            // 走訪用明確的堆疊，深度沒有上限。
            var tree = new MenuTree();
            tree.AddPath(Enumerable.Range(1, 20000).Select(i => "L" + i));

            var rows = tree.ToRows(MenuRowStyle.Compact);

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual("L20000", rows[0][19999]);
        }

        [TestMethod]
        public void ToCompactRows_TemplateTree_MatchesHandWrittenRows()
        {
            var expected = string.Join(" / ",
                "System,Reboot|Shutdown,,,,",
                "System,Info,Version|Uptime|Memory,,,",
                "Motor,Axis X|Axis Y|Axis Z,Home,,,",
                "Motor,Axis X|Axis Y|Axis Z,Jog,Forward|Backward,,",
                "Motor,Axis X|Axis Y|Axis Z,Speed,Low|Mid|High,,",
                "IO,Input," + TestTrees.Alternatives("DI", 32) + ",,,",
                "IO,Output," + TestTrees.Alternatives("DO", 8) + ",On|Off,,",
                "Diagnostics,Sensor,Temperature,Zone A|Zone B,Read,Raw|Calibrated",
                "Diagnostics,Log,Export|Clear,,,");

            Assert.AreEqual(expected, TestTrees.Describe(TestTrees.Template().ToRows(MenuRowStyle.Compact)));
        }

        /// <summary>
        /// 驗收條件：壓縮後再展開必須還原成同一棵樹（結構與順序都相同），而且列與列不重疊。
        /// </summary>
        [TestMethod]
        public void ToCompactRows_RandomTrees_RoundTripAndRowsDoNotOverlap()
        {
            var merged = 0;

            for (var seed = 0; seed < 1000; seed++)
            {
                var tree = TestTrees.Random(seed);
                var rows = tree.ToRows(MenuRowStyle.Compact);
                if (rows.Any(row => row.Any(cell => cell.IndexOf(CellExpansion.NAME_SEPARATOR) >= 0))) merged++;

                var back = MenuTree.FromRows(rows, out var warnings);

                Assert.AreEqual(0, warnings.Count, $"seed {seed}");
                Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back), $"seed {seed}");
                Assert.AreEqual(tree.GetLeaves().Count(), rows.Sum(TestTrees.ExpansionOf), $"seed {seed}：列與列重疊");
            }

            Assert.IsTrue(merged > 500, "隨機樹應該大多含有可合併的兄弟節點，否則這個測試沒有意義");
        }

        /// <summary>
        /// 驗收條件：已壓縮的結果再壓一次不變（同一棵樹只有一種寫法）。
        /// </summary>
        [TestMethod]
        public void ToCompactRows_RandomTrees_IsIdempotent()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                var once = TestTrees.Random(seed).ToRows(MenuRowStyle.Compact);
                var twice = MenuTree.FromRows(once, out _).ToRows(MenuRowStyle.Compact);

                Assert.AreEqual(TestTrees.Describe(once), TestTrees.Describe(twice), $"seed {seed}");
            }
        }

        [TestMethod]
        public void ToCompactRows_TreeAtLimit_FitsInOneRowAndRoundTrips()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths(
                TestTrees.Alternatives("p", 3), TestTrees.Alternatives("q", 5),
                TestTrees.Alternatives("r", 17), TestTrees.Alternatives("s", 257));

            var rows = tree.ToRows(MenuRowStyle.Compact);
            var back = MenuTree.FromRows(rows, out var warnings);

            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back));
        }

        #endregion 壓縮（ToRows(MenuRowStyle.Compact)）

        #region 大綱（ToRows(MenuRowStyle.Outline)）

        [TestMethod]
        public void ToOutlineRows_ParentRowListsAllChildren_BranchesGetOwnRow()
        {
            var tree = new MenuTree();
            tree.AddPath("A", "x");
            tree.AddPath("A", "m", "1");
            tree.AddPath("A", "y");

            Assert.AreEqual("A,x|m|y, / A,m,1", TestTrees.Describe(tree.ToRows(MenuRowStyle.Outline)));
        }

        [TestMethod]
        public void ToOutlineRows_IdenticalSubtrees_MergeEvenWhenNotAdjacent()
        {
            // 順序已經由父節點那一列決定，所以中間隔著 q 也能合併。
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("A", "p", "1|2");
            tree.AddPath("A", "q");
            tree.ExpandAndAddPaths("A", "r", "1|2");

            Assert.AreEqual("A,p|q|r, / A,p|r,1|2", TestTrees.Describe(tree.ToRows(MenuRowStyle.Outline)));
        }

        [TestMethod]
        public void ToOutlineRows_DifferentSubtrees_AreNotMerged()
        {
            var tree = new MenuTree();
            tree.AddPath("A", "m", "1");
            tree.AddPath("A", "n", "2");

            Assert.AreEqual("A,m|n, / A,m,1 / A,n,2", TestTrees.Describe(tree.ToRows(MenuRowStyle.Outline)));
        }

        [TestMethod]
        public void ToOutlineRows_TopLevelNodes_MergesAdjacentOnly()
        {
            // 第一層沒有父節點那一列可以決定順序，A 與 D 的子樹相同也不能合併。
            var tree = new MenuTree();
            tree.AddPath("A", "x");
            tree.AddPath("B", "x");
            tree.AddPath("C", "y");
            tree.AddPath("D", "x");
            tree.AddPath("Leaf1");
            tree.AddPath("Leaf2");

            Assert.AreEqual("A|B,x / C,y / D,x / Leaf1|Leaf2,", TestTrees.Describe(tree.ToRows(MenuRowStyle.Outline)));
        }

        [TestMethod]
        public void ToOutlineRows_EmptyTree_HasNoRows()
        {
            Assert.AreEqual(0, new MenuTree().ToRows(MenuRowStyle.Outline).Length);
        }

        [TestMethod]
        public void ToOutlineRows_NameWithSeparator_Throws()
        {
            var tree = new MenuTree();
            tree.AddPath("Root").GetOrAddChild("A|B");

            Assert.ThrowsException<ArgumentException>(() => tree.ToRows(MenuRowStyle.Outline));
        }

        [TestMethod]
        public void ToOutlineRows_DeepChain_DoesNotOverflowStack()
        {
            var tree = new MenuTree();
            tree.AddPath(Enumerable.Range(1, 20000).Select(i => "L" + i));

            var rows = tree.ToRows(MenuRowStyle.Outline);

            Assert.AreEqual(19999, rows.Length, "每個中間節點一列");
            Assert.AreEqual("L20000", rows[19998][19999]);
        }

        [TestMethod]
        public void ToOutlineRows_TemplateTree_MatchesHandWrittenRows()
        {
            var axes = "Axis X|Axis Y|Axis Z";
            var dis = TestTrees.Alternatives("DI", 32);
            var dos = TestTrees.Alternatives("DO", 8);
            var expected = string.Join(" / ",
                "System,Reboot|Shutdown|Info,,,,",
                "System,Info,Version|Uptime|Memory,,,",
                $"Motor,{axes},,,,",
                $"Motor,{axes},Home|Jog|Speed,,,",
                $"Motor,{axes},Jog,Forward|Backward,,",
                $"Motor,{axes},Speed,Low|Mid|High,,",
                "IO,Input|Output,,,,",
                $"IO,Input,{dis},,,",
                $"IO,Output,{dos},,,",
                $"IO,Output,{dos},On|Off,,",
                "Diagnostics,Sensor|Log,,,,",
                "Diagnostics,Sensor,Temperature,,,",
                "Diagnostics,Sensor,Temperature,Zone A|Zone B,,",
                "Diagnostics,Sensor,Temperature,Zone A|Zone B,Read,",
                "Diagnostics,Sensor,Temperature,Zone A|Zone B,Read,Raw|Calibrated",
                "Diagnostics,Log,Export|Clear,,,");

            Assert.AreEqual(expected, TestTrees.Describe(TestTrees.Template().ToRows(MenuRowStyle.Outline)));
        }

        /// <summary>
        /// 驗收條件：大綱讀回必須是同一棵樹（結構與順序都相同），而且展開後每個節點剛好出現一次（第一層的中間節點除外）。
        /// </summary>
        [TestMethod]
        public void ToOutlineRows_RandomTrees_RoundTrip()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                var tree = TestTrees.Random(seed);
                var rows = tree.ToRows(MenuRowStyle.Outline);
                var back = MenuTree.FromRows(rows, out var warnings);

                Assert.AreEqual(0, warnings.Count, $"seed {seed}");
                Assert.AreEqual(TestTrees.Fingerprint(tree), TestTrees.Fingerprint(back), $"seed {seed}");

                var expected = CountNodes(tree.Root) - tree.TopLevelNodes.Count(node => !node.IsLeaf);
                Assert.AreEqual(expected, rows.Sum(TestTrees.ExpansionOf), $"seed {seed}");
            }
        }

        /// <summary>
        /// 驗收條件：同一棵樹只有一種大綱寫法，不論從哪一種寫法讀進來。
        /// </summary>
        [TestMethod]
        public void ToOutlineRows_RandomTrees_IsIdempotentAcrossStyles()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                var tree = TestTrees.Random(seed);
                var outline = TestTrees.Describe(tree.ToRows(MenuRowStyle.Outline));

                foreach (var style in new[] { MenuRowStyle.Expanded, MenuRowStyle.Compact, MenuRowStyle.Outline })
                {
                    var back = MenuTree.FromRows(tree.ToRows(style), out _);
                    Assert.AreEqual(outline, TestTrees.Describe(back.ToRows(MenuRowStyle.Outline)), $"seed {seed}, {style}");
                }
            }
        }

        [TestMethod]
        public void ToRows_UndefinedStyle_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MenuTree().ToRows((MenuRowStyle)99));
        }

        private static long CountNodes(MenuNode root)
        {
            var count = 0L;
            var stack = new System.Collections.Generic.Stack<MenuNode>(root.Children);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                count++;
                foreach (var child in node.Children) stack.Push(child);
            }
            return count;
        }

        #endregion 大綱（ToRows(MenuRowStyle.Outline)）
    }
}