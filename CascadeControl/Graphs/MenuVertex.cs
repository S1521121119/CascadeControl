using System;
using System.Collections.Generic;

namespace CascadeControl.Graphs
{
    /// <summary>
    /// 有向圖中的一個頂點。名稱在整張圖中唯一，前驅與後繼都依第一次連上的順序排列。
    /// </summary>
    /// <remarks>
    /// 名稱以 <see cref="StringComparer.Ordinal"/> 比對，區分大小寫，理由與 <see cref="Trees.MenuNode"/> 相同。
    /// 頂點只存結構（名稱、前驅、後繼）；深度、序號、路徑都取決於從哪條路徑走到這裡，所以不在這裡，
    /// 要顯示時改用 <see cref="MenuGraphNode"/>。頂點只能由 <see cref="MenuGraph.GetOrAddVertex(string)"/> 建立。
    /// </remarks>
    public sealed class MenuVertex
    {
        private readonly List<MenuVertex> _successors = new List<MenuVertex>();
        private readonly HashSet<MenuVertex> _successorSet = new HashSet<MenuVertex>();
        private readonly List<MenuVertex> _predecessors = new List<MenuVertex>();
        private readonly HashSet<MenuVertex> _predecessorSet = new HashSet<MenuVertex>();

        internal MenuVertex(MenuGraph graph, string name)
        {
            Graph = graph;
            Name = name;
        }

        /// <summary>
        /// 頂點名稱，在所屬的圖中唯一。要修改請用 <see cref="Rename(string)"/>。
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>
        /// 所屬的圖；被 <see cref="MenuGraph.RemoveVertex(MenuVertex)"/> 移除後為 null。
        /// </summary>
        public MenuGraph Graph { get; internal set; }

        /// <summary>
        /// 直接後繼（出邊的終點），依第一次連上的順序排列。
        /// </summary>
        public IReadOnlyList<MenuVertex> Successors => _successors;

        /// <summary>
        /// 直接前驅（入邊的起點），依第一次連上的順序排列。
        /// </summary>
        public IReadOnlyList<MenuVertex> Predecessors => _predecessors;

        /// <summary>
        /// 出度：出邊的數量。
        /// </summary>
        public int OutDegree => _successors.Count;

        /// <summary>
        /// 入度：入邊的數量。
        /// </summary>
        public int InDegree => _predecessors.Count;

        /// <summary>
        /// 是否為來源頂點（入度為 0），對應樹的第一層。
        /// </summary>
        public bool IsSource => _predecessors.Count == 0;

        /// <summary>
        /// 是否為終點（出度為 0），對應樹的葉節點。
        /// </summary>
        public bool IsSink => _successors.Count == 0;

        /// <summary>
        /// 是否為匯流頂點（入度大於 1，有多個前驅）。
        /// </summary>
        public bool IsConvergent => _predecessors.Count > 1;

        /// <summary>
        /// 是否有自我迴圈（指向自己的邊）。
        /// </summary>
        public bool HasLoop => _successorSet.Contains(this);

        /// <summary>
        /// 是否有一條邊從本頂點指向 <paramref name="vertex"/>。
        /// </summary>
        /// <param name="vertex">要檢查的頂點。</param>
        /// <returns>有這條邊時為 true；<paramref name="vertex"/> 為 null 時為 false。</returns>
        public bool HasSuccessor(MenuVertex vertex)
        {
            return vertex != null && _successorSet.Contains(vertex);
        }

        /// <summary>
        /// 是否有一條邊從 <paramref name="vertex"/> 指向本頂點。
        /// </summary>
        /// <param name="vertex">要檢查的頂點。</param>
        /// <returns>有這條邊時為 true；<paramref name="vertex"/> 為 null 時為 false。</returns>
        public bool HasPredecessor(MenuVertex vertex)
        {
            return vertex != null && _predecessorSet.Contains(vertex);
        }

        /// <summary>
        /// 修改名稱。圖中沒有同名頂點時原地改名；已有同名頂點時，把本頂點的入邊與出邊全部轉接到既有頂點
        /// （重複的邊自動合併，接在既有頂點原本的邊之後），並移除本頂點。
        /// </summary>
        /// <param name="newName">新名稱，會先去除前後空白。</param>
        /// <returns>改名後的本頂點，或合併後的既有同名頂點。</returns>
        /// <remarks>本頂點與既有頂點之間原本的邊，合併後會變成既有頂點的自我迴圈。</remarks>
        /// <exception cref="InvalidOperationException">本頂點已經從圖中移除。</exception>
        /// <exception cref="ArgumentException"><paramref name="newName"/> 為空白或含有保留字元。</exception>
        public MenuVertex Rename(string newName)
        {
            if (Graph == null) throw new InvalidOperationException("頂點已經從圖中移除，無法改名。");
            return Graph.RenameVertex(this, newName);
        }

        /// <summary>
        /// 加入一條指向 <paramref name="to"/> 的邊，兩端一起維護；已存在時不做任何事。
        /// </summary>
        /// <returns>是否新增了邊。</returns>
        internal bool AddSuccessor(MenuVertex to)
        {
            if (!_successorSet.Add(to)) return false;

            _successors.Add(to);
            to._predecessorSet.Add(this);
            to._predecessors.Add(this);
            return true;
        }

        /// <summary>
        /// 移除指向 <paramref name="to"/> 的邊，兩端一起維護。
        /// </summary>
        /// <returns>是否移除了邊。</returns>
        internal bool RemoveSuccessor(MenuVertex to)
        {
            if (!_successorSet.Remove(to)) return false;

            _successors.Remove(to);
            to._predecessorSet.Remove(this);
            to._predecessors.Remove(this);
            return true;
        }

        /// <summary>
        /// 回傳 <see cref="Name"/>，方便除錯與記錄。
        /// </summary>
        public override string ToString() => Name;
    }
}