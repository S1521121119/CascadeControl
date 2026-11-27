using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Graphs.Csv
{
    /// <summary>
    /// 圖與純文字（CSV 格式）之間的轉換，不碰檔案。每一列固定兩欄 <c>From,To</c>，代表一組有向邊；寫出時的寫法見 <see cref="MenuGraphRowStyle"/>。
    /// </summary>
    /// <remarks>
    /// <code>
    /// MenuGraph graph = MenuGraphCsvText.Parse(@"
    ///     From,        To
    ///     Main,        System|Motor
    ///     System,      Info|Status
    ///     Info|Status, Version|Uptime
    /// ", out IList&lt;string&gt; warnings);
    /// </code>
    /// 格式與 <see cref="MenuGraphCsvFile"/> 讀寫的檔案相同（RFC 4180），兩格都可以用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉多個頂點。
    /// 表頭可有可無；空白行、每格前後的空白與縮排都會被忽略；警告中的「第 N 列」是文字中的實際行號。
    /// 解析規則見 <see cref="MenuGraph.FromRows(IEnumerable{string[]}, IReadOnlyList{int}, out IList{string})"/>。
    /// </remarks>
    public static class MenuGraphCsvText
    {
        /// <summary>
        /// 表頭第 1 欄（起點）的欄名。
        /// </summary>
        public const string HEADER_FROM = "From";

        /// <summary>
        /// 表頭第 2 欄（終點）的欄名。
        /// </summary>
        public const string HEADER_TO = "To";

        /// <summary>
        /// 產生表頭。
        /// </summary>
        /// <returns><c>From</c>、<c>To</c>。</returns>
        public static string[] CreateHeader()
        {
            return new[] { HEADER_FROM, HEADER_TO };
        }

        /// <summary>
        /// 解析文字並建立圖。不會丟例外，有問題的資料只會寫進警告。
        /// </summary>
        /// <param name="csvText">CSV 格式的文字；null 或空字串會得到空圖。</param>
        /// <param name="warnings">解析時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>建好的圖。</returns>
        /// <remarks>第一個非空白列的第一格是 From（不分大小寫）時，視為表頭並略過；否則整份都當成資料。</remarks>
        public static MenuGraph Parse(string csvText, out IList<string> warnings)
        {
            return Parse(csvText, false, out warnings);
        }

        /// <summary>
        /// 解析文字並建立圖。
        /// </summary>
        /// <param name="csvText">CSV 格式的文字。</param>
        /// <param name="warnIfNoHeader">第一列不是表頭時是否發出警告；讀檔時預期一定有表頭，所以要提醒。</param>
        /// <param name="warnings">解析時發現的問題。</param>
        internal static MenuGraph Parse(string csvText, bool warnIfNoHeader, out IList<string> warnings)
        {
            var messages = new List<string>();

            CsvFile.ParseRows(csvText, out string[][] data, out int[] lineNumbers);

            var start = 0;
            if (data.Length > 0)
            {
                if (IsHeader(data[0])) start = 1;
                else if (warnIfNoHeader)
                    messages.Add($"第一列不是表頭（預期 '{HEADER_FROM}'，實際 '{data[0][0]}'），已當成資料處理。");
            }

            var graph = MenuGraph.FromRows(
                data.Skip(start).ToArray(), lineNumbers.Skip(start).ToArray(), out IList<string> parseWarnings);

            messages.AddRange(parseWarnings);
            warnings = messages;
            return graph;
        }

        /// <summary>
        /// 把圖轉成 CSV 格式的文字，第一列是 <c>From,To</c> 表頭，每一列以 <see cref="Environment.NewLine"/> 結尾。
        /// </summary>
        /// <param name="graph">要轉換的圖。</param>
        /// <param name="style">寫法，預設為 <see cref="MenuGraphRowStyle.Expanded"/>（每條邊一列）。</param>
        /// <returns>
        /// 文字；交給 <see cref="Parse(string, out IList{string})"/> 會還原成同一張圖（保證範圍見 <see cref="MenuGraph.ToRows(MenuGraphRowStyle)"/>）。
        /// 空圖只有表頭。
        /// </returns>
        /// <remarks>寫出的文字如果讀回來會變成另一張圖，就直接丟例外，不會產生任何輸出。</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// 有頂點名稱含有 <see cref="CellExpansion.NAME_SEPARATOR"/>（讀回時會被拆成多個頂點）、
        /// 名稱前後有空白（讀回時會被去掉），或邊數超過 <see cref="MenuGraph.MAX_EDGES"/>（讀回時會整份放棄）。
        /// 三種寫法的列與列都不重疊，展開後的邊數一律等於 <see cref="MenuGraph.EdgeCount"/>。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static string Format(MenuGraph graph, MenuGraphRowStyle style = MenuGraphRowStyle.Expanded)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            foreach (var vertex in graph.Vertices)
            {
                if (vertex.Name.IndexOf(CellExpansion.NAME_SEPARATOR) >= 0)
                {
                    throw new ArgumentException(
                        $"頂點名稱含有保留字元 '{CellExpansion.NAME_SEPARATOR}'，無法寫出：{vertex.Name}", nameof(graph));
                }

                if (vertex.Name.Length != vertex.Name.Trim().Length)
                    throw new ArgumentException($"頂點名稱前後有空白，讀回時會被去掉：'{vertex.Name}'", nameof(graph));
            }

            if (graph.EdgeCount > MenuGraph.MAX_EDGES)
            {
                throw new ArgumentException(
                    $"共有 {graph.EdgeCount} 條邊，超過上限 {MenuGraph.MAX_EDGES}，寫出後無法讀回。", nameof(graph));
            }

            return CsvFile.FormatRowsWithHeader(CreateHeader(), graph.ToRows(style));
        }

        /// <summary>
        /// 重新整理資料列：合併重複的邊，並改寫成每條邊一列。
        /// </summary>
        /// <param name="rows"><c>From,To</c> 資料列，格子內可以用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉多個頂點。</param>
        /// <param name="warnings">整理時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>整理後的資料列。</returns>
        public static string[][] NormalizeRows(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            return NormalizeRows(rows, MenuGraphRowStyle.Expanded, out warnings);
        }

        /// <summary>
        /// 重新整理資料列：合併重複的邊，並依 <paramref name="style"/> 的列順序與寫法重寫。
        /// </summary>
        /// <param name="rows"><c>From,To</c> 資料列，格子內可以用 <see cref="CellExpansion.NAME_SEPARATOR"/> 列舉多個頂點。</param>
        /// <param name="style">整理後的寫法，見 <see cref="MenuGraphRowStyle"/>。</param>
        /// <param name="warnings">整理時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>整理後的資料列。由於沒有來源行號，警告訊息中的「第 N 列」是依輸入順序從 1 起算的流水號。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static string[][] NormalizeRows(IEnumerable<string[]> rows, MenuGraphRowStyle style, out IList<string> warnings)
        {
            return MenuGraph.FromRows(rows, out warnings).ToRows(style);
        }

        private static bool IsHeader(string[] row)
        {
            return row.Length > 0 && string.Equals(row[0], HEADER_FROM, StringComparison.OrdinalIgnoreCase);
        }
    }
}