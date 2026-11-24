using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControl.Views.Diagram
{
    // 展開收合：使用者點過的節點照那次的設定，其他的依勾選決定。
    public partial class CascadeDiagramView
    {
        #region 展開收合

        // 使用者點過展開鈕的節點；沒有的依勾選決定。屬於 _expandTree，換樹時清空。
        private readonly Dictionary<MenuNode, bool> _expandOverrides = new Dictionary<MenuNode, bool>();

        private MenuTree _expandTree;

        /// <summary>
        /// 有子節點的節點是否展開。
        /// </summary>
        /// <param name="node">要查詢的節點。</param>
        /// <returns>
        /// 使用者點過展開鈕（或呼叫過 <see cref="SetExpanded"/>）的節點照那次的設定；
        /// 其他的節點有任何子節點被勾選時為 true，沒有勾選資訊時為 false。null 回傳 false。
        /// </returns>
        public bool IsExpanded(MenuNode node)
        {
            if (node == null) return false;

            SyncExpandTree();
            if (_expandOverrides.TryGetValue(node, out var expanded)) return expanded;

            var checkSet = DisplayedCheckSet;
            return checkSet != null && node.Children.Any(checkSet.IsChecked);
        }

        /// <summary>
        /// 展開或收合節點並重畫；之後不再依勾選自動決定，直到換成另一棵樹。
        /// </summary>
        /// <param name="node">要展開或收合的節點。</param>
        /// <param name="expanded">true 展開、false 收合。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public void SetExpanded(MenuNode node, bool expanded)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            SyncExpandTree();
            _expandOverrides[node] = expanded;
            Redraw();
        }

        /// <summary>
        /// 展開 <paramref name="node"/> 與它底下的所有子孫節點並重畫；葉節點不做任何事。
        /// </summary>
        /// <param name="node">要全部展開的節點。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public void ExpandAll(MenuNode node) => SetSubtreeExpanded(node, expanded: true);

        /// <summary>
        /// 收合 <paramref name="node"/> 與它底下的所有子孫節點並重畫；葉節點不做任何事。
        /// </summary>
        /// <param name="node">要全部摺疊的節點。</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> 為 null。</exception>
        public void CollapseAll(MenuNode node) => SetSubtreeExpanded(node, expanded: false);

        private void SetSubtreeExpanded(MenuNode root, bool expanded)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            SyncExpandTree();
            if (root.Children.Count == 0) return;

            var stack = new Stack<MenuNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current.Children.Count == 0) continue;

                _expandOverrides[current] = expanded;
                for (var i = 0; i < current.Children.Count; i++) stack.Push(current.Children[i]);
            }

            Redraw();
        }

        // 展開選取節點的祖先，讓選取的節點畫得出來；之後選取移開也維持展開，和使用者點過展開鈕一樣。
        private void RevealSelection()
        {
            SyncExpandTree();
            for (var node = (_state.SelectedNode as MenuNode)?.Parent; node != null; node = node.Parent) _expandOverrides[node] = true;
        }

        // State 換成另一棵樹（或沒有樹）時，忘掉使用者點過的展開狀態。
        private void SyncExpandTree()
        {
            var tree = _state.Tree;
            if (ReferenceEquals(tree, _expandTree)) return;

            _expandOverrides.Clear();
            _expandTree = tree;
        }

        #endregion 展開收合
    }
}