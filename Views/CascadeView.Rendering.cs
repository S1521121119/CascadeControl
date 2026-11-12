using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 欄位的自繪與防閃爍。
    public partial class CascadeView
    {
        // 沒有焦點的欄位中，選取色與背景色的混合比例。
        private const double INACTIVE_SELECTION_BLEND = 0.25;

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

            var item = list.Items[e.Index] as ICascadeItem;
            var level = _lists.IndexOf(list);
            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var remembered = level >= 0 ? _navigator.RememberedAt(level) : null;

            var args = new CascadeDrawItemEventArgs(
                e.Graphics,
                e.Font,
                e.Bounds,
                item,
                level,
                isSelected,
                list.Focused,
                list.Enabled,
                !isSelected && item != null && Equals(item, remembered));

            OnDrawItem(args);

            if (!args.Handled && args.IsSelected && args.IsFocusedColumn) e.DrawFocusRectangle();
        }

        /// <summary>
        /// 預設外觀：焦點欄的選取項用系統選取色，其他欄的選取項用調淡的選取色，
        /// 記憶項目用淺灰底，停用時整欄顯示灰字。
        /// </summary>
        private void DrawDefaultItem(CascadeDrawItemEventArgs e)
        {
            var list = e.Level >= 0 && e.Level < _lists.Count ? _lists[e.Level] : null;
            var listBack = list?.BackColor ?? SystemColors.Window;
            var listFore = list?.ForeColor ?? SystemColors.WindowText;

            Color back, fore;
            if (!e.IsEnabled)
            {
                back = SystemColors.Control;
                fore = SystemColors.GrayText;
            }
            else if (e.IsSelected && e.IsFocusedColumn)
            {
                back = SystemColors.Highlight;
                fore = SystemColors.HighlightText;
            }
            else if (e.IsSelected)
            {
                // 有選取、但不是目前操作的欄位：用調淡的系統選取色。
                // 用灰色的話幾乎看不出選了什麼，整條路徑就像被藏起來；
                // 淡藍色看得出是選取項，又能和焦點欄的實心藍一眼區分。
                back = InactiveSelectionBackColor;
                fore = listFore;
            }
            else if (e.IsRemembered)
            {
                // 記憶項目不算選取，用不帶藍色的淺灰，避免和選取路徑混在一起。
                back = RememberedBackColor;
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
                e.Item?.Text ?? string.Empty,
                e.Font,
                e.Bounds,
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
    /// 防止閃爍與暫停繪製期間移動焦點用到的 Win32 API。
    /// </summary>
    internal static class NativeMethods
    {
        internal const int WS_EX_COMPOSITED = 0x02000000;
        internal const int WM_SETREDRAW = 0x000B;

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