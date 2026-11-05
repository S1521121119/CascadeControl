using CascadeControl.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Models
{
    /// <summary>
    /// 選單樹中的一個節點。子節點依第一次加入的順序排列，同名的子節點只會有一個。
    /// </summary>
    /// <remarks>
    /// 名稱以 <see cref="StringComparer.Ordinal"/> 比對，區分大小寫。
    /// 如果改成不分大小寫，來源資料中的 Reboot 和 reboot 會在沒有任何提示的情況下合併成一項，
    /// 對 debug 選單來說等於少了一個選項。
    /// </remarks>
    public sealed class MenuNode : ICascadeItem
    {
        private readonly List<MenuNode> _children = new List<MenuNode>();
        private readonly Dictionary<string, MenuNode> _index = new Dictionary<string, MenuNode>(StringComparer.Ordinal);

        /// <summary>
        /// 建立節點。
        /// </summary>
        /// <param name="name">節點名稱；樹根請傳空字串。</param>
        public MenuNode(string name)
        {
            Name = name ?? string.Empty;
        }

        /// <summary>
        /// 節點的原始名稱，不含序號。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 父節點；樹根為 null。
        /// </summary>
        public MenuNode Parent { get; private set; }

        /// <summary>
        /// 節點深度。樹根為 0，第一層（Level1）為 1。
        /// </summary>
        public int Depth { get; private set; }

        /// <summary>
        /// 在同一個父節點底下的序號，由 1 起算；樹根為 0。
        /// </summary>
        /// <remarks>
        /// 序號只在同一層的兄弟節點之間計算，不是全樹的流水號，每一層都從 1 開始。
        /// 數字取決於 <see cref="GetOrAddChild"/> 的呼叫順序，不是來源資料裡的欄位。
        /// </remarks>
        public int Index { get; private set; }

        /// <summary>
        /// 子節點，依第一次加入的順序排列。
        /// </summary>
        public IReadOnlyList<MenuNode> Children => _children;

        /// <summary>
        /// 是否為葉節點（沒有任何子節點）。
        /// </summary>
        public bool IsLeaf => _children.Count == 0;

        /// <summary>
        /// 畫面上的顯示文字，格式為 &lt;序號&gt;名稱，例如 &lt;01&gt;A。樹根為空字串。
        /// </summary>
        public string DisplayText => Parent == null ? string.Empty : $"<{Index:00}>{Name}";

        /// <summary>
        /// 從第一層到本節點的路徑字串，只含名稱、不含序號，例如 A &gt; A1 &gt; A11。
        /// </summary>
        /// <remarks>內容和來源資料寫的一樣，所以用在警告訊息裡，使用者可以直接拿去找出有問題的那一筆。</remarks>
        public string FullPath => BuildPath(node => node.Name);

        /// <summary>
        /// 從第一層到本節點的路徑字串，含序號，例如 &lt;01&gt;A &gt; &lt;02&gt;A1 &gt; &lt;01&gt;A11。
        /// </summary>
        public string DisplayPath => BuildPath(node => node.DisplayText);

        /// <summary>
        /// 從第一層到本節點經過的所有節點，由淺到深排列，不含樹根；樹根本身回傳空集合。
        /// </summary>
        /// <remarks>
        /// 呼叫端拿到節點就能直接用 foreach 逐層處理，不必自己沿著 <see cref="Parent"/> 往上找再反轉順序。
        /// </remarks>
        public IReadOnlyList<MenuNode> PathFromRoot
        {
            get
            {
                var path = new List<MenuNode>();
                for (var node = this; node?.Parent != null; node = node.Parent)
                    path.Add(node);
                path.Reverse();
                return path;
            }
        }

        private string BuildPath(Func<MenuNode, string> selector)
        {
            return string.Join(" > ", PathFromRoot.Select(selector));
        }

        /// <summary>
        /// 取得指定名稱的子節點，不存在就新增一個。同一個名稱重複呼叫，拿到的都是同一個節點。
        /// </summary>
        /// <param name="name">子節點名稱。</param>
        /// <returns>既有或新建的子節點。</returns>
        public MenuNode GetOrAddChild(string name)
        {
            var key = name ?? string.Empty;

            if (_index.TryGetValue(key, out MenuNode existing)) return existing;

            var child = new MenuNode(key) { Parent = this, Depth = Depth + 1, Index = _children.Count + 1 };
            _children.Add(child);
            _index.Add(key, child);
            return child;
        }

        /// <summary>
        /// 嘗試取得指定名稱的子節點。
        /// </summary>
        /// <param name="name">子節點名稱。</param>
        /// <param name="child">找到的子節點；找不到時為 null。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetChild(string name, out MenuNode child)
        {
            return _index.TryGetValue(name ?? string.Empty, out child);
        }

        /// <summary>
        /// 在 <see cref="CascadeView"/> 上顯示的文字，與 <see cref="DisplayText"/> 相同。
        /// </summary>
        string ICascadeItem.Text => DisplayText;

        IReadOnlyList<ICascadeItem> ICascadeItem.Children => _children;

        /// <summary>
        /// 回傳 <see cref="DisplayText"/>，方便除錯與記錄。
        /// </summary>
        public override string ToString() => DisplayText;
    }
}