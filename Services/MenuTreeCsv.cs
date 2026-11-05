using CascadeControl.Models;
using CascadeControl.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CascadeControl.Services
{
    /// <summary>
    /// 選單樹的 CSV 讀寫。每一列是一條通往葉節點的完整路徑，深度不足的尾欄留空。
    /// </summary>
    /// <remarks>
    /// 深度沒有上限：讀取時 CSV 有幾欄就是幾層，寫出時依最深的路徑決定欄數。
    /// 同一格可以用 <see cref="ALTERNATIVE_SEPARATOR"/> 列舉多個兄弟節點，整列會依各欄的笛卡兒積展開成多條路徑。
    /// <see cref="MenuTree"/> 只處理節點與路徑，樹和檔案之間的轉換都放在這裡，
    /// 兩邊只透過 <see cref="MenuTree.FromRows(IEnumerable{string[]}, out IList{string})"/> 與
    /// <see cref="MenuTree.ToRows"/> 的二維字串陣列交換資料。
    /// </remarks>
    public static class MenuTreeCsv
    {
        /// <summary>
        /// CSV 表頭的欄名前綴，第 n 欄的欄名是 Level{n}。
        /// </summary>
        public const string LEVEL_PREFIX = "Level";

        /// <summary>
        /// 在同一格內列舉多個兄弟節點時使用的分隔字元。節點名稱不能包含這個字元。
        /// </summary>
        public const char ALTERNATIVE_SEPARATOR = '|';

        /// <summary>
        /// 單一列展開後的路徑數上限，超過就跳過整列。這是用來擋手誤造成的組合爆炸，正常資料不會碰到。
        /// </summary>
        public const int MAX_EXPANSION_PER_ROW = 5000;

        /// <summary>
        /// 整份檔案展開後的路徑數上限，超過就整份放棄。
        /// </summary>
        public const int MAX_EXPANSION_TOTAL = 30000;

        /// <summary>
        /// 產生 <paramref name="depth"/> 欄的 CSV 表頭。
        /// </summary>
        /// <param name="depth">欄數；小於 1 時視為 1。</param>
        /// <returns>Level1 至 Level{depth}。</returns>
        public static string[] Header(int depth)
        {
            return Enumerable.Range(1, Math.Max(1, depth)).Select(n => LEVEL_PREFIX + n).ToArray();
        }

        /// <summary>
        /// 把多個兄弟節點的名稱用 <see cref="ALTERNATIVE_SEPARATOR"/> 串成一格；載入時會再展開成各自的路徑。
        /// </summary>
        /// <param name="names">同一層的兄弟節點名稱，依序排列。</param>
        /// <returns>例如 <c>Version|Uptime|Memory</c>；只有一個名稱時就是名稱本身。</returns>
        /// <remarks>
        /// 這是載入時展開的反向操作，所以檢查也照展開的規則：組出來的格子如果讀回去會被判定格式錯誤，
        /// 那一整列都會被跳過。與其等到載入時才從警告發現，不如在這裡直接丟例外。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="names"/> 為空，或其中有名稱為 null、空白，或含有 <see cref="ALTERNATIVE_SEPARATOR"/>。
        /// </exception>
        public static string JoinAlternatives(IEnumerable<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));

            var parts = names.ToList();
            if (parts.Count == 0) throw new ArgumentException("至少要有一個名稱。", nameof(names));

            for (var i = 0; i < parts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    throw new ArgumentException($"第 {i + 1} 個名稱是空的。", nameof(names));

                if (parts[i].IndexOf(ALTERNATIVE_SEPARATOR) >= 0)
                    throw new ArgumentException($"名稱含有保留字元 '{ALTERNATIVE_SEPARATOR}'：{parts[i]}", nameof(names));
            }

            return string.Join(ALTERNATIVE_SEPARATOR.ToString(), parts);
        }

        /// <summary>
        /// 由 CSV 載入選單樹。檔案不存在時回傳空樹並寫出一份只有表頭的範本。
        /// </summary>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="warnings">載入時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>載入的選單樹。發生任何錯誤都不會丟例外，而是回傳空樹。</returns>
        public static MenuTree Load(string filePath, out IList<string> warnings)
        {
            var messages = new List<string>();

            // CsvFile.ReadFile_DataWithHeader 在檔案不存在時會丟 FileNotFoundException，所以要先檢查。
            if (!File.Exists(filePath))
            {
                var template = new MenuTree();
                messages.Add($"找不到選單檔，已建立空白範本：{filePath}");
                try { Save(template, filePath); }
                catch (Exception ex) { messages.Add($"建立範本失敗：{ex.Message}"); }

                warnings = messages;
                return template;
            }

            string[] header;
            string[][] data;
            try
            {
                if (!new CsvFile().ReadFile_DataWithHeader(filePath, out header, out data))
                {
                    messages.Add($"選單檔讀取失敗：{filePath}");
                    warnings = messages;
                    return new MenuTree();
                }
            }
            catch (Exception ex)
            {
                messages.Add($"選單檔讀取失敗：{ex.Message}");
                warnings = messages;
                return new MenuTree();
            }

            var rows = new List<string[]>();
            var lineNumbers = new List<int>();

            // 行號是給警告訊息用的，讓使用者能直接找到有問題的那一行。
            // 注意：CsvFile.ParseStringToData 只收至少有一欄不是空白的列，
            // 所以 ReadFile_DataWithHeader 會直接略過整列空白的行。
            // 來源檔中間只要夾著空白行，這裡算出的行號就會比實際的小。
            // 要修正必須讓 CsvFile 一併回報原始行號，這裡不自行推算補償。
            if (header?.Length > 0 &&
                !string.Equals(header[0].Trim(), LEVEL_PREFIX + 1, StringComparison.OrdinalIgnoreCase))
            {
                // CsvFile 一律把第一列當成表頭。如果那一列其實是資料，就放回來一起解析，它的行號是 1。
                messages.Add($"選單檔第一列不是表頭（預期 '{LEVEL_PREFIX}1'，實際 '{header[0]}'），已當成資料處理。");
                rows.Add(header);
                lineNumbers.Add(1);
            }

            if (data != null)
            {
                // 表頭佔掉第 1 行，所以 data[i] 是第 i + 2 行。
                for (var i = 0; i < data.Length; i++)
                {
                    rows.Add(data[i]);
                    lineNumbers.Add(i + 2);
                }
            }

            if (!Expand(rows, lineNumbers, out List<string[]> expandedRows, out List<int> expandedLineNumbers, messages))
            {
                warnings = DropDuplicates(messages);
                return new MenuTree();
            }

            var tree = MenuTree.FromRows(expandedRows, expandedLineNumbers, out IList<string> parseWarnings);
            messages.AddRange(parseWarnings);

            // 同一列展開成多條路徑之後，FromRows 可能對每一條各發一次一模一樣的警告。
            warnings = DropDuplicates(messages);
            return tree;
        }

        /// <summary>
        /// 重新整理資料列：每一層都依第一次出現的順序分組排列。
        /// </summary>
        /// <param name="rows">
        /// 資料列，每列由左至右代表第 1 層、第 2 層…。
        /// 格子內可以用 <see cref="ALTERNATIVE_SEPARATOR"/> 列舉兄弟節點，會先展開再排序。
        /// </param>
        /// <param name="warnings">整理時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>整理後的資料列。</returns>
        /// <remarks>
        /// 排序方式是逐層依「第一次出現的順序」分組：輸入 A,B / C,X / B,Clea / A,X，
        /// 會得到 A,B / A,X / C,X / B,Clea。
        /// 另外也會順便：合併完全重複的路徑；把欄數補到最深的那一層；
        /// 刪掉原本是葉節點、後來又往下延伸的那一列
        /// （例如同時有 A,B,C 和 A,B,C,Set 時，只留下 A,B,C,Set）。
        /// 由於沒有檔案行號，警告訊息中的「第 N 列」是依輸入順序從 1 起算的流水號。
        /// </remarks>
        public static string[][] Sort(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            var messages = new List<string>();
            var source = (rows ?? Enumerable.Empty<string[]>()).ToList();
            var lineNumbers = Enumerable.Range(1, source.Count).ToList();

            if (!Expand(source, lineNumbers, out List<string[]> expandedRows, out List<int> expandedLineNumbers, messages))
            {
                warnings = DropDuplicates(messages);
                return new string[0][];
            }

            var tree = MenuTree.FromRows(expandedRows, expandedLineNumbers, out IList<string> parseWarnings);
            messages.AddRange(parseWarnings);

            warnings = DropDuplicates(messages);
            return tree.ToRows();
        }

        /// <summary>
        /// 將選單樹寫成 CSV。目錄不存在時會自動建立，空樹只會寫出表頭。
        /// </summary>
        /// <param name="menu">要寫出的選單樹。</param>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <remarks>一律寫出展開後的形式，不會再壓回 <see cref="ALTERNATIVE_SEPARATOR"/> 的寫法。</remarks>
        /// <exception cref="ArgumentException">
        /// 樹中有節點名稱含有 <see cref="ALTERNATIVE_SEPARATOR"/>；這種名稱寫出後再讀回來，會被拆成多個兄弟節點。
        /// </exception>
        public static void Save(MenuTree menu, string filePath)
        {
            // 這個檢查必須放在建立目錄與寫檔之前：寧可整個不寫，也不要留下寫到一半的檔案或空目錄。
            foreach (var node in Descendants(menu.Root))
            {
                if (node.Name.IndexOf(ALTERNATIVE_SEPARATOR) < 0) continue;

                throw new ArgumentException(
                    $"節點名稱含有保留字元 '{ALTERNATIVE_SEPARATOR}'，無法寫出：{node.FullPath}", nameof(menu));
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            new CsvFile().WriteFile_DataWithHeader(filePath, Header(menu.MaxDepth), menu.ToRows());
        }

        /// <summary>
        /// 把含有 <see cref="ALTERNATIVE_SEPARATOR"/> 的列依各欄的笛卡兒積展開成多條路徑，
        /// 每一條都沿用原始列的行號。
        /// </summary>
        /// <param name="rows">原始資料列。</param>
        /// <param name="sourceLineNumbers">與 <paramref name="rows"/> 等長的原始行號。</param>
        /// <param name="expandedRows">展開後的資料列。</param>
        /// <param name="expandedLineNumbers">展開後每一列對應的原始行號。</param>
        /// <param name="warnings">警告訊息會加到這裡。</param>
        /// <returns>展開結果是否可用；路徑總數超過 <see cref="MAX_EXPANSION_TOTAL"/> 時為 false。</returns>
        private static bool Expand(
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
                    var cell = row[column] ?? string.Empty;

                    // 不含分隔字元的格子照原樣保留，trim 交給 FromRows 處理。
                    if (cell.IndexOf(ALTERNATIVE_SEPARATOR) < 0) { alternatives.Add(new[] { cell }); continue; }

                    var parts = cell.Split(ALTERNATIVE_SEPARATOR).Select(part => part.Trim()).ToArray();

                    // 分隔字元連續（A||B）、出現在頭尾（|A、A|）、某一段只有空白（A| |B），
                    // 這三種寫法 trim 之後都會有一段是空字串，一律視為格式錯誤。
                    if (parts.Any(part => part.Length == 0)) { invalidColumn = column; break; }

                    alternatives.Add(parts);
                }

                if (invalidColumn >= 0)
                {
                    // 只跳過這一列，其餘列照常處理。
                    warnings.Add($"第 {lineNumber} 列：第 {invalidColumn + 1} 欄 '{row[invalidColumn]}' 的 '{ALTERNATIVE_SEPARATOR}' 用法有誤"
                        + "（不可連續、不可放在頭尾、每一段都不可為空），整列已跳過。");
                    continue;
                }

                var count = alternatives.Aggregate(1L, (total, options) => total * options.Length);

                if (count > MAX_EXPANSION_PER_ROW)
                {
                    warnings.Add($"第 {lineNumber} 列：展開後有 {count} 條路徑，超過單列上限 {MAX_EXPANSION_PER_ROW}，整列已跳過。");
                    continue;
                }

                if (expandedRows.Count + count > MAX_EXPANSION_TOTAL)
                {
                    warnings.Add($"展開後的路徑總數超過上限 {MAX_EXPANSION_TOTAL}（處理到第 {lineNumber} 列），已放棄整份內容。");
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
        /// 取各欄可選值的笛卡兒積。
        /// </summary>
        /// <param name="alternatives">每一欄的可選值，由左至右。</param>
        /// <returns>組合出來的資料列。</returns>
        /// <remarks>左欄是外層迴圈、右欄是內層迴圈，也就是越右邊的欄位變化越快。</remarks>
        private static IEnumerable<string[]> Combine(IReadOnlyList<string[]> alternatives)
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
        /// 依第一次加入的順序列舉樹根底下的所有節點（前序走訪）。
        /// </summary>
        /// <param name="root">起點節點，本身不包含在結果中。</param>
        /// <returns>節點序列。</returns>
        private static IEnumerable<MenuNode> Descendants(MenuNode root)
        {
            var stack = new Stack<MenuNode>();
            for (var i = root.Children.Count - 1; i >= 0; i--) stack.Push(root.Children[i]);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;

                for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
        }

        /// <summary>
        /// 移除重複的訊息，保留第一次出現的順序。
        /// </summary>
        /// <param name="messages">原始訊息。</param>
        /// <returns>移除重複後的訊息。</returns>
        private static IList<string> DropDuplicates(IEnumerable<string> messages)
        {
            return messages.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}