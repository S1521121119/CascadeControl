using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Columns
{
    // 鍵盤與滑鼠操作。焦點固定在操作欄，所以按鍵一律以導覽狀態為準，不看是哪一欄收到的。
    public partial class CascadeColumnsView
    {
        // 左鍵按在不改選取的位置（見 SelectsOnLeftClick）時，按在哪一欄的哪個節點；放開或失去滑鼠時清掉。
        private ListBox _pressedList;

        private ICascadeNode _pressedNode;

        // ← 退回上一層、→ 進入下一層、Enter 在選取的節點上觸發 NodeActivated；一般 ↑↓ 交給 ListBox 原生處理，
        // 搭配 Shift / Ctrl 的 ↑↓ Home End PgUp PgDn 是多選手勢（見 MultiSelectedNodes）；數字與字母是跳轉。
        private void OnColumnKeyDown(object sender, KeyEventArgs e)
        {
            if (_lists.IndexOf(sender as ListBox) < 0) return;

            if (TryGetDigit(e, out var digit))
            {
                // 攔下來，ListBox 原生的「按字元跳到開頭相符的項目」不要再動作。
                e.Handled = true;
                e.SuppressKeyPress = true;
                JumpByDigit(digit);
                return;
            }

            // 導覽鍵結束目前的數字與文字輸入；字母鍵不能清，它的字元稍後由 KeyPress 處理。
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Enter:
                case Keys.Escape:
                    ResetDigitJump();
                    break;
            }

            if ((e.Control || e.Shift) && !e.Alt && TryGetKeyTarget(e.KeyCode, out var targetIndex))
            {
                // Shift = 範圍、Ctrl+Shift = 加入範圍、Ctrl = 只移焦點；不交給 ListBox（它會直接改選）。
                e.Handled = true;
                e.SuppressKeyPress = true;

                var gesture = e.Control && e.Shift ? SelectionGesture.AddRange
                    : e.Shift ? SelectionGesture.Range
                    : SelectionGesture.FocusOnly;
                var columnIndex = ActiveColumnIndex;
                if (_lists[columnIndex].Items[targetIndex] is ICascadeNode target)
                    ApplySelectionGesture(columnIndex, target, gesture);

                FocusActiveColumn();
                return;
            }

            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Enter) return;

            // 左右鍵一律攔下來，就算沒有欄位可去也一樣。
            // 單欄模式的 ListBox 原生會把 ←/→ 當成 ↑/↓，選取會莫名其妙跳動。
            e.Handled = true;
            e.SuppressKeyPress = true;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    StepBack();
                    break;

                case Keys.Right:
                    StepForward();
                    break;

                case Keys.Enter:
                    // 和雙擊一樣，沒有選取（例如按過 Esc）時不觸發。
                    if (SelectedNode != null) OnNodeActivated(CreateNodeEventArgs());
                    break;
            }

            FocusActiveColumn();
        }

        /// <summary>
        /// ↑↓ Home End PgUp PgDn 在操作欄裡的目標列；從焦點列（沒有焦點列時從第一列）算起，限制在欄位範圍內。
        /// </summary>
        /// <returns>不是這些鍵，或操作欄沒有項目時為 false。</returns>
        private bool TryGetKeyTarget(Keys keyCode, out int targetIndex)
        {
            targetIndex = -1;

            var columnIndex = ActiveColumnIndex;
            if (columnIndex >= _lists.Count) return false;

            var list = _lists[columnIndex];
            var count = list.Items.Count;
            if (count == 0) return false;

            var current = list.SelectedIndex;
            var page = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));

            switch (keyCode)
            {
                case Keys.Up: targetIndex = current < 0 ? 0 : current - 1; break;
                case Keys.Down: targetIndex = current < 0 ? 0 : current + 1; break;
                case Keys.Home: targetIndex = 0; break;
                case Keys.End: targetIndex = count - 1; break;
                case Keys.PageUp: targetIndex = current < 0 ? 0 : current - (page - 1); break;
                case Keys.PageDown: targetIndex = current < 0 ? 0 : current + (page - 1); break;
                default: return false;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, count - 1));
            return true;
        }

        #region 數字鍵跳轉

        // 目前累積的數字，以及它跳到的節點。選取被其他方式改掉（滑鼠、↑↓、進出層）時，節點對不上就視為重新開始。
        private string _digitBuffer = string.Empty;

        private ICascadeNode _digitNode;

        private static readonly System.Text.RegularExpressions.Regex NumberTag =
            new System.Text.RegularExpressions.Regex(@"^\s*<(\d+)>", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool TryGetDigit(KeyEventArgs e, out char digit)
        {
            digit = '\0';
            if (e.Control || e.Alt || e.Shift) return false;

            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) digit = (char)('0' + (e.KeyCode - Keys.D0));
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) digit = (char)('0' + (e.KeyCode - Keys.NumPad0));
            else return false;

            return true;
        }

        private string _textBuffer = string.Empty;

        private static readonly System.Text.RegularExpressions.Regex NumberPrefix =
            new System.Text.RegularExpressions.Regex(@"^\s*<\d+>\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

        private void ResetDigitJump()
        {
            _textBuffer = string.Empty;
            _digitBuffer = string.Empty;
            _digitNode = null;
        }

        /// <summary>
        /// 在操作欄裡依數字跳到節點：比對的是 <see cref="ICascadeNode.DisplayText"/> 開頭 &lt;序號&gt; 標籤的數值（&lt;02&gt; 的數值是 2）。
        /// </summary>
        /// <remarks>
        /// 先試「已輸入的數字 + 這一下」，找不到就只用這一下重新開始，兩者都找不到時不改選取。
        /// 例如依序按 2、3 會到 &lt;23&gt;；按 2、5 而沒有 &lt;25&gt; 時會到 &lt;05&gt;。
        /// </remarks>
        private void JumpByDigit(char digit)
        {
            var columnIndex = ActiveColumnIndex;
            if (columnIndex >= _lists.Count) return;

            var nodes = _lists[columnIndex].Items.OfType<ICascadeNode>().ToList();

            // 選取不是上次跳到的節點，代表中間做過別的操作，重新開始。
            if (!ReferenceEquals(_digitNode, SelectedNode)) ResetDigitJump();

            _textBuffer = string.Empty;
            var candidate = _digitBuffer + digit;
            var node = FindByNumber(nodes, candidate);
            if (node == null)
            {
                candidate = digit.ToString();
                node = FindByNumber(nodes, candidate);
            }

            if (node == null)
            {
                ResetDigitJump();
                return;
            }

            ClearMultiSelection();
            _state.SelectNodeAt(columnIndex, node);
            FocusActiveColumn();

            _digitBuffer = candidate;
            _digitNode = SelectedNode;
        }

        /// <summary>
        /// 在操作欄裡依輸入的字元跳到名稱開頭相符的節點（略過開頭的 &lt;數字&gt; 標籤，不分大小寫）。
        /// </summary>
        /// <remarks>
        /// 連續輸入會累積成前綴：C 選到 Config，再按 l 選到 Clock。
        /// 累積的前綴找不到時，改用這一個字元重新開始；還是找不到就不改選取。
        /// </remarks>
        private void JumpByChar(char c)
        {
            var columnIndex = ActiveColumnIndex;
            if (columnIndex >= _lists.Count) return;

            var nodes = _lists[columnIndex].Items.OfType<ICascadeNode>().ToList();

            if (!ReferenceEquals(_digitNode, SelectedNode)) ResetDigitJump();

            _digitBuffer = string.Empty;
            var candidate = _textBuffer + c;
            var node = FindByPrefix(nodes, candidate);
            if (node == null)
            {
                candidate = c.ToString();
                node = FindByPrefix(nodes, candidate);
            }

            if (node == null)
            {
                ResetDigitJump();
                return;
            }

            ClearMultiSelection();
            _state.SelectNodeAt(columnIndex, node);
            FocusActiveColumn();

            _textBuffer = candidate;
            _digitNode = SelectedNode;
        }

        private static ICascadeNode FindByPrefix(IEnumerable<ICascadeNode> nodes, string prefix)
        {
            return nodes.FirstOrDefault(node =>
                NumberPrefix.Replace(node.Name ?? string.Empty, string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static ICascadeNode FindByNumber(IEnumerable<ICascadeNode> nodes, string digits)
        {
            if (!int.TryParse(digits, out var number)) return null;

            foreach (var node in nodes)
            {
                // 序號在顯示文字開頭的 <nn> 標籤裡（MenuNode.DisplayText），不在 Name。
                var match = NumberTag.Match(node.DisplayText ?? string.Empty);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var value) && value == number) return node;
            }

            return null;
        }

        #endregion 數字鍵跳轉

        /// <summary>
        /// 攔下控制項的快捷鍵。
        /// </summary>
        /// <remarks>
        /// 編輯框有焦點時：Enter 套用、Esc 取消，其他按鍵交給編輯框（Ctrl+C / Ctrl+V 是編輯框自己的複製貼上）。
        /// 焦點在欄位上時：Esc 先取消多選，沒有多選時清除選取（記憶保留，之後按 → 可以沿上次的路線走回去）；
        /// Ctrl+C 複製、Ctrl+Shift+C 複製路徑（CSV 文字，見 <see cref="CopyPath"/>）、Ctrl+A 多選操作欄的全部節點、Ctrl+Z 復原、Ctrl+Y 與 Ctrl+Shift+Z 重做（唯讀時改到樹的步驟不能復原或重做，見 <see cref="CanUndo"/>）；
        /// <see cref="ReadOnly"/> 為 false 時另有 Ctrl+X 剪下、Ctrl+V 貼在後面、Ctrl+Shift+V 貼在前面、Ctrl+Alt+V 貼成子節點（只對葉節點）、Delete 刪除、F2 改名、Ctrl+I 在後面插入、Ctrl+Shift+I 在前面插入、Ctrl+N 建立子節點（只對葉節點）。
        /// Enter、Esc 不是 ListBox 與單行 TextBox 的輸入鍵，表單設了 AcceptButton / CancelButton 時會先被表單處理，
        /// 所以要在 ProcessCmdKey 這一關攔：它由焦點控制項往上逐層呼叫，比表單的 ProcessDialogKey 早。
        /// 和 ←、→、Enter 一樣，這些鍵只要焦點在欄位上就一律攔下，即使當下沒有對象。
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ProcessCheckBoxKey(keyData)) return true;

            if (_edit != null && _edit.Box.ContainsFocus)
            {
                switch (keyData)
                {
                    case Keys.Enter:
                        EndEdit(commit: true, interactive: true);
                        return true;

                    case Keys.Escape:
                        EndEdit(commit: false, interactive: false);
                        return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (IsColumnFocused)
            {
                switch (keyData)
                {
                    case Keys.Escape:
                        if (_marks.Count > 0) ClearMultiSelection();
                        else _state.ClearSelection();
                        FocusActiveColumn();
                        return true;

                    case Keys.Control | Keys.C:
                        Copy();
                        return true;

                    case Keys.Control | Keys.Shift | Keys.C:
                        CopyPath();
                        return true;

                    case Keys.Control | Keys.A:
                        SelectAllInColumn(ActiveColumnIndex);
                        return true;

                    case Keys.Control | Keys.Space:
                        ToggleFocusedNode();
                        FocusActiveColumn();
                        return true;

                    case Keys.Control | Keys.Z:
                        Undo();
                        return true;

                    case Keys.Control | Keys.Y:
                    case Keys.Control | Keys.Shift | Keys.Z:
                        Redo();
                        return true;
                }

                if (CanEdit)
                {
                    switch (keyData)
                    {
                        case Keys.Control | Keys.X:
                            Cut();
                            return true;

                        case Keys.Control | Keys.V:
                            Paste(before: false);
                            return true;

                        case Keys.Control | Keys.Shift | Keys.V:
                            Paste(before: true);
                            return true;

                        case Keys.Control | Keys.Alt | Keys.V:
                            PasteAsChild();
                            return true;

                        case Keys.Delete:
                            Delete();
                            return true;

                        case Keys.F2:
                            BeginRename();
                            return true;

                        case Keys.Control | Keys.I:
                            BeginInsert(before: false);
                            return true;

                        case Keys.Control | Keys.Shift | Keys.I:
                            BeginInsert(before: true);
                            return true;

                        case Keys.Control | Keys.N:
                            BeginCreateChild();
                            return true;
                    }
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 左鍵點在某一列的 <paramref name="location"/> 時，要不要選取那一列；預設為 true，整列都會選取。
        /// </summary>
        /// <param name="node">被點的節點。</param>
        /// <param name="bounds">那一列的矩形，欄位內的座標。</param>
        /// <param name="location">點擊位置，和 <paramref name="bounds"/> 同一個座標系。</param>
        /// <remarks>
        /// 預設只有 <see cref="CheckBoxes"/> 的勾選框回傳 false；衍生類別在列裡畫了自己處理點擊的東西時覆寫它，在那個範圍回傳 false。
        /// 回傳 false 的位置，左鍵按下、放開與雙擊都不改選取，也不觸發 <see cref="NodeActivated"/>；
        /// 按下與放開都在同一列的這種位置時才觸發 <see cref="NodeMouseClick"/>，衍生類別在 <see cref="OnNodeMouseClick"/> 處理。
        /// 右鍵本來就不改選取，不受影響。
        /// </remarks>
        protected virtual bool SelectsOnLeftClick(ICascadeNode node, Rectangle bounds, Point location) => !IsOnCheckBox(node, bounds, location);

        /// <summary>
        /// 用左鍵或右鍵點了某一欄的空白處（最後一列下方）之後呼叫；預設不做任何事。
        /// </summary>
        /// <param name="columnIndex">那一欄的索引（0-based）。</param>
        /// <param name="nodes">那一欄的全部節點，不會是空集合。</param>
        /// <param name="button">按下的滑鼠鍵：左鍵或右鍵。</param>
        /// <param name="screenLocation">點擊位置的螢幕座標，用來顯示快顯功能表。</param>
        /// <remarks>點空白處不改選取，也不觸發 <see cref="NodeMouseClick"/>。呼叫時焦點已經回到操作欄。</remarks>
        protected virtual void OnColumnBlankMouseClick(int columnIndex, IReadOnlyList<ICascadeNode> nodes, MouseButtons button, Point screenLocation)
        {
        }

        /// <summary>
        /// 用左鍵或右鍵點了某一列：左鍵取消多選並選取該節點，把焦點交回操作欄，最後觸發 <see cref="NodeMouseClick"/>；
        /// 右鍵不改選取，觸發 <see cref="NodeMouseClick"/> 之後開啟 <see cref="NodeContextMenu"/>。
        /// </summary>
        /// <remarks>
        /// 左鍵點了不同的項目時，SelectedIndexChanged 已經先處理過，這裡再選一次不會有任何改變；
        /// 這裡要處理的是「點了已選取或記憶中的同一項」這種 ListBox 不會觸發 SelectedIndexChanged 的情況。
        /// 右鍵不改選取（ListBox 本身也不會因為右鍵而改選），欄位就不會因為右鍵而滑動（<see cref="CascadeColumnLayoutMode.Sliding"/>）。
        /// 點在欄位的空白處時不改選取、不觸發事件：右鍵開啟 <see cref="ColumnContextMenu"/>，再呼叫 <see cref="OnColumnBlankMouseClick"/>；
        /// 左鍵在按下時就已經交給多選處理（見 <see cref="TryBeginBandPress"/>）。焦點已經被那一欄拿走，要還給操作欄。
        /// 事件參數的座標在改選之前就先算好：改選後欄位可能滑動，游標卻還在原地。
        /// 左鍵點在 <see cref="SelectsOnLeftClick"/> 為 false 的位置時，按下那一刻已經被 <see cref="ColumnListBox"/> 攔下，
        /// 放開時也不改選取；按下與放開不在同一列、或放開時已經離開那個範圍，就當成沒點，什麼都不觸發。
        /// </remarks>
        private void OnColumnMouseUp(object sender, MouseEventArgs e)
        {
            if ((e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) || !(sender is ListBox list)) return;

            var columnIndex = _lists.IndexOf(list);
            if (columnIndex < 0) return;

            if (_swallowMouseUp)
            {
                _swallowMouseUp = false;
                return;
            }

            if (e.Button == MouseButtons.Left && _band != null && ReferenceEquals(_band.List, list))
            {
                EndBandPress(columnIndex, list, e.Location);
                return;
            }

            var pressedList = _pressedList;
            var pressedNode = _pressedNode;
            EndSelectionFreePress();

            var pressed = e.Button == MouseButtons.Left && pressedList != null;
            var hit = TryHitRow(list, e.Location, out var node, out var bounds);

            CascadeNodeMouseEventArgs args = null;
            if (hit && e.Button == MouseButtons.Right)
            {
                args = CreateMouseEventArgs(columnIndex, node, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
            }
            else if (hit)
            {
                var selectionFree = !SelectsOnLeftClick(node, bounds, e.Location);

                if (!pressed && !selectionFree)
                {
                    args = CreateMouseEventArgs(columnIndex, node, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
                    ApplySelectionGesture(columnIndex, node, SelectionGesture.Plain);
                }
                else if (pressed && selectionFree && pressedList == list && Equals(pressedNode, node))
                {
                    args = CreateMouseEventArgs(columnIndex, node, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
                }
            }

            FocusActiveColumn();

            if (args != null)
            {
                OnNodeMouseClick(args);
                if (args.Button == MouseButtons.Right) ShowNodeContextMenu(columnIndex, args.Node, args.ScreenLocation);
            }
            else if (!hit && !pressed)
            {
                var screenLocation = list.PointToScreen(e.Location);
                if (e.Button == MouseButtons.Right) ShowColumnContextMenu(columnIndex, screenLocation);
                if (list.Items.Count > 0) OnColumnBlankMouseClick(columnIndex, list.Items.OfType<ICascadeNode>().ToList(), e.Button, screenLocation);
            }
        }

        private CascadeNodeMouseEventArgs CreateMouseEventArgs(
            int columnIndex, ICascadeNode node, MouseButtons button, Point location, Rectangle bounds, Point screenLocation)
        {
            var path = _state.PathWithTrail.Take(columnIndex).Concat(new[] { node });
            return new CascadeNodeMouseEventArgs(path, PathSeparator, button, location, bounds, screenLocation);
        }

        /// <summary>
        /// 左鍵按在 <see cref="SelectsOnLeftClick"/> 為 false 的位置時，記下按在哪一欄的哪個節點，並抓住滑鼠。
        /// </summary>
        /// <returns>true 表示這一下不交給 ListBox 處理（它會在按下的當下就改選）。</returns>
        /// <remarks>
        /// 要先抓住滑鼠再記錄：抓的時候觸發的 MouseCaptureChanged 會清掉記錄。
        /// 抓住滑鼠之後，放開時的 MouseUp 一定回到這一欄，即使游標已經移到別欄。
        /// </remarks>
        private bool TryBeginSelectionFreePress(ListBox list, Point location)
        {
            if (_lists.IndexOf(list) < 0 || !TryHitRow(list, location, out var node, out var bounds)) return false;
            if (SelectsOnLeftClick(node, bounds, location)) return false;

            list.Capture = true;
            _pressedList = list;
            _pressedNode = node;
            return true;
        }

        private void EndSelectionFreePress()
        {
            _pressedList = null;
            _pressedNode = null;
        }

        // 還沒放開就失去滑鼠（例如跳出對話框、切到別的程式），這一下就不算數；框選停在目前的範圍。
        private void OnColumnMouseCaptureChanged(object sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _pressedList)) EndSelectionFreePress();
            if (_band != null && ReferenceEquals(sender, _band.List) && !_band.List.Capture) EndBand();
        }

        // 欄位內的座標落在哪一列；點在空白處時回傳 false。
        private static bool TryHitRow(ListBox list, Point location, out ICascadeNode node, out Rectangle bounds)
        {
            var index = list.IndexFromPoint(location);
            node = index >= 0 && index < list.Items.Count ? list.Items[index] as ICascadeNode : null;
            bounds = node == null ? Rectangle.Empty : list.GetItemRectangle(index);
            return node != null;
        }

        /// <summary>
        /// 欄位用的 ListBox：在 WndProc 這一關攔下不該讓 ListBox 改選取的滑鼠按下。
        /// </summary>
        /// <remarks>
        /// ListBox 在按下的當下就改選並觸發 SelectedIndexChanged，MouseDown 事件比它晚，所以只能在 WndProc 這一關攔。攔下的情況：
        /// 就地編輯中按下左右鍵（只用來結束編輯）；按在 <see cref="SelectsOnLeftClick"/> 為 false 的位置；
        /// 按住 Ctrl 或 Shift，或按在欄位空白處（多選，見 <see cref="TryBeginBandPress"/>）。
        /// 雙擊和按下一樣處理，所以在勾選框上快速點兩下就是切換兩次，和一般的勾選框相同，也不會觸發 DoubleClick；空白處雙擊也不會觸發 <see cref="NodeActivated"/>。
        /// 編輯框是欄位的子控制項，所以加上 WS_CLIPCHILDREN，ListBox 重畫時不會蓋掉它；欄位捲動後把編輯框對回它那一列。
        /// </remarks>
        private sealed class ColumnListBox : ListBox
        {
            private readonly CascadeColumnsView _owner;

            public ColumnListBox(CascadeColumnsView owner)
            {
                _owner = owner;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style |= NativeMethods.WS_CLIPCHILDREN;
                    return cp;
                }
            }

            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                base.OnKeyPress(e);

                // 一般字元（不含控制字元、空白與數字，數字在 KeyDown 處理）；攔下來，ListBox 原生的跳轉不再動作。
                if (!e.Handled && !_owner.IsEditing && !char.IsControl(e.KeyChar) && !char.IsWhiteSpace(e.KeyChar) && !char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                    _owner.JumpByChar(e.KeyChar);
                }
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case NativeMethods.WM_LBUTTONDOWN:
                    case NativeMethods.WM_LBUTTONDBLCLK:
                    case NativeMethods.WM_RBUTTONDOWN:
                    case NativeMethods.WM_RBUTTONDBLCLK:
                        if (_owner.IsEditing)
                        {
                            _owner.RequestCommitEdit();
                            return;
                        }

                        _owner._swallowMouseUp = false;
                        break;
                }

                if (m.Msg == NativeMethods.WM_LBUTTONDOWN || m.Msg == NativeMethods.WM_LBUTTONDBLCLK)
                {
                    var location = LocationOf(m.LParam);
                    var keys = m.WParam.ToInt64();
                    var control = (keys & NativeMethods.MK_CONTROL) != 0;
                    var shift = (keys & NativeMethods.MK_SHIFT) != 0;
                    if (_owner.TryBeginSelectionFreePress(this, location) || _owner.TryBeginBandPress(this, location, control, shift)) return;
                }

                base.WndProc(ref m);

                if (m.Msg == NativeMethods.WM_VSCROLL || m.Msg == NativeMethods.WM_MOUSEWHEEL) _owner.PositionEditBox();
            }

            // 滑鼠訊息的 lParam：低 16 位元是 x，高 16 位元是 y，都是有號數（多螢幕時可能為負）。
            private static Point LocationOf(IntPtr lParam)
            {
                var value = unchecked((int)lParam.ToInt64());
                return new Point(unchecked((short)value), unchecked((short)(value >> 16)));
            }
        }

        /// <summary>
        /// 控制項本身取得焦點時（例如呼叫 <see cref="Control.Focus"/>、設成表單的 ActiveControl），把焦點轉交給操作欄。
        /// </summary>
        /// <remarks>
        /// 按鍵是由欄位處理的，焦點停在控制項本身時方向鍵、Space 都沒有作用。
        /// 沒有欄位、或操作欄無法取得焦點時，焦點留在控制項本身。
        /// </remarks>
        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            FocusActiveColumn();
        }

        private void OnColumnDoubleClick(object sender, EventArgs e)
        {
            // 第一下點擊已經把選取移到這一欄，所以只要它是操作欄且有選取節點就觸發。
            var columnIndex = _lists.IndexOf(sender as ListBox);
            if (columnIndex >= 0 && columnIndex == _state.SelectedDepth - 1 && SelectedNode != null)
                OnNodeActivated(CreateNodeEventArgs());
        }

        // 焦點只影響選取項的配色與焦點框，只重畫那一列；整欄重畫會讓整欄跟著閃一下。
        private static void OnColumnFocusChanged(object sender, EventArgs e)
        {
            if (!(sender is ListBox list) || !list.IsHandleCreated || list.SelectedIndex < 0) return;

            list.Invalidate(list.GetItemRectangle(list.SelectedIndex));
        }

        // 操作欄可能在畫面外（例如使用者手動捲開後再用 Tab 回來），取得焦點時捲回來。
        private void OnColumnGotFocus(object sender, EventArgs e)
        {
            OnColumnFocusChanged(sender, e);
            ScrollToActive();
        }
    }
}