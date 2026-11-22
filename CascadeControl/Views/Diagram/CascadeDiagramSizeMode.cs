namespace CascadeControl.Views.Diagram
{
    /// <summary>
    /// <see cref="CascadeDiagramView"/> 的圖怎麼放進控制項。
    /// </summary>
    public enum TreeImageSizeMode
    {
        /// <summary>
        /// 照原始尺寸畫，放不下時出現水平／垂直捲軸。
        /// </summary>
        ActualSize,

        /// <summary>
        /// 維持長寬比例縮小到整張圖都放得進控制項，不出現捲軸；圖本來就放得下時照原始尺寸畫，不放大。
        /// </summary>
        Zoom,
    }
}