namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView"/> 決定欄寬與顯示欄數的方式。
    /// </summary>
    /// <remarks>
    /// 三種模式的畫面右緣都對齊「操作欄的下一欄」，下一欄不存在（選到葉節點）時就對齊操作欄本身，
    /// 所以資料有幾層，<see cref="CascadeView.DisplayColumnCount"/> 就設幾層，停在最深一層時右邊不會空一格。
    /// </remarks>
    public enum CascadeColumnSizeMode
    {
        /// <summary>
        /// 每一欄固定為 <see cref="CascadeView.ColumnWidth"/>，放不下時出現水平捲軸。
        /// </summary>
        Fixed,

        /// <summary>
        /// 讓 <see cref="CascadeView.DisplayColumnCount"/> 欄平均填滿控制項寬度，放不下的欄位出現水平捲軸，捲動時對齊欄位邊界；
        /// <see cref="CascadeView.ColumnWidth"/> 是最小欄寬。<see cref="CascadeView.DisplayColumnCount"/> 為 0 時等同 <see cref="Fixed"/>。
        /// </summary>
        Fill,

        /// <summary>
        /// 讓 <see cref="CascadeView.DisplayColumnCount"/> 欄平均填滿控制項寬度，只顯示以操作欄（或它的下一欄）為右緣的這幾欄，
        /// 更淺的欄位隱藏起來，不出現水平捲軸。<see cref="CascadeView.DisplayColumnCount"/> 為 0 時當成 1，也就是只顯示操作欄。
        /// </summary>
        /// <remarks>
        /// 平均後的欄寬小於 <see cref="CascadeView.ColumnWidth"/> 時，改為減少顯示欄數，至少顯示操作欄。
        /// 這個模式下 <see cref="CascadeView.ShowRememberedPath"/> 沒有作用。
        /// </remarks>
        Window,
    }
}