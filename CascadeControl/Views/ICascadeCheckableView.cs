using CascadeControl.Trees;
using System;

namespace CascadeControl.Views
{
    /// <summary>
    /// 具備節點勾選能力的 <see cref="ICascadeView"/>。
    /// </summary>
    public interface ICascadeCheckableView : ICascadeView
    {
        /// <summary>
        /// 目前顯示的勾選狀態；null 表示不顯示勾選。
        /// </summary>
        MenuCheckSet CheckSet { get; set; }

        /// <summary>
        /// <see cref="CheckSet"/> 的勾選狀態改變後觸發。
        /// </summary>
        event EventHandler<MenuCheckChangedEventArgs> CheckedChanged;
    }
}