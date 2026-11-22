<#
.SYNOPSIS
列出 CascadeControl.dll 的公開 API，一行一項，依序數排序。

.DESCRIPTION
用反射讀出公開型別（含公開的巢狀型別），以及其中 public、protected、protected internal 的成員。
每一行的格式是「型別全名 :: 內容」，型別本身那一行的內容以 type 開頭，所以任一行單獨拿出來都看得出屬於哪個型別。
參數的預設值與常數的值也列出來，因為改了它們呼叫端的行為就會不同。

用途是比對重構前後的公開 API：先建置，再把輸出和 tools\public-api.txt 比對。

CascadeControl.dll 是 .NET Framework 4.8 組件，所以要用 Windows PowerShell 5.1（powershell.exe）執行，不是 pwsh。

.PARAMETER AssemblyPath
要讀的組件，預設是 CascadeControl\bin\Debug\CascadeControl.dll。

.PARAMETER OutFile
寫入的檔案（UTF-8、CRLF）。沒有指定時輸出到標準輸出。

.EXAMPLE
powershell -NoProfile -File tools\dump-public-api.ps1 -OutFile $env:TEMP\public-api.txt
git diff --no-index tools\public-api.txt $env:TEMP\public-api.txt
#>
param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\CascadeControl\bin\Debug\CascadeControl.dll'),
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'CascadeControl.dll 是 .NET Framework 組件，請用 Windows PowerShell 5.1（powershell.exe）執行。'
}

$flags = [Reflection.BindingFlags]'Public, NonPublic, Instance, Static, DeclaredOnly'

function Get-TypeName([Type]$type) {
    if ($type.IsByRef) { return (Get-TypeName $type.GetElementType()) }
    if ($type.IsArray) { return (Get-TypeName $type.GetElementType()) + '[' + (',' * ($type.GetArrayRank() - 1)) + ']' }
    if ($type.IsGenericParameter) { return $type.Name }

    $name = $type.Name -replace '`\d+$', ''
    if ($type.IsNested) { $name = (Get-TypeName $type.DeclaringType) + '+' + $name }
    elseif ($type.Namespace) { $name = $type.Namespace + '.' + $name }

    if ($type.IsGenericType) {
        $name += '<' + (($type.GetGenericArguments() | ForEach-Object { Get-TypeName $_ }) -join ', ') + '>'
    }

    return $name
}

