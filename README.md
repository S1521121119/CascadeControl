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

**`MenuTree.AddPath`**：一次加入一條路徑。畫面上的順序就是呼叫的順序，同一條路徑重複加入會自動合併。名稱不可含有 `|`，否則丟 `ArgumentException`；要列舉同層項目請用下面的 `AddPaths`。

```csharp
var menu = new MenuTree();
menu.AddPath("System", "Reboot");
menu.AddPath("System", "Info", "Version");
menu.AddPath("Motor", "Axis X", "Home");

cascadeView1.SetRoots(menu.TopLevel);
MenuTreeCsv.Save(menu, "menu.csv");   // 需要的話存成檔案
```

**`MenuTree.AddPaths`**：一次加入一整列，每一格都可以用 `|` 列舉同層項目，規則和 CSV 相同。回傳每一條路徑的終點節點。

```csharp
menu.AddPaths("Motor", "Axis X|Axis Y", "Jog", "Forward|Backward");   // 加入 4 條路徑
menu.AddPaths("IO", "Input", AlternativeCell.Join(inputNames));        // 名稱清單來自程式時
```

和 `AddPath` 不同，`AddPaths` 會先去除每個名稱前後的空白；有任何一格不合法時丟 `ArgumentException`，整列都不會加入。

### 用文字組選單

**`MenuTreeText`** 處理文字和樹之間的轉換，不碰檔案。格式和 CSV 檔完全相同，所以可以直接在程式裡手寫，
也可以貼上從剪貼簿、資料庫或其他來源拿到的整段文字：

```csharp
MenuTree menu = MenuTreeText.Parse(@"
    System, Reboot|Shutdown
    System, Info,          Version|Uptime|Memory

    Motor,  Axis X|Axis Y, Home
", out IList<string> warnings);

string text = MenuTreeText.Format(menu, compact: true);   // 轉回文字，可另外存放
```

- 表頭可有可無；空白行、縮排、逗號後的空白都會被忽略。
- 警告中的「第 N 列」是文字中的實際行號，空白行和跨行的儲存格也照算。
- `Parse` 不會丟例外，有問題的資料只會寫進 `warnings`。

**`CascadeItem`**：不需要 `MenuTree` 的功能（例如 CSV、序號、`FullPath`）時，直接用內建節點組樹，再用 `Tag` 帶上自己的資料：

```csharp
cascadeView1.SetRoots(new[]
{
    new CascadeItem("System", "System", children: new[]   // 名稱、顯示文字
    {
        new CascadeItem("Reboot", "Reboot", tag: myRebootCommand),
        new CascadeItem("Shutdown", "Shutdown", tag: myShutdownCommand),
    }),
});

// 在 ItemActivated 中取回自己的資料
var command = ((CascadeItem)e.Item).Tag;
```

### 接上自己的資料模型

只要實作 `ICascadeItem` 的三個成員就行。這個介面不相依 WinForms，可以放在資料層。

```csharp
public sealed class DeviceNode : ICascadeItem
{
    public string Name { get; }                                  // 原始名稱，用於路徑文字
    public string Text => Name;                                  // 欄位上顯示的文字，可以和 Name 不同
    public IReadOnlyList<ICascadeItem> Children => LoadChildren(); // 空集合代表終點
    // ...
}
```

