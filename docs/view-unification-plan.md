# CascadeDiagramView 與 CascadeColumnsView 統一計畫

建立日期：2026-11-22。基準：`main` @ `9350480`，Debug 建置成功，單元測試 449 / 449 通過。

## 1. 目標

同一個表單可以同時放 Diagram 和 Columns，也可以只放其中一個。兩者綁定同一份 `CascadeState`。在任一個 View 上的編輯，都由資料層執行並發出通知，另一個 View 收到通知後重繪。View 只做兩件事：

- 把滑鼠與鍵盤輸入轉成對資料層的呼叫。
- 訂閱資料層的事件並重繪。

達成條件：

- 改樹、復原歷史與編輯事件不再屬於任何一個 View。
- Diagram 支援和 Columns 相同的編輯指令與快捷鍵。
- 兩個 View 的檔案結構、命名與輸入處理遵守同一套規則（見第 2 節）。

## 2. 已定案的設計決策

### D1. 共享單位是 `CascadeState`，不是 `MenuTree`

兩個 View 現在共用的是 `CascadeState`。`CascadeControlViewer/TreeViewer.cs:37` 寫的是 `diagramView1.State = cascadeView1.State`。選取同步，以及 Columns 編輯後 Diagram 跟著重畫，都靠 `CascadeState.SelectedPathChanged`：Columns 編輯完呼叫 `RefreshColumns`，進而呼叫 `CascadeState.Refresh`。`CascadeState` 的 remarks 也已經寫著「檢視控制項只負責呈現與把使用者操作轉成對它的呼叫」。所以資料層已經存在，缺的是編輯那一半。

### D2. 編輯落點是 `CascadeState`，不是 `MenuTree`

把編輯與事件放進 `MenuTree` 有四個問題：

1. 真正改樹的方法在 `MenuNode` 上（`InsertChildAt`、`RemoveFromParent`、`Rename` 都是 public）。事件放在 `MenuTree`，直接呼叫 `MenuNode` 就會繞過。要擋住就得讓節點回報到樹，結果剪貼簿解析、`FromRows`、`FromSubtree` 建暫存樹時也會觸發事件。
2. 編輯後要選哪個節點，靠的是 `CascadeState.Refresh(path)`，樹不知道選取。如果樹先發事件、State 後 Refresh，View 會在選取路徑還指向已刪除的節點時重畫。
3. 「刪除節點時取消它的勾選，並且和刪除算同一步復原」需要 `MenuCheckSet`，而 `MenuCheckSet` 依賴 `MenuTree`。讓 `MenuTree` 反過來認識 `MenuCheckSet`，依賴方向就倒了。
4. `SetTree` 換樹時，View 要同時重新訂閱 Tree 與 State 兩個來源。

`MenuTree` 維持純資料結構，不加事件、不加歷史。

### D3. 用 partial 檔 `CascadeState.Editing.cs`，不另設 `CascadeTreeEditor`

另設 Editor 物件時，它必須反向引用 State，才能讀 `Tree`、呼叫 `Refresh`、取得 `CheckSet`。

- Editor 不由 State 持有：表單得把 State 與 Editor 分別指派給每個 View 並保持一致。漏指派一次就會出現兩份歷史，和目前每個 Columns 各有一份 `History` 是同一類錯誤。
- Editor 由 State 持有（`state.Editor`）：只是多一層呼叫，追程式碼時多一跳，換不到別的好處。

partial 是同一個物件、同一個生命週期，追「按 Delete 之後發生什麼事」時少一層。`CascadeState` 不相依 WinForms，所以編輯邏輯可以直接寫單元測試。日後編輯邏輯真的需要替換實作，再抽成獨立類別。

### D4. 不用 `CascadeEditAction` 當「要執行的指令」

`CascadeEditAction` 描述的是已經發生的事，所以含有 `Undo`、`Redo`。各種編輯需要的參數完全不同：插入要父節點、位置與名稱，刪除要節點清單，改名要節點與新名稱。寫成 `Execute(CascadeEditAction, ...)` 會需要一包鬆散的參數，「Rename 沒給名稱」這種組合也能通過編譯。

