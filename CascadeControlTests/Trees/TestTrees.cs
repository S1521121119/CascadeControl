using CascadeControl.Common;
using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Trees
{
    /// <summary>
    /// 多個測試類別共用的測試資料與比對工具。
    /// </summary>
    internal static class TestTrees
    {
        /// <summary>
        /// 產生隨機的樹：名稱池很小，並刻意把同一組子樹接在不同的前綴後面，
        /// 讓「子樹完全相同可以合併」和「相似但不同、不可合併」的情況都常出現。
        /// </summary>
        public static MenuTree Random(int seed)
        {
            var rng = new Random(seed);
            var tree = new MenuTree();
            var pool = new[] { "a", "b", "c", "d", "e" };

            var templates = Enumerable.Range(0, 3).Select(_ =>
                Enumerable.Range(0, rng.Next(1, 5)).Select(__ =>
                    Enumerable.Range(0, rng.Next(1, 3)).Select(___ => pool[rng.Next(pool.Length)]).ToArray()).ToList()).ToList();

            var paths = rng.Next(1, 40);
            for (var p = 0; p < paths; p++)
            {
                var prefix = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => pool[rng.Next(pool.Length)]).ToList();

                if (rng.Next(3) == 0)
                {
                    foreach (var tail in templates[rng.Next(templates.Count)]) tree.AddPath(prefix.Concat(tail));
                }
                else
                {
                    tree.AddPath(prefix);
                }
            }

            return tree;
        }

        /// <summary>
        /// Demo 用的範本樹：葉節點深度 2～6 層、單欄 32 項，涵蓋大部分實際用法。
        /// </summary>
        public static MenuTree Template()
        {
            var tree = new MenuTree();
            tree.ExpandAndAddPaths("System", "Reboot|Shutdown");
            tree.ExpandAndAddPaths("System", "Info", "Version|Uptime|Memory");
            tree.ExpandAndAddPaths("Motor", "Axis X|Axis Y|Axis Z", "Home");
            tree.ExpandAndAddPaths("Motor", "Axis X|Axis Y|Axis Z", "Jog", "Forward|Backward");
            tree.ExpandAndAddPaths("Motor", "Axis X|Axis Y|Axis Z", "Speed", "Low|Mid|High");
            tree.ExpandAndAddPaths("IO", "Input", Alternatives("DI", 32));
            tree.ExpandAndAddPaths("IO", "Output", Alternatives("DO", 8), "On|Off");
            tree.ExpandAndAddPaths("Diagnostics", "Sensor", "Temperature", "Zone A|Zone B", "Read", "Raw|Calibrated");
            tree.ExpandAndAddPaths("Diagnostics", "Log", "Export|Clear");
            return tree;
        }

        /// <summary>
        /// 產生 <c>{prefix}1|{prefix}2|…|{prefix}{count}</c>。
        /// </summary>
        public static string Alternatives(string prefix, int count)
        {
            return MultiNameCellSyntax.JoinNames(Enumerable.Range(1, count).Select(i => prefix + i));
        }

        /// <summary>
        /// 樹的展開形式文字，同時包含結構與兄弟節點的順序，用來判斷兩棵樹是否完全相同。
        /// </summary>
        public static string Fingerprint(MenuTree tree) => MenuTreeCsvText.Format(tree);

        /// <summary>
        /// 由葉節點的 <see cref="MenuNode.FullPathText"/> 依序組成，方便在失敗訊息中閱讀。
        /// </summary>
        public static string[] LeafPaths(MenuTree tree) => tree.GetLeaves().Select(leaf => leaf.FullPathText).ToArray();

        /// <summary>
        /// 把資料列寫成「A,B / C,D」的形式，方便比對與閱讀。
        /// </summary>
        public static string Describe(IEnumerable<string[]> rows) => string.Join(" / ", rows.Select(row => string.Join(",", row)));

        /// <summary>
        /// 一列展開後的路徑數：各個非空格子的名稱數相乘。
        /// </summary>
        public static long ExpansionOf(string[] row)
        {
            return row.Where(cell => cell.Length > 0)
                      .Aggregate(1L, (total, cell) => total * cell.Split(MultiNameCellSyntax.NAME_SEPARATOR).Length);
        }
    }
}