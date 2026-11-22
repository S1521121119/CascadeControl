using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;

namespace CascadeControl.State
{
    /// <summary>
    /// 具備節點勾選狀態的 <see cref="CascadeState"/>：在 <see cref="SetTree"/> 時一併建立對應的 <see cref="MenuCheckSet"/>。
    /// </summary>
    /// <remarks>
    /// 這個檔案不相依 System.Windows.Forms，可同時綁定到 <see cref="Views.Columns.CascadeColumnsView"/> 與 <see cref="Views.Diagram.CascadeDiagramView"/>。
    /// </remarks>
    public class CascadeCheckableState : CascadeState
    {
        private MenuCheckSet _checkSet;

        /// <summary>
        /// 目前的勾選狀態；null 表示沒有勾選狀態。
        /// </summary>
        /// <remarks>
        /// 設定時把它的 <see cref="MenuCheckSet.History"/> 換成這份狀態的 <see cref="CascadeState.History"/>，勾選與樹的編輯才會排在同一條時間軸上；
        /// 換下來的那一份改回自己的一份新歷史。舊步驟會作用到換下來的那一份，所以 <see cref="CascadeState.History"/> 會清空。
        /// 更換這個屬性會觸發 <see cref="CheckSetChanged"/>，不會觸發 <see cref="CheckedChanged"/>。
        /// </remarks>
        public MenuCheckSet CheckSet
        {
            get => _checkSet;
            set
            {
                if (ReferenceEquals(_checkSet, value)) return;

                if (_checkSet != null)
                {
                    _checkSet.Changed -= OnInnerCheckSetChanged;
                    if (ReferenceEquals(_checkSet.History, History)) _checkSet.History = new MenuHistory();
                }

                _checkSet = value;
                if (_checkSet != null)
                {
                    _checkSet.History = History;
                    _checkSet.Changed += OnInnerCheckSetChanged;
                }

                History.Clear();
                OnCheckSetChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// <see cref="CheckSet"/> 的勾選狀態改變後觸發，內容與 <see cref="MenuCheckSet.Changed"/> 相同。
        /// </summary>
        /// <remarks>換上另一份 <see cref="CheckSet"/> 時不會觸發。</remarks>
        public event EventHandler<MenuCheckChangedEventArgs> CheckedChanged;

        /// <summary>
        /// <see cref="CheckSet"/> 參考被更換後觸發（包含 <see cref="SetTree"/> 與 <see cref="SetTopLevelNodes"/>）。
        /// </summary>
        public event EventHandler CheckSetChanged;

        /// <summary>
        /// 設定要顯示的選單樹，並在觸發 <see cref="CascadeState.SelectedPathChanged"/> 之前先換上針對它的全新 <see cref="CheckSet"/>。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空，<see cref="CheckSet"/> 也變成 null。</param>
        public override void SetTree(MenuTree tree)
        {
            CheckSet = tree == null ? null : new MenuCheckSet(tree);
            base.SetTree(tree);
        }

        /// <summary>
        /// 重新設定第一層的頂層節點集合，並將 <see cref="CheckSet"/> 設為 null。
        /// </summary>
        /// <param name="topLevelNodes">第一層節點；傳 null 或空集合表示清空。</param>
        public override void SetTopLevelNodes(IEnumerable<ICascadeNode> topLevelNodes)
        {
            CheckSet = null;
            base.SetTopLevelNodes(topLevelNodes);
        }

        /// <summary>
        /// 觸發 <see cref="CheckedChanged"/>。
        /// </summary>
        protected virtual void OnCheckedChanged(MenuCheckChangedEventArgs e) => CheckedChanged?.Invoke(this, e);

        /// <summary>
        /// 觸發 <see cref="CheckSetChanged"/>。
        /// </summary>
        protected virtual void OnCheckSetChanged(EventArgs e) => CheckSetChanged?.Invoke(this, e);

        private void OnInnerCheckSetChanged(object sender, MenuCheckChangedEventArgs e) => OnCheckedChanged(e);
    }
}