改成每種編輯一個方法。`CascadeEditAction` 只當 `TreeEdited` 的事件標籤，搬到 `State/`。

State 的編輯 API 草稿：

```csharp
// State/CascadeState.Editing.cs，不相依 WinForms
public MenuHistory History { get; }
public event EventHandler<CascadeTreeEditedEventArgs> TreeEdited;
public MenuNode InsertNode(MenuNode parent, int position, string name);
public MenuNode CreateChild(MenuNode leaf, string name);
public MenuNode RenameNode(MenuNode node, string name);   // 名稱沒變時回傳 null
public IReadOnlyList<MenuNode> PasteNodes(MenuNode parent, int position, IReadOnlyList<MenuNode> sources);
public bool CutNodes(IReadOnlyList<MenuNode> nodes);       // View 先寫好剪貼簿
public bool DeleteNodes(IReadOnlyList<MenuNode> nodes);    // View 先問過確認
public MenuHistoryResult Undo();
public MenuHistoryResult Redo();
protected virtual void OnNodesRemoved(IReadOnlyList<MenuNode> nodes) { } // CascadeCheckableState 覆寫成取消勾選
```

每個方法的流程都一樣：在 `History.BeginGroup` 裡改樹、記錄步驟、呼叫 `Refresh`，最後發出 `TreeEdited`。

### D5. 只有一份 History，由 State 持有

目前 `History` 是 Columns 的實例屬性（`CascadeColumnsView.Editing.cs:106`），`SyncCheckSetWithHistory`（`CascadeColumnsView.CheckBoxes.cs:158`）會把共享 CheckSet 的歷史換成自己那一份。兩個 View 都能編輯時，同一棵樹會有兩條時間軸。`MenuHistory` 發現樹對不上就會清掉整份歷史，使用者看到的是 Ctrl+Z 偶爾失效。History 移到 State 之後，所有 View 共用同一條時間軸。

### D6. 留在 View 的職責

以下和 WinForms 或單一 View 的畫面有關，不進 State：

- Windows 剪貼簿讀寫（抽成兩個 View 共用的 `Views/CascadeClipboard.cs`）。
- 刪除確認框。
- 就地編輯框。
- 多選。
- 焦點。
- `ReadOnly`：這個 View 接不接受編輯輸入。
- `CanUndo`、`CanRedo` 依 `ReadOnly` 決定能否復原改到樹的步驟。

### D7. 鍵盤處理規則

用 override 還是訂閱，由「誰拿到焦點」決定。WinForms 只把 KeyDown 送給有焦點的控制項。

- Columns 的焦點一定在子 ListBox 上（`OnGotFocus` 會主動轉交，`CascadeColumnsView.Input.cs:613`），Panel 自己的 `OnKeyDown` 收不到，只能處理子控制項的事件。
- Diagram 自己就是焦點控制項。類別處理自己的事件用 override 是 WinForms 慣例，訂閱是給外部用的。

統一的是快捷鍵這一層：

- 編輯快捷鍵（Ctrl 組合鍵、Delete、F2、Esc）：兩個 View 都在 `ProcessCmdKey` 處理，並查同一張對照表 `Views/CascadeEditShortcuts.cs`（`Keys` 對應到 internal enum `CascadeEditCommand`）。`ProcessCmdKey` 從焦點控制項往上逐層呼叫，不管焦點在哪個子控制項都攔得到，也比表單的 AcceptButton 與選單快捷鍵早執行。右鍵選單顯示的快捷鍵也從這張表讀，兩邊的綁定就不會各自走樣。
- 導覽鍵（方向鍵、Enter、Space、字元跳轉）：依呈現方式而不同，留在各自的 View。自己是焦點控制項就覆寫 `OnKeyDown` 加 `IsInputKey`；焦點在子控制項就在子控制項處理。

