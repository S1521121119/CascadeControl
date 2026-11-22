using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Columns
{
    // 編輯：唯讀切換、就地編輯框、Windows 剪貼簿，以及插入／建立子節點／改名／複製／剪下／貼上／刪除。
    public partial class CascadeColumnsView
    {
        private static readonly IReadOnlyList<MenuNode> NoMenuNodes = new MenuNode[0];

        private bool _readOnly = true;

        // 正在進行的就地編輯；沒有在編輯時為 null。
        private EditSession _edit;

        // Create Child 編輯期間，這個節點即使是葉節點也要多長出一欄放編輯框。
        private MenuNode _pendingChildParent;

        // 編輯中點了欄位：那一下只用來結束編輯，對應的放開也要略過。
        private bool _swallowMouseUp;

        #region 屬性

        /// <summary>
        /// 是否唯讀，預設為 true。
        /// </summary>
        /// <remarks>
        /// true：只能點選、多選與複製（右鍵功能表只有「複製」、「複製(路徑)」與欄位空白處的「全選」，快捷鍵只有 Ctrl+C、Ctrl+Shift+C、Ctrl+A），改到樹的步驟也不能復原或重做。
        /// false：右鍵功能表與快捷鍵可以插入、建立子節點、改名、剪下、貼上、刪除，以及復原、重做；樹是空的時也會留一個空欄，讓使用者在空白處插入第一個節點。
        /// 編輯只對 <see cref="SetTree"/> 顯示的 <see cref="MenuTree"/> 有作用；以 <see cref="SetTopLevelNodes"/> 顯示的資料一律唯讀。
        /// 改成 true 時，進行中的就地編輯會直接取消。
        /// </remarks>
        [Category("Cascade")]
        [Description("true：只能點選、多選與複製；false：右鍵功能表與快捷鍵可以插入、建立子節點、改名、剪下、貼上、刪除。只對 SetTree 顯示的樹有作用。")]
        [DefaultValue(true)]
        public bool ReadOnly
        {
            get => _readOnly;
            set
            {
                if (_readOnly == value) return;

                _readOnly = value;
                SyncColumns();
            }
        }

        /// <summary>
        /// 現在能不能復原：<see cref="State"/> 的 <see cref="CascadeState.History"/> 有步驟，而且那一步改到樹時控制項不是唯讀（<see cref="ReadOnly"/> 為 false）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanUndo => State.History.CanUndo && (CanEdit || !State.History.UndoChangesTree);

        /// <summary>
        /// 現在能不能重做：<see cref="State"/> 的 <see cref="CascadeState.History"/> 有被復原的步驟，而且那一步改到樹時控制項不是唯讀。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CanRedo => State.History.CanRedo && (CanEdit || !State.History.RedoChangesTree);

        /// <summary>
        /// 是否正在就地編輯節點名稱（插入、建立子節點、重新命名開啟的編輯框）。
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsEditing => _edit != null;

        #endregion 屬性

        #region 公開的編輯指令

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）連同子孫複製到 Windows 剪貼簿，順序與欄位中相同。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        public bool Copy()
        {
            CommitPendingEdit();
            return CascadeClipboard.CopyNodes(EditTargets());
        }

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）從第一層到它的路徑，以 CSV 文字（含表頭）複製到 Windows 剪貼簿，
        /// 每個節點一列，順序與欄位中相同；只到選取的那一層，不含子孫。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        /// <remarks>
        /// 例如路徑是 Motor &gt; Axis Y &gt; Jog，多選了 Forward、Backward，得到
        /// <c>Level1,Level2,Level3,Level4</c> / <c>Motor,Axis Y,Jog,Forward</c> / <c>Motor,Axis Y,Jog,Backward</c>。
        /// 只放純文字，不放 <see cref="CascadeClipboard.CLIPBOARD_FORMAT"/>：內容含上層路徑，不是要貼回控制項的節點。
        /// </remarks>
        public bool CopyPath()
        {
            CommitPendingEdit();
            return CascadeClipboard.CopyNodePaths(EditTargets());
        }

        /// <summary>
        /// 把多選的節點（沒有多選時是 <see cref="SelectedNode"/>）複製到 Windows 剪貼簿，然後從樹中移除。
        /// </summary>
        /// <returns>有剪下時為 true；唯讀、沒有對象或剪貼簿被佔用（這時不會移除）時為 false。</returns>
        public bool Cut()
        {
            CommitPendingEdit();
            return CutNodes(EditTargets());
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼到 <see cref="SelectedNode"/> 的後面或前面，順序與複製時相同；沒有選取時加在第一欄的最後。
        /// </summary>
        /// <param name="before">true 貼在前面，false 貼在後面。</param>
        /// <returns>有貼上時為 true。</returns>
        /// <remarks>同一層已有同名節點時，貼上的節點名稱自動加上後綴（例如 <c>Name - 2</c>）。貼完選取第一個貼上的節點，貼了多個時全部多選。</remarks>
        public bool Paste(bool before)
        {
            CommitPendingEdit();
            return SelectedNode is MenuNode node ? PasteBeside(node, before) : PasteAtEnd(0);
        }

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼成 <see cref="SelectedNode"/> 的子節點，順序與複製時相同；只有 <see cref="SelectedNode"/> 是葉節點時才有作用。
        /// </summary>
        /// <returns>有貼上時為 true；唯讀、沒有選取、選取的節點已經有子節點，或剪貼簿上沒有節點時為 false。</returns>
        /// <remarks>貼完選取第一個貼上的節點（原本的葉節點長出下一欄），貼了多個時全部多選。</remarks>
        public bool PasteAsChild()
        {
            CommitPendingEdit();
            return PasteIntoLeaf(SelectedNode as MenuNode);
        }

        /// <summary>
        /// 刪除多選的節點（沒有多選時是 <see cref="SelectedNode"/>）及其所有子孫；刪除前一律跳出確認。
        /// </summary>
        /// <returns>有刪除時為 true；唯讀、沒有對象或使用者取消時為 false。</returns>
        public bool Delete()
        {
            CommitPendingEdit();
            return DeleteNodes(EditTargets());
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 的後面或前面插入一列空白的編輯框，按 Enter 以輸入的名稱建立新節點，按 Esc 取消。
        /// </summary>
        /// <param name="before">true 插在前面，false 插在後面。</param>
        /// <returns>有開啟編輯框時為 true。</returns>
        public bool BeginInsert(bool before)
        {
            CommitPendingEdit();
            return BeginInsert(SelectedNode as MenuNode, before);
        }

        /// <summary>
        /// 在葉節點 <see cref="SelectedNode"/> 底下長出下一欄並開啟編輯框，按 Enter 建立它的第一個子節點，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true；選取的節點已經有子節點時為 false（請在子節點那一欄用 <see cref="BeginInsert(bool)"/>）。</returns>
        public bool BeginCreateChild()
        {
            CommitPendingEdit();
            return BeginCreateChild(SelectedNode as MenuNode);
        }

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 上開啟編輯框，文字是目前的名稱並全選；按 Enter 改名，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true。</returns>
        public bool BeginRename()
        {
            CommitPendingEdit();
            return BeginRename(SelectedNode as MenuNode);
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
        /// <remarks>
        /// 由 <see cref="CascadeState.Undo"/> 執行：選取跳到那一步的位置，改到樹時觸發 <see cref="CascadeState.TreeEdited"/>。
        /// 回來的節點不只一個時全部多選。
        /// </remarks>
        public bool Undo() => Replay(undo: true);

        /// <summary>
        /// 重做最近一次被復原的步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有重做時為 true；<see cref="CanRedo"/> 為 false，或樹已經和記錄時對不上時為 false。</returns>
        /// <remarks>由 <see cref="CascadeState.Redo"/> 執行，規則同 <see cref="Undo"/>。</remarks>
        public bool Redo() => Replay(undo: false);

        #endregion 公開的編輯指令

        #region 復原與重做

        private bool Replay(bool undo)
        {
            CommitPendingEdit();
            if (!(undo ? CanUndo : CanRedo)) return false;

            return KeepFocus(() => (undo ? State.Undo() : State.Redo()) != null);
        }

        #endregion 復原與重做

        #region 對象與樹

        private bool CanEdit => !_readOnly && Tree != null;

        // 多選優先，沒有多選時是目前選取的節點。
        private IReadOnlyList<MenuNode> EditTargets()
        {
            if (_marks.Count > 0) return MultiSelectedNodes.OfType<MenuNode>().ToList();

            return SelectedNode is MenuNode node ? new[] { node } : NoMenuNodes;
        }

        // 第 columnIndex 欄的節點是誰的子節點：第一欄是樹根，其他欄是路徑上前一欄的節點；那一欄不存在時為 null。
        private MenuNode ColumnParent(int columnIndex)
        {
            if (Tree == null || columnIndex < 0) return null;
            if (columnIndex == 0) return Tree.Root;

            var path = _state.PathWithTrail;
            return columnIndex - 1 < path.Count ? path[columnIndex - 1] as MenuNode : null;
        }

        // 和 ColumnNodes 相同，但欄位索引超出範圍時回傳空集合。
        private IReadOnlyList<ICascadeNode> SafeColumnNodes(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex > _state.PathWithTrail.Count) return NoNodes;
            return ColumnNodes(columnIndex);
        }

        private bool CanEditNodes(IReadOnlyList<MenuNode> nodes) => CanEdit && nodes != null && nodes.Count > 0 && nodes.All(_state.IsInTree);

        // 編輯會重建欄位，焦點原本在控制項裡時交回操作欄。
        private T KeepFocus<T>(Func<T> edit)
        {
            var hadFocus = ContainsFocus;
            var result = edit();
            if (hadFocus) FocusActiveColumn();
            return result;
        }

        // 任何一個綁同一份 State 的檢視編輯了樹：回到樹裡的節點不只一個時（貼上、復原刪除、重做貼上）全部多選。
        private void State_TreeEdited(object sender, CascadeTreeEditedEventArgs e)
        {
            var nodes = e.Nodes;
            if (nodes.Count > 1 && nodes.All(_state.IsInTree)) SetMarks(nodes[0].Depth - 1, nodes);
        }

        #endregion 對象與樹

        #region 剪下、貼上、刪除

        private bool CutNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes) || !CascadeClipboard.CopyNodes(nodes)) return false;

            return KeepFocus(() => State.CutNodes(nodes));
        }

        private bool DeleteNodes(IReadOnlyList<MenuNode> nodes)
        {
            if (!CanEditNodes(nodes)) return false;

            var hadFocus = ContainsFocus;
            var confirmed = CascadeEditPrompts.ConfirmDelete(this, nodes);
            if (hadFocus) FocusActiveColumn();
            if (!confirmed) return false;

            // 對話框開著的時候樹可能被換掉，再確認一次。
            if (!CanEditNodes(nodes)) return false;

            return KeepFocus(() => State.DeleteNodes(nodes));
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

        private bool PasteAtEnd(int columnIndex)
        {
            var parent = ColumnParent(columnIndex);
            return parent != null && PasteInto(parent, parent.Children.Count);
        }

        private bool PasteInto(MenuNode parent, int position)
        {
            if (!CanEdit || !_state.IsInTreeOrRoot(parent)) return false;

            var sources = CascadeClipboard.ReadNodes();
            if (sources.Count == 0) return false;

            return KeepFocus(() => State.PasteNodes(parent, position, sources).Count > 0);
        }

        #endregion 剪下、貼上、刪除

        #region 就地編輯

        private bool BeginInsert(MenuNode anchor, bool before)
        {
            if (!CanEditNodes(Single(anchor)) || !PrepareEdit(anchor)) return false;

            var position = anchor.SiblingNumber - 1 + (before ? 0 : 1);
            return OpenPlaceholderEditor(CascadeEditAction.Insert, anchor.Parent, position);
        }

        // 欄位空白處的 Insert：加在那一欄的最後。
        private bool BeginInsertAtEnd(int columnIndex)
        {
            var parent = ColumnParent(columnIndex);
            if (!CanEdit || !_state.IsInTreeOrRoot(parent)) return false;

            CommitPendingEdit();
            ClearMultiSelection();

            // 選取父節點，讓這一欄成為它的下一欄並留在畫面上；第一欄本來就在。
            if (!ReferenceEquals(parent, Tree.Root) && !TrySelectNode(parent)) return false;

            return OpenPlaceholderEditor(CascadeEditAction.Insert, parent, parent.Children.Count);
        }

        // 只對葉節點：先長出下一欄（_pendingChildParent），編輯框開在那一欄的第一列。
        private bool BeginCreateChild(MenuNode node)
        {
            if (!CanEditNodes(Single(node)) || !node.IsLeaf || !PrepareEdit(node)) return false;

            _pendingChildParent = node;
            SyncColumns();

            return OpenPlaceholderEditor(CascadeEditAction.CreateChild, node, node.Children.Count);
        }

        private bool BeginRename(MenuNode node)
        {
            if (!CanEditNodes(Single(node)) || !PrepareEdit(node)) return false;

            var columnIndex = node.Depth - 1;
            if (!IsColumnShown(columnIndex)) return false;

            _edit = new EditSession(CascadeEditAction.Rename, _lists[columnIndex], node, node.Parent, node.SiblingNumber - 1);
            OpenEditBox(node.Name);
            return true;
        }

        private static IReadOnlyList<MenuNode> Single(MenuNode node) => node == null ? NoMenuNodes : new[] { node };

        // 開始編輯之前：結束上一個編輯、取消多選，並選取對象，讓它所在的欄成為操作欄。
        private bool PrepareEdit(MenuNode node)
        {
            CommitPendingEdit();
            ClearMultiSelection();
            return TrySelectNode(node);
        }

        /// <summary>
        /// 在 <paramref name="parent"/> 的子節點那一欄的第 <paramref name="position"/> 列插入一列空白，並在上面開啟編輯框。
        /// </summary>
        private bool OpenPlaceholderEditor(CascadeEditAction action, MenuNode parent, int position)
        {
            var columnIndex = parent.Depth;
            if (columnIndex >= _lists.Count || !IsColumnShown(columnIndex))
            {
                _pendingChildParent = null;
                SyncColumns();
                return false;
            }

            var list = _lists[columnIndex];
            var placeholder = new EditPlaceholder();
            var row = Math.Max(0, Math.Min(position, list.Items.Count));

            // 插在選取項前面時 ListBox 會跟著調整選取，那不是使用者改選。
            _syncing = true;
            try
            {
                list.Items.Insert(row, placeholder);
            }
            finally
            {
                _syncing = false;
            }

            _edit = new EditSession(action, list, placeholder, parent, position);
            OpenEditBox(string.Empty);
            return true;
        }

        // 編輯框是欄位 ListBox 的子控制項，蓋在那一列上面；欄位的 WS_CLIPCHILDREN 讓 ListBox 重畫時不會蓋掉它。
        // 單行 TextBox 不能讓文字垂直置中，自動調整的高度又比列高高，字會偏上；
        // 所以外框是另一個子控制項（EditFrame），沒有框的 TextBox 只有一行字高，垂直置中放在外框裡。
        private void OpenEditBox(string text)
        {
            var session = _edit;
            var list = session.List;
            var row = list.Items.IndexOf(session.RowNode);
            EnsureRowVisible(list, row);

            var frame = new EditFrame { Name = "pnlEdit" };
            var box = new TextBox
            {
                Name = "txtEdit",
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.WindowText,
                Font = list.Font,
                Text = text,

                // 欄位為了讓 Space 當指令可能關掉了輸入法（CheckBoxes），編輯框不跟著繼承。
                ImeMode = ImeMode.NoControl,
            };

            session.Frame = frame;
            session.Box = box;
            frame.Controls.Add(box);
            list.Controls.Add(frame);
            PositionEditBox();

            box.LostFocus += OnEditBoxLostFocus;
            box.Focus();
            box.SelectAll();
        }

        // 讓編輯框對齊它那一列；欄位捲動、改變大小之後都要再對一次。
        private void PositionEditBox()
        {
            var session = _edit;
            if (session?.Frame == null || session.List.IsDisposed) return;

            var row = session.List.Items.IndexOf(session.RowNode);
            if (row < 0) return;

            // 外框至少和列一樣高；字比列高時外框跟著撐高（上下各多出一點蓋到鄰列），文字仍然在列的正中間。
            var bounds = session.List.GetItemRectangle(row);
            var textHeight = session.Box.Height;
            var frameHeight = Math.Max(bounds.Height, textHeight + (EDIT_FRAME_PADDING * 2));
            var frameTop = bounds.Top + ((bounds.Height - frameHeight) / 2);
            var frameWidth = Math.Max(EDIT_FRAME_PADDING * 3, bounds.Width);

            session.Frame.Bounds = new Rectangle(bounds.Left, frameTop, frameWidth, frameHeight);
            session.Box.Bounds = new Rectangle(
                EDIT_FRAME_PADDING,
                (frameHeight - textHeight) / 2,
                frameWidth - (EDIT_FRAME_PADDING * 2),
                textHeight);
        }

        // 外框線 1 像素，再留 1 像素空白。
        private const int EDIT_FRAME_PADDING = 2;

        /// <summary>
        /// 就地編輯框的外框：白底、1 像素的選取色框線，裡面放沒有框的 TextBox。
        /// </summary>
        private sealed class EditFrame : Control
        {
            public EditFrame()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = SystemColors.Window;
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.Highlight, ButtonBorderStyle.Solid);
            }
        }

        private static void EnsureRowVisible(ListBox list, int row)
        {
            if (row < 0) return;

            if (row < list.TopIndex)
            {
                list.TopIndex = row;
                return;
            }

            var visibleRows = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));
            if (row >= list.TopIndex + visibleRows) list.TopIndex = row - visibleRows + 1;
        }

        // Sliding 模式下範圍外的欄位是隱藏的，編輯框開在那裡使用者看不到。
        private bool IsColumnShown(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _lists.Count) return false;
            if (!IsSlidingMode) return true;

            var viewportColumns = ViewportColumnCount;
            var start = ViewportStartColumn(_lists.Count, ActiveColumnIndex, viewportColumns, ActiveColumnHasBranch());
            return columnIndex >= start && columnIndex < start + viewportColumns;
        }

        // 焦點離開編輯框（Tab、點了表單上的其他控制項、切到別的程式）就套用；等這一輪訊息處理完再做，不在焦點切換的當下改欄位。
        private void OnEditBoxLostFocus(object sender, EventArgs e)
        {
            var session = _edit;
            if (session == null || session.Prompting || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (_edit == session && !session.Prompting && !session.Box.ContainsFocus) EndEdit(commit: true, interactive: false);
            }));
        }

        /// <summary>
        /// 編輯中點了任何一欄：這一下只用來結束編輯（套用輸入的名稱），不選取、不開功能表。
        /// </summary>
        /// <remarks>由欄位的 WndProc 呼叫，所以延到這個訊息處理完才做：結束編輯會重建欄位，可能拆掉正在處理訊息的那一欄。</remarks>
        private void RequestCommitEdit()
        {
            var session = _edit;
            _swallowMouseUp = true;

            BeginInvoke(new Action(() =>
            {
                if (_edit == session) EndEdit(commit: true, interactive: false);
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
        /// 是否由使用者按 Enter 觸發：名稱含有保留字元時跳出提示並繼續編輯；
        /// 其他情況（失去焦點、點了別的地方）直接當成取消，不跳對話框。
        /// </param>
        /// <returns>編輯已經結束時為 true；跳出提示、繼續編輯時為 false。</returns>
        private bool EndEdit(bool commit, bool interactive)
        {
            var session = _edit;
            if (session == null) return true;

            var name = session.Box.Text.Trim();

            if (commit && name.IndexOf(MultiNameCellSyntax.NAME_SEPARATOR) >= 0)
            {
                if (interactive)
                {
                    session.Prompting = true;
                    try
                    {
                        MessageBox.Show(this, $"節點名稱不可含有 '{MultiNameCellSyntax.NAME_SEPARATOR}' 字元。", CaptionOf(session.Action),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        session.Prompting = false;
                    }

                    if (_edit == session && !session.Box.IsDisposed)
                    {
                        session.Box.Focus();
                        session.Box.SelectAll();
                    }

                    return false;
                }

                commit = false;
            }

            var hadFocus = session.Box.ContainsFocus;
            CloseEditBox(session, hadFocus);

            // 編輯成功時 State 會 Refresh 重建欄位，多長出來的那一欄要先收掉。
            _pendingChildParent = null;
            var result = commit && name.Length > 0 ? ApplyEdit(session, name) : null;
            if (result == null)
            {
                RemovePlaceholder(session);
                SyncColumns();
            }

            if (hadFocus) FocusActiveColumn();
            return true;
        }

        // 交給 State 套用輸入的名稱；對象已經不在樹裡，或改名成原本的名稱時回傳 null。
        private MenuNode ApplyEdit(EditSession session, string name)
        {
            switch (session.Action)
            {
                case CascadeEditAction.Insert: return State.InsertNode(session.Parent, session.Position, name);
                case CascadeEditAction.CreateChild: return State.CreateChild(session.Parent, name);
                case CascadeEditAction.Rename: return State.RenameNode((MenuNode)session.RowNode, name);
                default: return null;
            }
        }

        private static string CaptionOf(CascadeEditAction action)
        {
            switch (action)
            {
                case CascadeEditAction.Insert: return "插入";
                case CascadeEditAction.CreateChild: return "建立子節點";
                case CascadeEditAction.Rename: return "重新命名";
                default: return action.ToString();
            }
        }

        // 拿掉編輯框。編輯框原本有焦點時先把焦點交給它所在的欄，焦點才不會因為編輯框消失而掉出控制項。
        private void CloseEditBox(EditSession session, bool keepFocus)
        {
            _edit = null;

            var box = session.Box;
            box.LostFocus -= OnEditBoxLostFocus;

            if (keepFocus && !session.List.IsDisposed && session.List.CanFocus) session.List.Focus();

            if (!session.List.IsDisposed) session.List.Controls.Remove(session.Frame);

            // 編輯框是外框的子控制項，一起釋放。
            session.Frame.Dispose();
        }

        private void RemovePlaceholder(EditSession session)
        {
            if (!(session.RowNode is EditPlaceholder) || session.List.IsDisposed) return;

            _syncing = true;
            try
            {
                session.List.Items.Remove(session.RowNode);
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// 不套用、不重建欄位，直接丟掉進行中的編輯；欄位要重建、換樹或改成唯讀時呼叫。
        /// </summary>
        private void AbortEdit()
        {
            var session = _edit;
            if (session == null) return;

            CloseEditBox(session, session.Box.ContainsFocus);
            RemovePlaceholder(session);
            _pendingChildParent = null;
        }

        /// <summary>
        /// 就地編輯的狀態：編輯框蓋在哪一欄的哪一列，以及按 Enter 時要套用到哪裡。
        /// </summary>
        private sealed class EditSession
        {
            public EditSession(CascadeEditAction action, ListBox list, ICascadeNode rowNode, MenuNode parent, int position)
            {
                Action = action;
                List = list;
                RowNode = rowNode;
                Parent = parent;
                Position = position;
            }

            public CascadeEditAction Action { get; }

            public ListBox List { get; }

            /// <summary>編輯框蓋住的那一列：插入與建立子節點是暫時的空白列，改名是被改名的節點。</summary>
            public ICascadeNode RowNode { get; }

            /// <summary>新節點的父節點；改名時是被改名節點的父節點。</summary>
            public MenuNode Parent { get; }

            /// <summary>新節點在 <see cref="Parent"/> 底下的位置。</summary>
            public int Position { get; }

            /// <summary>編輯框的外框，欄位 ListBox 的子控制項。</summary>
            public Control Frame { get; set; }

            /// <summary>輸入名稱的 TextBox，<see cref="Frame"/> 的子控制項。</summary>
            public TextBox Box { get; set; }

            /// <summary>正在跳出提示；這時編輯框失去焦點不算結束編輯。</summary>
            public bool Prompting { get; set; }
        }

        /// <summary>
        /// 插入與建立子節點時暫時放在欄位裡的空白列，編輯框就蓋在它上面；不屬於任何樹。
        /// </summary>
        private sealed class EditPlaceholder : ICascadeNode
        {
            public string Name => string.Empty;

            public string DisplayText => string.Empty;

            public IReadOnlyList<ICascadeNode> Children => NoNodes;
        }

        #endregion 就地編輯

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachState(_state);
                _edit = null;
                NodeContextMenu.Dispose();
                ColumnContextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}