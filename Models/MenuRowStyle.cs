namespace CascadeControl.Models
{
    /// <summary>
    /// 把選單樹寫成資料列時的寫法。三種寫法讀回來都是同一棵樹，只差在列數與好不好手動維護。
    /// </summary>
    /// <remarks>
    /// 以這棵樹為例：<c>A</c> 底下有 <c>x</c>、<c>m</c>、<c>n</c>，<c>m</c> 與 <c>n</c> 底下都是 <c>1</c>、<c>2</c>。
    /// <list type="table">
    /// <listheader><term>寫法</term><description>資料列</description></listheader>
    /// <item><term><see cref="Expanded"/></term><description><c>A,x,</c> / <c>A,m,1</c> / <c>A,m,2</c> / <c>A,n,1</c> / <c>A,n,2</c></description></item>
    /// <item><term><see cref="Compact"/></term><description><c>A,x,</c> / <c>A,m|n,1|2</c></description></item>
    /// <item><term><see cref="Outline"/></term><description><c>A,x|m|n,</c> / <c>A,m|n,1|2</c></description></item>
    /// </list>
    /// </remarks>
    public enum MenuRowStyle
    {
        /// <summary>
        /// 展開：每個葉節點一列，不使用 <see cref="SiblingCellSyntax.SIBLING_SEPARATOR"/>。
        /// </summary>
        Expanded,

        /// <summary>
        /// 壓縮：相鄰且子樹完全相同的兄弟節點合併成同一格，每個葉節點只出現在一列，列與列不重疊。
        /// 遇到有下一層的兄弟節點就會切成新的一列，所以同一層的兄弟可能分散在好幾列。
        /// </summary>
        Compact,

        /// <summary>
        /// 大綱：每個中間節點一列，列出它底下的全部子節點；有下一層的子節點再用自己的一列往下展開。
        /// 子樹完全相同的兄弟節點（不必相鄰）共用同一列。這是最接近手寫的寫法，同一層的兄弟一定在同一格。
        /// </summary>
        Outline,
    }
}