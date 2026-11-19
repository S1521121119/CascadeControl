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
第 4 欄是 `Jog` 的子節點。選到任何一項再按 Enter 都會觸發 `NodeActivated`；進入下一層用 →。

- **需求**：.NET Framework 4.8、Windows Forms，不需要第三方套件。
- **命名空間**：`CascadeControl.Views`（控制項）、`CascadeControl.Trees`（選單樹與勾選）、`CascadeControl.Trees.Csv`（樹的文字與 CSV 讀寫）、`CascadeControl.Graphs`／`CascadeControl.Graphs.Csv`（有向圖與其 CSV 讀寫）、`CascadeControl.Common`（`ICascadeNode`、`|` 語法等共用部分）。

**目錄**：[快速開始](#快速開始) · [常見任務](#常見任務) · [CSV 格式](#csv-格式) · [操作方式](#操作方式) · [勾選](#勾選) · [API 速查](#api-速查) · [注意事項](#注意事項)

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
using CascadeControl.Trees;
using CascadeControl.Trees.Csv;
using CascadeControl.Views;

public MainForm()
{
    InitializeComponent();

    MenuTree menu = MenuTreeCsvFile.Load("menu.csv", out IList<string> warnings);
    foreach (var warning in warnings) Debug.WriteLine(warning);

    cascadeView1.NodeActivated += CascadeView1_NodeActivated;
    cascadeView1.SetTree(menu);
}

// 使用者在選取的節點上雙擊或按 Enter 時觸發，不論是不是終點。
private void CascadeView1_NodeActivated(object sender, CascadeNodeEventArgs e)
{
    if (!e.IsLeaf) return;   // 只執行終點
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
menu.ExpandAndAddPaths("IO", "Input", MultiNameCellSyntax.JoinNames(inputNames)); // 名稱清單來自程式時
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
- 畫面右緣（`Fixed` 捲動時、`Fill`、`Sliding` 都一樣）：操作欄裡**有任何一項有下一層**時，右邊一律預留一欄，即使選到的是終點、下一欄還沒長出來也留白，所以在同一欄上下改選時畫面不會左右跳；操作欄**全是終點**時不留白，右緣就是操作欄本身。不論哪一種，操作欄都不會被推出左邊：只顯示一欄時，看到的是操作欄而不是它的下一欄。

```
操作欄全是終點：不留白                     操作欄有節點：選到終點也留白
| ... | Node A   | | Leaf A   |           | ... | Node A   | |          |
| ... |[Leaf B ] | |[Leaf B  ]|           | ... |[Leaf B  ]| |          |
```
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
| 焦點欄的選取底色、文字色 | `SelectionBackColor`、`SelectionForeColor`，預設為系統選取色 |
| 非焦點欄的選取底色 | `InactiveSelectionBackColor`，預設為調淡的 `SelectionBackColor` |
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
  程式中組這種字串可以用 `MultiNameCellSyntax.JoinNames`（`CascadeControl.Common`）。
- **名稱**：前後空白會自動去除；**區分大小寫**，所以 `Reboot` 和 `reboot` 是兩個不同的項目。
- **重複與延伸**：完全相同的路徑會合併。`A,B` 和 `A,B,C` 同時存在時，`B` 會變成中間節點，不算錯誤。
- **顯示文字**：`MenuNode.DisplayText` 在畫面上顯示成 `<01>Name`，有子節點時再加上 ` [+]`；序號（`SiblingNumber`，由 1 起算）是它在同一層中第一次出現的順序。顯示文字只用在欄位上，路徑與記錄一律使用 `Name`。

以下情況不會丟例外，而是寫進 `warnings`：

- 中間缺一層，例如 `A,,C`：空欄之後的欄位會被忽略。
- `|` 用法錯誤，例如 `A||B`、`|A`、`A|`，或某一段只有空白：整列跳過。
- 單列展開超過 65,535 條（`MultiNameCellSyntax.MAX_EXPANDED_ROWS`）：整列跳過。
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
| `Compact` | **相鄰**而且**子樹完全相同**的兄弟節點合併成同一格；遇到有下一層的兄弟就切成新的一列 | `A,x,` / `A,m|n,1|2` |
| `Outline` | **每個中間節點一列**，列出它底下的全部子節點；子樹完全相同的兄弟（不必相鄰）共用一列 | `A,x|m|n,` / `A,m|n,1|2` |

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
| Enter | 在選取的節點上觸發 `NodeActivated`，不論是不是終點（用 `e.IsLeaf` 判斷）；沒有選取時不觸發 |
| Esc | 有多選時取消多選；沒有時清除選取（記憶軌跡保留），焦點回到第一欄 |
| 滑鼠點選 | 點不同的項目等同改選；點路徑或記憶軌跡中的同一項，等同連按 ← 或 → 到那一欄；有多選時一併取消 |
| 雙擊 | 在操作欄的節點上觸發 `NodeActivated` |
| Ctrl＋點選 | 在同一欄內多選：切換那一個節點，不改選取、不進入節點（第一次 Ctrl＋點選時，那一欄原本選取的節點也算在內） |
| 拖曳框選 | 按住 Ctrl 拖曳，或從欄位空白處（最後一列下方）開始拖曳：拖曳時畫出矩形框，多選框到的那幾列；Ctrl 時加進原本的多選 |
| Ctrl+A | 多選操作欄的全部節點 |
| Ctrl+C | 把多選的節點（沒有多選時是選取的節點）連同子孫複製到 Windows 剪貼簿 |
| Ctrl+Shift+C | 複製路徑：把多選的節點（沒有多選時是選取的節點）從第一層到它的路徑，以 CSV 文字（每個節點一列，含表頭，不含子孫）複製到剪貼簿 |
| Ctrl+Z | 復原，見[復原與重做](#復原與重做) |
| Ctrl+Y、Ctrl+Shift+Z | 重做 |
| 右鍵 | 不改選取，開啟快顯功能表，見[編輯](#編輯) |

- 鍵盤焦點固定在目前操作的那一欄（`ActiveColumnIndex`，最深選取項所在的欄），點在其他欄的空白處後，焦點會回到這一欄。
- 只要焦點在控制項的欄位上，←、→、Enter、Esc 一律由控制項處理。所以表單的 `CancelButton` 在這時候不會被 Esc 觸發。
- 多選只用來決定複製、剪下、刪除的對象；多選的節點以淺黃色標出，方向鍵改選、一般點選、Esc 都會取消多選。
- `Fixed` 模式下欄位多到放不下時會出現水平捲軸，並自動捲到操作欄，操作欄有任何一項有下一層時，讓它的右邊剛好留得下一欄；`Fill` 與 `Sliding` 的行為見[欄寬與顯示欄數](#欄寬與顯示欄數)。

---

## 編輯

`ReadOnly`（預設 true）決定右鍵功能表與快捷鍵能做什麼。編輯只對 `SetTree` 顯示的 `MenuTree` 有作用，`SetTopLevelNodes` 顯示的資料一律唯讀。

| 功能表項目 | 快捷鍵 | `ReadOnly = true` | `ReadOnly = false` |
|---|---|---|---|
| 復原 / 重做 | Ctrl+Z / Ctrl+Y | —（`CascadeCheckboxView` 可以復原勾選） | 見[復原與重做](#復原與重做)；兩個功能表都有 |
| 插入 ▸ 之前 / 之後 | Ctrl+I（之後） | — | 在節點前面／後面插入一列編輯框，按 Enter 建立新節點 |
| 建立子節點 | Ctrl+N | — | 只對葉節點：長出下一欄並開啟編輯框，按 Enter 建立第一個子節點；已經有子節點時停用，請在子節點那一欄插入 |
| 重新命名 | F2 | — | 在節點上開啟編輯框，文字全選 |
| 複製 | Ctrl+C | ✓ | ✓ |
| 複製(路徑) | Ctrl+Shift+C | ✓ | ✓（純文字，例如路徑 Motor > Axis Y > Jog 多選 Forward、Backward 時是 `Level1,Level2,Level3,Level4` / `Motor,Axis Y,Jog,Forward` / `Motor,Axis Y,Jog,Backward`；不能貼回控制項） |
| 剪下 | Ctrl+X | — | 複製到剪貼簿後從樹中移除 |
| 貼上 ▸ 之前 / 之後 | Ctrl+V（之後） | — | 貼在節點前面／後面，維持複製時的順序 |
| 貼上 ▸ 作為子節點 | | — | 只對葉節點：貼成它的子節點（長出下一欄），維持複製時的順序 |
| 刪除 | Delete | — | 確認後刪除（連同子孫） |
| 欄位空白處：插入 / 貼上 | | — | 加在那一欄的最後；`ReadOnly = false` 時空樹也會留一個空欄，可以在這裡插入第一個節點 |
| 欄位空白處：全選 | Ctrl+A | ✓ | ✓ |

- 多選時功能表只對複製、剪下、刪除有作用（右鍵點在多選的節點上，對象就是全部多選的節點）；插入、建立子節點、重新命名、貼上需要單一個對象。
- 多選的節點以淺黃色標出（`MultiSelectBackColor`），和選取路徑的藍色分開。
- 編輯框：Enter 套用、Esc 取消；焦點離開（Tab、點了別的地方）也算套用。名稱空白當成取消，含有 `|` 時提示並繼續編輯。編輯中點欄位，那一下只用來結束編輯。
- 同一層已有同名節點時（插入、改名、貼上），新名稱自動加上後綴，例如 `Axis X - 2`。
- 剪貼簿：Copy / Cut 在 Windows 剪貼簿放兩種格式：`CascadeView.CLIPBOARD_FORMAT`（給 Paste 用）與 CSV 純文字（可以直接貼到記事本或 Excel）。Paste 只認前者。
- 程式也可以直接呼叫：`Copy()`、`Cut()`、`Paste(before)`、`PasteAsChild()`、`Delete()`、`BeginInsert(before)`、`BeginCreateChild()`、`BeginRename()`、`EndEdit(commit)`，對象是多選的節點或 `SelectedNode`。
- 每次改完樹都會觸發 `TreeEdited`（`e.Action`、`e.Nodes`），這時欄位與選取已經更新好；復原、重做改到樹時也會觸發（`e.Action` 是 `Undo` / `Redo`）。

### 復原與重做

插入、建立子節點、重新命名、貼上、剪下、刪除，每一個改到樹的操作都記進 `History`（`MenuHistory`），可以用 Ctrl+Z 復原、Ctrl+Y 或 Ctrl+Shift+Z 重做，右鍵功能表最前面也有「復原」、「重做」。複製不改樹，不算一步。

- 最多保留 50 步（`History.Capacity`，預設 `MenuHistory.DEFAULT_CAPACITY`），超過時丟掉最舊的；做了新的操作，重做的步驟就清掉。
- 每一步只記差異，不存整棵樹的快照：插入與移除記父節點、位置和節點本身，改名記新舊名稱。一步佔的記憶體只和改到的節點數成正比，被刪除的子樹由那一步留著，直到它被擠出歷史為止。
- 復原刪除時接回的是**原本的節點物件**，不是複本，所以之後的勾選、選取都對得上。
- 復原或重做之後，選取跳到那一步的位置：回到樹裡的節點（不只一個時全部多選），或被拿掉的節點原位置的下一個兄弟（沒有就前一個，一個都不剩時是父節點）。
- `ReadOnly = true` 時改到樹的步驟不能復原或重做（`CanUndo` / `CanRedo` 為 false）。
- `SetTree`、`SetTopLevelNodes`、`Clear` 會清空歷史。在程式裡直接改 `MenuNode` 之後請呼叫 `History.Clear()`；萬一沒呼叫，復原時發現樹對不上也會自動清空，不會把樹改壞。
- 程式也可以直接呼叫 `Undo()`、`Redo()`。

---

## 勾選

`CascadeCheckboxView` 是每一列前面多一個勾選框的 `CascadeView`，用來在樹上**勾選多個節點**，再一次輸出勾選的部分。
選取（目前走到哪裡）和勾選（選了哪些）是兩回事：勾選狀態存在 `MenuCheckSet`，不存在節點上，所以 `MenuNode` 與 `MenuTree` 都不必改。

```csharp
// 用法和 CascadeView 一樣；SetTree 會自動建立一份針對這棵樹的 MenuCheckSet。
cascadeCheckboxView1.SetTree(menu);
cascadeCheckboxView1.CheckedChanged += (s, e) =>
    Debug.WriteLine($"{e.Reason}：+{e.Added.Count} / -{e.Removed.Count}");

MenuCheckSet checks = cascadeCheckboxView1.CheckSet;

// 取得勾選的節點（依樹的順序）
foreach (MenuNode node in checks.CheckedNodes) Debug.WriteLine(node.FullPathText);

// 輸出：先變成一棵只含勾選節點的樹，再用既有的方式寫出
MenuTree picked = checks.ToTree();
string[][] rows = picked.ToRows(MenuRowStyle.Compact);
MenuTreeCsvFile.Save(picked, "picked.csv", MenuRowStyle.Compact);

// 還原：把先前存下來的結果讀回來，套回同一棵樹
MenuTree saved = MenuTreeCsvFile.Load("picked.csv", out IList<string> loadWarnings);
checks.Apply(saved, out IList<string> applyWarnings);   // 原樹裡找不到的路徑會寫進 applyWarnings
```

### 勾選的規則

勾選集合永遠**向上封閉**：一個節點被勾選，它的所有祖先就一定也被勾選。

| 動作 | 結果 |
|---|---|
| 勾選一個節點 | 自動補勾它的所有祖先；兄弟與子孫不動 |
| 取消一個節點 | 連同它的所有子孫一起取消；祖先不動 |
| 只勾中間節點、不勾它的子節點 | 可以，代表「輸出到這一層為止」 |

勾選框有三種外觀：☐ 未勾選、☑ 自己和所有子孫都勾了、▣ 勾了但子孫沒有全勾。
第三種只是提示，狀態仍然是已勾選（`IsChecked` 為 true、`IsFullyChecked` 為 false）。

### 勾選的操作方式

| 按鍵 / 動作 | 行為 |
|---|---|
| 點勾選框 | 只切換那個節點的勾選，不改選取（`Sliding` 時畫面也不會滑動） |
| Shift＋點勾選框 | 連同所有子節點一起勾選；已經全勾時改為取消 |
| 點文字 | 只選取，不切換 |
| Space | 切換目前選取節點的勾選 |
| Shift+Space | 目前選取的節點連同所有子節點一起勾選；已經全勾時改為取消（和 Shift＋點勾選框相同） |
| Ctrl+Z | 復原；勾選與樹的編輯照發生的順序退回，之後選取會跳到那一步針對的節點 |
| Ctrl+Y、Ctrl+Shift+Z | 重做 |
| 右鍵點節點 | 不改選取，節點功能表最前面多一項「勾選(含所有子節點)」，後面接[編輯](#編輯)的項目（含復原、重做） |
| 右鍵點欄位空白處 | 不改選取，欄位功能表最前面多三項（對象是那一欄全部）：「全部勾選」（子節點不動）、「全部勾選(含子節點)」、「全部取消」（連同子節點） |

- 勾選和樹的編輯記在同一份 `History`（`CheckSet.History` 就是控制項的 `History`），排在同一條時間軸上，規則見[復原與重做](#復原與重做)。每一次勾選操作算一步（功能表的一個項目也是一步），沒有造成任何改變的操作不算一步。
- 唯讀時也可以復原、重做勾選；下一步是改到樹的步驟時就停在那裡。
- 剪下、刪除節點時，被移除節點的勾選一起取消，和移除算同一步，所以復原刪除時勾選也會回來。
- 欄位功能表要點在最後一列下方的空白處；欄位排滿、沒有空白處時開不了。整棵樹的 `CheckAll()` / `UncheckAll()` 沒有放進功能表，需要時自己呼叫。
- 勾選項目在對象有勾選框時一律顯示（`CheckSet` 為 null 時就不出現），編輯項目依 `ReadOnly` 決定；要加自己的項目，加到 `NodeContextMenu` 或 `ColumnContextMenu` 後面，用 `ContextMenuNode` 取得功能表針對的節點（欄位功能表是那一欄的第一個節點）。
- 焦點在欄位上時輸入法會被關掉：中文輸入法開著的話，Space 會先被輸入法收走。
- 列高至少放得下勾選框，字型很小時不會跟著縮到比勾選框還矮。

### 樹被編輯之後

透過控制項的[編輯](#編輯)功能改樹時，控制項會自己處理勾選，並保留復原歷史，不必另外處理。
在程式裡直接改 `MenuNode`（插入、刪除、改名）之後，除了 `RefreshColumns`，還要呼叫 `CheckSet.Reconcile()`：
它會丟掉已經不在樹裡的節點、補勾缺少的祖先，並清除復原歷史（這些改動沒有記進歷史，舊步驟可能對不上）。新加入的節點預設不勾選。

```csharp
node.RemoveFromParent();
cascadeCheckboxView1.CheckSet.Reconcile();
cascadeCheckboxView1.RefreshColumns();
```

`SetTree` 每次都會換上一份全新的 `MenuCheckSet`；自己設定 `CheckSet` 時，它的 `History` 會換成控制項的 `History`，並清空歷史。想保留勾選（例如重新載入同一棵樹），先用 `ToTree()` 存下來，`SetTree` 之後再 `Apply` 回去。

---

## API 速查

**`CascadeView`**（`CascadeControl.Views`，繼承自 `Panel`，實作 `ICascadeView`）

| 成員 | 說明 |
|---|---|
| `State` | 目前綁定的共享 `CascadeState`（預設各自持有一份，不可為 null）；讓多個控制項指派同一份即可同步選取與記憶 |
| `SetTree(tree)` / `SetTopLevelNodes(nodes)` / `Clear()` | 以選單樹或第一層節點集合重設，或清空 |
| `RefreshColumns([selectPath])` | 底層節點結構異動後重新整理欄位清單並選取指定路徑 |
| `TrySelectNode(node)` / `TrySelectPath(path)` | 選取指定節點或依序選取一條路徑 |
| `ReadOnly` | 是否唯讀（預設 true）；false 時可以從功能表與快捷鍵編輯，見[編輯](#編輯) |
| `Copy()` / `Cut()` / `Paste(before)` / `PasteAsChild()` / `Delete()` | 透過 Windows 剪貼簿複製、剪下、貼上（`PasteAsChild` 只對葉節點），或確認後刪除；對象是多選的節點或 `SelectedNode` |
| `BeginInsert(before)` / `BeginCreateChild()` / `BeginRename()` / `EndEdit(commit)` / `IsEditing` | 開啟、結束就地編輯框；`BeginCreateChild` 只對葉節點 |
| `MultiSelectedNodes` / `MultiSelectedColumnIndex` / `SelectAllInColumn(i)` / `ClearMultiSelection()` | 同一欄內的多選 |
| `Undo()` / `Redo()` / `CanUndo` / `CanRedo` / `History` | 復原與重做；`History` 是記錄步驟的 `MenuHistory`，見[復原與重做](#復原與重做) |
| `NodeContextMenu` / `ColumnContextMenu` | 右鍵點節點／欄位空白處時開啟的功能表，內建的復原、重做與編輯項目依 `ReadOnly` 顯示，可以加自己的項目 |
| `ContextMenuNode` / `ContextMenuNodes` / `ContextMenuColumnIndex` | 功能表針對的節點（被點的節點，或那一欄的第一個節點）、要操作的全部節點、哪一欄 |
| `FocusActiveColumn()` | 把焦點放到目前操作的那一欄；控制項本身取得焦點（`Focus()`、設成 `ActiveControl`）時也會自動轉交 |
| `SelectedNode` / `SelectedPath` / `SelectedPathText` | 目前的選取（不含記憶軌跡） |
| `IsLeafSelected` | 是否已選到終點 |
| `ActiveColumnIndex` | 目前操作中的欄位索引（0-based） |
| `ColumnLayoutMode` / `DisplayColumnCount` | 欄位排版模式（`Fixed`、`Fill`、`Sliding`）與一次顯示的欄數 |
| `ShowTrail` | 是否畫出記憶軌跡中的整段路線 |
| `StepBack()` / `StepForward()` | 和按 ← / → 相同的退回、進入一層（走記憶軌跡），不移動鍵盤焦點 |
| `ClearTrail()` | 忘掉所有分支的記憶軌跡，只留目前的選取路徑 |
| `ColumnWidth` / `ColumnSpacing` / `SelectionBackColor` / `SelectionForeColor` / `InactiveSelectionBackColor` / `TrailBackColor` / `MultiSelectBackColor` / `PathSeparator` | 外觀設定 |
| `SelectedPathChanged` | 選取路徑改變後觸發 |
| `NodeActivated` | 在選取的節點上雙擊或按 Enter 時觸發，不論是不是終點 |
| `NodeMouseClick` | 用左鍵或右鍵點了某個節點之後觸發；右鍵不改選取 |
| `TreeEdited` | 使用者透過功能表、快捷鍵或編輯框改了樹，或復原、重做改到樹之後觸發 |
| `MultiSelectionChanged` | `MultiSelectedNodes` 改變後觸發 |
| `DrawNode` | 繪製每一列節點之前觸發，用於自繪 |
| `ColumnWidthChanged` | `ColumnWidth` 改變後觸發，供資料繫結使用 |
| `InvalidateNodes()` | 讓所有欄位重畫，不重建欄位；自繪的外觀取決於外部狀態、而那份狀態改變時呼叫 |

給衍生類別用的 protected 成員：`CreateDefaultState()`、`OnStateChanged(oldState, newState)`、`DrawDefaultNode(e)`（可覆寫，畫預設外觀）、`DrawDefaultNode(e, textBounds)`（把文字畫在指定範圍）、
`IsColumnFocused`、`MinimumItemHeight`（可覆寫）、`OnNodeMouseClick`、
`SelectsOnLeftClick(node, bounds, location)`（可覆寫，回傳 false 的位置左鍵不改選取，例如勾選框）、
`OnColumnBlankMouseClick(columnIndex, nodes, button, screenLocation)`（可覆寫，點欄位空白處時呼叫）、
`OnContextMenuOpening(menu)`（可覆寫，內建功能表開啟前設定自己項目的 `Available`）、`OnTreeEdited`、`OnMultiSelectionChanged`；`SetTree` 是 virtual。

**`CascadeCheckboxView`**（`CascadeControl.Views`，繼承自 `CascadeView`，實作 `ICascadeCheckableView`）

| 成員 | 說明 |
|---|---|
| `CheckSet` | 目前顯示的勾選狀態（存在預設的 `CascadeCheckableState` 裡）；`SetTree` 會自動換上新的，設為 null 就不顯示勾選框 |
| `CheckedChanged` | `CheckSet` 的勾選狀態改變後觸發 |
| `GetCheckBoxBounds(rowBounds)` | 勾選框在某一列裡的位置 |

**`CascadeTreeImageView`**（`CascadeControl.Views`，繼承自 `ScrollableControl`）

把 `State` 的選單樹用 `TreeDrawer` 畫出來，跟著選取與勾選自動重畫；自己管捲軸，可以直接 `Dock = Fill`。可呼叫 `SetTree` 獨立運作，或共享 `CascadeView`／`CascadeCheckboxView` 的 `State` 同步顯示。

```csharp
// 與 CascadeView 或 CascadeCheckboxView 共享狀態（選取、記憶、PathSeparator 與勾選會自動同步）
treeImageView1.State = cascadeView1.State;
treeImageView1.NodeMouseClick += (s, e) =>
{
	Debug.WriteLine(e.PathText);              // 例如 Motor => Axis X => Jog
	treeImageView1.TrySelectPath(e.Path);     // 透過共享 State 讓兩邊一起跳到該節點
};

// 或脫離 CascadeView 獨立使用
var standalone = new CascadeTreeImageView();
standalone.SetTree(menu);
```

| 成員 | 說明 |
|---|---|
| `State` | 目前綁定的共享 `CascadeState`（預設各自持有一份，不可為 null）；綁定 `CascadeCheckableState` 時一併顯示勾選狀態 |
| `Tree` / `SetTree(tree)` / `Clear()` | 讀取、設定或清空目前顯示的選單樹 |
| `SelectedNode` / `SelectedPath` / `SelectedPathText` / `IsLeafSelected` | 目前的選取（不含記憶軌跡） |
| `TrySelectNode(node)` / `TrySelectPath(path)` / `StepBack()` / `StepForward()` | 選取節點或路徑，或沿記憶軌跡退回／進入一層 |
| `PathSeparator` | 層級分隔字串（與共享同一份 `State` 的檢視同步） |
| `DrawScope` / `UncheckedDisplay` | 畫整棵樹或選取的子樹；沒勾的節點變淡或隱藏（`State` 為 `CascadeCheckableState` 時有效） |
| `SizeMode` | `ActualSize` 原始尺寸，放不下時出現捲軸；`Zoom` 維持長寬比例縮小到整張看得到（不放大） |
| `Redraw()` | 重畫；在外部直接修改節點而未呼叫 `RefreshColumns` 或 `State.Refresh` 時呼叫 |
| `GetNodeAt(clientLocation)` | 控制項上某一點的節點，已考慮捲動與縮放 |
| `GetExpanderAt(clientLocation)` | 控制項上某一點落在哪個節點的展開鈕（[+]／[-]）上 |
| `IsExpanded(node)` / `SetExpanded(node, expanded)` | 節點是否展開／展開或收合並重畫。左鍵點 [+] 展開、點 [-] 收合（不觸發 `NodeMouseClick`）；沒點過的節點有子節點被勾選才展開，沒有勾選資訊時一律收合；換樹後重置 |
| `SelectedPathChanged` | 選取路徑改變後觸發 |
| `NodeMouseClick` / `NodeMouseDoubleClick` | 點了圖上的節點；參數有 `Node`、`Path`、`PathText`（以 `PathSeparator` 串接）、`Button` |
| `NodeActivated` | 在選取的節點上按 Enter；參數 `CascadeTreeImageNodeEventArgs`（`Node`、`Path`、`PathText`，滑鼠事件的參數由它衍生） |
| `DrawSettingsChanged` | 三個顯示設定改變後觸發，包含從右鍵功能表切換 |
| `TreeContextMenu` | 右鍵功能表，含範圍、未勾選節點（`State` 為 `CascadeCheckableState` 時顯示）、顯示（原始尺寸／自動縮放） |

焦點在樹圖上時可以用鍵盤操作。按鍵改的是 `State` 的選取，所以「進出記住位置」的記憶和共享同一份狀態的其他檢視一致：

| 按鍵 | 動作 |
|---|---|
| ↑ / ↓ | 在圖上看得到的同一層節點之間移動，可以跨父節點；被收合或隱藏的節點跳過，到頭不動 |
| ← | 退回上一層（同 `StepBack()`） |
| → | 進入下一層，有記憶走記憶，否則第一個子節點（同 `StepForward()`）；收合 [+] 的節點不動作，要先展開 |
| 空白 | 展開或收合選取的節點 |
| Enter | 在選取的節點上觸發 `NodeActivated`（參數 `Node`、`Path`、`PathText`），用途和雙擊相同，由訂閱端決定 |

原始尺寸下，移動後會捲動到選取的節點。

**`CascadeState` / `CascadeCheckableState`**（`CascadeControl.Views`，不相依 WinForms）

多個檢視控制項共享的資料、選取與勾選狀態。`CascadeView` 與 `CascadeTreeImageView` 預設建立 `CascadeState`，`CascadeCheckboxView` 預設建立 `CascadeCheckableState`。

| 成員 | 說明 |
|---|---|
| `Tree` / `TopLevelNodes` / `SetTree(tree)` / `SetTopLevelNodes(nodes)` / `Refresh([selectPath])` / `Clear()` | 管理樹與頂層節點資料來源；`Refresh` 會通知所有綁定的檢視重新同步 |
| `SelectedNode` / `SelectedPath` / `SelectedPathText` / `IsLeafSelected` / `ActiveColumnIndex` / `PathSeparator` | 選取狀態查詢與層級分隔字串 |
| `TrySelectNode(node)` / `TrySelectPath(path)` / `StepBack()` / `StepForward()` / `ClearTrail()` | 導覽與記憶軌跡操作 |
| `SelectedPathChanged` | 選取路徑改變或資料重設／重新整理後觸發 |
| `CascadeCheckableState.CheckSet` / `CheckedChanged` / `CheckSetChanged` | 勾選狀態與事件；`SetTree` 會自動建立新的 `MenuCheckSet`，切換 `CheckSet` 會觸發 `CheckSetChanged` 通知綁定的檢視更新 |

**`ICascadeView` / `ICascadeCheckableView`**（`CascadeControl.Views`，不相依 WinForms）

導覽與勾選能力的檢視介面，不含編輯與復原成員。`CascadeView` 實作 `ICascadeView`，`CascadeCheckboxView` 實作 `ICascadeCheckableView`。

`CascadeView.Tree` 是 `SetTree` 設定的樹；用 `SetTopLevelNodes` 顯示其他資料或 `Clear` 後為 null。
`TreeDrawer.RenderImage(...)` 和 `Render` 相同，另外帶回每個節點方塊與展開鈕的位置（`TreeImage.Nodes`、`GetNodeAt`、`GetExpanderAt`）。
三個繪製方法都有選用參數 `isExpanded`：回傳 false 的節點畫 [+]、不畫子節點；null 時全部展開（[-]）。

**`MenuCheckSet`**（`CascadeControl.Trees`，不相依 WinForms）

| 成員 | 說明 |
|---|---|
| `new MenuCheckSet(tree)` / `Tree` | 針對一棵樹建立一份空的勾選 |
| `IsChecked(node)` / `IsFullyChecked(node)` / `Contains(node)` | 是否勾選、是否連同子孫全勾、節點是否屬於這棵樹 |
| `CheckedNodes` / `Count` | 依樹的順序列舉勾選的節點；勾選的節點數 |
| `Check(node)` / `Uncheck(node)` / `Toggle(node)` | 勾選（補祖先）、取消（連同子孫）、切換 |
| `CheckSubtree(node)` | 勾選節點與它的全部子孫（補祖先） |
| `CheckSiblings(node)` | 勾選同一層的全部節點（補祖先），它們的子孫不動 |
| `CheckSiblingSubtrees(node)` | 勾選同一層的全部節點與它們的子孫（補祖先） |
| `UncheckSiblings(node)` | 取消同一層的全部節點與它們的子孫 |
| `CheckAll()` / `UncheckAll()` | 整棵樹 |
| `ToTree()` | 產生只含勾選節點的新樹，交給 `ToRows`、`MenuTreeCsvText.Format`、`MenuTreeCsvFile.Save` 輸出 |
| `Apply(subset, out warnings)` | 把勾選狀態換成 `subset` 裡有的節點（依名稱路徑對應），算一個復原步驟 |
| `History` | 勾選的步驟記在哪一份 `MenuHistory`；預設是自己的一份，交給 `CascadeCheckboxView` 後是控制項的 `History` |
| `Reconcile()` | 在程式裡直接改了樹的結構後呼叫；會清除 `History` |
| `Changed` | 勾選狀態改變後觸發，參數有 `Added`、`Removed`、`Target`、`Reason` |

異動方法都回傳「有沒有實際改變」；傳入不屬於這棵樹的節點會丟 `ArgumentException`。

**`MenuHistory`**（`CascadeControl.Trees`，不相依 WinForms）

| 成員 | 說明 |
|---|---|
| `Undo()` / `Redo()` | 復原、重做一步，回傳 `MenuHistoryResult`（`Focus`：之後要選的節點、`Nodes`：插入／移除／改名的節點、`TreeChanged`）；沒有步驟，或樹已經對不上（這時整份清空）時回傳 null |
| `CanUndo` / `CanRedo` / `UndoCount` / `RedoCount` | 可以復原、重做的步驟 |
| `Capacity` / `DEFAULT_CAPACITY` | 保留的步驟數（預設 50），調小時立刻丟掉最舊的 |
| `Clear()` | 清除歷史，勾選與樹都不變 |

**`MenuTree` / `MenuNode`**（`CascadeControl.Trees`）與 **`MultiNameCellSyntax`**（`CascadeControl.Common`）

| 成員 | 說明 |
|---|---|
| `MenuTree.TopLevelNodes` | 第一層節點集合 |
| `MenuTree.FromSubtree(rootNode)` | 以指定節點往下複製成一棵新子樹 |
| `MenuTree.AddPath(nodeNames)` | 加入一條路徑，回傳終點節點 |
| `MenuTree.ExpandAndAddPaths(cells)` | 加入一整列（格子可含 `|`），回傳各條路徑的終點節點 |
| `MenuTree.GetLeaves()` / `Height` / `IsEmpty` | 查詢葉節點、高度與是否為空 |
| `MenuTree.FromRows(rows, out warnings)` / `ToRows(style)` | 與二維字串陣列互轉 |
| `MultiNameCellSyntax.NAME_SEPARATOR` / `MAX_EXPANDED_ROWS` | `|` 與展開上限 65,535 |
| `MultiNameCellSyntax.JoinNames(names)` | 把多個名稱串成一格 |
| `MenuNode.Name` / `DisplayText` | 原始名稱與欄位顯示文字 |
| `MenuNode.FullPath` / `FullPathText` | 由第一層到本節點的節點序列與路徑字串 |
| `MenuNode.Parent` / `Depth` / `SiblingNumber` / `IsLeaf` | 位置資訊 |
| `MenuNode.CopySubtree([newName])` / `MergeSubtreeFrom(source)` | 複製與合併子樹 |
| `MenuNode.InsertSiblingBefore(source, [newName])` / `InsertSiblingAfter(source, [newName])` | 同層上方／下方插入兄弟節點 |
| `MenuNode.RemoveFromParent()` / `Rename(newName)` | 刪除與重新命名節點 |

**`MenuTreeCsvText`**（`CascadeControl.Trees.Csv`，純文字、不碰檔案）

| 成員 | 說明 |
|---|---|
| `Parse(csvText, out warnings)` | 解析文字，表頭可省略，不丟例外 |
| `Format(tree, [style])` | 轉成文字，`style` 預設為 `Expanded` |
| `NormalizeRows(rows, [style,] out warnings)` | 整理並正規化資料列 |
| `CreateHeader(columnCount)` | 產生 `Level1…LevelN` 表頭 |

**`MenuTreeCsvFile`**（`CascadeControl.Trees.Csv`，檔案）

| 成員 | 說明 |
|---|---|
| `Load(filePath, out warnings)` | 讀取 CSV，不丟例外 |
| `Save(tree, filePath, [style])` | 寫出 CSV（預設展開），目錄不存在時會自動建立 |

**`MenuGraph` / `MenuVertex` / `MenuEdge` / `MenuGraphNode`**（`CascadeControl.Graphs`）

有向圖：頂點名稱全圖唯一，同名就是同一個頂點，多個前驅可以共用同一個頂點；允許自我迴圈與環。CSV 固定兩欄 `From,To`，每列是一組有向邊，兩格都可以用 `|` 列舉多個頂點。

```csv
From,To
Main,System|Motor
System,Info|Status
Info|Status,Version|Uptime
```

| 成員 | 說明 |
|---|---|
| `MenuGraph.GetOrAddVertex(name)` / `TryGetVertex` / `ContainsVertex` / `RemoveVertex(vertex)` | 頂點的增刪查 |
| `MenuGraph.AddEdge(from, to)` / `RemoveEdge(from, to)` / `IsAdjacent(from, to)` | 邊的增刪查；重複的邊會合併 |
| `MenuGraph.ExpandAndAddEdges(fromCell, toCell)` | 加入一整列（格子可含 `|`），左格每個頂點都連向右格每個頂點 |
| `MenuGraph.Vertices` / `Edges` / `Sources` / `Sinks` / `VertexCount` / `EdgeCount` | 查詢；`Sources`（入度 0）就是選單的第一層 |
| `MenuGraph.GetDistances(start, [maxDistance])` / `IsAcyclic()` | 最少邊數、是否無環 |
| `MenuGraph.FromRows(rows, out warnings)` / `ToRows(style)` | 與 `From,To` 二維字串陣列互轉，寫法見 `MenuGraphRowStyle`（`Expanded` / `Compact` / `Outline`） |
| `MenuGraph.FromTree(tree)` / `ToTree([start,] out warnings)` | 與樹互轉：同名節點合併成一個頂點；展開成樹時共用頂點會被複製，環會被略過並警告 |
| `MenuGraph.CreateTopLevelNodes()` | 建立 `MenuGraphNode`（實作 `ICascadeNode`），可交給 `CascadeView.SetTopLevelNodes` 顯示 |
| `MenuVertex.Successors` / `Predecessors` / `InDegree` / `OutDegree` / `IsSource` / `IsSink` / `IsConvergent` / `HasLoop` | 頂點的鄰接資訊 |
| `MenuVertex.Rename(newName)` | 改名；改成既有名稱時把邊轉接過去並合併 |
| `MenuGraphNode.SiblingNumber` / `Depth` / `FullPathText` / `IsRepeatedOnPath` | 依走進來的路徑決定的序號、深度、路徑，以及是否走進了環 |

**`MenuGraphCsvText`** / **`MenuGraphCsvFile`**（`CascadeControl.Graphs.Csv`）

成員與樹的版本相同：`Parse` / `Format(graph, [style])` / `NormalizeRows` / `CreateHeader()`，以及 `Load` / `Save(graph, filePath, [style])`。

---

## 注意事項

- **資料改了要呼叫 `RefreshColumns` 或重新 `SetTree` / `SetTopLevelNodes`。** 已經顯示中的欄位不會自動反映 `CascadeNode.Children` 或你自己的模型的變動；`RefreshColumns` 會保留（或改選指定的）選取路徑，重新設定則會清空選取與記憶軌跡。
- **`SetTree`、`SetTopLevelNodes`、`Clear` 也會觸發 `SelectedPathChanged`**（未選取的狀態）。在表單建構式裡呼叫時，事件處理常式也會跑一次。
- **按 ← 會改變選取**，`SelectedPathChanged` 跟著觸發。依賴「← 只移動焦點」的舊程式要留意。
- **`DrawNode` 和 `ColumnWidthChanged` 不在屬性視窗裡**，需要時請在程式碼中訂閱。
- **在程式裡直接改 `MenuNode` 之後要清除復原歷史**：`CascadeView` 呼叫 `History.Clear()`，`CascadeCheckboxView` 呼叫 `CheckSet.Reconcile()`（它也會清除），見[樹被編輯之後](#樹被編輯之後)。
- **更新 dll 時要連同 `CascadeControl.xml` 一起換**，否則 IntelliSense 顯示的會是舊的說明。
- 控制項用 `WS_EX_COMPOSITED` 做雙緩衝，避免改選時閃爍。一般大小的選單感覺不出額外負擔；
  如果單欄有上千項，請留意重繪效能。
