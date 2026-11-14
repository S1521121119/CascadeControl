using CascadeControl.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// 以 Miller Columns 的方式呈現多層選單：有下一層就長出一欄，改選時後面的欄位會整批換掉。
    /// </summary>
    /// <remarks>
    /// 狀態只有一份，由 <see cref="CascadeNavigator"/> 持有：選取路徑，以及每個節點「上次在底下選了哪一個子節點」的記憶軌跡。
    /// 記憶逐節點記住，切換分支不會忘掉其他分支走過的路線，只用來讓使用者退回或切換後還能往右走回去。
    /// 欄位 ListBox 只負責呈現：每次狀態改變都由 SyncColumns 依狀態重建欄位、選取與排版，不反過來從 ListBox 讀狀態。
    /// 鍵盤操作：↑↓ 改選目前這一欄（ListBox 原生行為），← 退回上一層，→ 沿記憶或第一個子節點進入下一層，
    /// Enter 在葉節點上觸發 <see cref="NodeActivated"/>、不是葉節點時等同 →，Esc 清除選取（記憶保留）。
    /// 鍵盤焦點固定在目前操作的那一欄，所以按鍵一律以選取深度為準，不看是哪一欄收到按鍵。
    /// 欄位 ListBox 一律由控制項自己建立和移除，不接受從外部加入子控制項：
    /// 設計階段由 <see cref="CascadeViewDesigner"/> 取消容器行為，執行階段由 <see cref="CreateControlsInstance"/> 擋下。
    /// 這個檔案只放對外 API；排版、輸入、繪製分別在 CascadeView.Columns / Input / Rendering。
    /// </remarks>
    [DesignerCategory("")]
    [Designer(typeof(CascadeViewDesigner))]
    [ToolboxItem(true)]
    [DefaultEvent(nameof(SelectedPathChanged))]
    public partial class CascadeView : Panel
    {
        private const int DEFAULT_COLUMN_WIDTH = 160;
        private const int MIN_COLUMN_WIDTH = 20;
        private const int DEFAULT_COLUMN_SPACING = 6;
        private const string DEFAULT_PATH_SEPARATOR = " => ";

        private static readonly IReadOnlyList<ICascadeNode> NoNodes = new ICascadeNode[0];

        private readonly List<ListBox> _lists = new List<ListBox>();
        private readonly CascadeNavigator _navigator = new CascadeNavigator();

        // 第一欄的內容。_topLevelKey 每次 SetTopLevelNodes 都換一個，當作第一欄的「來源」，用來判斷第一欄要不要換內容。
        private IEnumerable<ICascadeNode> _topLevelSource;

        private IReadOnlyList<ICascadeNode> _topLevelNodes = NoNodes;

        private object _topLevelKey = new object();

        private int _columnWidth = DEFAULT_COLUMN_WIDTH;
        private int _columnSpacing = DEFAULT_COLUMN_SPACING;
        private CascadeColumnLayoutMode _columnLayoutMode = CascadeColumnLayoutMode.Fixed;
        private int _displayColumnCount;
        private bool _showTrail;
        private Color _inactiveSelectionBackColor = Color.Empty;
        private Color _trailBackColor = Color.Empty;
        private string _pathSeparator = DEFAULT_PATH_SEPARATOR;

        /// <summary>
        /// 建立控制項。欄位多到放不下時會出現水平捲軸。
        /// </summary>
        public CascadeView()
        {
            AutoScroll = true;
        }

        #region 外觀

        /// <summary>
        /// 每一欄的寬度（像素）；<see cref="ColumnLayoutMode"/> 為 <see cref="CascadeColumnLayoutMode.Fill"/> 或
        /// <see cref="CascadeColumnLayoutMode.Sliding"/> 時是最小欄寬。
        /// </summary>
        [Category("Cascade")]
        [Description("每一欄的寬度（像素）；ColumnLayoutMode 為 Fill 或 Sliding 時是最小欄寬。要調整欄位字型，請設定這個控制項的 Font。")]
        [DefaultValue(DEFAULT_COLUMN_WIDTH)]
        public int ColumnWidth
        {
            get => _columnWidth;
            set
            {
                if (value < MIN_COLUMN_WIDTH)
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"ColumnWidth 不可小於 {MIN_COLUMN_WIDTH}。");

                if (_columnWidth == value) return;

                _columnWidth = value;
                OnColumnWidthChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// 相鄰兩欄之間的水平間距（像素）。
        /// </summary>
        [Category("Cascade")]
        [Description("相鄰兩欄之間的水平間距（像素）。")]
        [DefaultValue(DEFAULT_COLUMN_SPACING)]
        public int ColumnSpacing
        {
            get => _columnSpacing;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "ColumnSpacing 不可為負數。");

                if (_columnSpacing == value) return;

                _columnSpacing = value;
                Relayout();
            }
        }

        /// <summary>
        /// 欄寬與顯示欄數的決定模式。
        /// </summary>
        [Category("Cascade")]
        [Description("Fixed：固定使用 ColumnWidth；Fill：讓 DisplayColumnCount 欄平均填滿寬度，其餘捲動；Sliding：只顯示最後 DisplayColumnCount 欄，畫面隨操作欄滑動，不捲動。")]
        [DefaultValue(CascadeColumnLayoutMode.Fixed)]
        public CascadeColumnLayoutMode ColumnLayoutMode
        {
            get => _columnLayoutMode;
            set
            {
                if (!Enum.IsDefined(typeof(CascadeColumnLayoutMode), value))
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CascadeColumnLayoutMode));

                if (_columnLayoutMode == value) return;

                _columnLayoutMode = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// <see cref="ColumnLayoutMode"/> 為 <see cref="CascadeColumnLayoutMode.Fill"/> 或 <see cref="CascadeColumnLayoutMode.Sliding"/> 時，
        /// 畫面上平均排列的可視欄數。資料有幾層就設幾層，停在最深一層時右邊不會空一格。
        /// </summary>
        /// <remarks>
        /// 0 在 <see cref="CascadeColumnLayoutMode.Fill"/> 時等同 <see cref="CascadeColumnLayoutMode.Fixed"/>，在 <see cref="CascadeColumnLayoutMode.Sliding"/> 時當成 1。
        /// 平均後的欄寬小於 <see cref="ColumnWidth"/> 時，<see cref="CascadeColumnLayoutMode.Fill"/> 改用 <see cref="ColumnWidth"/> 並出現水平捲軸，
        /// <see cref="CascadeColumnLayoutMode.Sliding"/> 則減少顯示的欄數。
        /// </remarks>
        [Category("Cascade")]
        [Description("ColumnLayoutMode 為 Fill 或 Sliding 時，畫面上平均排列的可視欄數；資料有幾層就設幾層。Fill 時 0 等同 Fixed，Sliding 時 0 當成 1。")]
        [DefaultValue(0)]
        public int DisplayColumnCount
        {
            get => _displayColumnCount;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "DisplayColumnCount 不可為負數。");

                if (_displayColumnCount == value) return;

                _displayColumnCount = value;
                Relayout();
            }
        }

        /// <summary>
        /// 退回較淺的層級後，是否仍畫出記憶軌跡中更深的整條路徑；false 時只畫到選取的下一欄。
        /// </summary>
        /// <remarks>
        /// 不論設定為何，記憶軌跡都會保留，按 → 就能沿記憶走回去；這個屬性只影響畫面，不影響選取。
        /// <see cref="ColumnLayoutMode"/> 為 <see cref="CascadeColumnLayoutMode.Sliding"/> 時沒有作用。
        /// </remarks>
        [Category("Cascade")]
        [Description("退回較淺的層級後，是否仍畫出記憶軌跡中更深的整條路徑；false 時只畫到選取的下一欄。")]
        [DefaultValue(false)]
        public bool ShowTrail
        {
            get => _showTrail;
            set
            {
                if (_showTrail == value) return;

                _showTrail = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// 沒有焦點的欄位中，選取項的背景色；預設為調淡的系統選取色。
        /// </summary>
        /// <remarks>
        /// 沒有設定時讀到的是實際使用的預設色，不是 <see cref="Color.Empty"/>，屬性視窗才看得到預設值，繪製時也可以直接拿來用。
        /// 設為 <see cref="Color.Empty"/>，或在屬性視窗按右鍵選「重設」，就會回到預設色；預設色會跟著系統配色變化。
        /// </remarks>
        [Category("Cascade")]
        [Description("沒有焦點的欄位中，選取項的背景色；預設為調淡的系統選取色，按右鍵「重設」可以回到預設。")]
        public Color InactiveSelectionBackColor
        {
            get => _inactiveSelectionBackColor.IsEmpty ? DefaultInactiveSelectionBackColor : _inactiveSelectionBackColor;
            set
            {
                if (_inactiveSelectionBackColor == value) return;

                _inactiveSelectionBackColor = value;
                foreach (var list in _lists) list.Invalidate();
            }
        }

        private bool ShouldSerializeInactiveSelectionBackColor() => !_inactiveSelectionBackColor.IsEmpty;

        private void ResetInactiveSelectionBackColor() => InactiveSelectionBackColor = Color.Empty;

        private static Color DefaultInactiveSelectionBackColor =>
            Blend(SystemColors.Highlight, SystemColors.Window, INACTIVE_SELECTION_BLEND);

        /// <summary>
        /// 記憶軌跡項目（退回前走過、目前不算選取的項目）的背景色；預設為 <see cref="SystemColors.ControlLight"/>。
        /// </summary>
        /// <remarks>
        /// 沒有設定時讀到的是實際使用的預設色，不是 <see cref="Color.Empty"/>。
        /// 設為 <see cref="Color.Empty"/>，或在屬性視窗按右鍵選「重設」，就會回到預設色。
        /// </remarks>
        [Category("Cascade")]
        [Description("記憶軌跡項目（退回前走過、目前不算選取的項目）的背景色；預設為系統淺灰色，按右鍵「重設」可以回到預設。")]
        public Color TrailBackColor
        {
            get => _trailBackColor.IsEmpty ? SystemColors.ControlLight : _trailBackColor;
            set
            {
                if (_trailBackColor == value) return;

                _trailBackColor = value;
                foreach (var list in _lists) list.Invalidate();
            }
        }

        private bool ShouldSerializeTrailBackColor() => !_trailBackColor.IsEmpty;

        private void ResetTrailBackColor() => TrailBackColor = Color.Empty;

        /// <summary>
        /// <see cref="SelectedPathText"/> 使用的層級分隔字串。
        /// </summary>
        [Category("Cascade")]
        [Description("SelectedPathText 使用的層級分隔字串。")]
        [DefaultValue(DEFAULT_PATH_SEPARATOR)]
        public string PathSeparator
        {
            get => _pathSeparator;
            set => _pathSeparator = value ?? string.Empty;
        }

        #endregion 外觀

        #region 資料輸入

        /// <summary>
        /// 設定要顯示的選單樹，以 <see cref="MenuTree.TopLevelNodes"/> 作為第一欄的內容。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空。</param>
        public void SetTree(MenuTree tree)
        {
            SetTopLevelNodes(tree?.TopLevelNodes);
        }

        /// <summary>
        /// 重新設定第一層的頂層節點集合：第一欄換成新內容，其他欄位全部移除，選取與記憶都清除；沒有內容時一欄都不留。
        /// </summary>
        /// <param name="topLevelNodes">第一層節點；傳 null 或空集合表示清空。</param>
        /// <remarks>執行後會觸發一次 <see cref="SelectedPathChanged"/>（未選取的狀態）。</remarks>
        public void SetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes)
        {
            _topLevelSource = topLevelNodes;
            _topLevelNodes = topLevelNodes?.ToList() ?? NoNodes;
            _topLevelKey = new object();
            _navigator.Clear();

            SyncColumns();
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        /// <summary>
        /// 當底層節點結構（插入、刪除、改名）變更後，重新整理所有欄位內容與顯示文字，並選取指定路徑。
        /// </summary>
        /// <param name="selectPath">更新後要選取的路徑；傳 null 則保留目前仍存在的選取路徑。</param>
        public void RefreshColumns(IEnumerable<ICascadeNode> selectPath = null)
        {
            var targetPath = (selectPath ?? _navigator.SelectedPath).ToList();
            _topLevelNodes = _topLevelSource?.ToList() ?? NoNodes;
            _topLevelKey = new object();

            // 清除既有欄位的來源標記，強制 SyncColumns 重建每一欄的項目清單與顯示文字。
            foreach (var list in _lists) list.Tag = null;

            if (targetPath.Count > 0)
            {
                TrySelectPath(targetPath);
            }
            else
            {
                _navigator.ClearSelection();
                SyncColumns();
                OnSelectedPathChanged(CreateNodeEventArgs());
            }
        }

        /// <summary>
        /// 最近一次呼叫 <see cref="CopySelectedNode"/> 所複製的節點子樹複本。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuNode CopiedNode { get; private set; }

        /// <summary>
        /// 複製目前選取的 <see cref="MenuNode"/>（含其下所有子孫節點的深層複本），並存入 <see cref="CopiedNode"/>。
        /// </summary>
        /// <returns>複製出的節點複本；目前未選取或選取項不是 <see cref="MenuNode"/> 時回傳 null。</returns>
        public MenuNode CopySelectedNode()
        {
            if (SelectedNode is MenuNode node)
            {
                CopiedNode = node.CopySubtree();
                return CopiedNode;
            }

            return null;
        }

        /// <summary>
        /// 移除所有欄位並清除選取。
        /// </summary>
        public void Clear() => SetTopLevelNodes(null);

        #endregion 資料輸入

        #region 選取查詢

        /// <summary>
        /// 目前選取的最深節點；未選取時為 null。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ICascadeNode SelectedNode => _navigator.SelectedNode;

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。不包含退回後留下的記憶軌跡。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ICascadeNode> SelectedPath => _navigator.SelectedPath.ToList();

        /// <summary>
        /// 目前選取路徑上各層的 <see cref="ICascadeNode.Name"/>，以 <see cref="PathSeparator"/> 串接；未選取時為空字串。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedPathText => string.Join(_pathSeparator, _navigator.SelectedPath.Select(node => node.Name));

        /// <summary>
        /// 目前的選取是否停在葉節點，也就是整條路徑已經選到底。
        /// </summary>
        /// <remarks>
        /// 每條路徑的深度可以不同：在 A &gt; A1 &gt; A11 中，如果 A11 沒有子節點，選到第三層就算完成。
        /// </remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLeafSelected => IsLeaf(SelectedNode);

        /// <summary>
        /// 目前操作中的欄位索引（0-based）：最深選取項所在的欄；未選取時為 0。
        /// </summary>
        /// <remarks>鍵盤焦點固定在這一欄，所以它和選取深度一致，不受控制項是否有焦點影響。</remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ActiveColumnIndex => _navigator.ActiveColumnIndex;

        #endregion 選取查詢

        #region 選取操作

        /// <summary>
        /// 選取指定的 <see cref="MenuNode"/>（依其 <see cref="MenuNode.FullPath"/> 由第一欄開始選取）。
        /// </summary>
        /// <param name="node">要選取的節點。</param>
        /// <returns>成功選取時為 true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public bool TrySelectNode(MenuNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            return TrySelectPath(node.FullPath);
        }

        /// <summary>
        /// 從第一欄開始，依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺到深的節點；以 <see cref="object.Equals(object)"/> 比對。</param>
        /// <returns>整條路徑都選到時為 true；中途找不到時，選取停在已選到的那一段，並回傳 false。</returns>
        /// <remarks>
        /// 不論成功與否，最後的選取都正好是已比對成功的那一段。路徑經過的每一層會記成「上次選的項目」，其他分支的記憶保留。
        /// 不管選了幾層，<see cref="SelectedPathChanged"/> 都只觸發一次。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> 為 null。</exception>
        public bool TrySelectPath(IEnumerable<ICascadeNode> path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var steps = path.ToList();
            var matched = new List<ICascadeNode>();
            var nodes = _topLevelNodes;

            foreach (var step in steps)
            {
                // 存入清單中的那一個實例，而不是呼叫端傳入的，之後在欄位裡找選取項時才對得上。
                var found = nodes.FirstOrDefault(node => node != null && Equals(node, step));
                if (found == null) break;

                matched.Add(found);
                nodes = CascadeNavigator.ChildrenOf(found);
            }

            _navigator.SelectPath(matched);
            SyncColumns();

            OnSelectedPathChanged(CreateNodeEventArgs());
            return matched.Count == steps.Count;
        }

        /// <summary>
        /// 忘掉所有分支的記憶軌跡，只保留目前的選取路徑；選取不變，所以不會觸發 <see cref="SelectedPathChanged"/>。
        /// </summary>
        /// <remarks>呼叫 <see cref="SetTopLevelNodes"/>、<see cref="SetTree"/> 或 <see cref="Clear"/> 時記憶也會一併清除，不需要另外呼叫。</remarks>
        public void ClearTrail()
        {
            _navigator.ClearTrail();
            SyncColumns();
        }

        /// <summary>
        /// 把鍵盤焦點移到目前操作的那一欄（<see cref="ActiveColumnIndex"/>）；未選取時就是第一欄。
        /// 沒有欄位，或那一欄無法取得焦點（例如控制項已停用）時，不做任何事。
        /// </summary>
        public void FocusActiveColumn()
        {
            var columnIndex = _navigator.ActiveColumnIndex;
            if (columnIndex >= _lists.Count) return;

            var list = _lists[columnIndex];
            if (list.CanFocus && !list.Focused) list.Focus();
        }

        #endregion 選取操作

        #region 事件

        /// <summary>
        /// 選取路徑改變後觸發，包含 ← / → 改變選取深度；呼叫 <see cref="SetTopLevelNodes"/>、<see cref="SetTree"/>、<see cref="Clear"/>、<see cref="TrySelectPath"/> 時也會觸發。
        /// </summary>
        /// <remarks>
        /// 參數包含完整的選取路徑、上一層父節點、下一欄的子節點內容，以及是否已選到葉節點；
        /// 未選取時 <see cref="CascadeNodeEventArgs.Node"/> 為 null，<see cref="CascadeNodeEventArgs.Children"/> 為第一欄的內容。
        /// </remarks>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

        /// <summary>
        /// 使用者在節點上雙擊或在葉節點上按 Enter 時觸發。參數型別與 <see cref="SelectedPathChanged"/> 相同。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeNodeEventArgs> NodeActivated;

        /// <summary>
        /// <see cref="ColumnWidth"/> 改變後觸發。
        /// </summary>
        /// <remarks>
        /// 不會出現在屬性視窗：設計階段用不到它，它的用途是讓資料繫結依照 {Property}Changed 慣例收到變更通知。
        /// </remarks>
        [Browsable(false)]
        public event EventHandler ColumnWidthChanged;

        /// <summary>
        /// 繪製每一列節點之前觸發；把 <see cref="CascadeDrawNodeEventArgs.Handled"/> 設為 true，就會改用你自己的畫法。
        /// </summary>
        /// <remarks>供進階自繪使用，不會出現在屬性視窗，需要時請在程式碼中訂閱。</remarks>
        [Browsable(false)]
        public event EventHandler<CascadeDrawNodeEventArgs> DrawNode;

        /// <summary>
        /// 觸發 <see cref="SelectedPathChanged"/>。
        /// </summary>
        protected virtual void OnSelectedPathChanged(CascadeNodeEventArgs e) => SelectedPathChanged?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="NodeActivated"/>。
        /// </summary>
        protected virtual void OnNodeActivated(CascadeNodeEventArgs e) => NodeActivated?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="ColumnWidthChanged"/> 並重新排版。
        /// </summary>
        protected virtual void OnColumnWidthChanged(EventArgs e)
        {
            Relayout();
            ColumnWidthChanged?.Invoke(this, e);
        }

        /// <summary>
        /// 觸發 <see cref="DrawNode"/>；訂閱端沒有自行繪製時，畫出預設外觀。
        /// </summary>
        protected virtual void OnDrawNode(CascadeDrawNodeEventArgs e)
        {
            DrawNode?.Invoke(this, e);
            if (!e.Handled) DrawDefaultNode(e);
        }

        /// <inheritdoc />
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        /// <inheritdoc />
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            var itemHeight = ItemHeight;
            foreach (var list in _lists) list.ItemHeight = itemHeight;

            Relayout();
        }

        #endregion 事件

        /// <summary>
        /// 導覽狀態改變後，重建欄位並觸發 <see cref="SelectedPathChanged"/>；沒有改變時不做任何事。
        /// </summary>
        private void ApplyNavigation(bool changed)
        {
            if (!changed) return;

            SyncColumns();
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        /// <summary>
        /// 依目前的選取狀態組出 <see cref="SelectedPathChanged"/> 與 <see cref="NodeActivated"/> 共用的事件參數。
        /// </summary>
        /// <remarks>第一欄的內容只在未選取時才需要，有選取時不去複製它。</remarks>
        private CascadeNodeEventArgs CreateNodeEventArgs()
        {
            var path = _navigator.SelectedPath.ToList();
            return new CascadeNodeEventArgs(path, path.Count == 0 ? _topLevelNodes : null);
        }

        private static bool IsLeaf(ICascadeNode node) => node != null && CascadeNavigator.ChildrenOf(node).Count == 0;

        // 沒有佔位項，SelectedIndex 為 -1 就是這一欄沒有選取。
        private static ICascadeNode NodeAt(ListBox list)
        {
            return list.SelectedIndex >= 0 ? list.Items[list.SelectedIndex] as ICascadeNode : null;
        }
    }
}