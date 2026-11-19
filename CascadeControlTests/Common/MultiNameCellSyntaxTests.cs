using CascadeControl.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace CascadeControlTests.Common
{
    [TestClass]
    public class MultiNameCellSyntaxTests
    {
        #region 常數與 JoinNames

        [TestMethod]
        public void Constants()
        {
            Assert.AreEqual('|', MultiNameCellSyntax.NAME_SEPARATOR);
            Assert.AreEqual(65535, MultiNameCellSyntax.MAX_EXPANDED_ROWS, "上限的依據是 ListBox 的 16 位元索引，改動前請先看 MAX_EXPANDED_ROWS 的說明");
        }

        [TestMethod]
        public void JoinSiblings_JoinsInOrder()
        {
            Assert.AreEqual("Version|Uptime|Memory", MultiNameCellSyntax.JoinNames(new[] { "Version", "Uptime", "Memory" }));
            Assert.AreEqual("Only", MultiNameCellSyntax.JoinNames(new[] { "Only" }));
        }

        [TestMethod]
        public void JoinSiblings_InvalidNames_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(() => MultiNameCellSyntax.JoinNames(null));
            Assert.ThrowsException<ArgumentException>(() => MultiNameCellSyntax.JoinNames(new string[0]));
            Assert.ThrowsException<ArgumentException>(() => MultiNameCellSyntax.JoinNames(new[] { "A", null }));
            Assert.ThrowsException<ArgumentException>(() => MultiNameCellSyntax.JoinNames(new[] { "A", " " }));
            Assert.ThrowsException<ArgumentException>(() => MultiNameCellSyntax.JoinNames(new[] { "A|B" }));
        }

        #endregion 常數與 JoinNames

        #region TrySplitNames / CountCombinations

        [TestMethod]
        public void TrySplitSiblings_WithoutSeparator_KeepsCellAsIs()
        {
            Assert.IsTrue(MultiNameCellSyntax.TrySplitNames(" A ", out var names));
            CollectionAssert.AreEqual(new[] { " A " }, names, "沒有分隔字元時不 trim，交給 FromRows 處理");
        }

        [TestMethod]
        public void TrySplitSiblings_WithSeparator_TrimsEachPart()
        {
            Assert.IsTrue(MultiNameCellSyntax.TrySplitNames(" A | B ", out var names));
            CollectionAssert.AreEqual(new[] { "A", "B" }, names);
        }

        [DataTestMethod]
        [DataRow("A||B")]
        [DataRow("|A")]
        [DataRow("A|")]
        [DataRow("A| |B")]
        [DataRow("|")]
        public void TrySplitSiblings_Malformed_ReturnsFalse(string cell)
        {
            Assert.IsFalse(MultiNameCellSyntax.TrySplitNames(cell, out var names));
            Assert.IsNull(names);
        }

        [TestMethod]
        public void CountCombinations_StopsMultiplyingPastLimit()
        {
            // 40 欄、每欄 10 個：真正的乘積會溢位，超過上限之後就不該再乘。
            var count = MultiNameCellSyntax.CountCombinations(Enumerable.Repeat(new string[10], 40));

            Assert.IsTrue(count > MultiNameCellSyntax.MAX_EXPANDED_ROWS && count <= MultiNameCellSyntax.MAX_EXPANDED_ROWS * 10L);
        }

        #endregion TrySplitNames / CountCombinations
    }
}