using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// 檢視控制項複製、剪下、貼上節點時用的 Windows 剪貼簿格式。
    /// </summary>
    public static class CascadeClipboard
    {
        /// <summary>
        /// 複製節點時放到 Windows 剪貼簿上的自訂格式名稱；內容是 UTF-8 的 CSV 文字（<see cref="MenuRowStyle.Expanded"/>，含表頭）。
        /// </summary>
        /// <remarks>
        /// 同時也會放一份純文字，可以直接貼到記事本或 Excel；但貼回控制項時只認這個格式，
        /// 剪貼簿上隨便一段文字不會被當成節點貼進來。
        /// </remarks>
        public const string CLIPBOARD_FORMAT = "CascadeControl.MenuNodes";

        private const int RETRY_TIMES = 5;
        private const int RETRY_DELAY = 100;

        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        // 每個節點各自當成第一層，連同子孫以 CSV 放到剪貼簿；讀回時第一層的順序就是複製時的順序。
        // 沒有對象、子樹大到寫出去讀不回來，或剪貼簿被其他程式佔用時為 false。
        internal static bool CopyNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return false;

            string text;
            try
            {
                var tree = new MenuTree();
                foreach (var node in nodes) tree.Root.InsertChildAt(tree.Root.Children.Count, node);

                text = MenuTreeCsvText.Format(tree, MenuRowStyle.Expanded);
            }
            catch (ArgumentException)
            {
                // 子樹大到展開後超過路徑上限，寫出去讀不回來。
                return false;
            }

            var data = new DataObject();
            data.SetData(CLIPBOARD_FORMAT, new MemoryStream(Encoding.UTF8.GetBytes(text)));
            data.SetText(text);

            return SetClipboard(data);
        }

        // 每個節點從第一層到它的路徑寫成一列 CSV 純文字（不含子孫），順序與傳入時相同。
        // 只放純文字，不放 CLIPBOARD_FORMAT：內容含上層路徑，不是要貼回控制項的節點。
        internal static bool CopyNodePaths(IReadOnlyList<MenuNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return false;

            string text;
            try
            {
                var tree = new MenuTree();
                foreach (var node in nodes) tree.AddPath(node.FullPath.Select(step => step.Name));

                text = MenuTreeCsvText.Format(tree, MenuRowStyle.Expanded);
            }
            catch (ArgumentException)
            {
                // 名稱含保留字元或前後有空白，寫出去讀不回來。
                return false;
            }

            return SetClipboard(new DataObject(DataFormats.UnicodeText, text));
        }

        // 剪貼簿上的節點（第一層，依複製時的順序）；沒有本格式或讀不到時為空集合。
        internal static IReadOnlyList<MenuNode> ReadNodes()
        {
            for (var attempt = 1; ContainsNodes(); attempt++)
            {
                var text = ReadText();
                if (text != null) return MenuTreeCsvText.Parse(text, out _).TopLevelNodes.ToList();
                if (attempt == RETRY_TIMES) break;

                Thread.Sleep(RETRY_DELAY);
            }

            return NoMenuNodes;
        }

        internal static bool ContainsNodes()
        {
            try
            {
                return Clipboard.ContainsData(CLIPBOARD_FORMAT);
            }
            catch (Exception ex) when (ex is ExternalException || ex is ThreadStateException)
            {
                return false;
            }
        }

        // 本格式的 CSV 文字；讀不到，或內容不是 CopyNodes 寫的 CSV 時為 null。
        // 剛寫入後立刻讀，OLE 偶爾回傳 null 或同樣長度的 0，剪貼簿上的資料其實正確，再讀一次就對了。
        private static string ReadText()
        {
            string text;
            try
            {
                // 存的是 MemoryStream，取回來也是原始位元組，不經過 BinaryFormatter。
                if (!(Clipboard.GetData(CLIPBOARD_FORMAT) is MemoryStream stream)) return null;
                text = Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex) when (ex is ExternalException || ex is ThreadStateException)
            {
                return null;
            }

            // CopyNodes 寫的 CSV 一定以表頭開頭，名稱也不會含 NUL。
            var valid = text.StartsWith(MenuTreeCsvText.HEADER_COLUMN_PREFIX + 1, StringComparison.Ordinal) && text.IndexOf('\0') < 0;
            return valid ? text : null;
        }

        private static bool SetClipboard(DataObject data)
        {
            try
            {
                Clipboard.SetDataObject(data, true, RETRY_TIMES, RETRY_DELAY);
                return true;
            }
            catch (Exception ex) when (ex is ExternalException || ex is ThreadStateException)
            {
                return false;
            }
        }
    }
}
