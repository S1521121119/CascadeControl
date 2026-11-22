using CascadeControl.State;
using CascadeControl.Trees;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views.Diagram
{
    // 滑鼠與鍵盤操作。
    public partial class CascadeDiagramView
    {
        #region 滑鼠

        // 左鍵點在展開鈕上就切換展開狀態；回傳 true 表示點到了展開鈕。
        private bool TryToggleExpander(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;

            var node = GetExpanderAt(e.Location);
            if (node == null) return false;

            SetExpanded(node, !IsExpanded(node));
            return true;
        }

        /// <inheritdoc />
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = GetNodeAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        /// <inheritdoc />
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 要有焦點，滾輪才捲得動。
            Focus();
            base.OnMouseDown(e);
        }

        /// <inheritdoc />
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseClick(CreateMouseEventArgs(node, e));
        }

        /// <inheritdoc />
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            // 連點兩下時第二下只會送 DoubleClick，當成第二次點擊再切換一次。
            if (TryToggleExpander(e)) return;

            var node = GetNodeAt(e.Location);
            if (node != null) OnNodeMouseDoubleClick(CreateMouseEventArgs(node, e));
        }

        private CascadeNodeMouseEventArgs CreateMouseEventArgs(MenuNode node, MouseEventArgs e)
        {
            var bounds = GetClientBoundsOf(node);
            return new CascadeNodeMouseEventArgs(node.FullPath, PathSeparator, e.Button, e.Location, bounds, PointToScreen(e.Location));
        }

        #endregion 滑鼠

        #region 鍵盤

        /// <inheritdoc />
        /// <remarks>方向鍵要自己處理，不讓表單拿去切換焦點；Enter 也是，不讓表單的 AcceptButton 搶走。</remarks>
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Enter:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        /// <summary>
        /// ↑↓ 在圖上看得到的同一層節點之間移動（可以跨父節點）；← 退回上一層、→ 進入下一層（記憶與 <see cref="State"/> 共用）；
        /// 空白鍵展開或收合選取的節點；Enter 在選取的節點上觸發 <see cref="NodeActivated"/>。改的是 <see cref="State"/> 的選取，所以共享狀態的檢視一起動。
        /// </summary>
        /// <param name="e">按鍵資料；有 Ctrl、Alt 或 Shift 時不處理。</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Modifiers != Keys.None) return;

            switch (e.KeyCode)
            {
                case Keys.Up: MoveVertical(-1); break;
                case Keys.Down: MoveVertical(1); break;
                case Keys.Left: _state.StepBack(); break;
                case Keys.Right: StepIn(); break;
                case Keys.Space: ToggleSelected(); break;
                case Keys.Enter: ActivateSelected(); break;
                default: return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            EnsureVisible(SelectedMenuNode);
        }

        private MenuNode SelectedMenuNode => _state.SelectedNode as MenuNode;

        /// <summary>
        /// 選同一層（同一欄）圖上看得到的上一個或下一個節點；到頭不動。未選取或選取的節點不在圖上時，選圖上第一個節點。
        /// </summary>
        /// <remarks><see cref="TreeImage.Nodes"/> 是前序排列，同一層的節點剛好由上往下。</remarks>
        private void MoveVertical(int direction)
        {
            if (_image == null || _image.Nodes.Count == 0) return;

            var current = FindImageNode(SelectedMenuNode);
            if (current == null)
            {
                _state.TrySelectNode(_image.Nodes[0].Node);
                return;
            }

            var column = _image.Nodes.Where(imageNode => imageNode.Node.Depth == current.Node.Depth).ToList();
            var index = column.IndexOf(current) + direction;
            if (index >= 0 && index < column.Count) _state.TrySelectNode(column[index].Node);
        }

        /// <summary>
        /// 進入下一層，照 <see cref="StepForward"/> 的規則走（有記憶走記憶，否則第一個子節點）；
        /// 選取的節點收合（[+]）時不動作，要先按空白鍵展開。
        /// </summary>
        /// <remarks>
        /// 隱藏未勾選的節點時，記憶或第一個子節點可能沒畫出來，改選圖上第一個子節點；子節點全被隱藏時不動作。
        /// </remarks>
        private void StepIn()
        {
            var node = SelectedMenuNode;
            if (node != null && !node.Children.Any(child => FindImageNode(child) != null)) return;

            if (!_state.StepForward() || node == null) return;
            if (FindImageNode(SelectedMenuNode) != null) return;

            var firstDrawn = node.Children.FirstOrDefault(child => FindImageNode(child) != null);
            if (firstDrawn != null) _state.TrySelectNode(firstDrawn);
        }

        // 和雙擊一樣只針對畫在圖上的節點；選取被收合藏起來或沒有選取時不觸發。
        private void ActivateSelected()
        {
            var node = SelectedMenuNode;
            if (FindImageNode(node) != null) OnNodeActivated(new CascadeNodeEventArgs(node.FullPath, null, PathSeparator));
        }

        private void ToggleSelected()
        {
            var node = SelectedMenuNode;
            if (node != null && node.Children.Count > 0) SetExpanded(node, !IsExpanded(node));
        }

        #endregion 鍵盤
    }
}