- 控制項在需要時才讀取 `Children`（例如要開下一欄的時候），所以可以在這裡延遲載入。
  但同一個節點可能被讀取不只一次（例如退回後再走回去），控制項用 `Equals` 比對記憶中的項目，
  所以延遲載入的結果要快取起來，或讓子節點覆寫 `Equals`；每次都產生新實例的話，記憶會對不上。
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
    lblPath.Text = cascadeView1.SelectedPathText; // 「Motor => Axis X」
}
```

不在事件裡時，可以直接讀控制項的 `SelectedItem`、`SelectedPath`、`SelectedPathText` 和 `IsLeafSelected`。

選取只算到目前操作的那一欄。例如選到第 7 層後按 ← 退到第 3 層，選取就是前 3 層，`SelectionChanged` 也會觸發；
第 4～7 層只是記憶（見[退回與記憶](#退回與記憶)），不會出現在 `Path` 裡。

### 用程式選取某條路徑

例如還原上次的選取：

```csharp
MenuNode last = menu.Leaves().FirstOrDefault(n => n.FullPath == savedPath);
if (last != null) cascadeView1.TrySelectPath(last.PathFromRoot);
```

- 比對用的是 `Equals`，所以要傳入「同一棵樹」裡的節點。
- 中途對不上時，選取會停在已對上的那一段，並回傳 `false`。
- 不論選了幾層，`SelectionChanged` 都只觸發一次。
- 路徑經過的每一層會記成「上次選的項目」，其他分支的記憶保留。

要把鍵盤焦點交給控制項時，呼叫 `FocusActiveColumn()`，焦點會落在目前操作的那一欄（未選取時是第一欄）。
舊的 `FocusFirstColumn()` 仍可呼叫，行為相同，但已標為過時。

### 欄寬與顯示欄數

`ColumnSizeMode` 決定欄寬，以及畫面上一次看得到幾欄：

| 模式 | 欄寬 | 畫面 |
|---|---|---|
| `Fixed`（預設） | 固定為 `ColumnWidth` | 放不下時出現水平捲軸 |
| `Fill` | 讓 `FillColumnCount` 欄平均填滿寬度，不小於 `ColumnWidth` | 一次看到 `FillColumnCount` 欄，捲動時對齊欄位邊界 |
| `Single` | 填滿控制項寬度，不小於 `ColumnWidth` | 只顯示目前操作的那一欄，不保留右側的下一欄 |

```csharp
cascadeView1.ColumnSizeMode = CascadeColumnSizeMode.Fill;
cascadeView1.FillColumnCount = 3;   // 控制項寬 300、欄距 0 時，每欄約 100
```

`FillColumnCount` 為 0 時，`Fill` 等同 `Fixed`。平均後小於 `ColumnWidth` 時改用 `ColumnWidth`，放不下的部分照樣出現水平捲軸。

### 退回與記憶

每個項目都會記住「上次在它底下選了哪一個子節點」。退回較淺的層級，或切換到別的分支再切回來時，
按 → 就能沿上次的路線走回去。改選別的項目只會改寫那一層的記錄，其他分支走過的路線都還在。

例如：
1. 先選到 `ActionStep\ActionPretest\RfStep1\GetHWAddr`。
2. 再改選 `ActionStep\ActionBurnIn\RfStep3\WifiTestVerify`，看一下設定。
3. 按 ← ← 回到 `ActionBurnIn`，再按 ↑ 回到 `ActionPretest`。
4. 按 → → 就回到 `GetHWAddr`。之後切回 `ActionBurnIn`，`RfStep3\WifiTestVerify` 也還記得。

`ShowRememberedPath` 只決定畫面上要不要畫出記憶的路線：

| `ShowRememberedPath` | 選取停在第 3 層、記憶還有第 4～7 層時的畫面 |
|---|---|
| `false`（預設） | 畫到第 4 欄，第 4 欄用淺灰底標出上次選的項目 |
| `true` | 第 4～7 欄都畫出來，記憶項目都用淺灰底標出；用 ↑↓ 切換項目時，右邊會跟著換成那個項目記住的路線 |

- 點選路徑或記憶中的**同一項**，只會移動選取深度，效果等同連按 ← 或 →。
- 按 Esc 只清除選取，記憶保留，第一欄會標出上次選的項目，按 → 就能沿記憶走回去。
- `ClearMemory()` 忘掉所有分支的記憶，只留下目前的選取路徑。`SetRoots`、`Clear` 會連同選取一起清空。
- 每個走過的父節點各記一筆，上限是節點總數，不會無限增長。

記憶項目的底色可以用 `RememberedBackColor` 調整。`Single` 模式只顯示一欄，所以 `ShowRememberedPath` 沒有作用，但記憶仍然有效。

> 記憶以節點本身當作 key，比對用 `Equals` 與 `GetHashCode`。`MenuNode`、`CascadeItem` 比對的是參照，不需要處理；
> 自訂模型如果覆寫 `Equals`，請一併覆寫 `GetHashCode`。

### 自訂外觀

| 想調整 | 用 |
|---|---|
| 字型、列高 | 控制項的 `Font`，列高會跟著字型自動調整 |
| 欄寬、欄距 | `ColumnWidth`（最小 20）、`ColumnGap`；平均或單欄顯示請見[欄寬與顯示欄數](#欄寬與顯示欄數) |
| 非焦點欄的選取底色 | `InactiveSelectionBackColor`，預設為調淡的系統選取色 |
| 記憶項目的底色 | `RememberedBackColor`，預設為系統淺灰色 |

這兩個顏色沒設定時，會跟著 Windows 的系統配色變。在屬性視窗按右鍵選「重設」就能回到預設；程式中設為 `Color.Empty` 也一樣。
| `SelectedPathText` 的分隔字串 | `PathSeparator`，預設 `" => "` |
| 完全自繪 | `DrawItem` 事件，設定 `e.Handled = true` 就會略過預設外觀 |

`DrawItem` 不會出現在屬性視窗裡，請在程式碼中訂閱。用 `e.IsSelected`、`e.IsRemembered` 分辨選取項和記憶項目。
自繪時要自己填滿背景，否則舊的選取色會殘留：

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

檔案（`MenuTreeCsv`）和文字（`MenuTreeText`）用的是同一種格式。

- **表頭**：第一列是 `Level1,Level2,…`（不分大小寫）。讀檔時如果第一列不是表頭，會被當成資料並發出警告；`MenuTreeText.Parse` 則不要求表頭。
- **一列一條路徑**：由左至右是第 1 層、第 2 層…，深度不足的尾欄留空。深度沒有上限。
- **用 `|` 列舉同層項目**：`Axis X|Axis Y` 代表兩個兄弟節點；同一列有好幾格這樣寫時，會依笛卡兒積展開成多條路徑（越右邊的欄位變化越快）。
  程式中組這種字串可以用 `AlternativeCell.Join`。
- **名稱**：前後空白會自動去除；**區分大小寫**，所以 `Reboot` 和 `reboot` 是兩個不同的項目。
- **重複與延伸**：完全相同的路徑會合併。`A,B` 和 `A,B,C` 同時存在時，`B` 會變成中間節點，不算錯誤。
- **顯示文字**：`MenuNode` 在畫面上顯示成 `<01>Name`，有子節點時再加上 ` [+]`；序號是它在同一層中第一次出現的順序。顯示文字只用在欄位上，路徑與記錄一律使用 `Name`。

以下情況不會丟例外，而是寫進 `warnings`：

- 中間缺一層，例如 `A,,C`：空欄之後的欄位會被忽略。
- `|` 用法錯誤，例如 `A||B`、`|A`、`A|`，或某一段只有空白：整列跳過。
- 單列展開超過 65,535 條（`AlternativeCell.MAX_EXPANSION`）：整列跳過。
- 整份展開累計超過 65,535 條：整份放棄，回傳空樹。

警告訊息中的「第 N 列」是檔案（或文字）中的實際行號。

### 上限為什麼是 65,535

這個數字不是隨意挑的保護值，而是現有架構的限制：

- **UI**：每一欄都是 Win32 `ListBox`，`LB_ITEMFROMPOINT`（`ListBox.IndexFromPoint`）只用 16 位元回傳索引。
  實測一欄放 70,000 項時，捲到底部點第一列，拿到的索引是 4442（= 69978 & 0xFFFF）。
  任何一欄的項目數都不會超過全樹的路徑數，所以總數限制在 65,535，就保證每一欄都點得對。
- **速度**：每次改選都會把下一欄整批重新加入，實測 1 萬項約 60 ms、65,535 項約 0.8 秒。上限內都能用，但單欄最好控制在數千項以內。
- **記憶體**：65,535 條路徑的樹約 30 MB，32 位元行程也沒問題。

單列與整份用同一個上限，所以壓縮寫出時，不會出現某一列超過上限、需要拆開的情況（見下節）。

### 寫出：展開或壓縮

`MenuTreeCsv.Save(menu, path, compact)` 和 `MenuTreeText.Format(menu, compact)` 都可以選擇寫出的形式，兩種形式讀回來都是同一棵樹：

- `compact: false`（預設）：每個終點一列。
- `compact: true`：**相鄰**而且**子樹完全相同**（名稱、順序、結構都一樣）的兄弟節點，合併成同一格。

```csv
Level1,Level2,Level3
Device,Info|Status,Version|Uptime|Memory
Device,Reset,
Network,Ping,
```

壓縮產生的是標準形，不追求最少列數：同一棵樹只有一種寫法，列與列不重疊，列序和展開形式一致，
已經壓縮過的結果再壓一次也不會改變。

寫出前會先檢查，讀不回同一棵樹的內容會直接丟 `ArgumentException`，不會寫出任何東西：
名稱含 `|`、名稱前後有空白，或路徑超過 65,535 條。

`MenuTreeText.Sort(rows, compact, out warnings)` 可以整理手寫的資料列：逐層分組排序、補齊欄數、去掉重複的路徑，並視需要壓縮。

---

## 操作方式

| 按鍵 / 動作 | 行為 |
|---|---|
| ↑ ↓ | 在同一欄內移動，也就是改選這一層；其他分支的記憶保留 |
| → | 進入下一欄：有記憶時選上次的項目，沒有時選第一項 |
| ← | 退回上一欄，**選取也跟著退回**；更深的那一段留作記憶 |
| Enter | 在終點上觸發 `ItemActivated`；不是終點時等同 → |
| Esc | 清除選取（記憶保留），焦點回到第一欄 |
| 滑鼠點選 | 點不同的項目等同改選；點路徑或記憶中的同一項，等同連按 ← 或 → 到那一欄 |
| 雙擊 | 在終點上觸發 `ItemActivated` |

- 鍵盤焦點固定在目前操作的那一欄（最深選取項所在的欄），點在其他欄的空白處後，焦點會回到這一欄。
- 只要焦點在控制項的欄位上，←、→、Enter、Esc 一律由控制項處理。所以表單的 `CancelButton` 在這時候不會被 Esc 觸發。
- 欄位多到放不下時會出現水平捲軸，並自動捲到操作欄，讓它的右邊剛好留得下一欄。

---

## API 速查

完整說明請看 IntelliSense（`CascadeControl.xml` 要和 dll 放在一起才看得到）。

**`CascadeView`**（`CascadeControl.Views`，繼承自 `Panel`）

| 成員 | 說明 |
|---|---|
| `SetRoots(roots)` / `Clear()` | 以第一層內容重設，或清空 |
| `TrySelectPath(path)` | 依序選取一條路徑 |
| `FocusActiveColumn()` | 把焦點放到目前操作的那一欄 |
| `SelectedItem` / `SelectedPath` / `SelectedPathText` | 目前的選取（不含記憶） |
| `IsLeafSelected` | 是否已選到終點 |
| `FocusedLevel` | 目前操作中的欄位索引，也就是最深選取項所在的欄 |
| `ColumnSizeMode` / `FillColumnCount` | 欄寬模式（`Fixed`、`Fill`、`Single`）與 `Fill` 的平均欄數 |
| `ShowRememberedPath` | 是否畫出記憶中的整段路線 |
| `ClearMemory()` | 忘掉所有分支的記憶，只留目前的選取路徑 |
| `ColumnWidth` / `ColumnGap` / `InactiveSelectionBackColor` / `RememberedBackColor` / `PathSeparator` | 外觀設定 |
| `SelectionChanged` | 選取路徑改變後觸發，包含 ←、→、Esc、`SetRoots` 與 `Clear` |
| `ItemActivated` | 在終點上按 Enter 或雙擊時觸發 |
| `DrawItem` | 繪製每一列之前觸發，用於自繪 |
| `ColumnWidthChanged` | `ColumnWidth` 改變後觸發，供資料繫結使用 |

**`MenuTree` / `MenuNode`**（`CascadeControl.Models`）

| 成員 | 說明 |
|---|---|
| `MenuTree.TopLevel` | 第一層節點，傳給 `SetRoots` |
| `MenuTree.AddPath(names)` | 加入一條路徑，回傳終點節點 |
| `MenuTree.AddPaths(cells)` | 加入一整列（格子可含 `\|`），回傳各條路徑的終點節點 |
| `MenuTree.Leaves()` / `MaxDepth` / `IsEmpty` | 查詢 |
| `MenuTree.FromRows(rows, out warnings)` / `ToRows(compact)` | 與二維字串陣列互轉；讀入時展開 `\|`，寫出時可以壓縮 |
| `AlternativeCell.SEPARATOR` / `MAX_EXPANSION` | `\|` 與展開上限 65,535 |
| `AlternativeCell.Join(names)` | 把多個同層名稱串成一格 |
| `MenuNode.Name` | 原始名稱，即 `ICascadeItem.Name`；路徑、記錄都用它 |
| `MenuNode.DisplayText` | 欄位上的顯示文字，即 `ICascadeItem.Text`：`<01>Name`，有子節點時為 `<01>Name [+]` |
| `MenuNode.FullPath` / `PathFromRoot` | 由第一層到本節點的路徑 |
| `MenuNode.Parent` / `Depth` / `Index` / `IsLeaf` | 位置資訊 |

**`MenuTreeText`**（`CascadeControl.Services`，純文字、不碰檔案）

| 成員 | 說明 |
|---|---|
| `Parse(text, out warnings)` | 解析文字，表頭可省略，不丟例外 |
| `Format(menu, compact)` | 轉成文字，`compact: true` 時壓回 `\|` 的寫法 |
| `Sort(rows, [compact,] out warnings)` | 整理資料列 |
| `Header(depth)` | 產生 `Level1…LevelN` 表頭 |

**`MenuTreeCsv`**（`CascadeControl.Services`，檔案）

| 成員 | 說明 |
|---|---|
| `Load(filePath, out warnings)` | 讀取 CSV，不丟例外 |
| `Save(menu, filePath, compact)` | 寫出 CSV（預設展開），目錄不存在時會自動建立 |

---

## 注意事項

- **資料改了要重新 `SetRoots`。** 已經顯示中的欄位不會自動反映 `CascadeItem.Children` 或你自己的模型的變動。
- **`SetRoots` 也會觸發 `SelectionChanged`**（未選取的狀態）。在表單建構式裡呼叫時，事件處理常式也會跑一次。
- **按 ← 會改變選取**，`SelectionChanged` 跟著觸發。依賴「← 只移動焦點」的舊程式要留意。
- **`DrawItem` 和 `ColumnWidthChanged` 不在屬性視窗裡**，需要時請在程式碼中訂閱。
- **更新 dll 時要連同 `CascadeControl.xml` 一起換**，否則 IntelliSense 顯示的會是舊的說明。
- 控制項用 `WS_EX_COMPOSITED` 做雙緩衝，避免改選時閃爍。一般大小的選單感覺不出額外負擔；
  如果單欄有上千項，請留意重繪效能。
