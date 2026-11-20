using CascadeControl.Common;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CascadeControl.Views.Columns
{
    // 欄位的自繪與防閃爍。
    public partial class CascadeColumnsView
    {
        // 沒有焦點的欄位中，選取色與背景色的混合比例；要淡到和焦點欄的實心選取色一眼就分得出來。
        private const double INACTIVE_SELECTION_BLEND = 0.12;

        /// <summary>
        /// 加上 WS_EX_COMPOSITED，讓控制項和所有欄位 ListBox 先在同一個緩衝區由下到上畫完，再一次顯示到螢幕上。
        /// </summary>
        /// <remarks>
        /// ListBox 是原生控制項，<see cref="Control.DoubleBuffered"/> 對它無效；
        /// 只有在父視窗開啟合成，欄位「先清成白底、再畫項目」的過程和面板背景才不會分兩次出現在畫面上。
        /// </remarks>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_COMPOSITED;
                return cp;
            }
        }

        private void OnColumnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (!(sender is ListBox list) || e.Index < 0 || e.Index >= list.Items.Count) return;

            var node = list.Items[e.Index] as ICascadeNode;
            var columnIndex = _lists.IndexOf(list);
            var trailNode = columnIndex >= 0 ? _state.GetTrailNodeAt(columnIndex) : null;

            // 多選的那一欄只看多選：多選的節點畫成 MultiSelectBackColor，一眼就看得出 Copy / Cut / Delete 會作用在哪些節點。
            var marking = IsMarkingColumn(columnIndex);
            var isMarked = marking && IsMarked(columnIndex, node);
            var isSelected = marking ? isMarked : (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            var args = new CascadeDrawNodeEventArgs(
                e.Graphics,
                e.Font,
                e.Bounds,
                node,
                columnIndex,
                isSelected,
                list.Focused,
                list.Enabled,
                !isSelected && node != null && Equals(node, trailNode),
                isMarked);

            OnDrawNode(args);

            // 右鍵功能表開著時被點的那一列畫虛線框；這時不畫系統焦點框，兩個框才不會疊在一起。
            if (IsContextMenuRow(list, node))
            {
                DrawContextMenuFrame(e.Graphics, e.Bounds, args.IsSelected && args.IsFocusedColumn && !args.IsMultiSelected);
            }
            else if (ShouldDrawFocusCue(args.Handled, (e.State & DrawItemState.Selected) == DrawItemState.Selected, args.IsFocusedColumn))
            {
                if (marking)
                {
                    // 多選的欄位裡，焦點列的底色是淺黃或一般底色，不是實心選取色；用深色虛線才看得到。
                    ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, list.ForeColor, isMarked ? MultiSelectBackColor : list.BackColor);
                }
                else
                {
                    e.DrawFocusRectangle();
                }
            }
        }

        /// <summary>
        /// 要不要在這一列畫焦點框：焦點列（ListBox 的選取項，也就是選取路徑在這一欄的節點）在有焦點的欄位裡就畫，
        /// 不論它有沒有被多選；自繪（<see cref="CascadeDrawNodeEventArgs.Handled"/>）時不畫。
        /// </summary>
        internal static bool ShouldDrawFocusCue(bool handled, bool isFocusRow, bool isFocusedColumn) =>
            !handled && isFocusRow && isFocusedColumn;

        /// <summary>
        /// 右鍵功能表針對的那一列：1 像素虛線框。底色是實心選取色時用 <see cref="SelectionForeColor"/>，其他用一般文字色，才看得清楚。
        /// </summary>
        private void DrawContextMenuFrame(Graphics graphics, Rectangle bounds, bool onHighlight)
        {
            using (var pen = new Pen(onHighlight ? SelectionForeColor : SystemColors.WindowText) { DashStyle = DashStyle.Dot })
            {
                graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
            }
        }

        /// <summary>
        /// 畫出這一列的預設外觀；<see cref="DrawNode"/> 的訂閱端沒有自行繪製時由 <see cref="OnDrawNode"/> 呼叫。
        /// </summary>
        /// <param name="e">這一列的繪製參數。</param>
        /// <remarks>
        /// 衍生類別要在預設外觀上多畫東西（例如勾選框）時覆寫它，文字的部分交給
        /// <see cref="DrawDefaultNode(CascadeDrawNodeEventArgs, Rectangle)"/>。
        /// </remarks>
        protected virtual void DrawDefaultNode(CascadeDrawNodeEventArgs e) => DrawDefaultNode(e, e.Bounds);

        /// <summary>
        /// 畫出預設外觀，但文字只畫在 <paramref name="textBounds"/> 裡；背景仍然填滿整個 <see cref="CascadeDrawNodeEventArgs.Bounds"/>。
        /// </summary>
        /// <param name="e">這一列的繪製參數。</param>
        /// <param name="textBounds">文字的範圍；衍生類別要在列的前面多畫東西（例如勾選框）時，把它往右縮。</param>
        /// <remarks>
        /// 配色規則：多選的節點用 <see cref="MultiSelectBackColor"/>（淺黃），焦點欄的選取項用 <see cref="SelectionBackColor"/> / <see cref="SelectionForeColor"/>，
        /// 其他欄的選取項用 <see cref="InactiveSelectionBackColor"/>（預設為調淡的選取色），
        /// 記憶軌跡項目用淺灰底，停用時整欄顯示灰字。
        /// </remarks>
        protected void DrawDefaultNode(CascadeDrawNodeEventArgs e, Rectangle textBounds)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            var list = e.ColumnIndex >= 0 && e.ColumnIndex < _lists.Count ? _lists[e.ColumnIndex] : null;
            var listBack = list?.BackColor ?? SystemColors.Window;
            var listFore = list?.ForeColor ?? SystemColors.WindowText;

            Color back, fore;
            if (!e.IsEnabled)
            {
                back = SystemColors.Control;
                fore = SystemColors.GrayText;
            }
            else if (e.IsMultiSelected)
            {
                back = MultiSelectBackColor;
                fore = listFore;
            }
            else if (e.IsSelected && e.IsFocusedColumn)
            {
                back = SelectionBackColor;
                fore = SelectionForeColor;
            }
            else if (e.IsSelected)
            {
                // 有選取、但不是目前操作的欄位：用 InactiveSelectionBackColor，預設是調淡的 SelectionBackColor。
                // 用灰色的話幾乎看不出選了什麼，整條路徑就像被藏起來；
                // 調淡的選取色看得出是選取項，又能和焦點欄的實心選取色一眼區分。
                back = InactiveSelectionBackColor;
                fore = listFore;
            }
            else if (e.IsTrailNode)
            {
                // 記憶軌跡項目不算選取，用不帶藍色的淺灰，避免和選取路徑混在一起。
                back = TrailBackColor;
                fore = listFore;
            }
            else
            {
                back = listBack;
                fore = listFore;
            }

            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);

            TextRenderer.DrawText(
                e.Graphics,
                e.Node?.DisplayText ?? string.Empty,
                e.Font,
                textBounds,
                fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static Color Blend(Color color, Color background, double ratio)
        {
            return Color.FromArgb(
                (int)((color.R * ratio) + (background.R * (1 - ratio))),
                (int)((color.G * ratio) + (background.G * (1 - ratio))),
                (int)((color.B * ratio) + (background.B * (1 - ratio))));
        }
    }

    /// <summary>
    /// 防止閃爍、暫停繪製期間移動焦點、攔下欄位滑鼠訊息用到的 Win32 API。
    /// </summary>
    internal static class NativeMethods
    {
        internal const int WS_EX_COMPOSITED = 0x02000000;
        internal const int WS_CLIPCHILDREN = 0x02000000;
        internal const int WM_SETREDRAW = 0x000B;
        internal const int WM_VSCROLL = 0x0115;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONDBLCLK = 0x0203;
        internal const int WM_RBUTTONDOWN = 0x0204;
        internal const int WM_RBUTTONDBLCLK = 0x0206;
        internal const int WM_MOUSEWHEEL = 0x020A;
        internal const int MK_CONTROL = 0x0008;
        internal const int MK_SHIFT = 0x0004;

        internal const uint RDW_INVALIDATE = 0x0001;
        internal const uint RDW_ERASE = 0x0004;
        internal const uint RDW_ALLCHILDREN = 0x0080;
        internal const uint RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr hWnd);
    }
}