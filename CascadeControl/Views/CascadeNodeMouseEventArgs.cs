using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace CascadeControl.Views
{
    /// <summary>
    /// 節點滑鼠事件（<see cref="CascadeView.NodeMouseClick"/>、<see cref="CascadeTreeImageView.NodeMouseClick"/> 與
    /// <see cref="CascadeTreeImageView.NodeMouseDoubleClick"/>）共用的事件參數：被點的節點、路徑與滑鼠資訊。
    /// </summary>
    public class CascadeNodeMouseEventArgs : CascadeNodeEventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="path">由淺至深到被點節點的路徑，不可為空。</param>
        /// <param name="pathSeparator"><see cref="CascadeNodeEventArgs.PathText"/> 使用的層級分隔字串。</param>
        /// <param name="button">按下的滑鼠鍵。</param>
        /// <param name="location">點擊位置，欄位或控制項的用戶區座標。</param>
        /// <param name="bounds">被點節點的矩形範圍，與 <paramref name="location"/> 同一個座標系。</param>
        /// <param name="screenLocation">點擊位置的螢幕座標。</param>
        /// <exception cref="ArgumentException"><paramref name="path"/> 為 null、空集合或含有 null。</exception>
        public CascadeNodeMouseEventArgs(
            IEnumerable<ICascadeNode> path, string pathSeparator,
            System.Windows.Forms.MouseButtons button,
            Point location, Rectangle bounds, Point screenLocation)
            : base(path, null, pathSeparator)
        {
            if (Node == null) throw new ArgumentException("點擊路徑不可為空。", nameof(path));

            Button = button;
            Location = location;
            Bounds = bounds;
            ScreenLocation = screenLocation;
        }

        /// <summary>按下的滑鼠鍵。</summary>
        public System.Windows.Forms.MouseButtons Button { get; }

        /// <summary>點擊位置，欄位或控制項內的座標，和 <see cref="Bounds"/> 同一個座標系。</summary>
        public Point Location { get; }

        /// <summary>被點節點的矩形範圍，和 <see cref="Location"/> 同一個座標系。</summary>
        public Rectangle Bounds { get; }

        /// <summary>
        /// 點擊位置的螢幕座標，用來顯示快顯功能表。
        /// </summary>
        /// <remarks>改選後欄位可能移動，欄位內的座標就對不回螢幕上的位置，所以另外提供。</remarks>
        public Point ScreenLocation { get; }
    }
}
