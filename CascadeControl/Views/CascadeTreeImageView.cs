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
    /// </remarks>
    [DesignerCategory("")]
    [ToolboxItem(true)]
    [DefaultEvent(nameof(NodeMouseClick))]
    public class CascadeTreeImageView : ScrollableControl
    {
        private const string DEFAULT_PATH_SEPARATOR = " => ";

        private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();

        private CascadeView _view;
        private TreeImage _image;
        private TreeDrawScope _drawScope = TreeDrawScope.WholeTree;
        private UncheckedNodeDisplay _uncheckedDisplay = UncheckedNodeDisplay.Dimmed;
        private TreeImageSizeMode _sizeMode = TreeImageSizeMode.ActualSize;
        private string _pathSeparator = DEFAULT_PATH_SEPARATOR;

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
        /// <see cref="CascadeTreeImageNodeMouseEventArgs.PathText"/> 使用的層級分隔字串。
        /// </summary>
        [Category("Cascade")]
        [Description("NodeMouseClick 事件參數 PathText 使用的層級分隔字串。")]
        [DefaultValue(DEFAULT_PATH_SEPARATOR)]
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
            var selected = _view?.SelectedNode as MenuNode;
            if (_drawScope == TreeDrawScope.SelectedSubtree && selected == null) tree = null;

            var checkSet = (_view as CascadeCheckboxView)?.CheckSet;
            var isChecked = checkSet == null ? (Func<MenuNode, bool>)null : checkSet.IsChecked;

            UpdateImage(TreeDrawer.RenderImage(tree, Font, isChecked, _drawScope, _uncheckedDisplay, selected));
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

            GetTransform(out var scale, out var offset);
            return _image.GetNodeAt(new PointF((clientLocation.X - offset.X) / scale, (clientLocation.Y - offset.Y) / scale));
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

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseClick(CreateMouseEventArgs(node, e));
        }

        /// <inheritdoc />
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseDoubleClick(CreateMouseEventArgs(node, e));
        }

        private CascadeTreeImageNodeMouseEventArgs CreateMouseEventArgs(MenuNode node, MouseEventArgs e)
        {
            return new CascadeTreeImageNodeMouseEventArgs(node, _pathSeparator, e.Button, e.Location, PointToScreen(e.Location));
        }

        #endregion 滑鼠

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
    /// <see cref="CascadeTreeImageView.NodeMouseClick"/> 與 <see cref="CascadeTreeImageView.NodeMouseDoubleClick"/> 的事件參數：點到的節點與它的路徑。
    /// </summary>
    public class CascadeTreeImageNodeMouseEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="node">點到的節點。</param>
        /// <param name="pathSeparator"><see cref="PathText"/> 的層級分隔字串。</param>
        /// <param name="button">按下的滑鼠鍵。</param>
        /// <param name="location">點擊位置，控制項的用戶區座標。</param>
        /// <param name="screenLocation">點擊位置的螢幕座標。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public CascadeTreeImageNodeMouseEventArgs(MenuNode node, string pathSeparator, MouseButtons button, Point location, Point screenLocation)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Path = node.FullPath;
            PathText = string.Join(pathSeparator ?? string.Empty, Path.Select(step => step.Name));
            Button = button;
            Location = location;
            ScreenLocation = screenLocation;
        }

        /// <summary>點到的節點。</summary>
        public MenuNode Node { get; }

        /// <summary>從第一層到 <see cref="Node"/> 的路徑，由淺到深，不含樹根；可以直接交給 <see cref="CascadeView.TrySelectPath"/>。</summary>
        public IReadOnlyList<MenuNode> Path { get; }

        /// <summary>路徑上各節點名稱以 <see cref="CascadeTreeImageView.PathSeparator"/> 串起來的字串。</summary>
        public string PathText { get; }

        /// <summary>按下的滑鼠鍵。</summary>
        public MouseButtons Button { get; }

        /// <summary>點擊位置，控制項的用戶區座標。</summary>
        public Point Location { get; }

        /// <summary>點擊位置的螢幕座標。</summary>
        public Point ScreenLocation { get; }
    }
}