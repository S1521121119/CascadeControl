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
第 4 欄是 `Jog` 的子節點。`Forward` 沒有下一層，選到它再按 Enter 就會觸發 `ItemActivated`。

- **需求**：.NET Framework 4.8、Windows Forms，不需要第三方套件。
- **命名空間**：`CascadeControl.Views`（控制項）、`CascadeControl.Models`（選單樹）、`CascadeControl.Services`（CSV 讀寫）。

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

    MenuTree menu = MenuTreeCsv.Load("menu.csv", out IList<string> warnings);
    foreach (var warning in warnings) Debug.WriteLine(warning);

    cascadeView1.ItemActivated += CascadeView1_ItemActivated;
    cascadeView1.SetRoots(menu.TopLevel);
}

// 使用者在終點（沒有下一層的項目）上按 Enter 或雙擊時觸發。
private void CascadeView1_ItemActivated(object sender, CascadeItemEventArgs e)
{
    var node = (MenuNode)e.Item;
    MessageBox.Show($"執行：{node.FullPath}");   // 例如「Motor > Axis X > Jog > Forward」
}
```

`MenuTreeCsv.Load` 不會丟例外：讀取失敗時回傳空樹，原因寫在 `warnings` 裡。
檔案不存在時，它會在同一個路徑寫出一份只有表頭的範本。

> 完整範例請參考 repo 裡的 `CascadeControlDemo/TestForm.cs`。

---

## 常見任務

### 用程式組選單

**`MenuTree.AddPath`**：一次加入一條路徑。畫面上的順序就是呼叫的順序，同一條路徑重複加入會自動合併。

```csharp
var menu = new MenuTree();
menu.AddPath("System", "Reboot");
menu.AddPath("System", "Info", "Version");
menu.AddPath("Motor", "Axis X", "Home");

cascadeView1.SetRoots(menu.TopLevel);
MenuTreeCsv.Save(menu, "menu.csv");   // 需要的話存成檔案
```

**`CascadeItem`**：不需要 `MenuTree` 的功能（例如 CSV、序號、`FullPath`）時，直接用內建節點組樹，再用 `Tag` 帶上自己的資料：

```csharp
cascadeView1.SetRoots(new[]
{
    new CascadeItem("System", children: new[]
    {
        new CascadeItem("Reboot", tag: myRebootCommand),
        new CascadeItem("Shutdown", tag: myShutdownCommand),
    }),
});

