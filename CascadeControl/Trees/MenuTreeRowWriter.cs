using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Trees
{
    /// <summary>
    /// 把樹寫成 <see cref="MenuRowStyle.Compact"/> 與 <see cref="MenuRowStyle.Outline"/> 寫法的資料列。
    /// </summary>
    /// <remarks>
    /// 兩種寫法都用 <see cref="CellExpansion.NAME_SEPARATOR"/> 把兄弟節點合併成一格，
    /// 由 <see cref="MenuTree.FromRows(IEnumerable{string[]}, out IList{string})"/> 讀回都是同一棵樹。
    /// <see cref="MenuRowStyle.Expanded"/> 不需要合併，直接由 <see cref="MenuTree"/> 產生。
    /// </remarks>
    internal static class MenuTreeRowWriter
    {
        /// <summary>
        /// 把樹寫成壓縮後的資料列：相鄰且子樹完全相同的兄弟節點合併成同一格。
        /// </summary>
        /// <param name="tree">要寫出的樹。</param>
        /// <returns>資料列，欄數一律補到 <see cref="MenuTree.Height"/>。</returns>
        /// <remarks>
        /// 產生的是標準形，不追求最少列數：
        /// <list type="number">
        /// <item><description>
        /// 由下往上做 hash-consing：節點的「形狀」是它所有子節點編號的序列，節點編號由（名稱, 形狀）決定。
        /// 形狀相同就代表子樹完全相同（名稱、順序、結構都一樣），整體是線性時間。
        /// </description></item>
        /// <item><description>同一個父節點底下，形狀相同而且相鄰的兄弟節點合成一組，名稱依原順序串成一格。只合併相鄰的，順序才不會變。</description></item>
        /// <item><description>依深度優先順序逐組輸出，代表節點是葉節點時寫出一列，否則往下處理它的子節點組。</description></item>
        /// </list>
        /// 結果是：同一棵樹只有一種寫法、每個葉節點只出現在一列（列與列不重疊）、列序與 <see cref="MenuRowStyle.Expanded"/> 一致。
        /// 讀回時越右邊的欄位變化越快，同一組的子列又是連續輸出，所以 <see cref="MenuTree.AddPath(string[])"/>
        /// 依第一次出現排出的順序，會和原本的樹完全相同。
        /// 走訪一律用明確的堆疊而不是遞迴，因為深度沒有上限。
        /// </remarks>
        /// <exception cref="ArgumentException">樹中有節點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>。</exception>
        internal static string[][] Compact(MenuTree tree)
        {
            var width = Math.Max(1, tree.Height);
            var shapeOf = BuildShapes(tree.Root);

            var rows = new List<string[]>();
            var cells = new string[width];
            var stack = new Stack<SiblingCursor>();
            stack.Push(new SiblingCursor(tree.Root.Children, 0));

            while (stack.Count > 0)
            {
                var cursor = stack.Peek();
                var siblings = cursor.Siblings;

                if (cursor.Next >= siblings.Count) { stack.Pop(); continue; }

                // 從 Next 開始，把形狀相同的相鄰兄弟收成一組。
                var start = cursor.Next;
                var shape = shapeOf[siblings[start]];
                var end = start + 1;
                while (end < siblings.Count && shapeOf[siblings[end]] == shape) end++;
                cursor.Next = end;

                var names = new string[end - start];
                for (var i = start; i < end; i++) names[i - start] = siblings[i].Name;
                cells[cursor.Depth] = CellExpansion.JoinNames(names);

                var representative = siblings[start];
                if (representative.IsLeaf)
                {
                    var row = Enumerable.Repeat(string.Empty, width).ToArray();
                    Array.Copy(cells, row, cursor.Depth + 1);
                    rows.Add(row);
                }
                else
                {
                    // 組內每個節點的子樹都一樣，只要走代表節點那一份。
                    stack.Push(new SiblingCursor(representative.Children, cursor.Depth + 1));
                }
            }

            return rows.ToArray();
        }

        /// <summary>
        /// 把樹寫成大綱形式的資料列：每個中間節點一列，列出它底下的全部子節點，最接近手寫的寫法。
        /// </summary>
        /// <param name="tree">要寫出的樹。</param>
        /// <returns>資料列，欄數一律補到 <see cref="MenuTree.Height"/>。</returns>
        /// <remarks>
        /// 讀回時「先寫成葉節點，後來又往下延伸」會直接合併（見 <see cref="MenuTree.FromRows(IEnumerable{string[]}, out IList{string})"/>），
        /// 所以中間節點可以先出現在父節點那一列，再用自己的一列補上下一層。規則：
        /// <list type="number">
        /// <item><description>形狀的算法和 <see cref="Compact"/> 相同：形狀相同就代表子樹完全相同。</description></item>
        /// <item><description>
        /// 依深度優先順序走訪中間節點（同一組只走代表節點）：先寫出「路徑 + 全部子節點」這一列，
        /// 再把有下一層、而且形狀相同的子節點收成一組，以整組當作路徑的下一格繼續往下。
        /// 子節點的順序已經由父節點那一列決定，所以這裡不必相鄰也能合併。
        /// </description></item>
        /// <item><description>
        /// 第一層沒有父節點那一列可以決定順序，所以只合併相鄰而且形狀相同的節點；第一層的葉節點各自寫成一列。
        /// </description></item>
        /// </list>
        /// 結果是：同一棵樹只有一種寫法、每個中間節點組剛好一列，第一層相鄰的葉節點另外合成一列。
        /// 列與列會重疊（中間節點同時出現在父節點那一列和自己那一列），所以展開後的路徑數比葉節點多，
        /// 大約是節點總數；寫出前要另外檢查是否超過 <see cref="CellExpansion.MAX_EXPANDED_ROWS"/>。
        /// 走訪一律用明確的堆疊而不是遞迴，因為深度沒有上限。
        /// </remarks>
        /// <exception cref="ArgumentException">樹中有節點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>。</exception>
        internal static string[][] Outline(MenuTree tree)
        {
            var width = Math.Max(1, tree.Height);
            var shapeOf = BuildShapes(tree.Root);

            var rows = new List<string[]>();
            var stack = new Stack<OutlineGroup>();

            // 第一層：只合併相鄰的同形狀節點，順序才不會變。
            var topGroups = new List<OutlineGroup>();
            var top = tree.Root.Children;
            for (var start = 0; start < top.Count;)
            {
                var end = start + 1;
                while (end < top.Count && shapeOf[top[end]] == shapeOf[top[start]]) end++;

                var names = new string[end - start];
                for (var i = start; i < end; i++) names[i - start] = top[i].Name;
                topGroups.Add(new OutlineGroup(new[] { CellExpansion.JoinNames(names) }, top[start]));

                start = end;
            }
            PushReversed(stack, topGroups);

            while (stack.Count > 0)
            {
                var group = stack.Pop();
                var node = group.Representative;

                if (node.IsLeaf)
                {
                    // 只有第一層的葉節點會走到這裡；更深的葉節點都已經列在父節點那一列。
                    rows.Add(Pad(group.Cells, width));
                    continue;
                }

                var children = node.Children;
                var childNames = new string[children.Count];
                for (var i = 0; i < children.Count; i++) childNames[i] = children[i].Name;
                rows.Add(Pad(Append(group.Cells, CellExpansion.JoinNames(childNames)), width));

                // 有下一層的子節點依形狀分組，組的順序取第一次出現的位置。
                var members = new Dictionary<int, List<MenuNode>>();
                var order = new List<int>();
                foreach (var child in children)
                {
                    if (child.IsLeaf) continue;

                    var shape = shapeOf[child];
                    if (!members.TryGetValue(shape, out List<MenuNode> list))
                    {
                        list = new List<MenuNode>();
                        members.Add(shape, list);
                        order.Add(shape);
                    }
                    list.Add(child);
                }

                var childGroups = new List<OutlineGroup>(order.Count);
                foreach (var shape in order)
                {
                    var list = members[shape];
                    var cell = CellExpansion.JoinNames(list.Select(member => member.Name));
                    childGroups.Add(new OutlineGroup(Append(group.Cells, cell), list[0]));
                }
                PushReversed(stack, childGroups);
            }

            return rows.ToArray();
        }

        // 反向推入，彈出時才會依原順序處理。
        private static void PushReversed(Stack<OutlineGroup> stack, List<OutlineGroup> groups)
        {
            for (var i = groups.Count - 1; i >= 0; i--) stack.Push(groups[i]);
        }

        private static string[] Append(string[] cells, string cell)
        {
            var result = new string[cells.Length + 1];
            Array.Copy(cells, result, cells.Length);
            result[cells.Length] = cell;
            return result;
        }

        private static string[] Pad(string[] cells, int width)
        {
            var row = Enumerable.Repeat(string.Empty, width).ToArray();
            Array.Copy(cells, row, cells.Length);
            return row;
        }

        /// <summary>
        /// 大綱走訪的一組節點：路徑上每一層的格子，以及代表這一組往下走的節點。
        /// </summary>
        private sealed class OutlineGroup
        {
            public OutlineGroup(string[] cells, MenuNode representative)
            {
                Cells = cells;
                Representative = representative;
            }

            public string[] Cells { get; }

            public MenuNode Representative { get; }
        }

        /// <summary>
        /// 為樹根底下的每個節點算出形狀編號；形狀編號相同代表子樹完全相同。
        /// </summary>
        private static Dictionary<MenuNode, int> BuildShapes(MenuNode root)
        {
            // 先序走訪的反向就是「子節點一定排在父節點前面」，算到父節點時子節點的編號都已經有了。
            var order = new List<MenuNode>();
            var stack = new Stack<MenuNode>();
            foreach (var child in root.Children) stack.Push(child);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                order.Add(node);
                foreach (var child in node.Children) stack.Push(child);
            }

            var shapes = new Dictionary<int[], int>(SequenceComparer.Instance);
            var nodes = new Dictionary<int[], int>(SequenceComparer.Instance);
            var nameIds = new Dictionary<string, int>(StringComparer.Ordinal);

            var shapeOf = new Dictionary<MenuNode, int>(order.Count);
            var nodeIdOf = new Dictionary<MenuNode, int>(order.Count);

            for (var i = order.Count - 1; i >= 0; i--)
            {
                var node = order[i];

                var childIds = new int[node.Children.Count];
                for (var c = 0; c < childIds.Length; c++) childIds[c] = nodeIdOf[node.Children[c]];
                var shape = Intern(shapes, childIds);

                if (!nameIds.TryGetValue(node.Name, out int nameId))
                {
                    nameId = nameIds.Count;
                    nameIds.Add(node.Name, nameId);
                }

                shapeOf[node] = shape;
                nodeIdOf[node] = Intern(nodes, new[] { nameId, shape });
            }

            return shapeOf;
        }

        private static int Intern(Dictionary<int[], int> table, int[] key)
        {
            if (table.TryGetValue(key, out int id)) return id;

            id = table.Count;
            table.Add(key, id);
            return id;
        }

        /// <summary>
        /// 一層兄弟節點的走訪進度。
        /// </summary>
        private sealed class SiblingCursor
        {
            public SiblingCursor(IReadOnlyList<MenuNode> siblings, int depth)
            {
                Siblings = siblings;
                Depth = depth;
            }

            public IReadOnlyList<MenuNode> Siblings { get; }

            /// <summary>這一層在資料列中的欄位索引，由 0 起算。</summary>
            public int Depth { get; }

            /// <summary>下一個還沒輸出的兄弟節點索引。</summary>
            public int Next { get; set; }
        }

        /// <summary>
        /// 以內容比較 int 陣列，作為 hash-consing 的鍵。
        /// </summary>
        private sealed class SequenceComparer : IEqualityComparer<int[]>
        {
            public static readonly SequenceComparer Instance = new SequenceComparer();

            public bool Equals(int[] x, int[] y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null || x.Length != y.Length) return false;

                for (var i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }

            public int GetHashCode(int[] values)
            {
                unchecked
                {
                    var hash = 17;
                    foreach (var value in values) hash = (hash * 31) + value;
                    return hash;
                }
            }
        }
    }
}