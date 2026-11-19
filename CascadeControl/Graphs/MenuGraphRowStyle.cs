namespace CascadeControl.Graphs
{
    /// <summary>
    /// 把圖寫成 <c>From,To</c> 資料列時的寫法。三種寫法讀回來都是同一張圖，只差在列數與好不好手動維護。
    /// </summary>
    /// <remarks>
    /// 以這張圖為例：<c>A</c> 指向 <c>x</c>、<c>m</c>、<c>n</c>，<c>m</c> 與 <c>n</c> 都指向 <c>1</c>、<c>2</c>，另有孤立頂點 <c>Z</c>。
    /// <list type="table">
    /// <listheader><term>寫法</term><description>資料列</description></listheader>
    /// <item><term><see cref="Expanded"/></term><description><c>A,x</c> / <c>A,m</c> / <c>A,n</c> / <c>m,1</c> / <c>m,2</c> / <c>n,1</c> / <c>n,2</c> / <c>Z,</c></description></item>
    /// <item><term><see cref="Compact"/></term><description><c>A,x|m|n</c> / <c>m|n,1|2</c> / <c>Z,</c></description></item>
    /// <item><term><see cref="Outline"/></term><description><c>A,x|m|n</c> / <c>m,1|2</c> / <c>n,1|2</c> / <c>Z,</c></description></item>
    /// </list>
    /// 三種寫法的列順序都相同：從來源頂點出發，沿後繼做深度優先前序走訪，依走訪順序寫出每個頂點的列。
    /// </remarks>
    public enum MenuGraphRowStyle
    {
        /// <summary>
        /// 展開：每條邊一列，不使用 <see cref="Common.MultiNameCellSyntax.NAME_SEPARATOR"/>。
        /// </summary>
        Expanded,

        /// <summary>
        /// 壓縮：在 <see cref="Outline"/> 的基礎上，把後繼序列完全相同、且從同一個起點走訪到的頂點合成同一格（不必相鄰）。
        /// 來源頂點各自是起點，所以不會互相合併，讀回時來源頂點的順序不變。
        /// </summary>
        Compact,

        /// <summary>
        /// 大綱：每個有出邊的頂點一列，列出它的全部後繼，也就是鄰接串列本身。
        /// </summary>
        Outline,
    }
}