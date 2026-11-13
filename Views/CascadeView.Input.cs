using System;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 鍵盤與滑鼠操作。焦點固定在操作欄，所以按鍵一律以導覽狀態為準，不看是哪一欄收到的。
    public partial class CascadeView
    {
        // ← 退回上一層、→ 進入下一層、Enter 在葉節點上觸發 ItemActivated；↑↓ 交給 ListBox 原生處理。
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
                    if (IsLeafSelected) OnItemActivated(CreateItemEventArgs());
                    else ApplyNavigation(_navigator.StepForward());
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
            if (keyData == Keys.Escape && _lists.Any(list => list.Focused))
            {
                ApplyNavigation(_navigator.ClearSelection());
                FocusActiveColumn();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 點選某一列：處理「點了已選取或記憶中的同一項」這種 ListBox 不會觸發 SelectedIndexChanged 的情況，最後把焦點交回操作欄。
        /// </summary>
        /// <remarks>
        /// 點了不同的項目時，SelectedIndexChanged 已經先處理過，這裡再選一次不會有任何改變。
        /// 點在欄位的空白處時不改選取，但焦點已經被那一欄拿走，要還給操作欄。
        /// </remarks>
        private void OnColumnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !(sender is ListBox list)) return;

            var level = _lists.IndexOf(list);
            if (level < 0) return;

            var index = list.IndexFromPoint(e.Location);
            if (index >= 0 && index < list.Items.Count && list.Items[index] is ICascadeItem item)
                ApplyNavigation(_navigator.Select(level, item));

            FocusActiveColumn();
        }

        private void OnColumnDoubleClick(object sender, EventArgs e)
        {
            // 第一下點擊已經把選取移到這一欄，所以只要它是操作欄且有選取項目就觸發。
            var level = _lists.IndexOf(sender as ListBox);
            if (level >= 0 && level == _navigator.Depth - 1 && SelectedItem != null)
                OnItemActivated(CreateItemEventArgs());
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