using CascadeControl.Common;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;

namespace CascadeControl.Views
{
    /// <summary>
    /// 持有選單樹與導覽選取狀態的檢視介面。
    /// </summary>
    /// <remarks>
    /// 只涵蓋資料載入、選取查詢與導覽操作，不包含編輯、復原歷史與欄位排版設定。
    /// 這個檔案不相依 System.Windows.Forms，測試或非 UI 配接器實作這個介面時不需要建立控制項。
    /// </remarks>
    public interface ICascadeView
    {
        /// <summary>
        /// 目前顯示的選單樹；尚未設定或已清空時為 null。
        /// </summary>
        MenuTree Tree { get; }

        /// <summary>
        /// 目前選取的最深節點；未選取時為 null。
        /// </summary>
        ICascadeNode SelectedNode { get; }

        /// <summary>
        /// 由淺至深的選取路徑；未選取時為空集合。
        /// </summary>
        IReadOnlyList<ICascadeNode> SelectedPath { get; }

        /// <summary>
        /// 目前選取路徑上各層節點名稱以 <see cref="PathSeparator"/> 串接的字串；未選取時為空字串。
        /// </summary>
        string SelectedPathText { get; }

        /// <summary>
        /// 目前的選取是否停在葉節點。
        /// </summary>
        bool IsLeafSelected { get; }

        /// <summary>
        /// <see cref="SelectedPathText"/> 使用的層級分隔字串。
        /// </summary>
        string PathSeparator { get; set; }

        /// <summary>
        /// 設定要顯示的選單樹；傳 null 表示清空。
        /// </summary>
        /// <param name="tree">要顯示的選單樹；傳 null 表示清空。</param>
        void SetTree(MenuTree tree);

        /// <summary>
        /// 清除目前顯示的資料與選取。
        /// </summary>
        void Clear();

        /// <summary>
        /// 選取指定的 <see cref="MenuNode"/>。
        /// </summary>
        /// <param name="node">要選取的節點。</param>
        /// <returns>成功選取時為 true。</returns>
        bool TrySelectNode(MenuNode node);

        /// <summary>
        /// 從第一層開始依序選取 <paramref name="path"/> 中的節點。
        /// </summary>
        /// <param name="path">由淺至深的節點序列。</param>
        /// <returns>整條路徑都選到時為 true。</returns>
        bool TrySelectPath(IEnumerable<ICascadeNode> path);

        /// <summary>
        /// 往上一層退回一步。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        bool StepBack();

        /// <summary>
        /// 往下一層前進一步。
        /// </summary>
        /// <returns>選取有改變時為 true。</returns>
        bool StepForward();

        /// <summary>
        /// 選取路徑改變後觸發。
        /// </summary>
        event EventHandler<CascadeNodeEventArgs> SelectedPathChanged;

        /// <summary>
        /// 使用者在選取的節點上執行啟動操作（雙擊或按 Enter）時觸發。
        /// </summary>
        event EventHandler<CascadeNodeEventArgs> NodeActivated;
    }
}
