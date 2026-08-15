#Requires -Version 5.1
<#
.SYNOPSIS
  ファイルを編集した直後に走る検査（PostToolUse フック）。

.DESCRIPTION
  編集されたファイルを見て、必要なものだけ走らせる。

    *.cs                          → dotnet build（差分ビルド。実測 1.3 秒）
    Contract/*.cs                 → + node scripts/check-contract.mjs
    docs/02-http-contract.md      → node scripts/check-contract.mjs

  決めごと 6 つ:

  1. **dotnet format はここでは走らせない。**
     1 ファイル指定でも 3.7 秒かかる（実測）。編集のたびに待たされる検査は、
     そのうち丸ごと切られる。整形の検査は scripts\check.ps1 に置いてある。

  2. **失敗は exit 2 + stderr で返す。**
     PostToolUse の exit 2 は「stderr を Claude に読ませる」の意味。
     C# はコンパイルエラーが安いので、書いた直後に出すのが一番効く。

  3. **stdin と stderr は UTF-8 で明示的に扱う。**
     Windows PowerShell 5.1 の既定は CP932。素で読み書きすると、
     フックが返す日本語が丸ごと化けて読めなくなる。

  4. **子プロセスの出力に `2>&1` を使わない。**
     PowerShell は native コマンドの stderr を ErrorRecord に包み、
     $ErrorActionPreference = 'Stop' だとそこで即死する。
     node は stderr に書くので、これを踏むと exit 2 に到達せず exit 1 で終わる。
     Start-Process でファイルへ分けて受ける。

  5. **同時に走らせない。** ロックを取れなければ黙って諦める。
     並行編集のたびに dotnet build が積み上がると、編集より検査のほうが遅くなる。

  6. **リポジトリの外のファイルには何もしない。**
     判定はこのスクリプトの位置からリポジトリ根を求めて行う（cwd は当てにしない）。

.NOTES
  設定は .claude/settings.json の hooks.PostToolUse。
#>

# 明示的に扱うので Stop にはしない（決めごと 4）
$ErrorActionPreference = 'Continue'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-HookError([string]$message) {
    $stream = [Console]::OpenStandardError()
    $writer = New-Object System.IO.StreamWriter($stream, $Utf8)
    $writer.Write($message)
    $writer.Flush()
}

# ---- 1. フックの入力（JSON が stdin で来る）----------------

$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), $Utf8)
$raw = $reader.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try { $payload = $raw | ConvertFrom-Json -ErrorAction Stop } catch { exit 0 }

$filePath = $payload.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }

# ---- 2. リポジトリ根と、編集されたファイルの位置 ------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$resolved = Resolve-Path -LiteralPath $filePath -ErrorAction SilentlyContinue
if ($null -eq $resolved) { exit 0 }

$full = $resolved.Path
if (-not $full.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { exit 0 }

$relative = $full.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')

# 生成物は見ない
if ($relative -match '/(bin|obj)/') { exit 0 }

$isCSharp = $relative -like '*.cs'
$touchesContract = ($relative -like 'src/OpenAiWindowsTts/Contract/*.cs') -or
                   ($relative -eq 'docs/02-http-contract.md')

if (-not $isCSharp -and -not $touchesContract) { exit 0 }

# ---- 3. 多重起動を避ける ------------------------------------

$lockPath = Join-Path $env:TEMP 'openai-windows-tts.cs-post-edit.lock'
$lock = $null
$deadline = (Get-Date).AddSeconds(3)

while ($null -eq $lock -and (Get-Date) -lt $deadline) {
    try { $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Start-Sleep -Milliseconds 150 }
}

# 先に走っているものがあるなら任せる
if ($null -eq $lock) { exit 0 }

# ---- 4. 検査 ------------------------------------------------

<#
  native コマンドを 1 本走らせ、終了コードと出力（stdout + stderr）を返す。
  Start-Process でファイルへ分けて受けるのは、パイプで 2>&1 すると
  PowerShell が stderr を ErrorRecord に包んでしまい、本文に
  「At line:… char:…」の飾りが混ざって読めなくなるため。
#>
function Invoke-Native([string]$file, [string[]]$commandArgs) {
    $outFile = [System.IO.Path]::GetTempFileName()
    $errFile = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process -FilePath $file -ArgumentList $commandArgs `
            -WorkingDirectory $repoRoot -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile

        $text = ((Get-Content -LiteralPath $outFile -Raw -Encoding UTF8) +
                 (Get-Content -LiteralPath $errFile -Raw -Encoding UTF8))
        return @{ ExitCode = $process.ExitCode; Output = ($text -replace "`0", '') }
    }
    finally {
        Remove-Item -LiteralPath $outFile, $errFile -Force -ErrorAction SilentlyContinue
    }
}

$problems = New-Object System.Collections.Generic.List[string]

try {
    if ($isCSharp) {
        $build = Invoke-Native 'dotnet' @('build', '--nologo', '-v', 'q', '--no-restore')
        if ($build.ExitCode -ne 0) {
            $lines = @($build.Output -split "`r?`n" |
                Where-Object { $_ -match ':\s*(error|warning)\s' } |
                Select-Object -First 20)
            if ($lines.Count -eq 0) {
                $lines = @($build.Output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 20)
            }
            $problems.Add("dotnet build が通りません:`n" + ($lines -join "`n"))
        }
    }

    if ($touchesContract -and $null -ne (Get-Command node -ErrorAction SilentlyContinue)) {
        $contract = Invoke-Native 'node' @('scripts/check-contract.mjs')
        if ($contract.ExitCode -ne 0) {
            $problems.Add(
                "契約の照合が通りません。docs/02-http-contract.md §6.2 の表と" +
                " src/OpenAiWindowsTts/Contract/ErrorCodes.cs の両方を直してください:`n" +
                $contract.Output.Trim())
        }
    }
}
finally {
    $lock.Dispose()
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}

# ---- 5. 結果 ------------------------------------------------

if ($problems.Count -eq 0) { exit 0 }

Write-HookError (($problems -join "`n`n") + "`n")
exit 2
