#Requires -Version 5.1
<#
.SYNOPSIS
  コミット前の検査をまとめて走らせる。

.DESCRIPTION
  BOM → ビルド → 整形 → テスト → 契約の照合。1 つでも落ちたら終了コード 1 を返す。

  決めごと 4 つ:

  1. **途中で落ちても最後まで走らせる。**
     ビルドが落ちた時点で止めると、テストと契約の照合が通るのかが分からない。
     1 回の実行で直すべき箇所を全部出す。

  2. **ESLint / Prettier に相当するものは足さない。**
     整形は dotnet format（SDK 同梱）、静的検査はコンパイラ警告
     （TreatWarningsAsErrors）と check-contract.mjs で足りている。
     依存を増やすと、検査そのものが壊れたときに直す相手が増える。

  3. **整形の検査はここでやる。編集のたびには走らせない。**
     dotnet format は 1 ファイル指定でも 3.7 秒かかる（実測）。
     保存のたびに待たされると、そのうち検査ごと切られる。

  4. **.ps1 の BOM を検査する。**
     Windows PowerShell 5.1 は BOM 無しの .ps1 を CP932 として読む。
     UTF-8 で保存した日本語がパース時点で壊れ、スクリプトが出すメッセージが
     丸ごと読めなくなる。エディタや別のツールが BOM を落とすことがあるので、
     気づける場所を 1 つ作っておく。

.EXAMPLE
  powershell -NoProfile -File scripts\check.ps1
  powershell -NoProfile -File scripts\check.ps1 -SkipTests
  powershell -NoProfile -File scripts\check.ps1 -Fix      # 整形の差分をその場で直す
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Fix
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$failures = @()

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )

    Write-Host ""
    Write-Host "-- $Name" -ForegroundColor Cyan

    $global:LASTEXITCODE = 0
    & $Body

    if ($LASTEXITCODE -ne 0) {
        $script:failures += $Name
        Write-Host "   NG" -ForegroundColor Red
    }
    else {
        Write-Host "   OK" -ForegroundColor Green
    }
}

Invoke-Step 'PowerShell スクリプトの BOM' {
    $missing = @()
    $scripts = Get-ChildItem -Path $root -Recurse -Filter *.ps1 -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.git)\\' }

    foreach ($script in $scripts) {
        $bytes = [System.IO.File]::ReadAllBytes($script.FullName)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        if (-not $hasBom) {
            $missing += $script.FullName.Substring($root.Length).TrimStart('\')
        }
    }

    if ($missing.Count -gt 0) {
        Write-Host ("   BOM がありません: " + ($missing -join ', ')) -ForegroundColor Red
        Write-Host "   UTF-8 (BOM 付き) で保存し直してください。BOM が無いと 5.1 は CP932 として読みます" -ForegroundColor Red
        $global:LASTEXITCODE = 1
    }
}

Invoke-Step 'dotnet build' { dotnet build --nologo -v q }

if ($Fix) {
    Invoke-Step 'dotnet format whitespace (--Fix)' {
        dotnet format whitespace --no-restore --verbosity quiet
    }
}
else {
    Invoke-Step 'dotnet format whitespace --verify-no-changes' {
        dotnet format whitespace --no-restore --verify-no-changes --verbosity quiet
    }
}

if ($SkipTests) {
    Write-Host ""
    Write-Host "-- dotnet test (-SkipTests でとばしました)" -ForegroundColor DarkGray
}
else {
    Invoke-Step 'dotnet test' { dotnet test --nologo -v q }
}

if ($null -eq (Get-Command node -ErrorAction SilentlyContinue)) {
    $failures += 'check-contract (node が見つかりません)'
    Write-Host ""
    Write-Host "-- check-contract" -ForegroundColor Cyan
    Write-Host "   NG  node が見つかりません。Node.js を入れてください" -ForegroundColor Red
}
else {
    Invoke-Step 'check-contract' { node scripts/check-contract.mjs }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host ("check NG -- " + ($failures -join ', ')) -ForegroundColor Red
    exit 1
}

Write-Host "check OK" -ForegroundColor Green
exit 0
