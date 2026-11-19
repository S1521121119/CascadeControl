using CascadeControl.Trees;
using CascadeControl.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlTests.Views
{
    [TestClass]
    public class CascadeViewLayoutTests
    {
        #region 右緣與視窗起點

        [DataTestMethod]
        [DataRow(3, 2, false, 2, DisplayName = "操作欄全是葉節點：右緣是操作欄")]
        [DataRow(4, 2, true, 3, DisplayName = "選到有子節點的項目：右緣是下一欄")]
        [DataRow(3, 2, true, 3, DisplayName = "選到葉節點但同欄有節點：預留還沒長出來的下一欄")]
        [DataRow(1, 0, false, 0, DisplayName = "只有第一欄，全是葉節點")]
        [DataRow(1, 0, true, 1, DisplayName = "只有第一欄，有節點：預留第二欄")]
        [DataRow(6, 2, true, 3, DisplayName = "記憶中還有更深的欄位：右緣仍只到下一欄")]
        public void RightEdgeColumn_ReservesNextColumnWhenActiveColumnHasBranch(int columnCount, int activeColumnIndex, bool reserveNext, int expected)
        {
            Assert.AreEqual(expected, CascadeView.RightEdgeColumn(columnCount, activeColumnIndex, reserveNext));
        }

        [DataTestMethod]
        [DataRow(3, 2, 3, false, 0, DisplayName = "3 層資料、顯示 3 欄、停在最深一層（全是葉節點）：從第一欄開始，右邊不空格")]
        [DataRow(4, 2, 3, true, 1, DisplayName = "長出第 4 欄：往左移一格")]
        [DataRow(3, 2, 3, true, 1, DisplayName = "選到葉節點但同欄有節點：仍往左移一格，右邊留白")]
        [DataRow(4, 2, 1, true, 2, DisplayName = "只顯示一欄：顯示操作欄，不是它的下一欄")]
        [DataRow(2, 0, 3, true, 0, DisplayName = "欄數比視窗少：從第一欄開始")]
        [DataRow(5, 4, 3, false, 2, DisplayName = "很深的葉節點")]
        public void ViewportStartColumn_AlignsRightEdge(int columnCount, int activeColumnIndex, int viewportColumns, bool reserveNext, int expected)
        {
            Assert.AreEqual(expected, CascadeView.ViewportStartColumn(columnCount, activeColumnIndex, viewportColumns, reserveNext));
        }

        [TestMethod]
        public void ViewportStartColumn_SelectingLeafOrSteppingBackFromLeaf_DoesNotMove()
        {
            // 停在第 2 層（有子節點，顯示第 3 欄） → 選第 3 層的葉節點 → 再退回第 2 層：欄數都是 3，起點都一樣。
            Assert.AreEqual(1, CascadeView.ViewportStartColumn(3, 1, 2, true), "停在第 2 層");
            Assert.AreEqual(1, CascadeView.ViewportStartColumn(3, 2, 2, false), "選到葉節點時不動");
        }

        [TestMethod]
        public void ViewportStartColumn_SwitchingBetweenNodeAndLeafInSameColumn_DoesNotMove()
        {
            // 第 3 欄同時有節點與葉節點：選節點時長出第 4 欄，改選葉節點時第 4 欄拆掉但位置保留，畫面不動。
            Assert.AreEqual(1, CascadeView.ViewportStartColumn(4, 2, 3, true), "選到節點 A");
            Assert.AreEqual(1, CascadeView.ViewportStartColumn(3, 2, 3, true), "改選葉節點 B");
        }

        #endregion 右緣與視窗起點

        #region 實際排版

        [TestMethod]
        public void Sliding_ShowsOnlyTheLastColumns()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Sliding, 3))
            {
                SelectDeepestLeaf(view, 4);

                // 4 層資料，停在第 4 層的葉節點：顯示第 2～4 欄，第 1 欄隱藏，第 2 欄排在最左邊。
                CollectionAssert.AreEqual(new[] { false, true, true, true }, Columns(view).Select(c => c.Visible).ToArray());
                Assert.AreEqual(Columns(view)[1].Left, Columns(view).Where(c => c.Visible).Min(c => c.Left));
                Assert.AreEqual(Size.Empty, view.AutoScrollMinSize, "Sliding 不捲動");
            }
        }

        [TestMethod]
        public void Sliding_WithOneColumn_ShowsOnlyActiveColumn()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Sliding, 1))
            {
                SelectDeepestLeaf(view, 3);
                view.TrySelectPath(new[] { view.SelectedPath[0] });   // 停在第一層，第二欄是它的下一欄

                CollectionAssert.AreEqual(new[] { true, false }, Columns(view).Select(c => c.Visible).ToArray());
            }
        }

        [TestMethod]
        public void Sliding_ZeroCount_IsTreatedAsOne()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Sliding, 0))
            {
                SelectDeepestLeaf(view, 3);

                Assert.AreEqual(1, Columns(view).Count(c => c.Visible));
            }
        }

        [TestMethod]
        public void Sliding_ColumnsTooNarrow_ShowsFewerColumns()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Sliding, 3))
            {
                view.ColumnWidth = 250;   // 600 寬放不下 3 欄 250
                SelectDeepestLeaf(view, 3);

                Assert.AreEqual(2, Columns(view).Count(c => c.Visible));
                Assert.IsTrue(Columns(view).Last().Visible, "操作欄一定要顯示");
            }
        }

        [TestMethod]
        public void Fill_DepthEqualsCount_LeavesNoEmptyColumnOnTheRight()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Fill, 3))
            {
                SelectDeepestLeaf(view, 3);

                Assert.AreEqual(3, Columns(view).Length);
                Assert.IsTrue(view.AutoScrollMinSize.Width <= view.ClientSize.Width, "三欄剛好放滿，不需要捲動");
                Assert.IsTrue(Columns(view).Last().Right <= view.ClientSize.Width);
            }
        }

        [TestMethod]
        public void Fixed_ScrollRangeEndsAtLastColumn()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Fixed, 0))
            {
                view.Width = 250;
                view.ColumnWidth = 100;
                SelectDeepestLeaf(view, 4);

                // 4 欄：左邊距 3 + 4 × 100 + 3 個欄距 × 6 + 右邊距 3 = 424；以前會再多留一欄（530）。
                Assert.AreEqual(424, view.AutoScrollMinSize.Width, "捲動範圍只到最後一欄");
            }
        }

        [TestMethod]
        public void Sliding_LeafSelectedInMixedColumn_KeepsBlankColumnOnTheRight()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Sliding, 2))
            {
                // A 底下有節點 A1（有子節點）與葉節點 A2；選 A > A2：操作欄是第 2 欄，第 3 欄不存在但要預留。
                var tree = new MenuTree();
                tree.ExpandAndAddPaths("A", "A1", "A1x");
                tree.ExpandAndAddPaths("A", "A2");
                view.SetTree(tree);
                Assert.IsTrue(view.TrySelectNode(tree.Root.Children[0].Children.Last()));

                // 顯示 2 欄：右緣預留第 3 欄，所以只看得到第 2 欄（操作欄），第 1 欄隱藏，右邊留白。
                CollectionAssert.AreEqual(new[] { false, true }, Columns(view).Select(c => c.Visible).ToArray());
            }
        }

        [TestMethod]
        public void Fixed_LeafSelectedInMixedColumn_ScrollRangeReservesNextColumn()
        {
            using (var view = CreateView(CascadeColumnLayoutMode.Fixed, 0))
            {
                view.Width = 250;
                view.ColumnWidth = 100;

                var tree = new MenuTree();
                tree.ExpandAndAddPaths("A", "A1", "A1x");
                tree.ExpandAndAddPaths("A", "A2");
                view.SetTree(tree);
                var a = tree.Root.Children[0];
                Assert.IsTrue(view.TrySelectNode(a.Children.Last()));

                // 只有 2 欄，原本放得下不必捲動；但第 2 欄有節點 A1，所以右緣預留到第 3 欄，要往右捲。
                var thirdRight = 3 + (2 * (100 + view.ColumnSpacing)) + 100;
                Assert.IsTrue(thirdRight > view.ClientSize.Width, "前提：第 3 欄超出可視範圍");
                Assert.AreEqual(thirdRight, view.AutoScrollMinSize.Width, "捲動範圍延伸到預留的第 3 欄右緣（以前只到第 2 欄，212）");
            }
        }

        #endregion 實際排版

        #region 屬性

        [TestMethod]
        public void DisplayColumnCount_Negative_Throws()
        {
            using (var view = new CascadeView())
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => view.DisplayColumnCount = -1);
            }
        }

        /// <summary>
        /// 沒設定時要讀到實際的預設色：繪製直接拿屬性值填底，讀到 Color.Empty 就會畫成透明，屬性視窗也會顯示空白。
        /// </summary>
        [TestMethod]
        public void Colors_Unset_ReturnEffectiveDefaults()
        {
            using (var view = new CascadeView())
            {
                Assert.AreEqual(SystemColors.Highlight, view.SelectionBackColor);
                Assert.AreEqual(SystemColors.HighlightText, view.SelectionForeColor);
                Assert.IsFalse(view.InactiveSelectionBackColor.IsEmpty);
                Assert.AreNotEqual(0, view.InactiveSelectionBackColor.A, "預設的非焦點選取色不可以是透明的");
                Assert.AreEqual(SystemColors.ControlLight, view.TrailBackColor);

                var properties = TypeDescriptor.GetProperties(view);
                Assert.IsFalse(properties[nameof(CascadeView.SelectionBackColor)].ShouldSerializeValue(view), "預設值不寫進 Designer.cs");
                Assert.IsFalse(properties[nameof(CascadeView.SelectionForeColor)].ShouldSerializeValue(view));
                Assert.IsFalse(properties[nameof(CascadeView.InactiveSelectionBackColor)].ShouldSerializeValue(view));
                Assert.IsFalse(properties[nameof(CascadeView.TrailBackColor)].ShouldSerializeValue(view));
            }
        }

        [TestMethod]
        public void Colors_SetThenReset_ReturnToDefaults()
        {
            using (var view = new CascadeView())
            {
                var inactiveDefault = view.InactiveSelectionBackColor;

                view.InactiveSelectionBackColor = Color.Red;
                view.TrailBackColor = Color.Blue;
                Assert.AreEqual(Color.Red, view.InactiveSelectionBackColor);
                Assert.AreEqual(Color.Blue, view.TrailBackColor);

                var properties = TypeDescriptor.GetProperties(view);
                Assert.IsTrue(properties[nameof(CascadeView.InactiveSelectionBackColor)].ShouldSerializeValue(view));
                properties[nameof(CascadeView.InactiveSelectionBackColor)].ResetValue(view);   // 屬性視窗的「重設」
                view.TrailBackColor = Color.Empty;

                Assert.AreEqual(inactiveDefault, view.InactiveSelectionBackColor);
                Assert.AreEqual(SystemColors.ControlLight, view.TrailBackColor);
            }
        }

        /// <summary>
        /// 非焦點欄的選取色沒有設定時跟著 SelectionBackColor 調淡，換了選取色不必再另外設定。
        /// </summary>
        [TestMethod]
        public void SelectionBackColor_DrivesInactiveDefault()
        {
            using (var view = new CascadeView())
            {
                var systemInactive = view.InactiveSelectionBackColor;

                view.SelectionBackColor = Color.DarkGreen;
                view.SelectionForeColor = Color.Yellow;
                Assert.AreEqual(Color.DarkGreen, view.SelectionBackColor);
                Assert.AreEqual(Color.Yellow, view.SelectionForeColor);
                Assert.AreNotEqual(systemInactive, view.InactiveSelectionBackColor, "預設的非焦點選取色跟著選取色變");

                view.InactiveSelectionBackColor = Color.Pink;
                Assert.AreEqual(Color.Pink, view.InactiveSelectionBackColor, "自己設定的非焦點選取色不受影響");

                var properties = TypeDescriptor.GetProperties(view);
                properties[nameof(CascadeView.SelectionBackColor)].ResetValue(view);
                properties[nameof(CascadeView.SelectionForeColor)].ResetValue(view);
                view.InactiveSelectionBackColor = Color.Empty;

                Assert.AreEqual(SystemColors.Highlight, view.SelectionBackColor);
                Assert.AreEqual(SystemColors.HighlightText, view.SelectionForeColor);
                Assert.AreEqual(systemInactive, view.InactiveSelectionBackColor);
            }
        }

        #endregion 屬性

        private static CascadeView CreateView(CascadeColumnLayoutMode mode, int displayColumnCount)
        {
            return new CascadeView
            {
                Size = new Size(600, 300),
                ColumnWidth = 40,
                ColumnLayoutMode = mode,
                DisplayColumnCount = displayColumnCount,
            };
        }

        // 建一棵 depth 層、每層兩個節點的樹，選到最深的第一個葉節點。
        private static void SelectDeepestLeaf(CascadeView view, int depth)
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths(Enumerable.Range(1, depth).Select(level => $"L{level}a|L{level}b").ToArray());

            view.SetTree(tree);
            Assert.IsTrue(view.TrySelectPath(tree.GetLeaves().First().FullPath));
        }

        private static ListBox[] Columns(CascadeView view) => view.Controls.OfType<ListBox>().OrderBy(c => c.TabIndex).ToArray();
    }
}