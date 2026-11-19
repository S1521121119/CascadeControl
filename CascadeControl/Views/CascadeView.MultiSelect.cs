using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// 同一欄內的選取手勢，對應檔案總管的規則。
    /// </summary>
    internal enum SelectionGesture
    {
        /// <summary>點選、↑↓ 等一般導覽：清除多選，焦點與錨點移到該項。</summary>
        Plain,

        /// <summary>Ctrl+點選、Ctrl+Space：切換該項，其他保留；焦點與錨點移到該項。</summary>
        Toggle,

        /// <summary>Shift+點選、Shift+方向鍵：多選換成「錨點～該項」；錨點不動，焦點移到該項。</summary>
        Range,

        /// <summary>Ctrl+Shift+點選、Ctrl+Shift+方向鍵：把「錨點～該項」加進原本的多選；錨點不動，焦點移到該項。</summary>
        AddRange,

        /// <summary>Ctrl+方向鍵：只移焦點，多選與錨點不變。</summary>
        FocusOnly,
    }

    // 同一欄內的多選，比照檔案總管：Ctrl / Shift / Ctrl+Shift 點選與方向鍵、Ctrl+Space、拖曳框選、Ctrl+A。
    // 焦點就是選取路徑在這一欄的節點，右邊的欄位顯示它的子節點；多選決定 Copy / Cut / Delete 的對象。
    public partial class CascadeView
    {
        // 多選的節點；只會在同一欄（_markColumnIndex）裡。_markSource 是那一欄的內容來源，欄位換了內容多選就作廢。
        private readonly HashSet<ICascadeNode> _marks = new HashSet<ICascadeNode>();

        private int _markColumnIndex = -1;
        private object _markSource;

        // Shift 範圍選取的起點。一般導覽（點選、方向鍵、跳轉）與 Ctrl 切換會把它移到該節點，Shift 系列與只移焦點不動它。
        // 多選變成 0 個時仍保留；不在目前這一欄時，範圍改從這一欄的焦點節點算起（見 AnchorIndex）。
        private ICascadeNode _anchorNode;

        // 進行中的框選；沒有在框選時為 null。
        private BandPress _band;

        /// <summary>
        /// 多選的節點，依欄位中的順序排列；沒有多選時為空集合。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 多選只會在同一欄裡，規則比照檔案總管。每一欄有三樣狀態：多選、焦點（選取路徑在這一欄的節點，畫虛線框，右邊的欄位顯示它的子節點）、
        /// 錨點（Shift 範圍選取的起點）。沒有多選時，焦點節點本身就算選取（藍色），Copy / Cut / Delete 的對象就是 <see cref="SelectedNode"/>。
        /// </para>
        /// <list type="table">
        /// <listheader><term>操作</term><description>多選／焦點／錨點</description></listheader>
        /// <item><term>點選、↑↓ Home End PgUp PgDn、數字或字母跳轉</term><description>清除／移到該項／移到該項</description></item>
        /// <item><term>Ctrl+點選</term><description>切換該項，其他保留（第一次時原本選取的焦點節點也算進去）／移到該項／移到該項</description></item>
        /// <item><term>Shift+點選、Shift+方向鍵</term><description>換成「錨點～該項」／移到該項／不動</description></item>
        /// <item><term>Ctrl+Shift+點選、Ctrl+Shift+方向鍵</term><description>把「錨點～該項」加進原本的多選／移到該項／不動</description></item>
        /// <item><term>Ctrl+方向鍵</term><description>不變（原本只有焦點選取時，它留下來變成多選）／移到該項／不動</description></item>
        /// <item><term>Ctrl+Space</term><description>切換焦點項／不動／移到焦點項</description></item>
        /// </list>
        /// <para>
        /// 另外：從欄位空白處拖曳、或按住 Ctrl / Shift 拖曳是框選（Ctrl 加入、其他取代），Ctrl+A 選取操作欄的全部節點，Esc 取消多選。
        /// 錨點在多選變成 0 個時仍保留；不在目前這一欄時，範圍從這一欄的焦點節點算起。
        /// 多選的節點畫成 <see cref="MultiSelectBackColor"/>；有多選時，Copy、Cut、Delete 的對象是多選的節點，而不是 <see cref="SelectedNode"/>。
        /// </para>
        /// </remarks>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ICascadeNode> MultiSelectedNodes =>
            _marks.Count == 0 ? NoNodes : SafeColumnNodes(_markColumnIndex).Where(_marks.Contains).ToList();

        /// <summary>
        /// 多選的節點在哪一欄（0-based）；沒有多選時為 -1。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MultiSelectedColumnIndex => _marks.Count > 0 ? _markColumnIndex : -1;

        /// <summary>
        /// <see cref="MultiSelectedNodes"/> 改變後觸發。
        /// </summary>
        [Category("Cascade")]
        public event EventHandler MultiSelectionChanged;

        /// <summary>
        /// 觸發 <see cref="MultiSelectionChanged"/>。
        /// </summary>
        protected virtual void OnMultiSelectionChanged(EventArgs e) => MultiSelectionChanged?.Invoke(this, e);

        /// <summary>
        /// 多選第 <paramref name="columnIndex"/> 欄的全部節點；選取路徑不變。
        /// </summary>
        /// <param name="columnIndex">欄位索引（0-based）。</param>
        /// <returns>那一欄存在而且有節點時為 true。</returns>
        public bool SelectAllInColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _lists.Count) return false;

            var nodes = SafeColumnNodes(columnIndex);
            if (nodes.Count == 0) return false;

            SetMarks(columnIndex, nodes);
            return true;
        }

        /// <summary>
        /// 取消多選；本來就沒有多選時不做任何事。
        /// </summary>
        public void ClearMultiSelection()
        {
            if (_marks.Count == 0) return;

            InvalidateColumn(_markColumnIndex);
            _marks.Clear();
            _markColumnIndex = -1;
            _markSource = null;
            OnMultiSelectionChanged(EventArgs.Empty);
        }

        private bool IsMarked(int columnIndex, ICascadeNode node) =>
            _marks.Count > 0 && columnIndex == _markColumnIndex && node != null && _marks.Contains(node);

        // 這一欄是否正顯示多選：是的話，這一欄的選取外觀只看多選，不看 ListBox 本身的選取項。
        private bool IsMarkingColumn(int columnIndex) => _marks.Count > 0 && columnIndex == _markColumnIndex;

        // 以 nodes 取代多選；nodes 是空的就等於取消多選。
        private void SetMarks(int columnIndex, IEnumerable<ICascadeNode> nodes)
        {
            var next = new HashSet<ICascadeNode>(nodes.Where(node => node != null));
            if (next.Count == 0 || columnIndex < 0 || columnIndex >= _lists.Count)
            {
                ClearMultiSelection();
                return;
            }

            if (columnIndex == _markColumnIndex && _marks.SetEquals(next)) return;

            if (columnIndex != _markColumnIndex) InvalidateColumn(_markColumnIndex);

            _marks.Clear();
            _marks.UnionWith(next);
            _markColumnIndex = columnIndex;
            _markSource = ColumnSource(columnIndex);
            InvalidateColumn(columnIndex);
            OnMultiSelectionChanged(EventArgs.Empty);
        }

        /// <summary>
        /// 欄位重建之後：多選的那一欄已經不在、或換了內容時，多選作廢。
        /// </summary>
        private void ValidateMarks()
        {
            if (_marks.Count == 0) return;

            if (_markColumnIndex >= _lists.Count || !Equals(_lists[_markColumnIndex].Tag, _markSource))
            {
                ClearMultiSelection();
                return;
            }

            // 節點可能被外部刪掉了，只留還在欄位裡的。
            var nodes = SafeColumnNodes(_markColumnIndex);
            if (_marks.Any(node => !nodes.Contains(node))) SetMarks(_markColumnIndex, nodes.Where(_marks.Contains));
        }

        /// <summary>
        /// 同一欄內所有選取手勢的入口（滑鼠與鍵盤共用），依檔案總管的規則一次更新多選、焦點與錨點。
        /// </summary>
        /// <param name="columnIndex">手勢發生的那一欄。</param>
        /// <param name="node">被點的節點，或方向鍵的目標節點；焦點會移到它身上（<see cref="SelectionGesture.Toggle"/> 由 Ctrl+Space 觸發時就是焦點本身）。</param>
        /// <param name="gesture">手勢種類，見 <see cref="SelectionGesture"/>。</param>
        /// <remarks>
        /// 多選與錨點先更新，焦點最後移動，所以 <see cref="SelectedPathChanged"/> 的處理常式看得到最終的多選。
        /// 移動焦點只會重建右邊的欄位，這一欄的內容不變，多選因此保留（見 <see cref="ValidateMarks"/>）。
        /// </remarks>
        private void ApplySelectionGesture(int columnIndex, ICascadeNode node, SelectionGesture gesture)
        {
            if (columnIndex < 0 || columnIndex >= _lists.Count || node == null) return;

            var list = _lists[columnIndex];
            var index = list.Items.IndexOf(node);
            if (index < 0) return;

            switch (gesture)
            {
                case SelectionGesture.Plain:
                    ClearMultiSelection();
                    ApplyNavigation(_navigator.SelectNodeAt(columnIndex, node));
                    _anchorNode = node;
                    return;

                case SelectionGesture.Toggle:
                    {
                        var marks = SeedMarks(columnIndex);
                        if (!marks.Remove(node)) marks.Add(node);
                        _anchorNode = node;
                        SetMarks(columnIndex, marks);
                        break;
                    }

                case SelectionGesture.Range:
                case SelectionGesture.AddRange:
                    {
                        // 錨點不動；先算錨點再改任何狀態，它可能是借用的焦點節點。
                        var anchorIndex = AnchorIndex(columnIndex, index);
                        var marks = gesture == SelectionGesture.AddRange ? SeedMarks(columnIndex) : new List<ICascadeNode>();
                        for (var i = Math.Min(anchorIndex, index); i <= Math.Max(anchorIndex, index); i++)
                        {
                            if (list.Items[i] is ICascadeNode item && !(item is EditPlaceholder) && !marks.Contains(item)) marks.Add(item);
                        }

                        _anchorNode = list.Items[anchorIndex] as ICascadeNode;
                        SetMarks(columnIndex, marks);
                        break;
                    }

                case SelectionGesture.FocusOnly:
                    // 沒有多選時焦點節點本身就是選取（藍色），只移焦點時要把它留下來，變成多選。
                    if (!IsMarkingColumn(columnIndex)) SetMarks(columnIndex, SeedMarks(columnIndex));
                    break;
            }

            MoveFocus(columnIndex, node);
        }

        /// <summary>
        /// 只把焦點（選取路徑在這一欄的節點）移到 <paramref name="node"/>，保留多選；右邊的欄位跟著換成它的子節點。
        /// </summary>
        private void MoveFocus(int columnIndex, ICascadeNode node)
        {
            if (!_navigator.SelectNodeAt(columnIndex, node)) return;

            SyncColumns();
            OnSelectedPathChanged(CreateNodeEventArgs());
        }

        /// <summary>
        /// Ctrl+Space：切換焦點節點是否多選；錨點移到焦點節點。沒有選取時不做任何事。
        /// </summary>
        private void ToggleFocusedNode()
        {
            var columnIndex = ActiveColumnIndex;
            if (SelectedNode == null || columnIndex >= _navigator.SelectedDepth) return;

            ApplySelectionGesture(columnIndex, SelectedNode, SelectionGesture.Toggle);
        }

        // 範圍選取的起點在這一欄的索引：錨點在這一欄裡就用它；否則用這一欄的焦點節點；都沒有時就是目標本身。
        private int AnchorIndex(int columnIndex, int targetIndex)
        {
            var list = _lists[columnIndex];

            var index = _anchorNode == null ? -1 : list.Items.IndexOf(_anchorNode);
            if (index >= 0) return index;

            if (columnIndex < _navigator.SelectedDepth)
            {
                index = list.Items.IndexOf(_navigator.PathWithTrail[columnIndex]);
                if (index >= 0) return index;
            }

            return targetIndex;
        }

        private void InvalidateColumn(int columnIndex)
        {
            if (columnIndex >= 0 && columnIndex < _lists.Count) _lists[columnIndex].Invalidate();
        }

        // 這一欄目前的選取集合：同一欄已有多選就是多選；沒有多選時是焦點節點本身（它畫成藍色，也是 Copy / Delete 的對象）。
        // Ctrl 切換、Ctrl+Shift 加入範圍、Ctrl 只移焦點都以它為起點，和檔案總管一樣。
        private List<ICascadeNode> SeedMarks(int columnIndex)
        {
            if (IsMarkingColumn(columnIndex)) return MultiSelectedNodes.ToList();

            var seed = new List<ICascadeNode>();
            if (columnIndex < _navigator.SelectedDepth) seed.Add(_navigator.PathWithTrail[columnIndex]);
            return seed;
        }

        /// <summary>
        /// 左鍵按下時判斷要不要開始多選：按住 Ctrl 或 Shift 時不論按在哪裡，都沒按時按在欄位空白處（最後一列下方）。
        /// </summary>
        /// <returns>true 表示這一下不交給 ListBox 處理（它會在按下的當下就改選）。</returns>
        /// <remarks>
        /// 原生 ListBox 按在空白處時會選到最近的一列，選取就跳回那一欄；點空白處不等於選了節點，所以一律攔下。
        /// 放開時沒有拖曳過：按在節點上依修飾鍵交給 <see cref="ApplySelectionGesture"/>（Ctrl 切換、Shift 範圍、Ctrl+Shift 加入範圍），
        /// 沒按修飾鍵點空白處交給 <see cref="OnColumnBlankMouseClick"/>。拖曳過就是框選：按住 Ctrl 加入原本的多選，其他取代。
        /// 要先抓住滑鼠再記錄：抓的時候觸發的 MouseCaptureChanged 會清掉記錄。
        /// </remarks>
        private bool TryBeginBandPress(ListBox list, Point location, bool control, bool shift)
        {
            var columnIndex = _lists.IndexOf(list);
            if (columnIndex < 0) return false;

            var hit = TryHitRow(list, location, out _, out _);
            if (hit && !control && !shift) return false;

            var baseMarks = control ? SeedMarks(columnIndex) : new List<ICascadeNode>();

            list.Capture = true;
            _band = new BandPress(list, hit ? list.IndexFromPoint(location) : -1, location, list.TopIndex, control, baseMarks)
            {
                Shift = shift,
            };
            return true;
        }

        // 框選結束（放開、失去滑鼠）時拿掉矩形框。
        private void EndBand()
        {
            var band = _band;
            _band = null;
            band?.HideFrame();
        }

        private void OnColumnMouseMove(object sender, MouseEventArgs e)
        {
            var band = _band;
            if (band == null || !ReferenceEquals(sender, band.List)) return;

            if (!band.Dragged)
            {
                var drag = SystemInformation.DragSize;
                if (Math.Abs(e.X - band.Start.X) <= drag.Width / 2 && Math.Abs(e.Y - band.Start.Y) <= drag.Height / 2) return;

                band.Dragged = true;
            }

            UpdateBand(band, e.Location, dragging: true);
        }

        /// <summary>
        /// 框選範圍是按下那一列到目前這一列之間的所有節點；從空白處開始時，起點當成最後一列的下方。
        /// 拖曳中（<paramref name="dragging"/>）拖到欄位上下緣之外時捲動一列，並移動矩形框；放開時只算最後的範圍。
        /// </summary>
        private void UpdateBand(BandPress band, Point location, bool dragging)
        {
            var list = band.List;
            var columnIndex = _lists.IndexOf(list);
            if (columnIndex < 0) return;

            if (dragging)
            {
                if (location.Y < 0 && list.TopIndex > 0) list.TopIndex--;
                else if (location.Y >= list.ClientSize.Height) list.TopIndex++;

                band.ShowFrame(location);
            }

            var count = list.Items.Count;
            var current = RowAt(list, location.Y);
            var start = band.StartIndex >= 0 ? band.StartIndex : count;
            var low = Math.Min(start, current);
            var high = Math.Min(Math.Max(start, current), count - 1);

            var marks = band.Additive ? new List<ICascadeNode>(band.BaseMarks) : new List<ICascadeNode>();
            for (var index = low; index <= high; index++)
            {
                if (list.Items[index] is ICascadeNode node && !marks.Contains(node)) marks.Add(node);
            }

            SetMarks(columnIndex, marks);
        }

        // y 落在第幾列：最後一列下方回傳列數（代表空白處），上方夾到 0。列高固定（OwnerDrawFixed），直接用 TopIndex 換算。
        private static int RowAt(ListBox list, int y)
        {
            var row = list.TopIndex + (int)Math.Floor((double)y / Math.Max(1, list.ItemHeight));
            return Math.Max(0, Math.Min(row, list.Items.Count));
        }

        /// <summary>
        /// 放開左鍵，結束多選的這一下。
        /// </summary>
        private void EndBandPress(int columnIndex, ListBox list, Point location)
        {
            var band = _band;
            EndBand();
            if (list.Capture) list.Capture = false;

            if (band.Dragged)
            {
                UpdateBand(band, location, dragging: false);
            }
            else if (band.StartIndex >= 0)
            {
                // 按在節點上又沒拖曳：只有按住 Ctrl 或 Shift 才會走到這裡（見 TryBeginBandPress），依修飾鍵決定手勢。
                if (band.StartIndex < list.Items.Count && list.Items[band.StartIndex] is ICascadeNode node)
                {
                    var gesture = band.Additive && band.Shift ? SelectionGesture.AddRange
                        : band.Shift ? SelectionGesture.Range
                        : SelectionGesture.Toggle;
                    ApplySelectionGesture(columnIndex, node, gesture);
                }
            }
            else if (!band.Additive)
            {
                // 沒按 Ctrl、也沒拖曳：就是點了空白處。
                ClearMultiSelection();
                FocusActiveColumn();
                if (list.Items.Count > 0)
                    OnColumnBlankMouseClick(columnIndex, list.Items.OfType<ICascadeNode>().ToList(), MouseButtons.Left, list.PointToScreen(location));
                return;
            }

            FocusActiveColumn();
        }

        /// <summary>
        /// 一次多選的滑鼠按壓：從哪一欄的哪一列開始、是否按住 Ctrl（加選）或 Shift（範圍）、開始前已經有哪些多選，以及拖曳時的矩形框。
        /// </summary>
        /// <remarks>
        /// 矩形框是四條 1 像素寬的子控制項，不是畫在 ListBox 上：
        /// 控制項開了 WS_EX_COMPOSITED，XOR 反轉框（ControlPaint.DrawReversibleFrame）會被重畫蓋掉或留下殘影；
        /// 自繪也只畫得到有項目的列，最後一列下方的空白處畫不到。子控制項由視窗系統合成，欄位又有 WS_CLIPCHILDREN，
        /// 移動時只重畫讓出來的那一小塊，不會閃爍。
        /// </remarks>
        private sealed class BandPress
        {
            private Control[] _edges;

            public BandPress(ListBox list, int startIndex, Point start, int startTopIndex, bool additive, List<ICascadeNode> baseMarks)
            {
                List = list;
                StartIndex = startIndex;
                Start = start;
                StartTopIndex = startTopIndex;
                Additive = additive;
                BaseMarks = baseMarks;
            }

            /// <summary>按下時的 TopIndex；拖到邊緣自動捲動之後，起點要跟著內容一起移動。</summary>
            public int StartTopIndex { get; }

            /// <summary>
            /// 顯示（或移動）從起點到 <paramref name="location"/> 的矩形框，範圍限制在欄位內。
            /// </summary>
            public void ShowFrame(Point location)
            {
                if (List.IsDisposed) return;

                var client = List.ClientRectangle;
                var startY = Start.Y - ((List.TopIndex - StartTopIndex) * Math.Max(1, List.ItemHeight));

                var left = Clamp(Math.Min(Start.X, location.X), 0, client.Width - 1);
                var right = Clamp(Math.Max(Start.X, location.X), 0, client.Width - 1);
                var top = Clamp(Math.Min(startY, location.Y), 0, client.Height - 1);
                var bottom = Clamp(Math.Max(startY, location.Y), 0, client.Height - 1);
                var width = right - left + 1;
                var height = bottom - top + 1;

                if (_edges == null)
                {
                    _edges = new Control[4];
                    for (var index = 0; index < _edges.Length; index++)
                    {
                        _edges[index] = new BandEdge();
                        List.Controls.Add(_edges[index]);
                    }
                }

                _edges[0].Bounds = new Rectangle(left, top, width, 1);
                _edges[1].Bounds = new Rectangle(left, bottom, width, 1);
                _edges[2].Bounds = new Rectangle(left, top, 1, height);
                _edges[3].Bounds = new Rectangle(right, top, 1, height);
            }

            public void HideFrame()
            {
                if (_edges == null) return;

                foreach (var edge in _edges)
                {
                    if (!List.IsDisposed) List.Controls.Remove(edge);
                    edge.Dispose();
                }

                _edges = null;
            }

            private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, Math.Max(min, max)));

            public ListBox List { get; }

            /// <summary>按下的那一列；按在空白處時為 -1。</summary>
            public int StartIndex { get; }

            public Point Start { get; }

            /// <summary>按住 Ctrl：框選範圍加進原本的多選，沒拖曳時切換按下的那一列。</summary>
            public bool Additive { get; }

            public List<ICascadeNode> BaseMarks { get; }

            public bool Dragged { get; set; }

            /// <summary>按住 Shift：沒拖曳時是範圍選取（只按 Shift 取代、Ctrl+Shift 加入），拖曳時只按 Shift 是取代式框選。</summary>
            public bool Shift { get; set; }
        }

        /// <summary>
        /// 框選矩形的一條邊：只有底色（系統選取色），不接收焦點。滑鼠在框選期間被欄位抓住，不會落到它身上。
        /// </summary>
        private sealed class BandEdge : Control
        {
            public BandEdge()
            {
                SetStyle(ControlStyles.Selectable, false);
                BackColor = SystemColors.Highlight;
                TabStop = false;
            }
        }
    }
}