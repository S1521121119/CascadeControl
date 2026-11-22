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
    /// 涵蓋共享狀態、資料載入、選取查詢、導覽操作、節點點擊事件與編輯指令。
    /// 把同一份 <see cref="State"/> 指派給多個檢視，它們的樹、選取與復原歷史就會同步，在任一個檢視上的編輯，其他檢視都會跟著重畫。
    /// 編輯指令放在檢視上，因為對象取決於該檢視的多選，插入與改名也要用到該檢視的就地編輯框；工具列按鈕可以直接呼叫目前有焦點的那個檢視。
    /// 復原歷史與編輯事件在 <see cref="State"/> 上（<see cref="CascadeState.History"/>、<see cref="CascadeState.TreeEdited"/>），不在這裡，
    /// 表單訂閱一次就夠，不會因為綁了兩個檢視而收到兩次通知。各檢視自己的呈現設定也不在這裡。
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

        /// <summary>
        /// 這個檢視是否唯讀。唯讀時只能複製，改到樹的指令與步驟都不能執行、復原或重做。
        /// </summary>
        /// <remarks>每個檢視各自決定唯讀，和綁同一份 <see cref="State"/> 的其他檢視無關。改成 true 時，進行中的就地編輯會取消。</remarks>
        bool ReadOnly { get; set; }

        /// <summary>
        /// 是否正在就地編輯節點名稱（<see cref="BeginInsert"/>、<see cref="BeginCreateChild"/>、<see cref="BeginRename"/> 開啟的編輯框）。
        /// </summary>
        bool IsEditing { get; }

        /// <summary>
        /// 現在能不能復原：<see cref="CascadeState.History"/> 有步驟，而且那一步改到樹時這個檢視不是唯讀。
        /// </summary>
        bool CanUndo { get; }

        /// <summary>
        /// 現在能不能重做：<see cref="CascadeState.History"/> 有被復原的步驟，而且那一步改到樹時這個檢視不是唯讀。
        /// </summary>
        bool CanRedo { get; }

        /// <summary>
        /// 把編輯對象（多選的節點，沒有多選時是 <see cref="SelectedNode"/>）連同子孫複製到 Windows 剪貼簿。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        bool Copy();

        /// <summary>
        /// 把編輯對象從第一層到它的路徑，以 CSV 文字（含表頭）複製到 Windows 剪貼簿，不含子孫。唯讀時也可以用。
        /// </summary>
        /// <returns>有複製到東西時為 true；沒有對象或剪貼簿被其他程式佔用時為 false。</returns>
        bool CopyPath();

        /// <summary>
        /// 把編輯對象複製到 Windows 剪貼簿，然後從樹中移除。
        /// </summary>
        /// <returns>有剪下時為 true；唯讀、沒有對象或剪貼簿被佔用（這時不會移除）時為 false。</returns>
        bool Cut();

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼到 <see cref="SelectedNode"/> 的後面或前面；沒有選取時加在第一層的最後。
        /// </summary>
        /// <param name="before">true 貼在前面，false 貼在後面。</param>
        /// <returns>有貼上時為 true。</returns>
        bool Paste(bool before);

        /// <summary>
        /// 把 Windows 剪貼簿上的節點貼成 <see cref="SelectedNode"/> 的子節點；只有 <see cref="SelectedNode"/> 是葉節點時才有作用。
        /// </summary>
        /// <returns>有貼上時為 true。</returns>
        bool PasteAsChild();

        /// <summary>
        /// 刪除編輯對象及其所有子孫；刪除前一律跳出確認。
        /// </summary>
        /// <returns>有刪除時為 true；唯讀、沒有對象或使用者取消時為 false。</returns>
        bool Delete();

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 的後面或前面開啟空白的編輯框，按 Enter 以輸入的名稱建立新節點，按 Esc 取消。
        /// </summary>
        /// <param name="before">true 插在前面，false 插在後面。</param>
        /// <returns>有開啟編輯框時為 true。</returns>
        bool BeginInsert(bool before);

        /// <summary>
        /// 在葉節點 <see cref="SelectedNode"/> 底下開啟空白的編輯框，按 Enter 建立它的第一個子節點，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true；選取的節點已經有子節點時為 false。</returns>
        bool BeginCreateChild();

        /// <summary>
        /// 在 <see cref="SelectedNode"/> 上開啟編輯框，文字是目前的名稱；按 Enter 改名，按 Esc 取消。
        /// </summary>
        /// <returns>有開啟編輯框時為 true。</returns>
        bool BeginRename();

        /// <summary>
        /// 結束進行中的就地編輯；沒有在編輯時不做任何事。
        /// </summary>
        /// <param name="commit">true 套用輸入的名稱，false 取消。</param>
        void EndEdit(bool commit);

        /// <summary>
        /// 復原 <see cref="CascadeState.History"/> 最近的一個步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有復原時為 true。</returns>
        bool Undo();

        /// <summary>
        /// 重做最近一次被復原的步驟；進行中的就地編輯會先套用。
        /// </summary>
        /// <returns>有重做時為 true。</returns>
        bool Redo();
    }
}