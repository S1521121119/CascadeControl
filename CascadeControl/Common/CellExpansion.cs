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
    /// 樹（<see cref="Trees.MenuTree"/>）展開後每列是一條路徑；圖（<see cref="Graphs.MenuGraph"/>）每列是一條邊。
    /// </remarks>
    public static class CellExpansion
    {
        /// <summary>
        /// 名稱分隔字元；名稱本身不可包含。
        /// </summary>
        public const char NAME_SEPARATOR = '|';

        /// <summary>
        /// 展開後的列數上限（樹的路徑數、圖的邊數）。單列超過跳過該列，整份超過則整份放棄。
        /// </summary>
        /// <remarks>
        /// Win32 ListBox 的 LB_ITEMFROMPOINT 只回傳低 16 位元索引，單欄超過 65,535 項就點不準；
        /// 任一欄項目數不超過總列數，所以限制總數即可。
        /// </remarks>
        public const int MAX_EXPANDED_ROWS = ushort.MaxValue;

        /// <summary>
        /// 把多個名稱用 <see cref="NAME_SEPARATOR"/> 串成一格，例如 <c>Version|Uptime|Memory</c>。
        /// </summary>
        /// <param name="names">名稱，依序排列。</param>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// 沒有名稱，或有名稱為空白或含 <see cref="NAME_SEPARATOR"/>（讀回時會格式錯誤）。
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
        /// 拆開一格。不含分隔字元時原樣回傳（不 trim），否則逐段 trim。
        /// </summary>
        /// <param name="cell">格子內容，不可為 null。</param>
        /// <param name="names">拆出來的名稱；格式錯誤時為 null。</param>
        /// <returns>格式是否正確；任一段 trim 後為空（如 <c>A||B</c>、<c>|A</c>）即為錯誤。</returns>
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
        /// 依各欄笛卡兒積展開樹狀資料列，展開出的列沿用原始行號。
        /// </summary>
        /// <param name="rows">原始資料列。</param>
        /// <param name="sourceLineNumbers">與 <paramref name="rows"/> 等長的原始行號。</param>
        /// <param name="expandedRows">展開後的資料列。</param>
        /// <param name="expandedLineNumbers">展開後每一列對應的原始行號。</param>
        /// <param name="warnings">警告訊息會加到這裡。</param>
        /// <returns>總數超過 <see cref="MAX_EXPANDED_ROWS"/> 時為 false。</returns>
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
        /// <remarks>越右邊的欄位變化越快。</remarks>
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