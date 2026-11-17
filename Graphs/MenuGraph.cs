using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Graphs
{
    /// <summary>
    /// 多層選單的有向圖。頂點名稱全圖唯一，同名就是同一個頂點，所以多個前驅可以共用同一個頂點（匯流）。
    /// </summary>
    /// <remarks>
    /// 採鄰接串列表示；不允許重邊，允許自我迴圈與環。
    /// 和 <see cref="MenuTree"/> 一樣只處理頂點與邊，不管資料存成什麼格式：匯入匯出一律透過
    /// <see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 與 <see cref="ToRows(MenuGraphRowStyle)"/> 的二維字串陣列，
    /// 每一列固定兩欄 <c>From,To</c>，格子內可以用 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 列舉多個頂點。
    /// 這個類別不相依 System.Windows.Forms 與檔案系統；所有走訪都用明確的佇列或堆疊，不用遞迴。
    /// </remarks>
    public sealed class MenuGraph
    {
        /// <summary>
        /// 單列展開的邊數與整份資料的總邊數上限；邊數超過這個數量的圖也無法寫出。
        /// </summary>
        /// <remarks>
        /// 與 <see cref="MultiNameCellSyntax.MAX_EXPANDED_ROWS"/> 相同。一個頂點的後繼數不會超過總邊數，
        /// 所以限制總邊數就保證顯示時任何一欄都在 ListBox 的 16 位元索引範圍內。
        /// </remarks>
        public const int MAX_EDGES = MultiNameCellSyntax.MAX_EXPANDED_ROWS;

        private readonly List<MenuVertex> _vertices = new List<MenuVertex>();
        private readonly Dictionary<string, MenuVertex> _index = new Dictionary<string, MenuVertex>(StringComparer.Ordinal);

        /// <summary>
        /// 所有頂點，依第一次出現的順序排列。
        /// </summary>
        public IReadOnlyList<MenuVertex> Vertices => _vertices;

        /// <summary>
        /// 頂點數。
        /// </summary>
        public int VertexCount => _vertices.Count;

        /// <summary>
        /// 邊數。
        /// </summary>
        public int EdgeCount { get; private set; }

        /// <summary>
        /// 圖是否沒有任何頂點。
        /// </summary>
        public bool IsEmpty => _vertices.Count == 0;

        /// <summary>
        /// 所有邊：依 <see cref="Vertices"/> 的順序，再依各頂點 <see cref="MenuVertex.Successors"/> 的順序。
        /// </summary>
        public IEnumerable<MenuEdge> Edges
        {
            get
            {
                foreach (var vertex in _vertices)
                {
                    foreach (var successor in vertex.Successors) yield return new MenuEdge(vertex, successor);
                }
            }
        }

        /// <summary>
        /// 來源頂點（入度為 0），依 <see cref="Vertices"/> 的順序；也就是選單的第一層。
        /// </summary>
        public IEnumerable<MenuVertex> Sources => _vertices.Where(vertex => vertex.IsSource);

        /// <summary>
        /// 終點（出度為 0），依 <see cref="Vertices"/> 的順序。
        /// </summary>
        public IEnumerable<MenuVertex> Sinks => _vertices.Where(vertex => vertex.IsSink);

        #region 頂點

        /// <summary>
        /// 取得指定名稱的頂點，不存在就新增一個。同一個名稱重複呼叫，拿到的都是同一個頂點。
        /// </summary>
        /// <param name="name">頂點名稱。以 <see cref="StringComparer.Ordinal"/> 原樣比對，不會 trim。</param>
        /// <returns>既有或新建的頂點。</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> 為 null、空白，或含有 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/>。</exception>
        public MenuVertex GetOrAddVertex(string name)
        {
            ValidateName(name, nameof(name));

            if (_index.TryGetValue(name, out MenuVertex existing)) return existing;

            var vertex = new MenuVertex(this, name);
            _vertices.Add(vertex);
            _index.Add(name, vertex);
            return vertex;
        }

        /// <summary>
        /// 嘗試取得指定名稱的頂點。
        /// </summary>
        /// <param name="name">頂點名稱。</param>
        /// <param name="vertex">找到的頂點；找不到時為 null。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetVertex(string name, out MenuVertex vertex)
        {
            if (name == null) { vertex = null; return false; }
            return _index.TryGetValue(name, out vertex);
        }

        /// <summary>
        /// 檢查是否已有指定名稱的頂點。
        /// </summary>
        /// <param name="name">頂點名稱。</param>
        /// <returns>已存在時為 true。</returns>
        public bool ContainsVertex(string name)
        {
            return name != null && _index.ContainsKey(name);
        }

        /// <summary>
        /// 移除頂點，連同所有相連的邊。
        /// </summary>
        /// <param name="vertex">要移除的頂點。</param>
        /// <returns>成功移除時為 true；<paramref name="vertex"/> 為 null 或不屬於本圖時為 false。</returns>
        public bool RemoveVertex(MenuVertex vertex)
        {
            if (!Owns(vertex)) return false;

            foreach (var successor in vertex.Successors.ToList()) RemoveEdge(vertex, successor);
            foreach (var predecessor in vertex.Predecessors.ToList()) RemoveEdge(predecessor, vertex);

            _vertices.Remove(vertex);
            _index.Remove(vertex.Name);
            vertex.Graph = null;
            return true;
        }

        /// <summary>
        /// <see cref="MenuVertex.Rename(string)"/> 的實作。
        /// </summary>
        internal MenuVertex RenameVertex(MenuVertex vertex, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("頂點名稱不可為空白。", nameof(newName));

            var key = newName.Trim();
            ValidateName(key, nameof(newName));

            if (string.Equals(vertex.Name, key, StringComparison.Ordinal)) return vertex;

            if (_index.TryGetValue(key, out MenuVertex existing))
            {
                // 先轉接再移除；指向自己或指向對方的邊，轉接後都會變成 existing 的自我迴圈。
                foreach (var successor in vertex.Successors.ToList())
                    AddEdge(existing, ReferenceEquals(successor, vertex) ? existing : successor);
                foreach (var predecessor in vertex.Predecessors.ToList())
                    AddEdge(ReferenceEquals(predecessor, vertex) ? existing : predecessor, existing);

                RemoveVertex(vertex);
                return existing;
            }

            _index.Remove(vertex.Name);
            vertex.Name = key;
            _index.Add(key, vertex);
            return vertex;
        }

        #endregion 頂點

        #region 邊

        /// <summary>
        /// 是否有一條邊從 <paramref name="from"/> 指向 <paramref name="to"/>。
        /// </summary>
        /// <param name="from">起點。</param>
        /// <param name="to">終點。</param>
        /// <returns>有這條邊時為 true；任一頂點為 null 或不屬於本圖時為 false。</returns>
        public bool IsAdjacent(MenuVertex from, MenuVertex to)
        {
            return Owns(from) && Owns(to) && from.HasSuccessor(to);
        }

        /// <summary>
        /// 加入一條從 <paramref name="from"/> 指向 <paramref name="to"/> 的邊；已存在時直接沿用（不會有重邊）。
        /// </summary>
        /// <param name="from">起點，必須屬於本圖。</param>
        /// <param name="to">終點，必須屬於本圖；可以和 <paramref name="from"/> 相同（自我迴圈）。</param>
        /// <returns>這條邊。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="from"/> 或 <paramref name="to"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="from"/> 或 <paramref name="to"/> 不屬於本圖。</exception>
        public MenuEdge AddEdge(MenuVertex from, MenuVertex to)
        {
            RequireOwned(from, nameof(from));
            RequireOwned(to, nameof(to));

            if (from.AddSuccessor(to)) EdgeCount++;
            return new MenuEdge(from, to);
        }

        /// <summary>
        /// 依名稱加入一條邊，頂點不存在就新增；兩個名稱先檢查完才動圖。
        /// </summary>
        /// <param name="fromName">起點名稱，原樣比對、不 trim。</param>
        /// <param name="toName">終點名稱，原樣比對、不 trim。</param>
        /// <returns>這條邊。</returns>
        /// <exception cref="ArgumentException">任一名稱為 null、空白，或含有 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/>。</exception>
        public MenuEdge AddEdge(string fromName, string toName)
        {
            ValidateName(fromName, nameof(fromName));
            ValidateName(toName, nameof(toName));

            return AddEdge(GetOrAddVertex(fromName), GetOrAddVertex(toName));
        }

        /// <summary>
        /// 移除從 <paramref name="from"/> 指向 <paramref name="to"/> 的邊；頂點本身保留。
        /// </summary>
        /// <param name="from">起點。</param>
        /// <param name="to">終點。</param>
        /// <returns>成功移除時為 true；沒有這條邊、或頂點為 null 或不屬於本圖時為 false。</returns>
        public bool RemoveEdge(MenuVertex from, MenuVertex to)
        {
            if (!Owns(from) || !Owns(to)) return false;
            if (!from.RemoveSuccessor(to)) return false;

            EdgeCount--;
            return true;
        }

        /// <summary>
        /// 加入一整列：兩格都可以用 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 列舉多個頂點，
        /// 左格的每個頂點都連向右格的每個頂點。
        /// </summary>
        /// <param name="fromCell">起點格，例如 <c>"Info|Status"</c>。</param>
        /// <param name="toCell">終點格，例如 <c>"Version|Uptime"</c>。</param>
        /// <returns>每一條邊，依展開順序排列（右格變化較快）。</returns>
        /// <remarks>
        /// 規則和讀取文字或檔案時相同，所以每個名稱都會先去除前後空白；這點和原樣比對的 <see cref="AddEdge(string, string)"/> 不同。
        /// 頂點依「先左格、再右格，各自由左至右」的順序加入。整列先全部檢查完才動圖，有任何一格不合法就整列都不加。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// 任一格為 null 或空白；<see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 用法錯誤；或展開後超過 <see cref="MAX_EDGES"/> 條邊。
        /// </exception>
        public IReadOnlyList<MenuEdge> ExpandAndAddEdges(string fromCell, string toCell)
        {
            var froms = SplitCell(fromCell, nameof(fromCell));
            var tos = SplitCell(toCell, nameof(toCell));

            var count = (long)froms.Length * tos.Length;
            if (count > MAX_EDGES)
                throw new ArgumentException($"展開後有 {count} 條邊，超過上限 {MAX_EDGES}。", nameof(toCell));

            return AddExpanded(froms, tos);
        }

        private static string[] SplitCell(string cell, string paramName)
        {
            if (string.IsNullOrWhiteSpace(cell)) throw new ArgumentException("名稱是空的。", paramName);

            if (!MultiNameCellSyntax.TrySplitNames(cell.Trim(), out string[] names))
                throw new ArgumentException($"'{cell}' 的 '{MultiNameCellSyntax.NAME_SEPARATOR}' 用法有誤（{MultiNameCellSyntax.UsageHint}）。", paramName);

            return names;
        }

        private List<MenuEdge> AddExpanded(string[] froms, string[] tos)
        {
            var fromVertices = froms.Select(GetOrAddVertex).ToList();
            var toVertices = tos.Select(GetOrAddVertex).ToList();

            var edges = new List<MenuEdge>(fromVertices.Count * toVertices.Count);
            foreach (var from in fromVertices)
            {
                foreach (var to in toVertices) edges.Add(AddEdge(from, to));
            }
            return edges;
        }

        #endregion 邊

        #region 走訪

        /// <summary>
        /// 由 <paramref name="start"/> 做廣度優先走訪，求出每個可到達頂點的最少邊數。
        /// </summary>
        /// <param name="start">起點，距離為 0。</param>
        /// <param name="maxDistance">只走到這個距離為止；超過的頂點不會出現在結果中。</param>
        /// <returns>可到達的頂點與它們的最少邊數。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="start"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="start"/> 不屬於本圖。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> 小於 0。</exception>
        public IReadOnlyDictionary<MenuVertex, int> GetDistances(MenuVertex start, int maxDistance = int.MaxValue)
        {
            RequireOwned(start, nameof(start));
            if (maxDistance < 0) throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "距離上限不可小於 0。");

            var distances = new Dictionary<MenuVertex, int> { { start, 0 } };
            var queue = new Queue<MenuVertex>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var vertex = queue.Dequeue();
                var next = distances[vertex] + 1;
                if (next > maxDistance) continue;

                foreach (var successor in vertex.Successors)
                {
                    if (distances.ContainsKey(successor)) continue;

                    distances.Add(successor, next);
                    queue.Enqueue(successor);
                }
            }

            return distances;
        }

        /// <summary>
        /// 是否無環。自我迴圈也算環。
        /// </summary>
        /// <returns>沒有任何環時為 true；空圖為 true。</returns>
        /// <remarks>Kahn 演算法：反覆拿掉入度為 0 的頂點，拿不完就代表有環。時間 O(V+E)。</remarks>
        public bool IsAcyclic()
        {
            var inDegree = _vertices.ToDictionary(vertex => vertex, vertex => vertex.InDegree);
            var ready = new Queue<MenuVertex>(_vertices.Where(vertex => vertex.InDegree == 0));
            var removed = 0;

            while (ready.Count > 0)
            {
                var vertex = ready.Dequeue();
                removed++;

                foreach (var successor in vertex.Successors)
                {
                    if (--inDegree[successor] == 0) ready.Enqueue(successor);
                }
            }

            return removed == _vertices.Count;
        }

        #endregion 走訪

        #region 資料列

        /// <summary>
        /// 由 <c>From,To</c> 資料列建立圖。遇到有問題的資料只會寫進警告，不會丟例外。
        /// </summary>
        /// <param name="rows">資料列，第 1 欄是起點、第 2 欄是終點。</param>
        /// <param name="warnings">解析時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>建好的圖。</returns>
        public static MenuGraph FromRows(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            return FromRows(rows, null, out warnings);
        }

        /// <summary>
        /// 由 <c>From,To</c> 資料列建立圖，並指定每一列在來源檔中的行號，讓警告訊息能對應回原始檔案。
        /// </summary>
        /// <param name="rows">資料列，第 1 欄是起點、第 2 欄是終點。</param>
        /// <param name="sourceLineNumbers">
        /// 與 <paramref name="rows"/> 等長的行號，警告訊息中的「第 N 列」取自這裡；傳 null 表示沒有來源行號，改用從 1 起算的流水號。
        /// </param>
        /// <param name="warnings">解析時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>建好的圖。</returns>
        /// <remarks>
        /// 規則：
        /// <list type="number">
        /// <item><description>每格先去除前後空白；兩格都可以用 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 列舉多個頂點，左格每個頂點都連向右格每個頂點。</description></item>
        /// <item><description>頂點依第一次出現的順序加入（同一列先 From 再 To）；後繼依第一次連上的順序；重複的邊直接合併，不發警告。</description></item>
        /// <item><description>To 為空的列只宣告頂點（孤立頂點的寫法）；From 為空而 To 有值的列缺少起點，跳過整列。</description></item>
        /// <item><description>第 3 欄以後不代表任何意義，有資料時略過並警告，不會解讀成路徑。</description></item>
        /// <item><description><see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 用法錯誤、或單列展開超過 <see cref="MAX_EDGES"/> 條邊時，跳過整列。</description></item>
        /// <item><description>展開後的邊數累計超過 <see cref="MAX_EDGES"/> 時放棄全部，回傳空圖。</description></item>
        /// </list>
        /// 這是 <see cref="ToRows(MenuGraphRowStyle)"/> 的反向操作，<see cref="MenuGraphRowStyle"/> 的每一種寫法都能讀回同一張圖。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="sourceLineNumbers"/> 不為 null，且長度與 <paramref name="rows"/> 不符。
        /// </exception>
        public static MenuGraph FromRows(IEnumerable<string[]> rows, IReadOnlyList<int> sourceLineNumbers, out IList<string> warnings)
        {
            var graph = new MenuGraph();
            var messages = new List<string>();

            if (rows == null)
            {
                warnings = messages;
                return graph;
            }

            var source = rows as IReadOnlyList<string[]> ?? rows.ToList();

            if (sourceLineNumbers != null && sourceLineNumbers.Count != source.Count)
            {
                throw new ArgumentException(
                    $"行號數量（{sourceLineNumbers.Count}）與資料列數（{source.Count}）不符。", nameof(sourceLineNumbers));
            }

            var total = 0L;

            for (var index = 0; index < source.Count; index++)
            {
                var lineNumber = sourceLineNumbers?[index] ?? index + 1;
                var row = source[index] ?? new string[0];

                var fromCell = CellAt(row, 0);
                var toCell = CellAt(row, 1);

                for (var column = 2; column < row.Length; column++)
                {
                    if (string.IsNullOrWhiteSpace(row[column])) continue;

                    messages.Add($"第 {lineNumber} 列：只有 From、To 兩欄，第 {column + 1} 欄之後的資料已略過。");
                    break;
                }

                if (fromCell.Length == 0)
                {
                    if (toCell.Length > 0) messages.Add($"第 {lineNumber} 列：From 是空的，整列已跳過。");
                    continue;
                }

                if (!TrySplitRowCell(fromCell, 1, lineNumber, messages, out string[] froms)) continue;

                var tos = new string[0];
                if (toCell.Length > 0 && !TrySplitRowCell(toCell, 2, lineNumber, messages, out tos)) continue;

                var count = (long)froms.Length * tos.Length;
                if (count > MAX_EDGES)
                {
                    messages.Add($"第 {lineNumber} 列：展開後有 {count} 條邊，超過上限 {MAX_EDGES}，整列已跳過。");
                    continue;
                }

                total += count;
                if (total > MAX_EDGES)
                {
                    messages.Add($"展開後的邊數總計超過上限 {MAX_EDGES}（處理到第 {lineNumber} 列），已放棄整份內容。");
                    warnings = DropDuplicates(messages);
                    return new MenuGraph();
                }

                graph.AddExpanded(froms, tos);
            }

            warnings = DropDuplicates(messages);
            return graph;
        }

        private static string CellAt(string[] row, int column)
        {
            return column < row.Length ? (row[column] ?? string.Empty).Trim() : string.Empty;
        }

        private static bool TrySplitRowCell(string cell, int column, int lineNumber, IList<string> messages, out string[] names)
        {
            if (MultiNameCellSyntax.TrySplitNames(cell, out names)) return true;

            messages.Add($"第 {lineNumber} 列：第 {column} 欄 '{cell}' 的 '{MultiNameCellSyntax.NAME_SEPARATOR}' 用法有誤"
                + $"（{MultiNameCellSyntax.UsageHint}），整列已跳過。");
            return false;
        }

        /// <summary>
        /// 攤平成 <c>From,To</c> 資料列，每條邊一列。
        /// </summary>
        /// <returns>資料列。</returns>
        public string[][] ToRows()
        {
            return ToRows(MenuGraphRowStyle.Expanded);
        }

        /// <summary>
        /// 攤平成 <c>From,To</c> 資料列，每列固定兩欄。
        /// </summary>
        /// <param name="style">寫法，見 <see cref="MenuGraphRowStyle"/>。</param>
        /// <returns>
        /// 資料列；交給 <see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 會還原成同一張圖：
        /// 頂點與邊相同、每個頂點的後繼順序相同、來源頂點的相對順序相同。
        /// 頂點整體順序與前驅順序不保證相同。
        /// </returns>
        /// <remarks>
        /// 要能完整讀回，邊數不能超過 <see cref="MAX_EDGES"/>，頂點名稱也不能有前後空白；
        /// 這兩項檢查在 <see cref="Csv.MenuGraphCsvText.Format(MenuGraph, MenuGraphRowStyle)"/> 寫出文字時才做，這裡不檢查。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="style"/> 不是 <see cref="MenuGraphRowStyle.Expanded"/>，且有頂點名稱含有 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/>。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public string[][] ToRows(MenuGraphRowStyle style)
        {
            switch (style)
            {
                case MenuGraphRowStyle.Expanded: return MenuGraphRowWriter.Expanded(this);
                case MenuGraphRowStyle.Compact: return MenuGraphRowWriter.Compact(this);
                case MenuGraphRowStyle.Outline: return MenuGraphRowWriter.Outline(this);
                default: throw new ArgumentOutOfRangeException(nameof(style), style, "未定義的寫法。");
            }
        }

        #endregion 資料列

        #region 與樹互轉

        /// <summary>
        /// 由樹建立圖：同名的節點合併成同一個頂點，每對父子節點成為一條邊。
        /// </summary>
        /// <param name="tree">來源樹。</param>
        /// <returns>建好的圖；頂點依樹的前序走訪、各節點的子節點依序加入。</returns>
        /// <remarks>
        /// 合併後的連帶效應：
        /// <list type="bullet">
        /// <item><description>
        /// 共用出邊：樹中 <c>AxisX &gt; Jog</c> 底下有子節點、<c>AxisY &gt; Jog</c> 是葉節點；轉成圖後兩者是同一個 <c>Jog</c>，
        /// 從 <c>AxisY</c> 也能走到原本只在 <c>AxisX &gt; Jog</c> 底下的項目。要讓兩者獨立，名稱必須不同。
        /// </description></item>
        /// <item><description>第一層節點不一定是來源頂點，葉節點也不一定是終點。</description></item>
        /// <item><description>可能產生環，例如 <c>A &gt; B &gt; A</c>。</description></item>
        /// </list>
        /// 每個名稱在樹中只出現一次時，<see cref="ToTree(out IList{string})"/> 會還原成原本的樹。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="tree"/> 為 null。</exception>
        /// <exception cref="ArgumentException">樹中有空白或含有 <see cref="MultiNameCellSyntax.NAME_SEPARATOR"/> 的節點名稱。</exception>
        public static MenuGraph FromTree(MenuTree tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));

            var graph = new MenuGraph();
            var stack = new Stack<MenuNode>();
            for (var i = tree.TopLevelNodes.Count - 1; i >= 0; i--) stack.Push(tree.TopLevelNodes[i]);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var vertex = graph.GetOrAddVertex(node.Name);

                foreach (var child in node.Children) graph.AddEdge(vertex, graph.GetOrAddVertex(child.Name));
                for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }

            return graph;
        }

        /// <summary>
        /// 以所有來源頂點為第一層（依 <see cref="Sources"/> 的順序），沿後繼展開成樹。
        /// </summary>
        /// <param name="warnings">展開時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>展開後的樹；圖有頂點卻沒有任何來源頂點（整張圖都在環上）時為空樹。</returns>
        /// <remarks>規則見 <see cref="ToTree(MenuVertex, out IList{string})"/>。</remarks>
        public MenuTree ToTree(out IList<string> warnings)
        {
            var messages = new List<string>();
            var sources = Sources.ToList();

            if (sources.Count == 0 && !IsEmpty)
            {
                messages.Add("圖中沒有來源頂點（每個頂點都在環上），無法決定第一層；請改用指定起點的 ToTree。");
                warnings = messages;
                return new MenuTree();
            }

            var tree = Unfold(sources, messages);
            warnings = DropDuplicates(messages);
            return tree;
        }

        /// <summary>
        /// 以 <paramref name="start"/> 為第一層，沿後繼展開成樹。共用的頂點會被複製到每個前驅底下。
        /// </summary>
        /// <param name="start">起點，必須屬於本圖。</param>
        /// <param name="warnings">展開時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>展開後的樹。</returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>迴圈（含自我迴圈）：後繼已在目前路徑上時，不展開這條邊並發出警告。</description></item>
        /// <item><description>
        /// 爆量：無環圖展開後的路徑數可能呈指數成長，葉節點總數超過 <see cref="MultiNameCellSyntax.MAX_EXPANDED_ROWS"/> 時放棄全部，回傳空樹。
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="start"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="start"/> 不屬於本圖。</exception>
        public MenuTree ToTree(MenuVertex start, out IList<string> warnings)
        {
            RequireOwned(start, nameof(start));

            var messages = new List<string>();
            var tree = Unfold(new[] { start }, messages);
            warnings = DropDuplicates(messages);
            return tree;
        }

        private static MenuTree Unfold(IEnumerable<MenuVertex> starts, List<string> messages)
        {
            var tree = new MenuTree();
            var onPath = new HashSet<MenuVertex>();
            var stack = new Stack<UnfoldFrame>();
            var leaves = 0;

            foreach (var start in starts)
            {
                stack.Push(new UnfoldFrame(start, tree.Root.GetOrAddChild(start.Name)));
                onPath.Add(start);

                while (stack.Count > 0)
                {
                    var frame = stack.Peek();
                    var successors = frame.Vertex.Successors;

                    if (frame.Next >= successors.Count)
                    {
                        stack.Pop();
                        onPath.Remove(frame.Vertex);

                        if (frame.Node.IsLeaf && ++leaves > MultiNameCellSyntax.MAX_EXPANDED_ROWS)
                        {
                            messages.Add($"展開後的路徑數超過上限 {MultiNameCellSyntax.MAX_EXPANDED_ROWS}，已放棄整份內容。");
                            return new MenuTree();
                        }
                        continue;
                    }

                    var successor = successors[frame.Next++];
                    if (onPath.Contains(successor))
                    {
                        messages.Add($"{frame.Vertex.Name} -> {successor.Name} 會形成迴圈，已略過。");
                        continue;
                    }

                    stack.Push(new UnfoldFrame(successor, frame.Node.GetOrAddChild(successor.Name)));
                    onPath.Add(successor);
                }
            }

            return tree;
        }

        /// <summary>
        /// 展開成樹時的一層走訪進度。
        /// </summary>
        private sealed class UnfoldFrame
        {
            public UnfoldFrame(MenuVertex vertex, MenuNode node)
            {
                Vertex = vertex;
                Node = node;
            }

            public MenuVertex Vertex { get; }

            public MenuNode Node { get; }

            /// <summary>下一個還沒處理的後繼索引。</summary>
            public int Next { get; set; }
        }

        #endregion 與樹互轉

        /// <summary>
        /// 為每個來源頂點建立一個 <see cref="MenuGraphNode"/>，作為 <see cref="ICascadeNode"/> 使用端（例如 CascadeView）的第一層。
        /// </summary>
        /// <returns>第一層節點，依 <see cref="Sources"/> 的順序，序號由 1 起算。</returns>
        /// <remarks>節點是建立當下的快照；圖被修改後請重新呼叫。</remarks>
        public IReadOnlyList<MenuGraphNode> CreateTopLevelNodes()
        {
            return Sources.Select((vertex, index) => new MenuGraphNode(vertex, null, index + 1)).ToList();
        }

        private bool Owns(MenuVertex vertex)
        {
            return vertex != null && ReferenceEquals(vertex.Graph, this);
        }

        private void RequireOwned(MenuVertex vertex, string paramName)
        {
            if (vertex == null) throw new ArgumentNullException(paramName);
            if (!Owns(vertex)) throw new ArgumentException($"頂點 '{vertex.Name}' 不屬於這張圖。", paramName);
        }

        private static void ValidateName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("頂點名稱不可為空白。", paramName);

            if (name.IndexOf(MultiNameCellSyntax.NAME_SEPARATOR) >= 0)
                throw new ArgumentException($"頂點名稱 '{name}' 含有保留字元 '{MultiNameCellSyntax.NAME_SEPARATOR}'。", paramName);
        }

        private static IList<string> DropDuplicates(IEnumerable<string> messages)
        {
            return messages.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}