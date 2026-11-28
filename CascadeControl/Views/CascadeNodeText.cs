using System.Globalization;

namespace CascadeControl.Views
{
    /// <summary>
    /// 節點在畫面上的文字規則，<see cref="Columns.CascadeColumnsView"/> 與 <see cref="Diagram.CascadeDiagramView"/> 共用。
    /// </summary>
    /// <remarks>
    /// 序號（&lt;n&gt;）與子節點數（(n)）各自由 View 的 ShowIndex / ShowChildCount 決定要不要顯示。
    /// 這裡只吃算好的數字，不讀節點：圖在拖曳預覽時序號與子節點數照移動後的結果重算，欄位上的節點也不一定是 <see cref="Trees.MenuNode"/>。
    /// </remarks>
    internal static class CascadeNodeText
    {
        /// <summary>
        /// 序號標籤，補零到兄弟數的位數：12 個兄弟時第 3 個是 &lt;03&gt;，5 個兄弟時是 &lt;3&gt;。
        /// </summary>
        /// <param name="number">同層序號，由 1 起算。</param>
        /// <param name="siblingCount">同層節點數（含自己）。</param>
        public static string Number(int number, int siblingCount)
        {
            var width = siblingCount.ToString(CultureInfo.InvariantCulture).Length;
            return "<" + number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0') + ">";
        }

        /// <summary>
        /// 名稱，<paramref name="showChildCount"/> 為 true 且有子節點時後面接子節點數：沒有勾選資訊時是 (X)，有時是 (x/X)。
        /// </summary>
        /// <param name="name">節點名稱。</param>
        /// <param name="showChildCount">是否顯示子節點數。</param>
        /// <param name="childCount">子節點總數 X；為 0 時不顯示。</param>
        /// <param name="checkedChildCount">勾選的子節點數 x；null 表示沒有勾選資訊。</param>
        public static string Name(string name, bool showChildCount, int childCount, int? checkedChildCount)
        {
            if (!showChildCount || childCount == 0) return name;

            return checkedChildCount.HasValue
                ? $"{name} ({checkedChildCount.Value}/{childCount})"
                : $"{name} ({childCount})";
        }
    }
}