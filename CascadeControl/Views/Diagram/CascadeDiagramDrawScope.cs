namespace CascadeControl.Views.Diagram
{
    /// <summary>
    /// <see cref="TreeDrawer"/> 要畫樹的哪個範圍。
    /// </summary>
    public enum CascadeDiagramDrawScope
    {
        /// <summary>
        /// 畫整棵樹；選取的節點在外框外圍另外畫一圈矩形紅框。
        /// </summary>
        WholeTree,

        /// <summary>
        /// 從選取的節點開始，畫它和它底下的所有節點；選取的節點就是圖的起點，不另外框起來。沒有選取時畫整棵樹。
        /// </summary>
        SelectedSubtree,
    }
}