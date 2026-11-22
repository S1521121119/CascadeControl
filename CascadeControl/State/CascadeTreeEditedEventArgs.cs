using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.State
{
    /// <summary>
    /// <see cref="CascadeState.TreeEdited"/> 的事件參數。
    /// </summary>
    public class CascadeTreeEditedEventArgs : EventArgs
    {
        /// <summary>
        /// 建立事件參數。
        /// </summary>
        /// <param name="action">編輯的種類。</param>
        /// <param name="nodes">受影響的節點，見 <see cref="Nodes"/>。</param>
        public CascadeTreeEditedEventArgs(CascadeEditAction action, IEnumerable<MenuNode> nodes)
        {
            Action = action;
            Nodes = Array.AsReadOnly((nodes ?? Enumerable.Empty<MenuNode>()).ToArray());
        }

        /// <summary>編輯的種類。</summary>
        public CascadeEditAction Action { get; }

        /// <summary>
        /// 受影響的節點：<see cref="CascadeEditAction.Cut"/> 與 <see cref="CascadeEditAction.Delete"/> 是已經從樹中移除的節點；
        /// <see cref="CascadeEditAction.Undo"/> 與 <see cref="CascadeEditAction.Redo"/> 是那一步插入、移除或改名的節點（見 <see cref="MenuHistoryResult.Nodes"/>），
        /// 可能已經不在樹裡；其他是編輯後留在樹裡的節點，依畫面上的順序排列。
        /// </summary>
        public IReadOnlyList<MenuNode> Nodes { get; }
    }
}