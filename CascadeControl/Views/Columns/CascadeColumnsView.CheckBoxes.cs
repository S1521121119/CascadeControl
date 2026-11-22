using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace CascadeControl.Views.Columns
{
    // 勾選框：CheckBoxes 為 true 時才畫、才接受操作；CheckSet 與 CheckedChanged 不論開關都可以用。
    public partial class CascadeColumnsView : ICascadeCheckableView
    {
        private const string CHECK_SUBTREE_TEXT = "勾選(含所有子節點)";
        private const string CHECK_SIBLINGS_TEXT = "全部勾選";
        private const string CHECK_SIBLING_SUBTREES_TEXT = "全部勾選(含子節點)";
        private const string UNCHECK_SIBLINGS_TEXT = "全部取消";

        // 勾選框與列的左緣、與文字之間的間距。
        private const int CHECK_BOX_MARGIN = 2;

        // 勾選框上下各留的空間，列高不足時用來撐高。
        private const int CHECK_BOX_VERTICAL_PADDING = 1;

        // 系統勾選框的大小（96 DPI 下是 13×13），各種狀態都一樣大，量一次就好。
        private static readonly Size GLYPH_SIZE = MeasureGlyph();

        private readonly ToolStripMenuItem _checkSubtreeItem = new ToolStripMenuItem(CHECK_SUBTREE_TEXT);
        private readonly ToolStripMenuItem _checkSiblingsItem = new ToolStripMenuItem(CHECK_SIBLINGS_TEXT);
        private readonly ToolStripMenuItem _checkSiblingSubtreesItem = new ToolStripMenuItem(CHECK_SIBLING_SUBTREES_TEXT);
        private readonly ToolStripMenuItem _uncheckSiblingsItem = new ToolStripMenuItem(UNCHECK_SIBLINGS_TEXT);

        private bool _checkBoxes;

        // 在建構式呼叫，排在 InitializeContextMenus 之後：勾選項目插在最前面，
        // 和編輯項目之間隔一條分隔線（沒有編輯項目，或 CheckBoxes 為 false 時 TidySeparators 會把它藏起來）。
        private void InitializeCheckBoxMenus()
        {
            _checkSubtreeItem.Click += (sender, e) => RunCheckMenuCommand(set => set.CheckSubtree(ContextMenuNode));
            _checkSiblingsItem.Click += (sender, e) => RunCheckMenuCommand(set => set.CheckSiblings(ContextMenuNode));
            _checkSiblingSubtreesItem.Click += (sender, e) => RunCheckMenuCommand(set => set.CheckSiblingSubtrees(ContextMenuNode));
            _uncheckSiblingsItem.Click += (sender, e) => RunCheckMenuCommand(set => set.UncheckSiblings(ContextMenuNode));

            var nodeItems = new ToolStripItem[] { _checkSubtreeItem, new ToolStripSeparator() };
            for (var index = 0; index < nodeItems.Length; index++) NodeContextMenu.Items.Insert(index, nodeItems[index]);

            var columnItems = new ToolStripItem[]
            {
                _checkSiblingsItem, _checkSiblingSubtreesItem, new ToolStripSeparator(), _uncheckSiblingsItem, new ToolStripSeparator(),
            };
            for (var index = 0; index < columnItems.Length; index++) ColumnContextMenu.Items.Insert(index, columnItems[index]);
        }

        #region 勾選狀態

        /// <summary>
        /// 是否在每一列前面顯示勾選框，並接受勾選操作（點勾選框、Space、右鍵的勾選項目）。
        /// </summary>
        /// <remarks>
        /// <see cref="CheckBoxes"/> 為 true 時每一列前面多一個勾選框：可以在樹上勾選多個節點，勾選狀態存在 <see cref="CheckSet"/>。
        /// 選取（目前走到哪裡）與勾選（選了哪些）是兩回事：選取由 <see cref="CascadeState"/> 管理，勾選交給 <see cref="MenuCheckSet"/>，
        /// 這個檔案只負責把它畫出來，以及把滑鼠與鍵盤操作轉成對它的呼叫。
        /// 操作方式：點勾選框只切換勾選、不改選取（<see cref="CascadeColumnLayoutMode.Sliding"/> 時畫面也不會滑動），
        /// 按住 Shift 點勾選框是連同所有子節點一起勾選；點文字只選取；Space 切換目前選取的節點，Shift+Space 連同所有子節點；
        /// 勾選與樹的編輯記在同一份 <see cref="CascadeState.History"/>，Ctrl+Z 依發生的順序復原，Ctrl+Y 或 Ctrl+Shift+Z 重做，之後選取會跳到那一步針對的節點。
        /// 右鍵不改選取：勾選項目放在 <see cref="NodeContextMenu"/> 與 <see cref="ColumnContextMenu"/> 的最前面，後面接復原、重做與編輯項目。
        /// 勾了但子孫沒有全勾的節點畫成「部分勾選」，那只是提示，狀態仍然是已勾選。
        /// 只有屬於 <see cref="CheckSet"/> 那棵樹的 <see cref="MenuNode"/> 才有勾選框；其他節點的外觀與行為和沒有勾選框時相同。
        /// false 時 <see cref="CheckSet"/> 與 <see cref="CheckedChanged"/> 照常可用，勾選也照常記進歷史，只是不畫也不操作勾選框；切換不會清空歷史。
        /// 不論 true 或 false，剪下、刪除節點時，被移除節點的勾選都會一起取消（<see cref="CascadeCheckableState"/>），和移除算同一步，所以復原時勾選也會回來。
        /// </remarks>
        [Category("Cascade")]
        [Description("true：每一列前面顯示勾選框，可以勾選多個節點；false：只有選取。")]
        [DefaultValue(false)]
        public bool CheckBoxes
        {
            get => _checkBoxes;
            set
            {
                if (_checkBoxes == value) return;

                _checkBoxes = value;
                InvalidateNodes();

                var itemHeight = ItemHeight;
                foreach (var list in _lists)
                {
                    list.ItemHeight = itemHeight;
                    list.ImeMode = ColumnImeMode;
                }

                Relayout();
            }
        }

        /// <summary>
        /// 目前的勾選狀態，也就是 <see cref="State"/> 的 <see cref="CascadeCheckableState.CheckSet"/>；null 表示沒有勾選狀態。
        /// </summary>
        /// <remarks>
        /// <see cref="SetTree"/> 會自動換上一份新的；想保留先前的勾選，或讓好幾個控制項共用同一份時，才需要自己設定。
        /// 它的 <see cref="MenuCheckSet.Tree"/> 應該就是目前顯示的樹，否則所有節點都不會有勾選框。
        /// 設定會把它的 <see cref="MenuCheckSet.History"/> 換成 <see cref="State"/> 的 <see cref="CascadeState.History"/>，並清空歷史（見 <see cref="CascadeCheckableState.CheckSet"/>）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><see cref="State"/> 不是 <see cref="CascadeCheckableState"/> 時指派非 null 的勾選狀態。</exception>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuCheckSet CheckSet
        {
            get => (State as CascadeCheckableState)?.CheckSet;
            set
            {
                if (!(State is CascadeCheckableState checkable))
                {
                    if (value == null) return;
                    throw new InvalidOperationException($"目前的 {nameof(State)} 不是 {nameof(CascadeCheckableState)}，無法設定 {nameof(CheckSet)}。");
                }

                checkable.CheckSet = value;
            }
        }

        /// <summary>
        /// <see cref="CheckSet"/> 的勾選狀態改變後觸發，內容與 <see cref="MenuCheckSet.Changed"/> 相同。
        /// </summary>
        /// <remarks>換上另一份 <see cref="CheckSet"/> 時不會觸發。</remarks>
        [Category("Cascade")]
        public event EventHandler<MenuCheckChangedEventArgs> CheckedChanged;

        /// <summary>
        /// 觸發 <see cref="CheckedChanged"/>。
        /// </summary>
        protected virtual void OnCheckedChanged(MenuCheckChangedEventArgs e) => CheckedChanged?.Invoke(this, e);

        // 勾選框要操作的那一份：CheckBoxes 為 false 時沒有。
        private MenuCheckSet ActiveCheckSet => _checkBoxes ? CheckSet : null;

        private void AttachCheckableState(CascadeState state)
        {
            if (!(state is CascadeCheckableState checkable)) return;

            checkable.CheckedChanged += State_CheckedChanged;
            checkable.CheckSetChanged += State_CheckSetChanged;
        }

        private void DetachCheckableState(CascadeState state)
        {
            if (!(state is CascadeCheckableState checkable)) return;

            checkable.CheckedChanged -= State_CheckedChanged;
            checkable.CheckSetChanged -= State_CheckSetChanged;
        }

        private void State_CheckedChanged(object sender, MenuCheckChangedEventArgs e)
        {
            if (_checkBoxes) InvalidateNodes();
            OnCheckedChanged(e);
        }

        private void State_CheckSetChanged(object sender, EventArgs e)
        {
            if (_checkBoxes) InvalidateNodes();
        }

        // 這個節點有沒有勾選框：要開著 CheckBoxes，而且是目前這份勾選的樹裡的節點。
        private bool TryGetCheckableNode(ICascadeNode node, out MenuNode menuNode)
        {
            menuNode = node as MenuNode;
            var checkSet = ActiveCheckSet;
            return menuNode != null && checkSet != null && checkSet.Contains(menuNode);
        }

        #endregion 勾選狀態

        #region 繪製

        /// <summary>
        /// 勾選框在某一列裡的位置。
        /// </summary>
        /// <param name="rowBounds">那一列的矩形，欄位內的座標。</param>
        /// <returns>勾選框的矩形，和 <paramref name="rowBounds"/> 同一個座標系；<see cref="CheckBoxes"/> 為 true 時點在這個範圍內會切換勾選。</returns>
        public Rectangle GetCheckBoxBounds(Rectangle rowBounds)
        {
            return new Rectangle(
                rowBounds.Left + CHECK_BOX_MARGIN,
                rowBounds.Top + Math.Max(0, (rowBounds.Height - GLYPH_SIZE.Height) / 2),
                GLYPH_SIZE.Width,
                Math.Min(GLYPH_SIZE.Height, rowBounds.Height));
        }

        // 列高至少要放得下勾選框；字型很小時（例如 9pt 的新細明體只有 12 像素高）勾選框才不會被裁掉。
        private static int CheckBoxRowHeight => GLYPH_SIZE.Height + (CHECK_BOX_VERTICAL_PADDING * 2);

        private static Size MeasureGlyph()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal);
            }
        }

        // 有勾選框的節點：在預設外觀的前面多畫一個勾選框，文字往右讓開；沒有勾選框時回傳 false，交給一般畫法。
        private bool TryDrawCheckBoxNode(CascadeDrawNodeEventArgs e)
        {
            if (!TryGetCheckableNode(e.Node, out var node)) return false;

            var state = CheckBoxStateOf(node, e.IsEnabled);
            var box = GetCheckBoxBounds(e.Bounds);
            var textLeft = box.Right + CHECK_BOX_MARGIN;
            DrawDefaultNode(e, Rectangle.FromLTRB(Math.Min(textLeft, e.Bounds.Right), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom));

            CheckBoxRenderer.DrawCheckBox(e.Graphics, box.Location, state);
            return true;
        }

        private CheckBoxState CheckBoxStateOf(MenuNode node, bool enabled)
        {
            var checkSet = CheckSet;
            if (!checkSet.IsChecked(node)) return enabled ? CheckBoxState.UncheckedNormal : CheckBoxState.UncheckedDisabled;
            if (checkSet.IsFullyChecked(node)) return enabled ? CheckBoxState.CheckedNormal : CheckBoxState.CheckedDisabled;

            return enabled ? CheckBoxState.MixedNormal : CheckBoxState.MixedDisabled;
        }

        #endregion 繪製

        #region 滑鼠與快顯功能表

        // 點在勾選框上：例如 Sliding 時停在第三層、去勾第四層的節點，選取不會跳過去，畫面也不會滑動。
        private bool IsOnCheckBox(ICascadeNode node, Rectangle bounds, Point location)
        {
            return TryGetCheckableNode(node, out _) && GetCheckBoxBounds(bounds).Contains(location);
        }

        // 左鍵點在勾選框上就切換勾選（按住 Shift 時連同所有子節點），之後才觸發 NodeMouseClick，所以訂閱端看到的是切換之後的狀態。
        // 勾選框上的左鍵不改選取（見 SelectsOnLeftClick），基底只在按下與放開都在同一個勾選框上時才會走到這裡。
        private void ToggleOnCheckBoxClick(CascadeNodeMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && TryGetCheckableNode(e.Node, out var node) && GetCheckBoxBounds(e.Bounds).Contains(e.Location))
            {
                Toggle(node, (ModifierKeys & Keys.Shift) != 0);
            }
        }

        // 對象有勾選框時，在快顯功能表最前面顯示勾選項目：
        // 節點上是「勾選(含所有子節點)」，欄位空白處是「全部勾選」、「全部勾選(含子節點)」、「全部取消」（對象是那一欄的全部節點）。
        // 欄位空白處的 ContextMenuNode 是那一欄的第一個節點，空欄時為 null，勾選項目也就不出現。
        private void SetCheckBoxMenuItems(ContextMenuStrip menu)
        {
            var show = TryGetCheckableNode(ContextMenuNode, out _);

            if (ReferenceEquals(menu, NodeContextMenu))
            {
                _checkSubtreeItem.Available = show;
            }
            else if (ReferenceEquals(menu, ColumnContextMenu))
            {
                _checkSiblingsItem.Available = show;
                _checkSiblingSubtreesItem.Available = show;
                _uncheckSiblingsItem.Available = show;
            }
        }

        // 功能表開著的時候樹或勾選狀態可能被換掉，執行前再確認一次節點還在。
        private void RunCheckMenuCommand(Action<MenuCheckSet> command)
        {
            var checkSet = ActiveCheckSet;
            if (checkSet != null && checkSet.Contains(ContextMenuNode)) command(checkSet);
        }

        #endregion 滑鼠與快顯功能表

        #region 鍵盤

        // 焦點在欄位上、而且有勾選框時，攔下 Space（切換目前選取節點的勾選）、
        // Shift+Space（連同所有子節點一起勾選，已經全勾時取消，和 Shift＋點勾選框相同）。
        // 這兩個鍵只要符合條件就一律攔下，即使當下沒有選取，行為才一致。
        private bool ProcessCheckBoxKey(Keys keyData)
        {
            if (ActiveCheckSet == null || !IsColumnFocused) return false;
            if (keyData != Keys.Space && keyData != (Keys.Shift | Keys.Space)) return false;

            if (TryGetCheckableNode(SelectedNode, out var node)) Toggle(node, keyData == (Keys.Shift | Keys.Space));
            return true;
        }

        // 點勾選框與 Space 共用：一般是 Toggle；withSubtree（按住 Shift）時還沒全勾就連同子孫一起勾，已經全勾就取消。
        private void Toggle(MenuNode node, bool withSubtree)
        {
            var checkSet = ActiveCheckSet;
            if (checkSet == null) return;

            if (!withSubtree) checkSet.Toggle(node);
            else if (checkSet.IsFullyChecked(node)) checkSet.Uncheck(node);
            else checkSet.CheckSubtree(node);
        }

        // 中文輸入法開著的時候，Space 會先被輸入法收走，控制項收到的是 ProcessKey 而不是 Space，勾選就切換不了。
        // 有勾選框時 Space 是指令而不是文字，所以欄位關掉輸入法。
        private ImeMode ColumnImeMode => _checkBoxes ? ImeMode.Disable : ImeMode.Inherit;

        #endregion 鍵盤
    }
}
