using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    /// <summary>
    /// 把 <see cref="MenuTree"/> 畫成由左到右展開的樹：一層一欄，節點是方塊（一行靠左，顯示名稱；開啟編號時在名稱前加上同層編號），
    /// 父子之間用貝茲曲線連接。
    /// </summary>
    /// <remarks>
    /// 有子節點的畫圓角長方形，方塊內的最右邊畫一個正方形的展開鈕（該層因此加寬）：收合時是 [+]、子節點不畫，展開時是 [-]；
    /// 葉節點畫直角長方形。畫整棵樹時第一層的節點各自從最左欄開始，不另外畫 Root 把它們接起來。同一層的方塊等寬，寬度依字型量出該層最長的名稱（顯示編號時含最長編號）決定，且不小於高度；
    /// 顯示編號時同一層的名稱從同一個位置開始，編號位數不同也對得齊。
    /// 圖照實際大小畫，不會為了塞進控制項而縮小；放不下時由外層的捲動容器處理。
    /// <para>
    /// 上色分三件事，彼此不衝突：
    /// 底色依原樹的深度決定色系，同一欄由上往下，同一個父節點的子節點算一組，各組輪流用該色系的 A 色（較淺）與 B 色（較深），
    /// 相鄰兩組的葉節點才分得出來；
    /// 有底色的節點，外框是同色系的深色；
    /// 有勾選資訊時，勾選的節點外框加粗、底色不變（全勾是實線，勾了但子孫沒有全勾是虛線），沒勾的依 <see cref="CascadeDiagramUncheckedDisplay"/> 變淡或不畫；
    /// 選取的節點在原本的外框外圍另外畫一圈矩形紅框，原本的外框（實線或虛線）照畫、不被蓋住。連線一律黑色，只有通往沒勾節點的連線變淡。
    /// </para>
    /// </remarks>
    public static class TreeDrawer
    {
        private const float MARGIN = 12f;
        private const float PADDING_X = 8f;
        private const float PADDING_Y = 3f;

        // 編號和名稱之間的距離。

        private const float NUMBER_GAP = 4f;
        private const float ROW_GAP = 6f;
        private const float MIN_LEVEL_GAP = 40f;
        private const float CORNER_RADIUS = 6f;

        // 佔位方塊的名稱寬度（幾個行高），隨字型縮放。
        private const float PLACEHOLDER_NAME_LINES = 4f;

        // GDI+ 的 Bitmap 單邊太大會配置失敗；超過時整張等比例縮小。
        private const float MAX_BITMAP_SIDE = 16000f;

        private static readonly Color DIMMED = Color.FromArgb(170, 170, 170);
        private static readonly Color DIMMED_LINE = Color.FromArgb(214, 214, 214);
        private static readonly Color SELECTED_BORDER = Color.Red;

        // 每一層（原樹的深度）的底色：[0] 是 A 色、[1] 是 B 色，同一個色相只差亮度。
        // 避開紅色（選取）；層數超過就從頭循環。
        private static readonly Color[][] LEVEL_COLORS =
        {
            new[] { Color.FromArgb(246, 235, 223), Color.FromArgb(224, 189, 153) }, // 橘
            new[] { Color.FromArgb(223, 246, 223), Color.FromArgb(153, 224, 153) }, // 綠
            new[] { Color.FromArgb(236, 223, 246), Color.FromArgb(196, 153, 224) }, // 紫
            new[] { Color.FromArgb(246, 243, 223), Color.FromArgb(224, 215, 153) }, // 黃
            new[] { Color.FromArgb(223, 246, 240), Color.FromArgb(153, 224, 208) }, // 薄荷
            new[] { Color.FromArgb(236, 246, 223), Color.FromArgb(196, 224, 153) }, // 黃綠
        };

        // 外框的亮度是底色的幾倍：色相與飽和度不變，只調暗。
        private const float BORDER_LIGHTNESS_RATIO = 0.5f;

        private const float BORDER_WIDTH = 1.5f;
        private const float CHECKED_BORDER_WIDTH = 2.5f;
        private const float SELECTED_BORDER_WIDTH = 2f;

        // 紅框與原本外框之間留的空隙。
        private const float SELECTED_BORDER_GAP = 1f;

        // 有子節點的方塊內的 [+]：邊長（奇數，加號才置中）、加號到 [+] 邊框的距離、與名稱的距離、到方塊右緣的距離。
        private const int EXPANDER_SIZE = 9;

        private const int EXPANDER_PADDING = 2;
        private const float EXPANDER_GAP = 6f;
        private const float EXPANDER_RIGHT = 5f;

        /// <summary>
        /// 暫時佔位的空白方塊：畫在 <see cref="Parent"/> 底下原樹第 <see cref="Position"/> 個子節點的位置，圖上以 <see cref="Node"/> 代表它。
        /// </summary>
        internal sealed class Placeholder
        {
            public Placeholder(MenuNode parent, int position)
            {
                Parent = parent;
                Position = position;
            }

            public MenuNode Parent { get; }

            public int Position { get; }

            /// <summary>不在樹裡的節點，只用來在 <see cref="TreeImage.Nodes"/> 找到這個方塊的位置。</summary>
            public MenuNode Node { get; } = new MenuNode(string.Empty);
        }

        /// <summary>
        /// 拖曳移動或複製時的預覽：<see cref="Nodes"/>（連同子孫）另外接一份在 <see cref="Parent"/> 底下第 <see cref="InsertIndex"/> 個位置，畫成半透明；
        /// 移動模式時原處標示為即將離開（調淡、灰色虛線外框與紅色叉叉），複製模式時原處維持原樣。
        /// </summary>
        internal sealed class MovePreview
        {
            private readonly HashSet<MenuNode> _moved;

            public MovePreview(MenuNode parent, IReadOnlyList<MenuNode> nodes)
                : this(parent, parent.Children.Count, nodes, isCopy: false, focusedNode: null)
            {
            }

            public MovePreview(MenuNode parent, int insertIndex, IReadOnlyList<MenuNode> nodes, bool isCopy = false, MenuNode focusedNode = null)
            {
                Parent = parent;
                InsertIndex = insertIndex;
                Nodes = nodes;
                IsCopy = isCopy;
                FocusedNode = focusedNode;
                _moved = new HashSet<MenuNode>(nodes);
            }

            public MenuNode Parent { get; }

            public int InsertIndex { get; }

            public IReadOnlyList<MenuNode> Nodes { get; }

            public bool IsCopy { get; }

            public MenuNode FocusedNode { get; }

            public bool IsMoved(MenuNode node) => _moved.Contains(node);

            public bool IsInMovedSubtree(MenuNode node)
            {
                for (var current = node; current != null; current = current.Parent)
                {
                    if (_moved.Contains(current)) return true;
                }

                return false;
            }
        }

        // 建立要畫的節點時，每一層都一樣的設定。
        private sealed class BuildOptions
        {
            public Func<MenuNode, bool> IsChecked { get; set; }
            public bool HideUnchecked { get; set; }
            public MenuNode SelectedNode { get; set; }
            public Placeholder Placeholder { get; set; }
            public MovePreview Preview { get; set; }
            public Func<MenuNode, bool> IsCut { get; set; }
        }

        private sealed class LayoutNode
        {
            public MenuNode Source { get; set; }
            public int Level { get; set; }
            public List<LayoutNode> Children { get; } = new List<LayoutNode>();

            // 插入或建立子節點時暫時佔位的空白方塊：Source 是不在樹裡的節點，不寫編號與名稱。
            public bool IsPlaceholder { get; set; }

            // 拖曳移動的預覽：接到新位置的節點（連同子孫），畫成半透明。
            public bool IsGhost { get; set; }

            // 拖曳移動的預覽：留在原處、放開後會被移走的節點（連同子孫），調淡並畫灰色虛線外框與紅叉。
            public bool IsLeaving { get; set; }

            // 待剪下（尚未貼上）的節點（連同子孫）：畫成半透明，不畫紅叉。
            public bool IsCut { get; set; }

            // 需要畫在半透明圖層上的節點：拖曳預覽的新位置（IsGhost）或待剪下的節點（IsCut）。
            public bool IsTranslucent => IsGhost || IsCut;

            // 同層編號（1-based）；預覽移動時照移動後的結果重算，會被移走的節點保留原樹的編號。
            public int Number { get; set; }

            // 同層節點數（含自己），決定編號補零的位數；和 Number 一樣照預覽移動後的結果算，會被移走的節點用原樹的數量。
            public int SiblingCount { get; set; }

            // 原樹的子節點數（含隱藏、收合的）；預覽移動時照移動後的結果重算。
            public int ChildCount { get; set; }

            // 勾選的子節點數，和 ChildCount 算法相同；沒有勾選資訊時為 null。
            public int? CheckedChildCount { get; set; }

            // 畫在名稱前的編號（如 <1>、<01>）；空字串表示不畫編號。排版量寬度和繪製都用這兩個字串。
            public string NumberText { get; set; } = string.Empty;

            // 名稱，顯示子節點數時後面接 (X) 或 (x/X)；佔位方塊是空字串。
            public string NameText { get; set; } = string.Empty;

            // 在原樹的深度（Root 是 0），決定色系；佔位方塊是父節點的深度加 1。
            public int Depth { get; set; }

            public bool IsChecked { get; set; }

            // 有展開鈕時是否展開；收合的節點不畫子節點。
            public bool IsExpanded { get; set; } = true;

            // 自己和原樹中的所有子孫都勾了。
            public bool IsFullyChecked { get; set; }

            // 目前選取的節點，外框外圍另外畫一圈紅框。
            public bool IsSelected { get; set; }

            // 底色用 B 色：所在的兄弟組是同一欄由上往下的第偶數組（第 2、4…組）。
            public bool UsesAlternateColor { get; set; }

            // 排版時先放「第幾列」（可以是 .5），算出尺寸後換成像素。
            public float CenterY { get; set; }

            public RectangleF Bounds { get; set; }

            // 名稱的起點（圖上的 X 座標）；同一層都一樣。
            public float NameLeft { get; set; }
        }

        /// <summary>
        /// 把選單樹畫到指定的 <see cref="PictureBox"/>；舊的圖會被釋放。
        /// </summary>
        /// <param name="pictureBox">要顯示圖的控制項；建議 <see cref="PictureBox.SizeMode"/> 設成 AutoSize 並放在可捲動的容器裡。</param>
        /// <param name="tree">要畫的樹；null 或空樹時畫成空白。</param>
        /// <param name="isChecked">判斷節點是否勾選；null 時不畫勾選狀態，<paramref name="uncheckedDisplay"/> 也不使用。</param>
        /// <param name="scope">要畫樹的哪個範圍。</param>
        /// <param name="uncheckedDisplay">有勾選資訊時，沒勾的節點怎麼呈現。</param>
        /// <param name="selectedNode">
        /// 目前選取的節點，要屬於 <paramref name="tree"/>；<see cref="CascadeDiagramDrawScope.SelectedSubtree"/> 從它開始畫，
        /// <see cref="CascadeDiagramDrawScope.WholeTree"/> 在它的外框外圍另外畫一圈矩形紅框。null 或不屬於 <paramref name="tree"/> 時當成沒有選取。
        /// </param>
        /// <param name="isExpanded">判斷有子節點的節點是否展開；收合的節點畫 [+]、不畫子節點。null 時全部展開。</param>
        /// <param name="showIndex">是否在節點名稱前畫出同層編號（如 &lt;1&gt;，補零到同層節點數的位數，12 個兄弟時是 &lt;01&gt;）；預設為 false。</param>
        /// <param name="showChildCount">是否在有子節點的節點名稱後畫出子節點數量：沒有勾選資訊時是 (3)，有 isChecked 時是 (勾選數/數量)，如 (1/3)；子節點數為 0 時不顯示，預設為 false。</param>
        public static void Draw(
            PictureBox pictureBox,
            MenuTree tree,
            Func<MenuNode, bool> isChecked = null,
            CascadeDiagramDrawScope scope = CascadeDiagramDrawScope.WholeTree,
            CascadeDiagramUncheckedDisplay uncheckedDisplay = CascadeDiagramUncheckedDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null,
            bool showIndex = false,
            bool showChildCount = false)
        {
            if (pictureBox == null) throw new ArgumentNullException(nameof(pictureBox));

            var bitmap = Render(tree, pictureBox.Font ?? SystemFonts.DefaultFont, isChecked, scope, uncheckedDisplay, selectedNode, isExpanded, showIndex, showChildCount);

            var oldImage = pictureBox.Image;
            pictureBox.Image = bitmap;
            oldImage?.Dispose();
        }

        /// <summary>
        /// 把選單樹畫成 <see cref="Bitmap"/>，大小依內容決定。
        /// </summary>
        /// <param name="tree">
        /// 要畫的樹；null、空樹，或 <see cref="CascadeDiagramUncheckedDisplay.Hidden"/> 下沒有節點可畫時，回傳 1×1 的空白圖。
        /// </param>
        /// <param name="font">文字的字型；null 時使用系統預設字型。</param>
        /// <param name="isChecked">判斷節點是否勾選；null 時不畫勾選狀態，<paramref name="uncheckedDisplay"/> 也不使用。</param>
        /// <param name="scope">要畫樹的哪個範圍。</param>
        /// <param name="uncheckedDisplay">有勾選資訊時，沒勾的節點怎麼呈現。</param>
        /// <param name="selectedNode">
        /// 目前選取的節點，要屬於 <paramref name="tree"/>；<see cref="CascadeDiagramDrawScope.SelectedSubtree"/> 從它開始畫，
        /// <see cref="CascadeDiagramDrawScope.WholeTree"/> 在它的外框外圍另外畫一圈矩形紅框。null 或不屬於 <paramref name="tree"/> 時當成沒有選取。
        /// </param>
        /// <param name="isExpanded">判斷有子節點的節點是否展開；收合的節點畫 [+]、不畫子節點。null 時全部展開。</param>
        /// <param name="showIndex">是否在節點名稱前畫出同層編號（如 &lt;1&gt;，補零到同層節點數的位數，12 個兄弟時是 &lt;01&gt;）；預設為 false。</param>
        /// <param name="showChildCount">是否在有子節點的節點名稱後畫出子節點數量：沒有勾選資訊時是 (3)，有 isChecked 時是 (勾選數/數量)，如 (1/3)；子節點數為 0 時不顯示，預設為 false。</param>
        public static Bitmap Render(
            MenuTree tree,
            Font font,
            Func<MenuNode, bool> isChecked = null,
            CascadeDiagramDrawScope scope = CascadeDiagramDrawScope.WholeTree,
            CascadeDiagramUncheckedDisplay uncheckedDisplay = CascadeDiagramUncheckedDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null,
            bool showIndex = false,
            bool showChildCount = false)
        {
            return RenderImage(tree, font, isChecked, scope, uncheckedDisplay, selectedNode, isExpanded, showIndex, showChildCount).Image;
        }

        /// <summary>
        /// 和 <see cref="Render"/> 相同，另外帶回每個節點方塊與展開鈕在圖上的位置，用來找出滑鼠點到哪個節點或哪個展開鈕。
        /// </summary>
        /// <param name="tree">要畫的樹；沒有東西可畫時，圖是 1×1 的空白、沒有任何節點。</param>
        /// <param name="font">文字的字型；null 時使用系統預設字型。</param>
        /// <param name="isChecked">判斷節點是否勾選；null 時不畫勾選狀態，<paramref name="uncheckedDisplay"/> 也不使用。</param>
        /// <param name="scope">要畫樹的哪個範圍。</param>
        /// <param name="uncheckedDisplay">有勾選資訊時，沒勾的節點怎麼呈現。</param>
        /// <param name="selectedNode">目前選取的節點，規則同 <see cref="Render"/>。</param>
        /// <param name="isExpanded">判斷有子節點的節點是否展開；收合的節點畫 [+]、不畫子節點。null 時全部展開。</param>
        /// <param name="showIndex">是否在節點名稱前畫出同層編號（如 &lt;1&gt;，補零到同層節點數的位數，12 個兄弟時是 &lt;01&gt;）；預設為 false。</param>
        /// <param name="showChildCount">是否在有子節點的節點名稱後畫出子節點數量：沒有勾選資訊時是 (3)，有 isChecked 時是 (勾選數/數量)，如 (1/3)；子節點數為 0 時不顯示，預設為 false。</param>
        /// <returns>圖與節點位置；用完要 Dispose。</returns>
        public static TreeImage RenderImage(
            MenuTree tree,
            Font font,
            Func<MenuNode, bool> isChecked = null,
            CascadeDiagramDrawScope scope = CascadeDiagramDrawScope.WholeTree,
            CascadeDiagramUncheckedDisplay uncheckedDisplay = CascadeDiagramUncheckedDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null,
            bool showIndex = false,
            bool showChildCount = false)
        {
            return RenderImageWithPlaceholder(tree, font, isChecked, scope, uncheckedDisplay, selectedNode, isExpanded, placeholder: null, showIndex: showIndex, showChildCount: showChildCount);
        }

        /// <summary>
        /// 和 <see cref="RenderImage"/> 相同，另外在 <paramref name="placeholder"/> 指定的位置畫一個空白方塊；它的父節點沒有畫出來時不畫。
        /// </summary>
        internal static TreeImage RenderImageWithPlaceholder(
            MenuTree tree,
            Font font,
            Func<MenuNode, bool> isChecked,
            CascadeDiagramDrawScope scope,
            CascadeDiagramUncheckedDisplay uncheckedDisplay,
            MenuNode selectedNode,
            Func<MenuNode, bool> isExpanded,
            Placeholder placeholder,
            MovePreview preview = null,
            float zoom = 1f,
            bool showIndex = false,
            bool showChildCount = false,
            Func<MenuNode, bool> isCut = null)
        {
            font = font ?? SystemFonts.DefaultFont;
            if (tree == null || selectedNode == null || !selectedNode.IsDescendantOf(tree.Root)) selectedNode = null;

            var options = new BuildOptions
            {
                IsChecked = isChecked,

                // 沒有勾選資訊時每個節點都當成勾選，不會被隱藏。
                HideUnchecked = isChecked != null && uncheckedDisplay == CascadeDiagramUncheckedDisplay.Hidden,
                SelectedNode = selectedNode,
                Placeholder = placeholder,
                Preview = preview,
                IsCut = isCut,
            };

            var roots = tree == null || tree.IsEmpty ? new List<LayoutNode>() : BuildLayout(tree, scope, options);
            if (roots.Count == 0) return new TreeImage(CreateBlank(), new TreeImageNode[0]);

            // 預覽時新的父節點一定展開，才看得到接上去的節點。
            if (isExpanded != null)
            {
                foreach (var root in roots) Collapse(root, preview == null ? isExpanded : node => ReferenceEquals(node, preview.Parent) || isExpanded(node));
            }

            var showChecks = isChecked != null;

            var nodes = Flatten(roots);
            AssignColorGroups(nodes);
            AssignTexts(nodes, showIndex, showChildCount);

            using (var format = CreateTextFormat())
            {
                SizeF size;
                using (var scratch = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(scratch))
                {
                    size = Arrange(g, font, format, roots, nodes);
                }

                var scale = Math.Min(zoom, Math.Min(MAX_BITMAP_SIDE / size.Width, MAX_BITMAP_SIDE / size.Height));
                var bitmap = new Bitmap(
                    Math.Max(1, (int)Math.Ceiling(size.Width * scale)),
                    Math.Max(1, (int)Math.Ceiling(size.Height * scale)));

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (scale != 1f) g.ScaleTransform(scale, scale);

                    DrawLinks(g, nodes, showChecks, translucent: false);
                    foreach (var node in nodes.Where(node => !node.IsTranslucent)) DrawNode(g, font, format, node, showChecks);

                    if (nodes.Any(node => node.IsTranslucent)) DrawTranslucent(g, bitmap.Size, scale, font, format, nodes, showChecks);
                }

                var hitNodes = nodes
                    .Select(node => new TreeImageNode(
                        node.Source,
                        Scale(node.Bounds, scale),
                        HasExpander(node) ? Scale(ExpanderBounds(node), scale) : RectangleF.Empty,
                        node.IsExpanded,
                        node.IsGhost,
                        node.IsLeaving))
                    .ToList();

                return new TreeImage(bitmap, hitNodes);
            }
        }

        private static RectangleF Scale(RectangleF bounds, float scale)
        {
            return new RectangleF(bounds.X * scale, bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
        }

        private static Bitmap CreateBlank()
        {
            var bitmap = new Bitmap(1, 1);
            bitmap.SetPixel(0, 0, Color.White);
            return bitmap;
        }

        private static StringFormat CreateTextFormat()
        {
            return new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces,
                LineAlignment = StringAlignment.Center,
            };
        }

        #region 建立要畫的節點

        /// <summary>
        /// 決定起點並建立要畫的節點，回傳最左欄的節點；沒有東西可畫時回傳空的清單。
        /// </summary>
        /// <remarks>
        /// <see cref="CascadeDiagramDrawScope.SelectedSubtree"/> 有選取時從選取的節點開始，它就是圖的起點，不另外框起來。
        /// </remarks>
        private static List<LayoutNode> BuildLayout(MenuTree tree, CascadeDiagramDrawScope scope, BuildOptions options)
        {
            var selectedNode = options.SelectedNode;
            if (scope == CascadeDiagramDrawScope.SelectedSubtree && selectedNode != null)
            {
                // 起點就是選取的節點，不另外框起來。
                options.SelectedNode = null;
                var start = BuildNode(selectedNode, 0, selectedNode.Depth, ghost: false, leaving: false, cut: false, options: options);
                if (start != null)
                {
                    start.Number = selectedNode.SiblingNumber;
                    start.SiblingCount = selectedNode.Parent?.Children.Count ?? 1;
                }
                return start == null ? new List<LayoutNode>() : new List<LayoutNode> { start };
            }

            // 只用來收集第一層節點（連同第一層的佔位方塊與預覽），本身不畫。
            var top = new LayoutNode { Source = tree.Root, Level = -1, Depth = 0 };
            AddChildren(top, options);
            return top.Children;
        }

        // 回傳 null 表示這個節點不畫（隱藏沒勾的節點時）。
        // depth 決定色系：一般就是原樹的深度；預覽移動時接到新位置的節點跟著新的父節點。
        private static LayoutNode BuildNode(MenuNode source, int level, int depth, bool ghost, bool leaving, bool cut, BuildOptions options)
        {
            var isNodeChecked = options.IsChecked == null || options.IsChecked(source);
            if (!isNodeChecked && options.HideUnchecked) return null;

            var preview = options.Preview;
            bool isSelected;
            if (preview == null)
            {
                isSelected = ReferenceEquals(source, options.SelectedNode);
            }
            else
            {
                var selected = preview.FocusedNode ?? options.SelectedNode;
                if (selected != null && preview.IsInMovedSubtree(selected))
                    isSelected = ghost && ReferenceEquals(source, selected);
                else
                    isSelected = !ghost && !leaving && ReferenceEquals(source, selected);
            }

            var isCut = !ghost && !leaving && (cut || (options.IsCut != null && options.IsCut(source)));
            var node = new LayoutNode
            {
                Source = source,
                Level = level,
                Depth = depth,
                IsChecked = isNodeChecked,
                IsSelected = isSelected,
                IsGhost = ghost,
                IsLeaving = leaving,
                IsCut = isCut,
            };
            AddChildren(node, options);
            node.IsFullyChecked &= isNodeChecked;
            return node;
        }

        // 加入子節點，並順便算出「子孫是否全勾」；被略過的子節點一定沒勾，所以父節點不算全勾。
        // 佔位方塊排在原樹第 Position 個子節點之前（隱藏沒勾的節點時照樣對得上），不算進全勾。
        // 預覽移動或複製時：在目標父節點的 InsertIndex 插入半透明的預覽節點；移動模式時原處標成會被移走，其他節點的編號照結果重算。
        private static void AddChildren(LayoutNode parent, BuildOptions options)
        {
            var placeholder = options.Placeholder;
            var placeholderHere = placeholder != null && ReferenceEquals(placeholder.Parent, parent.Source) ? placeholder : null;
            var preview = options.Preview;

            var allFullyChecked = true;
            var number = 0;
            var checkedCount = 0;
            var children = parent.Source.Children;
            var insertPreviewHere = preview != null && !parent.IsGhost && !parent.IsLeaving && ReferenceEquals(preview.Parent, parent.Source);
            var insertIndex = insertPreviewHere ? Math.Max(0, Math.Min(preview.InsertIndex, children.Count)) : -1;

            void AddGhosts()
            {
                foreach (var moved in preview.Nodes)
                {
                    number++;
                    if (options.IsChecked != null && options.IsChecked(moved)) checkedCount++;
                    var ghost = BuildNode(moved, parent.Level + 1, parent.Depth + 1, ghost: true, leaving: false, cut: false, options: options);
                    if (ghost == null) continue;

                    ghost.Number = number;
                    parent.Children.Add(ghost);
                    allFullyChecked &= ghost.IsFullyChecked;
                }
            }

            for (var i = 0; i < children.Count; i++)
            {
                if (placeholderHere != null && i == placeholderHere.Position) parent.Children.Add(CreatePlaceholder(parent, placeholderHere));
                if (insertPreviewHere && i == insertIndex) AddGhosts();

                // 被移走的節點不佔編號；整棵被移走的子樹裡，子孫照原樹的順序編號。
                var movedAway = preview != null && !preview.IsCopy && !parent.IsGhost && !parent.IsLeaving && preview.IsMoved(children[i]);
                if (!movedAway)
                {
                    number++;
                    if (options.IsChecked != null && options.IsChecked(children[i])) checkedCount++;
                }

                var child = BuildNode(children[i], parent.Level + 1, parent.Depth + 1, parent.IsGhost, parent.IsLeaving || movedAway, parent.IsCut, options);
                if (child != null)
                {
                    child.Number = movedAway ? children[i].SiblingNumber : number;
                    parent.Children.Add(child);
                }

                allFullyChecked &= child != null && child.IsFullyChecked;
            }

            if (placeholderHere != null && placeholderHere.Position >= children.Count) parent.Children.Add(CreatePlaceholder(parent, placeholderHere));
            if (insertPreviewHere && insertIndex >= children.Count) AddGhosts();

            parent.ChildCount = number;
            parent.CheckedChildCount = options.IsChecked == null ? (int?)null : checkedCount;
            parent.IsFullyChecked = allFullyChecked;

            // 會被移走的節點保留原樹的編號，補零的位數也照原樹的子節點數。
            foreach (var child in parent.Children)
                child.SiblingCount = child.IsLeaving && !parent.IsLeaving ? children.Count : number;
        }

        private static LayoutNode CreatePlaceholder(LayoutNode parent, Placeholder placeholder)
        {
            return new LayoutNode
            {
                Source = placeholder.Node,
                Level = parent.Level + 1,
                Depth = parent.Depth + 1,
                IsPlaceholder = true,

                // 新節點建立時沒有勾選：有勾選資訊時連線照沒勾的節點變淡。方塊本身另外畫成白底虛線框。
                IsChecked = false,
            };
        }

        /// <summary>
        /// 收合的節點拿掉子節點，不畫它們。
        /// </summary>
        /// <remarks>
        /// 在 <see cref="BuildNode"/> 算完 <see cref="LayoutNode.IsFullyChecked"/> 之後才拿掉，
        /// 所以收合的節點照樣依原樹的子孫決定外框是實線或虛線。
        /// </remarks>
        private static void Collapse(LayoutNode node, Func<MenuNode, bool> isExpanded)
        {
            if (HasExpander(node) && !isExpanded(node.Source))
            {
                node.IsExpanded = false;
                node.Children.Clear();
                return;
            }

            foreach (var child in node.Children) Collapse(child, isExpanded);
        }

        // 前序走訪，父節點在子節點之前。
        private static List<LayoutNode> Flatten(List<LayoutNode> roots)
        {
            var nodes = new List<LayoutNode>();
            var stack = new Stack<LayoutNode>();
            for (var i = roots.Count - 1; i >= 0; i--) stack.Push(roots[i]);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                nodes.Add(node);
                for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }

            return nodes;
        }

        /// <summary>
        /// 決定每個節點用 A 色還是 B 色：同一欄由上往下，同一個父節點的子節點算一組，各組輪流。
        /// </summary>
        /// <remarks>
        /// <paramref name="nodes"/> 是前序走訪，同一欄的父節點由上往下出現，所以它們的子節點組在下一欄也是由上往下。
        /// 最左欄的節點算一組，用 A 色。分組看的是畫出來的節點，隱藏沒勾的節點後，相鄰兩組照樣分得出來。
        /// </remarks>
        private static void AssignColorGroups(List<LayoutNode> nodes)
        {
            var groupCounts = new Dictionary<int, int>();
            foreach (var parent in nodes)
            {
                if (parent.Children.Count == 0) continue;

                var level = parent.Level + 1;
                groupCounts.TryGetValue(level, out var count);
                foreach (var child in parent.Children) child.UsesAlternateColor = count % 2 == 1;
                groupCounts[level] = count + 1;
            }
        }

        // 決定每個節點的編號與名稱文字（規則見 CascadeNodeText）；排版量寬度與繪製都直接用這裡的結果。
        private static void AssignTexts(List<LayoutNode> nodes, bool showIndex, bool showChildCount)
        {
            foreach (var node in nodes.Where(node => !node.IsPlaceholder))
            {
                if (showIndex) node.NumberText = CascadeNodeText.Number(node.Number, node.SiblingCount);
                node.NameText = CascadeNodeText.Name(node.Source.Name, showChildCount, node.ChildCount, node.CheckedChildCount);
            }
        }

        #endregion 建立要畫的節點

        #region 排版

        private static SizeF Arrange(Graphics g, Font font, StringFormat format, List<LayoutNode> roots, List<LayoutNode> nodes)
        {
            var lineHeight = (float)Math.Ceiling(font.GetHeight(g));
            var boxHeight = lineHeight + PADDING_Y * 2f;
            var levelGap = Math.Max(MIN_LEVEL_GAP, boxHeight);

            var numberWidths = new List<float>();
            var nameWidths = new List<float>();
            var hasExpanders = new List<bool>();
            foreach (var node in nodes)
            {
                while (numberWidths.Count <= node.Level)
                {
                    numberWidths.Add(0f);
                    nameWidths.Add(0f);
                    hasExpanders.Add(false);
                }

                hasExpanders[node.Level] |= HasExpander(node);

                numberWidths[node.Level] = Math.Max(numberWidths[node.Level], (float)Math.Ceiling(MeasureWidth(g, font, format, node.NumberText)));
                // 佔位方塊沒有名稱，留 PLACEHOLDER_NAME_LINES 個行高的寬度給蓋在上面的編輯框輸入。
                var nameWidth = node.IsPlaceholder ? lineHeight * PLACEHOLDER_NAME_LINES : MeasureWidth(g, font, format, node.NameText);
                nameWidths[node.Level] = Math.Max(nameWidths[node.Level], (float)Math.Ceiling(nameWidth));
            }

            var levelCount = numberWidths.Count;
            var levelWidths = new float[levelCount];
            var levelLefts = new float[levelCount];
            var nameOffsets = new float[levelCount];
            var x = MARGIN;
            for (var level = 0; level < levelCount; level++)
            {
                nameOffsets[level] = PADDING_X + (numberWidths[level] > 0f ? numberWidths[level] + NUMBER_GAP : 0f);
                var right = hasExpanders[level] ? EXPANDER_GAP + EXPANDER_SIZE + EXPANDER_RIGHT : PADDING_X;
                levelWidths[level] = Math.Max(boxHeight, nameOffsets[level] + nameWidths[level] + right);
                levelLefts[level] = x;
                x += levelWidths[level] + levelGap;
            }

            var rows = 0;
            foreach (var root in roots) AssignRows(root, ref rows);

            var rowPitch = boxHeight + ROW_GAP;
            foreach (var node in nodes)
            {
                var top = MARGIN + node.CenterY * rowPitch;
                node.Bounds = new RectangleF(levelLefts[node.Level], top, levelWidths[node.Level], boxHeight);
                node.NameLeft = levelLefts[node.Level] + nameOffsets[node.Level];
                node.CenterY = top + boxHeight / 2f;
            }

            return new SizeF(
                x - levelGap + MARGIN,
                MARGIN * 2f + rows * rowPitch - ROW_GAP);
        }

        private static void AssignRows(LayoutNode node, ref int nextRow)
        {
            if (node.Children.Count == 0)
            {
                node.CenterY = nextRow++;
                return;
            }

            foreach (var child in node.Children) AssignRows(child, ref nextRow);

            node.CenterY = (node.Children[0].CenterY + node.Children[node.Children.Count - 1].CenterY) / 2f;
        }

        private static float MeasureWidth(Graphics g, Font font, StringFormat format, string text)
        {
            return string.IsNullOrEmpty(text) ? 0f : g.MeasureString(text, font, int.MaxValue, format).Width;
        }

        #endregion 排版

        #region 繪製

        /// <summary>
        /// 畫父子之間的貝茲曲線：由父節點右緣中點到子節點左緣中點，同一個父節點的曲線共用起點。
        /// </summary>
        /// <remarks>
        /// 曲線一律黑色；有勾選資訊時（<paramref name="showChecks"/>），通往沒勾子節點的曲線畫成淺灰，
        /// 通往勾選子節點的畫在最上層，才不會被淺灰的蓋住。
        /// </remarks>
        private static void DrawLinks(Graphics g, List<LayoutNode> nodes, bool showChecks, bool translucent)
        {
            using (var normalPen = new Pen(showChecks ? DIMMED_LINE : Color.Black, 1.2f))
            using (var checkedPen = new Pen(Color.Black, 1.8f))
            {
                foreach (var drawChecked in new[] { false, true })
                {
                    if (drawChecked && !showChecks) break;

                    foreach (var parent in nodes)
                    {
                        var from = new PointF(parent.Bounds.Right, parent.CenterY);

                        foreach (var child in parent.Children)
                        {
                            if (child.IsTranslucent != translucent || (showChecks && child.IsChecked != drawChecked)) continue;

                            var to = new PointF(child.Bounds.Left, child.CenterY);
                            var middle = (from.X + to.X) / 2f;
                            g.DrawBezier(
                                drawChecked ? checkedPen : normalPen,
                                from,
                                new PointF(middle, from.Y),
                                new PointF(middle, to.Y),
                                to);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 畫一個方塊：底色依層與 A/B 色，外框依勾選與選取狀態。
        /// </summary>
        /// <remarks>
        /// 有底色的節點，外框是同色系的深色（A、B 色各自調暗）；沒勾的節點一律白底、灰框、灰字，不分層也不分 A/B 色；
        /// 勾選的節點底色與外框顏色不變，外框加粗，全勾是實線、勾了但子孫沒有全勾是虛線。
        /// 有子節點的方塊在最右邊畫展開鈕（收合 [+]、展開 [-]）。
        /// 選取的節點照樣畫原本的外框，再往外留 <see cref="SELECTED_BORDER_GAP"/> 的空隙另外畫一圈矩形紅框，不蓋住原本的外框；
        /// 預覽移動時會被移走的節點：底色往白色調淡 <see cref="LEAVING_FADE"/>，外框與文字用灰色，外框是虛線，不畫選取框。
        /// </remarks>
        private static void DrawNode(Graphics g, Font font, StringFormat format, LayoutNode node, bool showChecks, bool drawSelectionFrame = true)
        {
            var back = LevelColor(node);
            var border = Darken(back, BORDER_LIGHTNESS_RATIO);
            var fore = Color.Black;
            var borderWidth = BorderWidthOf(node, showChecks);
            var dashStyle = DashStyle.Solid;

            if (node.IsPlaceholder)
            {
                // 還不是節點：白底虛線框，和已經在樹裡的節點分得出來。
                back = Color.White;
                dashStyle = DashStyle.Dash;
            }
            else if (showChecks && !node.IsChecked)
            {
                back = Color.White;
                border = fore = DIMMED;
            }
            else if (showChecks)
            {
                if (!node.IsFullyChecked) dashStyle = DashStyle.Dash;
            }

            if (node.IsLeaving)
            {
                back = Fade(back, LEAVING_FADE);
                border = fore = DIMMED;
                borderWidth = BORDER_WIDTH;
                dashStyle = DashStyle.Dash;
            }

            // 圓角／直角依原樹決定：隱藏沒勾的節點時，子節點全被濾掉的節點仍然是圓角，看得出它原本還有下一層。
            var rounded = node.Source.Children.Count > 0;
            var bounds = node.Bounds;

            using (var path = rounded ? CreateRoundedRectangle(bounds, CORNER_RADIUS) : null)
            using (var backBrush = new SolidBrush(back))
            using (var borderPen = new Pen(border, borderWidth) { DashStyle = dashStyle })
            using (var foreBrush = new SolidBrush(fore))
            {
                if (rounded)
                {
                    g.FillPath(backBrush, path);
                    g.DrawPath(borderPen, path);
                }
                else
                {
                    g.FillRectangle(backBrush, bounds);
                    g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }

                if (node.NumberText.Length > 0)
                {
                    var numberLeft = bounds.Left + PADDING_X;
                    g.DrawString(node.NumberText, font, foreBrush, new RectangleF(numberLeft, bounds.Top, node.NameLeft - numberLeft, bounds.Height), format);
                }

                var nameRight = HasExpander(node) ? ExpanderBounds(node).Left - EXPANDER_GAP : bounds.Right - PADDING_X;
                g.DrawString(node.NameText, font, foreBrush, new RectangleF(node.NameLeft, bounds.Top, nameRight - node.NameLeft, bounds.Height), format);
            }

            if (HasExpander(node)) DrawExpander(g, node, border);
            if (node.IsLeaving) DrawLeavingCross(g, bounds);
            if (drawSelectionFrame && node.IsSelected) DrawSelectionFrame(g, bounds, borderWidth);
        }

        private static float BorderWidthOf(LayoutNode node, bool showChecks) =>
            !node.IsPlaceholder && !node.IsLeaving && showChecks && node.IsChecked ? CHECKED_BORDER_WIDTH : BORDER_WIDTH;

        private const float LEAVING_CROSS_WIDTH = 1.8f;

        private static void DrawLeavingCross(Graphics g, RectangleF bounds)
        {
            var inset = Math.Min(3f, Math.Min(bounds.Width, bounds.Height) / 4f);
            var left = bounds.Left + inset;
            var top = bounds.Top + inset;
            var right = bounds.Right - inset;
            var bottom = bounds.Bottom - inset;

            using (var pen = new Pen(SELECTED_BORDER, LEAVING_CROSS_WIDTH))
            {
                g.DrawLine(pen, left, top, right, bottom);
                g.DrawLine(pen, left, bottom, right, top);
            }
        }

        // 預覽移動時會被移走的節點，底色往白色調淡的比例（0 不變、1 全白）。
        private const float LEAVING_FADE = 0.6f;

        // 往白色調淡：每個 RGB 值往 255 靠近 ratio。
        private static Color Fade(Color color, float ratio) => Color.FromArgb(
            color.R + (int)((255 - color.R) * ratio),
            color.G + (int)((255 - color.G) * ratio),
            color.B + (int)((255 - color.B) * ratio));

        // 拖曳預覽的新位置與待剪下節點的不透明度。
        private const float GHOST_OPACITY = 0.45f;

        /// <summary>
        /// 半透明節點（拖曳預覽的新位置與待剪下的節點）以及通往它們的連線先畫在一張透明圖上，再以 <see cref="GHOST_OPACITY"/> 疊上去：整組一起變淡，重疊的地方不會更深。
        /// </summary>
        private static void DrawTranslucent(Graphics g, Size size, float scale, Font font, StringFormat format, List<LayoutNode> nodes, bool showChecks)
        {
            using (var layer = new Bitmap(size.Width, size.Height))
            {
                using (var lg = Graphics.FromImage(layer))
                {
                    lg.SmoothingMode = g.SmoothingMode;
                    lg.TextRenderingHint = g.TextRenderingHint;
                    if (scale != 1f) lg.ScaleTransform(scale, scale);

                    DrawLinks(lg, nodes, showChecks, translucent: true);
                    foreach (var node in nodes.Where(node => node.IsTranslucent)) DrawNode(lg, font, format, node, showChecks, drawSelectionFrame: false);
                }

                using (var attributes = new ImageAttributes())
                using (var transform = g.Transform)
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = GHOST_OPACITY });
                    g.ResetTransform();
                    g.DrawImage(layer, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
                    g.Transform = transform;
                }
            }

            foreach (var node in nodes.Where(node => node.IsTranslucent && node.IsSelected))
            {
                DrawSelectionFrame(g, node.Bounds, BorderWidthOf(node, showChecks));
            }
        }

        /// <summary>
        /// 在方塊外圍畫矩形紅框（不論方塊是圓角或直角）：往外推到原本外框的外緣再留 <see cref="SELECTED_BORDER_GAP"/>。
        /// </summary>
        /// <param name="borderWidth">原本外框的寬度；外框以方塊邊界為中心，往外凸出一半。</param>
        private static void DrawSelectionFrame(Graphics g, RectangleF bounds, float borderWidth)
        {
            var outset = borderWidth / 2f + SELECTED_BORDER_GAP + SELECTED_BORDER_WIDTH / 2f;
            var frame = RectangleF.Inflate(bounds, outset, outset);

            using (var pen = new Pen(SELECTED_BORDER, SELECTED_BORDER_WIDTH))
            {
                g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
            }
        }

        /// <summary>
        /// 在方塊內的最右邊畫一個正方形的展開鈕：收合時是 [+]、展開時是 [-]；白底，外框與符號用方塊的外框色（沒勾的節點是灰色）。
        /// </summary>
        /// <remarks>座標取整數、關掉反鋸齒畫，線條才不會糊掉。</remarks>
        private static void DrawExpander(Graphics g, LayoutNode node, Color color)
        {
            var box = ExpanderBounds(node);

            var x = (int)Math.Round(box.X);
            var y = (int)Math.Round(box.Y);
            var size = EXPANDER_SIZE - 1;
            var middle = EXPANDER_SIZE / 2;

            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;

            using (var pen = new Pen(color, 1f))
            {
                g.FillRectangle(Brushes.White, x, y, size, size);
                g.DrawRectangle(pen, x, y, size, size);
                g.DrawLine(pen, x + EXPANDER_PADDING, y + middle, x + size - EXPANDER_PADDING, y + middle);
                if (!node.IsExpanded) g.DrawLine(pen, x + middle, y + EXPANDER_PADDING, x + middle, y + size - EXPANDER_PADDING);
            }

            g.SmoothingMode = smoothing;
        }

        // 和圓角一樣依原樹決定：隱藏沒勾的節點時，子節點全被濾掉的節點仍然有展開鈕。
        private static bool HasExpander(LayoutNode node) => node.Source.Children.Count > 0;

        // [+] 的位置：方塊內、離右緣 EXPANDER_RIGHT，垂直置中。
        private static RectangleF ExpanderBounds(LayoutNode node)
        {
            return new RectangleF(
                node.Bounds.Right - EXPANDER_RIGHT - EXPANDER_SIZE, node.CenterY - EXPANDER_SIZE / 2f, EXPANDER_SIZE, EXPANDER_SIZE);
        }

        // 色系依原樹的深度（Root 是 0），不依畫出來的第幾欄，所以兩種範圍下同一層的顏色一樣。
        private static Color LevelColor(LayoutNode node)
        {
            var pair = LEVEL_COLORS[node.Depth % LEVEL_COLORS.Length];
            return node.UsesAlternateColor ? pair[1] : pair[0];
        }

        /// <summary>
        /// 同色系的深色：換成 HSL 後色相與飽和度不變，亮度乘上 <paramref name="lightnessRatio"/>。
        /// </summary>
        /// <remarks>
        /// 不直接把 RGB 各乘一個比例：淺色的 RGB 三個值很接近，等比例調暗會變成灰褐色，看不出是哪個色系。
        /// </remarks>
        private static Color Darken(Color color, float lightnessRatio)
        {
            var hue = color.GetHue();
            var saturation = color.GetSaturation();
            var lightness = color.GetBrightness() * lightnessRatio;

            // HSL 轉回 RGB。
            var chroma = (1f - Math.Abs(2f * lightness - 1f)) * saturation;
            var sector = hue / 60f;
            var x = chroma * (1f - Math.Abs(sector % 2f - 1f));
            var m = lightness - chroma / 2f;

            float r, g, b;
            if (sector < 1f) { r = chroma; g = x; b = 0f; }
            else if (sector < 2f) { r = x; g = chroma; b = 0f; }
            else if (sector < 3f) { r = 0f; g = chroma; b = x; }
            else if (sector < 4f) { r = 0f; g = x; b = chroma; }
            else if (sector < 5f) { r = x; g = 0f; b = chroma; }
            else { r = chroma; g = 0f; b = x; }

            return Color.FromArgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static int ToByte(float value) => Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));

        private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            var diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();

            return path;
        }

        #endregion 繪製
    }
}