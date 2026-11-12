using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Models
{
    /// <summary>
    /// 同一格列舉多個兄弟節點的語法：<c>Version|Uptime|Memory</c> 代表三個同層的節點。
    /// 同一列有好幾格這樣寫時，整列依各格的笛卡兒積展開成多條路徑。
    /// </summary>
    /// <remarks>
    /// 這套語法只和「資料列」有關，不相依任何檔案格式，所以放在 Models：
    /// <see cref="MenuTree.FromRows(IEnumerable{string[]}, out IList{string})"/> 讀入時展開、
    /// <see cref="MenuTree.ToRows(MenuRowStyle)"/> 寫出時可以壓回這種寫法、
    /// <see cref="MenuTree.AddPaths(string[])"/> 讓程式碼裡也能直接這樣寫。
    /// </remarks>
    public static class AlternativeCell
    {
        /// <summary>
        /// 在同一格內列舉多個兄弟節點時使用的分隔字元。節點名稱不能包含這個字元。
        /// </summary>
        public const char SEPARATOR = '|';

        /// <summary>
        /// 展開後的路徑數上限：單一列超過就跳過那一列，整份資料累計超過就整份放棄；樹的葉節點超過這個數量時也無法寫出。
        /// </summary>
        /// <remarks>
        /// 數值取 <see cref="ushort.MaxValue"/>（65,535），依據是現有架構的限制，而不是隨意挑的保護值：
        /// <list type="bullet">
        /// <item><description>
        /// UI：每一欄都是 Win32 ListBox，LB_ITEMFROMPOINT 只用低 16 位元回傳索引（ListBox.IndexFromPoint 也走這個訊息），
        /// 一欄超過 65,535 項就無法正確以滑鼠點選（實測 70,000 項時，捲到底部點第一列，得到的索引是 69978 &amp; 0xFFFF = 4442）。
        /// 任何一欄的項目數都不會超過全樹的葉節點數，所以把路徑總數限制在這裡，就保證每一欄都在範圍內。
        /// 另外每次改選都會把下一欄整批重新加入，實測 1 萬項約 60 ms、65,535 項約 0.8 秒，所以實際上單欄最好控制在數千項以內。
        /// </description></item>
        /// <item><description>
        /// 記憶體：每個 <see cref="MenuNode"/> 連同子節點清單與索引約 200 bytes，展開產生的節點數不超過路徑數的兩倍；
        /// 實測 65,535 條路徑的樹約 30 MB，建樹、寫出、讀回全部加起來約 0.5 秒，在 32 位元行程中也沒有問題。
        /// </description></item>
        /// <item><description>
        /// 單列與整份用同一個上限：<see cref="MenuRowStyle.Compact"/> 與 <see cref="MenuRowStyle.Outline"/> 的每一列展開後都不會超過整份的路徑數，
        /// 所以只要整份在上限內，寫出時就不必再拆列。
        /// </description></item>
        /// </list>
        /// </remarks>
        public const int MAX_EXPANSION = ushort.MaxValue;

        /// <summary>
        /// 把多個兄弟節點的名稱用 <see cref="SEPARATOR"/> 串成一格；讀回時會再展開成各自的路徑。
        /// </summary>
        /// <param name="names">同一層的兄弟節點名稱，依序排列。</param>
        /// <returns>例如 <c>Version|Uptime|Memory</c>；只有一個名稱時就是名稱本身。</returns>
        /// <remarks>
        /// 這是展開的反向操作，所以檢查也照展開的規則：組出來的格子如果讀回去會被判定格式錯誤，
        /// 那一整列都會被跳過。與其等到讀取時才從警告發現，不如在這裡直接丟例外。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="names"/> 為空，或其中有名稱為 null、空白，或含有 <see cref="SEPARATOR"/>。
        /// </exception>
        public static string Join(IEnumerable<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));

            var parts = names.ToList();
            if (parts.Count == 0) throw new ArgumentException("至少要有一個名稱。", nameof(names));

            for (var i = 0; i < parts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    throw new ArgumentException($"第 {i + 1} 個名稱是空的。", nameof(names));

                if (parts[i].IndexOf(SEPARATOR) >= 0)
                    throw new ArgumentException($"名稱含有保留字元 '{SEPARATOR}'：{parts[i]}", nameof(names));
            }

            return string.Join(SEPARATOR.ToString(), parts);
        }

        /// <summary>
        /// 拆開一格。不含 <see cref="SEPARATOR"/> 的格子原樣當成唯一的名稱（不 trim）；含有時拆開並逐段 trim。
        /// </summary>
        /// <param name="cell">格子內容，不可為 null。</param>
        /// <param name="names">拆出來的名稱；格式錯誤時為 null。</param>
        /// <returns>
        /// 格式是否正確。分隔字元連續（A||B）、出現在頭尾（|A、A|）、某一段只有空白（A| |B），
        /// 這三種寫法 trim 之後都會有一段是空字串，一律視為格式錯誤。
        /// </returns>
        internal static bool TrySplit(string cell, out string[] names)
        {
            if (cell.IndexOf(SEPARATOR) < 0) { names = new[] { cell }; return true; }

            var parts = cell.Split(SEPARATOR).Select(part => part.Trim()).ToArray();
            names = parts.Any(part => part.Length == 0) ? null : parts;
            return names != null;
        }

        /// <summary>
        /// 錯誤訊息共用的格式說明。
        /// </summary>
        internal static string UsageHint => $"'{SEPARATOR}' 不可連續、不可放在頭尾、每一段都不可為空";

        /// <summary>
        /// 把含有 <see cref="SEPARATOR"/> 的列依各欄的笛卡兒積展開成多條路徑，每一條都沿用原始列的行號。
        /// </summary>
        /// <param name="rows">原始資料列。</param>
        /// <param name="sourceLineNumbers">與 <paramref name="rows"/> 等長的原始行號。</param>
        /// <param name="expandedRows">展開後的資料列。</param>
        /// <param name="expandedLineNumbers">展開後每一列對應的原始行號。</param>
        /// <param name="warnings">警告訊息會加到這裡。</param>
        /// <returns>展開結果是否可用；路徑總數超過 <see cref="MAX_EXPANSION"/> 時為 false。</returns>
        /// <remarks>不含分隔字元的格子照原樣保留，空格與 trim 都交給 <see cref="MenuTree.FromRows(IEnumerable{string[]}, IReadOnlyList{int}, out IList{string})"/> 處理。</remarks>
        internal static bool Expand(
            IReadOnlyList<string[]> rows,
            IReadOnlyList<int> sourceLineNumbers,
            out List<string[]> expandedRows,
            out List<int> expandedLineNumbers,
            IList<string> warnings)
        {
            expandedRows = new List<string[]>();
            expandedLineNumbers = new List<int>();

            for (var index = 0; index < rows.Count; index++)
            {
                var lineNumber = sourceLineNumbers[index];
                var row = rows[index] ?? new string[0];

                var alternatives = new List<string[]>(row.Length);
                var invalidColumn = -1;

                for (var column = 0; column < row.Length; column++)
                {
                    if (!TrySplit(row[column] ?? string.Empty, out string[] names)) { invalidColumn = column; break; }
                    alternatives.Add(names);
                }

                if (invalidColumn >= 0)
                {
                    // 只跳過這一列，其餘列照常處理。
                    warnings.Add($"第 {lineNumber} 列：第 {invalidColumn + 1} 欄 '{row[invalidColumn]}' 的 '{SEPARATOR}' 用法有誤"
                        + $"（{UsageHint}），整列已跳過。");
                    continue;
                }

                var count = CountCombinations(alternatives);

                if (count > MAX_EXPANSION)
                {
                    warnings.Add($"第 {lineNumber} 列：展開後有 {count} 條路徑，超過上限 {MAX_EXPANSION}，整列已跳過。");
                    continue;
                }

                if (expandedRows.Count + count > MAX_EXPANSION)
                {
                    warnings.Add($"展開後的路徑總數超過上限 {MAX_EXPANSION}（處理到第 {lineNumber} 列），已放棄整份內容。");
                    expandedRows = new List<string[]>();
                    expandedLineNumbers = new List<int>();
                    return false;
                }

                foreach (var combination in Combine(alternatives))
                {
                    expandedRows.Add(combination);
                    expandedLineNumbers.Add(lineNumber);
                }
            }

            return true;
        }

        /// <summary>
        /// 各欄可選值個數的乘積，也就是展開後的路徑數。
        /// </summary>
        /// <remarks>超過 <see cref="MAX_EXPANSION"/> 之後就不再往下乘，避免很多欄時溢位。</remarks>
        internal static long CountCombinations(IEnumerable<string[]> alternatives)
        {
            var total = 1L;
            foreach (var options in alternatives)
            {
                total *= options.Length;
                if (total > MAX_EXPANSION) return total;
            }
            return total;
        }

        /// <summary>
        /// 取各欄可選值的笛卡兒積。
        /// </summary>
        /// <param name="alternatives">每一欄的可選值，由左至右。</param>
        /// <returns>組合出來的資料列。</returns>
        /// <remarks>左欄是外層迴圈、右欄是內層迴圈，也就是越右邊的欄位變化越快。</remarks>
        internal static List<string[]> Combine(IReadOnlyList<string[]> alternatives)
        {
            var result = new List<string[]> { new string[0] };

            foreach (var options in alternatives)
            {
                var next = new List<string[]>(result.Count * options.Length);

                foreach (var prefix in result)
                {
                    foreach (var option in options)
                    {
                        var row = new string[prefix.Length + 1];
                        Array.Copy(prefix, row, prefix.Length);
                        row[prefix.Length] = option;
                        next.Add(row);
                    }
                }

                result = next;
            }

            return result;
        }

        /// <summary>
        /// 把樹寫成壓縮後的資料列：相鄰且子樹完全相同的兄弟節點合併成同一格。
        /// </summary>
        /// <param name="tree">要寫出的樹。</param>
        /// <returns>資料列，欄數一律補到 <see cref="MenuTree.MaxDepth"/>。</returns>
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
        /// <exception cref="ArgumentException">樹中有節點名稱含有 <see cref="SEPARATOR"/>。</exception>
        internal static string[][] Compress(MenuTree tree)
        {
            var width = Math.Max(1, tree.MaxDepth);
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
                cells[cursor.Depth] = Join(names);

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
        /// <returns>資料列，欄數一律補到 <see cref="MenuTree.MaxDepth"/>。</returns>
        /// <remarks>
        /// 讀回時「先寫成葉節點，後來又往下延伸」會直接合併（見 <see cref="MenuTree.FromRows(IEnumerable{string[]}, out IList{string})"/>），
        /// 所以中間節點可以先出現在父節點那一列，再用自己的一列補上下一層。規則：
        /// <list type="number">
        /// <item><description>形狀的算法和 <see cref="Compress"/> 相同：形狀相同就代表子樹完全相同。</description></item>
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
        /// 大約是節點總數；寫出前要另外檢查是否超過 <see cref="MAX_EXPANSION"/>。
        /// 走訪一律用明確的堆疊而不是遞迴，因為深度沒有上限。
        /// </remarks>
        /// <exception cref="ArgumentException">樹中有節點名稱含有 <see cref="SEPARATOR"/>。</exception>
        internal static string[][] Outline(MenuTree tree)
        {
            var width = Math.Max(1, tree.MaxDepth);
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
                topGroups.Add(new OutlineGroup(new[] { Join(names) }, top[start]));

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
                rows.Add(Pad(Append(group.Cells, Join(childNames)), width));

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
                    var cell = Join(list.Select(member => member.Name));
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