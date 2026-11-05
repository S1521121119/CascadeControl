using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView.SelectionChanged"/> 與 <see cref="CascadeView.ItemActivated"/> 共用的事件參數，所有內容都由選取路徑推算而來。
    /// </summary>
    /// <remarks>
    /// 只保存 <see cref="Path"/>，其他屬性都從路徑推算，不會有兩份資料不一致的問題。
    /// <see cref="ICascadeItem"/> 沒有 Parent，所以 <see cref="Parent"/> 取自路徑中的上一層。
    /// 沒有選取任何節點時，<see cref="Item"/> 為 null、<see cref="Level"/> 為 -1，
    /// <see cref="Children"/> 則是第一欄的內容；這和「有子節點就開一欄」的規則一致，樹根的子節點就是第一欄。
    /// </remarks>
    public class CascadeItemEventArgs : EventArgs
    {
        private static readonly IReadOnlyList<ICascadeItem> NoItems = new ICascadeItem[0];

        private readonly IReadOnlyList<ICascadeItem> _roots;

        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="path">由淺至深的選取路徑；null 或空集合代表未選取。</param>
        /// <param name="roots">第一欄的內容，只有 <paramref name="path"/> 為空時才會成為 <see cref="Children"/>；可為 null。</param>
        /// <exception cref="ArgumentException"><paramref name="path"/> 中含有 null。</exception>
        public CascadeItemEventArgs(IEnumerable<ICascadeItem> path, IEnumerable<ICascadeItem> roots = null)
        {
            var steps = path?.ToArray() ?? new ICascadeItem[0];
            if (steps.Any(step => step == null))
                throw new ArgumentException("選取路徑中不可含有 null。", nameof(path));

            Path = Array.AsReadOnly(steps);
            _roots = steps.Length == 0 && roots != null ? Array.AsReadOnly(roots.ToArray()) : NoItems;
        }

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。
        /// </summary>
        public IReadOnlyList<ICascadeItem> Path { get; }

        /// <summary>
        /// 路徑的終點，也就是目前選取的最深節點；未選取時為 null。
        /// </summary>
        public ICascadeItem Item => Path.Count > 0 ? Path[Path.Count - 1] : null;

        /// <summary>
        /// <see cref="Item"/> 所在的欄位索引，由 0 起算；未選取時為 -1。
        /// </summary>
        public int Level => Path.Count - 1;

        /// <summary>
        /// <see cref="Item"/> 的上一層節點；<see cref="Item"/> 位於第一欄或未選取時為 null。
        /// </summary>
        public ICascadeItem Parent => Path.Count > 1 ? Path[Path.Count - 2] : null;

        /// <summary>
        /// 下一欄會顯示的內容：<see cref="Item"/> 的子節點；未選取時是第一欄的內容。不會是 null。
        /// </summary>
        public IReadOnlyList<ICascadeItem> Children => Item == null ? _roots : Item.Children ?? NoItems;

        /// <summary>
        /// <see cref="Item"/> 是否為葉節點，也就是整條路徑已經選到底；未選取時為 false。
        /// </summary>
        public bool IsLeaf => Item != null && Children.Count == 0;
    }

    /// <summary>
    /// <see cref="CascadeView.DrawItem"/> 的事件參數。把 <see cref="Handled"/> 設為 true 就會略過預設繪製。
    /// </summary>
    /// <remarks>
    /// 不繼承 <see cref="CascadeItemEventArgs"/>：繪製時關心的是「這一列」，不是選取路徑；
    /// 每畫一列就組一次路徑很浪費，對未選取的列也沒有意義。
    /// </remarks>
    public class CascadeDrawItemEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        public CascadeDrawItemEventArgs(
            Graphics graphics, Font font, Rectangle bounds,
            ICascadeItem item, int level, bool isSelected, bool isFocusedColumn, bool isEnabled)
        {
            Graphics = graphics;
            Font = font;
            Bounds = bounds;
            Item = item;
            Level = level;
            IsSelected = isSelected;
            IsFocusedColumn = isFocusedColumn;
            IsEnabled = isEnabled;
        }

        /// <summary>繪圖用的 Graphics。</summary>
        public Graphics Graphics { get; }

        /// <summary>欄位字型。</summary>
        public Font Font { get; }

        /// <summary>這一列的繪製範圍。</summary>
        public Rectangle Bounds { get; }

        /// <summary>這一列對應的節點。</summary>
        public ICascadeItem Item { get; }

        /// <summary>這一列所在的欄位索引，由 0 起算。</summary>
        public int Level { get; }

        /// <summary>這一列是否為該欄的選取項。</summary>
        public bool IsSelected { get; }

        /// <summary>這一欄是否持有鍵盤焦點。</summary>
        public bool IsFocusedColumn { get; }

        /// <summary>這一欄目前是否啟用。</summary>
        public bool IsEnabled { get; }

        /// <summary>設為 true 表示已自行繪製完成，控制項不會再畫預設外觀。</summary>
        public bool Handled { get; set; }
    }
}