using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Trees
{
    /// <summary>
    /// 樹的節點。子節點依加入順序排列，名稱不重複（<see cref="StringComparer.Ordinal"/>，區分大小寫）。
    /// </summary>
    public sealed class MenuNode : ICascadeNode
    {
        private const string DEFAULT_PATH_SEPARATOR = " > ";
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
        /// 節點深度。樹根為 0，第一層為 1。
        /// </summary>
        public int Depth { get; private set; }

        /// <summary>
        /// 在兄弟節點間的序號，依子節點順序由 1 起算；樹根為 0。
        /// </summary>
        public int SiblingNumber { get; private set; }

        /// <summary>
        /// 子節點，依第一次加入的順序排列。
        /// </summary>
        public IReadOnlyList<MenuNode> Children => _children;

        /// <summary>
        /// 是否為葉節點。
        /// </summary>
        public bool IsLeaf => _children.Count == 0;

        private string _displayText;

        /// <summary>
        /// 顯示文字：&lt;序號&gt;名稱，有子節點時加 " [+]"，例如 &lt;01&gt;A [+]。樹根為空字串。
        /// </summary>
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
                : $"<{SiblingNumber:00}>{Name}{(_children.Count > 0 ? " [+]" : string.Empty)}";
        }

        private void InvalidateDisplayText()
        {
            _displayText = null;
        }

        /// <summary>
        /// 從第一層到本節點的名稱路徑，例如 A &gt; A1 &gt; A11。
        /// </summary>
        public string FullPathText => BuildPath(node => node.Name);

        /// <summary>
        /// 從第一層到本節點的節點，由淺到深，不含樹根；樹根回傳空集合。
        /// </summary>
        public IReadOnlyList<MenuNode> FullPath
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
            return string.Join(DEFAULT_PATH_SEPARATOR, FullPath.Select(selector));
        }

        /// <summary>
        /// 取得指定名稱的子節點，不存在就新增。
        /// </summary>
        /// <param name="name">子節點名稱。</param>
        /// <returns>既有或新建的子節點。</returns>
        public MenuNode GetOrAddChild(string name)
        {
            var key = name ?? string.Empty;

            if (_index.TryGetValue(key, out MenuNode existing)) return existing;

            var child = new MenuNode(key) { Parent = this, Depth = Depth + 1, SiblingNumber = _children.Count + 1 };
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
        /// 是否有指定名稱的子節點。
        /// </summary>
        /// <param name="name">子節點名稱。</param>
        /// <returns>已存在時為 true。</returns>
        public bool ContainsChild(string name)
        {
            return _index.ContainsKey(name ?? string.Empty);
        }

        /// <summary>
        /// 取得不和現有子節點重複的名稱；重複時依序加上 <c>" - 2"</c>、<c>" - 3"</c>…。
        /// </summary>
        /// <param name="baseName">原始名稱。</param>
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
        /// 深層複製本節點與子樹，複本沒有父節點。
        /// </summary>
        /// <param name="newName">複本名稱；null 時沿用本節點名稱。</param>
        public MenuNode CopySubtree(string newName = null)
        {
            var copy = new MenuNode(newName ?? Name);
            copy.MergeSubtreeFrom(this);
            return copy;
        }

        /// <summary>
        /// 把 <paramref name="source"/> 的子孫合併到本節點底下，同名節點沿用既有的。
        /// </summary>
        /// <param name="source">來源節點。</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 為 null。</exception>
        public void MergeSubtreeFrom(MenuNode source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            // 先快照來源，合併進自己或祖先時才不會無窮迴圈。
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
        /// 在本節點之前插入 <paramref name="source"/> 的複本，規則同 <see cref="InsertChildAt"/>。
        /// </summary>
        /// <param name="source">來源節點。</param>
        /// <param name="newName">插入時的名稱；null 時沿用 <paramref name="source"/> 的名稱。</param>
        /// <returns>插入或合併後的節點。</returns>
        public MenuNode InsertSiblingBefore(MenuNode source, string newName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Parent == null) throw new InvalidOperationException("無法在樹根節點同層插入節點。");

            var position = Parent._children.IndexOf(this);
            return Parent.InsertChildAt(position, source, newName);
        }

        /// <summary>
        /// 在本節點之後插入 <paramref name="source"/> 的複本，規則同 <see cref="InsertChildAt"/>。
        /// </summary>
        /// <param name="source">來源節點。</param>
        /// <param name="newName">插入時的名稱；null 時沿用 <paramref name="source"/> 的名稱。</param>
        /// <returns>插入或合併後的節點。</returns>
        public MenuNode InsertSiblingAfter(MenuNode source, string newName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Parent == null) throw new InvalidOperationException("無法在樹根節點同層插入節點。");

            var position = Parent._children.IndexOf(this) + 1;
            return Parent.InsertChildAt(position, source, newName);
        }

        /// <summary>
        /// 在第 <paramref name="position"/> 個位置插入 <paramref name="source"/> 的複本；
        /// 已有同名子節點時改為合併進該節點，位置不變。
        /// </summary>
        /// <param name="position">插入位置（0-based），超出範圍時夾到頭尾。</param>
        /// <param name="source">來源節點。</param>
        /// <param name="newName">插入時的名稱；null 時沿用 <paramref name="source"/> 的名稱。</param>
        /// <returns>插入或合併後的節點。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 為 null。</exception>
        /// <exception cref="ArgumentException">名稱為空白或含有保留字元。</exception>
        public MenuNode InsertChildAt(int position, MenuNode source, string newName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var key = ValidateNodeName(newName ?? source.Name);

            if (_index.TryGetValue(key, out MenuNode existing))
            {
                existing.MergeSubtreeFrom(source);
                return existing;
            }

            var child = source.CopySubtree(key);
            child.Parent = this;
            child.UpdateDepthRecursive(Depth + 1);

            var clampedPos = Math.Max(0, Math.Min(position, _children.Count));
            _children.Insert(clampedPos, child);
            _index.Add(key, child);
            ReindexChildren();
            return child;
        }

        /// <summary>
        /// 把已脫離樹的節點原封不動接回第 <paramref name="position"/> 個位置，不複製、不合併。
        /// </summary>
        /// <param name="position">插入位置（0-based），超出範圍時夾到頭尾。</param>
        /// <param name="child">要接回的節點，通常來自 <see cref="RemoveFromParent"/>。</param>
        /// <remarks>給復原／重做用：接回同一個物件，以節點為鍵的狀態才對得上。先用 <see cref="CanAttachChild"/> 確認。</remarks>
        /// <exception cref="InvalidOperationException"><paramref name="child"/> 還有父節點，或本節點底下已有同名的子節點。</exception>
        internal void AttachChildAt(int position, MenuNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (!CanAttachChild(child)) throw new InvalidOperationException($"節點 '{child.Name}' 無法接回 '{Name}' 底下。");

            child.Parent = this;
            child.UpdateDepthRecursive(Depth + 1);

            var clampedPos = Math.Max(0, Math.Min(position, _children.Count));
            _children.Insert(clampedPos, child);
            _index.Add(child.Name, child);
            ReindexChildren();
        }

        /// <summary>
        /// <paramref name="child"/> 能否用 <see cref="AttachChildAt"/> 接回：沒有父節點、不是本節點或祖先、沒有同名子節點。
        /// </summary>
        internal bool CanAttachChild(MenuNode child)
        {
            if (child == null || child.Parent != null || _index.ContainsKey(child.Name)) return false;

            for (var current = this; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, child)) return false;
            }

            return true;
        }

        /// <summary>
        /// 從父節點移除本節點，並重排兄弟的序號。
        /// </summary>
        /// <returns>是否移除；樹根為 false。</returns>
        public bool RemoveFromParent()
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
        /// 改名已脫離樹的節點，移動時用來避開同名。
        /// </summary>
        /// <exception cref="InvalidOperationException">節點還接在父節點底下。</exception>
        internal void RenameDetached(string newName)
        {
            if (Parent != null) throw new InvalidOperationException("節點還接在父節點底下，請用 Rename。");

            Name = ValidateNodeName(newName);
            InvalidateDisplayText();
        }

        /// <summary>
        /// 改名；兄弟中已有同名節點時，改為把本節點合併進去並移除本節點。
        /// </summary>
        /// <param name="newName">新名稱。</param>
        /// <returns>本節點，或合併後的同名節點。</returns>
        /// <exception cref="InvalidOperationException">對樹根呼叫。</exception>
        /// <exception cref="ArgumentException"><paramref name="newName"/> 為空白或含有保留字元。</exception>
        public MenuNode Rename(string newName)
        {
            if (Parent == null) throw new InvalidOperationException("樹根不可重新命名。");

            var key = ValidateNodeName(newName);
            if (string.Equals(Name, key, StringComparison.Ordinal)) return this;

            var parent = Parent;
            if (parent._index.TryGetValue(key, out MenuNode existing))
            {
                existing.MergeSubtreeFrom(this);
                RemoveFromParent();
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
            if (trimmed.IndexOf(CellExpansion.NAME_SEPARATOR) >= 0)
                throw new ArgumentException($"節點名稱不可含有保留字元 '{CellExpansion.NAME_SEPARATOR}'。", nameof(name));

            return trimmed;
        }

        private void ReindexChildren()
        {
            for (var i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                var newNumber = i + 1;
                if (child.SiblingNumber != newNumber)
                {
                    child.SiblingNumber = newNumber;
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

        IReadOnlyList<ICascadeNode> ICascadeNode.Children => _children;

        /// <summary>
        /// 回傳 <see cref="Name"/>。
        /// </summary>
        public override string ToString() => Name;
    }
}