using CascadeControl.Common;
using CascadeControl.State;
using CascadeControl.Trees;
using System;
using System.Collections.Generic;

namespace CascadeControl.Views
{
    /// <summary>
    /// 持有選單樹與導覽選取狀態的檢視介面。
    /// </summary>
    /// <remarks>
    /// 涵蓋共享狀態、資料載入、選取查詢、導覽操作與節點點擊事件，不包含編輯、復原歷史與各檢視自己的呈現設定。
    /// 把同一份 <see cref="State"/> 指派給多個檢視，它們的樹與選取就會同步。
    /// 這個檔案不直接使用 System.Windows.Forms；<see cref="CascadeNodeMouseEventArgs.Button"/> 的型別是 MouseButtons，
    /// 所以實作端要參考 System.Windows.Forms 組件，但不需要建立控制項。
    /// </remarks>
    public interface ICascadeView
    {
        /// <summary>
        /// 目前綁定的共享狀態：樹、選取路徑、記憶軌跡與 <see cref="PathSeparator"/> 都存在這裡。不可設為 null。
        /// </summary>
        /// <exception cref="ArgumentNullException">設為 null。</exception>
        CascadeState State { get; set; }

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

        /// <summary>
        /// 使用者用滑鼠點了某個節點之後觸發。哪些按鍵與位置會觸發、選取會不會跟著改變，由各檢視決定。
        /// </summary>
        event EventHandler<CascadeNodeMouseEventArgs> NodeMouseClick;
    }
}