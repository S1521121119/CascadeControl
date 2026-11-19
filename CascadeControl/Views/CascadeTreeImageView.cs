using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeTreeImageView"/> 的圖怎麼放進控制項。
    /// </summary>
    public enum TreeImageSizeMode
    {
        /// <summary>
        /// 照原始尺寸畫，放不下時出現水平／垂直捲軸。
        /// </summary>
        ActualSize,

        /// <summary>
        /// 維持長寬比例縮小到整張圖都放得進控制項，不出現捲軸；圖本來就放得下時照原始尺寸畫，不放大。
        /// </summary>
        Zoom,
    }

    /// <summary>
    /// 把 <see cref="View"/> 目前顯示的選單樹用 <see cref="TreeDrawer"/> 畫出來，跟著選取與勾選自動重畫；點圖上的節點可以知道點到哪一個。
    /// </summary>
    /// <remarks>
    /// 取代「可捲動的 Panel 包一個 AutoSize 的 PictureBox」：控制項自己畫圖、自己管捲軸，所以可以直接 Dock = Fill。
    /// 右鍵功能表可以切換 <see cref="DrawScope"/>、<see cref="SizeMode"/>；<see cref="View"/> 是 <see cref="CascadeCheckboxView"/> 時
    /// 多一組 <see cref="UncheckedDisplay"/>（<see cref="CascadeView"/> 沒有勾選資訊，用不到這個設定，所以不列出來）。
    /// 會自動重畫的時機：<see cref="CascadeView.SelectedPathChanged"/>（包含 <see cref="CascadeView.SetTree"/>）、
    /// <see cref="CascadeCheckboxView.CheckedChanged"/>；樹的結構被編輯但選取沒變，或直接換掉 <see cref="CascadeCheckboxView.CheckSet"/> 時，
    /// 請自己呼叫 <see cref="Redraw"/>。
    /// <para>
    /// 有子節點的節點可以收合：左鍵點方塊右側的 [+] 展開（變成 [-]、畫出子節點），點 [-] 收合；點展開鈕不會觸發 <see cref="NodeMouseClick"/>。
    /// 沒點過的節點依勾選決定：有任何子節點被勾選就展開（勾選子孫會補勾祖先，所以祖先跟著展開），否則收合；
    /// 沒有勾選資訊（<see cref="View"/> 不是 <see cref="CascadeCheckboxView"/>）時一律收合。
    /// 點過的節點記住使用者的選擇，換成另一棵樹時全部忘掉；選取的節點被收合藏起來時不會自動展開。
    /// </para>
    /// <para>
    /// 取得焦點後可以用鍵盤操作，改的是 <see cref="View"/> 的選取，所以「進出記住位置」的記憶和 <see cref="CascadeView"/> 是同一份：
    /// ↑↓ 在圖上看得到的同一層節點之間移動，可以跨父節點，被收合或隱藏的節點跳過；
    /// ← 同 <see cref="CascadeView.StepBack"/>；→ 同 <see cref="CascadeView.StepForward"/>，但選取的節點收合（[+]）時不動作，要先展開；
    /// 空白鍵展開或收合選取的節點；Enter 觸發 <see cref="NodeActivated"/>（和雙擊同樣用途，由訂閱端決定做什麼）。原始尺寸下會捲動到選取的節點。
    /// <see cref="TreeDrawScope.SelectedSubtree"/> 下起點是它那一層唯一畫出來的節點，所以 ↑↓ 不會移動。
    /// </para>
    /// </remarks>
    [DesignerCategory("")]
    [ToolboxItem(true)]
    [DefaultEvent(nameof(NodeMouseClick))]
    public class CascadeTreeImageView : ScrollableControl
    {
        private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();

        private CascadeView _view;
        private TreeImage _image;
        private TreeDrawScope _drawScope = TreeDrawScope.WholeTree;
        private UncheckedNodeDisplay _uncheckedDisplay = UncheckedNodeDisplay.Dimmed;
        private TreeImageSizeMode _sizeMode = TreeImageSizeMode.ActualSize;
        private string _pathSeparator = CascadeNavigator.DEFAULT_PATH_SEPARATOR;

        // 使用者點過展開鈕的節點；沒有的依勾選決定。屬於 _expandTree，換樹時清空。
        private readonly Dictionary<MenuNode, bool> _expandOverrides = new Dictionary<MenuNode, bool>();

        private MenuTree _expandTree;

        /// <summary>
        /// 建立控制項。
        /// </summary>
        public CascadeTreeImageView()
        {
            // Selectable：點一下就取得焦點，滾輪才捲得動。
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
            AutoScroll = true;

            _contextMenu.Opening += ContextMenu_Opening;
            ContextMenuStrip = _contextMenu;

            UpdateImage(null);
        }

        #region 設定

        /// <summary>
        /// 要畫哪一個 <see cref="CascadeView"/> 的樹；樹、選取與勾選都取自它。null 時畫成空白。
        /// </summary>
        [Category("Cascade")]
        [Description("要畫哪一個 CascadeView 的樹；樹、選取與勾選都取自它。")]
        [DefaultValue(null)]
        public CascadeView View
        {
            get => _view;
            set
            {
                if (ReferenceEquals(_view, value)) return;

                if (_view != null) Detach(_view);
                _view = value;
                if (_view != null) Attach(_view);

                Redraw();
            }
        }

        /// <summary>
        /// 要畫樹的哪個範圍。
        /// </summary>
        [Category("Cascade")]
        [Description("要畫樹的哪個範圍：整棵樹，或從選取的節點開始。")]
        [DefaultValue(TreeDrawScope.WholeTree)]
        public TreeDrawScope DrawScope
        {
            get => _drawScope;
            set
            {
                if (_drawScope == value) return;
                _drawScope = value;
                Redraw();
                OnDrawSettingsChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// 有勾選資訊（<see cref="View"/> 是 <see cref="CascadeCheckboxView"/>）時，沒勾的節點怎麼呈現。
        /// </summary>
        [Category("Cascade")]
        [Description("View 是 CascadeCheckboxView 時，沒勾的節點變淡或不畫。")]
        [DefaultValue(UncheckedNodeDisplay.Dimmed)]
        public UncheckedNodeDisplay UncheckedDisplay
        {
            get => _uncheckedDisplay;
            set
            {
                if (_uncheckedDisplay == value) return;
                _uncheckedDisplay = value;
                Redraw();
                OnDrawSettingsChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// 圖怎麼放進控制項：原始尺寸（放不下時出現捲軸）或維持長寬比例縮小到整張看得到。
        /// </summary>
        [Category("Cascade")]
        [Description("ActualSize：原始尺寸，放不下時出現捲軸；Zoom：維持長寬比例縮小到整張圖看得到。")]
        [DefaultValue(TreeImageSizeMode.ActualSize)]
        public TreeImageSizeMode SizeMode
        {
            get => _sizeMode;
            set
            {
                if (_sizeMode == value) return;
                _sizeMode = value;
                UpdateScrollSize();
                Invalidate();
                OnDrawSettingsChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// <see cref="CascadeTreeImageNodeEventArgs.PathText"/> 使用的層級分隔字串。
        /// </summary>
        [Category("Cascade")]
        [Description("NodeMouseClick 事件參數 PathText 使用的層級分隔字串。")]
        [DefaultValue(CascadeNavigator.DEFAULT_PATH_SEPARATOR)]
        public string PathSeparator
        {
            get => _pathSeparator;
            set => _pathSeparator = value ?? string.Empty;
        }

        /// <summary>
        /// 右鍵功能表。每次開啟前會重建內建項目；要加自己的項目，請訂閱它的 Opening（在控制項之後訂閱的會在內建項目建好後才執行）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ContextMenuStrip TreeContextMenu => _contextMenu;

        /// <summary>
        /// 目前畫出的圖（原始尺寸）；沒有東西可畫時是 1×1 的空白圖。不要 Dispose 它，重畫時控制項會自己釋放。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image Image => _image?.Image;

        #endregion 設定

        #region 事件

        /// <summary>
        /// 用滑鼠點了圖上的某個節點之後觸發（左鍵、右鍵都會）；點在空白處或 Root 上不會觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeTreeImageNodeMouseEventArgs> NodeMouseClick;

        /// <summary>
        /// 在圖上的某個節點雙擊之後觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeTreeImageNodeMouseEventArgs> NodeMouseDoubleClick;

        /// <summary>
        /// 焦點在樹圖上時，在選取的節點上按 Enter 之後觸發；用途和 <see cref="NodeMouseDoubleClick"/> 相同，
        /// 讓鍵盤也能「啟動」節點（例如 <see cref="CascadeCheckboxView"/> 切換勾選、<see cref="CascadeView"/> 執行選取）。
        /// 控制項本身不做任何事，由訂閱端決定。沒有選取，或選取的節點被收合藏起來時不觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeTreeImageNodeEventArgs> NodeActivated;

        /// <summary>
        /// <see cref="DrawScope"/>、<see cref="UncheckedDisplay"/> 或 <see cref="SizeMode"/> 改變後觸發，包含從右鍵功能表切換。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler DrawSettingsChanged;

        /// <summary>
        /// 觸發 <see cref="NodeMouseClick"/>。
        /// </summary>
        protected virtual void OnNodeMouseClick(CascadeTreeImageNodeMouseEventArgs e) => NodeMouseClick?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeMouseDoubleClick"/>。
        /// </summary>
        protected virtual void OnNodeMouseDoubleClick(CascadeTreeImageNodeMouseEventArgs e) => NodeMouseDoubleClick?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeActivated"/>。
        /// </summary>
        protected virtual void OnNodeActivated(CascadeTreeImageNodeEventArgs e) => NodeActivated?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="DrawSettingsChanged"/>。
        /// </summary>
        protected virtual void OnDrawSettingsChanged(EventArgs e) => DrawSettingsChanged?.Invoke(this, e);

        #endregion 事件

        #region 繪製

        /// <summary>
        /// 依 <see cref="View"/> 目前的樹、選取與勾選重畫。
        /// </summary>
        /// <remarks>
        /// <see cref="TreeDrawScope.SelectedSubtree"/> 沒有選取時畫成空白；<see cref="View"/> 是用 <see cref="CascadeView.SetTopLevelNodes"/>
        /// 顯示其他資料（<see cref="CascadeView.Tree"/> 為 null）時也畫成空白。
        /// </remarks>
        public void Redraw()
        {
            var tree = _view?.Tree;
            SyncExpandTree();

            var selected = _view?.SelectedNode as MenuNode;
            if (_drawScope == TreeDrawScope.SelectedSubtree && selected == null) tree = null;

            var checkSet = (_view as CascadeCheckboxView)?.CheckSet;
            var isChecked = checkSet == null ? (Func<MenuNode, bool>)null : checkSet.IsChecked;

            UpdateImage(TreeDrawer.RenderImage(tree, Font, isChecked, _drawScope, _uncheckedDisplay, selected, IsExpanded));
        }

        /// <summary>
        /// 有子節點的節點是否展開。
        /// </summary>
        /// <param name="node">要查詢的節點。</param>
        /// <returns>
        /// 使用者點過展開鈕（或呼叫過 <see cref="SetExpanded"/>）的節點照那次的設定；
        /// 其他的節點有任何子節點被勾選時為 true，沒有勾選資訊時為 false。null 回傳 false。
        /// </returns>
        public bool IsExpanded(MenuNode node)
        {
            if (node == null) return false;

            SyncExpandTree();
            if (_expandOverrides.TryGetValue(node, out var expanded)) return expanded;

            var checkSet = (_view as CascadeCheckboxView)?.CheckSet;
            return checkSet != null && node.Children.Any(checkSet.IsChecked);
        }

        /// <summary>
        /// 展開或收合節點並重畫；之後不再依勾選自動決定，直到換成另一棵樹。
        /// </summary>
        /// <param name="node">要展開或收合的節點。</param>
        /// <param name="expanded">true 展開、false 收合。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public void SetExpanded(MenuNode node, bool expanded)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            SyncExpandTree();
            _expandOverrides[node] = expanded;
            Redraw();
        }

        // View 換成另一棵樹（或沒有樹）時，忘掉使用者點過的展開狀態。
        private void SyncExpandTree()
        {
            var tree = _view?.Tree;
            if (ReferenceEquals(tree, _expandTree)) return;

            _expandOverrides.Clear();
            _expandTree = tree;
        }

        private void UpdateImage(TreeImage image)
        {
            var old = _image;
            _image = image ?? TreeDrawer.RenderImage(null, Font);
            old?.Dispose();

            UpdateScrollSize();
            Invalidate();
        }

        private void UpdateScrollSize()
        {
            AutoScrollMinSize = _sizeMode == TreeImageSizeMode.ActualSize && _image != null ? _image.Image.Size : Size.Empty;
        }

        /// <summary>
        /// 圖畫在控制項上的縮放比例與位置：控制項座標 = 圖座標 × <paramref name="scale"/> + <paramref name="offset"/>。
        /// </summary>
        private void GetTransform(out float scale, out PointF offset)
        {
            if (_sizeMode == TreeImageSizeMode.ActualSize || _image == null)
            {
                scale = 1f;
                offset = AutoScrollPosition;
                return;
            }

            // 自動縮放只縮小不放大；控制項被縮到 0 時照原始尺寸算，避免除以 0。
            var size = _image.Image.Size;
            var client = ClientSize;
            scale = Math.Min(1f, Math.Min(client.Width / (float)size.Width, client.Height / (float)size.Height));
            if (scale <= 0f) scale = 1f;
            offset = PointF.Empty;
        }

        /// <inheritdoc />
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null) return;

            GetTransform(out var scale, out var offset);
            var size = _image.Image.Size;

            if (scale < 1f)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }

            e.Graphics.DrawImage(_image.Image, new RectangleF(offset.X, offset.Y, size.Width * scale, size.Height * scale));
        }

        /// <inheritdoc />
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Redraw();
        }

        #endregion 繪製

        #region 滑鼠

        /// <summary>
        /// 控制項上的某一點落在哪個節點方塊裡，已考慮捲動位置與縮放。
        /// </summary>
        /// <param name="clientLocation">控制項的用戶區座標。</param>
        /// <returns>點到的節點；沒點到時為 null。</returns>
        public MenuNode GetNodeAt(Point clientLocation)
        {
            if (_image == null) return null;

            return _image.GetNodeAt(ToImagePoint(clientLocation));
        }

        /// <summary>
        /// 控制項上的某一點落在哪個節點的展開鈕（[+] 或 [-]）上，已考慮捲動位置與縮放。
        /// </summary>
        /// <param name="clientLocation">控制項的用戶區座標。</param>
        /// <returns>展開鈕所屬的節點；沒點到時為 null。</returns>
        public MenuNode GetExpanderAt(Point clientLocation)
        {
            return _image?.GetExpanderAt(ToImagePoint(clientLocation));
        }

        private PointF ToImagePoint(Point clientLocation)
        {
            GetTransform(out var scale, out var offset);
            return new PointF((clientLocation.X - offset.X) / scale, (clientLocation.Y - offset.Y) / scale);
        }

        // 左鍵點在展開鈕上就切換展開狀態；回傳 true 表示點到了展開鈕。
        private bool TryToggleExpander(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;

            var node = GetExpanderAt(e.Location);
            if (node == null) return false;

            SetExpanded(node, !IsExpanded(node));
            return true;
        }

        /// <inheritdoc />
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = GetNodeAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        /// <inheritdoc />
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 要有焦點，滾輪才捲得動。
            Focus();
            base.OnMouseDown(e);
        }

        /// <inheritdoc />
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseClick(CreateMouseEventArgs(node, e));
        }

        /// <inheritdoc />
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            // 連點兩下時第二下只會送 DoubleClick，當成第二次點擊再切換一次。
            if (TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseDoubleClick(CreateMouseEventArgs(node, e));
        }

        private CascadeTreeImageNodeMouseEventArgs CreateMouseEventArgs(MenuNode node, MouseEventArgs e)
        {
            return new CascadeTreeImageNodeMouseEventArgs(node, _pathSeparator, e.Button, e.Location, PointToScreen(e.Location));
        }

        #endregion 滑鼠

        #region 鍵盤

        // 鍵盤移動後捲動到選取的節點時，方塊外圍多留的空白。
        private const int ENSURE_VISIBLE_MARGIN = 12;

        /// <inheritdoc />
        /// <remarks>方向鍵要自己處理，不讓表單拿去切換焦點；Enter 也是，不讓表單的 AcceptButton 搶走。</remarks>
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Enter:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        /// <summary>
        /// ↑↓ 在圖上看得到的同一層節點之間移動（可以跨父節點）；← 退回上一層、→ 進入下一層（記憶和 <see cref="View"/> 共用）；
        /// 空白鍵展開或收合選取的節點；Enter 在選取的節點上觸發 <see cref="NodeActivated"/>。改的是 <see cref="View"/> 的選取，所以兩邊一起動。
        /// </summary>
        /// <param name="e">按鍵資料；有 Ctrl、Alt 或 Shift 時不處理。</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Modifiers != Keys.None || _view == null) return;

            switch (e.KeyCode)
            {
                case Keys.Up: MoveVertical(-1); break;
                case Keys.Down: MoveVertical(1); break;
                case Keys.Left: _view.StepBack(); break;
                case Keys.Right: StepIn(); break;
                case Keys.Space: ToggleSelected(); break;
                case Keys.Enter: ActivateSelected(); break;
                default: return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            EnsureVisible(SelectedMenuNode);
        }

        private MenuNode SelectedMenuNode => _view?.SelectedNode as MenuNode;

        // 節點畫在圖上的那一格；沒畫出來（被收合、隱藏，或不在範圍內）時為 null。
        private TreeImageNode FindImageNode(MenuNode node)
        {
            return node == null || _image == null ? null : _image.Nodes.FirstOrDefault(imageNode => ReferenceEquals(imageNode.Node, node));
        }

        /// <summary>
        /// 選同一層（同一欄）圖上看得到的上一個或下一個節點；到頭不動。未選取或選取的節點不在圖上時，選圖上第一個節點。
        /// </summary>
        /// <remarks><see cref="TreeImage.Nodes"/> 是前序排列，同一層的節點剛好由上往下。</remarks>
        private void MoveVertical(int direction)
        {
            if (_image == null || _image.Nodes.Count == 0) return;

            var current = FindImageNode(SelectedMenuNode);
            if (current == null)
            {
                _view.TrySelectNode(_image.Nodes[0].Node);
                return;
            }

            var column = _image.Nodes.Where(imageNode => imageNode.Node.Depth == current.Node.Depth).ToList();
            var index = column.IndexOf(current) + direction;
            if (index >= 0 && index < column.Count) _view.TrySelectNode(column[index].Node);
        }

        /// <summary>
        /// 進入下一層，照 <see cref="CascadeView.StepForward"/> 的規則走（有記憶走記憶，否則第一個子節點）；
        /// 選取的節點收合（[+]）時不動作，要先按空白鍵展開。
        /// </summary>
        /// <remarks>
        /// 隱藏未勾選的節點時，記憶或第一個子節點可能沒畫出來，改選圖上第一個子節點；子節點全被隱藏時不動作。
        /// </remarks>
        private void StepIn()
        {
            var node = SelectedMenuNode;
            if (node != null && !node.Children.Any(child => FindImageNode(child) != null)) return;

            if (!_view.StepForward() || node == null) return;
            if (FindImageNode(SelectedMenuNode) != null) return;

            var firstDrawn = node.Children.FirstOrDefault(child => FindImageNode(child) != null);
            if (firstDrawn != null) _view.TrySelectNode(firstDrawn);
        }

        // 和雙擊一樣只針對畫在圖上的節點；選取被收合藏起來或沒有選取時不觸發。
        private void ActivateSelected()
        {
            var node = SelectedMenuNode;
            if (FindImageNode(node) != null) OnNodeActivated(new CascadeTreeImageNodeEventArgs(node, _pathSeparator));
        }

        private void ToggleSelected()
        {
            var node = SelectedMenuNode;
            if (node != null && node.Children.Count > 0) SetExpanded(node, !IsExpanded(node));
        }

        /// <summary>
        /// 原始尺寸模式下，捲動到選取節點的方塊完整露出（外圍多留 <see cref="ENSURE_VISIBLE_MARGIN"/>）；自動縮放時整張都看得到，不用捲。
        /// </summary>
        private void EnsureVisible(MenuNode node)
        {
            if (_sizeMode != TreeImageSizeMode.ActualSize) return;

            var imageNode = FindImageNode(node);
            if (imageNode == null) return;

            var bounds = RectangleF.Inflate(imageNode.Bounds, ENSURE_VISIBLE_MARGIN, ENSURE_VISIBLE_MARGIN);
            var client = ClientSize;

            // AutoScrollPosition 讀出來是負的，設定時要給正的。
            var x = -AutoScrollPosition.X;
            var y = -AutoScrollPosition.Y;

            if (bounds.Left < x) x = (int)Math.Floor(bounds.Left);
            else if (bounds.Right > x + client.Width) x = (int)Math.Ceiling(bounds.Right) - client.Width;

            if (bounds.Top < y) y = (int)Math.Floor(bounds.Top);
            else if (bounds.Bottom > y + client.Height) y = (int)Math.Ceiling(bounds.Bottom) - client.Height;

            var target = new Point(Math.Max(0, x), Math.Max(0, y));
            if (target == new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y)) return;

            AutoScrollPosition = target;
            Invalidate();
        }

        #endregion 鍵盤

        #region 右鍵功能表

        // 每次開啟前依目前的 View 重建：CascadeView 沒有勾選資訊，不列出「未勾選節點」。
        private void ContextMenu_Opening(object sender, CancelEventArgs e)
        {
            var items = _contextMenu.Items;
            items.Clear();

            var scopeMenu = new ToolStripMenuItem("範圍");
            scopeMenu.DropDownItems.Add(CreateChoice("整棵樹", _drawScope == TreeDrawScope.WholeTree, () => DrawScope = TreeDrawScope.WholeTree));
            scopeMenu.DropDownItems.Add(CreateChoice("選取的子樹", _drawScope == TreeDrawScope.SelectedSubtree, () => DrawScope = TreeDrawScope.SelectedSubtree));
            items.Add(scopeMenu);

            if (_view is CascadeCheckboxView)
            {
                var uncheckedMenu = new ToolStripMenuItem("未勾選節點");
                uncheckedMenu.DropDownItems.Add(CreateChoice("變淡", _uncheckedDisplay == UncheckedNodeDisplay.Dimmed, () => UncheckedDisplay = UncheckedNodeDisplay.Dimmed));
                uncheckedMenu.DropDownItems.Add(CreateChoice("隱藏", _uncheckedDisplay == UncheckedNodeDisplay.Hidden, () => UncheckedDisplay = UncheckedNodeDisplay.Hidden));
                items.Add(uncheckedMenu);
            }

            var displayMenu = new ToolStripMenuItem("顯示");
            displayMenu.DropDownItems.Add(CreateChoice("原始尺寸", _sizeMode == TreeImageSizeMode.ActualSize, () => SizeMode = TreeImageSizeMode.ActualSize));
            displayMenu.DropDownItems.Add(CreateChoice("自動縮放", _sizeMode == TreeImageSizeMode.Zoom, () => SizeMode = TreeImageSizeMode.Zoom));
            items.Add(displayMenu);

            // 清空項目後 ContextMenuStrip 會預設取消開啟，要明確允許。
            e.Cancel = false;
        }

        private static ToolStripMenuItem CreateChoice(string text, bool isChecked, Action apply)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (sender, e) => apply();
            return item;
        }

        #endregion 右鍵功能表

        #region View 的事件

        private void Attach(CascadeView view)
        {
            view.SelectedPathChanged += View_SelectedPathChanged;
            view.Disposed += View_Disposed;
            if (view is CascadeCheckboxView checkboxView) checkboxView.CheckedChanged += View_CheckedChanged;
        }

        private void Detach(CascadeView view)
        {
            view.SelectedPathChanged -= View_SelectedPathChanged;
            view.Disposed -= View_Disposed;
            if (view is CascadeCheckboxView checkboxView) checkboxView.CheckedChanged -= View_CheckedChanged;
        }

        private void View_SelectedPathChanged(object sender, CascadeNodeEventArgs e) => Redraw();

        private void View_CheckedChanged(object sender, MenuCheckChangedEventArgs e) => Redraw();

        private void View_Disposed(object sender, EventArgs e) => View = null;

        #endregion View 的事件

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_view != null) Detach(_view);
                _view = null;
                _image?.Dispose();
                _image = null;
                _contextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// <see cref="CascadeTreeImageView.NodeActivated"/> 的事件參數：對象節點與它的路徑；滑鼠事件的參數由它衍生。
    /// </summary>
    public class CascadeTreeImageNodeEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="node">對象節點。</param>
        /// <param name="pathSeparator"><see cref="PathText"/> 的層級分隔字串。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public CascadeTreeImageNodeEventArgs(MenuNode node, string pathSeparator)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Path = node.FullPath;
            PathText = string.Join(pathSeparator ?? string.Empty, Path.Select(step => step.Name));
        }

        /// <summary>對象節點。</summary>
        public MenuNode Node { get; }

        /// <summary>從第一層到 <see cref="Node"/> 的路徑，由淺到深，不含樹根；可以直接交給 <see cref="CascadeView.TrySelectPath"/>。</summary>
        public IReadOnlyList<MenuNode> Path { get; }

        /// <summary>路徑上各節點名稱以 <see cref="CascadeTreeImageView.PathSeparator"/> 串起來的字串。</summary>
        public string PathText { get; }
    }

    /// <summary>
    /// <see cref="CascadeTreeImageView.NodeMouseClick"/> 與 <see cref="CascadeTreeImageView.NodeMouseDoubleClick"/> 的事件參數：點到的節點、它的路徑與滑鼠資訊。
    /// </summary>
    public class CascadeTreeImageNodeMouseEventArgs : CascadeTreeImageNodeEventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="node">點到的節點。</param>
        /// <param name="pathSeparator"><see cref="CascadeTreeImageNodeEventArgs.PathText"/> 的層級分隔字串。</param>
        /// <param name="button">按下的滑鼠鍵。</param>
        /// <param name="location">點擊位置，控制項的用戶區座標。</param>
        /// <param name="screenLocation">點擊位置的螢幕座標。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public CascadeTreeImageNodeMouseEventArgs(MenuNode node, string pathSeparator, MouseButtons button, Point location, Point screenLocation)
            : base(node, pathSeparator)
        {
            Button = button;
            Location = location;
            ScreenLocation = screenLocation;
        }

        /// <summary>按下的滑鼠鍵。</summary>
        public MouseButtons Button { get; }

        /// <summary>點擊位置，控制項的用戶區座標。</summary>
        public Point Location { get; }

        /// <summary>點擊位置的螢幕座標。</summary>
        public Point ScreenLocation { get; }
    }
}