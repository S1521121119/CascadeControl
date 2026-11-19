using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Common
{
    /// <summary>
    /// 同一格列舉多個名稱的語法：<c>Version|Uptime|Memory</c> 代表三個名稱。
    /// 同一列有好幾格這樣寫時，整列依各格的笛卡兒積展開成多列。
    /// </summary>
    /// <remarks>
    /// 這套語法只和「資料列」有關，不相依任何檔案格式，由樹與圖共用：
    /// <list type="bullet">
    /// <item><description>樹（<see cref="Trees.MenuTree"/>）：一格列舉同層的兄弟節點，展開後每一列是一條路徑。</description></item>
    /// <item><description>圖（<see cref="Graphs.MenuGraph"/>）：<c>From</c> 格列舉多個起點、<c>To</c> 格列舉多個終點，展開後每一列是一條邊。</description></item>
    /// </list>
    /// </remarks>
    public static class MultiNameCellSyntax
    {
        /// <summary>
        /// 在同一格內列舉多個名稱時使用的分隔字元。節點與頂點的名稱都不能包含這個字元。
        /// </summary>
        public const char NAME_SEPARATOR = '|';

        /// <summary>
        /// 展開後的資料列數上限：單一列超過就跳過那一列，整份資料累計超過就整份放棄；樹的葉節點數、圖的邊數超過這個數量時也無法寫出。
        /// </summary>
        /// <remarks>
        /// 樹的一列是一條路徑，圖的一列是一條邊。
        /// 數值取 <see cref="ushort.MaxValue"/>（65,535），依據是現有架構的限制，而不是隨意挑的保護值：
        /// <list type="bullet">
        /// <item><description>
        /// UI：每一欄都是 Win32 ListBox，LB_ITEMFROMPOINT 只用低 16 位元回傳索引（ListBox.IndexFromPoint 也走這個訊息），
        /// 一欄超過 65,535 項就無法正確以滑鼠點選（實測 70,000 項時，捲到底部點第一列，得到的索引是 69978 &amp; 0xFFFF = 4442）。
        /// 任何一欄的項目數都不會超過全樹的葉節點數（圖則是總邊數），所以把總數限制在這裡，就保證每一欄都在範圍內。
        /// 另外每次改選都會把下一欄整批重新加入，實測 1 萬項約 60 ms、65,535 項約 0.8 秒，所以實際上單欄最好控制在數千項以內。
        /// </description></item>
        /// <item><description>
        /// 記憶體：每個樹節點連同子節點清單與索引約 200 bytes，展開產生的節點數不超過路徑數的兩倍；
        /// 實測 65,535 條路徑的樹約 30 MB，建樹、寫出、讀回全部加起來約 0.5 秒，在 32 位元行程中也沒有問題。
        /// </description></item>
        /// <item><description>
        /// 單列與整份用同一個上限：壓縮或大綱寫法的每一列展開後都不會超過整份的總數，
        /// 所以只要整份在上限內，寫出時就不必再拆列。
        /// </description></item>
        /// </list>
        /// </remarks>
        public const int MAX_EXPANDED_ROWS = ushort.MaxValue;

        /// <summary>
        /// 把多個名稱用 <see cref="NAME_SEPARATOR"/> 串成一格；讀回時會再展開成各自的列。
        /// </summary>
        /// <param name="names">名稱，依序排列。</param>
        /// <returns>例如 <c>Version|Uptime|Memory</c>；只有一個名稱時就是名稱本身。</returns>
        /// <remarks>
        /// 這是展開的反向操作，所以檢查也照展開的規則：組出來的格子如果讀回去會被判定格式錯誤，
        /// 那一整列都會被跳過。與其等到讀取時才從警告發現，不如在這裡直接丟例外。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="names"/> 為空，或其中有名稱為 null、空白，或含有 <see cref="NAME_SEPARATOR"/>。
        /// </exception>
        public static string JoinNames(IEnumerable<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));

            var parts = names.ToList();
            if (parts.Count == 0) throw new ArgumentException("至少要有一個名稱。", nameof(names));

            for (var i = 0; i < parts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    throw new ArgumentException($"第 {i + 1} 個名稱是空的。", nameof(names));

                if (parts[i].IndexOf(NAME_SEPARATOR) >= 0)
                    throw new ArgumentException($"名稱含有保留字元 '{NAME_SEPARATOR}'：{parts[i]}", nameof(names));
            }

            return string.Join(NAME_SEPARATOR.ToString(), parts);
        }

        /// <summary>
        /// 拆開一格。不含 <see cref="NAME_SEPARATOR"/> 的格子原樣當成唯一的名稱（不 trim）；含有時拆開並逐段 trim。
        /// </summary>
        /// <param name="cell">格子內容，不可為 null。</param>
        /// <param name="names">拆出來的名稱；格式錯誤時為 null。</param>
        /// <returns>
        /// 格式是否正確。分隔字元連續（A||B）、出現在頭尾（|A、A|）、某一段只有空白（A| |B），
        /// 這三種寫法 trim 之後都會有一段是空字串，一律視為格式錯誤。
        /// </returns>
        internal static bool TrySplitNames(string cell, out string[] names)
        {
            if (cell.IndexOf(NAME_SEPARATOR) < 0) { names = new[] { cell }; return true; }

            var parts = cell.Split(NAME_SEPARATOR).Select(part => part.Trim()).ToArray();
            names = parts.Any(part => part.Length == 0) ? null : parts;
            return names != null;
        }

        /// <summary>
        /// 錯誤訊息共用的格式說明。
        /// </summary>
        internal static string UsageHint => $"'{NAME_SEPARATOR}' 不可連續、不可放在頭尾、每一段都不可為空";

        /// <summary>
        /// 把含有 <see cref="NAME_SEPARATOR"/> 的樹狀資料列依各欄的笛卡兒積展開成多條路徑，每一條都沿用原始列的行號。
        /// </summary>
        /// <param name="rows">原始資料列。</param>
        /// <param name="sourceLineNumbers">與 <paramref name="rows"/> 等長的原始行號。</param>
        /// <param name="expandedRows">展開後的資料列。</param>
        /// <param name="expandedLineNumbers">展開後每一列對應的原始行號。</param>
        /// <param name="warnings">警告訊息會加到這裡。</param>
        /// <returns>展開結果是否可用；路徑總數超過 <see cref="MAX_EXPANDED_ROWS"/> 時為 false。</returns>
        /// <remarks>
        /// 不含分隔字元的格子照原樣保留，空格與 trim 都交給 <see cref="Trees.MenuTree.FromRows(IEnumerable{string[]}, IReadOnlyList{int}, out IList{string})"/> 處理。
        /// 圖的資料列固定兩欄、規則不同，由 <see cref="Graphs.MenuGraph"/> 直接使用 <see cref="TrySplitNames"/> 與 <see cref="CountCombinations"/>。
        /// </remarks>
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
                    if (!TrySplitNames(row[column] ?? string.Empty, out string[] names)) { invalidColumn = column; break; }
                    alternatives.Add(names);
                }

                if (invalidColumn >= 0)
                {
                    // 只跳過這一列，其餘列照常處理。
                    warnings.Add($"第 {lineNumber} 列：第 {invalidColumn + 1} 欄 '{row[invalidColumn]}' 的 '{NAME_SEPARATOR}' 用法有誤"
                        + $"（{UsageHint}），整列已跳過。");
                    continue;
                }

                var count = CountCombinations(alternatives);

                if (count > MAX_EXPANDED_ROWS)
                {
                    warnings.Add($"第 {lineNumber} 列：展開後有 {count} 條路徑，超過上限 {MAX_EXPANDED_ROWS}，整列已跳過。");
                    continue;
                }

                if (expandedRows.Count + count > MAX_EXPANDED_ROWS)
                {
                    warnings.Add($"展開後的路徑總數超過上限 {MAX_EXPANDED_ROWS}（處理到第 {lineNumber} 列），已放棄整份內容。");
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
        /// 各欄可選值個數的乘積，也就是展開後的列數。
        /// </summary>
        /// <remarks>超過 <see cref="MAX_EXPANDED_ROWS"/> 之後就不再往下乘，避免很多欄時溢位。</remarks>
        internal static long CountCombinations(IEnumerable<string[]> alternatives)
        {
            var total = 1L;
            foreach (var options in alternatives)
            {
                total *= options.Length;
                if (total > MAX_EXPANDED_ROWS) return total;
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
    }
}