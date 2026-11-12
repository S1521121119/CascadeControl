using CascadeControl.Models;
using CascadeControl.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Services
{
    /// <summary>
    /// 選單樹與純文字（CSV 格式）之間的轉換，不碰檔案。每一列由左至右是第 1 層、第 2 層…，各層以逗號分隔；寫出時的寫法見 <see cref="MenuRowStyle"/>。
    /// </summary>
    /// <remarks>
    /// 適合直接在程式裡手寫選單、從剪貼簿或其他來源匯入整段文字，或把樹轉成文字另外存放：
    /// <code>
    /// MenuTree menu = MenuTreeText.Parse(@"
    ///     System, Reboot|Shutdown
    ///     System, Info, Version|Uptime|Memory
    ///     Motor,  Axis X|Axis Y, Home
    /// ", out IList&lt;string&gt; warnings);
    /// </code>
    /// 格式與 <see cref="MenuTreeCsv"/> 讀寫的檔案相同（RFC 4180），同一格可以用 <see cref="AlternativeCell.SEPARATOR"/> 列舉兄弟節點。
    /// 表頭可有可無；空白行、每格前後的空白與縮排都會被忽略；警告中的「第 N 列」是文字中的實際行號。
    /// </remarks>
    public static class MenuTreeText
    {
        /// <summary>
        /// 表頭的欄名前綴，第 n 欄的欄名是 Level{n}。
        /// </summary>
        public const string LEVEL_PREFIX = "Level";

        /// <summary>
        /// 產生 <paramref name="depth"/> 欄的表頭。
        /// </summary>
        /// <param name="depth">欄數；小於 1 時視為 1。</param>
        /// <returns>Level1 至 Level{depth}。</returns>
        public static string[] Header(int depth)
        {
            return Enumerable.Range(1, Math.Max(1, depth)).Select(n => LEVEL_PREFIX + n).ToArray();
        }

        /// <summary>
        /// 解析文字並建立選單樹。不會丟例外，有問題的資料只會寫進警告。
        /// </summary>
        /// <param name="text">CSV 格式的文字；null 或空字串會得到空樹。</param>
        /// <param name="warnings">解析時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>建好的選單樹。</returns>
        /// <remarks>第一個非空白列的第一格是 Level1（不分大小寫）時，視為表頭並略過；否則整份都當成資料。</remarks>
        public static MenuTree Parse(string text, out IList<string> warnings)
        {
            return Parse(text, false, out warnings);
        }

        /// <summary>
        /// 解析文字並建立選單樹。
        /// </summary>
        /// <param name="text">CSV 格式的文字。</param>
        /// <param name="warnIfNoHeader">第一列不是表頭時是否發出警告；讀檔時預期一定有表頭，所以要提醒。</param>
        /// <param name="warnings">解析時發現的問題。</param>
        internal static MenuTree Parse(string text, bool warnIfNoHeader, out IList<string> warnings)
        {
            var messages = new List<string>();

            CsvFile.ParseStringToData(text, out string[][] data, out int[] lineNumbers);

            var start = 0;
            if (data.Length > 0)
            {
                if (IsHeader(data[0])) start = 1;
                else if (warnIfNoHeader)
                    messages.Add($"第一列不是表頭（預期 '{LEVEL_PREFIX}1'，實際 '{data[0][0]}'），已當成資料處理。");
            }

            var tree = MenuTree.FromRows(
                data.Skip(start).ToArray(), lineNumbers.Skip(start).ToArray(), out IList<string> parseWarnings);

            messages.AddRange(parseWarnings);
            warnings = messages;
            return tree;
        }

        /// <summary>
        /// 把選單樹轉成 CSV 格式的文字，第一列是表頭，每一列以 <see cref="Environment.NewLine"/> 結尾。
        /// </summary>
        /// <param name="menu">要轉換的選單樹。</param>
        /// <param name="style">
        /// 寫法，預設為 <see cref="MenuRowStyle.Expanded"/>（每個葉節點一列）。
        /// <see cref="MenuRowStyle.Compact"/> 例如 <c>Device,Info|Status,Version|Uptime|Memory</c>；
        /// <see cref="MenuRowStyle.Outline"/> 每個中間節點一列，最接近手寫。規則見 <see cref="MenuTree.ToRows(MenuRowStyle)"/>。
        /// </param>
        /// <returns>文字；交給 <see cref="Parse(string, out IList{string})"/> 會還原成同一棵樹。空樹只有表頭。</returns>
        /// <remarks>寫出的文字如果讀回來會變成另一棵樹，就直接丟例外，不會產生任何輸出。</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="menu"/> 為 null。</exception>
        /// <exception cref="ArgumentException">
        /// 有節點名稱含有 <see cref="AlternativeCell.SEPARATOR"/>（讀回時會被拆成多個兄弟節點）、
        /// 名稱前後有空白（讀回時會被去掉），或寫出的資料列展開後超過 <see cref="AlternativeCell.MAX_EXPANSION"/> 條路徑（讀回時會整份放棄）。
        /// 展開後的路徑數在 <see cref="MenuRowStyle.Expanded"/> 與 <see cref="MenuRowStyle.Compact"/> 等於葉節點數，
        /// 在 <see cref="MenuRowStyle.Outline"/> 會多出中間節點那幾條，所以同一棵樹可能只有 <see cref="MenuRowStyle.Outline"/> 寫不出來。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static string Format(MenuTree menu, MenuRowStyle style = MenuRowStyle.Expanded)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));

            var leaves = 0;
            foreach (var node in Descendants(menu.Root))
            {
                if (node.Name.IndexOf(AlternativeCell.SEPARATOR) >= 0)
                {
                    throw new ArgumentException(
                        $"節點名稱含有保留字元 '{AlternativeCell.SEPARATOR}'，無法寫出：{node.FullPath}", nameof(menu));
                }

                if (node.Name.Length != node.Name.Trim().Length)
                    throw new ArgumentException($"節點名稱前後有空白，讀回時會被去掉：'{node.Name}'（{node.FullPath}）", nameof(menu));

                if (node.IsLeaf) leaves++;
            }

            // 任何寫法展開後都至少有葉節點數那麼多條路徑，先擋下來，免得白白組出一大堆資料列。
            if (leaves > AlternativeCell.MAX_EXPANSION)
            {
                throw new ArgumentException(
                    $"共有 {leaves} 條路徑，超過上限 {AlternativeCell.MAX_EXPANSION}，寫出後無法讀回。", nameof(menu));
            }

            var rows = menu.ToRows(style);

            // 大綱的列與列會重疊，展開後的路徑數比葉節點多，要照實際寫出的列再算一次。
            if (style == MenuRowStyle.Outline)
            {
                var expanded = rows.Sum(row => AlternativeCell.CountCombinations(
                    row.Select(cell => cell.Split(AlternativeCell.SEPARATOR))));

                if (expanded > AlternativeCell.MAX_EXPANSION)
                {
                    throw new ArgumentException(
                        $"大綱寫法展開後有 {expanded} 條路徑（含中間節點），超過上限 {AlternativeCell.MAX_EXPANSION}，寫出後無法讀回；"
                        + $"請改用 {nameof(MenuRowStyle.Compact)}。", nameof(menu));
                }
            }

            return CsvFile.FormatDataWithHeader(Header(menu.MaxDepth), rows);
        }

        /// <summary>
        /// 重新整理資料列：每一層都依第一次出現的順序分組排列，並一律展開。
        /// </summary>
        /// <param name="rows">資料列，格子內可以用 <see cref="AlternativeCell.SEPARATOR"/> 列舉兄弟節點。</param>
        /// <param name="warnings">整理時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>整理後的資料列。</returns>
        /// <remarks>整理規則見 <see cref="Sort(IEnumerable{string[]}, MenuRowStyle, out IList{string})"/>。</remarks>
        public static string[][] Sort(IEnumerable<string[]> rows, out IList<string> warnings)
        {
            return Sort(rows, MenuRowStyle.Expanded, out warnings);
        }

        /// <summary>
        /// 重新整理資料列：每一層都依第一次出現的順序分組排列。
        /// </summary>
        /// <param name="rows">資料列，格子內可以用 <see cref="AlternativeCell.SEPARATOR"/> 列舉兄弟節點，會先展開再排序。</param>
        /// <param name="style">整理後的寫法，見 <see cref="MenuTree.ToRows(MenuRowStyle)"/>。</param>
        /// <param name="warnings">整理時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>整理後的資料列。</returns>
        /// <remarks>
        /// 排序方式是逐層依「第一次出現的順序」分組：輸入 A,B / C,X / B,Clea / A,X，
        /// 會得到 A,B / A,X / C,X / B,Clea。
        /// 另外也會順便：合併完全重複的路徑；把欄數補到最深的那一層；
        /// 刪掉原本是葉節點、後來又往下延伸的那一列
        /// （例如同時有 A,B,C 和 A,B,C,Set 時，只留下 A,B,C,Set）。
        /// 結果再整理一次不會改變。由於沒有來源行號，警告訊息中的「第 N 列」是依輸入順序從 1 起算的流水號。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="style"/> 不是 <see cref="MenuRowStyle.Expanded"/>，且有節點名稱含有 <see cref="AlternativeCell.SEPARATOR"/>。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static string[][] Sort(IEnumerable<string[]> rows, MenuRowStyle style, out IList<string> warnings)
        {
            var tree = MenuTree.FromRows(rows, out warnings);
            return tree.ToRows(style);
        }

        private static bool IsHeader(string[] row)
        {
            return row.Length > 0 && string.Equals(row[0], LEVEL_PREFIX + 1, StringComparison.OrdinalIgnoreCase);
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
    }
}