using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    /// <summary>
    /// 把 <see cref="State"/> 目前顯示的選單樹用 <see cref="TreeDrawer"/> 畫出來，跟著選取與勾選自動重畫；點圖上的節點可以知道點到哪一個。
    /// </summary>
    /// <remarks>
    /// 取代「可捲動的 Panel 包一個 AutoSize 的 PictureBox」：控制項自己畫圖、自己管捲軸，所以可以直接 Dock = Fill。
    /// 可以直接呼叫 <see cref="SetTree"/> 獨立顯示與導覽，也可以把與 <see cref="Columns.CascadeColumnsView"/> 相同的 <see cref="CascadeState"/> 指派給 <see cref="State"/> 同步顯示。
    /// 右鍵功能表可以切換 <see cref="DrawScope"/>、<see cref="SizeMode"/>；<see cref="ShowChecks"/> 為 true 且 <see cref="State"/> 是 <see cref="CascadeCheckableState"/> 時
    /// 多一組 <see cref="UncheckedDisplay"/>（沒有勾選資訊時用不到這個設定，所以不列出來）。
    /// 會自動重畫的時機：<see cref="CascadeState.SelectedPathChanged"/>（包含 <see cref="CascadeState.SetTree"/> 與 <see cref="CascadeState.Refresh"/>）、
    /// <see cref="CascadeCheckableState.CheckedChanged"/> 與 <see cref="CascadeCheckableState.CheckSetChanged"/>；在外部直接修改節點而未呼叫 <see cref="CascadeState.Refresh"/> 時，
    /// 請自己呼叫 <see cref="Redraw"/>。
    /// <para>
    /// 有子節點的節點可以收合：左鍵點方塊右側的 [+] 展開（變成 [-]、畫出子節點），點 [-] 收合；點展開鈕不會觸發 <see cref="NodeMouseClick"/>。
    /// 選取改變時（包含從共享 State 的 <see cref="Columns.CascadeColumnsView"/> 切換節點），選取節點的祖先自動展開，效果和點了展開鈕相同；原始尺寸下也會捲動到選取的節點。
    /// 沒點過的節點依勾選決定：有任何子節點被勾選就展開（勾選子孫會補勾祖先，所以祖先跟著展開），否則收合；
    /// 沒有勾選資訊（<see cref="ShowChecks"/> 為 false，或 <see cref="State"/> 不是 <see cref="CascadeCheckableState"/>）時一律收合。
    /// 點過的節點記住使用者的選擇，換成另一棵樹時全部忘掉；選取不變時點收合，選取的節點會被藏起來，直到下次選取改變。
    /// </para>
    /// <para>
    /// 取得焦點後可以用鍵盤操作，改的是 <see cref="State"/> 的選取，所以「進出記住位置」的記憶和共享同一份狀態的其他檢視一致：
    /// ↑↓ 在圖上看得到的同一層節點之間移動，可以跨父節點，被收合或隱藏的節點跳過；
    /// ← 同 <see cref="StepBack"/>；→ 同 <see cref="StepForward"/>，但選取的節點收合（[+]）時不動作，要先展開；
    /// 空白鍵展開或收合選取的節點；Enter 觸發 <see cref="NodeActivated"/>（和雙擊同樣用途，由訂閱端決定做什麼）。原始尺寸下會捲動到選取的節點。
    /// <see cref="TreeDrawScope.SelectedSubtree"/> 下起點是它那一層唯一畫出來的節點，所以 ↑↓ 不會移動。
    /// </para>
    /// <para>
    /// 編輯快捷鍵與 <see cref="Columns.CascadeColumnsView"/> 相同（改名、複製、剪下、貼上、刪除、復原、重做），對象是選取的節點；右鍵功能表的對象是被點的節點。
    /// 改名（F2）在節點方塊上開編輯框，Enter 套用、Esc 取消；樹被任何檢視改過時編輯框直接關掉。
    /// <see cref="ReadOnly"/> 預設為 true，只能複製。改樹由 <see cref="State"/> 執行，綁同一份 State 的檢視一起更新，也共用同一份復原歷史。
    /// </para>
    /// </remarks>
    [DesignerCategory("")]
    [ToolboxItem(true)]
    [DefaultEvent(nameof(NodeMouseClick))]
    public partial class CascadeDiagramView : ScrollableControl, ICascadeView
    {
        private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();

        private CascadeState _state;
        private TreeImage _image;
        private TreeDrawScope _drawScope = TreeDrawScope.WholeTree;
        private UncheckedNodeDisplay _uncheckedDisplay = UncheckedNodeDisplay.Dimmed;
        private bool _showChecks;
        private TreeImageSizeMode _sizeMode = TreeImageSizeMode.ActualSize;

        /// <summary>
        /// 建立控制項。
        /// </summary>
        public CascadeDiagramView()
        {
            // Selectable：點一下就取得焦點，滾輪才捲得動。
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
            AutoScroll = true;

            _state = new CascadeCheckableState();
            AttachState(_state);

            _contextMenu.Opening += ContextMenu_Opening;
            _contextMenu.Closed += ContextMenu_Closed;
            ContextMenuStrip = _contextMenu;

            UpdateImage(null);
        }

        #region 設定與狀態

        /// <summary>
        /// 目前綁定的共享狀態：樹、選取路徑、記憶軌跡、<see cref="PathSeparator"/> 與勾選狀態都取自這裡。不可設為 null。
        /// </summary>
        /// <remarks>
        /// 預設每個控制項各自持有一份 <see cref="CascadeCheckableState"/>，單獨使用時 <see cref="ShowChecks"/> 也有勾選狀態可以顯示。
        /// </remarks>
        /// <exception cref="ArgumentNullException">設為 null。</exception>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CascadeState State
        {
            get => _state;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_state, value)) return;

                DetachState(_state);
                _state = value;
                AttachState(_state);

                RevealSelection();
                Redraw();
            }
        }

        /// <summary>
        /// 目前顯示的選單樹；尚未設定或已清空時為 null。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuTree Tree => _state.Tree;

        /// <summary>
        /// 目前選取的最深節點；未選取時為 null。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ICascadeNode SelectedNode => _state.SelectedNode;

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ICascadeNode> SelectedPath => _state.SelectedPath;

        /// <summary>
        /// 目前選取路徑上各層節點名稱以 <see cref="PathSeparator"/> 串接的字串；未選取時為空字串。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedPathText => _state.SelectedPathText;

        /// <summary>
        /// 目前的選取是否停在葉節點。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLeafSelected => _state.IsLeafSelected;

        /// <summary>
        /// 設定要顯示的選單樹；傳 null 表示清空。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空。</param>
        public void SetTree(MenuTree tree) => _state.SetTree(tree);

        /// <summary>
        /// 清除目前顯示的樹與選取。
        /// </summary>
        public void Clear() => _state.Clear();

        /// <summary>
        /// 選取指定的 <see cref="MenuNode"/>。
        /// </summary>
        /// <param name="node">要選取的節點。</param>
        /// <returns>成功選取時為 true。</returns>
        public bool TrySelectNode(MenuNode node) => _state.TrySelectNode(node);

        /// <summary>
        /// 從第一層開始依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺至深的節點序列。</param>
        /// <returns>整條路徑都選到時為 true。</returns>
        public bool TrySelectPath(IEnumerable<ICascadeNode> path) => _state.TrySelectPath(path);

        /// <summary>
        /// 往上一層退回一步。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        public bool StepBack() => _state.StepBack();

        /// <summary>
        /// 往下一層前進一步。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        public bool StepForward() => _state.StepForward();

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
        /// 是否依 <see cref="State"/> 的勾選狀態畫圖：沒勾的節點依 <see cref="UncheckedDisplay"/> 呈現，有勾選子節點的節點預設展開。
        /// </summary>
        /// <remarks>
        /// 這個控制項與 <see cref="Columns.CascadeColumnsView"/> 的 <see cref="State"/> 預設都是 <see cref="CascadeCheckableState"/>，
        /// 所以不能用 State 的型別判斷要不要畫勾選；和 <see cref="Columns.CascadeColumnsView.CheckBoxes"/> 一樣由這個屬性決定。
        /// <see cref="State"/> 不是 <see cref="CascadeCheckableState"/> 時沒有勾選資訊，設成 true 也沒有作用。
        /// </remarks>
        [Category("Cascade")]
        [Description("true：依 State 的勾選狀態畫圖，沒勾的節點變淡或不畫；false：不畫勾選狀態。")]
        [DefaultValue(false)]
        public bool ShowChecks
        {
            get => _showChecks;
            set
            {
                if (_showChecks == value) return;
                _showChecks = value;
                Redraw();
                OnDrawSettingsChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// <see cref="ShowChecks"/> 為 true 且有勾選資訊時，沒勾的節點怎麼呈現。
        /// </summary>
        [Category("Cascade")]
        [Description("ShowChecks 為 true 時，沒勾的節點變淡或不畫。")]
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
                PositionEditBox();
                Invalidate();
                OnDrawSettingsChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// <see cref="CascadeNodeEventArgs.PathText"/> 與 <see cref="SelectedPathText"/> 使用的層級分隔字串。
        /// </summary>
        [Category("Cascade")]
        [Description("NodeMouseClick 事件參數 PathText 與 SelectedPathText 使用的層級分隔字串。")]
        [DefaultValue(CascadeNavigator.DEFAULT_PATH_SEPARATOR)]
        public string PathSeparator
        {
            get => _state.PathSeparator;
            set => _state.PathSeparator = value;
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

        #endregion 設定與狀態

        #region 事件

        /// <summary>
        /// 選取路徑改變後觸發，包含 <see cref="SetTree"/>、<see cref="Clear"/> 與導覽操作。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

        /// <summary>
        /// 用滑鼠點了圖上的某個節點之後觸發（左鍵、右鍵都會）；點在空白處或 Root 上不會觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeMouseEventArgs> NodeMouseClick;

        /// <summary>
        /// 在圖上的某個節點雙擊之後觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeMouseEventArgs> NodeMouseDoubleClick;

        /// <summary>
        /// 焦點在樹圖上時，在選取的節點上按 Enter 之後觸發；用途和 <see cref="NodeMouseDoubleClick"/> 相同，
        /// 讓鍵盤也能「啟動」節點（例如 <see cref="Columns.CascadeColumnsView"/> 開著 <see cref="Columns.CascadeColumnsView.CheckBoxes"/> 時切換勾選，或執行選取）。
        /// 控制項本身不做任何事，由訂閱端決定。沒有選取，或選取的節點被收合藏起來時不觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeEventArgs> NodeActivated;

        /// <summary>
        /// <see cref="DrawScope"/>、<see cref="ShowChecks"/>、<see cref="UncheckedDisplay"/> 或 <see cref="SizeMode"/> 改變後觸發，包含從右鍵功能表切換。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler DrawSettingsChanged;

        /// <summary>
        /// 觸發 <see cref="SelectedPathChanged"/>。
        /// </summary>
        protected virtual void OnSelectedPathChanged(CascadeNodeEventArgs e) => SelectedPathChanged?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeMouseClick"/>。
        /// </summary>
        protected virtual void OnNodeMouseClick(CascadeNodeMouseEventArgs e) => NodeMouseClick?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeMouseDoubleClick"/>。
        /// </summary>
        protected virtual void OnNodeMouseDoubleClick(CascadeNodeMouseEventArgs e) => NodeMouseDoubleClick?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeActivated"/>。
        /// </summary>
        protected virtual void OnNodeActivated(CascadeNodeEventArgs e) => NodeActivated?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="DrawSettingsChanged"/>。
        /// </summary>
        protected virtual void OnDrawSettingsChanged(EventArgs e) => DrawSettingsChanged?.Invoke(this, e);

        #endregion 事件

        #region State 的事件

        private void AttachState(CascadeState state)
        {
            state.Invalidated += State_Invalidated;
            state.SelectedPathChanged += State_SelectedPathChanged;
            if (state is CascadeCheckableState checkableState)
            {
                checkableState.CheckedChanged += State_CheckedChanged;
                checkableState.CheckSetChanged += State_CheckSetChanged;
            }
        }

        private void DetachState(CascadeState state)
        {
            if (state == null) return;

            AbortEdit();
            state.Invalidated -= State_Invalidated;
            state.SelectedPathChanged -= State_SelectedPathChanged;
            if (state is CascadeCheckableState checkableState)
            {
                checkableState.CheckedChanged -= State_CheckedChanged;
                checkableState.CheckSetChanged -= State_CheckSetChanged;
            }
        }

        // 換樹或樹被改過（任何綁同一份 State 的檢視編輯、復原、重做）：編輯框的對象可能已經不在或改了名，直接關掉、不套用。
        // 只是選取改變時保留編輯框，重畫後再對齊（見 PositionEditBox）。
        private void State_Invalidated(CascadeStateInvalidation kind)
        {
            if (kind == CascadeStateInvalidation.Reset || kind == CascadeStateInvalidation.Refreshed) AbortEdit();
        }

        private void State_SelectedPathChanged(object sender, CascadeNodeEventArgs e)
        {
            RevealSelection();
            Redraw();
            EnsureVisible(SelectedMenuNode);
            OnSelectedPathChanged(e);
        }

        private void State_CheckedChanged(object sender, MenuCheckChangedEventArgs e) => Redraw();

        private void State_CheckSetChanged(object sender, EventArgs e) => Redraw();

        #endregion State 的事件

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachState(_state);
                _image?.Dispose();
                _image = null;
                _contextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}