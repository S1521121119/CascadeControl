using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Views
{
    /// <summary>
    /// <see cref="CascadeView.TreeEdited"/> 對樹做了哪一種編輯。
    /// </summary>
    public enum CascadeEditAction
    {
        /// <summary>插入新節點（Insert Next / Before，或在欄位空白處 Insert 加在最後）。</summary>
        Insert,

        /// <summary>在葉節點底下建立第一個子節點。</summary>
        CreateChild,

        /// <summary>重新命名；同一層已有同名節點時，新名稱自動加上後綴（例如 <c>Name - 2</c>）。</summary>
        Rename,

        /// <summary>從 Windows 剪貼簿貼上一個或多個節點（貼在節點前後，或貼成葉節點的子節點），順序與複製時相同。</summary>
        Paste,

        /// <summary>剪下：複製到 Windows 剪貼簿後從樹中移除。</summary>
        Cut,

        /// <summary>刪除。</summary>
        Delete,

        /// <summary>復原了一個改到樹的步驟（<see cref="CascadeView.Undo"/>）。只改勾選的步驟不觸發 <see cref="CascadeView.TreeEdited"/>。</summary>
        Undo,

        /// <summary>重做了一個改到樹的步驟（<see cref="CascadeView.Redo"/>）。只改勾選的步驟不觸發 <see cref="CascadeView.TreeEdited"/>。</summary>
        Redo,
    }

    /// <summary>
    /// <see cref="CascadeView.TreeEdited"/> 的事件參數。
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