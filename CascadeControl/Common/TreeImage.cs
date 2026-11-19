using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace CascadeControl.Common
{
    /// <summary>
    /// <see cref="TreeDrawer.RenderImage"/> 畫出來的圖，連同每個節點方塊在圖上的位置，用來找出滑鼠點到哪個節點。
    /// </summary>
    /// <remarks>
    /// 位置以 <see cref="Image"/> 的像素為單位；圖太大被等比例縮小時，位置也跟著縮小。
    /// 整棵樹第一層有多個節點時畫出的 Root 不屬於任何 <see cref="MenuNode"/>，不在 <see cref="Nodes"/> 裡。
    /// </remarks>
    public sealed class TreeImage : IDisposable
    {
        internal TreeImage(Bitmap image, IReadOnlyList<TreeImageNode> nodes)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        }

        /// <summary>
        /// 畫好的圖。
        /// </summary>
        public Bitmap Image { get; }

        /// <summary>
        /// 圖上每個節點方塊的位置，前序排列（父節點在子節點之前）。
        /// </summary>
        public IReadOnlyList<TreeImageNode> Nodes { get; }

        /// <summary>
        /// 找出圖上某一點落在哪個節點方塊裡。
        /// </summary>
        /// <param name="location">圖上的座標（<see cref="Image"/> 的像素）。</param>
        /// <returns>點到的節點；沒點到任何方塊時為 null。</returns>
        public MenuNode GetNodeAt(PointF location)
        {
            foreach (var node in Nodes)
            {
                if (node.Bounds.Contains(location)) return node.Node;
            }

            return null;
        }

        /// <summary>
        /// 找出圖上某一點落在哪個節點的展開鈕（[+] 或 [-]）上；展開鈕很小，判斷範圍往外多算 <see cref="EXPANDER_HIT_MARGIN"/>。
        /// </summary>
        /// <param name="location">圖上的座標（<see cref="Image"/> 的像素）。</param>
        /// <returns>展開鈕所屬的節點；沒點到任何展開鈕時為 null。</returns>
        public MenuNode GetExpanderAt(PointF location)
        {
            foreach (var node in Nodes)
            {
                if (node.ExpanderBounds.IsEmpty) continue;
                if (RectangleF.Inflate(node.ExpanderBounds, EXPANDER_HIT_MARGIN, EXPANDER_HIT_MARGIN).Contains(location)) return node.Node;
            }

            return null;
        }

        private const float EXPANDER_HIT_MARGIN = 2f;

        /// <summary>
        /// 釋放 <see cref="Image"/>。
        /// </summary>
        public void Dispose()
        {
            Image.Dispose();
        }
    }

    /// <summary>
    /// <see cref="TreeImage"/> 上一個節點方塊的位置。
    /// </summary>
    public sealed class TreeImageNode
    {
        internal TreeImageNode(MenuNode node, RectangleF bounds, RectangleF expanderBounds, bool isExpanded)
        {
            Node = node;
            Bounds = bounds;
            ExpanderBounds = expanderBounds;
            IsExpanded = isExpanded;
        }

        /// <summary>
        /// 這個方塊代表的節點。
        /// </summary>
        public MenuNode Node { get; }

        /// <summary>
        /// 方塊在圖上的位置（<see cref="TreeImage.Image"/> 的像素）。
        /// </summary>
        public RectangleF Bounds { get; }

        /// <summary>
        /// 展開鈕（[+] 或 [-]）在圖上的位置（<see cref="TreeImage.Image"/> 的像素）；葉節點沒有展開鈕，是 <see cref="RectangleF.Empty"/>。
        /// </summary>
        public RectangleF ExpanderBounds { get; }

        /// <summary>
        /// 是否展開；收合的節點畫 [+]，子節點不在圖上。葉節點一律為 true。
        /// </summary>
        public bool IsExpanded { get; }
    }
}