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
        public string Name { get; private set; }

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

        private string _displayText;

        /// <summary>
        /// 畫面上的顯示文字，格式為 &lt;序號&gt;名稱，有子節點時再加上 " [+]"，例如 &lt;01&gt;A [+]、&lt;01&gt;A11。樹根為空字串。
        /// </summary>
        /// <remarks>
        /// 第一次讀取時才組字串並快取；子節點增加時由 <see cref="GetOrAddChild"/> 清除快取。
        /// </remarks>
        public string DisplayText
        {
            get
            {
                if (_displayText == null)
                    _displayText = BuildDisplayText();
                return _displayText;
            }
        }

        private string BuildDisplayText()
        {
            return Parent == null
                ? string.Empty
                : $"<{Index:00}>{Name}{(_children.Count > 0 ? " [+]" : string.Empty)}";
        }

        // 顯示文字會隨 _children 是否為空而改變，所以 _children 增加時要呼叫。
        // Name 不可變；Parent / Index 只在 GetOrAddChild 建立節點時設定一次，當時還沒有快取，不必呼叫。
        private void InvalidateDisplayText()
        {
            _displayText = null;
        }

        /// <summary>
        /// 從第一層到本節點的路徑字串，只含名稱、不含序號，例如 A &gt; A1 &gt; A11。
        /// </summary>
        /// <remarks>內容和來源資料寫的一樣，所以用在警告訊息裡，使用者可以直接拿去找出有問題的那一筆。</remarks>
        public string FullPath => BuildPath(node => node.Name);

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
            InvalidateDisplayText(); // 本節點可能剛從葉節點變成中間節點，" [+]" 要重算
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
        /// 檢查底下是否已存在指定名稱的子節點。
        /// </summary>
        /// <param name="name">子節點名稱。</param>
        /// <returns>已存在時為 true。</returns>
        public bool ContainsChild(string name)
        {
            return _index.ContainsKey(name ?? string.Empty);
        }

        /// <summary>
        /// 取得底下不重複的子節點名稱：若 <paramref name="baseName"/> 尚未使用則原樣回傳；
        /// 若已存在，則在後綴依序加上 <c>" - 2"</c>、<c>" - 3"</c>… 直到不重複為止。
        /// </summary>
        /// <param name="baseName">原始名稱。</param>
        /// <returns>在本節點底下不重複的名稱。</returns>
        public string GetUniqueChildName(string baseName)
        {
            var key = baseName ?? string.Empty;
            if (!_index.ContainsKey(key)) return key;

            var suffix = 2;
            while (true)
            {
                var candidate = $"{key} - {suffix}";
                if (!_index.ContainsKey(candidate)) return candidate;
                suffix++;
            }
        }

        /// <summary>
        /// 建立本節點及其所有子孫節點的深層複本（脫離原本的父節點）。
        /// </summary>
        /// <param name="newName">複本的新名稱；傳 null 則沿用本節點名稱。</param>
        /// <returns>獨立的節點複本。</returns>
        public MenuNode Copy(string newName = null)
        {
            var copy = new MenuNode(newName ?? Name);
            copy.MergeFrom(this);
            return copy;
        }

        /// <summary>
        /// 將 <paramref name="source"/> 底下的所有子孫節點遞迴合併（往下疊）至本節點底下。
        /// </summary>
        /// <param name="source">來源節點。</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 為 null。</exception>
        public void MergeFrom(MenuNode source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            // 先對來源做快照，即使把節點合併進自己或祖先節點也不會造成無窮迴圈。
            var stack = new Stack<KeyValuePair<IReadOnlyList<MenuNode>, MenuNode>>();
            stack.Push(new KeyValuePair<IReadOnlyList<MenuNode>, MenuNode>(source._children.ToList(), this));

            while (stack.Count > 0)
            {
                var pair = stack.Pop();
                var children = pair.Key;
                var target = pair.Value;

                foreach (var child in children)
                {
                    var childSnapshot = child._children.ToList();
                    var dstChild = target.GetOrAddChild(child.Name);
                    if (childSnapshot.Count > 0)
                    {
                        stack.Push(new KeyValuePair<IReadOnlyList<MenuNode>, MenuNode>(childSnapshot, dstChild));
                    }
                }
            }
        }

        /// <summary>
        /// 在本節點上方（同層前一個位置）插入 <paramref name="source"/> 的深層複本；
        /// 若同層已有同名節點，則將 <paramref name="source"/> 的子孫節點往下疊合併至該既有節點。
        /// </summary>
        /// <param name="source">要插入的來源節點。</param>
        /// <param name="overrideName">指定插入時使用的名稱；傳 null 則使用 <paramref name="source"/> 的名稱。</param>
        /// <returns>插入或合併後的節點。</returns>
        public MenuNode InsertBefore(MenuNode source, string overrideName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Parent == null) throw new InvalidOperationException("無法在樹根節點同層插入節點。");

            var position = Parent._children.IndexOf(this);
            return Parent.InsertChildAt(position, source, overrideName);
        }

        /// <summary>
        /// 在本節點下方（同層後一個位置）插入 <paramref name="source"/> 的深層複本；
        /// 若同層已有同名節點，則將 <paramref name="source"/> 的子孫節點往下疊合併至該既有節點。
        /// </summary>
        /// <param name="source">要插入的來源節點。</param>
        /// <param name="overrideName">指定插入時使用的名稱；傳 null 則使用 <paramref name="source"/> 的名稱。</param>
        /// <returns>插入或合併後的節點。</returns>
        public MenuNode InsertAfter(MenuNode source, string overrideName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Parent == null) throw new InvalidOperationException("無法在樹根節點同層插入節點。");

            var position = Parent._children.IndexOf(this) + 1;
            return Parent.InsertChildAt(position, source, overrideName);
        }

        private MenuNode InsertChildAt(int position, MenuNode source, string overrideName)
        {
            var key = ValidateNodeName(overrideName ?? source.Name);

            if (_index.TryGetValue(key, out MenuNode existing))
            {
                existing.MergeFrom(source);
                return existing;
            }

            var child = source.Copy(key);
            child.Parent = this;
            child.UpdateDepthRecursive(Depth + 1);

            var clampedPos = Math.Max(0, Math.Min(position, _children.Count));
            _children.Insert(clampedPos, child);
            _index.Add(key, child);
            ReindexChildren();
            return child;
        }

        /// <summary>
        /// 將本節點從父節點中刪除，並重新計算同層兄弟節點的序號。
        /// </summary>
        /// <returns>成功移除時為 true；本節點為樹根或不在父節點底下時為 false。</returns>
        public bool Remove()
        {
            if (Parent == null) return false;

            var parent = Parent;
            if (!parent._children.Remove(this)) return false;

            parent._index.Remove(Name);
            Parent = null;
            parent.ReindexChildren();
            return true;
        }

        /// <summary>
        /// 修改本節點名稱。若同層尚無同名節點，則直接原地改名；
        /// 若同層已有同名節點，則將本節點的子樹往下疊合併至該既有節點，並移除本節點。
        /// </summary>
        /// <param name="newName">新名稱。</param>
        /// <returns>改名後的本節點，或合併後的既有同名節點。</returns>
        /// <exception cref="InvalidOperationException">對樹根呼叫此方法。</exception>
        /// <exception cref="ArgumentException"><paramref name="newName"/> 為空白或含有保留字元。</exception>
        public MenuNode Rename(string newName)
        {
            if (Parent == null) throw new InvalidOperationException("樹根不可重新命名。");

            var key = ValidateNodeName(newName);
            if (string.Equals(Name, key, StringComparison.Ordinal)) return this;

            var parent = Parent;
            if (parent._index.TryGetValue(key, out MenuNode existing))
            {
                existing.MergeFrom(this);
                Remove();
                return existing;
            }

            parent._index.Remove(Name);
            Name = key;
            parent._index.Add(key, this);
            InvalidateDisplayText();
            return this;
        }

        private static string ValidateNodeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("節點名稱不可為空白。", nameof(name));

            var trimmed = name.Trim();
            if (trimmed.IndexOf(AlternativeCell.SEPARATOR) >= 0)
                throw new ArgumentException($"節點名稱不可含有保留字元 '{AlternativeCell.SEPARATOR}'。", nameof(name));

            return trimmed;
        }

        private void ReindexChildren()
        {
            for (var i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                var newIndex = i + 1;
                if (child.Index != newIndex)
                {
                    child.Index = newIndex;
                    child.InvalidateDisplayText();
                }
            }

            InvalidateDisplayText();
        }

        private void UpdateDepthRecursive(int rootDepth)
        {
            Depth = rootDepth;
            var stack = new Stack<MenuNode>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current._children)
                {
                    child.Depth = current.Depth + 1;
                    if (child._children.Count > 0)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        /// <summary>
        /// 在 <see cref="CascadeView"/> 上顯示的文字，與 <see cref="DisplayText"/> 相同。
        /// </summary>
        string ICascadeItem.Text => DisplayText;

        IReadOnlyList<ICascadeItem> ICascadeItem.Children => _children;

        /// <summary>
        /// 回傳 <see cref="Name"/>，方便除錯與記錄。
        /// </summary>
        /// <remarks>欄位上的顯示文字由 <see cref="ICascadeItem.Text"/> 決定，不受這裡影響。</remarks>
        public override string ToString() => Name;
    }
}