function Format-Value($value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [string]) { return '"' + $value + '"' }
    if ($value -is [bool]) { return $value.ToString().ToLowerInvariant() }
    if ($value -is [Enum]) { return (Get-TypeName $value.GetType()) + '.' + $value }
    return [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Access($member) {
    if ($member.IsPublic) { return 'public' }
    if ($member.IsFamilyOrAssembly) { return 'protected internal' }
    if ($member.IsFamily) { return 'protected' }
    return $null
}

# static、abstract、virtual、override、sealed override；介面實作（virtual + final、不是 override）不加。
function Get-Modifiers([Reflection.MethodBase]$method) {
    $words = @()
    if ($method.IsStatic) { $words += 'static' }

    if ($method -is [Reflection.MethodInfo] -and $method.IsVirtual -and -not $method.DeclaringType.IsInterface) {
        $isOverride = $method.GetBaseDefinition().DeclaringType -ne $method.DeclaringType
        if ($method.IsAbstract) { $words += 'abstract' }
        elseif ($isOverride -and $method.IsFinal) { $words += 'sealed override' }
        elseif ($isOverride) { $words += 'override' }
        elseif (-not $method.IsFinal) { $words += 'virtual' }
    }

    return ($words -join ' ')
}

function Format-Parameters([Reflection.ParameterInfo[]]$parameters) {
    $parts = foreach ($p in $parameters) {
        $text = (Get-TypeName $p.ParameterType) + ' ' + $p.Name
        if ($p.ParameterType.IsByRef) { $text = $(if ($p.IsOut) { 'out ' } else { 'ref ' }) + $text }
        if ($p.IsDefined([ParamArrayAttribute], $false)) { $text = 'params ' + $text }
        if ($p.HasDefaultValue) { $text += ' = ' + (Format-Value $p.DefaultValue) }
        $text
    }

    return ($parts -join ', ')
}

function Join-Words([string[]]$words) {
    return (($words | Where-Object { $_ }) -join ' ')
}

function Get-TypeLine([Type]$type) {
    $kind =
        if ($type.IsEnum) { 'enum' }
        elseif ($type.IsInterface) { 'interface' }
        elseif ($type.IsValueType) { 'struct' }
        elseif ($type.IsSubclassOf([Delegate])) { 'delegate' }
        else { 'class' }

    $modifiers =
        if ($kind -ne 'class') { $null }
        elseif ($type.IsAbstract -and $type.IsSealed) { 'static' }
        elseif ($type.IsAbstract) { 'abstract' }
        elseif ($type.IsSealed) { 'sealed' }
        else { $null }

    $bases = @()
    if ($kind -eq 'class' -and $type.BaseType -ne [object]) { $bases += Get-TypeName $type.BaseType }
    if ($kind -eq 'enum') { $bases += Get-TypeName ([Enum]::GetUnderlyingType($type)) }
    if ($kind -ne 'delegate' -and $kind -ne 'enum') {
        $bases += $type.GetInterfaces() | Where-Object { $_.IsVisible } | ForEach-Object { Get-TypeName $_ } | Sort-Object
    }

    $line = Join-Words @('type', 'public', $modifiers, $kind)
    if ($bases.Count -gt 0) { $line += ' : ' + ($bases -join ', ') }
    return $line
}

function Get-MemberLines([Type]$type) {
    if ($type.IsEnum) {
        foreach ($field in $type.GetFields([Reflection.BindingFlags]'Public, Static')) {
            'value ' + $field.Name + ' = ' + [Convert]::ToString($field.GetRawConstantValue(), [Globalization.CultureInfo]::InvariantCulture)
        }
        return
    }

    if ($type.IsSubclassOf([Delegate])) {
        $invoke = $type.GetMethod('Invoke')
        'signature ' + (Get-TypeName $invoke.ReturnType) + '(' + (Format-Parameters $invoke.GetParameters()) + ')'
        return
    }

    foreach ($member in $type.GetMembers($flags)) {
        switch ($member.MemberType) {
            'Constructor' {
                $access = Get-Access $member
                if (-not $access) { continue }
                Join-Words @($access, (Get-Modifiers $member), ('ctor(' + (Format-Parameters $member.GetParameters()) + ')'))
            }

            'Method' {
                $access = Get-Access $member
                if (-not $access -or $member.IsSpecialName) { continue }
                $generic = if ($member.IsGenericMethodDefinition) { '<' + (($member.GetGenericArguments() | ForEach-Object Name) -join ', ') + '>' } else { '' }
                Join-Words @($access, (Get-Modifiers $member), 'method', (Get-TypeName $member.ReturnType),
                    ($member.Name + $generic + '(' + (Format-Parameters $member.GetParameters()) + ')'))
            }

            'Field' {
                $access = Get-Access $member
                if (-not $access) { continue }
                $kind = if ($member.IsLiteral) { 'const' } elseif ($member.IsStatic -and $member.IsInitOnly) { 'static readonly' } elseif ($member.IsStatic) { 'static' } elseif ($member.IsInitOnly) { 'readonly' } else { $null }
                $line = Join-Words @($access, $kind, 'field', (Get-TypeName $member.FieldType), $member.Name)
                if ($member.IsLiteral) { $line += ' = ' + (Format-Value $member.GetRawConstantValue()) }
                $line
            }

            'Property' {
                $accessors = @(@($member.GetGetMethod($true), $member.GetSetMethod($true)) | Where-Object { $_ -and (Get-Access $_) })
                if ($accessors.Count -eq 0) { continue }
                $access = ($accessors | ForEach-Object { Get-Access $_ } | Sort-Object { @('public', 'protected internal', 'protected').IndexOf($_) } | Select-Object -First 1)
                $parts = foreach ($accessor in $accessors) {
                    $word = if ($accessor.Name.StartsWith('get_')) { 'get;' } else { 'set;' }
                    $accessorAccess = Get-Access $accessor
                    if ($accessorAccess -ne $access) { $word = $accessorAccess + ' ' + $word }
                    $word
                }
                $indexers = $member.GetIndexParameters()
                $name = if ($indexers.Count -gt 0) { $member.Name + '[' + (Format-Parameters $indexers) + ']' } else { $member.Name }
                Join-Words @($access, (Get-Modifiers $accessors[0]), 'property', (Get-TypeName $member.PropertyType), $name, ('{ ' + ($parts -join ' ') + ' }'))
            }

            'Event' {
                $add = $member.GetAddMethod($true)
                $access = Get-Access $add
                if (-not $access) { continue }
                Join-Words @($access, (Get-Modifiers $add), 'event', (Get-TypeName $member.EventHandlerType), $member.Name)
            }
        }
    }
}

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)

$lines = New-Object 'System.Collections.Generic.List[string]'
foreach ($type in $assembly.GetTypes() | Where-Object { $_.IsVisible }) {
    $prefix = (Get-TypeName $type) + ' :: '
    $lines.Add($prefix + (Get-TypeLine $type))
    foreach ($line in Get-MemberLines $type) { $lines.Add($prefix + $line) }
}

$lines.Sort([StringComparer]::Ordinal)

if ($OutFile) {
    $path = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).Path, $OutFile))
    [IO.File]::WriteAllLines($path, $lines, (New-Object Text.UTF8Encoding $true))
}
else {
    $lines
}
