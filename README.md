# CascadeControl

`CascadeView` 是用 Miller Columns（多欄串接）呈現多層選單的 WinForms 控制項。
選了某一項，如果它還有下一層，右邊就長出一欄；改選時，後面的欄位會整批換掉。
適合 debug 選單、設定項目這類「路徑深淺不一，最後選到一個終點去執行」的情境。

```
  第 1 欄          第 2 欄          第 3 欄（焦點）   第 4 欄
+--------------+ +--------------+ +--------------+ +--------------+
| <01>System   | | <01>Axis X   | | <01>Home     | | <01>Forward  |
|[<02>Motor   ]| |[<02>Axis Y  ]| |#<02>Jog     #| | <02>Backward |
| <03>IO       | | <03>Axis Z   | | <03>Speed    | |              |
| <04>Diagnost.| |              | |              | |              |
+--------------+ +--------------+ +--------------+ +--------------+
```

`[ ]` 是選取路徑上的項目（淡藍），`# #` 是鍵盤焦點所在欄的選取項（實心藍），
第 4 欄是 `Jog` 的子節點。`Forward` 沒有下一層，選到它再按 Enter 就會觸發 `NodeActivated`。

- **需求**：.NET Framework 4.8、Windows Forms，不需要第三方套件。
- **命名空間**：`CascadeControl.Views`（控制項）、`CascadeControl.Models`（選單樹與 `|` 語法）、`CascadeControl.Services`（文字與 CSV 讀寫）。

