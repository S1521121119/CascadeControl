using CascadeControl.Common;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 鍵盤與滑鼠操作。焦點固定在操作欄，所以按鍵一律以導覽狀態為準，不看是哪一欄收到的。
    public partial class CascadeView
    {
        // 左鍵按在不改選取的位置（見 SelectsOnLeftClick）時，按在哪一欄的哪個節點；放開或失去滑鼠時清掉。
        private ListBox _pressedList;
        private ICascadeNode _pressedNode;

        // ← 退回上一層、→ 進入下一層、Enter 在選取的節點上觸發 NodeActivated；↑↓ 交給 ListBox 原生處理。
        private void OnColumnKeyDown(object sender, KeyEventArgs e)
        {
            if (_lists.IndexOf(sender as ListBox) < 0) return;

            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Enter) return;

            // 左右鍵一律攔下來，就算沒有欄位可去也一樣。
            // 單欄模式的 ListBox 原生會把 ←/→ 當成 ↑/↓，選取會莫名其妙跳動。
            e.Handled = true;
            e.SuppressKeyPress = true;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    ApplyNavigation(_navigator.StepBack());
                    break;

                case Keys.Right:
                    ApplyNavigation(_navigator.StepForward());
                    break;

                case Keys.Enter:
                    // 和雙擊一樣，沒有選取（例如按過 Esc）時不觸發。
                    if (SelectedNode != null) OnNodeActivated(CreateNodeEventArgs());
                    break;
            }

            FocusActiveColumn();
        }

        /// <summary>
        /// 焦點在欄位上時攔下 Esc，清除選取；記憶保留，之後按 → 可以沿上次的路線走回去。
        /// </summary>
        /// <remarks>
        /// Esc 不是 ListBox 的輸入鍵，表單設了 CancelButton 時會先被表單處理，欄位的 KeyDown 收不到，
        /// 所以要在 ProcessCmdKey 這一關攔：它由焦點控制項往上逐層呼叫，比表單的 ProcessDialogKey 早。
        /// 和 ←、→、Enter 一樣，只要焦點在欄位上就一律攔下，即使已經沒有選取。
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && IsColumnFocused)
            {
                ApplyNavigation(_navigator.ClearSelection());
                FocusActiveColumn();
                return true;
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
        /// 衍生類別在列裡畫了自己處理點擊的東西（例如勾選框）時覆寫它，在那個範圍回傳 false。
        /// 回傳 false 的位置，左鍵按下、放開與雙擊都不改選取，也不觸發 <see cref="NodeActivated"/>；
        /// 按下與放開都在同一列的這種位置時才觸發 <see cref="NodeMouseClick"/>，衍生類別在 <see cref="OnNodeMouseClick"/> 處理。
        /// 右鍵本來就不改選取，不受影響。
        /// </remarks>
        protected virtual bool SelectsOnLeftClick(ICascadeNode node, Rectangle bounds, Point location) => true;

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
        /// 用左鍵或右鍵點了某一列：左鍵選取該節點，把焦點交回操作欄，最後觸發 <see cref="NodeMouseClick"/>；右鍵不改選取。
        /// </summary>
        /// <remarks>
        /// 左鍵點了不同的項目時，SelectedIndexChanged 已經先處理過，這裡再選一次不會有任何改變；
        /// 這裡要處理的是「點了已選取或記憶中的同一項」這種 ListBox 不會觸發 SelectedIndexChanged 的情況。
        /// 右鍵不改選取（ListBox 本身也不會因為右鍵而改選），欄位就不會因為右鍵而滑動（<see cref="CascadeColumnLayoutMode.Sliding"/>）。
        /// 點在欄位的空白處時不改選取、不觸發事件，改呼叫 <see cref="OnColumnBlankMouseClick"/>；焦點已經被那一欄拿走，要還給操作欄。
        /// 事件參數的座標在改選之前就先算好：改選後欄位可能滑動，游標卻還在原地。
        /// 左鍵點在 <see cref="SelectsOnLeftClick"/> 為 false 的位置時，按下那一刻已經被 <see cref="ColumnListBox"/> 攔下，
        /// 放開時也不改選取；按下與放開不在同一列、或放開時已經離開那個範圍，就當成沒點，什麼都不觸發。
        /// </remarks>
        private void OnColumnMouseUp(object sender, MouseEventArgs e)
        {
            if ((e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) || !(sender is ListBox list)) return;

            var columnIndex = _lists.IndexOf(list);
            if (columnIndex < 0) return;

            var pressedList = _pressedList;
            var pressedNode = _pressedNode;
            EndSelectionFreePress();

            var pressed = e.Button == MouseButtons.Left && pressedList != null;
            var hit = TryHitRow(list, e.Location, out var node, out var bounds);

            CascadeNodeMouseEventArgs args = null;
            if (hit && e.Button == MouseButtons.Right)
            {
                args = new CascadeNodeMouseEventArgs(node, columnIndex, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
            }
            else if (hit)
            {
                var selectionFree = !SelectsOnLeftClick(node, bounds, e.Location);

                if (!pressed && !selectionFree)
                {
                    args = new CascadeNodeMouseEventArgs(node, columnIndex, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
                    ApplyNavigation(_navigator.SelectNodeAt(columnIndex, node));
                }
                else if (pressed && selectionFree && pressedList == list && Equals(pressedNode, node))
                {
                    args = new CascadeNodeMouseEventArgs(node, columnIndex, e.Button, e.Location, bounds, list.PointToScreen(e.Location));
                }
            }

            FocusActiveColumn();

            if (args != null)
            {
                OnNodeMouseClick(args);
            }
            else if (!hit && !pressed && list.Items.Count > 0)
            {
                OnColumnBlankMouseClick(columnIndex, list.Items.OfType<ICascadeNode>().ToList(), e.Button, list.PointToScreen(e.Location));
            }
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

        /// <summary>
        /// 左鍵按在欄位的空白處（最後一列下方）時，不交給 ListBox 處理。
        /// </summary>
        /// <returns>true 表示按在空白處，這一下要攔下。</returns>
        /// <remarks>
        /// 原生 ListBox 按在空白處時會選到最近的一列（通常是最後一列），觸發 SelectedIndexChanged，
        /// 選取就跳回那一欄；點空白處不等於選了節點，所以攔下。放開時的 MouseUp 照常收到，由 <see cref="OnColumnMouseUp"/> 交給
        /// <see cref="OnColumnBlankMouseClick"/>，焦點也在那裡還給操作欄。雙擊一併攔下，空白處雙擊才不會觸發 <see cref="NodeActivated"/>。
        /// </remarks>
        private bool IsBlankPress(ListBox list, Point location)
        {
            return _lists.IndexOf(list) >= 0 && !TryHitRow(list, location, out _, out _);
        }

        private void EndSelectionFreePress()
        {
            _pressedList = null;
            _pressedNode = null;
        }

        // 還沒放開就失去滑鼠（例如跳出對話框、切到別的程式），這一下就不算數。
        private void OnColumnMouseCaptureChanged(object sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _pressedList)) EndSelectionFreePress();
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
        /// 欄位用的 ListBox：左鍵按在欄位空白處，或按在 <see cref="SelectsOnLeftClick"/> 為 false 的位置時，攔下按下與雙擊，不讓 ListBox 改選取。
        /// </summary>
        /// <remarks>
        /// ListBox 在按下的當下就改選並觸發 SelectedIndexChanged，MouseDown 事件比它晚，所以只能在 WndProc 這一關攔。
        /// 雙擊和按下一樣處理，所以在勾選框上快速點兩下就是切換兩次，和一般的勾選框相同，也不會觸發 DoubleClick。
        /// </remarks>
        private sealed class ColumnListBox : ListBox
        {
            private readonly CascadeView _owner;

            public ColumnListBox(CascadeView owner)
            {
                _owner = owner;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_LBUTTONDOWN || m.Msg == NativeMethods.WM_LBUTTONDBLCLK)
                {
                    var location = LocationOf(m.LParam);
                    if (_owner.IsBlankPress(this, location) || _owner.TryBeginSelectionFreePress(this, location)) return;
                }

                base.WndProc(ref m);
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
            if (columnIndex >= 0 && columnIndex == _navigator.SelectedDepth - 1 && SelectedNode != null)
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