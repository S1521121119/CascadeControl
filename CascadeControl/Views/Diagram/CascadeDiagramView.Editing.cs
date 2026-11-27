using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 編輯：唯讀切換、就地改名與插入、建立子節點，以及複製／剪下／貼上／刪除／復原／重做。改樹由 State 執行，這裡只決定對象與確認。
    public partial class CascadeDiagramView
    {
        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        private bool _readOnly = true;

        // 正在進行的就地編輯；沒有在編輯時為 null。
        private EditSession _edit;

        // 編輯中在圖上按了滑鼠：那一下只用來結束編輯，對應的點擊也要略過。
        private bool _swallowClick;

        // 尚未貼上的待剪下狀態（節點先留在樹上並畫成半透明，真正貼上時才移走）。
        private CascadeClipboard.PendingCut _pendingCut;
        private HashSet<MenuNode> _cutRoots;
        private bool _suppressPendingCutRedraw;

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
            set
            {
                _readOnly = value;
                if (value)
                {
                    AbortEdit();
                    CancelPendingCut(redraw: true);
                }
            }
        }

        /// <summary>
        /// 是否正在就地編輯節點名稱（<see cref="BeginRename()"/>、<see cref="BeginInsert(bool)"/>、<see cref="BeginCreateChild()"/> 開啟的編輯框）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsEditing => _edit != null;

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
        /// 把 <see cref="SelectedNode"/> 連同子孫複製到 Windows 剪貼簿；有多選時改成複製 <see cref="MultiSelectedNodes"/>。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有選取或剪貼簿被其他程式佔用時為 false。</returns>
        /// <remarks>多選時依圖上的前序排列，祖先也被多選的節點不重複複製（祖先已經帶著它）。</remarks>
        public bool Copy()
        {
            CommitPendingEdit();
            return CopyNodes(EditTargets());
        }

        /// <summary>
        /// 把 <see cref="SelectedNode"/> 從第一層到它的路徑，以 CSV 文字（含表頭）複製到 Windows 剪貼簿；不含子孫。有多選時改成每個多選節點的路徑。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有選取或剪貼簿被其他程式佔用時為 false。</returns>
        /// <remarks>只放純文字，不放 <see cref="CascadeClipboard.CLIPBOARD_FORMAT"/>：內容含上層路徑，不是要貼回控制項的節點。</remarks>
        public bool CopyPath()
        {
            CommitPendingEdit();
            return CopyNodePaths(_marks.Count > 0 ? MultiSelectedNodes : Single(SelectedMenuNode));
        }

        /// <summary>
        /// 把 <see cref="SelectedNode"/> 複製到 Windows 剪貼簿並標示為半透明待剪下，直到真正貼上時才從原位置移除；
        /// 有多選時改成多選的節點（祖先也被多選的子孫跟著祖先走）。
        /// </summary>
        /// <returns>有標示為剪下時為 true；唯讀、沒有對象或剪貼簿被佔用時為 false。</returns>
        public bool Cut()
        {
            CommitPendingEdit();
            return CutNodes(EditTargets());
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼到 <see cref="SelectedNode"/> 的後面或前面，順序與複製時相同；沒有選取時加在第一層的最後。
        /// 有多選時改成貼在多選的最後一個節點後面，或第一個節點前面（依圖上的前序，祖先也被多選的子孫不算）。
        /// </summary>
        /// <param name="before">true 貼在前面，false 貼在後面。</param>
        /// <returns>有貼上時為 true。</returns>
        /// <remarks>同一層已有同名節點時，貼上的節點名稱自動加上後綴（例如 <c>Name - 2</c>）。貼完選取第一個貼上的節點，貼了多個時全部多選。</remarks>
        public bool Paste(bool before)
        {
            CommitPendingEdit();
            var node = PasteAnchor(before);
            if (node != null) return PasteBeside(node, before);

            return Tree != null && PasteInto(Tree.Root, Tree.Root.Children.Count);
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼成 <see cref="SelectedNode"/> 的子節點，順序與複製時相同；只有 <see cref="SelectedNode"/> 是葉節點時才有作用。
        /// 有多選時對象改成多選的節點，只有多選剛好一個葉節點時才有作用。
        /// </summary>
        /// <returns>有貼上時為 true；唯讀、沒有對象、對象已經有子節點，或剪貼簿上沒有節點時為 false。</returns>
        /// <remarks>貼完選取第一個貼上的節點，貼了多個時全部多選。</remarks>
        public bool PasteAsChild()
        {
            CommitPendingEdit();
            if (_marks.Count == 0) return PasteIntoLeaf(SelectedMenuNode);

            var marks = MultiSelectedNodes;
            return marks.Count == 1 && PasteIntoLeaf(marks[0]);
        }

        /// <summary>
        /// 刪除 <see cref="SelectedNode"/> 及其所有子孫；有多選時改成多選的節點（可以屬於不同的父節點）。刪除前一律跳出確認，一次復原全部回來。
        /// </summary>
        /// <returns>有刪除時為 true；唯讀、沒有對象或使用者取消時為 false。</returns>
        public bool Delete()
        {
            CommitPendingEdit();
            return DeleteNodes(EditTargets());
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 的後面或前面畫一個空白方塊並在上面開啟編輯框，按 Enter 以輸入的名稱建立新節點，按 Esc 取消。
        /// </summary>
        /// <param name="before">true 插在前面，false 插在後面。</param>
        /// <returns>
        /// 有開啟編輯框時為 true；唯讀、沒有選取，或 <see cref="DrawScope"/> 是 <see cref="CascadeDiagramDrawScope.SelectedSubtree"/>
        /// （選取的節點是圖的起點，旁邊沒有位置）時為 false。
        /// </returns>
        /// <remarks>按 Enter 之前樹與 <see cref="CascadeState.History"/> 都不變，空白方塊只畫在這個檢視上；取消時什麼都沒有發生。結束編輯的規則同 <see cref="BeginRename()"/>。</remarks>
        public bool BeginInsert(bool before)
        {
            CommitPendingEdit();
            return BeginInsert(SelectedMenuNode, before);
        }

        /// <summary>
        /// 在葉節點 <see cref="SelectedNode"/> 底下畫一個空白方塊並在上面開啟編輯框，按 Enter 建立它的第一個子節點，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true；唯讀、沒有選取、選取的節點已經有子節點（請在子節點旁邊用 <see cref="BeginInsert(bool)"/>），或它沒有畫在圖上時為 false。</returns>
        /// <remarks>規則同 <see cref="BeginInsert(bool)"/>。</remarks>
        public bool BeginCreateChild()
        {
            CommitPendingEdit();
            return BeginCreateChild(SelectedMenuNode);
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 的方塊上開啟編輯框，文字是目前的名稱並全選；按 Enter 改名，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true；唯讀、沒有選取，或選取的節點沒有畫在圖上時為 false。</returns>
        /// <remarks>
        /// 編輯框失去焦點或在圖上按了滑鼠時套用。<see cref="State"/> 重新整理（任何綁同一份 State 的檢視改了樹，或換了樹）時，編輯框直接關掉、不套用。
        /// </remarks>
        public bool BeginRename()
        {
            CommitPendingEdit();
            return BeginRename(SelectedMenuNode);
        }

        /// <summary>
        /// 結束進行中的就地編輯；沒有在編輯時不做任何事。
        /// </summary>
        /// <param name="commit">true 套用輸入的名稱（空白或含有保留字元時當成取消），false 取消。</param>
        public void EndEdit(bool commit)
        {
            EndEdit(commit, interactive: false);
        }

        /// <summary>
        /// 復原 <see cref="CascadeState.History"/> 最近的一個步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有復原時為 true；<see cref="CanUndo"/> 為 false，或樹已經和記錄時對不上（這時整份歷史會被清除）時為 false。</returns>
        /// <remarks>由 <see cref="CascadeState.Undo"/> 執行：選取跳到那一步的位置，改到樹時觸發 <see cref="CascadeState.TreeEdited"/>。</remarks>
        public bool Undo()
        {
            CommitPendingEdit();
            return CanUndo && _state.Undo() != null;
        }

        /// <summary>
        /// 重做最近一次被復原的步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有重做時為 true；<see cref="CanRedo"/> 為 false，或樹已經和記錄時對不上時為 false。</returns>
        /// <remarks>由 <see cref="CascadeState.Redo"/> 執行，規則同 <see cref="Undo"/>。</remarks>
        public bool Redo()
        {
            CommitPendingEdit();
            return CanRedo && _state.Redo() != null;
        }

        #endregion 公開的編輯指令

        #region 對象與樹

        private bool CanEdit => !_readOnly && Tree != null;

        private bool CanEditNode(MenuNode node) => CanEdit && _state.IsInTree(node);

        private static IReadOnlyList<MenuNode> Single(MenuNode node) => node == null ? NoMenuNodes : new[] { node };

        private bool CanEditNodes(IReadOnlyList<MenuNode> nodes) => CanEdit && nodes.Count > 0 && nodes.All(_state.IsInTree);

        private bool CopyNodes(IReadOnlyList<MenuNode> nodes) => CascadeClipboard.CopyNodes(nodes);

        private bool CopyNodePaths(IReadOnlyList<MenuNode> nodes) => CascadeClipboard.CopyNodePaths(nodes);

        private bool CutNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes)) return false;

            var roots = TopMostNodes(nodes);
            CascadeClipboard.PendingCut staged;
            var previousSuppress = _suppressPendingCutRedraw;
            _suppressPendingCutRedraw = true;
            try
            {
                CascadeClipboard.PendingCut created = null;
                created = CascadeClipboard.StageCut(_state, roots, () => OnPendingCutCleared(created));
                staged = created;
            }
            finally
            {
                _suppressPendingCutRedraw = previousSuppress;
            }

            if (staged == null) return false;

            _pendingCut = staged;
            _cutRoots = new HashSet<MenuNode>(roots);
            ClearMultiSelection();
            Redraw();
            return true;
        }

        private static List<MenuNode> TopMostNodes(IReadOnlyList<MenuNode> nodes)
        {
            var set = new HashSet<MenuNode>(nodes);
            return nodes.Distinct().Where(node => !set.Any(other => !ReferenceEquals(other, node) && IsSelfOrAncestor(other, node))).ToList();
        }

        private void OnPendingCutCleared(CascadeClipboard.PendingCut cut)
        {
            if (!ReferenceEquals(_pendingCut, cut)) return;

            _pendingCut = null;
            _cutRoots = null;
            if (!_suppressPendingCutRedraw && !IsDisposed) Redraw();
        }

        private bool SyncPendingCut(bool redrawOnClear)
        {
            if (_pendingCut == null) return false;

            var previousSuppress = _suppressPendingCutRedraw;
            if (!redrawOnClear) _suppressPendingCutRedraw = true;
            try
            {
                if (ReferenceEquals(CascadeClipboard.GetActivePendingCut(), _pendingCut)) return true;

                _pendingCut = null;
                _cutRoots = null;
                return false;
            }
            finally
            {
                _suppressPendingCutRedraw = previousSuppress;
            }
        }

        private bool CancelPendingCut(bool redraw, bool clearClipboard = false)
        {
            var cut = _pendingCut;
            if (cut == null) return false;

            _pendingCut = null;
            _cutRoots = null;

            var previousSuppress = _suppressPendingCutRedraw;
            _suppressPendingCutRedraw = true;
            try
            {
                CascadeClipboard.ClearPendingCut(cut, clearClipboard);
            }
            finally
            {
                _suppressPendingCutRedraw = previousSuppress;
            }

            if (redraw && !IsDisposed) Redraw();
            return true;
        }

        private bool CancelActivePendingCut()
        {
            if (!SyncPendingCut(redrawOnClear: true)) return false;

            return CancelPendingCut(redraw: true, clearClipboard: true);
        }

        private bool IsInPendingCut(MenuNode node)
        {
            if (_cutRoots == null || _cutRoots.Count == 0) return false;

            for (var current = node; current != null; current = current.Parent)
            {
                if (_cutRoots.Contains(current)) return true;
            }

            return false;
        }

        private bool IsSameStatePendingCutTarget(MenuNode parent)
        {
            var pending = CascadeClipboard.GetActivePendingCut();
            return pending != null && ReferenceEquals(pending.State, _state) && pending.Nodes.Any(root => IsSelfOrAncestor(root, parent));
        }

        private bool DeleteNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes) || !CascadeEditPrompts.ConfirmDelete(this, nodes)) return false;

            // 對話框開著的時候樹可能被換掉，再確認一次。
            return CanEditNodes(nodes) && _state.DeleteNodes(nodes);
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

            if (CascadeClipboard.TryCompletePendingCutPaste(_state, parent, position, out var pasted)) return pasted;

            var sources = CascadeClipboard.ReadNodes();
            if (sources.Count == 0) return false;

            return _state.PasteNodes(parent, position, sources).Count > 0;
        }

        #endregion 對象與樹

        #region 就地編輯

        // 編輯的對象要是選取的節點（插入時是插在它旁邊的那個）：選取改了會重畫，之後才算得出方塊的位置。

        private bool BeginInsert(MenuNode anchor, bool before)
        {
            if (!CanEditNode(anchor) || _drawScope == CascadeDiagramDrawScope.SelectedSubtree) return false;

            _state.TrySelectNode(anchor);
            var position = anchor.SiblingNumber - 1 + (before ? 0 : 1);
            return OpenPlaceholderEditor(CascadeEditAction.Insert, new TreeDrawer.Placeholder(anchor.Parent, position));
        }

        // 只對葉節點，和 State.CreateChild 的規則一致。
        private bool BeginCreateChild(MenuNode node)
        {
            if (!CanEditNode(node) || !node.IsLeaf) return false;

            _state.TrySelectNode(node);
            return OpenPlaceholderEditor(CascadeEditAction.CreateChild, new TreeDrawer.Placeholder(node, 0));
        }

        private bool BeginRename(MenuNode node)
        {
            if (!CanEditNode(node)) return false;

            _state.TrySelectNode(node);
            return OpenEditBox(new EditSession(CascadeEditAction.Rename, node, null, CreateEditBox(node.Name)));
        }

        // 先把空白方塊畫出來，編輯框才有位置可以蓋；位置不在圖上時 PositionEditBox 會中止編輯並把方塊拿掉。
        private bool OpenPlaceholderEditor(CascadeEditAction action, TreeDrawer.Placeholder placeholder)
        {
            var session = new EditSession(action, placeholder.Node, placeholder, CreateEditBox(string.Empty));
            _edit = session;
            Redraw();

            return _edit == session && OpenEditBox(session);
        }

        private TextBox CreateEditBox(string text)
        {
            return new TextBox
            {
                Name = "txtEdit",
                // 放大時用另外建立的字型，CloseEditBox 負責釋放。
                Font = _zoomFactor == 1f ? Font : new Font(Font.FontFamily, Font.Size * _zoomFactor, Font.Style, Font.Unit),
                Text = text,
                ImeMode = ImeMode.NoControl,
                AutoSize = false,
            };
        }

        // 對象沒有畫在圖上時不開，回傳 false。
        private bool OpenEditBox(EditSession session)
        {
            _edit = session;
            if (FindImageNode(session.Node) == null)
            {
                AbortEdit();
                return false;
            }

            EnsureVisible(session.Node);

            var box = session.Box;
            Controls.Add(box);
            PositionEditBox();

            box.LostFocus += EditBox_LostFocus;
            box.Focus();
            box.SelectAll();
            return true;
        }

        // 編輯框蓋在節點方塊上；圖重畫、縮放或改變大小之後都要再對一次。節點不再畫在圖上時直接關掉。
        // 捲動不用處理：ScrollableControl 捲動時會一起移動子控制項。
        private void PositionEditBox()
        {
            var session = _edit;
            if (session == null) return;

            var bounds = GetClientBoundsOf(session.Node);
            if (bounds.IsEmpty)
            {
                AbortEdit();
                return;
            }

            // 和方塊一樣大，蓋住佔位方塊的虛線框；單行 TextBox 要關掉 AutoSize 才能指定高度。
            session.Box.Bounds = bounds;
        }

        // 焦點離開編輯框（Tab、點了表單上的其他控制項、切到別的程式）就套用；等這一輪訊息處理完再做，不在焦點切換的當下改樹。
        private void EditBox_LostFocus(object sender, EventArgs e)
        {
            var session = _edit;
            if (session == null || session.Prompting || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (_edit == session && !session.Prompting && !session.Box.ContainsFocus) EndEdit(commit: true, interactive: false);
            }));
        }

        // 公開指令開始前，先把還開著的編輯框套用掉。
        private void CommitPendingEdit()
        {
            if (_edit != null) EndEdit(commit: true, interactive: false);
        }

        /// <summary>
        /// 結束就地編輯。
        /// </summary>
        /// <param name="commit">是否套用。</param>
        /// <param name="interactive">
        /// 是否由使用者按 Enter 觸發：名稱含有保留字元時跳出提示並繼續編輯；其他情況直接當成取消，不跳對話框。
        /// </param>
        private void EndEdit(bool commit, bool interactive)
        {
            var session = _edit;
            if (session == null) return;

            var name = session.Box.Text.Trim();

            if (commit && CascadeEditPrompts.HasReservedCharacter(name))
            {
                if (interactive)
                {
                    session.Prompting = true;
                    try
                    {
                        CascadeEditPrompts.WarnReservedCharacter(this, session.Action);
                    }
                    finally
                    {
                        session.Prompting = false;
                    }

                    if (_edit == session)
                    {
                        session.Box.Focus();
                        session.Box.SelectAll();
                    }

                    return;
                }

                commit = false;
            }

            // 先關編輯框再改樹：State 改完會重新整理，那時已經沒有編輯要中止。
            // 有建立節點時選取跳到新節點，重畫時已經沒有空白方塊；沒有建立時自己重畫把它拿掉。
            CloseEditBox(session);
            var applied = commit && name.Length > 0 && ApplyEdit(session, name) != null;
            if (!applied && session.Placeholder != null) Redraw();
        }

        // 交給 State 套用輸入的名稱；對象已經不在樹裡，或改名成原本的名稱時回傳 null。
        private MenuNode ApplyEdit(EditSession session, string name)
        {
            switch (session.Action)
            {
                case CascadeEditAction.Insert: return _state.InsertNode(session.Placeholder.Parent, session.Placeholder.Position, name);
                case CascadeEditAction.CreateChild: return _state.CreateChild(session.Placeholder.Parent, name);
                case CascadeEditAction.Rename: return _state.RenameNode(session.Node, name);
                default: return null;
            }
        }

        /// <summary>
        /// 不套用，直接丟掉進行中的編輯；State 重新整理、換樹、換 State 或改成唯讀時呼叫。
        /// </summary>
        private void AbortEdit()
        {
            var session = _edit;
            if (session == null) return;

            CloseEditBox(session);
            if (session.Placeholder != null) Redraw();
        }

        // 拿掉編輯框。編輯框原本有焦點時把焦點交回控制項，焦點才不會因為編輯框消失而掉出去。
        private void CloseEditBox(EditSession session)
        {
            _edit = null;

            var box = session.Box;
            box.LostFocus -= EditBox_LostFocus;

            if (box.ContainsFocus && CanFocus) Focus();

            Controls.Remove(box);
            var zoomedFont = ReferenceEquals(box.Font, Font) ? null : box.Font;
            box.Dispose();
            zoomedFont?.Dispose();
        }

        /// <summary>
        /// 就地編輯的狀態：做哪一種編輯、編輯框蓋在哪個方塊上、用哪個編輯框。
        /// </summary>
        private sealed class EditSession
        {
            public EditSession(CascadeEditAction action, MenuNode node, TreeDrawer.Placeholder placeholder, TextBox box)
            {
                Action = action;
                Node = node;
                Placeholder = placeholder;
                Box = box;
            }

            /// <summary><see cref="CascadeEditAction.Rename"/>、<see cref="CascadeEditAction.Insert"/> 或 <see cref="CascadeEditAction.CreateChild"/>。</summary>
            public CascadeEditAction Action { get; }

            /// <summary>編輯框蓋住的方塊：改名的節點，或 <see cref="Placeholder"/> 的節點。</summary>
            public MenuNode Node { get; }

            /// <summary>插入、建立子節點時新節點的位置；改名時為 null。</summary>
            public TreeDrawer.Placeholder Placeholder { get; }

            /// <summary>輸入名稱的 TextBox，控制項的子控制項。</summary>
            public TextBox Box { get; }

            /// <summary>正在跳出提示；這時編輯框失去焦點不算結束編輯。</summary>
            public bool Prompting { get; set; }
        }

        #endregion 就地編輯
    }
}
