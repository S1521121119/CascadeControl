using System.Text.RegularExpressions;

namespace CascadeControl.Common
{
    /// <summary>
    /// 欄位顯示文字的格式：&lt;序號&gt;名稱，有子節點時再加上 " [+]"，例如 &lt;01&gt;A [+]。
    /// </summary>
    /// <remarks>
    /// <see cref="Views.Columns.CascadeColumnsView"/> 的數字鍵跳轉靠開頭的 &lt;序號&gt; 找節點，
    /// 所以產生與解析放在一起；自己實作 <see cref="ICascadeNode"/> 時用 <see cref="Format"/> 就能支援數字鍵跳轉。
    /// </remarks>
    public static class CascadeNodeText
    {
        private static readonly Regex NumberTag = new Regex(@"^\s*<(\d+)>\s*", RegexOptions.Compiled);

        /// <summary>
        /// 組出顯示文字。
        /// </summary>
        /// <param name="number">同層序號，由 1 起算，補零到兩位。</param>
        /// <param name="name">名稱。</param>
        /// <param name="hasChildren">有子節點時加上 " [+]"。</param>
        /// <returns>顯示文字，例如 &lt;01&gt;A [+]。</returns>
        public static string Format(int number, string name, bool hasChildren)
        {
            return $"<{number:00}>{name}{(hasChildren ? " [+]" : string.Empty)}";
        }

        /// <summary>
        /// 讀出顯示文字開頭 &lt;序號&gt; 的數值（&lt;02&gt; 的數值是 2）。
        /// </summary>
        /// <param name="text">顯示文字；null 視為空字串。</param>
        /// <param name="number">序號；沒有序號時為 0。</param>
        /// <returns>開頭有 &lt;序號&gt; 時為 true。</returns>
        public static bool TryGetNumber(string text, out int number)
        {
            number = 0;
            var match = NumberTag.Match(text ?? string.Empty);
            return match.Success && int.TryParse(match.Groups[1].Value, out number);
        }

        /// <summary>
        /// 去掉開頭的 &lt;序號&gt; 與其後的空白；沒有序號時原樣回傳。
        /// </summary>
        /// <param name="text">文字；null 視為空字串。</param>
        /// <returns>去掉序號後的文字。</returns>
        public static string StripNumber(string text)
        {
            return NumberTag.Replace(text ?? string.Empty, string.Empty, 1);
        }
    }
}
