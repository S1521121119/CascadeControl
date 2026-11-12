namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView"/> 決定欄寬與顯示欄數的方式。
    /// </summary>
    public enum CascadeColumnSizeMode
    {
        /// <summary>
        /// 每一欄固定為 <see cref="CascadeView.ColumnWidth"/>，放不下時出現水平捲軸。
        /// </summary>
        Fixed,

        /// <summary>
        /// 讓 <see cref="CascadeView.FillColumnCount"/> 欄平均填滿控制項寬度，捲動時一次看到這麼多欄；
        /// <see cref="CascadeView.ColumnWidth"/> 是最小欄寬。<see cref="CascadeView.FillColumnCount"/> 為 0 時等同 <see cref="Fixed"/>。
        /// </summary>
        Fill,

        /// <summary>
        /// 只顯示目前操作的那一欄，欄寬填滿控制項，不保留右側的下一欄；
        /// <see cref="CascadeView.ColumnWidth"/> 是最小欄寬。這個模式下 <see cref="CascadeView.ShowRememberedPath"/> 沒有作用。
        /// </summary>
        Single,
    }
}