using CascadeControl.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CascadeControl.Services
{
    /// <summary>
    /// 選單樹的 CSV 檔案讀寫。每一列由左至右是第 1 層、第 2 層…，深度不足的尾欄留空；寫出時的寫法見 <see cref="MenuRowStyle"/>。
    /// </summary>
    /// <remarks>
    /// 這裡只負責檔案：編碼、目錄與範本檔。文字與樹之間的轉換都交給 <see cref="MenuTreeCsvText"/>，
    /// 所以檔案內容與 <see cref="MenuTreeCsvText.Parse(string, out IList{string})"/>／<see cref="MenuTreeCsvText.Format(MenuTree, MenuRowStyle)"/> 的文字完全相同。
    /// 深度沒有上限：讀取時 CSV 有幾欄就是幾層，寫出時依最深的路徑決定欄數。
    /// 同一格可以用 <see cref="SiblingCellSyntax.SIBLING_SEPARATOR"/> 列舉多個兄弟節點，整列會依各欄的笛卡兒積展開成多條路徑。
    /// </remarks>
    public static class MenuTreeCsvFile
    {
        // 固定使用帶 BOM 的 UTF-8，Excel 才能正確辨識編碼，不會出現亂碼。
        private static readonly Encoding FileEncoding = new UTF8Encoding(true);

        /// <summary>
        /// 由 CSV 載入選單樹。檔案不存在時回傳空樹並寫出一份只有表頭的範本。
        /// </summary>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="warnings">載入時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>載入的選單樹。發生任何錯誤都不會丟例外，而是回傳空樹。</returns>
        /// <remarks>
        /// 檔案預期以 Level1,Level2,… 的表頭開頭；第一列不是表頭時會當成資料，並發出警告。
        /// 警告中的「第 N 列」是檔案中的實際行號。
        /// </remarks>
        public static MenuTree Load(string filePath, out IList<string> warnings)
        {
            var messages = new List<string>();

            if (!File.Exists(filePath))
            {
                var template = new MenuTree();
                messages.Add($"找不到選單檔，已建立空白範本：{filePath}");
                try { Save(template, filePath); }
                catch (Exception ex) { messages.Add($"建立範本失敗：{ex.Message}"); }

                warnings = messages;
                return template;
            }

            string text;
            try
            {
                // 有 BOM 時依 BOM 判斷編碼，沒有 BOM 就當成 UTF-8。
                text = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                messages.Add($"選單檔讀取失敗：{ex.Message}");
                warnings = messages;
                return new MenuTree();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                messages.Add($"選單檔是空的：{filePath}");
                warnings = messages;
                return new MenuTree();
            }

            var tree = MenuTreeCsvText.Parse(text, true, out IList<string> parseWarnings);
            messages.AddRange(parseWarnings);

            warnings = messages;
            return tree;
        }

        /// <summary>
        /// 將選單樹寫成 CSV。目錄不存在時會自動建立，空樹只會寫出表頭。編碼為帶 BOM 的 UTF-8。
        /// </summary>
        /// <param name="tree">要寫出的選單樹。</param>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="style">
        /// 寫法，預設為 <see cref="MenuRowStyle.Expanded"/>（每個葉節點一列），規則見 <see cref="MenuTree.ToRows(MenuRowStyle)"/>。
        /// 每一種寫法由 <see cref="Load"/> 讀回都是同一棵樹。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="tree"/> 為 null。</exception>
        /// <exception cref="ArgumentException">樹的內容寫出後無法讀回同一棵樹，條件見 <see cref="MenuTreeCsvText.Format(MenuTree, MenuRowStyle)"/>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static void Save(MenuTree tree, string filePath, MenuRowStyle style = MenuRowStyle.Expanded)
        {
            // 先轉成文字：內容不合法時在這裡就丟例外，寧可整個不寫，也不要留下寫到一半的檔案或空目錄。
            var text = MenuTreeCsvText.Format(tree, style);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, text, FileEncoding);
        }
    }
}