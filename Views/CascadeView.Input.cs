using System;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    // 鍵盤與滑鼠操作。
    public partial class CascadeView
    {
        // → 進入下一欄、← 把焦點移回上一欄、Enter 在葉節點上觸發 ItemActivated；↑↓ 交給 ListBox 原生處理。
        private void OnColumnKeyDown(object sender, KeyEventArgs e)
        {
            var level = _lists.IndexOf(sender as ListBox);
            if (level < 0) return;

            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Enter) return;

            // 左右鍵一律攔下來，就算沒有欄位可去也一樣。
            // 單欄模式的 ListBox 原生會把 ←/→ 當成 ↑/↓，選取會莫名其妙跳動。
            e.Handled = true;
            e.SuppressKeyPress = true;

            ListBox target = null;
            switch (e.KeyCode)
            {
                case Keys.Left:
                    target = LeavePreviousLevel(level);
                    break;

                case Keys.Right:
                    target = EnterNextLevel(level);
                    break;

                case Keys.Enter:
                    if (!TryActivate(level)) target = EnterNextLevel(level);
                    break;
            }

            target?.Focus();
        }

        private void OnColumnDoubleClick(object sender, EventArgs e)
        {
            var level = _lists.IndexOf(sender as ListBox);
            if (level >= 0) TryActivate(level);
        }

        /// <summary>
        /// 第 <paramref name="level"/> 欄是選取路徑的最後一欄，而且選到的是葉節點時，觸發 <see cref="ItemActivated"/>。
        /// </summary>
        /// <remarks>
        /// 選到葉節點就不會再長出下一欄，所以「終點所在欄」必定是最後一欄；
        /// 只看欄位索引即可，不需要資料自己帶深度。
        /// </remarks>
        private bool TryActivate(int level)
        {
            if (level != _lists.Count - 1 || !IsLeafSelected) return false;

            OnItemActivated(CreateItemEventArgs());
            return true;
        }

        private ListBox EnterNextLevel(int level)
        {
            var next = level + 1;
            if (next >= _lists.Count) return null;

            var list = _lists[next];
            if (!list.Enabled || list.Items.Count == 0) return null;

            // 下一欄還沒選時直接選第一項，按 → 進去就能接著操作，不必再按一次 ↓。
            if (list.SelectedIndex < 0) list.SelectedIndex = 0;

            return list;
        }

        private ListBox LeavePreviousLevel(int level)
        {
            var previous = level - 1;
            if (previous < 0) return null;

            // 只移動焦點，不動選取；否則退回上一層會把後面的欄位全部拆掉。
            var list = _lists[previous];
            return list.Enabled ? list : null;
        }

        // 焦點只影響選取項的配色與焦點框，只重畫那一列；整欄重畫會讓整欄跟著閃一下。
        private static void OnColumnFocusChanged(object sender, EventArgs e)
        {
            if (!(sender is ListBox list) || !list.IsHandleCreated || list.SelectedIndex < 0) return;

            list.Invalidate(list.GetItemRectangle(list.SelectedIndex));
        }

        // ← 只移動焦點、不改選取，所以焦點改變時也要跟著捲動，否則按 ← 退回的那一欄可能在畫面外。
        private void OnColumnGotFocus(object sender, EventArgs e)
        {
            OnColumnFocusChanged(sender, e);
            ScrollToActive();
        }
    }
}