### D8. 命名與檔案規則

- 給表單用的公開型別（控制項、屬性用的 enum、EventArgs）以 `Cascade` 開頭。只屬於某個 View 的 enum 用 `Cascade{View}{概念}`，比照 `CascadeColumnLayoutMode`。
- `TreeDrawer`、`TreeImage`、`TreeImageNode` 是繪圖工具，名稱準確，不改。
- 一個 enum 一個檔。
- partial 檔名後綴代表關注點。兩個 View 都有的關注點用同一個後綴：`Input`、`Rendering`、`Editing`、`ContextMenu`。各自特有的用自己的後綴：Columns 的 `Columns`、`MultiSelect`、`CheckBoxes`，Diagram 的 `Expansion`。
- 事件處理常式寫成 `來源_事件`（例如 `State_SelectedPathChanged`）。`OnXxx` 只留給 protected virtual 的事件觸發方法。

### D9. `ICascadeView` 的成員

保留現有成員，另外新增：

- `CascadeState State { get; set; }`：兩個 View 同步靠的就是它。
- `bool ReadOnly { get; set; }`
- `event EventHandler<CascadeNodeMouseEventArgs> NodeMouseClick`
- 編輯指令：`Copy`、`CopyPath`、`Cut`、`Paste(bool before)`、`PasteAsChild`、`Delete`、`BeginInsert(bool before)`、`BeginCreateChild`、`BeginRename`、`EndEdit(bool commit)`、`IsEditing`、`Undo`、`Redo`、`CanUndo`、`CanRedo`。

編輯指令放在 View 上，是因為操作對象取決於該 View 的多選，`Begin*` 也要用到該 View 的就地編輯框。工具列按鈕可以直接呼叫目前有焦點的那個 View。

不放進介面的：

- `History` 與 `TreeEdited`：它們在 State 上，表單訂閱一次就夠。兩個 View 各轉發一次的話，同一次編輯會收到兩次通知。
- 各 View 自己的呈現設定，例如 `DrawScope`、`SizeMode`、`ColumnWidth`。

`ICascadeCheckableView` 不動。

### D10. 目標檔案結構

```
CascadeControl/
  State/
    CascadeState.cs                  選取與導覽（現有）
    CascadeState.Editing.cs          新增：History、TreeEdited、編輯方法
    CascadeCheckableState.cs         CheckSet.History 指向 State.History；覆寫 OnNodesRemoved
    CascadeEditAction.cs             從 Views/Columns 移入
    CascadeTreeEditedEventArgs.cs    從 Views/Columns 移入
  Views/
    ICascadeView.cs
    ICascadeCheckableView.cs
    CascadeNodeMouseEventArgs.cs
    CascadeClipboard.cs              新增：剪貼簿讀寫，兩個 View 共用
    CascadeEditShortcuts.cs          新增：Keys 對應到 CascadeEditCommand
    CascadeEditCommand.cs            新增：internal enum
    Columns/
      CascadeColumnsView.cs
      CascadeColumnsView.Columns.cs
      CascadeColumnsView.Input.cs
      CascadeColumnsView.Rendering.cs
      CascadeColumnsView.MultiSelect.cs
      CascadeColumnsView.CheckBoxes.cs
      CascadeColumnsView.Editing.cs       只剩就地編輯框與指令轉譯
      CascadeColumnsView.ContextMenu.cs   從 Editing 抽出
      CascadeColumnLayoutMode.cs
      CascadeDrawNodeEventArgs.cs
      CascadeColumnsViewDesigner.cs
    Diagram/
      CascadeDiagramView.cs               公開 API、State 掛接
      CascadeDiagramView.Rendering.cs     Redraw、OnPaint、座標轉換
      CascadeDiagramView.Expansion.cs     展開收合
      CascadeDiagramView.Input.cs         滑鼠、OnKeyDown、ProcessCmdKey
      CascadeDiagramView.Editing.cs       編輯指令與就地編輯框
      CascadeDiagramView.ContextMenu.cs   顯示設定與編輯項目
      CascadeDiagramSizeMode.cs
      CascadeDiagramDrawScope.cs
      CascadeDiagramUncheckedDisplay.cs
      TreeDrawer.cs
      TreeImage.cs
```

