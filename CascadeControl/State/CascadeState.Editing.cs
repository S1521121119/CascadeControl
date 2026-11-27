using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.State
{
    // 編輯：改樹、記進歷史、更新選取，最後觸發 TreeEdited。不相依 System.Windows.Forms。
    public partial class CascadeState
    {
        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        /// <summary>
        /// 復原／重做的歷史：綁定這份狀態的所有檢視控制項共用同一條時間軸，樹的編輯與勾選（<see cref="CascadeCheckableState.CheckSet"/>）照發生的順序記在這裡；
        /// 預設最多 <see cref="MenuHistory.DEFAULT_CAPACITY"/> 步。
        /// </summary>
        /// <remarks>
        /// 複製不改樹，不進歷史。<see cref="SetTree"/>、<see cref="SetTopLevelNodes"/>、<see cref="Clear"/> 會清空歷史；換掉檢視控制項的 State 不會。
        /// 在程式裡直接改 <see cref="MenuNode"/> 之後請呼叫 <see cref="MenuHistory.Clear"/>（或 <see cref="MenuCheckSet.Reconcile"/>，它也會清空）。
        /// 要調整保留的步驟數，設定 <see cref="MenuHistory.Capacity"/>。
        /// 直接呼叫 <see cref="MenuHistory.Undo"/> 不會更新選取，也不會觸發 <see cref="TreeEdited"/>；請改用 <see cref="Undo"/>。
        /// </remarks>
        public MenuHistory History { get; } = new MenuHistory();

        /// <summary>
        /// 這份狀態的編輯方法改了樹，或 <see cref="Undo"/>、<see cref="Redo"/> 退回或重做了改到樹的步驟之後觸發；選取已經更新完畢。
        /// </summary>
        /// <remarks>
        /// 複製不改樹，所以不觸發。改名成原本的名稱、復原或重做只改勾選的步驟也不觸發。
        /// 在編輯的同一組歷史步驟裡觸發，處理常式跟著做的異動（例如改勾選）和編輯算同一步。
        /// </remarks>
        public event EventHandler<CascadeTreeEditedEventArgs> TreeEdited;

        /// <summary>
        /// 在 <paramref name="parent"/> 底下第 <paramref name="position"/> 個位置插入名為 <paramref name="name"/> 的新節點，並選取它。
        /// </summary>
        /// <param name="parent">新節點的父節點：<see cref="Tree"/> 的樹根或樹裡的節點。</param>
        /// <param name="position">插入位置（0-based）。</param>
        /// <param name="name">新節點的名稱；同一層已有同名節點時自動加上後綴（例如 <c>Name - 2</c>）。</param>
        /// <returns>新節點；<paramref name="parent"/> 不在 <see cref="Tree"/> 裡時為 null。</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> 是空白或含有保留字元。</exception>
        public MenuNode InsertNode(MenuNode parent, int position, string name)
        {
            if (!IsInTreeOrRoot(parent)) return null;

            return Insert(CascadeEditAction.Insert, parent, position, name);
        }

        /// <summary>
        /// 在葉節點 <paramref name="leaf"/> 底下建立第一個子節點，並選取它。
        /// </summary>
        /// <param name="leaf">要長出子節點的葉節點。</param>
        /// <param name="name">新節點的名稱。</param>
        /// <returns>新節點；<paramref name="leaf"/> 不在 <see cref="Tree"/> 裡或已經有子節點時為 null。</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> 是空白或含有保留字元。</exception>
        public MenuNode CreateChild(MenuNode leaf, string name)
        {
            if (!IsInTree(leaf) || !leaf.IsLeaf) return null;

            return Insert(CascadeEditAction.CreateChild, leaf, 0, name);
        }

        /// <summary>
        /// 把 <paramref name="node"/> 改名為 <paramref name="name"/>，並選取它。
        /// </summary>
        /// <param name="node">要改名的節點。</param>
        /// <param name="name">新名稱；同一層已有同名節點時自動加上後綴，所以不會合併。</param>
        /// <returns>改名後的節點；<paramref name="node"/> 不在 <see cref="Tree"/> 裡，或名稱沒變時為 null。</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> 是空白或含有保留字元。</exception>
        public MenuNode RenameNode(MenuNode node, string name)
        {
            if (!IsInTree(node) || string.Equals(node.Name, name, StringComparison.Ordinal)) return null;

            using (History.BeginGroup())
            {
                var oldName = node.Name;
                var renamed = node.Rename(node.Parent.GetUniqueChildName(name));
                History.RecordRename(Tree, renamed, oldName);

                FinishEdit(CascadeEditAction.Rename, new[] { renamed }, renamed);
                return renamed;
            }
        }

        /// <summary>
        /// 把 <paramref name="sources"/> 的複本（連同子孫）依序插入 <paramref name="parent"/> 底下第 <paramref name="position"/> 個位置起，並選取第一個。
        /// </summary>
        /// <param name="parent">貼上的位置的父節點：<see cref="Tree"/> 的樹根或樹裡的節點。</param>
        /// <param name="position">第一個節點的位置（0-based）。</param>
        /// <param name="sources">要貼上的節點；不會被移動或修改。</param>
        /// <returns>
        /// 貼上的節點，順序與 <paramref name="sources"/> 相同；同一層已有同名節點時名稱自動加上後綴，所以每一個都是新節點。
        /// <paramref name="parent"/> 不在 <see cref="Tree"/> 裡或沒有來源時為空集合。
        /// </returns>
        public IReadOnlyList<MenuNode> PasteNodes(MenuNode parent, int position, IReadOnlyList<MenuNode> sources) =>
            PasteNodes(parent, position, sources, focusSource: null);

        internal IReadOnlyList<MenuNode> PasteNodes(MenuNode parent, int position, IReadOnlyList<MenuNode> sources, MenuNode focusSource)
        {
            if (!IsInTreeOrRoot(parent) || sources == null || sources.Count == 0) return NoMenuNodes;

            using (History.BeginGroup())
            {
                var pasted = new List<MenuNode>(sources.Count);
                foreach (var source in sources)
                {
                    pasted.Add(parent.InsertChildAt(position, source, parent.GetUniqueChildName(source.Name)));
                    position++;
                }

                History.RecordInsert(Tree, parent, pasted);
                var selected = ResolvePastedFocus(sources, pasted, focusSource) ?? pasted[0];
                FinishEdit(CascadeEditAction.Paste, pasted, selected);
                return pasted;
            }
        }

        /// <summary>
        /// 剪下：從樹中移除 <paramref name="nodes"/>（連同子孫）。呼叫端要先把它們放到剪貼簿。
        /// </summary>
        /// <returns>有移除時為 true；規則同 <see cref="DeleteNodes"/>。</returns>
        public bool CutNodes(IReadOnlyList<MenuNode> nodes) => RemoveNodes(CascadeEditAction.Cut, nodes);

        /// <summary>
        /// 刪除 <paramref name="nodes"/>（連同子孫）。呼叫端要先向使用者確認。
        /// </summary>
        /// <param name="nodes">要移除的節點，都在 <see cref="Tree"/> 裡；可以屬於不同的父節點，祖先也在裡面的節點跟著祖先移除，不另外算。</param>
        /// <returns>有移除時為 true；沒有節點，或有節點不在 <see cref="Tree"/> 裡時為 false。</returns>
        /// <remarks>
        /// 移除後選取第一個節點原位置的下一個兄弟（沒有就選前一個），一個都不剩時選父節點。
        /// 移除與 <see cref="OnNodesRemoved"/> 的異動算同一步，復原時一起回來。
        /// </remarks>
        public bool DeleteNodes(IReadOnlyList<MenuNode> nodes) => RemoveNodes(CascadeEditAction.Delete, nodes);

        /// <summary>
        /// 把 <paramref name="nodes"/>（連同子孫）依序移到 <paramref name="newParent"/> 的子節點最後面，並選取第一個。
        /// </summary>
        /// <param name="nodes">要移動的節點，都在 <see cref="Tree"/> 裡；可以屬於不同的父節點，祖先也在裡面的節點跟著祖先移動，不另外算。</param>
        /// <param name="newParent">新的父節點：<see cref="Tree"/> 的樹根或樹裡的節點，不可以是要移動的節點或它們的子孫。</param>
        /// <returns>移動的節點，順序與 <paramref name="nodes"/> 相同；不能移動時為空集合。</returns>
        /// <remarks>
        /// 新的父節點底下已有同名節點時，移過去的節點名稱自動加上後綴（例如 <c>Name - 2</c>），不會合併。
        /// 移動與 <see cref="OnNodesMoved"/> 的異動算同一步，復原時節點回到原本的父節點、位置與名稱。
        /// </remarks>
        public IReadOnlyList<MenuNode> MoveNodes(IReadOnlyList<MenuNode> nodes, MenuNode newParent)
        {
            if (!IsInTreeOrRoot(newParent)) return NoMenuNodes;

            return MoveNodes(nodes, newParent, newParent.Children.Count, focus: null);
        }

        /// <summary>
        /// 把 <paramref name="nodes"/>（連同子孫）依序移到 <paramref name="newParent"/> 底下第 <paramref name="position"/> 個位置（依移動前的順序計算），並選取第一個。
        /// </summary>
        /// <param name="nodes">要移動的節點，都在 <see cref="Tree"/> 裡；可以屬於不同的父節點，祖先也在裡面的節點跟著祖先移動，不另外算。</param>
        /// <param name="newParent">新的父節點：<see cref="Tree"/> 的樹根或樹裡的節點，不可以是要移動的節點或它們的子孫。</param>
        /// <param name="position">插入位置（0-based，依移動前 <paramref name="newParent"/> 的子節點順序計算）。</param>
        /// <returns>移動的節點，順序與 <paramref name="nodes"/> 相同；不能移動或位置沒有改變時為空集合。</returns>
        public IReadOnlyList<MenuNode> MoveNodes(IReadOnlyList<MenuNode> nodes, MenuNode newParent, int position) =>
            MoveNodes(nodes, newParent, position, focus: null);

        internal IReadOnlyList<MenuNode> MoveNodes(IReadOnlyList<MenuNode> nodes, MenuNode newParent, int position, MenuNode focus)
        {
            if (nodes == null || nodes.Count == 0 || !nodes.All(IsInTree) || !IsInTreeOrRoot(newParent)) return NoMenuNodes;

            var roots = TopMost(nodes);
            if (roots.Any(root => IsSelfOrAncestor(root, newParent))) return NoMenuNodes;

            var insertAt = AdjustedInsertIndex(roots, newParent, position);
            if (IsNoOpMoveAt(roots, newParent, insertAt)) return NoMenuNodes;

            using (History.BeginGroup())
            {
                var entries = roots.Select(node => new MoveStep.Entry
                {
                    Node = node,
                    FromParent = node.Parent,
                    FromIndex = node.SiblingNumber - 1,
                    FromName = node.Name,
                }).ToList();

                foreach (var entry in entries) entry.Node.RemoveFromParent();

                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    entry.Node.RenameDetached(newParent.GetUniqueChildName(entry.FromName));
                    newParent.AttachChildAt(insertAt + i, entry.Node);
                    entry.ToParent = newParent;
                    entry.ToIndex = entry.Node.SiblingNumber - 1;
                    entry.ToName = entry.Node.Name;
                }

                History.RecordMove(Tree, entries);
                OnNodesMoved(roots);

                var selected = focus != null && IsInTree(focus) ? focus : roots[0];
                FinishEdit(CascadeEditAction.Move, roots, selected);
                return roots;
            }
        }

        internal static bool IsNoOpMove(IReadOnlyList<MenuNode> roots, MenuNode newParent, int position) =>
            roots == null || roots.Count == 0 || newParent == null
            || IsNoOpMoveAt(roots, newParent, AdjustedInsertIndex(roots, newParent, position));

        private static int AdjustedInsertIndex(IReadOnlyList<MenuNode> roots, MenuNode newParent, int position)
        {
            var clamped = Math.Max(0, Math.Min(position, newParent.Children.Count));
            return clamped - roots.Count(root => ReferenceEquals(root.Parent, newParent) && (root.SiblingNumber - 1) < clamped);
        }

        private static bool IsNoOpMoveAt(IReadOnlyList<MenuNode> roots, MenuNode newParent, int insertAt)
        {
            for (var i = 0; i < roots.Count; i++)
            {
                if (!ReferenceEquals(roots[i].Parent, newParent) || (roots[i].SiblingNumber - 1) != insertAt + i) return false;
            }

            return true;
        }

        private static MenuNode ResolvePastedFocus(IReadOnlyList<MenuNode> sources, IReadOnlyList<MenuNode> pasted, MenuNode focusSource)
        {
            if (focusSource == null) return null;

            for (var i = 0; i < sources.Count; i++)
            {
                if (ReferenceEquals(sources[i], focusSource)) return pasted[i];

                var path = new List<int>();
                for (var current = focusSource; current != null && !ReferenceEquals(current, sources[i]); current = current.Parent)
                {
                    if (current.Parent == null)
                    {
                        path = null;
                        break;
                    }

                    path.Add(current.SiblingNumber - 1);
                }

                if (path == null) continue;

                var target = pasted[i];
                for (var step = path.Count - 1; step >= 0; step--)
                {
                    var index = path[step];
                    if (index < 0 || index >= target.Children.Count) return pasted[i];
                    target = target.Children[index];
                }

                return target;
            }

            return null;
        }

        /// <summary>
        /// 復原 <see cref="History"/> 最近的一個步驟。
        /// </summary>
        /// <returns>這一步做了什麼；沒有步驟，或樹已經和記錄時對不上（這時整份歷史會被清除）時為 null。</returns>
        /// <remarks>
        /// 改到樹的步驟：選取跳到那一步的位置（回到樹裡的節點，或被拿掉的節點原位置的鄰居），
        /// 然後觸發 <see cref="TreeEdited"/>（<see cref="CascadeEditAction.Undo"/>）。只改勾選的步驟：選取跳到那一步針對的節點。
        /// </remarks>
        public MenuHistoryResult Undo() => Replay(History.Undo(), CascadeEditAction.Undo);

        /// <summary>
        /// 重做最近一次被復原的步驟。
        /// </summary>
        /// <returns>同 <see cref="Undo"/>。</returns>
        /// <remarks>選取與事件的規則同 <see cref="Undo"/>，事件的種類是 <see cref="CascadeEditAction.Redo"/>。</remarks>
        public MenuHistoryResult Redo() => Replay(History.Redo(), CascadeEditAction.Redo);

        /// <summary>
        /// 取消選取並記進 <see cref="History"/>：復原會選回那個節點，重做再取消選取。
        /// </summary>
        /// <returns>原本有選取時為 true。</returns>
        internal bool DeselectWithHistory()
        {
            var selected = SelectedNode as MenuNode;
            var recorded = IsInTree(selected);

            if (!ClearSelection()) return false;

            if (recorded) History.RecordDeselect(Tree, selected);
            return true;
        }

        /// <summary>
        /// 觸發 <see cref="TreeEdited"/>。
        /// </summary>
        protected virtual void OnTreeEdited(CascadeTreeEditedEventArgs e) => TreeEdited?.Invoke(this, e);

        /// <summary>
        /// 剪下、刪除把節點移出樹之後、選取更新之前呼叫，和移除在同一組歷史步驟裡；預設不做任何事。<see cref="MoveNodes(IReadOnlyList{MenuNode}, MenuNode)"/> 不呼叫這裡，見 <see cref="OnNodesMoved"/>。
        /// </summary>
        /// <param name="nodes">被移除的節點（不含子孫），已經不在樹裡。</param>
        protected virtual void OnNodesRemoved(IReadOnlyList<MenuNode> nodes)
        {
        }

        /// <summary>
        /// <see cref="MoveNodes(IReadOnlyList{MenuNode}, MenuNode)"/> 把節點接到新的父節點之後、選取更新之前呼叫，和移動在同一組歷史步驟裡；預設不做任何事。
        /// </summary>
        /// <param name="nodes">被移動的節點（不含子孫），已經在新的父節點底下。</param>
        protected virtual void OnNodesMoved(IReadOnlyList<MenuNode> nodes)
        {
        }

        // 祖先也在 nodes 裡的節點跟著祖先走，拿掉；順序不變。
        private static List<MenuNode> TopMost(IReadOnlyList<MenuNode> nodes)
        {
            var set = new HashSet<MenuNode>(nodes);
            return nodes.Distinct().Where(node => !AncestorsOf(node).Any(set.Contains)).ToList();
        }

        private static IEnumerable<MenuNode> AncestorsOf(MenuNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent) yield return current;
        }

        private static bool IsSelfOrAncestor(MenuNode ancestor, MenuNode node) =>
            ReferenceEquals(ancestor, node) || AncestorsOf(node).Contains(ancestor);

        // 節點是否屬於目前的樹；樹根不算。
        internal bool IsInTree(MenuNode node) => node != null && Tree != null && node.IsDescendantOf(Tree.Root);

        internal bool IsInTreeOrRoot(MenuNode node) => Tree != null && (ReferenceEquals(node, Tree.Root) || IsInTree(node));

        private MenuNode Insert(CascadeEditAction action, MenuNode parent, int position, string name)
        {
            using (History.BeginGroup())
            {
                var inserted = parent.InsertChildAt(position, new MenuNode(name), parent.GetUniqueChildName(name));
                History.RecordInsert(Tree, parent, new[] { inserted });

                FinishEdit(action, new[] { inserted }, inserted);
                return inserted;
            }
        }

        // 記下移除前的位置，復原時才接得回原處。
        private bool RemoveNodes(CascadeEditAction action, IReadOnlyList<MenuNode> nodes)
        {
            if (nodes == null || nodes.Count == 0 || !nodes.All(IsInTree)) return false;

            // 每個父節點各記一步（位置是同一個父節點底下的），合成一組；不同父節點的步驟彼此獨立。
            var roots = TopMost(nodes);
            var groups = roots
                .GroupBy(node => node.Parent)
                .Select(group => new
                {
                    Parent = group.Key,
                    Removed = group.Select(node => new KeyValuePair<int, MenuNode>(node.SiblingNumber - 1, node)).ToList(),
                })
                .ToList();

            var parent = groups[0].Parent;
            var firstIndex = groups[0].Removed.Min(entry => entry.Key);

            using (History.BeginGroup())
            {
                foreach (var node in roots) node.RemoveFromParent();
                foreach (var group in groups) History.RecordRemove(Tree, group.Parent, group.Removed);
                OnNodesRemoved(roots);

                MenuNode next = null;
                if (parent.Children.Count > 0) next = parent.Children[Math.Min(firstIndex, parent.Children.Count - 1)];
                else if (!ReferenceEquals(parent, Tree.Root)) next = parent;

                FinishEdit(action, roots, next);
                return true;
            }
        }

        private MenuHistoryResult Replay(MenuHistoryResult result, CascadeEditAction action)
        {
            if (result == null) return null;

            if (result.ClearsSelection)
            {
                ClearSelection();
            }
            else if (result.TreeChanged)
            {
                FinishEdit(action, result.Nodes, result.Focus);
            }
            else if (IsInTree(result.Focus))
            {
                TrySelectNode(result.Focus);
            }

            return result;
        }

        // 改完樹之後：重新整理並選取 select（null 表示不選取），然後觸發 TreeEdited。
        private void FinishEdit(CascadeEditAction action, IReadOnlyList<MenuNode> nodes, MenuNode select)
        {
            Refresh(select?.FullPath ?? NoMenuNodes);
            OnTreeEdited(new CascadeTreeEditedEventArgs(action, nodes));
        }
    }
}