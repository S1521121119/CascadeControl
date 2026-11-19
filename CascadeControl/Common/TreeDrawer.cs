using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Common
{
    /// <summary>
    /// <see cref="TreeDrawer"/> 要畫樹的哪個範圍。
    /// </summary>
    public enum TreeDrawScope
    {
        /// <summary>
        /// 畫整棵樹；選取的節點在外框外圍另外畫一圈矩形紅框。
        /// </summary>
        WholeTree,

        /// <summary>
        /// 從選取的節點開始，畫它和它底下的所有節點；選取的節點就是圖的起點，不另外框起來。沒有選取時畫整棵樹。
        /// </summary>
        SelectedSubtree,
    }

    /// <summary>
    /// 有勾選資訊時，沒勾的節點怎麼呈現；沒有勾選資訊時不使用。
    /// </summary>
    public enum UncheckedNodeDisplay
    {
        /// <summary>
        /// 照樣畫出來但變淡：白底、灰框、灰字，不套用每層的底色。
        /// </summary>
        Dimmed,

        /// <summary>
        /// 不畫，剩下的節點重新排版；編號與圓角／直角沿用原樹，才能對回原本的選單。
        /// </summary>
        Hidden,
    }

    /// <summary>
    /// 把 <see cref="MenuTree"/> 畫成由左到右展開的樹：一層一欄，節點是方塊（一行：編號在前、名稱在後，都靠左），
    /// 父子之間用貝茲曲線連接。
    /// </summary>
    /// <remarks>
    /// 有子節點的畫圓角長方形，方塊內的最右邊畫一個正方形的展開鈕（該層因此加寬）：收合時是 [+]、子節點不畫，展開時是 [-]；
    /// 葉節點畫直角長方形。第一層有多個節點時畫出的 Root 永遠展開，不畫展開鈕。同一層的方塊等寬，寬度依字型量出該層最長的編號與名稱決定，且不小於高度；
    /// 同一層的名稱從同一個位置開始，編號位數不同也對得齊。
    /// 圖照實際大小畫，不會為了塞進控制項而縮小；放不下時由外層的捲動容器處理。
    /// <para>
    /// 上色分三件事，彼此不衝突：
    /// 底色依原樹的深度決定色系，同一欄由上往下，同一個父節點的子節點算一組，各組輪流用該色系的 A 色（較淺）與 B 色（較深），
    /// 相鄰兩組的葉節點才分得出來；
    /// 有底色的節點，外框是同色系的深色；
    /// 有勾選資訊時，勾選的節點外框加粗、底色不變（全勾是實線，勾了但子孫沒有全勾是虛線），沒勾的依 <see cref="UncheckedNodeDisplay"/> 變淡或不畫；
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

        // GDI+ 的 Bitmap 單邊太大會配置失敗；超過時整張等比例縮小。
        private const float MAX_BITMAP_SIDE = 16000f;

        private const string ROOT_LABEL = "Root";

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

        private sealed class LayoutNode
        {
            public MenuNode Source { get; set; }
            public int Level { get; set; }
            public List<LayoutNode> Children { get; } = new List<LayoutNode>();

            // 沒有名稱的 Root：只在第一層有多個節點時畫出來，不寫編號。
            public bool IsVirtualRoot { get; set; }

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
        /// 目前選取的節點，要屬於 <paramref name="tree"/>；<see cref="TreeDrawScope.SelectedSubtree"/> 從它開始畫，
        /// <see cref="TreeDrawScope.WholeTree"/> 在它的外框外圍另外畫一圈矩形紅框。null 或不屬於 <paramref name="tree"/> 時當成沒有選取。
        /// </param>
        /// <param name="isExpanded">判斷有子節點的節點是否展開；收合的節點畫 [+]、不畫子節點。null 時全部展開。</param>
        public static void Draw(
            PictureBox pictureBox,
            MenuTree tree,
            Func<MenuNode, bool> isChecked = null,
            TreeDrawScope scope = TreeDrawScope.WholeTree,
            UncheckedNodeDisplay uncheckedDisplay = UncheckedNodeDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null)
        {
            if (pictureBox == null) throw new ArgumentNullException(nameof(pictureBox));

            var bitmap = Render(tree, pictureBox.Font ?? SystemFonts.DefaultFont, isChecked, scope, uncheckedDisplay, selectedNode, isExpanded);

            var oldImage = pictureBox.Image;
            pictureBox.Image = bitmap;
            oldImage?.Dispose();
        }

        /// <summary>
        /// 把選單樹畫成 <see cref="Bitmap"/>，大小依內容決定。
        /// </summary>
        /// <param name="tree">
        /// 要畫的樹；null、空樹，或 <see cref="UncheckedNodeDisplay.Hidden"/> 下起點沒有勾選時，回傳 1×1 的空白圖。
        /// </param>
        /// <param name="font">文字的字型；null 時使用系統預設字型。</param>
        /// <param name="isChecked">判斷節點是否勾選；null 時不畫勾選狀態，<paramref name="uncheckedDisplay"/> 也不使用。</param>
        /// <param name="scope">要畫樹的哪個範圍。</param>
        /// <param name="uncheckedDisplay">有勾選資訊時，沒勾的節點怎麼呈現。</param>
        /// <param name="selectedNode">
        /// 目前選取的節點，要屬於 <paramref name="tree"/>；<see cref="TreeDrawScope.SelectedSubtree"/> 從它開始畫，
        /// <see cref="TreeDrawScope.WholeTree"/> 在它的外框外圍另外畫一圈矩形紅框。null 或不屬於 <paramref name="tree"/> 時當成沒有選取。
        /// </param>
        /// <param name="isExpanded">判斷有子節點的節點是否展開；收合的節點畫 [+]、不畫子節點。null 時全部展開。</param>
        public static Bitmap Render(
            MenuTree tree,
            Font font,
            Func<MenuNode, bool> isChecked = null,
            TreeDrawScope scope = TreeDrawScope.WholeTree,
            UncheckedNodeDisplay uncheckedDisplay = UncheckedNodeDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null)
        {
            return RenderImage(tree, font, isChecked, scope, uncheckedDisplay, selectedNode, isExpanded).Image;
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
        /// <returns>圖與節點位置；用完要 Dispose。</returns>
        public static TreeImage RenderImage(
            MenuTree tree,
            Font font,
            Func<MenuNode, bool> isChecked = null,
            TreeDrawScope scope = TreeDrawScope.WholeTree,
            UncheckedNodeDisplay uncheckedDisplay = UncheckedNodeDisplay.Dimmed,
            MenuNode selectedNode = null,
            Func<MenuNode, bool> isExpanded = null)
        {
            font = font ?? SystemFonts.DefaultFont;
            if (tree == null || !BelongsTo(selectedNode, tree)) selectedNode = null;

            // 沒有勾選資訊時每個節點都當成勾選，不會被隱藏。
            var hideUnchecked = isChecked != null && uncheckedDisplay == UncheckedNodeDisplay.Hidden;

            var root = tree == null || tree.IsEmpty ? null : BuildLayout(tree, isChecked, scope, hideUnchecked, selectedNode);
            if (root == null) return new TreeImage(CreateBlank(), new TreeImageNode[0]);
            if (isExpanded != null) Collapse(root, isExpanded);

            var showChecks = isChecked != null;

            var nodes = Flatten(root);
            AssignColorGroups(nodes);

            using (var format = CreateTextFormat())
            {
                SizeF size;
                using (var scratch = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(scratch))
                {
                    size = Arrange(g, font, format, root, nodes);
                }

                var scale = Math.Min(1f, Math.Min(MAX_BITMAP_SIDE / size.Width, MAX_BITMAP_SIDE / size.Height));
                var bitmap = new Bitmap(
                    Math.Max(1, (int)Math.Ceiling(size.Width * scale)),
                    Math.Max(1, (int)Math.Ceiling(size.Height * scale)));

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (scale < 1f) g.ScaleTransform(scale, scale);

                    DrawLinks(g, nodes, showChecks);
                    foreach (var node in nodes) DrawNode(g, font, format, node, showChecks);
                }

                // 位置跟著縮小的比例換算成圖上的像素；Root 不對應任何節點，不放進去。
                var hitNodes = nodes
                    .Where(node => !node.IsVirtualRoot)
                    .Select(node => new TreeImageNode(
                        node.Source,
                        Scale(node.Bounds, scale),
                        HasExpander(node) ? Scale(ExpanderBounds(node), scale) : RectangleF.Empty,
                        node.IsExpanded))
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
        /// 決定起點並建立要畫的節點；沒有東西可畫時回傳 null。
        /// </summary>
        /// <remarks>
        /// <see cref="TreeDrawScope.SelectedSubtree"/> 有選取時從選取的節點開始，它就是圖的起點，不另外框起來。
        /// 其他情況畫整棵樹：第一層只有一個節點時直接從它開始；有多個時以沒有名稱的 Root 當起點，把它們接起來。
        /// </remarks>
        private static LayoutNode BuildLayout(MenuTree tree, Func<MenuNode, bool> isChecked, TreeDrawScope scope, bool hideUnchecked, MenuNode selectedNode)
        {
            if (scope == TreeDrawScope.SelectedSubtree && selectedNode != null) return BuildNode(selectedNode, 0, isChecked, hideUnchecked, null);

            if (tree.TopLevelNodes.Count == 1) return BuildNode(tree.TopLevelNodes[0], 0, isChecked, hideUnchecked, selectedNode);

            var root = new LayoutNode { Source = tree.Root, Level = 0, IsVirtualRoot = true };
            AddChildren(root, isChecked, hideUnchecked, selectedNode);

            // Root 不在勾選的範圍內：底下有勾選就當成勾選，全部都全勾才算全勾。
            root.IsChecked = isChecked == null || tree.TopLevelNodes.Any(isChecked);
            return root.IsChecked || !hideUnchecked ? root : null;
        }

        // 回傳 null 表示這個節點不畫（隱藏沒勾的節點時）。
        private static LayoutNode BuildNode(MenuNode source, int level, Func<MenuNode, bool> isChecked, bool hideUnchecked, MenuNode selectedNode)
        {
            var isNodeChecked = isChecked == null || isChecked(source);
            if (!isNodeChecked && hideUnchecked) return null;

            var node = new LayoutNode
            {
                Source = source,
                Level = level,
                IsChecked = isNodeChecked,
                IsSelected = ReferenceEquals(source, selectedNode),
            };
            AddChildren(node, isChecked, hideUnchecked, selectedNode);
            node.IsFullyChecked &= isNodeChecked;
            return node;
        }

        // 加入子節點，並順便算出「子孫是否全勾」；被略過的子節點一定沒勾，所以父節點不算全勾。
        private static void AddChildren(LayoutNode parent, Func<MenuNode, bool> isChecked, bool hideUnchecked, MenuNode selectedNode)
        {
            var allFullyChecked = true;
            foreach (var source in parent.Source.Children)
            {
                var child = BuildNode(source, parent.Level + 1, isChecked, hideUnchecked, selectedNode);
                if (child != null) parent.Children.Add(child);
                allFullyChecked &= child != null && child.IsFullyChecked;
            }

            parent.IsFullyChecked = allFullyChecked;
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

        // 節點是不是這棵樹裡的（樹根不算）。
        private static bool BelongsTo(MenuNode node, MenuTree tree)
        {
            if (node == null || ReferenceEquals(node, tree.Root)) return false;

            for (var current = node; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, tree.Root)) return true;
            }

            return false;
        }

        // 前序走訪，父節點在子節點之前。
        private static List<LayoutNode> Flatten(LayoutNode root)
        {
            var nodes = new List<LayoutNode>();
            var stack = new Stack<LayoutNode>();
            stack.Push(root);

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
        /// 起點自成一組，用 A 色。分組看的是畫出來的節點，隱藏沒勾的節點後，相鄰兩組照樣分得出來。
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

        #endregion 建立要畫的節點

        #region 排版

        /// <summary>
        /// 算出每個方塊的位置與大小，回傳整張圖需要的尺寸。
        /// </summary>
        /// <remarks>
        /// 垂直方向：每個沒有子節點可畫的方塊各佔一列，父節點置中對齊第一個與最後一個子節點，所以不會重疊。
        /// 水平方向：一層一欄，欄寬是該層最寬的編號加上最寬的名稱再加內距，且不小於方塊高度。
        /// </remarks>
        private static SizeF Arrange(Graphics g, Font font, StringFormat format, LayoutNode root, List<LayoutNode> nodes)
        {
            var lineHeight = (float)Math.Ceiling(font.GetHeight(g));
            var boxHeight = lineHeight + PADDING_Y * 2f;
            var levelGap = Math.Max(MIN_LEVEL_GAP, boxHeight);

            // 每一層最寬的編號與最寬的名稱分開量，名稱才能從同一個位置開始；該層有任何節點要畫 [+] 時，整層都留它的位置。
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

                numberWidths[node.Level] = Math.Max(numberWidths[node.Level], (float)Math.Ceiling(MeasureWidth(g, font, format, NumberText(node))));
                nameWidths[node.Level] = Math.Max(nameWidths[node.Level], (float)Math.Ceiling(MeasureWidth(g, font, format, NameText(node))));
            }

            var levelCount = numberWidths.Count;
            var levelWidths = new float[levelCount];
            var levelLefts = new float[levelCount];
            var nameOffsets = new float[levelCount];
            var x = MARGIN;
            for (var level = 0; level < levelCount; level++)
            {
                // 沒有編號的那一層（Root）不留編號的位置。
                nameOffsets[level] = PADDING_X + (numberWidths[level] > 0f ? numberWidths[level] + NUMBER_GAP : 0f);
                var right = hasExpanders[level] ? EXPANDER_GAP + EXPANDER_SIZE + EXPANDER_RIGHT : PADDING_X;
                levelWidths[level] = Math.Max(boxHeight, nameOffsets[level] + nameWidths[level] + right);
                levelLefts[level] = x;
                x += levelWidths[level] + levelGap;
            }

            var rows = 0;
            AssignRows(root, ref rows);

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

        private static string NameText(LayoutNode node) => node.IsVirtualRoot ? ROOT_LABEL : node.Source.Name;

        private static string NumberText(LayoutNode node) => node.IsVirtualRoot ? string.Empty : $"<{node.Source.SiblingNumber}>";

        #endregion 排版

        #region 繪製

        /// <summary>
        /// 畫父子之間的貝茲曲線：由父節點右緣中點到子節點左緣中點，同一個父節點的曲線共用起點。
        /// </summary>
        /// <remarks>
        /// 曲線一律黑色；有勾選資訊時（<paramref name="showChecks"/>），通往沒勾子節點的曲線畫成淺灰，
        /// 通往勾選子節點的畫在最上層，才不會被淺灰的蓋住。
        /// </remarks>
        private static void DrawLinks(Graphics g, List<LayoutNode> nodes, bool showChecks)
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
                            if (showChecks && child.IsChecked != drawChecked) continue;

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
        /// 選取的節點照樣畫原本的外框，再往外留 <see cref="SELECTED_BORDER_GAP"/> 的空隙另外畫一圈矩形紅框，不蓋住原本的外框。
        /// </remarks>
        private static void DrawNode(Graphics g, Font font, StringFormat format, LayoutNode node, bool showChecks)
        {
            var back = LevelColor(node);
            var border = Darken(back, BORDER_LIGHTNESS_RATIO);
            var fore = Color.Black;
            var borderWidth = BORDER_WIDTH;
            var dashStyle = DashStyle.Solid;

            if (showChecks && !node.IsChecked)
            {
                back = Color.White;
                border = fore = DIMMED;
            }
            else if (showChecks)
            {
                borderWidth = CHECKED_BORDER_WIDTH;
                if (!node.IsFullyChecked) dashStyle = DashStyle.Dash;
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

                // 編號和名稱在同一行，垂直置中；Root 沒有編號，只畫名稱。
                var numberLeft = bounds.Left + PADDING_X;
                if (!node.IsVirtualRoot)
                {
                    g.DrawString(NumberText(node), font, foreBrush, new RectangleF(numberLeft, bounds.Top, node.NameLeft - numberLeft, bounds.Height), format);
                }

                var nameRight = HasExpander(node) ? ExpanderBounds(node).Left - EXPANDER_GAP : bounds.Right - PADDING_X;
                g.DrawString(NameText(node), font, foreBrush, new RectangleF(node.NameLeft, bounds.Top, nameRight - node.NameLeft, bounds.Height), format);
            }

            if (HasExpander(node)) DrawExpander(g, node, border);
            if (node.IsSelected) DrawSelectionFrame(g, bounds, borderWidth);
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
        // Root 不對應真正的節點，永遠展開，不畫展開鈕。
        private static bool HasExpander(LayoutNode node) => !node.IsVirtualRoot && node.Source.Children.Count > 0;

        // [+] 的位置：方塊內、離右緣 EXPANDER_RIGHT，垂直置中。
        private static RectangleF ExpanderBounds(LayoutNode node)
        {
            return new RectangleF(
                node.Bounds.Right - EXPANDER_RIGHT - EXPANDER_SIZE, node.CenterY - EXPANDER_SIZE / 2f, EXPANDER_SIZE, EXPANDER_SIZE);
        }

        // 色系依原樹的深度（Root 是 0），不依畫出來的第幾欄，所以兩種範圍下同一層的顏色一樣。
        private static Color LevelColor(LayoutNode node)
        {
            var pair = LEVEL_COLORS[node.Source.Depth % LEVEL_COLORS.Length];
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