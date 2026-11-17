using System;
using System.Collections.Generic;

namespace CascadeControl.Trees
{
    /// <summary>
    /// <see cref="MenuCheckSet"/> 的勾選狀態為什麼改變。
    /// </summary>
    public enum MenuCheckChangeReason
    {
        /// <summary>呼叫了勾選、取消這一類的異動方法。</summary>
        User,

        /// <summary><see cref="MenuCheckSet.Undo"/>。</summary>
        Undo,

        /// <summary><see cref="MenuCheckSet.Redo"/>。</summary>
        Redo,

        /// <summary><see cref="MenuCheckSet.Apply"/>。</summary>
        Apply,

        /// <summary><see cref="MenuCheckSet.Reconcile"/>。</summary>
        Reconcile,
    }

    /// <summary>
    /// <see cref="MenuCheckSet.Changed"/> 的事件參數：這一次實際多勾了哪些節點、取消了哪些節點。
    /// </summary>
    public class MenuCheckChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="added">這一次變成勾選的節點。</param>
        /// <param name="removed">這一次變成未勾選的節點。</param>
        /// <param name="target">這一次操作針對的節點；針對整棵樹的操作為 null。</param>
        /// <param name="reason">改變的原因。</param>
        /// <exception cref="ArgumentNullException"><paramref name="added"/> 或 <paramref name="removed"/> 為 null。</exception>
        public MenuCheckChangedEventArgs(
            IReadOnlyList<MenuNode> added, IReadOnlyList<MenuNode> removed, MenuNode target, MenuCheckChangeReason reason)
        {
            Added = added ?? throw new ArgumentNullException(nameof(added));
            Removed = removed ?? throw new ArgumentNullException(nameof(removed));
            Target = target;
            Reason = reason;
        }

        /// <summary>這一次變成勾選的節點；包含自動補勾的祖先。</summary>
        public IReadOnlyList<MenuNode> Added { get; }

        /// <summary>這一次變成未勾選的節點；包含一起取消的子孫。</summary>
        public IReadOnlyList<MenuNode> Removed { get; }

        /// <summary>
        /// 這一次操作針對的節點；針對整棵樹的操作（全部勾選、全部取消、<see cref="MenuCheckSet.Apply"/>、<see cref="MenuCheckSet.Reconcile"/>）為 null。
        /// </summary>
        public MenuNode Target { get; }

        /// <summary>改變的原因。</summary>
        public MenuCheckChangeReason Reason { get; }
    }
}