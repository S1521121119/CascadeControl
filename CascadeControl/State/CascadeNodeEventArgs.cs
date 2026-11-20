using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.State
{
    /// <summary>
    /// <see cref="Views.Columns.CascadeColumnsView.SelectedPathChanged"/> 與 <see cref="Views.Columns.CascadeColumnsView.NodeActivated"/> 共用的事件參數，所有內容都由選取路徑推算而來。
    /// </summary>
    /// <remarks>
    /// 只保存 <see cref="Path"/>，其他屬性都從路徑推算，不會有兩份資料不一致的問題。
    /// <see cref="ICascadeNode"/> 沒有 Parent，所以 <see cref="Parent"/> 取自路徑中的上一層。
    /// 沒有選取任何節點時，<see cref="Node"/> 為 null、<see cref="ColumnIndex"/> 為 -1，
    /// <see cref="Children"/> 則是第一欄的內容；這和「有子節點就開一欄」的規則一致，樹根的子節點就是第一欄。
    /// </remarks>
    public class CascadeNodeEventArgs : EventArgs
    {
        private static readonly IReadOnlyList<ICascadeNode> NoNodes = new ICascadeNode[0];

        private readonly IReadOnlyList<ICascadeNode> _topLevelNodes;

        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="path">由淺至深的選取路徑；null 或空集合代表未選取。</param>
        /// <param name="topLevelNodes">第一欄的內容，只有 <paramref name="path"/> 為空時才會成為 <see cref="Children"/>；可為 null。</param>
        /// <param name="pathSeparator"><see cref="PathText"/> 使用的層級分隔字串。</param>
        /// <exception cref="ArgumentException"><paramref name="path"/> 中含有 null。</exception>
        public CascadeNodeEventArgs(
            IEnumerable<ICascadeNode> path,
            IEnumerable<ICascadeNode> topLevelNodes = null,
            string pathSeparator = CascadeNavigator.DEFAULT_PATH_SEPARATOR)
        {
            var steps = path?.ToArray() ?? new ICascadeNode[0];
            if (steps.Any(step => step == null))
                throw new ArgumentException("選取路徑中不可含有 null。", nameof(path));

            Path = Array.AsReadOnly(steps);
            PathText = string.Join(pathSeparator ?? string.Empty, steps.Select(step => step.Name));
            _topLevelNodes = steps.Length == 0 && topLevelNodes != null ? Array.AsReadOnly(topLevelNodes.ToArray()) : NoNodes;
        }

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。
        /// </summary>
        public IReadOnlyList<ICascadeNode> Path { get; }

        /// <summary>
        /// 路徑上各節點名稱以層級分隔字串串接的結果；未選取時為空字串。
        /// </summary>
        public string PathText { get; }

        /// <summary>
        /// 路徑的終點，也就是目前選取的最深節點；未選取時為 null。
        /// </summary>
        public ICascadeNode Node => Path.Count > 0 ? Path[Path.Count - 1] : null;

        /// <summary>
        /// <see cref="Node"/> 所在的欄位索引，由 0 起算；未選取時為 -1。
        /// </summary>
        public int ColumnIndex => Path.Count - 1;

        /// <summary>
        /// <see cref="Node"/> 的上一層父節點；<see cref="Node"/> 位於第一欄或未選取時為 null。
        /// </summary>
        public ICascadeNode Parent => Path.Count > 1 ? Path[Path.Count - 2] : null;

        /// <summary>
        /// 下一欄會顯示的內容：<see cref="Node"/> 的子節點；未選取時是第一欄的內容。不會是 null。
        /// </summary>
        public IReadOnlyList<ICascadeNode> Children => Node == null ? _topLevelNodes : Node.Children ?? NoNodes;

        /// <summary>
        /// <see cref="Node"/> 是否為葉節點，也就是整條路徑已經選到底；未選取時為 false。
        /// </summary>
        public bool IsLeaf => Node != null && Children.Count == 0;
    }
}