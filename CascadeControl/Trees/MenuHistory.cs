using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Trees
{
    /// <summary>
    /// 復原／重做的歷史：勾選（<see cref="MenuCheckSet"/>）與樹的編輯（插入、建立子節點、改名、貼上、剪下、刪除、移動）照發生的順序排在同一條時間軸上。
    /// </summary>
    /// <remarks>
    /// 每一步只記差異，不存整棵樹的快照：插入與移除記父節點、位置和節點本身，改名記新舊名稱，勾選記多勾與取消的節點。
    /// 所以一步佔的記憶體只和那一步改到的節點數成正比；被刪除的子樹由那一步留著，復原時接回的是同一批節點物件，
    /// 之後的步驟（例如勾選）指向的節點才對得上。
    /// 每一步都只在它被記錄時的樹狀態上有意義。在程式裡直接改 <see cref="MenuNode"/> 而沒有經過記錄時，請呼叫 <see cref="Clear"/>；
    /// 復原或重做時發現樹已經對不上，也會自動清除整份歷史並回傳 null。
    /// 這個類別不相依 System.Windows.Forms。
    /// </remarks>
    public sealed class MenuHistory
    {
        /// <summary>
        /// <see cref="Capacity"/> 的預設值。
        /// </summary>
        public const int DEFAULT_CAPACITY = 50;

        // 兩個都是最舊的在前面，最新的在最後。
        private readonly List<MenuHistoryStep> _undoSteps = new List<MenuHistoryStep>();

        private readonly List<MenuHistoryStep> _redoSteps = new List<MenuHistoryStep>();

        private int _capacity = DEFAULT_CAPACITY;

        // BeginGroup 開著的期間記下來的步驟，結束時合成一步；沒有開著時為 null。
        private List<MenuHistoryStep> _group;

        private int _groupDepth;

        /// <summary>
        /// 最多保留幾個步驟，預設為 <see cref="DEFAULT_CAPACITY"/>；超過時丟掉最舊的。設為 0 表示不保留歷史。
        /// </summary>
        /// <remarks>調小時，超出的舊步驟會立刻丟掉。</remarks>
        /// <exception cref="ArgumentOutOfRangeException">設為負數。</exception>
        public int Capacity
        {
            get => _capacity;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "不可為負數。");

                _capacity = value;
                Trim(_undoSteps);
                Trim(_redoSteps);
            }
        }

        /// <summary>
        /// 可以復原的步驟數。
        /// </summary>
        public int UndoCount => _undoSteps.Count;

        /// <summary>
        /// 可以重做的步驟數。
        /// </summary>
        public int RedoCount => _redoSteps.Count;

        /// <summary>
        /// 是否有步驟可以復原。
        /// </summary>
        public bool CanUndo => _undoSteps.Count > 0;

        /// <summary>
        /// 是否有步驟可以重做。
        /// </summary>
        public bool CanRedo => _redoSteps.Count > 0;

        /// <summary>
        /// 復原最近的一個步驟。
        /// </summary>
        /// <returns>
        /// 這一步做了什麼、之後該跳到哪個節點；沒有步驟可以復原時為 null。
        /// 樹的結構已經和記錄時對不上（在程式裡直接改過樹）時，整份歷史會被清除，也回傳 null。
        /// </returns>
        public MenuHistoryResult Undo() => Replay(_undoSteps, _redoSteps, undo: true);

        /// <summary>
        /// 重做最近一次被復原的步驟。
        /// </summary>
        /// <returns>同 <see cref="Undo"/>。</returns>
        public MenuHistoryResult Redo() => Replay(_redoSteps, _undoSteps, undo: false);

        /// <summary>
        /// 清除所有復原與重做的步驟；勾選與樹都不變。
        /// </summary>
        public void Clear()
        {
            _undoSteps.Clear();
            _redoSteps.Clear();
            _group?.Clear();
        }

        /// <summary>
        /// 下一個要復原的步驟會不會改到樹的結構；沒有步驟時為 false。
        /// </summary>
        internal bool UndoChangesTree => _undoSteps.Count > 0 && _undoSteps[_undoSteps.Count - 1].ChangesTree;

        /// <summary>
        /// 下一個要重做的步驟會不會改到樹的結構；沒有步驟時為 false。
        /// </summary>
        internal bool RedoChangesTree => _redoSteps.Count > 0 && _redoSteps[_redoSteps.Count - 1].ChangesTree;

        /// <summary>
        /// 開始一組步驟：到 Dispose 為止記下來的步驟合成一步，復原時一起退回。可以巢狀，最外層結束時才合成。
        /// </summary>
        /// <remarks>例如刪除節點與「取消被刪節點的勾選」是同一個操作，要一次復原。</remarks>
        internal IDisposable BeginGroup()
        {
            if (_groupDepth == 0) _group = new List<MenuHistoryStep>();
            _groupDepth++;
            return new GroupScope(this);
        }

        /// <summary>
        /// 記下一步：清掉重做的步驟，超過 <see cref="Capacity"/> 時丟掉最舊的；在 <see cref="BeginGroup"/> 裡面時先暫存，結束時合成一步。
        /// </summary>
        internal void Record(MenuHistoryStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            if (_groupDepth > 0)
            {
                _group.Add(step);
                return;
            }

            Push(step);
        }

        /// <summary>
        /// 記下「在 <paramref name="parent"/> 底下插入了 <paramref name="nodes"/>」；呼叫時節點已經在樹裡。
        /// </summary>
        internal void RecordInsert(MenuTree tree, MenuNode parent, IEnumerable<MenuNode> nodes)
        {
            var entries = nodes.Select(node => new KeyValuePair<int, MenuNode>(node.SiblingNumber - 1, node)).ToList();
            if (entries.Count > 0) Record(new StructureStep(tree, parent, entries, inserted: true));
        }

        /// <summary>
        /// 記下「從 <paramref name="parent"/> 底下移除了這些節點」；每一筆是移除前的位置（0-based）與節點。
        /// </summary>
        internal void RecordRemove(MenuTree tree, MenuNode parent, IEnumerable<KeyValuePair<int, MenuNode>> removed)
        {
            var entries = removed.ToList();
            if (entries.Count > 0) Record(new StructureStep(tree, parent, entries, inserted: false));
        }

        /// <summary>
        /// 記下一次移動；每一筆是節點移動前與移動後的父節點、位置（0-based）與名稱。呼叫時節點已經在新的位置。
        /// </summary>
        internal void RecordMove(MenuTree tree, IEnumerable<MoveStep.Entry> entries)
        {
            var list = entries.ToList();
            if (list.Count > 0) Record(new MoveStep(tree, list));
        }

        /// <summary>
        /// 記下「<paramref name="node"/> 從 <paramref name="oldName"/> 改成現在的名稱」。
        /// </summary>
        internal void RecordRename(MenuTree tree, MenuNode node, string oldName)
        {
            if (!string.Equals(oldName, node.Name, StringComparison.Ordinal)) Record(new RenameStep(tree, node, oldName, node.Name));
        }

        /// <summary>
        /// 記下「取消了 <paramref name="node"/> 的選取」；復原時選回那個節點，重做時再取消選取。
        /// </summary>
        internal void RecordDeselect(MenuTree tree, MenuNode node)
        {
            if (node != null) Record(new DeselectStep(tree, node));
        }

        private void EndGroup()
        {
            if (_groupDepth == 0) return;

            _groupDepth--;
            if (_groupDepth > 0) return;

            var steps = _group;
            _group = null;

            if (steps.Count == 1) Push(steps[0]);
            else if (steps.Count > 1) Push(new GroupStep(steps));
        }

        private void Push(MenuHistoryStep step)
        {
            _redoSteps.Clear();
            _undoSteps.Add(step);
            Trim(_undoSteps);
        }

        // 復原與重做是同一件事：從一邊取出最新的步驟，套用（復原時反過來套用），再放到另一邊。
        private MenuHistoryResult Replay(List<MenuHistoryStep> from, List<MenuHistoryStep> to, bool undo)
        {
            if (from.Count == 0) return null;

            var step = from[from.Count - 1];
            if (!step.CanApply(undo))
            {
                // 樹在記錄之外被改過，這一步（以及更早的步驟）都可能指向不存在的位置。
                Clear();
                return null;
            }

            from.RemoveAt(from.Count - 1);
            to.Add(step);
            return step.Apply(undo);
        }

        private void Trim(List<MenuHistoryStep> steps)
        {
            if (steps.Count > _capacity) steps.RemoveRange(0, steps.Count - _capacity);
        }

        private sealed class GroupScope : IDisposable
        {
            private MenuHistory _owner;

            public GroupScope(MenuHistory owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner?.EndGroup();
                _owner = null;
            }
        }
    }

    /// <summary>
    /// <see cref="MenuHistory.Undo"/> 與 <see cref="MenuHistory.Redo"/> 的結果：這一步改了什麼，之後畫面該跳到哪裡。
    /// </summary>
    public sealed class MenuHistoryResult
    {
        internal MenuHistoryResult(MenuNode focus, IReadOnlyList<MenuNode> nodes, bool treeChanged, bool clearsSelection = false)
        {
            Focus = focus;
            Nodes = nodes ?? Array.Empty<MenuNode>();
            TreeChanged = treeChanged;
            ClearsSelection = clearsSelection;
        }

        /// <summary>
        /// 這一步的結果是取消選取（重做「取消選取」）；此時 <see cref="Focus"/> 為 null。
        /// </summary>
        public bool ClearsSelection { get; }

        /// <summary>
        /// 之後要選取的節點：勾選的步驟是那一步針對的節點（針對整棵樹時為 null）；
        /// 節點回到樹裡時是第一個回來的節點；節點離開樹時是原位置的下一個兄弟（沒有就前一個），一個都不剩時是父節點（第一層時為 null）。
        /// </summary>
        public MenuNode Focus { get; }

        /// <summary>
        /// 這一步插入、移除或改名的節點，依位置排列；只改勾選時是空集合。
        /// </summary>
        public IReadOnlyList<MenuNode> Nodes { get; }

        /// <summary>
        /// 這一步有沒有改到樹的結構（插入、移除、改名）；只改勾選時為 false。
        /// </summary>
        public bool TreeChanged { get; }
    }

    /// <summary>
    /// <see cref="MenuHistory"/> 裡的一步。
    /// </summary>
    internal abstract class MenuHistoryStep
    {
        /// <summary>這一步會不會改到樹的結構。</summary>
        public abstract bool ChangesTree { get; }

        /// <summary>現在的樹還能不能套用這一步（<paramref name="undo"/> 為 true 時是反過來套用）。</summary>
        public abstract bool CanApply(bool undo);

        /// <summary>套用這一步；<paramref name="undo"/> 為 true 時是反過來套用。呼叫前要先確認 <see cref="CanApply"/>。</summary>
        public abstract MenuHistoryResult Apply(bool undo);

        // 節點是不是樹根，或掛在樹根底下。
        protected static bool IsInTreeOrRoot(MenuTree tree, MenuNode node) =>
            ReferenceEquals(node, tree.Root) || (node != null && node.IsDescendantOf(tree.Root));
    }

    /// <summary>
    /// 勾選的一步：多勾了哪些、取消了哪些。
    /// </summary>
    internal sealed class CheckStep : MenuHistoryStep
    {
        private readonly MenuCheckSet _set;
        private readonly MenuNode _target;
        private readonly IReadOnlyList<MenuNode> _added;
        private readonly IReadOnlyList<MenuNode> _removed;

        public CheckStep(MenuCheckSet set, MenuNode target, IReadOnlyList<MenuNode> added, IReadOnlyList<MenuNode> removed)
        {
            _set = set;
            _target = target;
            _added = added;
            _removed = removed;
        }

        public override bool ChangesTree => false;

        // 勾選只是集合的增減，同一條時間軸上前後的結構步驟都退回或重做好了，不必另外檢查。
        public override bool CanApply(bool undo) => true;

        public override MenuHistoryResult Apply(bool undo)
        {
            if (undo) _set.Restore(_removed, _added, _target, MenuCheckChangeReason.Undo);
            else _set.Restore(_added, _removed, _target, MenuCheckChangeReason.Redo);

            return new MenuHistoryResult(_target, null, treeChanged: false);
        }
    }

    /// <summary>
    /// 取消選取的一步：復原時選回被取消的節點，重做時再取消選取。
    /// </summary>
    internal sealed class DeselectStep : MenuHistoryStep
    {
        private readonly MenuTree _tree;
        private readonly MenuNode _node;

        public DeselectStep(MenuTree tree, MenuNode node)
        {
            _tree = tree;
            _node = node;
        }

        public override bool ChangesTree => false;

        // 選取不改樹；只要節點還在樹裡就選得回去（前後的結構步驟都已退回或重做好）。
        public override bool CanApply(bool undo) => !undo || (_node.Parent != null && IsInTreeOrRoot(_tree, _node));

        public override MenuHistoryResult Apply(bool undo) =>
            undo
                ? new MenuHistoryResult(_node, null, treeChanged: false)
                : new MenuHistoryResult(null, null, treeChanged: false, clearsSelection: true);
    }

    /// <summary>
    /// 插入或移除的一步：同一個父節點底下的一個或多個節點，記著各自的位置。
    /// </summary>
    internal sealed class StructureStep : MenuHistoryStep
    {
        private readonly MenuTree _tree;
        private readonly MenuNode _parent;

        // 依位置由小到大；接回時照這個順序，每個節點才會回到原本的位置。
        private readonly IReadOnlyList<KeyValuePair<int, MenuNode>> _entries;

        // 原本的操作是插入（true）還是移除（false）。
        private readonly bool _inserted;

        public StructureStep(MenuTree tree, MenuNode parent, IEnumerable<KeyValuePair<int, MenuNode>> entries, bool inserted)
        {
            _tree = tree;
            _parent = parent;
            _entries = entries.OrderBy(entry => entry.Key).ToList();
            _inserted = inserted;
        }

        public override bool ChangesTree => true;

        public override bool CanApply(bool undo) => undo == _inserted ? CanDetach() : CanAttach();

        public override MenuHistoryResult Apply(bool undo) => undo == _inserted ? Detach() : Attach();

        private IReadOnlyList<MenuNode> Nodes => _entries.Select(entry => entry.Value).ToList();

        private bool CanAttach()
        {
            if (!IsInTreeOrRoot(_tree, _parent)) return false;

            var names = new HashSet<string>(StringComparer.Ordinal);
            return _entries.All(entry => names.Add(entry.Value.Name) && _parent.CanAttachChild(entry.Value));
        }

        private bool CanDetach() => IsInTreeOrRoot(_tree, _parent) && _entries.All(entry => ReferenceEquals(entry.Value.Parent, _parent));

        private MenuHistoryResult Attach()
        {
            foreach (var entry in _entries) _parent.AttachChildAt(entry.Key, entry.Value);

            return new MenuHistoryResult(_entries[0].Value, Nodes, treeChanged: true);
        }

        private MenuHistoryResult Detach()
        {
            foreach (var entry in _entries) entry.Value.RemoveFromParent();

            // 和控制項刪除節點之後一樣：選原位置的下一個兄弟，沒有就前一個，一個都不剩時選父節點。
            var firstIndex = _entries[0].Key;
            MenuNode focus = null;
            if (_parent.Children.Count > 0) focus = _parent.Children[Math.Min(firstIndex, _parent.Children.Count - 1)];
            else if (!ReferenceEquals(_parent, _tree.Root)) focus = _parent;

            return new MenuHistoryResult(focus, Nodes, treeChanged: true);
        }
    }

    /// <summary>
    /// 改名的一步。
    /// </summary>
    internal sealed class RenameStep : MenuHistoryStep
    {
        private readonly MenuTree _tree;
        private readonly MenuNode _node;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameStep(MenuTree tree, MenuNode node, string oldName, string newName)
        {
            _tree = tree;
            _node = node;
            _oldName = oldName;
            _newName = newName;
        }

        public override bool ChangesTree => true;

        public override bool CanApply(bool undo)
        {
            var from = undo ? _newName : _oldName;
            var to = undo ? _oldName : _newName;

            // 目標名稱已經被別的兄弟用掉時，Rename 會合併，那就不是原本的狀態了。
            return _node.Parent != null && IsInTreeOrRoot(_tree, _node)
                && string.Equals(_node.Name, from, StringComparison.Ordinal)
                && !_node.Parent.ContainsChild(to);
        }

        public override MenuHistoryResult Apply(bool undo)
        {
            _node.Rename(undo ? _oldName : _newName);
            return new MenuHistoryResult(_node, new[] { _node }, treeChanged: true);
        }
    }

    /// <summary>
    /// 移動的一步：一個或多個節點（連同子孫）從原本的父節點移到新的父節點底下，可能為了避開同名而改名。
    /// </summary>
    /// <remarks>
    /// 兩個方向做法一樣：先把節點全部拿下來，改成那一邊的名稱，再依位置由小到大接上去。
    /// 全部拿下來之後，每個父節點底下剩的就是沒被移動的兄弟，照位置排序接回去，每個節點都會回到記錄的位置。
    /// </remarks>
    internal sealed class MoveStep : MenuHistoryStep
    {
        private readonly MenuTree _tree;
        private readonly IReadOnlyList<Entry> _entries;

        public MoveStep(MenuTree tree, IReadOnlyList<Entry> entries)
        {
            _tree = tree;
            _entries = entries;
        }

        /// <summary>一個被移動的節點：移動前（From）與移動後（To）的父節點、位置與名稱。</summary>
        internal sealed class Entry
        {
            public MenuNode Node { get; set; }
            public MenuNode FromParent { get; set; }
            public int FromIndex { get; set; }
            public string FromName { get; set; }
            public MenuNode ToParent { get; set; }
            public int ToIndex { get; set; }
            public string ToName { get; set; }

            public MenuNode Parent(bool after) => after ? ToParent : FromParent;

            public int Index(bool after) => after ? ToIndex : FromIndex;

            public string Name(bool after) => after ? ToName : FromName;
        }

        public override bool ChangesTree => true;

        // undo：從移動後回到移動前；redo 反過來。
        public override bool CanApply(bool undo)
        {
            var current = undo;
            var target = !undo;

            if (!_entries.All(entry => ReferenceEquals(entry.Node.Parent, entry.Parent(current))
                && string.Equals(entry.Node.Name, entry.Name(current), StringComparison.Ordinal))) return false;

            // 要接上去的父節點還在樹裡，而且不在被移動的子樹裡（那樣節點拿下來之後它就不在樹裡了）。
            var moved = new HashSet<MenuNode>(_entries.Select(entry => entry.Node));
            foreach (var parent in _entries.Select(entry => entry.Parent(target)).Distinct())
            {
                if (!IsInTreeOrRoot(_tree, parent)) return false;
                for (var node = parent; node != null; node = node.Parent)
                {
                    if (moved.Contains(node)) return false;
                }
            }

            // 名稱不能和留在那裡的兄弟撞名，也不能彼此撞名。
            var taken = new HashSet<Tuple<MenuNode, string>>();
            foreach (var entry in _entries)
            {
                var parent = entry.Parent(target);
                var name = entry.Name(target);
                if (!taken.Add(Tuple.Create(parent, name))) return false;
                if (parent.TryGetChild(name, out var existing) && !moved.Contains(existing)) return false;
            }

            return true;
        }

        public override MenuHistoryResult Apply(bool undo)
        {
            var after = !undo;
            foreach (var entry in _entries) entry.Node.RemoveFromParent();

            foreach (var entry in _entries.OrderBy(entry => entry.Index(after)))
            {
                entry.Node.RenameDetached(entry.Name(after));
                entry.Parent(after).AttachChildAt(entry.Index(after), entry.Node);
            }

            var nodes = _entries.Select(entry => entry.Node).ToList();
            return new MenuHistoryResult(nodes[0], nodes, treeChanged: true);
        }
    }

    /// <summary>
    /// <see cref="MenuHistory.BeginGroup"/> 合成的一步：復原時由後往前退回，重做時由前往後。
    /// </summary>
    internal sealed class GroupStep : MenuHistoryStep
    {
        private readonly IReadOnlyList<MenuHistoryStep> _steps;

        public GroupStep(IEnumerable<MenuHistoryStep> steps)
        {
            _steps = steps.ToList();
        }

        public override bool ChangesTree => _steps.Any(step => step.ChangesTree);

        // 先全部檢查再動手，才不會做到一半停下來。組裡的結構步驟彼此獨立（例如從不同父節點移除，沒有一個在另一個的子樹裡），
        // 其他是勾選，所以各自檢查的結果和依序套用時相同。
        public override bool CanApply(bool undo) => _steps.All(step => step.CanApply(undo));

        public override MenuHistoryResult Apply(bool undo)
        {
            var order = undo ? _steps.Reverse() : _steps;
            var results = order.Select(step => step.Apply(undo)).ToList();

            // 改到樹的步驟合起來代表這個操作：選取跟著第一個，節點全部列出；都沒有時取第一個（復原時是最後記下的那一步）。
            var changed = results.Where(result => result.TreeChanged).ToList();
            if (changed.Count == 0) return results[0];

            return changed.Count == 1 ? changed[0] : new MenuHistoryResult(changed[0].Focus, changed.SelectMany(result => result.Nodes).ToList(), treeChanged: true);
        }
    }
}