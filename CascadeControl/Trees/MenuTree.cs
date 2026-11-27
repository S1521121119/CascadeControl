using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Trees
{
    /// <summary>
    /// 多層樹，深度不限。
    /// </summary>
    /// <remarks>
    /// 匯入匯出透過 <see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 與 <see cref="ToRows(MenuRowStyle)"/> 的二維字串陣列，
    /// 不相依任何檔案格式或 UI。程式中組樹用 <see cref="AddPath(string[])"/> 或 <see cref="ExpandAndAddPaths(string[])"/>。
    /// </remarks>
    public sealed class MenuTree
    {
        /// <summary>
        /// 樹根，不代表任何一層。
        /// </summary>
        public MenuNode Root { get; } = new MenuNode(string.Empty);

        /// <summary>
        /// 第一層節點。
        /// </summary>
        public IReadOnlyList<MenuNode> TopLevelNodes => Root.Children;

        /// <summary>
        /// 樹是否沒有任何節點。
        /// </summary>
        public bool IsEmpty => Root.Children.Count == 0;

        /// <summary>
        /// 最深那條路徑的層數（樹的高度）；空樹為 0。
        /// </summary>
        public int Height => GetLeaves().Select(leaf => leaf.Depth).DefaultIfEmpty(0).Max();

        /// <summary>
        /// 建立空樹。
        /// </summary>
        public MenuTree()
        {
        }

        /// <summary>
        /// 以指定節點作為第一層，連同其下所有子孫節點建立一棵新的選單子樹。
        /// </summary>
        /// <param name="rootNode">作為新樹起點的節點；若傳入樹根（名稱為空），則直接複製其下的所有子節點。</param>
        /// <returns>建好的選單樹。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rootNode"/> 為 null。</exception>
        public static MenuTree FromSubtree(MenuNode rootNode)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));

            var tree = new MenuTree();
            var target = string.IsNullOrEmpty(rootNode.Name)
                ? tree.Root
                : tree.Root.GetOrAddChild(rootNode.Name);
            target.MergeSubtreeFrom(rootNode);
            return tree;
        }

        /// <summary>
        /// 由資料列建樹；資料有問題只寫進警告，不丟例外。
        /// </summary>
        /// <param name="rows">
        /// 每列由左至右為第 1 層、第 2 層…，尾欄可留空；格子可用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉兄弟節點。
        /// </param>
        /// <param name="warnings">警告，重複的只留一筆。</param>
        public static MenuTree FromRows(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            return FromRows(rows, null, out warnings);
        }

        /// <inheritdoc cref="FromRows(IEnumerable{string[]}, out IList{string})"/>
        /// <param name="rows">
        /// 每列由左至右為第 1 層、第 2 層…，尾欄可留空；格子可用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉兄弟節點。
        /// </param>
        /// <param name="sourceLineNumbers">警告訊息用的行號，與 <paramref name="rows"/> 等長；null 時用 1 起算的流水號。</param>
        /// <param name="warnings">警告，重複的只留一筆。</param>
        /// <remarks>
        /// 重複路徑、葉節點再往下延伸都直接合併。中間缺層時捨棄缺口之後的部分；
        /// <see cref="CellExpansion.NAME_SEPARATOR"/> 用法錯誤或單列展開過多時跳過該列；總數超過上限時回傳空樹。
        /// 與 <see cref="ToRows(MenuRowStyle)"/> 互為反向。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="sourceLineNumbers"/> 不為 null，且長度與 <paramref name="rows"/> 不符。
        /// </exception>
        public static MenuTree FromRows(IEnumerable<string[]> rows, IReadOnlyList<int> sourceLineNumbers, out IList<string> warnings)
        {
            var tree = new MenuTree();
            var messages = new List<string>();

            if (rows == null)
            {
                warnings = messages;
                return tree;
            }

            var source = rows as IReadOnlyList<string[]> ?? rows.ToList();

            if (sourceLineNumbers != null && sourceLineNumbers.Count != source.Count)
            {
                throw new ArgumentException(
                    $"行號數量（{sourceLineNumbers.Count}）與資料列數（{source.Count}）不符。", nameof(sourceLineNumbers));
            }

            var lineNumbers = sourceLineNumbers ?? Enumerable.Range(1, source.Count).ToArray();

            if (!CellExpansion.Expand(source, lineNumbers, out List<string[]> expandedRows, out List<int> expandedLineNumbers, messages))
            {
                warnings = DropDuplicates(messages);
                return tree;
            }

            for (var index = 0; index < expandedRows.Count; index++)
            {
                var rowNumber = expandedLineNumbers[index];
                var row = expandedRows[index];

                var names = new List<string>(row.Length);
                var gapAt = -1;

                for (var level = 0; level < row.Length; level++)
                {
                    var cell = (row[level] ?? string.Empty).Trim();

                    if (cell.Length == 0)
                    {
                        // 空欄後還有資料表示中間缺層；不自動補位，捨棄後半段並警告。
                        for (var rest = level + 1; rest < row.Length; rest++)
                        {
                            if (!string.IsNullOrWhiteSpace(row[rest])) { gapAt = level; break; }
                        }
                        break;
                    }

                    names.Add(cell);
                }

                if (gapAt >= 0)
                    messages.Add($"第 {rowNumber} 列：第 {gapAt + 1} 層是空的，但後面還有資料；空欄之後的欄位已略過。");

                // AddPath 沿用既有節點，重複路徑自然合併。
                if (names.Count > 0) tree.AddPath(names);
            }

            // 同一列展開的多條路徑可能發出相同警告。
            warnings = DropDuplicates(messages);
            return tree;
        }

        /// <summary>
        /// 從第一層逐層加入路徑，已存在的節點直接沿用。
        /// </summary>
        /// <param name="nodeNames">
        /// 由淺到深的名稱，原樣比對（不 trim），不可含 <see cref="CellExpansion.NAME_SEPARATOR"/>。
        /// </param>
        /// <returns>路徑終點的節點。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="nodeNames"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="nodeNames"/> 為空，或有名稱為空白或含 <see cref="CellExpansion.NAME_SEPARATOR"/>。
        /// </exception>
        public MenuNode AddPath(params string[] nodeNames)
        {
            return AddPath((IEnumerable<string>)nodeNames);
        }

        /// <inheritdoc cref="AddPath(string[])"/>
        public MenuNode AddPath(IEnumerable<string> nodeNames)
        {
            if (nodeNames == null) throw new ArgumentNullException(nameof(nodeNames));

            var steps = nodeNames.ToList();
            if (steps.Count == 0) throw new ArgumentException("路徑至少要有一層。", nameof(nodeNames));

            // 先整條檢查完才動樹，避免留下半條路徑。
            var blankAt = steps.FindIndex(string.IsNullOrWhiteSpace);
            if (blankAt >= 0) throw new ArgumentException($"第 {blankAt + 1} 層的名稱是空的。", nameof(nodeNames));

            // 含 | 的名稱讀回時會被拆開。
            var separatorAt = steps.FindIndex(n => n.IndexOf(CellExpansion.NAME_SEPARATOR) >= 0);
            if (separatorAt >= 0)
                throw new ArgumentException(
                    $"第 {separatorAt + 1} 層的名稱 '{steps[separatorAt]}' 含有保留字元 '{CellExpansion.NAME_SEPARATOR}'；要列舉兄弟節點請改用 {nameof(ExpandAndAddPaths)}。",
                    nameof(nodeNames));

            var node = Root;
            foreach (var name in steps) node = node.GetOrAddChild(name);
            return node;
        }

        /// <summary>
        /// 加入一整列：每格可用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉兄弟節點，依笛卡兒積展開後逐條加入。
        /// </summary>
        /// <param name="cells">由淺到深的格子，例如 <c>"Motor", "Axis X|Axis Y", "Home"</c>。</param>
        /// <returns>各路徑的終點節點，依展開順序。</returns>
        /// <remarks>名稱會先 trim（<see cref="AddPath(string[])"/> 不會）；任一格不合法就整列不加。</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="cells"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="cells"/> 為空、有格子為空白、<see cref="CellExpansion.NAME_SEPARATOR"/> 用法錯誤，
        /// 或展開超過 <see cref="CellExpansion.MAX_EXPANDED_ROWS"/> 條。
        /// </exception>
        public IReadOnlyList<MenuNode> ExpandAndAddPaths(params string[] cells)
        {
            return ExpandAndAddPaths((IEnumerable<string>)cells);
        }

        /// <inheritdoc cref="ExpandAndAddPaths(string[])"/>
        public IReadOnlyList<MenuNode> ExpandAndAddPaths(IEnumerable<string> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            var steps = cells.ToList();
            if (steps.Count == 0) throw new ArgumentException("路徑至少要有一層。", nameof(cells));

            var alternatives = new List<string[]>(steps.Count);
            for (var i = 0; i < steps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(steps[i]))
                    throw new ArgumentException($"第 {i + 1} 層的名稱是空的。", nameof(cells));

                if (!CellExpansion.TrySplitNames(steps[i].Trim(), out string[] names))
                    throw new ArgumentException($"第 {i + 1} 層 '{steps[i]}' 的 '{CellExpansion.NAME_SEPARATOR}' 用法有誤（{CellExpansion.UsageHint}）。", nameof(cells));

                alternatives.Add(names);
            }

            var count = CellExpansion.CountCombinations(alternatives);
            if (count > CellExpansion.MAX_EXPANDED_ROWS)
                throw new ArgumentException($"展開後有 {count} 條路徑，超過上限 {CellExpansion.MAX_EXPANDED_ROWS}。", nameof(cells));

            return CellExpansion.Combine(alternatives).Select(path => AddPath(path)).ToList();
        }

        /// <summary>
        /// 依加入順序列舉所有葉節點。
        /// </summary>
        public IEnumerable<MenuNode> GetLeaves()
        {
            var stack = new Stack<MenuNode>();
            for (var i = Root.Children.Count - 1; i >= 0; i--) stack.Push(Root.Children[i]);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.IsLeaf)
                {
                    yield return node;
                    continue;
                }

                for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
        }

        /// <summary>
        /// 攤平成資料列：每個葉節點一列，欄數補到 <see cref="Height"/>。
        /// </summary>
        public string[][] ToRows()
        {
            return ToRows(MenuRowStyle.Expanded);
        }

        /// <summary>
        /// 攤平成資料列，欄數補到 <see cref="Height"/>。
        /// </summary>
        /// <param name="style">寫法，見 <see cref="MenuRowStyle"/>。</param>
        /// <returns>資料列；交給 <see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 會還原成同一棵樹。</returns>
        /// <remarks>
        /// 路徑數超過 <see cref="CellExpansion.MAX_EXPANDED_ROWS"/> 或名稱有前後空白時無法完整讀回；這裡不檢查。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="style"/> 不是 <see cref="MenuRowStyle.Expanded"/>，且有節點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public string[][] ToRows(MenuRowStyle style)
        {
            switch (style)
            {
                case MenuRowStyle.Expanded: return ExpandedRows();
                case MenuRowStyle.Compact: return MenuTreeRowWriter.Compact(this);
                case MenuRowStyle.Outline: return MenuTreeRowWriter.Outline(this);
                default: throw new ArgumentOutOfRangeException(nameof(style), style, "未定義的寫法。");
            }
        }

        private string[][] ExpandedRows()
        {
            var width = Math.Max(1, Height);

            return GetLeaves().Select(leaf =>
            {
                var cells = Enumerable.Repeat(string.Empty, width).ToArray();

                var parts = leaf.FullPath;
                for (var i = 0; i < parts.Count; i++) cells[i] = parts[i].Name;
                return cells;
            }).ToArray();
        }

        /// <summary>
        /// 移除重複的訊息，保留第一次出現的順序。
        /// </summary>
        private static IList<string> DropDuplicates(IEnumerable<string> messages)
        {
            return messages.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}