## 3. 步驟清單

A 段是重構，除了第 5 節列出的待決定行為變更，其他行為都不變。B 段是新功能。每一步各自一個 commit，通過驗證才進下一步。

### 共用驗證

每一步都要跑：

```powershell
# 1. Debug 與 Release 建置，0 錯誤、0 新增警告
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CascadeControl.sln /p:Configuration=Debug /nologo /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CascadeControl.sln /p:Configuration=Release /nologo /v:minimal

# 2. 單元測試全部通過（基準 449，之後逐步增加）
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" CascadeControlTests\bin\Debug\CascadeControlTests.dll
```

另外開啟 CascadeControlViewer，手動操作該步列出的動作。

標示「API 比對」的步驟，用 A0 建立的公開 API 傾印腳本比對前後輸出。只搬檔案的步驟必須沒有差異；有預期改名的步驟，差異只能是那些改名。

```powershell
# 建置 Debug 之後執行。必須用 Windows PowerShell 5.1（powershell.exe），不是 pwsh。
powershell -NoProfile -File tools\dump-public-api.ps1 -OutFile $env:TEMP\public-api.txt
git diff --no-index tools\public-api.txt $env:TEMP\public-api.txt
```

差異符合預期之後，用新的輸出覆蓋 `tools\public-api.txt`，和該步一起 commit。

### A 段：重構

- [x] **A0 釘住現有行為**
  - 新增 `CascadeControlTests/Views/Columns/CascadeColumnsViewEditingTests.cs`，透過 Columns 現有公開 API 釘住插入、建立子節點、改名（`EndEdit`）、貼上、剪下、復原、重做之後的樹形、選取，以及 `TreeEdited` 的 `Action` 與 `Nodes`。
  - 刪除會跳 MessageBox，這一步不釘，A3 搬到 State 之後在 State 層測。
  - 新增公開 API 傾印腳本（例如 `tools/dump-public-api.ps1`），用反射列出 `CascadeControl.dll` 的公開型別與成員，輸出排序過的文字，並存一份基準輸出。
  - 驗證：新測試全部通過；腳本連跑兩次輸出相同。
  - 結果：新增 22 個測試，全套 471 個。`tools/dump-public-api.ps1` 與基準 `tools/public-api.txt`（452 行、38 個公開型別）連跑兩次相同，Debug 與 Release 相同。
  - 剪貼簿測試在 STA 執行緒上跑（`RunOnSta`）。`Copy()`、`Cut()` 回傳 false 表示剪貼簿被其他程式佔用，記為 Inconclusive，不算失敗（`RequireClipboard`）。
  - 已知問題：貼上偶爾貼進名稱全是 NUL 字元的節點，約每 20 輪失敗 1 次，見 issue #1，在 A4 處理。這種失敗不是該步的改動造成的，重跑即可；其他失敗都要查。

