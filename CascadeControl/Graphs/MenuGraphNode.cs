using CascadeControl.Common;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Graphs
{
    /// <summary>
    /// 沿著某條路徑走到的頂點，讓圖可以交給 <see cref="ICascadeNode"/> 的使用端（例如 CascadeColumnsView）逐欄顯示。
    /// </summary>
    /// <remarks>
    /// 同一個頂點從不同前驅走進來，會是不同的 <see cref="MenuGraphNode"/>，所以序號、深度、路徑都有明確的值。
    /// 子節點在第一次讀取 <see cref="Children"/> 時才建立並快取，因此有環的圖也能一路往下展開，不會無窮遞迴。
    /// 節點是建立當下的快照：圖被修改後，已建立的子節點不會自動更新，請重新呼叫 <see cref="MenuGraph.CreateTopLevelNodes"/>。
    /// </remarks>
    public sealed class MenuGraphNode : ICascadeNode
    {
        private IReadOnlyList<MenuGraphNode> _children;
        private string _displayText;

        internal MenuGraphNode(MenuVertex vertex, MenuGraphNode parent, int siblingNumber)
        {
            Vertex = vertex;
            Parent = parent;
            SiblingNumber = siblingNumber;
            Depth = parent == null ? 1 : parent.Depth + 1;

            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor.Vertex, vertex)) { IsRepeatedOnPath = true; break; }
            }
        }

        /// <summary>
        /// 對應的頂點。
        /// </summary>
        public MenuVertex Vertex { get; }

        /// <summary>
        /// 頂點名稱。
        /// </summary>
        public string Name => Vertex.Name;

        /// <summary>
        /// 路徑上的前一個節點；第一層為 null。
        /// </summary>
        public MenuGraphNode Parent { get; }

        /// <summary>
        /// 深度。第一層為 1，與 <see cref="Trees.MenuNode.Depth"/> 相同。
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// 顯示序號，由 1 起算：第一層依來源頂點的順序，其餘依它在前一個頂點 <see cref="MenuVertex.Successors"/> 中的位置。
        /// </summary>
        public int SiblingNumber { get; }

        /// <summary>
        /// 是否為葉節點（頂點沒有後繼）。
        /// </summary>
        public bool IsLeaf => Vertex.IsSink;

        /// <summary>
        /// 本頂點是否已經出現在祖先路徑上，也就是走進了環。呼叫端可以據此決定是否繼續展開。
        /// </summary>
        public bool IsRepeatedOnPath { get; }

        /// <summary>
        /// 顯示文字，格式與 <see cref="Trees.MenuNode.DisplayText"/> 相同：&lt;序號&gt;名稱，有後繼時再加上 " [+]"。
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (_displayText == null)
                    _displayText = $"<{SiblingNumber:00}>{Name}{(Vertex.OutDegree > 0 ? " [+]" : string.Empty)}";
                return _displayText;
            }
        }

        /// <summary>
        /// 子節點，每個後繼一個，依 <see cref="MenuVertex.Successors"/> 的順序。第一次讀取時才建立。
        /// </summary>
        public IReadOnlyList<MenuGraphNode> Children
        {
            get
            {
                if (_children == null)
                    _children = Vertex.Successors.Select((successor, index) => new MenuGraphNode(successor, this, index + 1)).ToList();
                return _children;
            }
        }

        /// <summary>
        /// 從第一層到本節點經過的所有節點，由淺到深排列。
        /// </summary>
        public IReadOnlyList<MenuGraphNode> FullPath
        {
            get
            {
                var path = new List<MenuGraphNode>(Depth);
                for (var node = this; node != null; node = node.Parent) path.Add(node);
                path.Reverse();
                return path;
            }
        }

        /// <summary>
        /// 從第一層到本節點的路徑字串，只含名稱，例如 Main &gt; Motor &gt; AxisX &gt; Jog。
        /// </summary>
        public string FullPathText => string.Join(" > ", FullPath.Select(node => node.Name));

        IReadOnlyList<ICascadeNode> ICascadeNode.Children => Children;

        /// <summary>
        /// 回傳 <see cref="Name"/>，方便除錯與記錄。
        /// </summary>
        public override string ToString() => Name;
    }
}