using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 滑鼠與鍵盤操作。
    public partial class CascadeDiagramView
    {
        #region 滑鼠

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
            if (_move != null && e.Button == MouseButtons.Left)
            {
                DragMovePress(e.Location);
                return;
            }

            Cursor = GetNodeAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
            if (_press != null && e.Button == MouseButtons.Left) DragMarkPress(e.Location);
        }

        /// <inheritdoc />
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 編輯中按在圖上：這一下只用來套用輸入的名稱，不選取、不切換展開。
            if (_edit != null)
            {
                _swallowClick = true;
                EndEdit(commit: true, interactive: false);
            }

            // 要有焦點，滾輪才捲得動。
            Focus();

            // 右鍵功能表接著開啟，對象是被點的節點；點在空白處時是選取的節點。
            _contextMenuNode = e.Button == MouseButtons.Right ? GetNodeAt(e.Location) : null;

            // 展開鈕交給點擊處理；其他左鍵按壓可能是框選或 Ctrl+點選（見 MultiSelectedNodes）。
            _press = null;
            if (_move != null) EndMovePress(apply: false);
            if (e.Button == MouseButtons.Left && !_swallowClick && GetExpanderAt(e.Location) == null)
                BeginMarkPress(e.Location, IsControlKeyDown);

            base.OnMouseDown(e);
        }

        /// <inheritdoc />
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_swallowClick || _press != null || IsMoveDragging || (_move != null && _move.StartedWithControl) || TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node == null) return;

            if (e.Button == MouseButtons.Left) ClearMultiSelection();
            OnNodeMouseClick(CreateMouseEventArgs(node, e));
        }

        /// <inheritdoc />
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            // 連點兩下時第二下只會送 DoubleClick，當成第二次點擊再切換一次。
            if (_swallowClick || _press != null || IsMoveDragging || (_move != null && _move.StartedWithControl) || TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseDoubleClick(CreateMouseEventArgs(node, e));
        }

        /// <inheritdoc />
        /// <remarks>Click 與 DoubleClick 在 MouseUp 之前觸發，所以在這裡結束略過與多選手勢。</remarks>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _swallowClick = false;
            if (_press != null && e.Button == MouseButtons.Left) EndMarkPress();
            if (_move != null && e.Button == MouseButtons.Left) EndMovePress(apply: true);
        }

        /// <inheritdoc />
        /// <remarks>按著左鍵時失去滑鼠（例如切到別的程式）：框選停在目前的範圍，拖曳移動取消。</remarks>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;

            if (_move != null) EndMovePress(apply: false);
            if (_press == null) return;

            _press = null;
            Invalidate();
        }

        private CascadeNodeMouseEventArgs CreateMouseEventArgs(MenuNode node, MouseEventArgs e)
        {
            var bounds = GetClientBoundsOf(node);
            return new CascadeNodeMouseEventArgs(node.FullPath, PathSeparator, e.Button, e.Location, bounds, PointToScreen(e.Location));
        }

        #endregion 滑鼠

        #region 鍵盤

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
        /// ↑↓ 在圖上看得到的同一層節點之間移動（可以跨父節點）；← 退回上一層、→ 進入下一層（記憶與 <see cref="State"/> 共用）；
        /// 空白鍵展開或收合選取的節點；* 展開選取節點與其全部子孫；數字鍵在同層兄弟之間依編號跳轉；
        /// Enter 在選取的節點上觸發 <see cref="NodeActivated"/>。改的是 <see cref="State"/> 的選取，所以共享狀態的檢視一起動。
        /// </summary>
        /// <param name="e">按鍵資料。</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_move != null && _move.Dragged && e.KeyCode == Keys.ControlKey)
            {
                UpdateMoveDropTarget(_move.LastClient);
                return;
            }

            if (e.Handled || e.Modifiers != Keys.None) return;

            if (e.KeyCode == Keys.Multiply)
            {
                ResetDigitJump();
                ExpandAllSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (TryGetDigit(e, out var digit))
            {
                JumpByDigit(digit);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Up: ResetDigitJump(); MoveVertical(-1); break;
                case Keys.Down: ResetDigitJump(); MoveVertical(1); break;
                case Keys.Left: ResetDigitJump(); _state.StepBack(); break;
                case Keys.Right: ResetDigitJump(); StepIn(); break;
                case Keys.Space: ResetDigitJump(); ToggleSelected(); break;
                case Keys.Enter: ResetDigitJump(); ActivateSelected(); break;
                default: return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            EnsureVisible(SelectedMenuNode);
        }

        /// <inheritdoc />
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (_move != null && _move.Dragged && e.KeyCode == Keys.ControlKey)
                UpdateMoveDropTarget(_move.LastClient);
        }

        /// <inheritdoc />
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!e.Handled && e.KeyChar == '*')
            {
                ResetDigitJump();
                ExpandAllSelected();
                e.Handled = true;
            }
        }

        private const int WM_KEYDOWN = 0x0100;
        private const int VK_PROCESSKEY = 0xE5;
        private const int SPACE_SCAN_CODE = 0x39;

        /// <inheritdoc />
        /// <remarks>
        /// 中文輸入法開著的時候，Space 會先被輸入法收走，送到這裡的 WM_KEYDOWN 是 ProcessKey 而不是 Space，展開／收合就沒反應。
        /// ProcessKey 的 lParam 仍帶著原本的掃描碼，認得出是 Space 就改回 Space，讓 <see cref="OnKeyDown"/> 照常處理。
        /// 就地編輯框是另一個視窗，它的按鍵不會經過這裡。
        /// </remarks>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_KEYDOWN && m.WParam.ToInt64() == VK_PROCESSKEY && ScanCodeOf(m.LParam) == SPACE_SCAN_CODE)
                m.WParam = (IntPtr)Keys.Space;

            base.WndProc(ref m);
        }

        // 按鍵訊息的 lParam：第 16～23 位元是掃描碼，第 24 位元是延伸鍵旗標（Space 不是延伸鍵）。
        private static int ScanCodeOf(IntPtr lParam) => (int)((lParam.ToInt64() >> 16) & 0x1FF);

        /// <summary>
        /// 編輯框有焦點時：Enter 套用、Esc 取消，其他按鍵交給編輯框（Ctrl+C、Ctrl+V、Delete 是編輯框自己的）。
        /// 其他時候編輯快捷鍵查 <see cref="CascadeEditShortcuts"/>，對象是 <see cref="SelectedNode"/>；唯讀時不攔會改樹的指令。
        /// </summary>
        /// <remarks>
        /// 有多選時 Esc 取消多選；沒有多選時不攔，Ctrl+A 與勾選也不攔（屬於 <see cref="Columns.CascadeColumnsView"/>）。
        /// 不攔的按鍵照常交給表單（例如選單快捷鍵、Esc 的 CancelButton）。
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
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

            if (keyData == Keys.Escape)
            {
                ResetDigitJump();

                if (_move != null)
                {
                    EndMovePress(apply: false);
                    _swallowClick = true;
                    return true;
                }

                if (_marks.Count > 0)
                {
                    ClearMultiSelection();
                    return true;
                }
            }

            if (CascadeEditShortcuts.TryGetCommand(keyData, out var command)
                && (CanEdit || !CascadeEditShortcuts.ChangesTree(command))
                && TryExecute(command))
            {
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 回傳 false 表示這個指令 Diagram 不處理，按鍵交給表單。
        private bool TryExecute(CascadeEditCommand command)
        {
            switch (command)
            {
                case CascadeEditCommand.Copy: Copy(); return true;
                case CascadeEditCommand.CopyPath: CopyPath(); return true;
                case CascadeEditCommand.Undo: Undo(); return true;
                case CascadeEditCommand.Redo: Redo(); return true;
                case CascadeEditCommand.Cut: Cut(); return true;
                case CascadeEditCommand.Paste: Paste(before: false); return true;
                case CascadeEditCommand.PasteBefore: Paste(before: true); return true;
                case CascadeEditCommand.PasteAsChild: PasteAsChild(); return true;
                case CascadeEditCommand.Delete: Delete(); return true;
                case CascadeEditCommand.Rename: BeginRename(); return true;
                case CascadeEditCommand.Insert: BeginInsert(before: false); return true;
                case CascadeEditCommand.InsertBefore: BeginInsert(before: true); return true;
                case CascadeEditCommand.CreateChild: BeginCreateChild(); return true;
                default: return false;
            }
        }

        private MenuNode SelectedMenuNode => _state.SelectedNode as MenuNode;

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
                _state.TrySelectNode(_image.Nodes[0].Node);
                return;
            }

            var column = _image.Nodes.Where(imageNode => imageNode.Node.Depth == current.Node.Depth).ToList();
            var index = column.IndexOf(current) + direction;
            if (index >= 0 && index < column.Count) _state.TrySelectNode(column[index].Node);
        }

        /// <summary>
        /// 進入下一層，照 <see cref="StepForward"/> 的規則走（有記憶走記憶，否則第一個子節點）；
        /// 選取的節點收合（[+]）時，<see cref="StepIntoCollapsed"/> 為 true 先展開再進入，false 不動作。
        /// </summary>
        /// <remarks>
        /// 隱藏未勾選的節點時，記憶或第一個子節點可能沒畫出來，改選圖上第一個子節點；子節點全被隱藏時不動作。
        /// </remarks>
        private void StepIn()
        {
            var node = SelectedMenuNode;
            if (StepIntoCollapsed && node != null && node.Children.Count > 0 && !IsExpanded(node) && FindImageNode(node) != null)
            {
                SetExpanded(node, true);
            }

            if (node != null && !node.Children.Any(child => FindImageNode(child) != null)) return;

            if (!_state.StepForward() || node == null) return;
            if (FindImageNode(SelectedMenuNode) != null) return;

            var firstDrawn = node.Children.FirstOrDefault(child => FindImageNode(child) != null);
            if (firstDrawn != null) _state.TrySelectNode(firstDrawn);
        }

        // 和雙擊一樣只針對畫在圖上的節點；選取被收合藏起來或沒有選取時不觸發。
        private void ActivateSelected()
        {
            var node = SelectedMenuNode;
            if (FindImageNode(node) != null) OnNodeActivated(new CascadeNodeEventArgs(node.FullPath, null, PathSeparator));
        }

        private void ToggleSelected()
        {
            var node = SelectedMenuNode;
            if (node != null && node.Children.Count > 0) SetExpanded(node, !IsExpanded(node));
        }

        private void ExpandAllSelected()
        {
            var node = SelectedMenuNode;
            if (node == null || node.Children.Count == 0) return;

            ExpandAll(node);
            EnsureVisible(node);
        }

        #region 數字鍵跳轉

        private string _digitBuffer = string.Empty;

        private MenuNode _digitNode;

        private static bool TryGetDigit(KeyEventArgs e, out char digit)
        {
            digit = '\0';
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) digit = (char)('0' + (e.KeyCode - Keys.D0));
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) digit = (char)('0' + (e.KeyCode - Keys.NumPad0));
            else return false;

            return true;
        }

        private void ResetDigitJump()
        {
            _digitBuffer = string.Empty;
            _digitNode = null;
        }

        private void JumpByDigit(char digit)
        {
            var selected = SelectedMenuNode;
            var siblings = selected?.Parent?.Children ?? Tree?.Root.Children;
            if (siblings == null || siblings.Count == 0) return;

            var drawn = siblings.Where(child => FindImageNode(child) != null).ToList();
            if (drawn.Count == 0) return;

            if (!ReferenceEquals(_digitNode, selected)) ResetDigitJump();

            var candidate = _digitBuffer + digit;
            var node = FindBySiblingNumber(drawn, candidate);
            if (node == null)
            {
                candidate = digit.ToString();
                node = FindBySiblingNumber(drawn, candidate);
            }

            if (node == null)
            {
                ResetDigitJump();
                return;
            }

            ClearMultiSelection();
            _state.TrySelectNode(node);
            EnsureVisible(SelectedMenuNode);

            _digitBuffer = candidate;
            _digitNode = SelectedMenuNode;
        }

        private static MenuNode FindBySiblingNumber(IEnumerable<MenuNode> nodes, string digits)
        {
            if (!int.TryParse(digits, out var number)) return null;
            return nodes.FirstOrDefault(node => node.SiblingNumber == number);
        }

        #endregion 數字鍵跳轉

        #endregion 鍵盤
    }
}