using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeState"/> 內部狀態變更的種類，供同組件的檢視控制項決定要同步哪些畫面狀態。
    /// </summary>
    internal enum CascadeStateInvalidation
    {
        /// <summary>資料來源被重設（<see cref="CascadeState.SetTree"/>、<see cref="CascadeState.SetTopLevelNodes"/> 或 <see cref="CascadeState.Clear"/>）。</summary>
        Reset,

        /// <summary>底層節點結構異動後重新整理（<see cref="CascadeState.Refresh"/>）。</summary>
        Refreshed,

        /// <summary>選取路徑改變（<see cref="CascadeState.TrySelectPath"/>、<see cref="CascadeState.StepBack"/>、<see cref="CascadeState.StepForward"/> 等）。</summary>
        SelectionChanged,

        /// <summary>只清除記憶軌跡，選取路徑未變（<see cref="CascadeState.ClearTrail"/>）。</summary>
        TrailCleared,
    }

    /// <summary>
    /// 多個檢視控制項可共享的選單資料與導覽狀態：持有 <see cref="Tree"/>、頂層節點、選取路徑與記憶軌跡。
    /// </summary>
    /// <remarks>
    /// 狀態集中在這個物件，檢視控制項只負責呈現與把使用者操作轉成對它的呼叫。
    /// 這個檔案不相依 System.Windows.Forms，可以在非 UI 邏輯與單元測試中直接使用。
    /// </remarks>
    public class CascadeState
    {
        private static readonly IReadOnlyList<ICascadeNode> NoNodes = new ICascadeNode[0];

        private readonly CascadeNavigator _navigator = new CascadeNavigator();

        private IEnumerable<ICascadeNode> _topLevelSource;
        private IReadOnlyList<ICascadeNode> _topLevelNodes = NoNodes;
        private object _topLevelKey = new object();
        private string _pathSeparator = CascadeNavigator.DEFAULT_PATH_SEPARATOR;

        /// <summary>
        /// 目前顯示的選單樹：由 <see cref="SetTree"/> 設定；以 <see cref="SetTopLevelNodes"/> 設定其他資料或 <see cref="Clear"/> 後為 null。
        /// </summary>
        public MenuTree Tree { get; private set; }

        /// <summary>
        /// 第一層的頂層節點清單；沒有資料時為空集合。
        /// </summary>
        public IReadOnlyList<ICascadeNode> TopLevelNodes => _topLevelNodes;

        /// <summary>
        /// 目前選取的最深節點；未選取時為 null。
        /// </summary>
        public ICascadeNode SelectedNode => _navigator.SelectedNode;

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。不包含退回後留下的記憶軌跡。
        /// </summary>
        public IReadOnlyList<ICascadeNode> SelectedPath => _navigator.SelectedPath.ToList();

        /// <summary>
        /// 目前選取路徑上各層的 <see cref="ICascadeNode.Name"/>，以 <see cref="PathSeparator"/> 串接；未選取時為空字串。
        /// </summary>
        public string SelectedPathText => string.Join(_pathSeparator, _navigator.SelectedPath.Select(node => node.Name));

        /// <summary>
        /// 目前的選取是否停在葉節點。
        /// </summary>
        public bool IsLeafSelected => IsLeaf(SelectedNode);

        /// <summary>
        /// 目前操作中的欄位索引（0-based）：最深選取項所在的欄；未選取時為 0。
        /// </summary>
        public int ActiveColumnIndex => _navigator.ActiveColumnIndex;

        /// <summary>
        /// <see cref="SelectedPathText"/> 與共享檢視使用的層級分隔字串。
        /// </summary>
        public string PathSeparator
        {
            get => _pathSeparator;
            set => _pathSeparator = value ?? string.Empty;
        }

        /// <summary>
        /// 選取路徑改變後觸發，包含 <see cref="SetTree"/>、<see cref="SetTopLevelNodes"/>、<see cref="Clear"/>、<see cref="Refresh"/> 與導覽操作。
        /// </summary>
        public event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

        /// <summary>
        /// 設定要顯示的選單樹，以 <see cref="MenuTree.TopLevelNodes"/> 作為第一層內容，並清除選取與記憶軌跡。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空。</param>
        public virtual void SetTree(MenuTree tree)
        {
            ResetTopLevelNodes(tree?.TopLevelNodes, tree);
        }

        /// <summary>
        /// 重新設定第一層的頂層節點集合，並清除選取與記憶軌跡；<see cref="Tree"/> 會變成 null。
        /// </summary>
        /// <param name="topLevelNodes">第一層節點；傳 null 或空集合表示清空。</param>
        public virtual void SetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes)
        {
            ResetTopLevelNodes(topLevelNodes, null);
        }

        /// <summary>
        /// 當底層節點結構變更後，重新讀取頂層節點並套用指定的選取路徑。
        /// </summary>
        /// <param name="selectPath">更新後要選取的路徑；傳 null 則保留目前仍存在的選取路徑，傳空集合則清除選取。</param>
        public void Refresh(IEnumerable<ICascadeNode> selectPath = null)
        {
            var targetPath = (selectPath ?? _navigator.SelectedPath).ToList();
            _topLevelNodes = _topLevelSource?.ToList() ?? NoNodes;
            _topLevelKey = new object();

            if (targetPath.Count > 0)
            {
                _navigator.SelectPath(MatchPath(targetPath));
            }
            else
            {
                _navigator.ClearSelection();
            }

            OnInvalidated(CascadeStateInvalidation.Refreshed);
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        /// <summary>
        /// 清除資料、選取與記憶軌跡。
        /// </summary>
        public void Clear() => SetTopLevelNodes(null);

        /// <summary>
        /// 選取指定的 <see cref="MenuNode"/>（依其 <see cref="MenuNode.FullPath"/> 由第一層開始選取）。
        /// </summary>
        /// <param name="node">要選取的節點。</param>
        /// <returns>成功選取時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public bool TrySelectNode(MenuNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            return TrySelectPath(node.FullPath);
        }

        /// <summary>
        /// 從第一層開始，依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺到深的節點；以 <see cref="object.Equals(object)"/> 比對。</param>
        /// <returns>整條路徑都選到時為 true；中途找不到時停在已比對成功的那一段，並回傳 false。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> 為 null。</exception>
        public bool TrySelectPath(IEnumerable<ICascadeNode> path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var steps = path.ToList();
            var matched = MatchPath(steps);

            _navigator.SelectPath(matched);
            NotifySelectionChanged();
            return matched.Count == steps.Count;
        }

        /// <summary>
        /// 往上一層退回一步；退回的那一層留在記憶軌跡裡。
        /// </summary>
        /// <returns>選取有改變時為 true；已在第一層或未選取時回傳 false。</returns>
        public bool StepBack()
        {
            if (!_navigator.StepBack()) return false;

            NotifySelectionChanged();
            return true;
        }

        /// <summary>
        /// 往下一層前進一步：有記憶軌跡時選上次的節點，否則選第一個子節點。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        public bool StepForward()
        {
            if (!_navigator.StepForward()) return false;

            NotifySelectionChanged();
            return true;
        }

        /// <summary>
        /// 清除所有分支的記憶軌跡，只保留目前的選取路徑；不觸發 <see cref="SelectedPathChanged"/>。
        /// </summary>
        public void ClearTrail()
        {
            _navigator.ClearTrail();
            OnInvalidated(CascadeStateInvalidation.TrailCleared);
        }

        /// <summary>
        /// 觸發 <see cref="SelectedPathChanged"/>。
        /// </summary>
        protected virtual void OnSelectedPathChanged(CascadeNodeEventArgs e) => SelectedPathChanged?.Invoke(this, e);

        internal object TopLevelKey => _topLevelKey;

        internal IReadOnlyList<ICascadeNode> PathWithTrail => _navigator.PathWithTrail;

        internal int SelectedDepth => _navigator.SelectedDepth;

        internal ICascadeNode GetTrailNodeAt(int columnIndex) => _navigator.GetTrailNodeAt(columnIndex);

        internal event Action<CascadeStateInvalidation> Invalidated;

        internal bool SelectNodeAt(int columnIndex, ICascadeNode node)
        {
            if (!_navigator.SelectNodeAt(columnIndex, node)) return false;

            NotifySelectionChanged();
            return true;
        }

        internal bool ClearSelection()
        {
            if (!_navigator.ClearSelection()) return false;

            NotifySelectionChanged();
            return true;
        }

        internal CascadeNodeEventArgs CreateNodeEventArgs()
        {
            var path = _navigator.SelectedPath.ToList();
            return new CascadeNodeEventArgs(path, path.Count == 0 ? _topLevelNodes : null, _pathSeparator);
        }

        private void ResetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes, MenuTree tree)
        {
            Tree = tree;
            _topLevelSource = topLevelNodes;
            _topLevelNodes = topLevelNodes?.ToList() ?? NoNodes;
            _topLevelKey = new object();
            _navigator.Clear();

            OnInvalidated(CascadeStateInvalidation.Reset);
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        private List<ICascadeNode> MatchPath(IReadOnlyList<ICascadeNode> steps)
        {
            var matched = new List<ICascadeNode>();
            var nodes = _topLevelNodes;

            foreach (var step in steps)
            {
                var found = nodes.FirstOrDefault(node => node != null && Equals(node, step));
                if (found == null) break;

                matched.Add(found);
                nodes = CascadeNavigator.ChildrenOf(found);
            }

            return matched;
        }

        private void NotifySelectionChanged()
        {
            OnInvalidated(CascadeStateInvalidation.SelectionChanged);
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        private void OnInvalidated(CascadeStateInvalidation kind) => Invalidated?.Invoke(kind);

        private static bool IsLeaf(ICascadeNode node) => node != null && CascadeNavigator.ChildrenOf(node).Count == 0;
    }
}
