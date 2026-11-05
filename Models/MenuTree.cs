using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Models
{
    /// <summary>
    /// 多層選單樹。每一條從第一層到葉節點的路徑，就是一個可執行的項目。
    /// </summary>
    /// <remarks>
    /// 樹只處理節點與路徑，不管資料存成什麼格式：匯入匯出一律透過 <see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 與
    /// <see cref="ToRows"/> 的二維字串陣列，檔案讀寫交給 Services 層。
    /// 要在程式中直接組樹，就用 <see cref="AddPath(string[])"/> 一次加入一條路徑；<see cref="FromRows(IEnumerable{string[]}, out IList{string})"/> 內部也是逐列呼叫它。
    /// 深度沒有上限，資料有幾欄就有幾層。
    /// 這個類別不相依 System.Windows.Forms 與檔案系統，之後要搬進測試專案也不用修改。
    /// </remarks>
    public sealed class MenuTree
    {
        /// <summary>
        /// 樹根，不代表任何一層。
        /// </summary>
        public MenuNode Root { get; } = new MenuNode(string.Empty);

        /// <summary>
        /// 第一層節點，也就是 Miller Columns 最左邊那一欄的內容。
        /// </summary>
        public IReadOnlyList<MenuNode> TopLevel => Root.Children;

        /// <summary>
        /// 樹是否沒有任何節點。
        /// </summary>
        public bool IsEmpty => Root.Children.Count == 0;

        /// <summary>
        /// 最深那條路徑的層數；空樹為 0。
        /// </summary>
        public int MaxDepth => Leaves().Select(leaf => leaf.Depth).DefaultIfEmpty(0).Max();

        /// <summary>
        /// 由資料列建立選單樹。各列長度可以不同；遇到有問題的資料只會寫進警告，不會丟例外。
        /// </summary>
        /// <param name="rows">資料列，每列由左至右代表第 1 層、第 2 層…，深度不足的尾欄留空。</param>
        /// <param name="warnings">解析時發現的問題。</param>
        /// <returns>建好的選單樹。</returns>
        public static MenuTree FromRows(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            return FromRows(rows, null, out warnings);
        }

        /// <summary>
        /// 由資料列建立選單樹，並指定每一列在來源檔中的行號，讓警告訊息能對應回原始檔案。
        /// </summary>
        /// <param name="rows">資料列，每列由左至右代表第 1 層、第 2 層…，深度不足的尾欄留空。</param>
        /// <param name="sourceLineNumbers">
        /// 與 <paramref name="rows"/> 等長的行號，警告訊息中的「第 N 列」取自這裡；
        /// 傳 null 表示沒有來源行號，改用從 1 起算的流水號。
        /// </param>
        /// <param name="warnings">解析時發現的問題。</param>
        /// <returns>建好的選單樹。</returns>
        /// <remarks>
        /// 行號只用在警告訊息裡，樹本身仍然不知道資料來自哪裡，也不相依任何檔案格式。
        /// 以下三種寫法都合法，會直接合併、不發警告：重複的路徑、先寫成葉節點後來又往下延伸、
        /// 已經有下一層後來又單獨寫成葉節點。只有「中間缺一層」才算資料有問題。
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

            var source = rows as IList<string[]> ?? rows.ToList();

            if (sourceLineNumbers != null && sourceLineNumbers.Count != source.Count)
            {
                throw new ArgumentException(
                    $"行號數量（{sourceLineNumbers.Count}）與資料列數（{source.Count}）不符。", nameof(sourceLineNumbers));
            }

            for (var index = 0; index < source.Count; index++)
            {
                var rowNumber = sourceLineNumbers == null ? index + 1 : sourceLineNumbers[index];
                var row = source[index] ?? new string[0];

                var names = new List<string>(row.Length);
                var gapAt = -1;

                for (var level = 0; level < row.Length; level++)
                {
                    var cell = (row[level] ?? string.Empty).Trim();

                    if (cell.Length == 0)
                    {
                        // 空欄後面還有資料，表示這一列中間缺了一層。
                        // 不自動補位：偷偷改寫使用者的資料比丟掉還糟，所以捨棄後半段並發出警告。
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

                // 重複路徑、葉節點後來又往下延伸，AddPath 都會沿用既有節點，自然就合併了，不必另外處理。
                if (names.Count > 0) tree.AddPath(names);
            }

            warnings = messages;
            return tree;
        }

        /// <summary>
        /// 從第一層開始，依 <paramref name="names"/> 逐層往下建立節點；同名節點已存在就直接沿用。
        /// </summary>
        /// <param name="names">由淺到深的節點名稱。以 <see cref="StringComparer.Ordinal"/> 原樣比對，不會 trim。</param>
        /// <returns>路徑終點的節點。</returns>
        /// <remarks>
        /// 子節點依第一次加入的順序排列，所以呼叫的順序就是畫面上的順序。
        /// 同一條路徑加入多次不會產生重複節點；以既有的葉節點為開頭繼續往下加，它就會變成中間節點。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 為 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="names"/> 為空，或其中有 null 或空白的名稱。</exception>
        public MenuNode AddPath(params string[] names)
        {
            return AddPath((IEnumerable<string>)names);
        }

        /// <inheritdoc cref="AddPath(string[])"/>
        public MenuNode AddPath(IEnumerable<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));

            var steps = names.ToList();
            if (steps.Count == 0) throw new ArgumentException("路徑至少要有一層。", nameof(names));

            // 先整條檢查完才動樹，寧可不加也不要留下半條路徑。
            var blankAt = steps.FindIndex(string.IsNullOrWhiteSpace);
            if (blankAt >= 0) throw new ArgumentException($"第 {blankAt + 1} 層的名稱是空的。", nameof(names));

            var node = Root;
            foreach (var name in steps) node = node.GetOrAddChild(name);
            return node;
        }

        /// <summary>
        /// 依第一次加入的順序列舉所有葉節點。
        /// </summary>
        /// <returns>葉節點序列。</returns>
        public IEnumerable<MenuNode> Leaves()
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
        /// 攤平成資料列：每個葉節點一列，欄數一律補到 <see cref="MaxDepth"/>。
        /// </summary>
        /// <returns>資料列。</returns>
        public string[][] ToRows()
        {
            var width = Math.Max(1, MaxDepth);

            return Leaves().Select(leaf =>
            {
                var cells = Enumerable.Repeat(string.Empty, width).ToArray();

                var parts = leaf.PathFromRoot;
                for (var i = 0; i < parts.Count; i++) cells[i] = parts[i].Name;
                return cells;
            }).ToArray();
        }
    }
}