namespace CascadeControl.Views
{
    /// <summary>
    /// 檢視控制項的編輯快捷鍵對應的指令；按鍵與指令的對照見 <see cref="CascadeEditShortcuts"/>。
    /// </summary>
    internal enum CascadeEditCommand
    {
        /// <summary>取消多選；沒有多選時清除選取。</summary>
        ClearSelection,

        /// <summary>複製節點。</summary>
        Copy,

        /// <summary>複製節點的路徑。</summary>
        CopyPath,

        /// <summary>多選同一層的全部節點。</summary>
        SelectAll,

        /// <summary>切換目前節點的勾選。</summary>
        ToggleCheck,

        /// <summary>復原。</summary>
        Undo,

        /// <summary>重做。</summary>
        Redo,

        /// <summary>剪下節點。</summary>
        Cut,

        /// <summary>貼在目前節點後面。</summary>
        Paste,

        /// <summary>貼在目前節點前面。</summary>
        PasteBefore,

        /// <summary>貼成目前節點的子節點。</summary>
        PasteAsChild,

        /// <summary>刪除節點。</summary>
        Delete,

        /// <summary>改名。</summary>
        Rename,

        /// <summary>在目前節點後面插入。</summary>
        Insert,

        /// <summary>在目前節點前面插入。</summary>
        InsertBefore,

        /// <summary>建立子節點。</summary>
        CreateChild,
    }
}
