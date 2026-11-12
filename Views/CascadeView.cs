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
    /// 狀態只有一份，由 <see cref="CascadeNavigator"/> 持有：選取路徑，以及每個節點「上次在底下選了哪一個子節點」的記憶。
    /// 記憶逐節點記住，切換分支不會忘掉其他分支走過的路線，只用來讓使用者退回或切換後還能往右走回去。
    /// 欄位 ListBox 只負責呈現：每次狀態改變都由 SyncColumns 依狀態重建欄位、選取與排版，不反過來從 ListBox 讀狀態。
    /// 鍵盤操作：↑↓ 改選目前這一欄（ListBox 原生行為），← 退回上一層，→ 沿記憶或第一個子節點進入下一層，
    /// Enter 在葉節點上觸發 <see cref="ItemActivated"/>、不是葉節點時等同 →，Esc 清除選取（記憶保留）。
    /// 鍵盤焦點固定在目前操作的那一欄，所以按鍵一律以選取深度為準，不看是哪一欄收到按鍵。
    /// 欄位 ListBox 一律由控制項自己建立和移除，不接受從外部加入子控制項：
    /// 設計階段由 <see cref="CascadeViewDesigner"/> 取消容器行為，執行階段由 <see cref="CreateControlsInstance"/> 擋下。
    /// 這個檔案只放對外 API；排版、輸入、繪製分別在 CascadeView.Columns / Input / Rendering。
    /// </remarks>
    [DesignerCategory("")]
    [Designer(typeof(CascadeViewDesigner))]
    [ToolboxItem(true)]
    [DefaultEvent(nameof(SelectionChanged))]
    public partial class CascadeView : Panel
    {
        private const int DEFAULT_COLUMN_WIDTH = 160;
        private const int MIN_COLUMN_WIDTH = 20;
        private const int DEFAULT_COLUMN_GAP = 6;
        private const string DEFAULT_PATH_SEPARATOR = " => ";

        private static readonly IReadOnlyList<ICascadeItem> NoItems = new ICascadeItem[0];

        private readonly List<ListBox> _lists = new List<ListBox>();
        private readonly CascadeNavigator _navigator = new CascadeNavigator();

        // 第一欄的內容。_rootsKey 每次 SetRoots 都換一個，當作第一欄的「來源」，用來判斷第一欄要不要換內容。
        private IReadOnlyList<ICascadeItem> _roots = NoItems;

        private object _rootsKey = new object();

        private int _columnWidth = DEFAULT_COLUMN_WIDTH;
        private int _columnGap = DEFAULT_COLUMN_GAP;
        private CascadeColumnSizeMode _columnSizeMode = CascadeColumnSizeMode.Fixed;
        private int _fillColumnCount;
        private bool _showRememberedPath;
        private Color _inactiveSelectionBackColor = Color.Empty;
        private Color _rememberedBackColor = Color.Empty;
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
        /// 每一欄的寬度（像素）；<see cref="ColumnSizeMode"/> 為 <see cref="CascadeColumnSizeMode.Fill"/> 或
        /// <see cref="CascadeColumnSizeMode.Single"/> 時是最小欄寬。
        /// </summary>
        [Category("Cascade")]
        [Description("每一欄的寬度（像素）；ColumnSizeMode 為 Fill 或 Single 時是最小欄寬。要調整欄位字型，請設定這個控制項的 Font。")]
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
        /// 相鄰兩欄之間的間距（像素）。
        /// </summary>
        [Category("Cascade")]
        [Description("相鄰兩欄之間的間距（像素）。")]
        [DefaultValue(DEFAULT_COLUMN_GAP)]
        public int ColumnGap
        {
            get => _columnGap;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "ColumnGap 不可為負數。");

                if (_columnGap == value) return;

                _columnGap = value;
                Relayout();
            }
        }

        /// <summary>
        /// 欄寬與顯示欄數的決定方式。
        /// </summary>
        [Category("Cascade")]
        [Description("Fixed：固定使用 ColumnWidth；Fill：讓 FillColumnCount 欄平均填滿寬度；Single：只顯示目前操作的那一欄。")]
        [DefaultValue(CascadeColumnSizeMode.Fixed)]
        public CascadeColumnSizeMode ColumnSizeMode
        {
            get => _columnSizeMode;
            set
            {
                if (!Enum.IsDefined(typeof(CascadeColumnSizeMode), value))
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CascadeColumnSizeMode));

                if (_columnSizeMode == value) return;

                _columnSizeMode = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// <see cref="ColumnSizeMode"/> 為 <see cref="CascadeColumnSizeMode.Fill"/> 時，畫面上平均排列的欄數；0 表示等同 <see cref="CascadeColumnSizeMode.Fixed"/>。
        /// </summary>
        /// <remarks>平均後的欄寬小於 <see cref="ColumnWidth"/> 時改用 <see cref="ColumnWidth"/>，放不下的部分出現水平捲軸。</remarks>
        [Category("Cascade")]
        [Description("ColumnSizeMode 為 Fill 時，畫面上平均排列的欄數；0 表示等同 Fixed。")]
        [DefaultValue(0)]
        public int FillColumnCount
        {
            get => _fillColumnCount;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "FillColumnCount 不可為負數。");

                if (_fillColumnCount == value) return;

                _fillColumnCount = value;
                Relayout();
            }
        }

        /// <summary>
        /// 退回較淺的層級後，是否仍畫出記憶中更深的整條路徑；false 時只畫到選取的下一欄。
        /// </summary>
        /// <remarks>
        /// 不論設定為何，記憶都會保留，按 → 就能沿記憶走回去；這個屬性只影響畫面，不影響選取。
        /// <see cref="ColumnSizeMode"/> 為 <see cref="CascadeColumnSizeMode.Single"/> 時沒有作用。
        /// </remarks>
        [Category("Cascade")]
        [Description("退回較淺的層級後，是否仍畫出記憶中更深的整條路徑；false 時只畫到選取的下一欄。")]
        [DefaultValue(false)]
        public bool ShowRememberedPath
        {
            get => _showRememberedPath;
            set
            {
                if (_showRememberedPath == value) return;

                _showRememberedPath = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// 沒有焦點的欄位中，選取項的背景色；設為 <see cref="Color.Empty"/> 時使用調淡的系統選取色。
        /// </summary>
        [Category("Cascade")]
        [Description("沒有焦點的欄位中，選取項的背景色；留空時使用調淡的系統選取色。")]
        public Color InactiveSelectionBackColor
        {
            get => _inactiveSelectionBackColor;
            set
            {
                if (_inactiveSelectionBackColor == value) return;

                _inactiveSelectionBackColor = value;
                foreach (var list in _lists) list.Invalidate();
            }
        }

        private bool ShouldSerializeInactiveSelectionBackColor() => !_inactiveSelectionBackColor.IsEmpty;

        private void ResetInactiveSelectionBackColor() => InactiveSelectionBackColor = Color.Empty;

        /// <summary>
        /// 記憶項目（退回前走過、目前不算選取的項目）的背景色；設為 <see cref="Color.Empty"/> 時使用 <see cref="SystemColors.ControlLight"/>。
        /// </summary>
        [Category("Cascade")]
        [Description("記憶項目（退回前走過、目前不算選取的項目）的背景色；留空時使用系統淺灰色。")]
        public Color RememberedBackColor
        {
            get => _rememberedBackColor;
            set
            {
                if (_rememberedBackColor == value) return;

                _rememberedBackColor = value;
                foreach (var list in _lists) list.Invalidate();
            }
        }

        private bool ShouldSerializeRememberedBackColor() => !_rememberedBackColor.IsEmpty;

        private void ResetRememberedBackColor() => RememberedBackColor = Color.Empty;

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
        /// 重新設定第一層的內容：第一欄換成新內容，其他欄位全部移除，選取與記憶都清除；沒有內容時一欄都不留。
        /// </summary>
        /// <param name="roots">第一層節點；傳 null 或空集合表示清空。</param>
        /// <remarks>執行後會觸發一次 <see cref="SelectionChanged"/>（未選取的狀態）。</remarks>
        public void SetRoots(IEnumerable<ICascadeItem> roots)
        {
            _roots = roots?.ToList() ?? NoItems;
            _rootsKey = new object();
            _navigator.Clear();

            SyncColumns();
            OnSelectionChanged(CreateItemEventArgs());
        }

        /// <summary>
        /// 移除所有欄位並清除選取。
        /// </summary>
        public void Clear() => SetRoots(null);

        #endregion 資料輸入

        #region 選取查詢

        /// <summary>
        /// 目前選取的最深節點；未選取時為 null。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ICascadeItem SelectedItem => _navigator.Active;

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。不包含退回後留下的記憶。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ICascadeItem> SelectedPath => _navigator.Selection.ToList();

        /// <summary>
        /// 目前選取路徑上各層的 <see cref="ICascadeItem.Name"/>，以 <see cref="PathSeparator"/> 串接；未選取時為空字串。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedPathText => string.Join(_pathSeparator, _navigator.Selection.Select(item => item.Name));

        /// <summary>
        /// 目前的選取是否停在葉節點，也就是整條路徑已經選到底。
        /// </summary>
        /// <remarks>
        /// 每條路徑的深度可以不同：在 A &gt; A1 &gt; A11 中，如果 A11 沒有子節點，選到第三層就算完成。
        /// </remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLeafSelected => IsLeaf(SelectedItem);

        /// <summary>
        /// 目前操作中的欄位索引：最深選取項所在的欄；未選取時為 0。
        /// </summary>
        /// <remarks>鍵盤焦點固定在這一欄，所以它和選取深度一致，不受控制項是否有焦點影響。</remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int FocusedLevel => _navigator.ActiveLevel;

        #endregion 選取查詢

        #region 選取操作

        /// <summary>
        /// 從第一欄開始，依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺到深的節點；以 <see cref="object.Equals(object)"/> 比對。</param>
        /// <returns>整條路徑都選到時為 true；中途找不到時，選取停在已選到的那一段，並回傳 false。</returns>
        /// <remarks>
        /// 不論成功與否，最後的選取都正好是已比對成功的那一段。路徑經過的每一層會記成「上次選的項目」，其他分支的記憶保留。
        /// 不管選了幾層，<see cref="SelectionChanged"/> 都只觸發一次。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> 為 null。</exception>
        public bool TrySelectPath(IEnumerable<ICascadeItem> path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var steps = path.ToList();
            var matched = new List<ICascadeItem>();
            var items = _roots;

            foreach (var step in steps)
            {
                // 存入清單中的那一個實例，而不是呼叫端傳入的，之後在欄位裡找選取項時才對得上。
                var found = items.FirstOrDefault(item => item != null && Equals(item, step));
                if (found == null) break;

                matched.Add(found);
                items = CascadeNavigator.ChildrenOf(found);
            }

            _navigator.SelectPath(matched);
            SyncColumns();

            OnSelectionChanged(CreateItemEventArgs());
            return matched.Count == steps.Count;
        }

        /// <summary>
        /// 忘掉所有分支的記憶，只保留目前的選取路徑；選取不變，所以不會觸發 <see cref="SelectionChanged"/>。
        /// </summary>
        /// <remarks>呼叫 <see cref="SetRoots"/> 或 <see cref="Clear"/> 時記憶也會一併清除，不需要另外呼叫。</remarks>
        public void ClearMemory()
        {
            _navigator.ClearMemory();
            SyncColumns();
        }

        /// <summary>
        /// 把鍵盤焦點移到目前操作的那一欄（<see cref="FocusedLevel"/>）；未選取時就是第一欄。
        /// 沒有欄位，或那一欄無法取得焦點（例如控制項已停用）時，不做任何事。
        /// </summary>
        public void FocusActiveColumn()
        {
            var level = _navigator.ActiveLevel;
            if (level >= _lists.Count) return;

            var list = _lists[level];
            if (list.CanFocus && !list.Focused) list.Focus();
        }

        /// <summary>
        /// 已改名為 <see cref="FocusActiveColumn"/>：鍵盤焦點固定在目前操作的那一欄，不會停在其他欄。
        /// </summary>
        [Obsolete("請改用 FocusActiveColumn：鍵盤焦點固定在目前操作的那一欄。")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void FocusFirstColumn() => FocusActiveColumn();

        #endregion 選取操作

        #region 事件

        /// <summary>
        /// 選取路徑改變後觸發，包含 ← / → 改變選取深度；呼叫 <see cref="SetRoots"/>、<see cref="Clear"/>、<see cref="TrySelectPath"/> 時也會觸發。
        /// </summary>
        /// <remarks>
        /// 參數包含完整的選取路徑、上一層節點、下一欄的內容，以及是否已選到葉節點；
        /// 未選取時 <see cref="CascadeItemEventArgs.Item"/> 為 null，<see cref="CascadeItemEventArgs.Children"/> 為第一欄的內容。
        /// </remarks>
        [Category("Cascade")]
        public event EventHandler<CascadeItemEventArgs> SelectionChanged;

        /// <summary>
        /// 使用者在葉節點上按 Enter 或雙擊時觸發。參數型別與 <see cref="SelectionChanged"/> 相同，<see cref="CascadeItemEventArgs.IsLeaf"/> 一定是 true。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler<CascadeItemEventArgs> ItemActivated;

        /// <summary>
        /// <see cref="ColumnWidth"/> 改變後觸發。
        /// </summary>
        /// <remarks>
        /// 不會出現在屬性視窗：設計階段用不到它，它的用途是讓資料繫結依照 {Property}Changed 慣例收到變更通知。
        /// </remarks>
        [Browsable(false)]
        public event EventHandler ColumnWidthChanged;

        /// <summary>
        /// 繪製每一列之前觸發；把 <see cref="CascadeDrawItemEventArgs.Handled"/> 設為 true，就會改用你自己的畫法。
        /// </summary>
        /// <remarks>供進階自繪使用，不會出現在屬性視窗，需要時請在程式碼中訂閱。</remarks>
        [Browsable(false)]
        public event EventHandler<CascadeDrawItemEventArgs> DrawItem;

        /// <summary>
        /// 觸發 <see cref="SelectionChanged"/>。
        /// </summary>
        protected virtual void OnSelectionChanged(CascadeItemEventArgs e) => SelectionChanged?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="ItemActivated"/>。
        /// </summary>
        protected virtual void OnItemActivated(CascadeItemEventArgs e) => ItemActivated?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="ColumnWidthChanged"/> 並重新排版。
        /// </summary>
        protected virtual void OnColumnWidthChanged(EventArgs e)
        {
            Relayout();
            ColumnWidthChanged?.Invoke(this, e);
        }

        /// <summary>
        /// 觸發 <see cref="DrawItem"/>；訂閱端沒有自行繪製時，畫出預設外觀。
        /// </summary>
        protected virtual void OnDrawItem(CascadeDrawItemEventArgs e)
        {
            DrawItem?.Invoke(this, e);
            if (!e.Handled) DrawDefaultItem(e);
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
        /// 導覽狀態改變後，重建欄位並觸發 <see cref="SelectionChanged"/>；沒有改變時不做任何事。
        /// </summary>
        private void ApplyNavigation(bool changed)
        {
            if (!changed) return;

            SyncColumns();
            OnSelectionChanged(CreateItemEventArgs());
        }

        /// <summary>
        /// 依目前的選取狀態組出 <see cref="SelectionChanged"/> 與 <see cref="ItemActivated"/> 共用的事件參數。
        /// </summary>
        /// <remarks>第一欄的內容只在未選取時才需要，有選取時不去複製它。</remarks>
        private CascadeItemEventArgs CreateItemEventArgs()
        {
            var path = _navigator.Selection.ToList();
            return new CascadeItemEventArgs(path, path.Count == 0 ? _roots : null);
        }

        private static bool IsLeaf(ICascadeItem item) => item != null && CascadeNavigator.ChildrenOf(item).Count == 0;

        // 沒有佔位項，SelectedIndex 為 -1 就是這一欄沒有選取。
        private static ICascadeItem ItemAt(ListBox list)
        {
            return list.SelectedIndex >= 0 ? list.Items[list.SelectedIndex] as ICascadeItem : null;
        }
    }
}