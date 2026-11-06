using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 欄位的生長、拆除與排版。
    public partial class CascadeView
    {
        private const int MARGIN = 3;

        // 換欄位內容時 Items.Clear 也會觸發 SelectedIndexChanged，那不是使用者改選，要略過。
        private bool _replacingItems;

        // BeginColumnUpdate 可巢狀呼叫（TrySelectPath 裡會逐欄觸發 SelectedIndexChanged），只有最外層真的暫停／恢復繪製。
        private int _columnUpdateDepth;

        private bool _redrawSuspended;

        private void OnColumnSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_replacingItems) return;

            var level = _lists.IndexOf(sender as ListBox);
            if (level < 0) return;

            BeginColumnUpdate();
            try
            {
                // 這一層改選後，後面各欄的內容都過時了：下一欄換成新選取項的子節點，更深的全部拆掉。
                ShowColumn(level + 1, ItemAt(_lists[level])?.Children);

                if (!_bulkSelecting) ScrollToActive();
            }
            finally
            {
                EndColumnUpdate();
            }

            if (_bulkSelecting) return;

            OnSelectionChanged(CreateItemEventArgs());
        }

        /// <summary>
        /// 讓第 <paramref name="level"/> 欄顯示 <paramref name="items"/>，並拆掉更深的欄位；
        /// <paramref name="items"/> 為空時連第 <paramref name="level"/> 欄一起拆掉。
        /// </summary>
        /// <remarks>
        /// 那一欄已經存在時只換內容、不重建。新建的 ListBox 在 Handle 建立的當下會先清成白底，
        /// 下一次繪製才畫上項目，中間那一瞬間的空白欄，就是改選時看到的閃爍。
        /// </remarks>
        private void ShowColumn(int level, IReadOnlyList<ICascadeItem> items)
        {
            if (!(items?.Count > 0))
            {
                TrimColumns(level);
                return;
            }

            TrimColumns(level + 1);

            if (level < _lists.Count) ReplaceItems(_lists[level], items);
            else AddColumn(items);
        }

        /// <summary>
        /// 換掉既有欄位的內容；選取與捲動位置都會回到初始狀態，和新建的欄位一樣。
        /// </summary>
        private void ReplaceItems(ListBox list, IReadOnlyList<ICascadeItem> items)
        {
            _replacingItems = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(items.Cast<object>().ToArray());
            }
            finally
            {
                list.EndUpdate();
                _replacingItems = false;
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
        private void AddColumn(IReadOnlyList<ICascadeItem> items)
        {
            var level = _lists.Count;
            var list = new ListBox
            {
                Name = $"lstColumn{level + 1:00}",
                Location = ColumnLocation(level),
                Size = new Size(ColumnWidth, ColumnHeight),
                TabIndex = level,
                // ListBox 預設會把高度湊成整數列，關掉才能讓每一欄都等高。
                IntegralHeight = false,

                // 失去焦點後選取項仍是標準藍底，多欄一起看就分不出鍵盤在哪一欄，所以改為自繪。
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = ItemHeight,

                // 讓 GetItemText（打字跳到項目、無障礙功能）也使用 ICascadeItem.Text，而不是各實作自己的 ToString。
                FormattingEnabled = true,
            };

            list.Format += OnColumnFormat;
            list.Items.AddRange(items.Cast<object>().ToArray());

            list.SelectedIndexChanged += OnColumnSelectedIndexChanged;
            list.KeyDown += OnColumnKeyDown;
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
        /// 捲動到焦點欄右邊剛好放得下一整欄的位置。
        /// </summary>
        /// <remarks>
        /// 焦點欄在畫面上的位置只和它的層數與控制項寬度有關，和「下一層有沒有長出來」無關，
        /// 所以選到葉節點時，焦點欄不會突然往右跳一格，視線才不會跟丟。
        /// 連兩欄都放不下時，改為讓焦點欄靠左，以免把焦點欄本身捲出畫面。
        /// </remarks>
        private void ScrollToActive()
        {
            // 最後一欄右邊固定多留一整欄的空間。少了這段，選到葉節點時會捲不過去，
            // 焦點欄被推到最右邊，就是這一下跳動讓人跟丟。
            var minSize = new Size(ColumnLeft(_lists.Count) + ColumnWidth + MARGIN, 0);
            if (AutoScrollMinSize != minSize) AutoScrollMinSize = minSize;

            var left = ColumnLeft(FocusedLevel);
            var nextRight = left + ColumnWidth + ColumnGap + ColumnWidth;

            // 位置沒變就不要重設，設下去會讓所有欄位跟著重畫一次。AutoScrollPosition 讀出來是負值。
            var wanted = Math.Max(0, Math.Min(left, nextRight - ClientSize.Width));
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
        private int ColumnLeft(int level) => MARGIN + (level * (ColumnWidth + ColumnGap));

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
        /// 依目前的欄寬與欄高重新擺放所有欄位，並修正捲動範圍。
        /// </summary>
        private void Relayout()
        {
            if (_lists.Count == 0) return;

            var size = new Size(ColumnWidth, ColumnHeight);

            SuspendLayout();
            try
            {
                for (var level = 0; level < _lists.Count; level++)
                    _lists[level].Bounds = new Rectangle(ColumnLocation(level), size);
            }
            finally
            {
                ResumeLayout();
            }

            ScrollToActive();
        }
    }
}