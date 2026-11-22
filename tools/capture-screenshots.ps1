<#
.SYNOPSIS
產生 README 用的截圖：CascadeColumnsView、CascadeDiagramView，以及兩者共享 State 的畫面。

.DESCRIPTION
用範例選單樹建立真正的控制項，顯示在螢幕上，用程式設定選取與勾選後，抓下控制項所在的螢幕範圍存成 PNG。
欄位是 Win32 ListBox，DrawToBitmap 畫不出內容，所以用 CopyFromScreen；執行時請不要用其他視窗蓋住。
重跑會覆蓋既有的圖檔。

CascadeControl.dll 是 .NET Framework 4.8 組件，所以要用 Windows PowerShell 5.1（powershell.exe）執行，不是 pwsh。

.PARAMETER AssemblyPath
要載入的組件，預設是 CascadeControl\bin\Debug\CascadeControl.dll。

.PARAMETER OutDir
圖檔輸出目錄，預設是 docs\images。

.EXAMPLE
powershell -NoProfile -File tools\capture-screenshots.ps1
#>
param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\CascadeControl\bin\Debug\CascadeControl.dll'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\images')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'CascadeControl.dll 是 .NET Framework 組件，請用 Windows PowerShell 5.1（powershell.exe）執行。'
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class DpiAwareness
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
# 不宣告 DPI 感知時，高 DPI 螢幕上的座標會被縮放，CopyFromScreen 抓到的位置會偏掉。
[void][DpiAwareness]::SetProcessDPIAware()
[void][Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$sampleCsv = @'
Level1,Level2,Level3,Level4
System,Reboot|Shutdown,,
System,Info,Version|Uptime|Memory,
Motor,Axis X|Axis Y|Axis Z,Home|Speed,
Motor,Axis X|Axis Y|Axis Z,Jog,Forward|Backward
IO,Input,DI0|DI1|DI2,
IO,Output,DO0|DO1,
'@

function New-SampleTree {
    $warnings = $null
    $tree = [CascadeControl.Trees.Csv.MenuTreeCsvText]::Parse($sampleCsv, [ref]$warnings)
    if ($warnings.Count -gt 0) { throw "範例資料有警告：$($warnings -join '; ')" }
    return $tree
}

# 依名稱路徑找節點，例如 Find-Node $tree 'Motor', 'Axis X', 'Jog'。
function Find-Node([CascadeControl.Trees.MenuTree]$tree, [string[]]$names) {
    $node = $tree.Root
    foreach ($name in $names) {
        $child = $null
        if (-not $node.TryGetChild($name, [ref]$child)) { throw "找不到節點：$($names -join ' > ')" }
        $node = $child
    }
    return $node
}

function New-Form([int]$width, [int]$height) {
    $form = New-Object System.Windows.Forms.Form
    $form.FormBorderStyle = 'FixedSingle'
    $form.StartPosition = 'Manual'
    $form.Location = New-Object System.Drawing.Point(40, 40)
    $form.ClientSize = New-Object System.Drawing.Size($width, $height)
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $form.BackColor = [System.Drawing.SystemColors]::Window
    $form.TopMost = $true
    return $form
}

# 顯示表單、執行設定狀態的動作、等畫面穩定後抓下 $target 的螢幕範圍。
function Save-Screenshot([System.Windows.Forms.Form]$form, [System.Windows.Forms.Control]$target, [scriptblock]$arrange, [string]$fileName) {
    $form.Show()
    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()
    & $arrange
    for ($i = 0; $i -lt 10; $i++) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }

    $bounds = $target.RectangleToScreen($target.ClientRectangle)
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $path = Join-Path $OutDir $fileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "已寫出 $path（$($bounds.Width)x$($bounds.Height)）"
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $form.Close()
        $form.Dispose()
    }
}

# 1. CascadeColumnsView：選到 Motor > Axis X > Jog，第 4 欄是 Jog 的子節點。
$tree = New-SampleTree
$form = New-Form 760 150
$columns = New-Object CascadeControl.Views.Columns.CascadeColumnsView
$columns.Dock = 'Fill'
$form.Controls.Add($columns)
Save-Screenshot $form $columns {
    $columns.SetTree($tree)
    $columns.ColumnLayoutMode = [CascadeControl.Views.Columns.CascadeColumnLayoutMode]::Fill
    $columns.DisplayColumnCount = $tree.Height
    [void]$columns.TrySelectNode((Find-Node $tree 'Motor', 'Axis X', 'Jog'))
    $columns.FocusActiveColumn()
} 'columns.png'

# 2. CascadeDiagramView：同一棵樹，同一條選取。
$tree = New-SampleTree
$form = New-Form 900 560
$diagram = New-Object CascadeControl.Views.Diagram.CascadeDiagramView
$diagram.Dock = 'Fill'
$form.Controls.Add($diagram)
Save-Screenshot $form $diagram {
    $diagram.SetTree($tree)
    $diagram.SizeMode = [CascadeControl.Views.Diagram.CascadeDiagramSizeMode]::Zoom
    [void]$diagram.TrySelectNode((Find-Node $tree 'Motor', 'Axis X', 'Jog'))
    foreach ($node in $tree.TopLevelNodes) { $diagram.SetExpanded($node, $true) }
    foreach ($path in @(@('Motor', 'Axis X'), @('Motor', 'Axis X', 'Jog'), @('System', 'Info'))) {
        $diagram.SetExpanded((Find-Node $tree $path), $true)
    }
} 'diagram.png'

# 3. 兩者共享 State：左邊勾選，右邊的樹狀圖把沒勾的節點變淡。
$tree = New-SampleTree
$form = New-Form 1440 400
$split = New-Object System.Windows.Forms.SplitContainer
$split.Dock = 'Fill'
$form.Controls.Add($split)
$columns = New-Object CascadeControl.Views.Columns.CascadeColumnsView
$columns.Dock = 'Fill'
$split.Panel1.Controls.Add($columns)
$diagram = New-Object CascadeControl.Views.Diagram.CascadeDiagramView
$diagram.Dock = 'Fill'
$split.Panel2.Controls.Add($diagram)
Save-Screenshot $form $split {
    # 表單顯示後才有實際寬度，SplitterDistance 要在這之後設。
    $split.SplitterDistance = 4 * 170 + 4 * $columns.ColumnSpacing + 8
    $diagram.State = $columns.State
    $columns.CheckBoxes = $true
    $columns.SetTree($tree)
    $columns.ColumnWidth = 170
    $diagram.SizeMode = [CascadeControl.Views.Diagram.CascadeDiagramSizeMode]::Zoom
    $diagram.UncheckedDisplay = [CascadeControl.Views.Diagram.CascadeDiagramUncheckedDisplay]::Dimmed

    $checks = $columns.CheckSet
    [void]$checks.CheckSubtree((Find-Node $tree 'Motor', 'Axis X'))
    [void]$checks.Check((Find-Node $tree 'Motor', 'Axis Y', 'Home'))
    [void]$checks.Check((Find-Node $tree 'System', 'Reboot'))
    [void]$columns.TrySelectNode((Find-Node $tree 'Motor', 'Axis X', 'Jog'))
    foreach ($path in @(@('System'), @('Motor', 'Axis X', 'Jog'), @('Motor', 'Axis Y'))) {
        $diagram.SetExpanded((Find-Node $tree $path), $true)
    }
    $columns.FocusActiveColumn()
} 'together.png'
