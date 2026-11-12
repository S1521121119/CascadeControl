using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 依導覽狀態建立、拆除與排列欄位。
    public partial class CascadeView
    {
        private const int MARGIN = 3;

        // SyncColumns 換內容、設定選取時都會觸發 SelectedIndexChanged，那不是使用者改選，要略過。
        private bool _syncing;

        // BeginColumnUpdate 可巢狀呼叫，只有最外層真的暫停／恢復繪製。
        private int _columnUpdateDepth;

        private bool _redrawSuspended;

        // 使用者用 ↑↓、打字或滑鼠改了某一欄的選取。
        private void OnColumnSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_syncing) return;

            var level = _lists.IndexOf(sender as ListBox);
            if (level < 0) return;

            var item = ItemAt(_lists[level]);
            if (item == null) return;

            ApplyNavigation(_navigator.Select(level, item));
        }

        /// <summary>
        /// 依導覽狀態重建欄位：要顯示哪幾欄、每欄的內容與選取、哪一欄可以取得焦點，最後重新排版。
        /// </summary>
        /// <remarks>
        /// 內容來源沒變的欄位只更新選取，不重建也不換內容。新建的 ListBox 在 Handle 建立的當下會先清成白底，
        /// 下一次繪製才畫上項目，中間那一瞬間的空白欄，就是改選時看到的閃爍。
        /// 原本有焦點時，先把焦點交給新的操作欄再拆掉多餘的欄位；順序反過來的話，
        /// 被拆掉的焦點欄會把焦點丟給表單上的其他控制項。拆完後再修正一次捲動範圍。
        /// </remarks>
        private void SyncColumns()
        {
            var hadFocus = ContainsFocus;
            var active = _navigator.ActiveLevel;
            var count = VisibleColumnCount();

            BeginColumnUpdate();
            _syncing = true;
            try
            {
                for (var level = 0; level < count; level++)
                {
                    var source = ColumnSource(level);
                    if (level >= _lists.Count)
                    {
                        AddColumn(ColumnItems(level), source);
                    }
                    else if (!Equals(_lists[level].Tag, source))
                    {
                        ReplaceItems(_lists[level], ColumnItems(level));
                        _lists[level].Tag = source;
                    }

                    var list = _lists[level];
                    var selected = level < _navigator.Depth ? _navigator.Record[level] : null;
                    if (!Equals(ItemAt(list), selected))
                        list.SelectedIndex = selected == null ? -1 : list.Items.IndexOf(selected);

                    // 只有操作欄能用 Tab 進入，焦點才不會停在其他欄。
                    list.TabStop = level == active;
                }

                // 先排好位置（Window 模式也在這裡決定哪幾欄要顯示）再移焦點：欄位取得焦點時，外層會把它捲進可視範圍，
                // 用的是當下的位置，新長出來的欄位還在舊位置的話，會把剛算好的捲動位置蓋掉。
                Relayout();

                // 暫停繪製期間 WM_SETREDRAW 會清掉控制項的 WS_VISIBLE，CanFocus 為 false，Focus() 不會有作用，
                // 所以直接呼叫 SetFocus；它不檢查可見性，WinForms 仍會照常收到 WM_SETFOCUS 並更新 ActiveControl。
                if (hadFocus && active < count)
                {
                    var target = _lists[active];
                    if (!target.Focused && target.Enabled && target.Visible && target.IsHandleCreated)
                        NativeMethods.SetFocus(target.Handle);
                }

                TrimColumns(count);
                ScrollToActive();
            }
            finally
            {
                _syncing = false;
                EndColumnUpdate();
            }
        }

        /// <summary>
        /// 依導覽狀態與 <see cref="ShowRememberedPath"/> 算出要建立的欄數。
        /// </summary>
        /// <remarks>
        /// 畫到最後一個要顯示的項目為止，它有子節點時再多一欄。
        /// <see cref="CascadeColumnSizeMode.Window"/> 不能捲動，看不到比操作欄的下一欄更深的欄位，所以一律照不顯示記憶的規則建立欄位。
        /// </remarks>
        private int VisibleColumnCount()
        {
            var showTrail = _showRememberedPath && !IsWindowMode;
            var last = showTrail ? _navigator.Record.Count : _navigator.Depth;

            if (last == 0) return _roots.Count > 0 ? 1 : 0;

            return last + (CascadeNavigator.ChildrenOf(_navigator.Record[last - 1]).Count > 0 ? 1 : 0);
        }

        // 第 level 欄內容的來源：第一欄是這次 SetRoots 的代表物件，其他欄是上一層的項目。
        private object ColumnSource(int level) => level == 0 ? _rootsKey : _navigator.Record[level - 1];

        private IReadOnlyList<ICascadeItem> ColumnItems(int level)
        {
            return level == 0 ? _roots : CascadeNavigator.ChildrenOf(_navigator.Record[level - 1]);
        }

        /// <summary>
        /// 換掉既有欄位的內容；捲動位置會回到頂端，和新建的欄位一樣。
        /// </summary>
        private static void ReplaceItems(ListBox list, IReadOnlyList<ICascadeItem> items)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(items.Cast<object>().ToArray());
            }
            finally
            {
                list.EndUpdate();
            }
        }

        /// <summary>
        /// 暫停整個控制項的繪製，直到呼叫對應的 <see cref="EndColumnUpdate"/> 為止。
        /// </summary>
        /// <remarks>
        /// 一次改選會連續做「拆欄、換內容、長欄、調整捲動範圍、捲動」，每一步都會讓畫面重畫一次，
        /// 中間狀態（欄高跳動、捲到一半）被畫出來就是閃爍，所以全部做完再一次重畫。
        /// 控制項不可見時不送 WM_SETREDRAW，因為恢復繪製時會順便把視窗設成可見。
        /// </remarks>
        private void BeginColumnUpdate()
        {
            if (_columnUpdateDepth++ > 0) return;

            SuspendLayout();

            if (IsHandleCreated && NativeMethods.IsWindowVisible(Handle))
            {
                NativeMethods.SendMessage(Handle, NativeMethods.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                _redrawSuspended = true;
            }
        }

        /// <summary>
        /// 結束 <see cref="BeginColumnUpdate"/>，恢復繪製並整個重畫一次。
        /// </summary>
        /// <remarks>
        /// 暫停期間的失效區域不會被記下來，所以要連子視窗與邊框一起重畫；
        /// 只改選取深度（← / →）時欄位內容不變，但配色變了，也靠這次重畫更新。
        /// 這次重畫有 <see cref="CreateParams"/> 設定的 WS_EX_COMPOSITED 做雙緩衝，不會看到一欄一欄刷新。
        /// </remarks>
        private void EndColumnUpdate()
        {
            if (--_columnUpdateDepth > 0) return;

            ResumeLayout();

            if (!_redrawSuspended) return;
            _redrawSuspended = false;

            NativeMethods.SendMessage(Handle, NativeMethods.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            NativeMethods.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.RDW_ERASE | NativeMethods.RDW_FRAME | NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ALLCHILDREN);
        }

        /// <summary>
        /// 在最右側新增一欄。
        /// </summary>
        private void AddColumn(IReadOnlyList<ICascadeItem> items, object source)
        {
            var level = _lists.Count;
            var list = new ListBox
            {
                Name = $"lstColumn{level + 1:00}",
                Location = ColumnLocation(level),
                Size = new Size(ActualColumnWidth, ColumnHeight),
                TabIndex = level,
                Tag = source,

                // ListBox 預設會把高度湊成整數列，關掉才能讓每一欄都等高。
                IntegralHeight = false,

                // 失去焦點後選取項仍是標準藍底，多欄一起看就分不出鍵盤在哪一欄，記憶項目也要另外上色，所以改為自繪。
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = ItemHeight,

                // 讓 GetItemText（打字跳到項目、無障礙功能）也使用 ICascadeItem.Text，而不是各實作自己的 ToString。
                FormattingEnabled = true,
            };

            list.Format += OnColumnFormat;
            list.Items.AddRange(items.Cast<object>().ToArray());

            list.SelectedIndexChanged += OnColumnSelectedIndexChanged;
            list.KeyDown += OnColumnKeyDown;
            list.MouseUp += OnColumnMouseUp;
            list.DoubleClick += OnColumnDoubleClick;
            list.DrawItem += OnColumnDrawItem;
            list.GotFocus += OnColumnGotFocus;
            list.LostFocus += OnColumnFocusChanged;

            // 順序不可對調：ColumnCollection 只放行已登記在 _lists 裡的欄位。
            _lists.Add(list);
            Controls.Add(list);
        }

        /// <summary>
        /// 改用自訂的子控制項集合，只允許加入控制項自己建立的欄位。
        /// </summary>
        /// <remarks>
        /// <see cref="Control.Parent"/> 的 setter 與 <see cref="Control.ControlCollection.AddRange"/> 最後都會走到
        /// <see cref="Control.ControlCollection.Add"/>，所以只要擋這一個入口就夠了。
        /// 移除不設限：欄位 Dispose、控制項本身 Dispose 時都會經過 Remove，擋了反而出錯。
        /// </remarks>
        protected override ControlCollection CreateControlsInstance() => new ColumnCollection(this);

        private sealed class ColumnCollection : ControlCollection
        {
            private readonly CascadeView _view;

            public ColumnCollection(CascadeView owner) : base(owner)
            {
                _view = owner;
            }

            public override void Add(Control value)
            {
                if (value != null && !_view._lists.Contains(value))
                {
                    throw new InvalidOperationException(
                        $"{nameof(CascadeView)} 的欄位由控制項自行建立與移除，不可加入其他控制項（{value.GetType().Name} '{value.Name}'）。");
                }

                base.Add(value);
            }
        }

        private static void OnColumnFormat(object sender, ListControlConvertEventArgs e)
        {
            if (e.ListItem is ICascadeItem item) e.Value = item.Text;
        }

        /// <summary>
        /// 拆掉第 <paramref name="keep"/> 欄（含）之後的所有欄位。
        /// </summary>
        private void TrimColumns(int keep)
        {
            for (var level = _lists.Count - 1; level >= keep; level--)
            {
                var list = _lists[level];
                _lists.RemoveAt(level);
                Controls.Remove(list);
                list.Dispose();
            }
        }

        /// <summary>
        /// 捲動到能看見操作欄，而且右緣剛好是「操作欄的下一欄」的位置；下一欄不存在時，右緣就是操作欄本身。
        /// </summary>
        /// <remarks>
        /// 右緣只在下一欄真的存在時才預留，所以資料有 N 層、一次顯示 N 欄時，停在最深一層不會在右邊空一格。
        /// 選到葉節點時欄數不變（操作欄移到原本的下一欄），← 從葉節點退回時欄數也不變，畫面都不會移動；
        /// 只有選到有子節點的項目、真的長出新的一欄時，畫面才往左移一格。
        /// 連兩欄都放不下時，改為讓操作欄靠左，以免把操作欄本身捲出畫面。
        /// <see cref="CascadeColumnSizeMode.Fill"/> 的平均欄寬放得下時，捲動位置對齊欄位邊界，一次正好看到 <see cref="DisplayColumnCount"/> 欄。
        /// <see cref="CascadeColumnSizeMode.Window"/> 改用隱藏欄位的方式呈現，不需要捲動。
        /// 沒有欄位時（包含設計階段）捲動範圍要歸零，否則設計工具會把非預設的 AutoScrollMinSize 寫進 Designer.cs。
        /// </remarks>
        private void ScrollToActive()
        {
            if (IsWindowMode || _lists.Count == 0)
            {
                if (AutoScrollMinSize != Size.Empty) AutoScrollMinSize = Size.Empty;
                if (AutoScrollPosition != Point.Empty) AutoScrollPosition = Point.Empty;
                return;
            }

            var level = FocusedLevel;
            var width = ActualColumnWidth;
            var step = width + ColumnGap;
            var clientWidth = ClientSize.Width;

            int wanted;
            var window = WindowColumns;
            if (window > 0)
            {
                wanted = WindowStart(_lists.Count, level, window) * step;
            }
            else
            {
                var left = ColumnLeft(level);
                var right = ColumnLeft(RightEdgeLevel(_lists.Count, level)) + width;
                wanted = Math.Max(0, Math.Min(left, right - clientWidth));
            }

            // 捲動範圍只算到最後一欄；捲動位置本身需要的寬度另外補上，免得被範圍擋住。
            var minSize = new Size(Math.Max(ColumnLeft(_lists.Count - 1) + width + MARGIN, wanted + clientWidth), 0);
            if (AutoScrollMinSize != minSize) AutoScrollMinSize = minSize;

            // 位置沒變就不要重設，設下去會讓所有欄位跟著重畫一次。AutoScrollPosition 讀出來是負值。
            var current = AutoScrollPosition;
            if (current.X != -wanted || current.Y != 0) AutoScrollPosition = new Point(wanted, 0);
        }

        /// <summary>
        /// 第 <paramref name="level"/> 欄該放的位置。
        /// </summary>
        /// <remarks>
        /// Location 是相對於「捲動後」的工作區原點，不是內容的左上角。已經捲動過時
        /// 如果直接填未捲動的座標，欄位會整個被推到右邊，捲動範圍也跟著暴增，所以要補上
        /// <see cref="ScrollableControl.AutoScrollPosition"/>（已捲動時為負值）。
        /// </remarks>
        private Point ColumnLocation(int level)
        {
            var scroll = AutoScrollPosition;
            return new Point(ColumnLeft(level) + scroll.X, MARGIN + scroll.Y);
        }

        /// <summary>
        /// 第 <paramref name="level"/> 欄左緣的 X 座標（以未捲動時為準）。
        /// </summary>
        /// <remarks>
        /// <see cref="CascadeColumnSizeMode.Window"/> 時從顯示範圍的第一欄開始排；範圍外的欄位是隱藏的，一律放在最左邊。
        /// </remarks>
        private int ColumnLeft(int level)
        {
            var step = ActualColumnWidth + ColumnGap;

            if (!IsWindowMode) return MARGIN + (level * step);

            var start = WindowStart(_lists.Count, FocusedLevel, WindowColumns);
            var offset = level - start;
            return offset >= 0 && offset < WindowColumns ? MARGIN + (offset * step) : MARGIN;
        }

        /// <summary>
        /// 依 <see cref="ColumnSizeMode"/> 算出的實際欄寬，不會小於 <see cref="ColumnWidth"/>。
        /// </summary>
        private int ActualColumnWidth
        {
            get
            {
                if (IsWindowMode) return Math.Max(ColumnWidth, FillWidth(WindowColumns));

                if (_columnSizeMode == CascadeColumnSizeMode.Fill && _displayColumnCount > 0)
                    return Math.Max(ColumnWidth, FillWidth(_displayColumnCount));

                return ColumnWidth;
            }
        }

        // 讓 count 欄（含欄距與左右邊距）剛好填滿寬度時的欄寬。
        private int FillWidth(int count) => (ClientSize.Width - (MARGIN * 2) - ((count - 1) * ColumnGap)) / count;

        private bool IsWindowMode => _columnSizeMode == CascadeColumnSizeMode.Window;

        /// <summary>
        /// 畫面上一次要看到的欄數；0 表示不分頁，照 <see cref="ColumnWidth"/> 排列並捲動。
        /// </summary>
        /// <remarks>
        /// <see cref="CascadeColumnSizeMode.Fill"/>：平均欄寬放得下時是 <see cref="DisplayColumnCount"/>，
        /// 放不下（只能改用最小欄寬）時為 0。
        /// <see cref="CascadeColumnSizeMode.Window"/>：<see cref="DisplayColumnCount"/>（0 當成 1），
        /// 平均欄寬放不下時減少到放得下的欄數，至少 1 欄。
        /// </remarks>
        private int WindowColumns
        {
            get
            {
                switch (_columnSizeMode)
                {
                    case CascadeColumnSizeMode.Fill:
                        return _displayColumnCount > 0 && FillWidth(_displayColumnCount) >= ColumnWidth ? _displayColumnCount : 0;

                    case CascadeColumnSizeMode.Window:
                        var count = Math.Max(1, _displayColumnCount);
                        if (FillWidth(count) >= ColumnWidth) return count;

                        var fits = (ClientSize.Width - (MARGIN * 2) + ColumnGap) / (ColumnWidth + ColumnGap);
                        return Math.Max(1, Math.Min(count, fits));

                    default:
                        return 0;
                }
            }
        }

        /// <summary>
        /// 畫面右緣要對齊的欄位：操作欄的下一欄存在時是下一欄，否則是操作欄本身。
        /// </summary>
        /// <param name="columnCount">目前建立的欄數。</param>
        /// <param name="activeLevel">操作欄的索引。</param>
        internal static int RightEdgeLevel(int columnCount, int activeLevel)
        {
            return Math.Max(0, Math.Min(columnCount - 1, activeLevel + 1));
        }

        /// <summary>
        /// 一次顯示 <paramref name="window"/> 欄時，畫面最左邊那一欄的索引。
        /// </summary>
        /// <param name="columnCount">目前建立的欄數。</param>
        /// <param name="activeLevel">操作欄的索引。</param>
        /// <param name="window">一次顯示的欄數，至少 1。</param>
        /// <remarks>
        /// 右緣對齊 <see cref="RightEdgeLevel"/>，但不讓操作欄被推出左邊：只顯示一欄時，顯示的是操作欄而不是它的下一欄。
        /// </remarks>
        internal static int WindowStart(int columnCount, int activeLevel, int window)
        {
            if (window <= 0) return 0;

            var right = RightEdgeLevel(columnCount, activeLevel);
            return Math.Max(0, Math.Min(activeLevel, right - window + 1));
        }

        /// <summary>
        /// 依目前控制項高度算出的欄高。
        /// </summary>
        /// <remarks>
        /// 水平捲軸出現後 ClientSize.Height 會變小，直接拿來算，欄高就會隨捲軸出現或消失而跳動。
        /// 所以先把捲軸佔掉的高度加回來，再固定預留一條捲軸的高度：
        /// 不論有幾欄，欄高都一樣，也不會因為水平捲軸而多冒出一條垂直捲軸。
        /// </remarks>
        private int ColumnHeight
        {
            get
            {
                var clientHeight = ClientSize.Height
                    + (HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);

                return Math.Max(0, clientHeight - (MARGIN * 2) - SystemInformation.HorizontalScrollBarHeight);
            }
        }

        /// <summary>
        /// 每一列的高度。
        /// </summary>
        /// <remarks>
        /// OwnerDrawFixed 的 ItemHeight 不會隨字型自動調整（固定是預設的 13），
        /// 字型放大後文字會被上下裁掉，所以改用控制項字型的高度。ListBox 的 ItemHeight 上限是 255。
        /// </remarks>
        private int ItemHeight => Math.Min(255, Font.Height);

        /// <summary>
        /// 依目前的欄寬與欄高重新擺放所有欄位，決定哪幾欄要顯示，並修正捲動範圍。
        /// </summary>
        /// <remarks>
        /// 沒有欄位時仍要修正捲動範圍，否則清空後會留下舊的水平捲軸。
        /// <see cref="CascadeColumnSizeMode.Window"/> 一次顯示的欄數會隨控制項寬度改變，所以顯示與否也在這裡決定，而不是只在改選時。
        /// </remarks>
        private void Relayout()
        {
            if (_lists.Count > 0)
            {
                var size = new Size(ActualColumnWidth, ColumnHeight);
                var windowed = IsWindowMode;
                var window = WindowColumns;
                var start = windowed ? WindowStart(_lists.Count, FocusedLevel, window) : 0;

                SuspendLayout();
                try
                {
                    for (var level = 0; level < _lists.Count; level++)
                    {
                        var list = _lists[level];
                        list.Bounds = new Rectangle(ColumnLocation(level), size);
                        list.Visible = !windowed || (level >= start && level < start + window);
                    }
                }
                finally
                {
                    ResumeLayout();
                }
            }

            ScrollToActive();
        }
    }
}