- [x] **A1 搬型別，並把已共有的成員加進 ICascadeView**
  - `CascadeEditAction` 與 `CascadeTreeEditedEventArgs` 移到 `State/`，各自一個檔，命名空間改為 `CascadeControl.State`。
  - `State` 與 `NodeMouseClick` 加進 `ICascadeView`（兩個 View 都已實作）。
  - 更新 Viewer 與測試的 `using`。
  - 驗證：共用驗證；API 比對只有命名空間變動與介面新增成員。
  - 結果：Debug 與 Release 建置成功，只有原本就有的 2 個 CS1573 警告（`TreeDrawer.cs:622`）。API 比對只有兩個型別換命名空間（連帶 `TreeEdited`、`OnTreeEdited` 的簽章）與 `ICascadeView` 新增 `State`、`NodeMouseClick`，`tools/public-api.txt` 已更新為 454 行。Viewer 與 Selector 原本就有 `using CascadeControl.State`，只有 `CascadeColumnsView.Editing.cs` 與 `CascadeColumnsViewEditingTests.cs` 需要補。
  - 移過去的 XML 註解仍以完整名稱 cref 指向 `Views.Columns.CascadeColumnsView.TreeEdited`，A3 把事件搬到 State 時改掉。
  - 拆出 enum 後，新檔與舊檔相似度低於 git 預設的 50%，同一個 commit 裡 git 不會把它認成改名。要讓 `git log --follow` 追得到，分兩個 commit：先 `git mv` 加改命名空間，再拆出 `CascadeEditAction.cs`。
  - issue #1 的貼上失敗比 A0 記錄的頻繁：同一台機器上 `HEAD` 與 A1 各跑 4 輪全套，都有 2 輪失敗，失敗的都是剪貼簿貼上測試（`InsertAndPasteKeys_BeforeAndAsChild`、`UndoRedo_Paste_RemovesAndReturnsSameNodes`、`PasteAsChild_MultipleNodes_KeepsOrder_MultiSelectsThem`）。

- [x] **A2 History 改由 State 持有**
  - `CascadeState` 持有 `History`，`SetTree`、`SetTopLevelNodes`、`Clear` 時清空。
  - `CascadeCheckableState` 換 `CheckSet` 時，讓 `CheckSet.History` 指向 State 的 `History`。
  - 刪除 `CascadeColumnsView.History` 與 `SyncCheckSetWithHistory` 裡換指標的邏輯。Columns 的 `State` setter 不再清空歷史。
  - Viewer 改讀 `State.History`。
  - `CascadeColumnsViewCheckBoxesTests.cs` 第 244、650、761 行的斷言照新規則改寫。
  - 驗證：共用驗證；在 Viewer 勾選後按 Ctrl+Z 會復原；兩個 View 綁同一份 State 時，`UndoCount` 只有一份。
  - 結果：Debug 與 Release 建置成功，只有原本的 2 個 CS1573 警告。新增 3 個測試，全套 474 個通過。API 比對只有 `History` 從 `CascadeColumnsView` 移到 `CascadeState`，`tools/public-api.txt` 已更新。在 Viewer 對欄位送 Space 勾選後 `UndoCount` 為 1，Ctrl+Z 取消勾選後變成 Undo 0、Redo 1，Diagram 的 `State.History` 是同一個物件。
  - `History` 暫放在 `CascadeState.cs`，A3 新增 `CascadeState.Editing.cs` 時搬過去。
  - 連帶變更：切換 Columns 的 `CheckBoxes` 不再清空歷史，`SyncCheckSetWithHistory` 整個刪除，只剩重畫。換 `CheckSet` 時清空 State 的歷史，換下來的那一份改回自己的一份新歷史，之後對它的勾選不會混進 State 的時間軸。
  - 刪除時取消勾選仍然只在 `CheckBoxes = true` 時發生，A3 改成只要有 `CheckSet` 就取消。在那之前，`CheckBoxes = false` 時刪掉已勾選的節點，勾選會留在已經不在樹裡的節點上。
  - issue #1 第一輪全套出現一次，這次是 `Cut_SelectsNextSibling_AndCanBePastedBack`（貼回的節點名稱是 NUL）；重跑全部通過。

- [ ] **A3 編輯邏輯移到 State**
  - 新增 `State/CascadeState.Editing.cs`（D4 的 API）與 `OnNodesRemoved` 掛鉤；`CascadeCheckableState` 覆寫它來取消勾選。
  - Columns 的 `ApplyEdit`、`RemoveNodes`、`PasteInto`、`Replay` 改成呼叫 State。
  - Columns 訂閱 `State.TreeEdited`，一次回來多個節點時（貼上、復原刪除）設定多選。
  - 刪除 `CascadeColumnsView.TreeEdited` 與 `OnTreeEdited`，Viewer 改訂閱 `State.TreeEdited`。
  - 新增 `CascadeControlTests/State/CascadeStateEditingTests.cs`（不需要 WinForms），涵蓋每個編輯方法的樹形、選取、事件與歷史，包含「刪除與取消勾選算同一步」。
  - 驗證：共用驗證；A0 的測試照常通過；在 Viewer 用 Columns 編輯，Diagram 跟著重畫，記錄區每次編輯只出現一行 TreeEdited。

