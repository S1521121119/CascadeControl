//=============================================================================================================================
//
//                                                  EDIT HISTORY FOR MODULE
//
//  This section contains comments describing changes made to the module.
//  Notice that changes are listed in chronological order.
//
//  when            ver             who                     what, where, why
//  ----------      ----------      ----------------        -------------------------------------------------------------------
//  2023-11-24      0.00.001        Jeff.WK.Lin             Create
//  2024-06-12      0.00.002        Jeff.WK.Lin             Change namespace to Aggregate.UtilityFiles
//  2024-06-17      0.00.003        Jeff.WK.Lin             Upgrade to .net standard 2.0
//  2024-07-10      0.00.004        Jeff.WK.Lin             1. Rename SaveFile to WriteFile
//                                                          2. Remove SaveFile (string file_path, string[,] data)
//                                                          3. Add WriteFile_DataWithHeader
//  2024-07-11      0.00.005        Jeff.WK.Lin             Add AppendData(string filePath, string[][] data) method
//  2024-07-22      0.00.006        Jeff.WK.Lin             Add Create File method
//  2025-05-28      0.00.007        Jeff.WK.Lin             1. Add method 'ParseStringToData'
//                                                          2. fix read file with skip empty row
//  2025-05-29      0.00.008        Jeff.WK.Lin             Trim each cell after write or read
//  2026-02-26      0.00.009        Jeff.WK.Lin             1. Fixed: Handle CSV fields containing newlines (RFC 4180)
//                                                          2. Use StreamWriter with BOM to fix Excel opening issues.
//                                                          3. Standardize column counts per row for strict parsers.
//  2026-05-21      0.00.010        Jeff.WK.Lin             Add XML documentation comments in traditional Chinese.
//  2026-11-05      0.00.011        Jeff.WK.Lin             Polish comment wording and align docs with actual behavior.
//  2026-11-06      0.00.012        Jeff.WK.Lin             1. Add ParseStringToData overload that reports each row's source line number
//                                                          2. Add static FormatDataWithHeader; WriteFile_DataWithHeader reuses it
//                                                          3. Skip a whitespace-only last row, same as the other rows
//
//=============================================================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CascadeControl.Utilities
{
    /// <summary>
    /// CSV 檔案的讀寫工具，支援 RFC 4180；寫檔時使用帶 BOM 的 UTF-8，讓 Excel 能正確開啟。
    /// </summary>
    internal class CsvFile
    {
        private readonly object _lock = new object();

        // 固定使用帶 BOM 的 UTF-8，Excel 才能正確辨識編碼，不會出現亂碼。
        private readonly Encoding _encoding = new UTF8Encoding(true);

        /// <summary>
        /// 讀取有標題列的 CSV 檔案。
        /// </summary>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="header">第一列，作為標題列。</param>
        /// <param name="data">其餘的列，作為資料列。</param>
        /// <returns>讀取成功且檔案有內容時為 true，否則為 false。</returns>
        public bool ReadFile_DataWithHeader(string filePath, out string[] header, out string[][] data)
        {
            if (!ReadFile(filePath, out var rawData) || rawData == null || rawData.Length == 0)
            {
                data = null;
                header = null;
                return false;
            }
            header = rawData[0];
            data = rawData.Skip(1).ToArray();
            return true;
        }

        /// <summary>
        /// 將 CSV 格式的字串解析為二維陣列。
        /// <para>支援 RFC 4180：用雙引號包住的欄位、跳脫的引號（""）以及跨行的儲存格。</para>
        /// <para>每一格都會去除前後空白；整列都是空白的列會被略過。</para>
        /// </summary>
        /// <param name="input">CSV 格式的原始字串。</param>
        /// <param name="data">解析後的二維字串陣列。</param>
        /// <returns>解析成功時為 true；目前的實作一律回傳 true。</returns>
        public static bool ParseStringToData(string input, out string[][] data)
        {
            return ParseStringToData(input, out data, out _);
        }

        /// <summary>
        /// 將 CSV 格式的字串解析為二維陣列，並回報每一列在原始字串中從第幾行開始。
        /// <para>解析規則與 <see cref="ParseStringToData(string, out string[][])"/> 相同。</para>
        /// </summary>
        /// <param name="input">CSV 格式的原始字串。</param>
        /// <param name="data">解析後的二維字串陣列。</param>
        /// <param name="lineNumbers">
        /// 與 <paramref name="data"/> 等長，第 i 個元素是 data[i] 起始的行號（由 1 起算）。
        /// CRLF、LF、CR 都算一次換行，引號內的換行也照算，所以被略過的空白列與跨行儲存格都不會讓行號偏掉。
        /// </param>
        /// <returns>解析成功時為 true；目前的實作一律回傳 true。</returns>
        public static bool ParseStringToData(string input, out string[][] data, out int[] lineNumbers)
        {
            if (string.IsNullOrEmpty(input)) { data = new string[0][]; lineNumbers = new int[0]; return true; }

            List<string[]> rows = new List<string[]>();
            List<int> rowLines = new List<int>();
            List<string> currentRow = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;
            int line = 1;       // 目前字元所在的行號
            int rowStart = 1;   // 目前這一列從第幾行開始

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"') { currentField.Append('"'); i++; }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);

                        // 引號內的換行屬於儲存格內容，但仍要計入行號；CRLF 只在 LF 算一次。
                        if (c == '\n' || (c == '\r' && !(i + 1 < input.Length && input[i + 1] == '\n'))) line++;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',') { currentRow.Add(currentField.ToString().Trim()); currentField.Clear(); }
                    else if (c == '\n' || c == '\r')
                    {
                        currentRow.Add(currentField.ToString().Trim());
                        if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
                        {
                            rows.Add(currentRow.ToArray());
                            rowLines.Add(rowStart);
                        }

                        currentRow.Clear();
                        currentField.Clear();
                        if (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n')
                        {
                            i++;
                        }

                        line++;
                        rowStart = line;
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }
            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString().Trim());
                if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    rows.Add(currentRow.ToArray());
                    rowLines.Add(rowStart);
                }
            }
            data = rows.ToArray();
            lineNumbers = rowLines.ToArray();
            return true;
        }

        /// <summary>
        /// 讀取 CSV 檔案並解析為二維陣列。
        /// </summary>
        /// <param name="filePath">CSV 檔案路徑。</param>
        /// <param name="data">解析後的二維字串陣列；讀取失敗時為 null。</param>
        /// <returns>讀取並解析成功時為 true；讀檔時發生例外則為 false。</returns>
        /// <exception cref="FileNotFoundException">檔案不存在。</exception>
        public bool ReadFile(string filePath, out string[][] data)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found.", filePath);
            }

            lock (_lock)
            {
                try
                {
                    // 有 BOM 時依 BOM 判斷編碼，沒有 BOM 就當成 UTF-8
                    string rawData = File.ReadAllText(filePath, Encoding.UTF8);
                    return ParseStringToData(rawData, out data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading file: {ex.Message}");
                    data = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// 依 RFC 4180 跳脫欄位內容。
        /// <para>先去除前後空白；內容含有逗號、換行或雙引號時，整格用雙引號包起來，裡面的每個雙引號改寫成兩個（""）。</para>
        /// </summary>
        private static string EscapeField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return "";
            }

            field = field.Trim();
            // 檢查是否需要加引號：包含逗號、換行或原本就有引號
            if (field.Contains(",") || field.Contains("\n") || field.Contains("\r") || field.Contains("\""))
            {
                // 內容裡的每個引號都要寫成兩個 ""
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        /// <summary>
        /// 將二維陣列轉換為符合 RFC 4180 格式的 CSV 字串。
        /// </summary>
        private string ParseToString(string[][] data)
        {
            if (data?.Any(row => row == null) != false)
            {
                throw new ArgumentNullException(nameof(data), "Data and its rows cannot be null.");
            }

            StringBuilder csvContent = new StringBuilder();

            foreach (var row in data)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    csvContent.Append(EscapeField(row[i]));
                    if (i < row.Length - 1)
                    {
                        csvContent.Append(',');
                    }
                }
                csvContent.AppendLine();
            }

            return csvContent.ToString();
        }

        /// <summary>
        /// 建立新的 CSV 檔案並寫入標題列；檔案已存在時會被覆寫。
        /// </summary>
        /// <param name="filePath">目標檔案路徑。</param>
        /// <param name="header">標題列的各欄名稱。</param>
        public void CreateFile(string filePath, string[] header)
        {
            lock (_lock)
            {
                string headerLine = string.Join(",", header.Select(EscapeField)) + Environment.NewLine;
                File.WriteAllText(filePath, headerLine, Encoding.UTF8);
            }
        }

        /// <summary>
        /// 將標題列與所有資料列轉換為符合 RFC 4180 格式的 CSV 字串，每一列以 <see cref="Environment.NewLine"/> 結尾。
        /// <para>每一列的欄數會對齊標題列：不足的補上空白欄，多出來的捨棄。</para>
        /// </summary>
        /// <param name="header">標題列的各欄名稱。</param>
        /// <param name="data">資料列；可為 null。</param>
        /// <returns>CSV 字串。</returns>
        public static string FormatDataWithHeader(string[] header, string[][] data)
        {
            StringBuilder csvContent = new StringBuilder();

            // 寫入標題列
            csvContent.AppendLine(string.Join(",", header.Select(EscapeField)));

            // 寫入資料列
            if (data != null)
            {
                foreach (var row in data)
                {
                    // 欄數對齊標題列，較嚴格的解析器（例如 csvLens）才不會出錯
                    var escapedRow = new string[header.Length];
                    for (int i = 0; i < header.Length; i++)
                    {
                        escapedRow[i] = i < row.Length ? EscapeField(row[i]) : "";
                    }
                    csvContent.AppendLine(string.Join(",", escapedRow));
                }
            }

            return csvContent.ToString();
        }

        /// <summary>
        /// 將標題列與所有資料列寫入 CSV 檔案；檔案已存在時會被覆寫。
        /// <para>內容與 <see cref="FormatDataWithHeader"/> 相同：欄數對齊標題列。編碼為帶 BOM 的 UTF-8。</para>
        /// </summary>
        /// <param name="filePath">目標檔案路徑。</param>
        /// <param name="header">標題列的各欄名稱。</param>
        /// <param name="data">資料列；可為 null。</param>
        public void WriteFile_DataWithHeader(string filePath, string[] header, string[][] data)
        {
            lock (_lock)
            {
                using (var writer = new StreamWriter(filePath, false, _encoding))
                {
                    writer.Write(FormatDataWithHeader(header, data));
                }
            }
        }

        /// <summary>
        /// 將二維陣列寫入 CSV 檔案；檔案已存在時會被覆寫。
        /// </summary>
        /// <param name="filePath">目標檔案路徑。</param>
        /// <param name="data">二維資料陣列。</param>
        public void WriteFile(string filePath, string[][] data)
        {
            lock (_lock)
            {
                File.WriteAllText(filePath, ParseToString(data), Encoding.UTF8);
            }
        }

        /// <summary>
        /// 將多筆資料列附加到現有的 CSV 檔案結尾。
        /// </summary>
        /// <param name="filePath">目標檔案路徑。</param>
        /// <param name="data">要附加的二維資料陣列。</param>
        /// <exception cref="FileNotFoundException">目標檔案不存在。</exception>
        public void AppendData(string filePath, string[][] data)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }

            lock (_lock)
            {
                File.AppendAllText(filePath, ParseToString(data), Encoding.UTF8);
            }
        }

        /// <summary>
        /// 將單筆資料列附加到現有的 CSV 檔案結尾。
        /// </summary>
        /// <param name="filePath">目標檔案路徑。</param>
        /// <param name="data">要附加的一維資料陣列。</param>
        /// <exception cref="FileNotFoundException">目標檔案不存在。</exception>
        public void AppendData(string filePath, string[] data)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }

            lock (_lock)
            {
                // 附加時也要使用同一種編碼
                using (var writer = new StreamWriter(filePath, true, _encoding))
                {
                    writer.WriteLine(string.Join(",", data.Select(EscapeField)));
                }
            }
        }
    }
}