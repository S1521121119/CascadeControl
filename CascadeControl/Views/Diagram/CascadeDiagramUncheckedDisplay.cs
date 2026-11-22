namespace CascadeControl.Views.Diagram
{
    /// <summary>
    /// 有勾選資訊時，沒勾的節點怎麼呈現；沒有勾選資訊時不使用。
    /// </summary>
    public enum CascadeDiagramUncheckedDisplay
    {
        /// <summary>
        /// 照樣畫出來但變淡：白底、灰框、灰字，不套用每層的底色。
        /// </summary>
        Dimmed,

        /// <summary>
        /// 不畫，剩下的節點重新排版；編號與圓角／直角沿用原樹，才能對回原本的選單。
        /// </summary>
        Hidden,
    }
}