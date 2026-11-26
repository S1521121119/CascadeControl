# MindMap 滑鼠與鍵盤 UI 功能對照表

這份參考文件列出 `MindMap` 所有滑鼠與鍵盤 UI 操作，以及每項功能實作所在的 `.cs` 檔案。文件範圍只看 UI 互動，不涉及底層資料結構。

`MindMap` 主視窗為 `MindMap/MainForm.cs`，畫布控制項為繼承自 `CascadeDiagramView` 的 `MindMap/MindMapDiagramView.cs`（在表單設計檔中設定為 `ReadOnly = false`、`AutoScroll = true`）。

## 滑鼠操作

| 操作方式 | UI 行為 | 所在 `.cs` 檔案 |
| :--- | :--- | :--- |
| 滑鼠移到節點上（Hover） | 游標切換為手形（`Cursors.Hand`），移離節點恢復預設游標（`Cursors.Default`）。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseMove`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`（`GetNodeAt`）、`CascadeControl/Views/Diagram/TreeImage.cs`（`GetNodeAt`） |
| 左鍵點擊節點 | 取得焦點、清除原本多選，並選取該節點（畫紅框）。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseDown`、`OnMouseClick`）、`MindMap/MindMapDiagramView.cs`（`OnNodeMouseClick`） |
| 左鍵點擊或雙擊 `[+]`、`[-]` 展開鈕 | 展開或收合該節點的子節點，不改變選取。連點兩下時，第二下視為再切換一次。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryToggleExpander`、`OnMouseClick`、`OnMouseDoubleClick`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Expansion.cs`（`SetExpanded`、`IsExpanded`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`（`GetExpanderAt`）、`CascadeControl/Views/Diagram/TreeImage.cs`（`GetExpanderAt`） |
| 左鍵點擊空白處 | 取得焦點並取消全部多選。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseDown`、`OnMouseUp`）、`CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`BeginMarkPress`、`EndMarkPress`、`ClearMultiSelection`） |
| `Ctrl` + 左鍵點擊節點（未拖曳即放開） | 切換該節點的多選狀態，不改單選焦點。取消多選時連同已多選子孫一起取消；原本無多選時，會先將目前單選節點納入起點。 | `CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`BeginMarkPress`、`ToggleMark`、`SeedMarks`）、`CascadeControl/Views/Diagram/CascadeDiagramView.DragMove.cs`（`TryBeginMovePress`、`EndMovePress`） |
| 左鍵從空白處拖曳框選 | 超過系統拖曳門檻（`SystemInformation.DragSize`）後畫出半透明藍框，框住的節點取代目前多選。拖到視窗邊緣外時自動捲動（原始尺寸模式）。失去滑鼠擷取時停在目前選取範圍。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseDown`、`OnMouseMove`、`OnMouseUp`、`OnMouseCaptureChanged`）、`CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`BeginMarkPress`、`DragMarkPress`、`EndMarkPress`、`ScrollTowards`、`PaintMarks`） |
| `Ctrl` + 左鍵從空白處拖曳框選 | 在原本多選（或目前單選節點）的基礎上疊加框選到的節點。 | `CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`SeedMarks`、`BeginMarkPress`、`DragMarkPress`） |
| 左鍵按住節點拖曳（搬移） | 搬移單一節點或整個多選群組。移到目標節點上下 20% 邊緣或兄弟間隙、首尾 50% 高度內時插入同層指定位置；移到目標節點中央時放成最後一個子節點。拖曳途中顯示半透明預覽、原處淡化並畫虛線與紅叉、目標高亮相框、不可放處顯示禁止游標（`Cursors.No`）、視窗邊緣自動捲動。失去滑鼠擷取時取消。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseMove`、`OnMouseUp`、`OnMouseCaptureChanged`）、`CascadeControl/Views/Diagram/CascadeDiagramView.DragMove.cs`（`TryBeginMovePress`、`DragMovePress`、`UpdateMoveDropTarget`、`BuildNodeHitDestination`、`TryFindSiblingGapDestination`、`PaintDropTarget`、`SetDropDestination`、`EndMovePress`）、`CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`BeginMarkPress`、`ScrollTowards`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`（`OnPaint`） |
| `Ctrl` + 左鍵按住節點拖曳（複製） | 落點與預覽規則同拖曳搬移，但改為複製到目標位置且原處維持不變。拖曳過程中按下或放開 `Ctrl` 鍵可即時切換搬移與複製預覽。 | `CascadeControl/Views/Diagram/CascadeDiagramView.DragMove.cs`（`UpdateMoveDropTarget`、`EndMovePress`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnKeyDown`、`OnKeyUp`） |
| `Shift` + 左鍵拖曳（畫布任何處） | 自由平移畫布（手形抓紙方向，游標切換為 `Cursors.SizeAll`），即使圖面比視窗小或已達捲軸盡頭也能把任意節點移到畫面中央（至少保留 `48px` 圖面可見）。不觸發點選、展開鈕切換、框選或節點搬移；拖曳途中按 `Esc` 還原至拖曳前位置；自動縮放（`Zoom`）模式下不啟用。 | `MindMap/MindMapDiagramView.cs`（`OnMouseDown`、`OnMouseMove`、`OnMouseClick`、`OnMouseDoubleClick`、`OnMouseUp`、`OnMouseCaptureChanged`、`ProcessCmdKey`、`SetScreenOffset`、`ResetPan`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`（`PanOffset`、`GetTransform`、`ShiftViewport`、`NotifyPanChanged`、`OnImageUpdated`） |
| 編輯中左鍵點擊畫布任何處 | 套用並關閉就地編輯框，同時略過該次點擊的選取與展開動作。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseDown`、`OnMouseClick`、`OnMouseUp`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`EndEdit`） |
| 右鍵點擊節點或空白處 | 開啟右鍵選單。點在節點上以該節點為對象，點在多選節點上以整個多選為對象，點在空白處以目前選取節點為對象。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnMouseDown`）、`CascadeControl/Views/Diagram/CascadeDiagramView.ContextMenu.cs`（`ContextMenu_Opening`、`ContextMenu_Closed`）、`CascadeControl/Views/Diagram/CascadeDiagramView.cs`（`_contextMenu` 建立與綁定） |
| 滾輪捲動 | 畫布上下捲動（需先取得焦點）。 | `MindMap/MindMapDiagramView.cs`（`OnMouseWheel`）、`CascadeControl/Views/Diagram/CascadeDiagramView.cs`（`AutoScroll`、`ControlStyles.Selectable`） |
| `Ctrl` + 滾輪 | 以游標所在點為中心分級縮放（25% 到 400% 共 15 階，支援觸控板細步累積），並更新狀態列縮放百分比。 | `MindMap/MindMapDiagramView.cs`（`OnMouseWheel`、`SetZoomIndex`、`ZoomSteps`）、`MindMap/MainForm.cs`（`UpdateZoomStatus`） |
| 從外部拖放 `.csv` 檔案到視窗 | 游標顯示複製圖示，放開後若有未存變更先跳確認框，再開啟第一個 `.csv`。表單與所有動態加入的子控制項（包含就地編輯框）皆接收拖放。 | `MindMap/MainForm.cs`（`EnableFileDrop`、`Form_DragEnter`、`Form_DragDrop`、`TryGetDroppedCsv`） |
| 點擊視窗關閉鈕 | 若有未存檔變更，跳出 Yes/No/Cancel 存檔確認對話框。 | `MindMap/MainForm.cs`（`OnFormClosing`、`ConfirmDiscardChanges`） |

## 鍵盤操作（非編輯模式）

| 按鍵 | UI 行為 | 所在 `.cs` 檔案 |
| :--- | :--- | :--- |
| `↑`、`↓` | 在畫面上可見的同一層節點間上下移動（可跨父節點，略過收合與隱藏節點；未選取時選第一個可見節點），並自動捲動至可見範圍。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`IsInputKey`、`OnKeyDown`、`MoveVertical`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`（`EnsureVisible`） |
| `←` | 退回上一層（選取父節點）。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`IsInputKey`、`OnKeyDown`） |
| `→` | 進入下一層子節點。若目前節點收合且 `StepIntoCollapsed` 為 `true`，先展開再進入。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`IsInputKey`、`OnKeyDown`、`StepIn`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Expansion.cs`（`SetExpanded`） |
| `Space` | 展開或收合目前選取的節點。`WndProc` 會攔截中文輸入法開啟時的 `VK_PROCESSKEY` 並依掃描碼 `0x39` 還原為 `Keys.Space`。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnKeyDown`、`WndProc`、`ToggleSelected`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Expansion.cs`（`SetExpanded`） |
| `*`（主鍵盤或數字鍵盤乘號） | 展開選取節點與其全部子孫。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnKeyDown`、`OnKeyPress`、`ExpandAllSelected`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Expansion.cs`（`ExpandAll`） |
| `0` 到 `9`（主鍵盤與數字鍵盤） | 僅在 **View > Show Index** 開啟時生效。依同層編號跳轉選取並清除多選，支援連續輸入多位數緩衝。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`OnKeyDown`、`TryGetDigit`、`JumpByDigit`、`ResetDigitJump`）、`CascadeControl/Views/Diagram/CascadeDiagramView.cs`（`ShowIndex`） |
| `Tab` | 在選取節點底下最後面開啟就地編輯框插入子項。葉節點直接建立第一個子節點；已有子節點則選最後一個子節點並在其後方插入。 | `MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`、`BeginInsertChild`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`BeginCreateChild`、`BeginInsert`） |
| `Enter` | 在選取節點後方（下方）開啟就地編輯框插入同層節點。 | `MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`BeginInsert`） |
| `Ctrl+A` | 多選目前畫在圖上的全部節點。 | `MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`SelectAll`） |
| `Esc` | 重設數字跳轉緩衝。若正在 `Shift` + 左鍵平移畫布則還原位置並取消；若正在拖曳搬移或複製則取消拖曳；否則若有多選則取消多選。 | `MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`、`EndPan`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.DragMove.cs`（`EndMovePress`）、`CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`（`ClearMultiSelection`） |
| `F2` | 在選取節點上開啟就地重新命名編輯框並全選文字。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`ProcessCmdKey`、`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`BeginRename`） |
| `Ctrl+I` | 在選取節點後方開啟就地編輯框插入同層節點。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`BeginInsert`） |
| `Ctrl+Shift+I` | 在選取節點前方開啟就地編輯框插入同層節點。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`BeginInsert`） |
| `Ctrl+C` | 複製選取節點（或所有多選節點）到剪貼簿。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Copy`） |
| `Ctrl+Shift+C` | 複製選取節點（或各多選節點）的路徑純文字到剪貼簿。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`CopyPath`） |
| `Ctrl+X` | 剪下選取節點（或所有多選節點）。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Cut`） |
| `Ctrl+V` | 貼在選取節點後方。多選時貼在最後一個多選節點後方，無選取時加在第一層末尾。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Paste`） |
| `Ctrl+Shift+V` | 貼在選取節點前方。多選時貼在第一個多選節點前方。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Paste`） |
| `Ctrl+Alt+V` | 貼成葉節點的子節點。多選時只在剛好選到單一葉節點時生效。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`PasteAsChild`） |
| `Delete` | 刪除選取節點（或所有多選節點），刪除前跳出確認對話框（預設鈕為「否」）。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Delete`、`DeleteNodes`）、`CascadeControl/Views/CascadeEditPrompts.cs`（`ConfirmDelete`） |
| `Ctrl+Z` | 復原上一步。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Undo`） |
| `Ctrl+Y`、`Ctrl+Shift+Z` | 重做上一步。 | `CascadeControl/Views/CascadeEditShortcuts.cs`、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`TryExecute`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`Redo`） |
| `Ctrl+N` | 新建文件（**File > New**），若有未存變更先跳確認框。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuFileNew_Click`）、`MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`、`FindMenuItem`） |
| `Ctrl+O` | 開啟檔案對話框（**File > Open...**）。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuFileOpen_Click`）、`MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`） |
| `Ctrl+S` | 存檔（**File > Save**），新文件自動開啟另存對話框。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuFileSave_Click`、`Save`）、`MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`） |
| `Ctrl+Shift+S` | 另存新檔（**File > Save As...**）。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuFileSaveAs_Click`、`Save`）、`MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`） |
| `Ctrl++`（`Ctrl+Oemplus`） | 放大一階（**View > Zoom In**）。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuViewZoomIn_Click`）、`MindMap/MindMapDiagramView.cs`（`ZoomIn`） |
| `Ctrl+-`（`Ctrl+OemMinus`） | 縮小一階（**View > Zoom Out**）。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuViewZoomOut_Click`）、`MindMap/MindMapDiagramView.cs`（`ZoomOut`） |
| `Ctrl+0`（`Ctrl+D0`） | 重設縮放為 100% 並重設畫布平移位置（**View > Reset Zoom (100%)**）。 | `MindMap/MainForm.Designer.cs`、`MindMap/MainForm.cs`（`MnuViewResetZoom_Click`）、`MindMap/MindMapDiagramView.cs`（`ResetZoom`、`ResetPan`） |
| `Alt+F`、`Alt+E`、`Alt+V` 與各項目助記鍵 | 開啟主功能表與子項目（WinForms `MenuStrip` 的 `&` 存取鍵）。 | `MindMap/MainForm.Designer.cs` |

## 就地編輯框操作（編輯模式）

| 操作方式 | UI 行為 | 所在 `.cs` 檔案 |
| :--- | :--- | :--- |
| `Enter` | 套用輸入名稱並關閉編輯框。若名稱含有保留字元則跳出警告對話框，保留編輯框並全選文字供重輸。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`EndEdit`）、`CascadeControl/Views/CascadeEditPrompts.cs`（`HasReservedCharacter`、`WarnReservedCharacter`） |
| `Esc` | 放棄編輯並關閉編輯框（若有佔位虛線方塊則移除重畫），焦點還回畫布。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`EndEdit`、`CloseEditBox`） |
| 編輯框失去焦點（按 `Tab`、點其他控制項或切換視窗） | 自動套用輸入名稱並關閉編輯框。若空白或含保留字元則直接當作取消，不跳警告對話框。 | `CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`（`EditBox_LostFocus`、`EndEdit`） |
| 一般文字編輯鍵（`Ctrl+C`、`Ctrl+V`、`Ctrl+X`、`Ctrl+A`、方向鍵、`Delete`） | 編輯框擁有焦點時不攔截，交由 `TextBox` 自行處理文字編輯。 | `MindMap/MindMapDiagramView.cs`（`ProcessCmdKey`）、`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`（`ProcessCmdKey`） |

## 主功能表與右鍵選單項目

### 上方功能表列

定義在 `MindMap/MainForm.Designer.cs`，事件與狀態切換實作在 `MindMap/MainForm.cs`。

- **File**：New、Open...、Save、Save As...、Exit。
- **Edit**：Undo、Redo、Cut、Copy、Paste、Delete、Select All、Insert（開啟下拉時依選取與復原狀態更新啟用；快捷鍵僅設 `ShortcutKeyDisplayString` 供顯示，實際按鍵交由畫布處理）。
- **View**：Zoom In、Zoom Out、Reset Zoom (100%)、Show Index（切換節點編號顯示與數字鍵跳轉）、Show Child Count（切換在有子節點的節點名稱後顯示直屬子節點數量，如 `Motor (3)`，無子節點時不顯示）。

### 畫布右鍵選單

定義與動態組裝在 `CascadeControl/Views/Diagram/CascadeDiagramView.ContextMenu.cs`，快捷鍵顯示文字讀取自 `CascadeControl/Views/CascadeEditShortcuts.cs`。

- **復原**、**重做**
- **插入 > 之前**、**插入 > 之後**（在「選取的子樹」範圍模式下停用）
- **建立子節點**（僅對葉節點啟用）
- **重新命名**
- **複製**、**複製(路徑)**、**剪下**
- **貼上 > 之前**、**貼上 > 之後**、**貼上 > 作為子節點**（剪貼簿有節點資料時啟用，「作為子節點」僅對葉節點啟用）
- **刪除**
- **全部展開**、**全部摺疊**（僅對有子節點的節點啟用）
- **範圍 > 整棵樹**、**範圍 > 選取的子樹**
- **顯示 > 原始尺寸**、**顯示 > 自動縮放**

## 各 `.cs` 檔案職責總覽

- `MindMap/MainForm.cs`、`MindMap/MainForm.Designer.cs`：主功能表（File、Edit、View）點擊與快捷鍵設定、外部拖放 CSV 開檔、未存檔關閉確認框、狀態列（節點數、層數、選取、縮放百分比）更新。
- `MindMap/MindMapDiagramView.cs`：MindMap 專屬互動覆寫，包含 `Shift` + 左鍵自由平移畫布、`Ctrl` + 滾輪縮放（維持游標錨點）、左鍵點節點選取、`Tab` 插入子項、`Enter` 下方插入同層、`Ctrl+A` 全選，以及讓主功能表快捷鍵優先於基底編輯快捷鍵。
- `CascadeControl/Views/Diagram/CascadeDiagramView.cs`：右鍵選單物件建立與事件掛載、檢視模式屬性（`DrawScope`、`SizeMode`、`ZoomFactor`、`ShowIndex`、`ShowChildCount`、`StepIntoCollapsed`）。
- `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs`：畫布滑鼠事件（Hover、Click、DoubleClick、MouseDown、MouseMove、MouseUp、CaptureChanged）與鍵盤分派（方向鍵、`Space` 與中文輸入法 `WndProc` 修正、`*`、數字鍵跳轉、`Esc`、編輯快捷鍵派發）。
- `CascadeControl/Views/Diagram/CascadeDiagramView.MultiSelect.cs`：框選多選、`Ctrl` + 點選切換、拖曳時邊緣自動捲動、全選與清除多選、多選與選取框繪製。
- `CascadeControl/Views/Diagram/CascadeDiagramView.DragMove.cs`：左鍵拖曳搬移、`Ctrl` + 拖曳複製、拖曳中即時切換搬移與複製、落點判定（兄弟間隙、上下 20% 邊緣、目標子節點）、預覽與目標高亮相框繪製。
- `CascadeControl/Views/Diagram/CascadeDiagramView.Editing.cs`：就地編輯 `TextBox` 建立、定位、失焦套用、Enter 套用與 Esc 取消，以及所有編輯 UI 指令入口（`Copy`、`CopyPath`、`Cut`、`Paste`、`PasteAsChild`、`Delete`、`BeginInsert`、`BeginCreateChild`、`BeginRename`、`Undo`、`Redo`）。
- `CascadeControl/Views/Diagram/CascadeDiagramView.ContextMenu.cs`：右鍵選單開啟前重建項目、啟用條件與點選後延遲執行。
- `CascadeControl/Views/Diagram/CascadeDiagramView.Expansion.cs`：展開與收合狀態切換、子樹全部展開與全部摺疊。
- `CascadeControl/Views/Diagram/CascadeDiagramView.Rendering.cs`、`CascadeControl/Views/Diagram/TreeImage.cs`：滑鼠座標反算、節點與展開鈕命中偵測（`GetNodeAt`、`GetExpanderAt`）、鍵盤導覽後自動捲動至可見（`EnsureVisible`）。
- `CascadeControl/Views/CascadeEditShortcuts.cs`：編輯按鍵與指令的集中對照表，以及右鍵選單快捷鍵字串格式化。
- `CascadeControl/Views/CascadeEditPrompts.cs`：刪除確認對話框與保留字元警告對話框。

## 互動覆蓋與邊界行為

1. **`Ctrl+N` 觸發開新檔而非建立子節點**：右鍵選單的「建立子節點」仍從 `CascadeControl/Views/CascadeEditShortcuts.cs` 讀取並顯示 `Ctrl+N`。`MindMap/MindMapDiagramView.cs` 的 `ProcessCmdKey` 會先比對主功能表快捷鍵，因此按 `Ctrl+N` 實際執行 **File > New**，建立子節點改由 `Tab` 或右鍵選單執行。
2. **`Enter` 覆蓋基底節點啟動事件**：`MindMap/MindMapDiagramView.cs` 攔截 `Enter` 執行 `BeginInsert(before: false)`，覆蓋基底 `CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs` 按 `Enter` 觸發 `NodeActivated` 的行為。
3. **雙擊節點本體沒有動作**：`CascadeControl/Views/Diagram/CascadeDiagramView.Input.cs` 在雙擊節點時只觸發 `NodeMouseDoubleClick`，`MainForm` 與 `MindMapDiagramView` 均未訂閱該事件。雙擊 `[+]`、`[-]` 展開鈕仍會切換展開狀態。
4. **右鍵選單不顯示「未勾選節點」子選單**：`CascadeControl/Views/Diagram/CascadeDiagramView.ContextMenu.cs` 只在 `ShowChecks` 為 `true` 時加入該選單，`MindMap` 維持預設 `false`。
5. **就地編輯時 `File` 與 `View` 快捷鍵仍會觸發**：`MainForm` 的 **File** 與 **View** 項目設有 WinForms `ShortcutKeys`（`Ctrl+N`、`Ctrl+O`、`Ctrl+S`、`Ctrl+Shift+S`、`Ctrl++`、`Ctrl+-`、`Ctrl+0`），就地編輯時按鍵會經 `base.ProcessCmdKey` 傳往表單功能表。**Edit** 選單未設 `ShortcutKeys`，所以 `Ctrl+C`、`Ctrl+V`、`Ctrl+X`、`Ctrl+Z`、`Ctrl+A`、`Delete` 會留在文字框內生效。
