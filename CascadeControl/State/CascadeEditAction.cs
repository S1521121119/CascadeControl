namespace CascadeControl.State
{
    /// <summary>
    /// <see cref="CascadeState.TreeEdited"/> 對樹做了哪一種編輯。
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

        /// <summary>復原了一個改到樹的步驟（<see cref="CascadeState.Undo"/>）。只改勾選的步驟不觸發 <see cref="CascadeState.TreeEdited"/>。</summary>
        Undo,

        /// <summary>重做了一個改到樹的步驟（<see cref="CascadeState.Redo"/>）。只改勾選的步驟不觸發 <see cref="CascadeState.TreeEdited"/>。</summary>
        Redo,

        /// <summary>把一個或多個節點（連同子孫）移到另一個父節點底下（<see cref="CascadeState.MoveNodes"/>）。</summary>
        Move,
    }
}