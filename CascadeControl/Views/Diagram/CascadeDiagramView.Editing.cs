using CascadeControl.State;
using CascadeControl.Trees;
using System.Collections.Generic;
using System.ComponentModel;

namespace CascadeControl.Views.Diagram
{
    // 編輯：唯讀切換，以及複製／剪下／貼上／刪除／復原／重做。改樹由 State 執行，這裡只決定對象與確認。
    public partial class CascadeDiagramView
    {
        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        private bool _readOnly = true;

        #region 屬性

        /// <summary>
        /// 是否唯讀，預設為 true。
        /// </summary>
        /// <remarks>
        /// true：只能複製（右鍵功能表只有「複製」、「複製(路徑)」，快捷鍵只有 Ctrl+C、Ctrl+Shift+C），改到樹的步驟也不能復原或重做。
        /// false：右鍵功能表與快捷鍵可以剪下、貼上、刪除，以及復原、重做。
        /// 只對 <see cref="State"/> 以 <see cref="CascadeState.SetTree"/> 顯示的 <see cref="MenuTree"/> 有作用。
        /// 每個檢視各自決定唯讀，和綁同一份 <see cref="State"/> 的其他檢視無關。
        /// </remarks>
        [Category("Cascade")]
        [Description("true：只能複製；false：右鍵功能表與快捷鍵可以剪下、貼上、刪除、復原、重做。只對 SetTree 顯示的樹有作用。")]
        [DefaultValue(true)]
        public bool ReadOnly
        {
            get => _readOnly;
            set => _readOnly = value;
        }

        /// <summary>
        /// 現在能不能復原：<see cref="State"/> 的 <see cref="CascadeState.History"/> 有步驟，而且那一步改到樹時控制項不是唯讀（<see cref="ReadOnly"/> 為 false）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanUndo => _state.History.CanUndo && (CanEdit || !_state.History.UndoChangesTree);

        /// <summary>
        /// 現在能不能重做：<see cref="State"/> 的 <see cref="CascadeState.History"/> 有被復原的步驟，而且那一步改到樹時控制項不是唯讀。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanRedo => _state.History.CanRedo && (CanEdit || !_state.History.RedoChangesTree);

        #endregion 屬性

        #region 公開的編輯指令

        /// <summary>
        /// 把 <see cref="SelectedNode"/> 連同子孫複製到 Windows 剪貼簿。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有選取或剪貼簿被其他程式佔用時為 false。</returns>
        public bool Copy() => CascadeClipboard.CopyNodes(Single(SelectedMenuNode));

        /// <summary>
        /// 把 <see cref="SelectedNode"/> 從第一層到它的路徑，以 CSV 文字（含表頭）複製到 Windows 剪貼簿；不含子孫。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有選取或剪貼簿被其他程式佔用時為 false。</returns>
        /// <remarks>只放純文字，不放 <see cref="CascadeClipboard.CLIPBOARD_FORMAT"/>：內容含上層路徑，不是要貼回控制項的節點。</remarks>
        public bool CopyPath() => CascadeClipboard.CopyNodePaths(Single(SelectedMenuNode));

        /// <summary>
        /// 把 <see cref="SelectedNode"/> 複製到 Windows 剪貼簿，然後從樹中移除。
        /// </summary>
        /// <returns>有剪下時為 true；唯讀、沒有選取或剪貼簿被佔用（這時不會移除）時為 false。</returns>
        public bool Cut() => CutNode(SelectedMenuNode);

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼到 <see cref="SelectedNode"/> 的後面或前面，順序與複製時相同；沒有選取時加在第一層的最後。
        /// </summary>
        /// <param name="before">true 貼在前面，false 貼在後面。</param>
        /// <returns>有貼上時為 true。</returns>
        /// <remarks>同一層已有同名節點時，貼上的節點名稱自動加上後綴（例如 <c>Name - 2</c>）。貼完選取第一個貼上的節點。</remarks>
        public bool Paste(bool before)
        {
            var node = SelectedMenuNode;
            if (node != null) return PasteBeside(node, before);

            return Tree != null && PasteInto(Tree.Root, Tree.Root.Children.Count);
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼成 <see cref="SelectedNode"/> 的子節點，順序與複製時相同；只有 <see cref="SelectedNode"/> 是葉節點時才有作用。
        /// </summary>
        /// <returns>有貼上時為 true；唯讀、沒有選取、選取的節點已經有子節點，或剪貼簿上沒有節點時為 false。</returns>
        public bool PasteAsChild() => PasteIntoLeaf(SelectedMenuNode);

        /// <summary>
        /// 刪除 <see cref="SelectedNode"/> 及其所有子孫；刪除前一律跳出確認。
        /// </summary>
        /// <returns>有刪除時為 true；唯讀、沒有選取或使用者取消時為 false。</returns>
        public bool Delete() => DeleteNode(SelectedMenuNode);

        /// <summary>
        /// 復原 <see cref="CascadeState.History"/> 最近的一個步驟。
        /// </summary>
        /// <returns>有復原時為 true；<see cref="CanUndo"/> 為 false，或樹已經和記錄時對不上（這時整份歷史會被清除）時為 false。</returns>
        /// <remarks>由 <see cref="CascadeState.Undo"/> 執行：選取跳到那一步的位置，改到樹時觸發 <see cref="CascadeState.TreeEdited"/>。</remarks>
        public bool Undo() => CanUndo && _state.Undo() != null;

        /// <summary>
        /// 重做最近一次被復原的步驟。
        /// </summary>
        /// <returns>有重做時為 true；<see cref="CanRedo"/> 為 false，或樹已經和記錄時對不上時為 false。</returns>
        /// <remarks>由 <see cref="CascadeState.Redo"/> 執行，規則同 <see cref="Undo"/>。</remarks>
        public bool Redo() => CanRedo && _state.Redo() != null;

        #endregion 公開的編輯指令

        #region 對象與樹

        private bool CanEdit => !_readOnly && Tree != null;

        private bool CanEditNode(MenuNode node) => CanEdit && _state.IsInTree(node);

        private static IReadOnlyList<MenuNode> Single(MenuNode node) => node == null ? NoMenuNodes : new[] { node };

        private bool CutNode(MenuNode node)
        {
            if (!CanEditNode(node) || !CascadeClipboard.CopyNodes(Single(node))) return false;

            return _state.CutNodes(Single(node));
        }

        private bool DeleteNode(MenuNode node)
        {
            if (!CanEditNode(node) || !CascadeEditPrompts.ConfirmDelete(this, Single(node))) return false;

            // 對話框開著的時候樹可能被換掉，再確認一次。
            return CanEditNode(node) && _state.DeleteNodes(Single(node));
        }

        private bool PasteBeside(MenuNode target, bool before)
        {
            if (!_state.IsInTree(target)) return false;

            var position = target.SiblingNumber - 1 + (before ? 0 : 1);
            return PasteInto(target.Parent, position);
        }

        // 貼成葉節點的子節點；已經有子節點的節點不接受，和建立子節點的規則一致。
        private bool PasteIntoLeaf(MenuNode target)
        {
            if (!_state.IsInTree(target) || !target.IsLeaf) return false;

            return PasteInto(target, 0);
        }

        private bool PasteInto(MenuNode parent, int position)
        {
            if (!CanEdit || !_state.IsInTreeOrRoot(parent)) return false;

            var sources = CascadeClipboard.ReadNodes();
            if (sources.Count == 0) return false;

            return _state.PasteNodes(parent, position, sources).Count > 0;
        }

        #endregion 對象與樹
    }
}
