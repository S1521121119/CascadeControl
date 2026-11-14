using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView"/> 的導覽狀態：目前的選取路徑，以及每個節點「上次在底下選了哪一個子節點」的記憶軌跡。
    /// </summary>
    /// <remarks>
    /// 記憶是逐節點記的：在某一層改選別的項目，只會改寫那一層的父節點那一筆，其他分支的記憶都還在，
    /// 所以切回原本的分支時，按 → 就能沿著上次的路線走回去。
    /// <see cref="PathWithTrail"/>（選取 + 記憶軌跡尾段）不另外存，而是每次狀態改變時，從最深的選取項沿記憶往下查出來並快取。
    /// 這是控制項唯一的真實狀態，欄位 ListBox 只負責把它畫出來；
    /// 不相依 WinForms，所以導覽規則可以直接用單元測試驗證。
    /// 節點一律以 <see cref="object.Equals(object, object)"/> 與 <see cref="object.GetHashCode"/> 比對。
    /// </remarks>
    internal sealed class CascadeNavigator
    {
        private static readonly IReadOnlyList<ICascadeNode> NoNodes = new ICascadeNode[0];

        // 第一層沒有父節點，用這個物件當它在記憶表裡的 key。
        private static readonly object RootKey = new object();

        private readonly List<ICascadeNode> _selectedPath = new List<ICascadeNode>();
        private readonly Dictionary<object, ICascadeNode> _lastChild = new Dictionary<object, ICascadeNode>();
        private readonly List<ICascadeNode> _pathWithTrail = new List<ICascadeNode>();

        /// <summary>
        /// 選取路徑加上記憶軌跡尾段，由淺至深；前 <see cref="SelectedDepth"/> 項是選取，其餘是從最深選取項沿記憶查出來的路線。
        /// </summary>
        public IReadOnlyList<ICascadeNode> PathWithTrail => _pathWithTrail;

        /// <summary>
        /// 選取的層數；0 代表未選取。
        /// </summary>
        public int SelectedDepth => _selectedPath.Count;

        /// <summary>
        /// 選取路徑，由淺至深。
        /// </summary>
        public IEnumerable<ICascadeNode> SelectedPath => _selectedPath;

        /// <summary>
        /// 選取的最深節點；未選取時為 null。
        /// </summary>
        public ICascadeNode SelectedNode => SelectedDepth > 0 ? _selectedPath[SelectedDepth - 1] : null;

        /// <summary>
        /// 目前操作中的欄位索引：最深選取項所在的欄；未選取時為第一欄（0）。
        /// </summary>
        public int ActiveColumnIndex => Math.Max(SelectedDepth - 1, 0);

        /// <summary>
        /// 第 <paramref name="columnIndex"/> 欄的記憶軌跡節點；那一欄在選取範圍內或沒有記憶時為 null。
        /// </summary>
        public ICascadeNode GetTrailNodeAt(int columnIndex) =>
            columnIndex >= SelectedDepth && columnIndex < _pathWithTrail.Count ? _pathWithTrail[columnIndex] : null;

        /// <summary>
        /// 回傳節點的子節點；節點為 null 或沒有子節點時回傳空集合。
        /// </summary>
        public static IReadOnlyList<ICascadeNode> ChildrenOf(ICascadeNode node) => node?.Children ?? NoNodes;

        /// <summary>
        /// 選取第 <paramref name="columnIndex"/> 欄的 <paramref name="node"/>：選取變成 <see cref="PathWithTrail"/> 的前 <paramref name="columnIndex"/> 項再接上 <paramref name="node"/>，
        /// 並記住它是上一層的「上次選的子節點」。
        /// </summary>
        /// <remarks>
        /// 點了路徑或記憶中的同一項時，效果等同連按 ← 或 →；點了不同的項目時，只改寫上一層那一筆記憶，其他分支不受影響。
        /// </remarks>
        /// <returns>選取有改變時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="columnIndex"/> 小於 0 或大於 <see cref="PathWithTrail"/> 的長度。</exception>
        public bool SelectNodeAt(int columnIndex, ICascadeNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (columnIndex < 0 || columnIndex > _pathWithTrail.Count)
                throw new ArgumentOutOfRangeException(nameof(columnIndex), columnIndex, $"欄位索引必須介於 0 與 {_pathWithTrail.Count} 之間。");

            var path = _pathWithTrail.Take(columnIndex).ToList();
            path.Add(node);

            if (path.SequenceEqual(_selectedPath)) return false;

            SetSelection(path);
            return true;
        }

        /// <summary>
        /// 往左退一層，退回的那一層留在記憶軌跡裡；已在第一層或未選取時不動作。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        public bool StepBack()
        {
            if (SelectedDepth <= 1) return false;

            _selectedPath.RemoveAt(SelectedDepth - 1);
            RebuildPathWithTrail();
            return true;
        }

        /// <summary>
        /// 往右進一層：有記憶軌跡時選上次的節點；沒有記憶而最深選取項有子節點時，選它的第一個子節點。
        /// 未選取時，第一層有記憶就選上次的節點，否則不動作。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        public bool StepForward()
        {
            if (_pathWithTrail.Count > SelectedDepth) return SelectNodeAt(SelectedDepth, _pathWithTrail[SelectedDepth]);

            if (SelectedDepth == 0) return false;

            var children = ChildrenOf(SelectedNode);
            return children.Count > 0 && children[0] != null && SelectNodeAt(SelectedDepth, children[0]);
        }

        /// <summary>
        /// 清除選取，記憶軌跡保留。
        /// </summary>
        /// <returns>原本有選取時為 true。</returns>
        public bool ClearSelection()
        {
            if (SelectedDepth == 0) return false;

            _selectedPath.Clear();
            RebuildPathWithTrail();
            return true;
        }

        /// <summary>
        /// 忘掉所有記憶軌跡，只留下目前選取路徑本身；選取不變。
        /// </summary>
        /// <remarks>選取路徑上的那幾筆要留著，否則按 ← 退回後，按 → 會走不回剛剛的位置。</remarks>
        public void ClearTrail()
        {
            _lastChild.Clear();
            RecordTrail(_selectedPath);
            RebuildPathWithTrail();
        }

        /// <summary>
        /// 清除選取與所有記憶軌跡，用於換上一棵新的樹。
        /// </summary>
        public void Clear()
        {
            _selectedPath.Clear();
            _lastChild.Clear();
            _pathWithTrail.Clear();
        }

        /// <summary>
        /// 以 <paramref name="path"/> 取代選取，並記住路徑經過的每一層；其他分支的記憶保留。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="path"/> 中含有 null。</exception>
        public void SelectPath(IEnumerable<ICascadeNode> path)
        {
            var steps = path?.ToList() ?? new List<ICascadeNode>();
            if (steps.Any(step => step == null))
                throw new ArgumentException("路徑中不可含有 null。", nameof(path));

            SetSelection(steps);
        }

        private void SetSelection(List<ICascadeNode> path)
        {
            _selectedPath.Clear();
            _selectedPath.AddRange(path);
            RecordTrail(path);
            RebuildPathWithTrail();
        }

        private void RecordTrail(IReadOnlyList<ICascadeNode> path)
        {
            for (var columnIndex = 0; columnIndex < path.Count; columnIndex++)
                _lastChild[columnIndex == 0 ? RootKey : path[columnIndex - 1]] = path[columnIndex];
        }

        /// <summary>
        /// 重算 <see cref="PathWithTrail"/>：選取路徑之後，沿著「上次選的子節點」一路往下接。
        /// </summary>
        /// <remarks>
        /// 樹是使用者的資料，不保證沒有循環（例如兩個節點互為子節點），遇到已經在路線上的節點就停下來，以免無窮迴圈。
        /// </remarks>
        private void RebuildPathWithTrail()
        {
            _pathWithTrail.Clear();
            _pathWithTrail.AddRange(_selectedPath);

            var parent = SelectedDepth == 0 ? RootKey : _selectedPath[SelectedDepth - 1];
            while (_lastChild.TryGetValue(parent, out var child) && !_pathWithTrail.Contains(child))
            {
                if (parent is ICascadeNode parentNode && !ChildrenOf(parentNode).Contains(child))
                {
                    _lastChild.Remove(parent);
                    break;
                }

                _pathWithTrail.Add(child);
                parent = child;
            }
        }
    }
}