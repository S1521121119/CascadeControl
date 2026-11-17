using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace CascadeControl.Views
{
    /// <summary>
    /// 每一列前面多一個勾選框的 <see cref="CascadeView"/>：可以在樹上勾選多個節點，勾選狀態存在 <see cref="CheckSet"/>。
    /// </summary>
    /// <remarks>
    /// 選取（目前走到哪裡）與勾選（選了哪些）是兩回事：選取仍然由基底管理，勾選交給 <see cref="MenuCheckSet"/>，
    /// 這個類別只負責把它畫出來，以及把滑鼠與鍵盤操作轉成對它的呼叫。
    /// 操作方式：點勾選框只切換勾選、不改選取（<see cref="CascadeColumnLayoutMode.Sliding"/> 時畫面也不會滑動），
    /// 按住 Shift 點勾選框是連同所有子節點一起勾選；點文字只選取；Space 切換目前選取的節點，Shift+Space 連同所有子節點；
    /// Ctrl+Z 復原，Ctrl+Y 或 Ctrl+Shift+Z 重做，之後選取會跳到那一步針對的節點。
    /// 右鍵不改選取：點在節點上開啟 <see cref="NodeContextMenu"/>，點在欄位空白處開啟 <see cref="ColumnContextMenu"/>。
    /// 勾了但子孫沒有全勾的節點畫成「部分勾選」，那只是提示，狀態仍然是已勾選。
    /// 只有屬於 <see cref="CheckSet"/> 那棵樹的 <see cref="MenuNode"/> 才有勾選框；其他節點的外觀與行為和基底相同。
    /// </remarks>
    [DesignerCategory("")]
    public class CascadeCheckboxView : CascadeView
    {
        private const string DEFAULT_CHECK_SUBTREE_TEXT = "勾選(含所有子節點)";
        private const string DEFAULT_CHECK_SIBLINGS_TEXT = "全部勾選";
        private const string DEFAULT_CHECK_SIBLING_SUBTREES_TEXT = "全部勾選(含子節點)";
        private const string DEFAULT_UNCHECK_SIBLINGS_TEXT = "全部取消";

        // 勾選框與列的左緣、與文字之間的間距。
        private const int CHECK_BOX_MARGIN = 2;

        // 勾選框上下各留的空間，列高不足時用來撐高。
        private const int CHECK_BOX_VERTICAL_PADDING = 1;

        // 系統勾選框的大小（96 DPI 下是 13×13），各種狀態都一樣大，量一次就好。
        private static readonly Size GLYPH_SIZE = MeasureGlyph();

        private readonly ToolStripMenuItem _checkSubtreeItem = new ToolStripMenuItem(DEFAULT_CHECK_SUBTREE_TEXT);
        private readonly ToolStripMenuItem _checkSiblingsItem = new ToolStripMenuItem(DEFAULT_CHECK_SIBLINGS_TEXT);
        private readonly ToolStripMenuItem _checkSiblingSubtreesItem = new ToolStripMenuItem(DEFAULT_CHECK_SIBLING_SUBTREES_TEXT);
        private readonly ToolStripMenuItem _uncheckSiblingsItem = new ToolStripMenuItem(DEFAULT_UNCHECK_SIBLINGS_TEXT);

        private MenuCheckSet _checkSet;

        /// <summary>
        /// 建立控制項。
        /// </summary>
        public CascadeCheckboxView()
        {
            _checkSubtreeItem.Click += (sender, e) => RunMenuCommand(set => set.CheckSubtree(ContextMenuNode));
            _checkSiblingsItem.Click += (sender, e) => RunMenuCommand(set => set.CheckSiblings(ContextMenuNode));
            _checkSiblingSubtreesItem.Click += (sender, e) => RunMenuCommand(set => set.CheckSiblingSubtrees(ContextMenuNode));
            _uncheckSiblingsItem.Click += (sender, e) => RunMenuCommand(set => set.UncheckSiblings(ContextMenuNode));

            NodeContextMenu.Items.Add(_checkSubtreeItem);
            ColumnContextMenu.Items.AddRange(new ToolStripItem[]
            {
                _checkSiblingsItem, _checkSiblingSubtreesItem, new ToolStripSeparator(), _uncheckSiblingsItem,
            });
        }

        #region 勾選狀態

        /// <summary>
        /// 目前顯示的勾選狀態；null 表示不顯示勾選框。
        /// </summary>
        /// <remarks>
        /// <see cref="SetTree"/> 會自動換上一份新的；想保留先前的勾選，或讓好幾個控制項共用同一份時，才需要自己設定。
        /// 它的 <see cref="MenuCheckSet.Tree"/> 應該就是目前顯示的樹，否則所有節點都不會有勾選框。
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuCheckSet CheckSet
        {
            get => _checkSet;
            set
            {
                if (ReferenceEquals(_checkSet, value)) return;

                if (_checkSet != null) _checkSet.Changed -= OnCheckSetChanged;
                _checkSet = value;
                if (_checkSet != null) _checkSet.Changed += OnCheckSetChanged;

                InvalidateNodes();
            }
        }

        /// <summary>
        /// 設定要顯示的選單樹，並換上一份針對它的全新 <see cref="CheckSet"/>（沒有任何勾選、沒有歷史）。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空，<see cref="CheckSet"/> 也變成 null。</param>
        public override void SetTree(MenuTree tree)
        {
            CheckSet = tree == null ? null : new MenuCheckSet(tree);
            base.SetTree(tree);
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

        private void OnCheckSetChanged(object sender, MenuCheckChangedEventArgs e)
        {
            InvalidateNodes();
            OnCheckedChanged(e);
        }

        // 這個節點有沒有勾選框：要是目前這份勾選的樹裡的節點。
        private bool TryGetCheckableNode(ICascadeNode node, out MenuNode menuNode)
        {
            menuNode = node as MenuNode;
            return menuNode != null && _checkSet != null && _checkSet.Contains(menuNode);
        }

        #endregion 勾選狀態

        #region 繪製

        /// <summary>
        /// 勾選框在某一列裡的位置。
        /// </summary>
        /// <param name="rowBounds">那一列的矩形，欄位內的座標。</param>
        /// <returns>勾選框的矩形，和 <paramref name="rowBounds"/> 同一個座標系；點在這個範圍內會切換勾選。</returns>
        public Rectangle GetCheckBoxBounds(Rectangle rowBounds)
        {
            return new Rectangle(
                rowBounds.Left + CHECK_BOX_MARGIN,
                rowBounds.Top + Math.Max(0, (rowBounds.Height - GLYPH_SIZE.Height) / 2),
                GLYPH_SIZE.Width,
                Math.Min(GLYPH_SIZE.Height, rowBounds.Height));
        }

        /// <summary>
        /// 列高至少要放得下勾選框；字型很小時（例如 9pt 的新細明體只有 12 像素高）勾選框才不會被裁掉。
        /// </summary>
        protected override int MinimumItemHeight => GLYPH_SIZE.Height + (CHECK_BOX_VERTICAL_PADDING * 2);

        private static Size MeasureGlyph()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal);
            }
        }

        /// <summary>
        /// 在預設外觀的前面多畫一個勾選框，文字往右讓開；沒有勾選框的節點畫法和基底相同。
        /// </summary>
        protected override void DrawDefaultNode(CascadeDrawNodeEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (!TryGetCheckableNode(e.Node, out var node))
            {
                base.DrawDefaultNode(e);
                return;
            }

            var state = CheckBoxStateOf(node, e.IsEnabled);
            var box = GetCheckBoxBounds(e.Bounds);
            var textLeft = box.Right + CHECK_BOX_MARGIN;
            DrawDefaultNode(e, Rectangle.FromLTRB(Math.Min(textLeft, e.Bounds.Right), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom));

            CheckBoxRenderer.DrawCheckBox(e.Graphics, box.Location, state);
        }

        private CheckBoxState CheckBoxStateOf(MenuNode node, bool enabled)
        {
            if (!_checkSet.IsChecked(node)) return enabled ? CheckBoxState.UncheckedNormal : CheckBoxState.UncheckedDisabled;
            if (_checkSet.IsFullyChecked(node)) return enabled ? CheckBoxState.CheckedNormal : CheckBoxState.CheckedDisabled;

            return enabled ? CheckBoxState.MixedNormal : CheckBoxState.MixedDisabled;
        }

        #endregion 繪製

        #region 滑鼠與快顯功能表

        /// <summary>
        /// 右鍵時是否顯示內建的快顯功能表（<see cref="NodeContextMenu"/> 與 <see cref="ColumnContextMenu"/>），預設為 true。
        /// </summary>
        [Category("Cascade")]
        [Description("右鍵點節點或欄位空白處時，是否顯示內建的勾選快顯功能表。")]
        [DefaultValue(true)]
        public bool ShowCheckContextMenu { get; set; } = true;

        /// <summary>
        /// 右鍵點節點時開啟的快顯功能表，內建「勾選(含所有子節點)」；可以在後面加上自己的項目。
        /// </summary>
        /// <remarks>右鍵不改選取，自己加的項目要用 <see cref="ContextMenuNode"/> 取得被點的節點。</remarks>
        [Browsable(false)]
        public ContextMenuStrip NodeContextMenu { get; } = new ContextMenuStrip();

        /// <summary>
        /// 右鍵點欄位空白處（最後一列下方）時開啟的快顯功能表，內建「全部勾選」、「全部勾選(含子節點)」、「全部取消」，
        /// 對象是那一欄的全部節點；可以在後面加上自己的項目。
        /// </summary>
        /// <remarks>
        /// 自己加的項目用 <see cref="ContextMenuNode"/> 取得那一欄的第一個節點，它的 <see cref="MenuNode.Parent"/> 的子節點就是整欄。
        /// 欄位排滿、沒有空白處時無法開啟，這時請把控制項拉高。
        /// </remarks>
        [Browsable(false)]
        public ContextMenuStrip ColumnContextMenu { get; } = new ContextMenuStrip();

        /// <summary>
        /// 最近一次開啟的快顯功能表針對的節點：<see cref="NodeContextMenu"/> 是被點的節點，
        /// <see cref="ColumnContextMenu"/> 是那一欄的第一個節點（代表那一層）。還沒開過時為 null。
        /// </summary>
        /// <remarks>開啟時記下來，之後選取即使改變也不影響。</remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MenuNode ContextMenuNode { get; private set; }

        /// <summary>
        /// <see cref="NodeContextMenu"/>「勾選(含所有子節點)」的文字：勾選節點與它的所有子孫，並補勾祖先。
        /// </summary>
        [Category("Cascade")]
        [Description("快顯功能表「勾選(含所有子節點)」的文字。")]
        [DefaultValue(DEFAULT_CHECK_SUBTREE_TEXT)]
        [Localizable(true)]
        public string CheckSubtreeMenuText
        {
            get => _checkSubtreeItem.Text;
            set => _checkSubtreeItem.Text = value ?? string.Empty;
        }

        /// <summary>
        /// <see cref="ColumnContextMenu"/>「全部勾選」的文字：勾選這一欄的全部節點並補勾祖先，子節點維持原狀。
        /// </summary>
        [Category("Cascade")]
        [Description("快顯功能表「全部勾選」的文字。")]
        [DefaultValue(DEFAULT_CHECK_SIBLINGS_TEXT)]
        [Localizable(true)]
        public string CheckSiblingsMenuText
        {
            get => _checkSiblingsItem.Text;
            set => _checkSiblingsItem.Text = value ?? string.Empty;
        }

        /// <summary>
        /// <see cref="ColumnContextMenu"/>「全部勾選(含子節點)」的文字：勾選這一欄的全部節點與它們的所有子孫，並補勾祖先。
        /// </summary>
        [Category("Cascade")]
        [Description("快顯功能表「全部勾選(含子節點)」的文字。")]
        [DefaultValue(DEFAULT_CHECK_SIBLING_SUBTREES_TEXT)]
        [Localizable(true)]
        public string CheckSiblingSubtreesMenuText
        {
            get => _checkSiblingSubtreesItem.Text;
            set => _checkSiblingSubtreesItem.Text = value ?? string.Empty;
        }

        /// <summary>
        /// <see cref="ColumnContextMenu"/>「全部取消」的文字：取消這一欄的全部節點與它們的所有子孫，祖先不動。
        /// </summary>
        [Category("Cascade")]
        [Description("快顯功能表「全部取消」的文字。")]
        [DefaultValue(DEFAULT_UNCHECK_SIBLINGS_TEXT)]
        [Localizable(true)]
        public string UncheckSiblingsMenuText
        {
            get => _uncheckSiblingsItem.Text;
            set => _uncheckSiblingsItem.Text = value ?? string.Empty;
        }

        /// <summary>
        /// 勾選框上的左鍵只切換勾選，不選取那一列；其他位置照基底的規則選取。
        /// </summary>
        /// <remarks>
        /// 例如 <see cref="CascadeColumnLayoutMode.Sliding"/> 時停在第三層、去勾第四層的節點，選取不會跳過去，畫面也不會滑動。
        /// </remarks>
        protected override bool SelectsOnLeftClick(ICascadeNode node, Rectangle bounds, Point location)
        {
            return !(TryGetCheckableNode(node, out _) && GetCheckBoxBounds(bounds).Contains(location));
        }

        /// <summary>
        /// 左鍵點在勾選框上就切換勾選（按住 Shift 時連同所有子節點）；右鍵開啟 <see cref="NodeContextMenu"/>。
        /// 之後才觸發 <see cref="CascadeView.NodeMouseClick"/>，所以訂閱端看到的是切換之後的狀態。
        /// </summary>
        /// <remarks>
        /// 勾選框上的左鍵不改選取（見 <see cref="SelectsOnLeftClick"/>），基底只在按下與放開都在同一個勾選框上時才會呼叫這裡。
        /// Shift＋點勾選框：還沒全勾就 <see cref="MenuCheckSet.CheckSubtree"/>，已經全勾就取消，所以連點兩下會回到原狀。
        /// 右鍵不改選取，功能表針對的節點記在 <see cref="ContextMenuNode"/>。
        /// </remarks>
        protected override void OnNodeMouseClick(CascadeNodeMouseEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (TryGetCheckableNode(e.Node, out var node))
            {
                if (e.Button == MouseButtons.Left && GetCheckBoxBounds(e.Bounds).Contains(e.Location))
                {
                    Toggle(node, (ModifierKeys & Keys.Shift) != 0);
                }
                else if (e.Button == MouseButtons.Right && ShowCheckContextMenu)
                {
                    ShowContextMenu(NodeContextMenu, node, e.ScreenLocation);
                }
            }

            base.OnNodeMouseClick(e);
        }

        /// <summary>
        /// 右鍵點在欄位空白處時開啟 <see cref="ColumnContextMenu"/>，對象是那一欄的全部節點。
        /// </summary>
        protected override void OnColumnBlankMouseClick(int columnIndex, IReadOnlyList<ICascadeNode> nodes, MouseButtons button, Point screenLocation)
        {
            base.OnColumnBlankMouseClick(columnIndex, nodes, button, screenLocation);

            if (button == MouseButtons.Right && ShowCheckContextMenu && nodes != null && nodes.Count > 0
                && TryGetCheckableNode(nodes[0], out var first))
            {
                ShowContextMenu(ColumnContextMenu, first, screenLocation);
            }
        }

        private void ShowContextMenu(ContextMenuStrip menu, MenuNode target, Point screenLocation)
        {
            ContextMenuNode = target;
            menu.Show(screenLocation);
        }

        // 功能表開著的時候樹或勾選狀態可能被換掉，執行前再確認一次節點還在。
        private void RunMenuCommand(Action<MenuCheckSet> command)
        {
            if (_checkSet != null && _checkSet.Contains(ContextMenuNode)) command(_checkSet);
        }

        #endregion 滑鼠與快顯功能表

        #region 鍵盤

        /// <summary>
        /// 焦點在欄位上、而且有 <see cref="CheckSet"/> 時，攔下 Space（切換目前選取節點的勾選）、
        /// Shift+Space（連同所有子節點一起勾選，已經全勾時取消，和 Shift＋點勾選框相同）、
        /// Ctrl+Z（復原）、Ctrl+Y 與 Ctrl+Shift+Z（重做）。
        /// </summary>
        /// <remarks>
        /// 這幾個鍵只要符合條件就一律攔下，即使當下沒有選取或沒有步驟可以復原，行為才一致。
        /// 復原或重做之後，選取會跳到那一步針對的節點，方便看到改了哪裡；針對整棵樹的步驟不移動選取。
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_checkSet != null && IsColumnFocused)
            {
                switch (keyData)
                {
                    case Keys.Space:
                    case Keys.Shift | Keys.Space:
                        if (TryGetCheckableNode(SelectedNode, out var node)) Toggle(node, keyData == (Keys.Shift | Keys.Space));
                        return true;

                    case Keys.Control | Keys.Z:
                        SelectTarget(_checkSet.Undo());
                        return true;

                    case Keys.Control | Keys.Y:
                    case Keys.Control | Keys.Shift | Keys.Z:
                        SelectTarget(_checkSet.Redo());
                        return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 點勾選框與 Space 共用：一般是 Toggle；withSubtree（按住 Shift）時還沒全勾就連同子孫一起勾，已經全勾就取消。
        private void Toggle(MenuNode node, bool withSubtree)
        {
            if (!withSubtree) _checkSet.Toggle(node);
            else if (_checkSet.IsFullyChecked(node)) _checkSet.Uncheck(node);
            else _checkSet.CheckSubtree(node);
        }

        private void SelectTarget(MenuNode target)
        {
            if (target != null && _checkSet.Contains(target)) TrySelectNode(target);
            FocusActiveColumn();
        }

        /// <summary>
        /// 欄位一建立就關掉輸入法。
        /// </summary>
        /// <remarks>
        /// 中文輸入法開著的時候，Space 會先被輸入法收走，控制項收到的是 ProcessKey 而不是 Space，勾選就切換不了。
        /// 這個控制項的 Space 是指令而不是文字，所以焦點在欄位上時不需要輸入法。
        /// </remarks>
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);

            if (e.Control is ListBox) e.Control.ImeMode = ImeMode.Disable;
        }

        #endregion 鍵盤

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_checkSet != null) _checkSet.Changed -= OnCheckSetChanged;
                NodeContextMenu.Dispose();
                ColumnContextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}