**目錄**：[快速開始](#快速開始) · [常見任務](#常見任務) · [CSV 格式](#csv-格式) · [操作方式](#操作方式) · [API 速查](#api-速查) · [注意事項](#注意事項)

---

## 快速開始

### 1. 加入參考

把建置輸出（`bin\Release\`）裡的兩個檔案複製到你的專案，例如放在 `lib\`：

| 檔案 | 說明 |
|---|---|
| `CascadeControl.dll` | 必要。 |
| `CascadeControl.xml` | 建議一併複製。IntelliSense 的中文說明來自這個檔案，必須和 dll 放在同一個資料夾才會生效。 |

接著在方案總管的專案上按右鍵，選「加入 → 參考 → 瀏覽」，然後選取 `CascadeControl.dll`。

### 2. 放到表單上

在工具箱上按右鍵，選「選擇項目」，切到「.NET Framework 元件」分頁，按「瀏覽」選取 `CascadeControl.dll`，
再勾選 `CascadeView`。之後就能從工具箱把它拖到表單上。

不想用設計工具的話，直接在程式碼裡建立也可以：

```csharp
var cascadeView1 = new CascadeView { Dock = DockStyle.Fill };
Controls.Add(cascadeView1);
```

### 3. 準備選單檔

每一列是一條由第一層到終點的路徑。同一格可以用 `|` 列舉多個同層項目，詳見 [CSV 格式](#csv-格式)。

```csv
Level1,Level2,Level3,Level4
System,Reboot,,
System,Info,Version|Uptime,
Motor,Axis X|Axis Y,Home,
Motor,Axis X|Axis Y,Jog,Forward|Backward
```

### 4. 載入選單並處理執行

```csharp
using CascadeControl.Models;
using CascadeControl.Services;
using CascadeControl.Views;

public MainForm()
{
    InitializeComponent();

    MenuTree menu = MenuTreeCsvFile.Load("menu.csv", out IList<string> warnings);
    foreach (var warning in warnings) Debug.WriteLine(warning);

    cascadeView1.NodeActivated += CascadeView1_NodeActivated;
    cascadeView1.SetTree(menu);
}

// 使用者在節點上雙擊或在葉節點上按 Enter 時觸發。
private void CascadeView1_NodeActivated(object sender, CascadeNodeEventArgs e)
{
    var node = (MenuNode)e.Node;
    MessageBox.Show($"執行：{node.FullPathText}");   // 例如「Motor > Axis X > Jog > Forward」
}
```

`MenuTreeCsvFile.Load` 不會丟例外：讀取失敗時回傳空樹，原因寫在 `warnings` 裡。
檔案不存在時，它會在同一個路徑寫出一份只有表頭的範本。

---

## 常見任務

### 用程式組選單

**`MenuTree.AddPath`**：一次加入一條路徑。畫面上的順序就是呼叫的順序，同一條路徑重複加入會自動合併。名稱不可含有 `|`，否則丟 `ArgumentException`；要列舉同層項目請用下面的 `ExpandAndAddPaths`。

```csharp
var menu = new MenuTree();
menu.AddPath("System", "Reboot");
menu.AddPath("System", "Info", "Version");
menu.AddPath("Motor", "Axis X", "Home");

cascadeView1.SetTree(menu);
MenuTreeCsvFile.Save(menu, "menu.csv");   // 需要的話存成檔案
```

**`MenuTree.ExpandAndAddPaths`**：一次加入一整列，每一格都可以用 `|` 列舉同層項目，規則和 CSV 相同。回傳每一條路徑的終點節點。

```csharp
menu.ExpandAndAddPaths("Motor", "Axis X|Axis Y", "Jog", "Forward|Backward");   // 加入 4 條路徑
menu.ExpandAndAddPaths("IO", "Input", SiblingCellSyntax.JoinSiblings(inputNames)); // 名稱清單來自程式時
```

和 `AddPath` 不同，`ExpandAndAddPaths` 會先去除每個名稱前後的空白；有任何一格不合法時丟 `ArgumentException`，整列都不會加入。

### 用文字組選單

**`MenuTreeCsvText`** 處理文字和樹之間的轉換，不碰檔案。格式和 CSV 檔完全相同，所以可以直接在程式裡手寫，
也可以貼上從剪貼簿、資料庫或其他來源拿到的整段文字：

```csharp
MenuTree menu = MenuTreeCsvText.Parse(@"
    System, Reboot|Shutdown
    System, Info,          Version|Uptime|Memory

    Motor,  Axis X|Axis Y, Home
", out IList<string> warnings);

string text = MenuTreeCsvText.Format(menu, MenuRowStyle.Outline);   // 轉回文字，可另外存放
```

- 表頭可有可無；空白行、縮排、逗號後的空白都會被忽略。
- 警告中的「第 N 列」是文字中的實際行號，空白行和跨行的儲存格也照算。
- `Parse` 不會丟例外，有問題的資料只會寫進 `warnings`。

**`CascadeNode`**：不需要 `MenuTree` 的功能（例如 CSV、序號、`FullPathText`）時，直接用內建節點組樹，再用 `Tag` 帶上自己的資料：

```csharp
cascadeView1.SetTopLevelNodes(new[]
{
    new CascadeNode("System", "System", children: new[]   // 名稱、顯示文字
    {
        new CascadeNode("Reboot", "Reboot", tag: myRebootCommand),
        new CascadeNode("Shutdown", "Shutdown", tag: myShutdownCommand),
    }),
});

// 在 NodeActivated 中取回自己的資料
var command = ((CascadeNode)e.Node).Tag;
```

### 接上自己的資料模型

只要實作 `ICascadeNode` 的三個成員就行。這個介面不相依 WinForms，可以放在資料層。

```csharp
public sealed class DeviceNode : ICascadeNode
{
    public string Name { get; }                                    // 原始名稱，用於路徑文字
    public string DisplayText => Name;                             // 欄位上顯示的文字，可以和 Name 不同
    public IReadOnlyList<ICascadeNode> Children => LoadChildren(); // 空集合代表終點
    // ...
}
```

### 讀取目前的選取

`SelectedPathChanged` 和 `NodeActivated` 用的是同一個 `CascadeNodeEventArgs`：

| 屬性 | 內容 | 未選取時 |
|---|---|---|
| `Path` | 由淺到深的整條選取路徑 | 空集合 |
| `Node` | 路徑的終點（最深的選取） | `null` |
| `ColumnIndex` | `Node` 所在欄位，由 0 起算 | `-1` |
| `Parent` | `Node` 的上一層；在第一欄時為 `null` | `null` |
| `Children` | 下一欄會顯示的內容（永遠不為 null） | 第一欄的內容 |
| `IsLeaf` | 是否已選到終點 | `false` |

```csharp
private void CascadeView1_SelectedPathChanged(object sender, CascadeNodeEventArgs e)
{
    btnRun.Enabled = e.IsLeaf;                    // 選到終點才能執行
    lblPath.Text = cascadeView1.SelectedPathText; // 「Motor => Axis X」
}
```

不在事件裡時，可以直接讀控制項的 `SelectedNode`、`SelectedPath`、`SelectedPathText` 和 `IsLeafSelected`。

### 用程式選取某條路徑

例如還原上次的選取：

```csharp
MenuNode last = menu.GetLeaves().FirstOrDefault(n => n.FullPathText == savedPath);
if (last != null) cascadeView1.TrySelectNode(last);
```

### 欄寬與顯示欄數

`ColumnLayoutMode` 決定欄寬，以及畫面上一次看得到幾欄：

| 模式 | 欄寬 | 畫面 |
|---|---|---|
| `Fixed`（預設） | 固定為 `ColumnWidth` | 放不下時出現水平捲軸 |
| `Fill` | 讓 `DisplayColumnCount` 欄平均填滿寬度，不小於 `ColumnWidth` | 一次看到 `DisplayColumnCount` 欄，其餘出現水平捲軸，捲動時對齊欄位邊界 |
| `Sliding` | 讓 `DisplayColumnCount` 欄平均填滿寬度，不小於 `ColumnWidth` | 一次顯示 `DisplayColumnCount` 欄，畫面隨操作欄滑動，更淺的欄位隱藏，**不出現捲軸** |

```csharp
cascadeView1.ColumnLayoutMode = CascadeColumnLayoutMode.Fill;
cascadeView1.DisplayColumnCount = menu.Height;   // 資料有幾層就設幾層
```

- `DisplayColumnCount` 預設為 `0`：`Fill` 時等同 `Fixed`，`Sliding` 時當成 `1`。不可設為負數。
- `Sliding` 的畫面右緣對齊操作欄的下一欄（沒有下一欄時對齊操作欄本身），但操作欄不會被推出左邊：只顯示一欄時，看到的是操作欄而不是它的下一欄。
- 平均後的欄寬小於 `ColumnWidth` 時，`Fill` 改用 `ColumnWidth` 並出現水平捲軸；`Sliding` 則自動減少顯示的欄數（至少 1 欄）。

### 退回與記憶軌跡

`ShowTrail` 決定畫面上要不要畫出記憶軌跡的路線：

| `ShowTrail` | 選取停在第 3 層、記憶還有第 4～7 層時的畫面 |
|---|---|
| `false`（預設） | 畫到第 4 欄，第 4 欄用淺灰底標出上次選的項目 |
| `true` | 第 4～7 欄都畫出來，記憶項目都用淺灰底標出；用 ↑↓ 切換項目時，右邊會跟著換成那個項目記住的路線 |

- `ClearTrail()` 忘掉所有分支的記憶軌跡，只留下目前的選取路徑。`SetTopLevelNodes`、`SetTree`、`Clear` 會連同選取一起清空。
- 記憶軌跡項目的底色可以用 `TrailBackColor` 調整。
- 不論 `ShowTrail` 怎麼設，記憶軌跡都會保留，按 → 就能沿記憶走回去；這個屬性只影響畫面。`ColumnLayoutMode` 為 `Sliding` 時沒有作用。

### 自訂外觀

| 想調整 | 用 |
|---|---|
| 字型、列高 | 控制項的 `Font`，列高會跟著字型自動調整 |
| 欄寬、欄距 | `ColumnWidth`（最小 20）、`ColumnSpacing`；平均排列或只顯示最後幾欄請見[欄寬與顯示欄數](#欄寬與顯示欄數) |
| 非焦點欄的選取底色 | `InactiveSelectionBackColor`，預設為調淡的系統選取色 |
| 記憶軌跡項目的底色 | `TrailBackColor`，預設為系統淺灰色 |
| `SelectedPathText` 的分隔字串 | `PathSeparator`，預設 `" => "` |
| 完全自繪 | `DrawNode` 事件，設定 `e.Handled = true` 就會略過預設外觀 |

```csharp
cascadeView1.DrawNode += (s, e) =>
{
    // 只改「未選取的終點」：用綠字標出可以直接執行的項目，其餘交給預設外觀。
    if (e.IsSelected || !(e.Node is MenuNode node) || !node.IsLeaf) return;

    e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds);
    TextRenderer.DrawText(e.Graphics, e.Node.DisplayText, e.Font, e.Bounds, Color.DarkGreen,
        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    e.Handled = true;
};
```

---

## CSV 格式

檔案（`MenuTreeCsvFile`）和文字（`MenuTreeCsvText`）用的是同一種格式。

- **表頭**：第一列是 `Level1,Level2,…`（不分大小寫）。讀檔時如果第一列不是表頭，會被當成資料並發出警告；`MenuTreeCsvText.Parse` 則不要求表頭。
- **一列一條路徑**：由左至右是第 1 層、第 2 層…，深度不足的尾欄留空。深度沒有上限。
- **用 `|` 列舉同層項目**：`Axis X|Axis Y` 代表兩個兄弟節點；同一列有好幾格這樣寫時，會依笛卡兒積展開成多條路徑（越右邊的欄位變化越快）。
  程式中組這種字串可以用 `SiblingCellSyntax.JoinSiblings`。
- **名稱**：前後空白會自動去除；**區分大小寫**，所以 `Reboot` 和 `reboot` 是兩個不同的項目。
- **重複與延伸**：完全相同的路徑會合併。`A,B` 和 `A,B,C` 同時存在時，`B` 會變成中間節點，不算錯誤。
- **顯示文字**：`MenuNode.DisplayText` 在畫面上顯示成 `<01>Name`，有子節點時再加上 ` [+]`；序號（`SiblingNumber`，由 1 起算）是它在同一層中第一次出現的順序。顯示文字只用在欄位上，路徑與記錄一律使用 `Name`。

以下情況不會丟例外，而是寫進 `warnings`：

- 中間缺一層，例如 `A,,C`：空欄之後的欄位會被忽略。
- `|` 用法錯誤，例如 `A||B`、`|A`、`A|`，或某一段只有空白：整列跳過。
- 單列展開超過 65,535 條（`SiblingCellSyntax.MAX_EXPANDED_PATHS`）：整列跳過。
- 整份展開累計超過 65,535 條：整份放棄，回傳空樹。

警告訊息中的「第 N 列」是檔案（或文字）中的實際行號。

### 上限為什麼是 65,535

這個數字不是隨意挑的保護值，而是現有架構的限制：

- **UI**：每一欄都是 Win32 `ListBox`，`LB_ITEMFROMPOINT`（`ListBox.IndexFromPoint`）只用 16 位元回傳索引。
  實測一欄放 70,000 項時，捲到底部點第一列，拿到的索引是 4442（= 69978 & 0xFFFF）。
  任何一欄的項目數都不會超過全樹的路徑數，所以總數限制在 65,535，就保證每一欄都點得對。
- **速度**：每次改選都會把下一欄整批重新加入，實測 1 萬項約 60 ms、65,535 項約 0.8 秒。上限內都能用，但單欄最好控制在數千項以內。
- **記憶體**：65,535 條路徑的樹約 30 MB，32 位元行程也沒問題。

單列與整份用同一個上限，所以寫出時不會出現某一列超過上限、需要拆開的情況（見下節）。

### 寫出：展開、壓縮或大綱

`MenuTreeCsvFile.Save(tree, filePath, style)` 和 `MenuTreeCsvText.Format(tree, style)` 都可以選擇寫出的寫法（`MenuRowStyle`），
三種寫法讀回來都是同一棵樹。以 `A` 底下有 `x`、`m`、`n`，而 `m`、`n` 底下都是 `1`、`2` 為例：

| `MenuRowStyle` | 規則 | 資料列 |
|---|---|---|
| `Expanded`（預設） | 每個終點一列 | `A,x,` / `A,m,1` / `A,m,2` / `A,n,1` / `A,n,2` |
| `Compact` | **相鄰**而且**子樹完全相同**的兄弟節點合併成同一格；遇到有下一層的兄弟就切成新的一列 | `A,x,` / `A,m\|n,1\|2` |
| `Outline` | **每個中間節點一列**，列出它底下的全部子節點；子樹完全相同的兄弟（不必相鄰）共用一列 | `A,x\|m\|n,` / `A,m\|n,1\|2` |

「子樹完全相同」指名稱、順序、結構都一樣。`Outline` 最接近手寫：同一層的兄弟一定擠在同一格，
有下一層的節點先出現在父節點那一列，再用自己的一列往下展開。

```csv
Level1,Level2,Level3,Level4
ActionStep,Init|ActionPretest|ActionBurnIn|Last,,
ActionStep,ActionPretest,Init|RfStep1|RfStep2|Analysis,
ActionStep,ActionPretest,RfStep1|RfStep2,Init|GetHWAddr|Last
ActionStep,ActionBurnIn,Init|Stop,
```

兩種壓縮都是標準形，不追求最少列數：同一棵樹只有一種寫法，再轉一次也不會改變。
`Compact` 的列與列不重疊，列序和展開形式一致；`Outline` 的中間節點會同時出現在父節點那一列和自己那一列。

寫出前會先檢查，讀不回同一棵樹的內容會直接丟 `ArgumentException`，不會寫出任何東西：
名稱含 `|`、名稱前後有空白，或展開後超過 65,535 條路徑。
`Outline` 展開後還包含中間節點那幾條，路徑數比終點多，所以很大的樹可能只有 `Outline` 寫不出來，這時請改用 `Compact`。

`MenuTreeCsvText.NormalizeRows(rows, style, out warnings)` 可以整理手寫的資料列：逐層分組排序、補齊欄數、去掉重複的路徑，再用指定的寫法寫出。

---

## 操作方式

| 按鍵 / 動作 | 行為 |
|---|---|
| ↑ ↓ | 在同一欄內移動，也就是改選這一層；其他分支的記憶軌跡保留 |
| → | 進入下一欄：有記憶軌跡時選上次的項目，沒有時選第一項 |
| ← | 退回上一欄，**選取也跟著退回**；更深的那一段留作記憶軌跡 |
| Enter | 在終點上觸發 `NodeActivated`；不是終點時等同 → |
| Esc | 清除選取（記憶軌跡保留），焦點回到第一欄 |
| 滑鼠點選 | 點不同的項目等同改選；點路徑或記憶軌跡中的同一項，等同連按 ← 或 → 到那一欄 |
| 雙擊 | 在操作欄的節點上觸發 `NodeActivated` |

- 鍵盤焦點固定在目前操作的那一欄（`ActiveColumnIndex`，最深選取項所在的欄），點在其他欄的空白處後，焦點會回到這一欄。
- 只要焦點在控制項的欄位上，←、→、Enter、Esc 一律由控制項處理。所以表單的 `CancelButton` 在這時候不會被 Esc 觸發。
- `Fixed` 模式下欄位多到放不下時會出現水平捲軸，並自動捲到操作欄，讓它的右邊剛好留得下一欄；`Fill` 與 `Sliding` 的行為見[欄寬與顯示欄數](#欄寬與顯示欄數)。

---

## API 速查

**`CascadeView`**（`CascadeControl.Views`，繼承自 `Panel`）

| 成員 | 說明 |
|---|---|
| `SetTree(tree)` / `SetTopLevelNodes(nodes)` / `Clear()` | 以選單樹或第一層節點集合重設，或清空 |
| `RefreshColumns([selectPath])` | 底層節點結構異動後重新整理欄位清單並選取指定路徑 |
| `TrySelectNode(node)` / `TrySelectPath(path)` | 選取指定節點或依序選取一條路徑 |
| `CopySelectedNode()` / `CopiedNode` | 複製目前選取的節點子樹至 `CopiedNode` |
| `FocusActiveColumn()` | 把焦點放到目前操作的那一欄 |
| `SelectedNode` / `SelectedPath` / `SelectedPathText` | 目前的選取（不含記憶軌跡） |
| `IsLeafSelected` | 是否已選到終點 |
| `ActiveColumnIndex` | 目前操作中的欄位索引（0-based） |
| `ColumnLayoutMode` / `DisplayColumnCount` | 欄位排版模式（`Fixed`、`Fill`、`Sliding`）與一次顯示的欄數 |
| `ShowTrail` | 是否畫出記憶軌跡中的整段路線 |
| `ClearTrail()` | 忘掉所有分支的記憶軌跡，只留目前的選取路徑 |
| `ColumnWidth` / `ColumnSpacing` / `InactiveSelectionBackColor` / `TrailBackColor` / `PathSeparator` | 外觀設定 |
| `SelectedPathChanged` | 選取路徑改變後觸發 |
| `NodeActivated` | 在節點上雙擊或在葉節點上按 Enter 時觸發 |
| `DrawNode` | 繪製每一列節點之前觸發，用於自繪 |
| `ColumnWidthChanged` | `ColumnWidth` 改變後觸發，供資料繫結使用 |

**`MenuTree` / `MenuNode` / `SiblingCellSyntax`**（`CascadeControl.Models`）

| 成員 | 說明 |
|---|---|
| `MenuTree.TopLevelNodes` | 第一層節點集合 |
| `MenuTree.FromSubtree(rootNode)` | 以指定節點往下複製成一棵新子樹 |
| `MenuTree.AddPath(nodeNames)` | 加入一條路徑，回傳終點節點 |
| `MenuTree.ExpandAndAddPaths(cells)` | 加入一整列（格子可含 `\|`），回傳各條路徑的終點節點 |
| `MenuTree.GetLeaves()` / `Height` / `IsEmpty` | 查詢葉節點、高度與是否為空 |
| `MenuTree.FromRows(rows, out warnings)` / `ToRows(style)` | 與二維字串陣列互轉 |
| `SiblingCellSyntax.SIBLING_SEPARATOR` / `MAX_EXPANDED_PATHS` | `\|` 與展開上限 65,535 |
| `SiblingCellSyntax.JoinSiblings(names)` | 把多個同層名稱串成一格 |
| `MenuNode.Name` / `DisplayText` | 原始名稱與欄位顯示文字 |
| `MenuNode.FullPath` / `FullPathText` | 由第一層到本節點的節點序列與路徑字串 |
| `MenuNode.Parent` / `Depth` / `SiblingNumber` / `IsLeaf` | 位置資訊 |
| `MenuNode.CopySubtree([newName])` / `MergeSubtreeFrom(source)` | 複製與合併子樹 |
| `MenuNode.InsertSiblingBefore(source, [newName])` / `InsertSiblingAfter(source, [newName])` | 同層上方／下方插入兄弟節點 |
| `MenuNode.RemoveFromParent()` / `Rename(newName)` | 刪除與重新命名節點 |

**`MenuTreeCsvText`**（`CascadeControl.Services`，純文字、不碰檔案）

| 成員 | 說明 |
|---|---|
| `Parse(csvText, out warnings)` | 解析文字，表頭可省略，不丟例外 |
| `Format(tree, [style])` | 轉成文字，`style` 預設為 `Expanded` |
| `NormalizeRows(rows, [style,] out warnings)` | 整理並正規化資料列 |
| `CreateHeader(columnCount)` | 產生 `Level1…LevelN` 表頭 |

**`MenuTreeCsvFile`**（`CascadeControl.Services`，檔案）

| 成員 | 說明 |
|---|---|
| `Load(filePath, out warnings)` | 讀取 CSV，不丟例外 |
| `Save(tree, filePath, [style])` | 寫出 CSV（預設展開），目錄不存在時會自動建立 |

---

## 注意事項

- **資料改了要呼叫 `RefreshColumns` 或重新 `SetTree` / `SetTopLevelNodes`。** 已經顯示中的欄位不會自動反映 `CascadeNode.Children` 或你自己的模型的變動；`RefreshColumns` 會保留（或改選指定的）選取路徑，重新設定則會清空選取與記憶軌跡。
- **`SetTree`、`SetTopLevelNodes`、`Clear` 也會觸發 `SelectedPathChanged`**（未選取的狀態）。在表單建構式裡呼叫時，事件處理常式也會跑一次。
- **按 ← 會改變選取**，`SelectedPathChanged` 跟著觸發。依賴「← 只移動焦點」的舊程式要留意。
- **`DrawNode` 和 `ColumnWidthChanged` 不在屬性視窗裡**，需要時請在程式碼中訂閱。
- **更新 dll 時要連同 `CascadeControl.xml` 一起換**，否則 IntelliSense 顯示的會是舊的說明。
- 控制項用 `WS_EX_COMPOSITED` 做雙緩衝，避免改選時閃爍。一般大小的選單感覺不出額外負擔；
  如果單欄有上千項，請留意重繪效能。
