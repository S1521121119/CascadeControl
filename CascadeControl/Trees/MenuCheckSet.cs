using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Trees
{
    /// <summary>
    /// 對一棵 <see cref="MenuTree"/> 做的一份勾選：記住哪些節點被勾選，每一次異動記進 <see cref="History"/> 以便復原／重做。
    /// </summary>
    /// <remarks>
    /// 勾選不是節點本身的資料，而是「對這棵樹做的一份選擇」，所以放在樹的外面，同一棵樹可以同時有好幾份。
    /// 勾選集合永遠<b>向上封閉</b>：節點被勾選，它的所有祖先就一定也被勾選。
    /// 所以勾選一個節點會自動補勾祖先，取消一個節點會連同它的所有子孫一起取消；
    /// 中間節點可以只勾自己，代表輸出到這一層為止。
    /// 每一次呼叫異動方法算一個復原步驟，只記錄實際的差異；沒有造成任何改變的呼叫不會進入歷史。
    /// 歷史放在 <see cref="History"/>，可以和樹的編輯共用同一條時間軸（<see cref="Views.Columns.CascadeCheckboxView"/> 就是這樣用的）。
    /// 在程式裡直接改了樹的結構（插入、刪除、改名合併）之後要呼叫 <see cref="Reconcile"/>。
    /// 這個類別不相依 System.Windows.Forms。
    /// </remarks>
    public sealed class MenuCheckSet
    {
        private static readonly MenuNode[] NoNodes = new MenuNode[0];

        private readonly HashSet<MenuNode> _checked = new HashSet<MenuNode>();

        // IsFullyChecked 在繪製時每一列都會問一次，結果快取到下一次勾選狀態改變為止。
        private readonly Dictionary<MenuNode, bool> _fullyCheckedCache = new Dictionary<MenuNode, bool>();

        private MenuHistory _history = new MenuHistory();

        /// <summary>
        /// 建立一份空的勾選。
        /// </summary>
        /// <param name="tree">要勾選的樹。</param>
        /// <exception cref="ArgumentNullException"><paramref name="tree"/> 為 null。</exception>
        public MenuCheckSet(MenuTree tree)
        {
            Tree = tree ?? throw new ArgumentNullException(nameof(tree));
        }

        /// <summary>
        /// 這份勾選所屬的樹。
        /// </summary>
        public MenuTree Tree { get; }

        /// <summary>
        /// 勾選的異動記在哪一份歷史；預設是這份勾選自己的一份。
        /// </summary>
        /// <remarks>
        /// 換成別的 <see cref="MenuHistory"/>，就能和樹的編輯排在同一條時間軸上；
        /// <see cref="Views.Columns.CascadeCheckboxView.CheckSet"/> 被設定時會把它換成控制項的 <see cref="Views.Columns.CascadeView.History"/>。
        /// 換掉時，舊歷史裡的步驟留在舊的那一份，不會搬過來。
        /// </remarks>
        /// <exception cref="ArgumentNullException">設為 null。</exception>
        public MenuHistory History
        {
            get => _history;
            set => _history = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 勾選狀態改變後觸發，包含復原、重做、<see cref="Apply"/>、<see cref="Reconcile"/>，以及控制項剪下、刪除節點時一起取消的勾選；沒有實際變化時不觸發。
        /// </summary>
        public event EventHandler<MenuCheckChangedEventArgs> Changed;

        #region 查詢

        /// <summary>
        /// 被勾選的節點數。
        /// </summary>
        public int Count => _checked.Count;

        /// <summary>
        /// 依樹的順序（前序：父節點在子節點之前）列舉所有被勾選的節點。
        /// </summary>
        public IEnumerable<MenuNode> CheckedNodes
        {
            get
            {
                // 向上封閉：沒勾選的節點底下不會有勾選的節點，所以不必往下走。
                var stack = new Stack<MenuNode>();
                PushCheckedChildren(stack, Tree.Root);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    yield return node;
                    PushCheckedChildren(stack, node);
                }
            }
        }

        /// <summary>
        /// 節點是否屬於 <see cref="Tree"/>；樹根本身不算。
        /// </summary>
        /// <param name="node">要檢查的節點；null 回傳 false。</param>
        public bool Contains(MenuNode node)
        {
            if (node == null || ReferenceEquals(node, Tree.Root)) return false;

            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, Tree.Root)) return true;
            }

            return false;
        }

        /// <summary>
        /// 節點是否被勾選。
        /// </summary>
        /// <param name="node">要查詢的節點；null 或不屬於 <see cref="Tree"/> 的節點回傳 false。</param>
        public bool IsChecked(MenuNode node) => node != null && _checked.Contains(node);

        /// <summary>
        /// 節點自己和它的所有子孫是否都被勾選。勾選了但這裡回傳 false，就是「只勾了一部分」。
        /// </summary>
        /// <param name="node">要查詢的節點；null 或不屬於 <see cref="Tree"/> 的節點回傳 false。</param>
        public bool IsFullyChecked(MenuNode node)
        {
            if (!IsChecked(node)) return false;
            if (_fullyCheckedCache.TryGetValue(node, out var cached)) return cached;

            var result = node.Children.All(IsFullyChecked);
            _fullyCheckedCache[node] = result;
            return result;
        }

        #endregion 查詢

        #region 異動

        /// <summary>
        /// 勾選節點，並補勾它的所有祖先；子孫不動。
        /// </summary>
        /// <param name="node">要勾選的節點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool Check(MenuNode node)
        {
            Validate(node);
            return Commit(node, Unchecked(PathTo(node)), NoNodes, MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 取消節點，連同它的所有子孫；祖先不動。
        /// </summary>
        /// <param name="node">要取消的節點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool Uncheck(MenuNode node)
        {
            Validate(node);
            return Commit(node, NoNodes, Checked(Subtree(node)), MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 節點已勾選就 <see cref="Uncheck"/>，否則 <see cref="Check"/>。
        /// </summary>
        /// <param name="node">要切換的節點。</param>
        /// <returns>勾選狀態有實際改變時為 true（一定會改變）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool Toggle(MenuNode node)
        {
            Validate(node);
            return IsChecked(node) ? Uncheck(node) : Check(node);
        }

        /// <summary>
        /// 勾選節點、它的所有子孫，以及所有祖先。
        /// </summary>
        /// <param name="node">子樹的起點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool CheckSubtree(MenuNode node)
        {
            Validate(node);
            return Commit(node, Unchecked(PathTo(node.Parent).Concat(Subtree(node))), NoNodes, MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 勾選節點所在那一層的全部兄弟節點（包含它自己），並補勾祖先；它們的子孫不動，原本勾或沒勾都維持原狀。
        /// </summary>
        /// <param name="node">那一層的任何一個節點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool CheckSiblings(MenuNode node)
        {
            Validate(node);
            var nodes = PathTo(node.Parent).Concat(node.Parent.Children);
            return Commit(node, Unchecked(nodes), NoNodes, MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 勾選節點所在那一層的全部兄弟節點（包含它自己）與它們的所有子孫，並補勾祖先。
        /// </summary>
        /// <param name="node">那一層的任何一個節點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool CheckSiblingSubtrees(MenuNode node)
        {
            Validate(node);
            var nodes = PathTo(node.Parent).Concat(node.Parent.Children.SelectMany(Subtree));
            return Commit(node, Unchecked(nodes), NoNodes, MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 取消節點所在那一層的全部兄弟節點（包含它自己）與它們的所有子孫；祖先不動。
        /// </summary>
        /// <param name="node">那一層的任何一個節點。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> 不屬於 <see cref="Tree"/>，或是樹根。</exception>
        public bool UncheckSiblings(MenuNode node)
        {
            Validate(node);
            return Commit(node, NoNodes, Checked(node.Parent.Children.SelectMany(Subtree)), MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 勾選整棵樹的所有節點。
        /// </summary>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        public bool CheckAll()
        {
            return Commit(null, Unchecked(Tree.Root.Children.SelectMany(Subtree)), NoNodes, MenuCheckChangeReason.User);
        }

        /// <summary>
        /// 取消所有勾選。
        /// </summary>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        public bool UncheckAll()
        {
            return Commit(null, NoNodes, CheckedNodes.ToList(), MenuCheckChangeReason.User);
        }

        #endregion 異動

        #region 輸出與還原

        /// <summary>
        /// 產生一棵只含勾選節點的新樹，順序與原樹一致；沒有任何勾選時是空樹。
        /// </summary>
        /// <remarks>
        /// 新樹可以直接交給 <see cref="MenuTree.ToRows(MenuRowStyle)"/> 或 Services 層輸出。
        /// 新樹的節點是另外建立的，編號（<see cref="MenuNode.SiblingNumber"/>）會重新從 1 起算；
        /// 只勾了自己的中間節點在新樹裡會變成葉節點。
        /// </remarks>
        public MenuTree ToTree()
        {
            var tree = new MenuTree();

            // 只要加入每一條勾選路徑的終點，路徑上的祖先就會跟著建立。
            foreach (var node in CheckedNodes)
            {
                if (!node.Children.Any(IsChecked)) tree.AddPath(node.FullPath.Select(step => step.Name));
            }

            return tree;
        }

        /// <summary>
        /// 把勾選狀態換成 <paramref name="subset"/> 裡有的節點：依名稱路徑對應回 <see cref="Tree"/>，
        /// 對應得到的節點勾選，其餘全部取消。整個呼叫算一個復原步驟。
        /// </summary>
        /// <param name="subset">
        /// 要套用的子集合，通常是先前 <see cref="ToTree"/> 的結果（或它存檔後再讀回來的樹）。
        /// 裡面的每一個節點都算勾選，不只有葉節點。
        /// </param>
        /// <param name="warnings">在 <see cref="Tree"/> 裡找不到的路徑，每一條一則訊息；全部對應得到時是空集合。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="subset"/> 為 null。</exception>
        public bool Apply(MenuTree subset, out IList<string> warnings)
        {
            if (subset == null) throw new ArgumentNullException(nameof(subset));

            var messages = new List<string>();
            var wanted = new HashSet<MenuNode>();

            // 成對往下走：subset 的節點與它在原樹裡對應的節點。
            var stack = new Stack<KeyValuePair<MenuNode, MenuNode>>();
            stack.Push(new KeyValuePair<MenuNode, MenuNode>(subset.Root, Tree.Root));

            while (stack.Count > 0)
            {
                var pair = stack.Pop();

                for (var i = pair.Key.Children.Count - 1; i >= 0; i--)
                {
                    var wantedChild = pair.Key.Children[i];
                    if (pair.Value.TryGetChild(wantedChild.Name, out var match))
                    {
                        wanted.Add(match);
                        stack.Push(new KeyValuePair<MenuNode, MenuNode>(wantedChild, match));
                    }
                    else
                    {
                        var skipped = Subtree(wantedChild).Count() - 1;
                        messages.Add(
                            $"找不到路徑「{wantedChild.FullPathText}」，已略過"
                            + (skipped > 0 ? $"（連同它底下的 {skipped} 個節點）。" : "。"));
                    }
                }
            }

            // 由後往前處理的，訊息反過來才是樹的順序。
            messages.Reverse();
            warnings = messages;

            var added = Tree.Root.Children.SelectMany(Subtree).Where(node => wanted.Contains(node) && !_checked.Contains(node)).ToList();
            var removed = CheckedNodes.Where(node => !wanted.Contains(node)).ToList();
            return Commit(null, added, removed, MenuCheckChangeReason.Apply);
        }

        #endregion 輸出與還原

        #region 對齊樹的結構

        /// <summary>
        /// 在程式裡直接改了樹的結構之後呼叫：丟掉已經不在樹裡的節點，補勾缺少的祖先讓集合維持向上封閉，並清除 <see cref="History"/>。
        /// </summary>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <remarks>
        /// 歷史記錄的是節點本身，沒有經過記錄的結構變動會讓舊步驟指向已經不存在的位置，所以一律清除，不論勾選狀態有沒有變。
        /// 新加入的節點預設不勾選。透過控制項的編輯功能改樹時不必呼叫，控制項會自己處理並保留歷史。
        /// </remarks>
        public bool Reconcile()
        {
            _history.Clear();
            _fullyCheckedCache.Clear();

            var removed = _checked.Where(node => !Contains(node)).ToList();
            foreach (var node in removed) _checked.Remove(node);

            var added = new List<MenuNode>();
            foreach (var node in _checked.ToList())
            {
                for (var ancestor = node.Parent; ancestor != null && !ReferenceEquals(ancestor, Tree.Root); ancestor = ancestor.Parent)
                {
                    if (_checked.Add(ancestor)) added.Add(ancestor);
                }
            }

            if (added.Count == 0 && removed.Count == 0) return false;

            Changed?.Invoke(this, new MenuCheckChangedEventArgs(added, removed, null, MenuCheckChangeReason.Reconcile));
            return true;
        }

        /// <summary>
        /// 取消 <paramref name="removedRoots"/> 與它們所有子孫的勾選，算一個步驟記進 <see cref="History"/>。
        /// </summary>
        /// <param name="removedRoots">剛從樹中移除的節點（控制項剪下、刪除的節點）。</param>
        /// <returns>勾選狀態有實際改變時為 true。</returns>
        /// <remarks>
        /// 控制項在同一組步驟裡先移除節點、再呼叫這裡，所以復原刪除時勾選也會一起回來。
        /// 移除一整棵子樹不會破壞向上封閉，不必補勾祖先。
        /// </remarks>
        internal bool UncheckRemoved(IEnumerable<MenuNode> removedRoots)
        {
            var removed = removedRoots.SelectMany(Subtree).Where(_checked.Contains).ToList();
            return Commit(null, NoNodes, removed, MenuCheckChangeReason.Reconcile);
        }

        /// <summary>
        /// 復原或重做勾選的一步：直接套用差異並觸發 <see cref="Changed"/>，不記進歷史。
        /// </summary>
        internal void Restore(IReadOnlyList<MenuNode> added, IReadOnlyList<MenuNode> removed, MenuNode target, MenuCheckChangeReason reason)
        {
            ApplyDifference(added, removed);
            Changed?.Invoke(this, new MenuCheckChangedEventArgs(added, removed, target, reason));
        }

        #endregion 對齊樹的結構

        /// <summary>
        /// 套用一次異動並記進 <see cref="History"/>；沒有任何差異時什麼都不做。
        /// </summary>
        private bool Commit(MenuNode target, IReadOnlyList<MenuNode> added, IReadOnlyList<MenuNode> removed, MenuCheckChangeReason reason)
        {
            if (added.Count == 0 && removed.Count == 0) return false;

            ApplyDifference(added, removed);
            _history.Record(new CheckStep(this, target, added, removed));

            Changed?.Invoke(this, new MenuCheckChangedEventArgs(added, removed, target, reason));
            return true;
        }

        private void ApplyDifference(IReadOnlyList<MenuNode> added, IReadOnlyList<MenuNode> removed)
        {
            foreach (var node in removed) _checked.Remove(node);
            foreach (var node in added) _checked.Add(node);
            _fullyCheckedCache.Clear();
        }

        private void Validate(MenuNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!Contains(node)) throw new ArgumentException($"節點 '{node.Name}' 不屬於這份勾選的樹，或是樹根。", nameof(node));
        }

        private void PushCheckedChildren(Stack<MenuNode> stack, MenuNode parent)
        {
            for (var i = parent.Children.Count - 1; i >= 0; i--)
            {
                if (_checked.Contains(parent.Children[i])) stack.Push(parent.Children[i]);
            }
        }

        // 從第一層到 node（含）；node 是樹根時為空。
        private static IEnumerable<MenuNode> PathTo(MenuNode node) => node.FullPath;

        // node 與它的所有子孫，前序。
        private static IEnumerable<MenuNode> Subtree(MenuNode node)
        {
            var stack = new Stack<MenuNode>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                for (var i = current.Children.Count - 1; i >= 0; i--) stack.Push(current.Children[i]);
            }
        }

        private List<MenuNode> Unchecked(IEnumerable<MenuNode> nodes) => nodes.Where(node => !_checked.Contains(node)).ToList();

        private List<MenuNode> Checked(IEnumerable<MenuNode> nodes) => nodes.Where(_checked.Contains).ToList();
    }
}