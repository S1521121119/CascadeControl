using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControl.Views
{
    /// <summary>
    /// 檢視控制項編輯時跳出的對話框；兩個檢視共用，文字才不會各自走樣。
    /// </summary>
    internal static class CascadeEditPrompts
    {
        /// <summary>
        /// 刪除前向使用者確認；預設按鈕是「是」。
        /// </summary>
        /// <returns>使用者按了「是」時為 true。</returns>
        internal static bool ConfirmDelete(IWin32Window owner, IReadOnlyList<MenuNode> nodes)
        {
            var message = nodes.Count == 1
                ? $"是否要刪除「{nodes[0].Name}」？\r\n\r\nPath:\r\n  {nodes[0].FullPathText}"
                : $"是否要刪除已選取的 {nodes.Count} 個節點？";
            if (nodes.Any(node => !node.IsLeaf)) message += Environment.NewLine + "底下的子節點也會一起刪除。";

            return MessageBox.Show(owner, message, "刪除", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes;
        }

        /// <summary>
        /// 名稱含有保留字元 <see cref="CellExpansion.NAME_SEPARATOR"/> 時為 true；這種名稱不能套用。
        /// </summary>
        internal static bool HasReservedCharacter(string name) => name.IndexOf(CellExpansion.NAME_SEPARATOR) >= 0;

        /// <summary>
        /// 告訴使用者名稱不可含有保留字元；標題是 <paramref name="action"/> 的名稱。
        /// </summary>
        internal static void WarnReservedCharacter(IWin32Window owner, CascadeEditAction action)
        {
            MessageBox.Show(owner, $"節點名稱不可含有 '{CellExpansion.NAME_SEPARATOR}' 字元。", CaptionOf(action), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
    }
}