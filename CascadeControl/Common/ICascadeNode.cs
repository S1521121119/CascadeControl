using System.Collections.Generic;

namespace CascadeControl.Common
{
    /// <summary>
    /// <see cref="Views.Columns.CascadeColumnsView"/> 顯示節點時需要的最少資訊：名稱與子節點。
    /// </summary>
    /// <remarks>
    /// 不包含 Parent 與 Depth：路徑由控制項的欄位順序決定，資料不需要知道自己在第幾層。
    /// 欄位上的文字由控制項依名稱、位置與子節點數組出（見 ShowIndex、ShowChildCount），資料不需要提供。
    /// 欄位上的每個節點都會讀取 <see cref="Children"/> 的數量，用來畫 [+] 與子節點數；實作端可以延遲建立更深的子節點。
    /// 這個檔案不相依 System.Windows.Forms，資料模型實作這個介面也不會因此相依 UI。
    /// </remarks>
    public interface ICascadeNode
    {
        /// <summary>
        /// 節點名稱：欄位上顯示它，也用來組成 <see cref="Views.Columns.CascadeColumnsView.SelectedPathText"/>。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 子節點；空集合代表葉節點。
        /// </summary>
        IReadOnlyList<ICascadeNode> Children { get; }
    }

    /// <summary>
    /// 內建的 <see cref="ICascadeNode"/> 實作。不想讓自己的模型實作介面時，可以直接用它組樹。
    /// </summary>
    public sealed class CascadeNode : ICascadeNode
    {
        private readonly List<CascadeNode> _children = new List<CascadeNode>();

        /// <summary>
        /// 建立節點。
        /// </summary>
        /// <param name="name">名稱；null 視為空字串。</param>
        /// <param name="tag">呼叫端自己的資料物件，選取後可由 <see cref="Tag"/> 取回。</param>
        /// <param name="children">初始的子節點；可為 null。</param>
        public CascadeNode(string name, object tag = null, IEnumerable<CascadeNode> children = null)
        {
            Name = name ?? string.Empty;
            Tag = tag;
            if (children != null) _children.AddRange(children);
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        /// 呼叫端自己的資料物件。
        /// </summary>
        public object Tag { get; }

        /// <summary>
        /// 子節點，可以直接增刪；已經顯示的欄位不會自動更新，修改後請重新呼叫 <see cref="Views.Columns.CascadeColumnsView.SetTopLevelNodes"/> 或 <see cref="Views.Columns.CascadeColumnsView.RefreshColumns"/>。
        /// </summary>
        public IList<CascadeNode> Children => _children;

        IReadOnlyList<ICascadeNode> ICascadeNode.Children => _children;

        /// <summary>
        /// 回傳 <see cref="Name"/>，方便除錯與記錄。
        /// </summary>
        public override string ToString() => Name;
    }
}