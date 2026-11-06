using System.Collections.Generic;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView"/> 顯示節點時需要的最少資訊：名稱、顯示文字與子節點。
    /// </summary>
    /// <remarks>
    /// 不包含 Parent 與 Depth：路徑由控制項的欄位順序決定，資料不需要知道自己在第幾層。
    /// <see cref="Children"/> 要開新的一欄時才會讀取，實作端可以延遲建立子節點。
    /// 這個檔案不相依 System.Windows.Forms，資料模型實作這個介面也不會因此相依 UI。
    /// </remarks>
    public interface ICascadeItem
    {
        /// <summary>
        /// 項目的原始名稱，不會顯示在欄位上，用來組成 <see cref="CascadeView.SelectedPathText"/>。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 欄位上顯示的文字，可以和 <see cref="Name"/> 不同，例如加上序號或展開標記。
        /// </summary>
        string Text { get; }

        /// <summary>
        /// 子節點；空集合代表葉節點。
        /// </summary>
        IReadOnlyList<ICascadeItem> Children { get; }
    }

    /// <summary>
    /// 內建的 <see cref="ICascadeItem"/> 實作。不想讓自己的模型實作介面時，可以直接用它組樹。
    /// </summary>
    public sealed class CascadeItem : ICascadeItem
    {
        private readonly List<CascadeItem> _children = new List<CascadeItem>();

        /// <summary>
        /// 建立節點。
        /// </summary>
        /// <param name="name">原始名稱，用於路徑文字；null 視為空字串。</param>
        /// <param name="text">欄位上的顯示文字；null 視為空字串。</param>
        /// <param name="tag">呼叫端自己的資料物件，選取後可由 <see cref="Tag"/> 取回。</param>
        /// <param name="children">初始的子節點；可為 null。</param>
        public CascadeItem(string name, string text, object tag = null, IEnumerable<CascadeItem> children = null)
        {
            Name = name ?? string.Empty;
            Text = text ?? string.Empty;
            Tag = tag;
            if (children != null) _children.AddRange(children);
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public string Text { get; }

        /// <summary>
        /// 呼叫端自己的資料物件。
        /// </summary>
        public object Tag { get; }

        /// <summary>
        /// 子節點，可以直接增刪；已經顯示的欄位不會自動更新，修改後請重新呼叫 <see cref="CascadeView.SetRoots"/>。
        /// </summary>
        public IList<CascadeItem> Children => _children;

        IReadOnlyList<ICascadeItem> ICascadeItem.Children => _children;

        /// <summary>
        /// 回傳 <see cref="Name"/>，方便除錯與記錄。
        /// </summary>
        public override string ToString() => Name;
    }
}