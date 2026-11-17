using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CascadeControl.Graphs.Csv
{
    /// <summary>
    /// 圖的 CSV 檔案讀寫。每一列固定兩欄 <c>From,To</c>，代表一組有向邊；寫出時的寫法見 <see cref="MenuGraphRowStyle"/>。
    /// </summary>
    /// <remarks>
    /// 這裡只負責檔案：編碼、目錄與範本檔。文字與圖之間的轉換都交給 <see cref="MenuGraphCsvText"/>，
    /// 所以檔案內容與 <see cref="MenuGraphCsvText.Parse(string, out IList{string})"/>／<see cref="MenuGraphCsvText.Format(MenuGraph, MenuGraphRowStyle)"/> 的文字完全相同。
    /// </remarks>
    public static class MenuGraphCsvFile
    {
        // 固定使用帶 BOM 的 UTF-8，Excel 才能正確辨識編碼，不會出現亂碼。
        private static readonly Encoding FileEncoding = new UTF8Encoding(true);

        /// <summary>
        /// 由 CSV 載入圖。檔案不存在時回傳空圖並寫出一份只有表頭的範本。
        /// </summary>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="warnings">載入時發現的問題，重複的訊息只保留一筆。</param>
        /// <returns>載入的圖。發生任何錯誤都不會丟例外，而是回傳空圖。</returns>
        /// <remarks>
        /// 檔案預期以 From,To 表頭開頭；第一列不是表頭時會當成資料，並發出警告。
        /// 警告中的「第 N 列」是檔案中的實際行號。
        /// </remarks>
        public static MenuGraph Load(string filePath, out IList<string> warnings)
        {
            var messages = new List<string>();

            if (!File.Exists(filePath))
            {
                var template = new MenuGraph();
                messages.Add($"找不到圖檔，已建立空白範本：{filePath}");
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
                messages.Add($"圖檔讀取失敗：{ex.Message}");
                warnings = messages;
                return new MenuGraph();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                messages.Add($"圖檔是空的：{filePath}");
                warnings = messages;
                return new MenuGraph();
            }

            var graph = MenuGraphCsvText.Parse(text, true, out IList<string> parseWarnings);
            messages.AddRange(parseWarnings);

            warnings = messages;
            return graph;
        }

        /// <summary>
        /// 將圖寫成 CSV。目錄不存在時會自動建立，空圖只會寫出表頭。編碼為帶 BOM 的 UTF-8。
        /// </summary>
        /// <param name="graph">要寫出的圖。</param>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="style">寫法，預設為 <see cref="MenuGraphRowStyle.Expanded"/>（每條邊一列）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> 為 null。</exception>
        /// <exception cref="ArgumentException">圖的內容寫出後無法讀回同一張圖，條件見 <see cref="MenuGraphCsvText.Format(MenuGraph, MenuGraphRowStyle)"/>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> 不是已定義的值。</exception>
        public static void Save(MenuGraph graph, string filePath, MenuGraphRowStyle style = MenuGraphRowStyle.Expanded)
        {
            // 先轉成文字：內容不合法時在這裡就丟例外，寧可整個不寫，也不要留下寫到一半的檔案或空目錄。
            var text = MenuGraphCsvText.Format(graph, style);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, text, FileEncoding);
        }
    }
}