// 在 ItemActivated 中取回自己的資料
var command = ((CascadeItem)e.Item).Tag;
```

### 接上自己的資料模型

只要實作 `ICascadeItem` 的兩個成員就行。這個介面不相依 WinForms，可以放在資料層。

```csharp
public sealed class DeviceNode : ICascadeItem
{
    public string Text => Name;                                  // 欄位上顯示的文字
    public IReadOnlyList<ICascadeItem> Children => LoadChildren(); // 空集合代表終點
    // ...
}
```

- 控制項在需要時才讀取 `Children`（例如要開下一欄的時候），所以可以在這裡延遲載入。
- 不需要提供 Parent 或 Depth。路徑由欄位順序決定，事件參數會直接把整條路徑交給你。

### 讀取目前的選取

`SelectionChanged` 和 `ItemActivated` 用的是同一個 `CascadeItemEventArgs`：

| 屬性 | 內容 | 未選取時 |
|---|---|---|
| `Path` | 由淺到深的整條選取路徑 | 空集合 |
| `Item` | 路徑的終點（最深的選取） | `null` |
| `Level` | `Item` 所在欄位，由 0 起算 | `-1` |
| `Parent` | `Item` 的上一層；在第一欄時為 `null` | `null` |
| `Children` | 下一欄會顯示的內容（永遠不為 null） | 第一欄的內容 |
| `IsLeaf` | 是否已選到終點 | `false` |

```csharp
private void CascadeView1_SelectionChanged(object sender, CascadeItemEventArgs e)
{
    btnRun.Enabled = e.IsLeaf;                    // 選到終點才能執行
    lblPath.Text = cascadeView1.SelectedPathText; // 「<02>Motor => <01>Axis X」
}
```

不在事件裡時，可以直接讀控制項的 `SelectedItem`、`SelectedPath`、`SelectedPathText` 和 `IsLeafSelected`。

### 用程式選取某條路徑

例如還原上次的選取：

```csharp
MenuNode last = menu.Leaves().FirstOrDefault(n => n.FullPath == savedPath);
if (last != null) cascadeView1.TrySelectPath(last.PathFromRoot);
```

- 比對用的是 `Equals`，所以要傳入「同一棵樹」裡的節點。
- 中途對不上時，選取會停在已對上的那一段，並回傳 `false`。
- 不論選了幾層，`SelectionChanged` 都只觸發一次。

要把鍵盤焦點交給控制項時，呼叫 `FocusFirstColumn()`。

### 自訂外觀

| 想調整 | 用 |
|---|---|
| 字型、列高 | 控制項的 `Font`，列高會跟著字型自動調整 |
| 欄寬、欄距 | `ColumnWidth`（最小 20）、`ColumnGap` |
| 非焦點欄的選取底色 | `InactiveSelectionBackColor`，留空時使用調淡的系統選取色 |
| `SelectedPathText` 的分隔字串 | `PathSeparator`，預設 `" => "` |
| 完全自繪 | `DrawItem` 事件，設定 `e.Handled = true` 就會略過預設外觀 |

`DrawItem` 不會出現在屬性視窗裡，請在程式碼中訂閱。自繪時要自己填滿背景，否則舊的選取色會殘留：

```csharp
cascadeView1.DrawItem += (s, e) =>
{
    // 只改「未選取的終點」：用綠字標出可以直接執行的項目，其餘交給預設外觀。
    if (e.IsSelected || !(e.Item is MenuNode node) || !node.IsLeaf) return;

    e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds);
    TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Font, e.Bounds, Color.DarkGreen,
        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    e.Handled = true;
};
```

---

## CSV 格式

- **表頭**：第一列是 `Level1,Level2,…`（不分大小寫）。如果第一列不是表頭，會被當成資料並發出警告。
- **一列一條路徑**：由左至右是第 1 層、第 2 層…，深度不足的尾欄留空。深度沒有上限。
- **用 `|` 列舉同層項目**：`Axis X|Axis Y` 代表兩個兄弟節點；同一列有好幾格這樣寫時，會依笛卡兒積展開成多條路徑。
  程式中組這種字串可以用 `MenuTreeCsv.JoinAlternatives`。
- **名稱**：前後空白會自動去除；**區分大小寫**，所以 `Reboot` 和 `reboot` 是兩個不同的項目。
- **重複與延伸**：完全相同的路徑會合併。`A,B` 和 `A,B,C` 同時存在時，`B` 會變成中間節點，不算錯誤。
- **顯示文字**：`MenuNode` 在畫面上顯示成 `<01>Name`，序號是它在同一層中第一次出現的順序。

以下情況不會丟例外，而是寫進 `warnings`：

- 中間缺一層，例如 `A,,C`：空欄之後的欄位會被忽略。
- `|` 用法錯誤，例如 `A||B`、`|A`、`A|`，或某一段只有空白：整列跳過。
- 單列展開超過 5000 條（`MAX_EXPANSION_PER_ROW`）：整列跳過。
- 整份展開超過 30000 條（`MAX_EXPANSION_TOTAL`）：整份放棄，回傳空樹。

其他須知：

- 警告訊息中的「第 N 列」是檔案行號。檔案中如果夾著空白行，行號會比實際的小。
- `MenuTreeCsv.Save` 一律寫出展開後的形式，不會壓回 `|` 的寫法；節點名稱含 `|` 時會丟 `ArgumentException`。
- `MenuTreeCsv.Sort` 可以整理手寫的資料列：逐層分組排序、補齊欄數，並去掉重複的路徑。

---

## 操作方式

| 按鍵 / 動作 | 行為 |
|---|---|
| ↑ ↓ | 在同一欄內移動，也就是改選這一層 |
| → | 進入下一欄；下一欄還沒選時，會直接選第一項 |
| ← | 焦點退回上一欄，**選取不變**，後面的欄位會保留 |
| Enter | 在終點上觸發 `ItemActivated`；不是終點時等同 → |
| 滑鼠點選 | 選取該項 |
| 雙擊 | 在終點上觸發 `ItemActivated` |

欄位多到放不下時會出現水平捲軸，並自動捲到焦點欄，讓它的右邊剛好留得下一欄。

---

## API 速查

完整說明請看 IntelliSense（`CascadeControl.xml` 要和 dll 放在一起才看得到）。

**`CascadeView`**（`CascadeControl.Views`，繼承自 `Panel`）

| 成員 | 說明 |
|---|---|
| `SetRoots(roots)` / `Clear()` | 以第一層內容重設，或清空 |
| `TrySelectPath(path)` | 依序選取一條路徑 |
| `FocusFirstColumn()` | 把焦點放到第一欄 |
| `SelectedItem` / `SelectedPath` / `SelectedPathText` | 目前的選取 |
| `IsLeafSelected` | 是否已選到終點 |
| `FocusedLevel` | 目前操作中的欄位索引 |
| `ColumnWidth` / `ColumnGap` / `InactiveSelectionBackColor` / `PathSeparator` | 外觀設定 |
| `SelectionChanged` | 任何一層的選取改變後觸發，包含 `SetRoots` 與 `Clear` |
| `ItemActivated` | 在終點上按 Enter 或雙擊時觸發 |
| `DrawItem` | 繪製每一列之前觸發，用於自繪 |
| `ColumnWidthChanged` | `ColumnWidth` 改變後觸發，供資料繫結使用 |

**`MenuTree` / `MenuNode`**（`CascadeControl.Models`）

| 成員 | 說明 |
|---|---|
| `MenuTree.TopLevel` | 第一層節點，傳給 `SetRoots` |
| `MenuTree.AddPath(names)` | 加入一條路徑，回傳終點節點 |
| `MenuTree.Leaves()` / `MaxDepth` / `IsEmpty` | 查詢 |
| `MenuTree.FromRows(rows, out warnings)` / `ToRows()` | 與二維字串陣列互轉 |
| `MenuNode.Name` / `DisplayText` | 原始名稱，以及 `<01>Name` 形式的顯示文字 |
| `MenuNode.FullPath` / `DisplayPath` / `PathFromRoot` | 由第一層到本節點的路徑 |
| `MenuNode.Parent` / `Depth` / `Index` / `IsLeaf` | 位置資訊 |

**`MenuTreeCsv`**（`CascadeControl.Services`）

| 成員 | 說明 |
|---|---|
| `Load(filePath, out warnings)` | 讀取 CSV，不丟例外 |
| `Save(menu, filePath)` | 寫出展開後的 CSV，目錄不存在時會自動建立 |
| `JoinAlternatives(names)` | 把多個同層名稱以 `ALTERNATIVE_SEPARATOR` 串成一格 |
| `Sort(rows, out warnings)` | 整理資料列 |
| `Header(depth)` | 產生 `Level1…LevelN` 表頭 |

---

## 注意事項

- **資料改了要重新 `SetRoots`。** 已經顯示中的欄位不會自動反映 `CascadeItem.Children` 或你自己的模型的變動。
- **`SetRoots` 也會觸發 `SelectionChanged`**（未選取的狀態）。在表單建構式裡呼叫時，事件處理常式也會跑一次。
- **`DrawItem` 和 `ColumnWidthChanged` 不在屬性視窗裡**，需要時請在程式碼中訂閱。
- **更新 dll 時要連同 `CascadeControl.xml` 一起換**，否則 IntelliSense 顯示的會是舊的說明。
- 控制項用 `WS_EX_COMPOSITED` 做雙緩衝，避免改選時閃爍。一般大小的選單感覺不出額外負擔；
  如果單欄有上千項，請留意重繪效能。