- [ ] **A4 抽出剪貼簿**
  - Columns.Editing 的剪貼簿讀寫與 `CLIPBOARD_FORMAT` 移到 `Views/CascadeClipboard.cs`。類別為 public，只公開常數，方法為 internal。
  - 驗證：共用驗證；在 Viewer 複製節點後貼回另一處；複製後貼到記事本看得到 CSV 文字。

- [ ] **A5 快捷鍵對照表**
  - 新增 `Views/CascadeEditCommand.cs` 與 `Views/CascadeEditShortcuts.cs`。
  - Columns 的 `ProcessCmdKey` 與右鍵選單的 `ShortcutKeyDisplayString` 改成查表。
  - 新增表格測試，逐一列出現有的全部綁定。
  - 驗證：共用驗證；在 Viewer 逐一按每個快捷鍵，結果和改之前相同。

- [ ] **A6 拆檔，只搬不改**
  - Diagram 拆成 `CascadeDiagramView.cs`、`.Rendering.cs`、`.Expansion.cs`、`.Input.cs`、`.ContextMenu.cs`。
  - `TreeImageSizeMode`、`TreeDrawScope`、`UncheckedNodeDisplay` 各自一個檔（這一步不改名）。
  - Columns 的右鍵選單從 `.Editing.cs` 抽到 `.ContextMenu.cs`。
  - csproj 補上 `DependentUpon`。
  - 驗證：共用驗證；API 比對沒有差異；`git log --follow` 追得到搬移前的歷史。

### B 段：新功能

- [ ] **B1 Diagram 預設 State 改成 `CascadeCheckableState`**
  - 驗證：單獨放一個 Diagram，設 `ShowChecks = true`，用程式勾選後畫面有變化。

- [ ] **B2 Diagram 加上不需要輸入文字的編輯**
  - 加上 `ReadOnly`、`Copy`、`CopyPath`、`Cut`、`Paste`、`PasteAsChild`、`Delete`、`Undo`、`Redo`、`CanUndo`、`CanRedo`。
  - 透過 `ProcessCmdKey`（查 A5 的對照表）與右鍵選單觸發，對象是目前選取的節點。
  - 驗證：共用驗證；在 Viewer 的 Diagram 按 Delete，Columns 同步更新；在 Columns 按 Ctrl+Z 能復原 Diagram 的刪除。

- [ ] **B3 Diagram 就地改名**
  - TextBox 疊在節點方塊上，位置用現有的 `GetClientBoundsOf` 計算。
  - State 發出 `TreeEdited` 或 Refresh 時，中止進行中的編輯。
  - 驗證：在 Diagram 按 F2 改名，Columns 同步；Columns 的編輯框開著時在 Diagram 刪除節點，編輯框正確關掉。

- [ ] **B4 Diagram 插入與建立子節點**
  - 這個操作方式在專案裡沒有前例。先各做一個原型比較，再決定：
    - 在圖上留一個暫時的空白方塊（要改 `TreeDrawer`），確認後才建立節點。
    - 先用預設名稱建立節點再開改名框，按 Esc 時復原。
  - 驗證：Ctrl+I、Ctrl+Shift+I、Ctrl+N 可以建立節點；按 Esc 取消後，樹與歷史都沒有變化。

- [ ] **B5 編輯指令寫進 `ICascadeView`**
  - 加入 D9 列出的編輯指令。此時兩個 View 都已實作。
  - 驗證：共用驗證。

