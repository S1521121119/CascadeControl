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
    /// 整個控制項只靠一條規則：節點有子節點，就為它的子節點開一欄。樹根也一樣，
    /// 所以選單有內容時，載入後自然會有第一欄；空選單則一欄都沒有，不需要特別處理。
    /// 選取狀態直接放在各欄的 SelectedIndex，類別本身不另外記一份，以免兩邊不同步。
    /// 鍵盤操作：↑↓ 在同一欄內移動（ListBox 原生行為），→ 進入下一欄，← 把焦點移回上一欄；
    /// Enter 在葉節點上觸發 <see cref="ItemActivated"/>，不是葉節點時等同 →。
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

        private readonly List<ListBox> _lists = new List<ListBox>();

        private int _columnWidth = DEFAULT_COLUMN_WIDTH;
        private int _columnGap = DEFAULT_COLUMN_GAP;
        private Color _inactiveSelectionBackColor = Color.Empty;
        private string _pathSeparator = DEFAULT_PATH_SEPARATOR;

        // TrySelectPath 會連續改好幾欄，期間不逐層觸發事件，結束後再統一觸發一次。
        private bool _bulkSelecting;

        /// <summary>
        /// 建立控制項。欄位多到放不下時會出現水平捲軸。
        /// </summary>
        public CascadeView()
        {
            AutoScroll = true;
        }

        #region 外觀

        /// <summary>
        /// 每一欄的寬度（像素）。
        /// </summary>
        [Category("Cascade")]
        [Description("每一欄的寬度（像素）。要調整欄位字型，請設定這個控制項的 Font。")]
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
        /// 重新設定第一層的內容：第一欄換成新內容，其他欄位全部移除；沒有內容時一欄都不留。
        /// </summary>
        /// <param name="roots">第一層節點；傳 null 或空集合表示清空。</param>
        /// <remarks>執行後會觸發一次 <see cref="SelectionChanged"/>（未選取的狀態）。</remarks>
        public void SetRoots(IEnumerable<ICascadeItem> roots)
        {
            BeginColumnUpdate();
            try
            {
                ShowColumn(0, roots?.ToList());
                ScrollToActive();
            }
            finally
            {
                EndColumnUpdate();
            }

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
        public ICascadeItem SelectedItem => Selection.LastOrDefault();

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ICascadeItem> SelectedPath => Selection.ToList();

        /// <summary>
        /// 目前選取路徑的文字表示，以 <see cref="PathSeparator"/> 串接；未選取時為空字串。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedPathText => string.Join(_pathSeparator, Selection.Select(item => item.Text));

        /// <summary>
        /// 目前的選取是否停在葉節點，也就是整條路徑已經選到底。
        /// </summary>
        /// <remarks>
        /// 每條路徑的深度可以不同：在 A &gt; A1 &gt; A11 中，如果 A11 沒有子節點，選到第三層就算完成。
        /// </remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLeafSelected => IsLeaf(SelectedItem);

        /// <summary>
        /// 目前操作中的欄位索引：優先取有鍵盤焦點的那一欄；沒有欄位有焦點時，取最深一個有選取的欄；兩者都沒有時為 0。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int FocusedLevel
        {
            get
            {
                var focused = _lists.FindLastIndex(list => list.Focused);
                if (focused >= 0) return focused;

                return Math.Max(0, _lists.FindLastIndex(list => list.SelectedIndex >= 0));
            }
        }

        #endregion 選取查詢

        #region 選取操作

        /// <summary>
        /// 從第一欄開始，依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺到深的節點；以 <see cref="object.Equals(object)"/> 比對。</param>
        /// <returns>整條路徑都選到時為 true；中途找不到時，選取停在已選到的那一段，並回傳 false。</returns>
        /// <remarks>
        /// 不論成功與否，最後的選取都正好是已比對成功的那一段，更深的舊選取會被清除。
        /// 不管選了幾層，<see cref="SelectionChanged"/> 都只觸發一次。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> 為 null。</exception>
        public bool TrySelectPath(IEnumerable<ICascadeItem> path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var steps = path.ToList();
            var matched = 0;

            BeginColumnUpdate();
            _bulkSelecting = true;
            try
            {
                foreach (var step in steps)
                {
                    if (matched >= _lists.Count) break;

                    var index = _lists[matched].Items.IndexOf(step);
                    if (index < 0) break;

                    _lists[matched].SelectedIndex = index;
                    matched++;
                }

                // 同一欄原本就選著同一項時不會觸發 SelectedIndexChanged，後面的舊選取還在，要手動清掉。
                if (matched < _lists.Count) _lists[matched].SelectedIndex = -1;

                ScrollToActive();
            }
            finally
            {
                _bulkSelecting = false;
                EndColumnUpdate();
            }

            OnSelectionChanged(CreateItemEventArgs());
            return matched == steps.Count;
        }

        /// <summary>
        /// 把鍵盤焦點移到第一欄。沒有欄位，或第一欄無法取得焦點（例如控制項已停用）時，不做任何事。
        /// </summary>
        public void FocusFirstColumn()
        {
            var first = _lists.FirstOrDefault();
            if (first?.CanFocus == true) first.Focus();
        }

        #endregion 選取操作

        #region 事件

        /// <summary>
        /// 任何一層的選取改變後觸發；呼叫 <see cref="SetRoots"/>、<see cref="Clear"/> 時也會觸發。
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

        // 深一層的欄位是因為淺一層有選取才長出來的，所以選取一定是連續的，
        // 直接由淺至深收集即可，只有最後一欄可能還沒選。
        private IEnumerable<ICascadeItem> Selection => _lists.Select(ItemAt).Where(item => item != null);

        /// <summary>
        /// 依目前的選取狀態組出 <see cref="SelectionChanged"/> 與 <see cref="ItemActivated"/> 共用的事件參數。
        /// </summary>
        /// <remarks>第一欄的內容只在未選取時才需要，有選取時不去複製它。</remarks>
        private CascadeItemEventArgs CreateItemEventArgs()
        {
            var path = Selection.ToList();
            var roots = path.Count == 0 && _lists.Count > 0 ? _lists[0].Items.OfType<ICascadeItem>() : null;
            return new CascadeItemEventArgs(path, roots);
        }

        private static bool IsLeaf(ICascadeItem item) => item != null && !(item.Children?.Count > 0);

        // 沒有佔位項，SelectedIndex 為 -1 就是這一欄還沒選。
        private static ICascadeItem ItemAt(ListBox list)
        {
            return list.SelectedIndex >= 0 ? list.Items[list.SelectedIndex] as ICascadeItem : null;
        }
    }
}