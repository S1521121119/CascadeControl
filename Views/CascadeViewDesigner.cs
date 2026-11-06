using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView"/> 的設計工具。它不是容器，設計階段無法把其他控制項拖進去。
    /// </summary>
    /// <remarks>
    /// <see cref="CascadeView"/> 繼承自 <see cref="Panel"/>，沒有指定設計工具的話會沿用 Panel 的容器設計工具（PanelDesigner），
    /// 可以拖放子控制項；但欄位 ListBox 由控制項自己管理，外來的子控制項會打亂排版與捲動。
    /// 設計階段沒有資料，一欄都沒有，畫面一片空白，所以另外畫上虛線框與名稱，讓人看得出它的位置與大小。
    /// 只適用於 .NET Framework：之後若移植到 .NET（Core），WinForms 設計工具會改在處理序外（out-of-process）執行，這個類別需要改寫。
    /// </remarks>
    internal sealed class CascadeViewDesigner : ControlDesigner
    {
        /// <inheritdoc />
        protected override void OnPaintAdornments(PaintEventArgs pe)
        {
            base.OnPaintAdornments(pe);

            var bounds = Control.ClientRectangle;
            if (bounds.Width <= 1 || bounds.Height <= 1) return;

            using (var pen = new Pen(SystemColors.ControlDark) { DashStyle = DashStyle.Dash })
            {
                pe.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

            TextRenderer.DrawText(
                pe.Graphics,
                $"{nameof(CascadeView)}（{Control.Name}）",
                Control.Font,
                bounds,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}