- [ ] **B6（選做）改名**
  - Diagram 的三個 enum 改名為 `CascadeDiagramSizeMode`、`CascadeDiagramDrawScope`、`CascadeDiagramUncheckedDisplay`，同時更新 Selector 與 Viewer 的設計工具檔。
  - Columns 的事件處理常式改成 `來源_事件` 寫法。
  - Columns 的 KeyDown 改由 `ColumnListBox.OnKeyDown` 轉呼叫，和 KeyPress 一致。
  - 每一項各自一個 commit。
  - 驗證：共用驗證；API 比對差異只有預期的改名；在 Visual Studio 開啟兩個表單的設計工具，沒有錯誤。

## 4. 下一步

目前是 **A3 編輯邏輯移到 State**。A0、A1、A2 已完成。第 5 節「待決定的行為變更」分別在 A2、A3 用到，使用者已決定。

## 5. 注意事項

### 待決定的行為變更

這兩項會在 A2、A3 發生，需要確認後才能進行：

1. **勾選要不要進復原歷史。**現在 Columns 的 `CheckBoxes = false` 時，勾選不進歷史。History 改成共用之後，記不記就不能再由單一 View 決定。建議一律記錄，因為 Diagram 開了 `ShowChecks` 也看得到勾選。使用者決定要一律紀錄。
2. **刪除節點時要不要取消它的勾選。**現在只有 `CheckBoxes = true` 時才會取消。建議只要有 `CheckSet` 就一律取消，因為已經不在樹裡的節點保持勾選沒有意義。使用者決定只要有 `CheckSet` 就一律取消。

另一項連帶變更：換掉某個 View 的 `State` 時不再清空歷史，因為歷史跟著 State 走。

### 刻意不改的東西

- `MenuTree`、`MenuNode`：維持純資料結構，不加事件與歷史（見 D2）。
- `MenuHistory`：不改內部，只換持有者。
- `ICascadeView` 現有的轉發成員（`Tree`、`SelectedNode`、`SelectedPath` 等）：保留。有了 `State` 之後它們看似多餘，但移除會破壞公開 API，換不到好處。
- `ICascadeCheckableView`：不動，Diagram 不實作它。
- `TreeDrawer`、`TreeImage`、`TreeImageNode`：名稱不改（見 D8）。
- 多選：留在各自的 View，不共享。不同 View 的多選可以不一樣。
- 導覽鍵：不統一。方向鍵、Space 在兩個 View 的意義本來就不同（見 D7）。

### 其他注意事項

- **不留轉發 API。** A2、A3 刪除 Columns 的 `History`、`TreeEdited`、`OnTreeEdited` 時，同一步更新所有呼叫端（Viewer 與測試），不留轉發用的舊 API。
- **搬檔要保留歷史。** 搬檔用 `git mv`，拆檔與搬檔分成不同 commit，讓 `git log --follow` 追得到。
- **公開型別的命名空間要跟資料夾一致。** 見 `docs/refactor-plan.md` 第二輪的 IDE0130 規則。
- **刪除很難自動測試。** 刪除會跳 MessageBox，不能在 View 層自動測試。刪除的行為在 State 層（A3）測。
- **剪貼簿相關測試要在 STA 執行緒執行。** 用 `CascadeColumnsViewEditingTests` 的 `RunOnSta`。這台機器開著 Windows 剪貼簿歷程，剪貼簿偶爾被佔用，所以寫入剪貼簿失敗記為 Inconclusive。
- **腳本與文件的編碼。** `.ps1` 要存成 UTF-8 含 BOM，否則 Windows PowerShell 5.1 會用 ANSI 讀中文而解析失敗。Git Bash 的 `sed -i` 會把 CRLF 改成 LF，改檔請用 Edit 工具。
- **跨 View 的就地編輯。** 一個 View 的編輯框開著時，另一個 View 編輯了樹，State 會 Refresh。Columns 已經會在 Refreshed 時中止編輯；B3 的 Diagram 編輯框也要做到。
