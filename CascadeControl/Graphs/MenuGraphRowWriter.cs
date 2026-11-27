using CascadeControl.Common;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Graphs
{
    /// <summary>
    /// 把圖寫成 <see cref="MenuGraphRowStyle"/> 三種寫法的 <c>From,To</c> 資料列。
    /// </summary>
    /// <remarks>
    /// 三種寫法的列順序相同（見 <see cref="RowOrder"/>）：
    /// 孤立頂點（沒有任何邊）寫成 <c>名稱,</c>；只有入邊的頂點不需要自己的列，它已經出現在前驅的列裡。
    /// 每個頂點的後繼都完整出現在同一列，或依序出現在連續的列，所以讀回時後繼順序不變。
    /// 列與列不重疊，展開後的邊數一律等於 <see cref="MenuGraph.EdgeCount"/>。
    /// </remarks>
    internal static class MenuGraphRowWriter
    {
        /// <summary>
        /// 每條邊一列。
        /// </summary>
        internal static string[][] Expanded(MenuGraph graph)
        {
            var rows = new List<string[]>();

            foreach (var vertex in RowOrder(graph))
            {
                if (IsIsolated(vertex)) rows.Add(new[] { vertex.Name, string.Empty });
                foreach (var successor in vertex.Successors) rows.Add(new[] { vertex.Name, successor.Name });
            }

            return rows.ToArray();
        }

        /// <summary>
        /// 每個有出邊的頂點一列，列出它的全部後繼。
        /// </summary>
        /// <exception cref="System.ArgumentException">有頂點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>。</exception>
        internal static string[][] Outline(MenuGraph graph)
        {
            var rows = new List<string[]>();

            foreach (var vertex in RowOrder(graph))
            {
                if (IsIsolated(vertex)) rows.Add(new[] { vertex.Name, string.Empty });
                else if (vertex.OutDegree > 0) rows.Add(new[] { vertex.Name, JoinSuccessors(vertex) });
            }

            return rows.ToArray();
        }

        /// <summary>
        /// 在 <see cref="Outline"/> 的基礎上，把後繼序列完全相同的頂點合成同一格。
        /// </summary>
        /// <remarks>
        /// 只合併從同一個起點走訪到的頂點（見 <see cref="RowOrder"/>），組不必相鄰，組內依走訪順序排列，整組在第一個成員被走訪到時寫出：
        /// <list type="bullet">
        /// <item><description>每個頂點的後繼都在同一列，合併不會改變任何頂點的後繼順序。</description></item>
        /// <item><description>
        /// 每個來源頂點都是自己的起點，所以來源頂點不會被併進別的來源頂點那一列，讀回時來源頂點的順序不變；
        /// 孤立頂點也是來源頂點，因此各自一列。
        /// </description></item>
        /// <item><description>
        /// 不跨起點合併，讀回後走訪順序與分組都相同，所以結果再整理一次不會改變。
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="System.ArgumentException">有頂點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>。</exception>
        internal static string[][] Compact(MenuGraph graph)
        {
            var order = Traverse(graph, out Dictionary<MenuVertex, int> startOf);

            // 分組鍵：起點編號 + 後繼序列（以頂點在 Vertices 中的索引表示）。
            var indexOf = new Dictionary<MenuVertex, int>(graph.VertexCount);
            for (var i = 0; i < graph.Vertices.Count; i++) indexOf.Add(graph.Vertices[i], i);

            var groups = new Dictionary<string, List<MenuVertex>>();
            var groupOf = new Dictionary<MenuVertex, List<MenuVertex>>();

            foreach (var vertex in order)
            {
                if (!IsIsolated(vertex) && vertex.OutDegree == 0) continue;

                var key = startOf[vertex] + ":" + string.Join(",", vertex.Successors.Select(successor => indexOf[successor]));
                if (!groups.TryGetValue(key, out List<MenuVertex> members))
                {
                    members = new List<MenuVertex>();
                    groups.Add(key, members);
                }
                members.Add(vertex);
                groupOf.Add(vertex, members);
            }

            var rows = new List<string[]>();
            var written = new HashSet<List<MenuVertex>>();

            foreach (var vertex in order)
            {
                if (!groupOf.TryGetValue(vertex, out List<MenuVertex> members) || !written.Add(members)) continue;

                var fromCell = CellExpansion.JoinNames(members.Select(member => member.Name));
                rows.Add(new[] { fromCell, vertex.OutDegree > 0 ? JoinSuccessors(vertex) : string.Empty });
            }

            return rows.ToArray();
        }

        /// <summary>
        /// 列的順序：依序以每個 <see cref="MenuGraph.Sources"/> 為起點，沿後繼做深度優先前序走訪；
        /// 之後若還有沒走到的頂點（只存在於環上），依 <see cref="MenuGraph.Vertices"/> 順序挑第一個沒走到的頂點當成新的起點繼續走訪。
        /// </summary>
        internal static List<MenuVertex> RowOrder(MenuGraph graph)
        {
            return Traverse(graph, out _);
        }

        /// <summary>
        /// <see cref="RowOrder"/> 的實作，另外回報每個頂點是從第幾個起點走訪到的。
        /// </summary>
        /// <param name="graph">要走訪的圖。</param>
        /// <param name="startOf">每個頂點是從第幾個起點走訪到的。</param>
        private static List<MenuVertex> Traverse(MenuGraph graph, out Dictionary<MenuVertex, int> startOf)
        {
            var order = new List<MenuVertex>(graph.VertexCount);
            startOf = new Dictionary<MenuVertex, int>(graph.VertexCount);
            var stack = new Stack<MenuVertex>();
            var startIndex = 0;

            foreach (var start in graph.Sources.Concat(graph.Vertices))
            {
                if (startOf.ContainsKey(start)) continue;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    var vertex = stack.Pop();
                    if (startOf.ContainsKey(vertex)) continue;

                    startOf.Add(vertex, startIndex);
                    order.Add(vertex);
                    for (var i = vertex.Successors.Count - 1; i >= 0; i--)
                    {
                        if (!startOf.ContainsKey(vertex.Successors[i])) stack.Push(vertex.Successors[i]);
                    }
                }

                startIndex++;
            }

            return order;
        }

        private static bool IsIsolated(MenuVertex vertex) => vertex.InDegree == 0 && vertex.OutDegree == 0;

        private static string JoinSuccessors(MenuVertex vertex)
        {
            return CellExpansion.JoinNames(vertex.Successors.Select(successor => successor.Name));
        }
    }
}