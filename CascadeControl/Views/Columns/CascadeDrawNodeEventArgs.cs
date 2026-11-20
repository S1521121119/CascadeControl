using CascadeControl.Common;
using CascadeControl.State;
using System;
using System.Drawing;

namespace CascadeControl.Views.Columns
{
    /// <summary>
    /// <see cref="CascadeColumnsView.DrawNode"/> 的事件參數。把 <see cref="Handled"/> 設為 true 就會略過預設繪製。
    /// </summary>
    /// <remarks>
    /// 不繼承 <see cref="CascadeNodeEventArgs"/>：繪製時關心的是「這一列」，不是選取路徑；
    /// 每畫一列就組一次路徑很浪費，對未選取的列也沒有意義。
    /// </remarks>
    public class CascadeDrawNodeEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        public CascadeDrawNodeEventArgs(
            Graphics graphics, Font font, Rectangle bounds,
            ICascadeNode node, int columnIndex, bool isSelected, bool isFocusedColumn, bool isEnabled,
            bool isTrailNode = false, bool isMultiSelected = false)
        {
            IsTrailNode = isTrailNode;
            IsMultiSelected = isMultiSelected;
            Graphics = graphics;
            Font = font;
            Bounds = bounds;
            Node = node;
            ColumnIndex = columnIndex;
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
        public ICascadeNode Node { get; }

        /// <summary>這一列所在的欄位索引，由 0 起算。</summary>
        public int ColumnIndex { get; }

        /// <summary>這一列是否為該欄的選取項。</summary>
        public bool IsSelected { get; }

        /// <summary>
        /// 這一列是否為記憶軌跡項目：使用者退回較淺的層級前走過、目前不算選取的項目。與 <see cref="IsSelected"/> 不會同時為 true。
        /// </summary>
        public bool IsTrailNode { get; }

        /// <summary>
        /// 這一列是否為多選的節點（見 <see cref="CascadeColumnsView.MultiSelectedNodes"/>）；為 true 時 <see cref="IsSelected"/> 也是 true。
        /// </summary>
        public bool IsMultiSelected { get; }

        /// <summary>這一欄是否持有鍵盤焦點。</summary>
        public bool IsFocusedColumn { get; }

        /// <summary>這一欄目前是否啟用。</summary>
        public bool IsEnabled { get; }

        /// <summary>設為 true 表示已自行繪製完成，控制項不會再畫預設外觀。</summary>
        public bool Handled { get; set; }
    }
}
