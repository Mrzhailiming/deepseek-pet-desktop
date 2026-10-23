# 一键接入 / 断开：Claude Code、Codex、DeepSeek Harness。由「连接AI助手.cmd」「断开AI助手.cmd」调用。
#
#   Claude Code       装了就往 ~/.claude/settings.json 加桌宠的钩子（先备份；交给 DeepseekPet.exe 自己的安装器做）
#   Codex             不用设置：桌宠自己读 ~/.codex/sessions
#   DeepSeek Harness  找得到 dsh 命令就把同目录的 dsh-pet-plugin-*.tgz 装进去（web profile）
# 最后把桌宠启动起来。断开时反着来，并且不启动桌宠。
param([switch]$Uninstall)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "DeepseekPet.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $root "..\..\Build\DeepseekPet.exe" }  # 在源码仓库里直接跑的时候
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$name, [string]$state, [string]$detail) {
    $results.Add([pscustomobject]@{ Name = $name; State = $state; Detail = $detail })
}

Write-Host ""
Write-Host ($(if ($Uninstall) { "  正在断开 AI 编程助手……" } else { "  正在接入 AI 编程助手……" }))
Write-Host ""

# ─── Claude Code ───
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE ".claude" }
$hasClaude = (Test-Path $claudeDir) -or (Get-Command claude -ErrorAction SilentlyContinue)
if (-not $hasClaude) {
    Add-Result "Claude Code" "跳过" "没找到 Claude Code（装好之后再点一次）"
} elseif (-not (Test-Path $exe)) {
    Add-Result "Claude Code" "失败" "找不到 DeepseekPet.exe：$exe"
} else {
    $resultFile = Join-Path $env:TEMP ("deepseek-pet-" + [guid]::NewGuid().ToString("N") + ".txt")
    $command = if ($Uninstall) { "--disconnect-claude" } else { "--connect-claude" }
    $p = Start-Process -FilePath $exe -ArgumentList @("-batchmode", "-nographics", $command, "--result", "`"$resultFile`"") -PassThru -Wait -WindowStyle Hidden
    if (Test-Path $resultFile) {
        $lines = Get-Content $resultFile -Encoding UTF8
        Remove-Item $resultFile -ErrorAction SilentlyContinue
        $state = if ($lines[0] -eq "error") { "失败" } elseif ($Uninstall) { "已断开" } else { "已接入" }
        Add-Result "Claude Code" $state ($lines[1] -replace "^已连接：|^已断开：", "")
    } else {
        Add-Result "Claude Code" "失败" ("DeepseekPet.exe 没有给出结果（退出码 " + $p.ExitCode + "）")
    }
}

# ─── Codex ───
$codexDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
if ($Uninstall) {
    Add-Result "Codex" "不用管" "桌宠只是读 Codex 的会话文件，关掉桌宠就不读了"
} elseif (Test-Path $codexDir) {
    Add-Result "Codex" "已接入" "不用设置，桌宠会自己读 $codexDir\sessions"
} else {
    Add-Result "Codex" "跳过" "没找到 Codex（装好之后不用再点，桌宠会自动接上）"
}

# ─── DeepSeek Harness ───
$dsh = Get-Command dsh -ErrorAction SilentlyContinue
$tgz = Get-ChildItem -Path $root -Filter "dsh-pet-plugin-*.tgz" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
if (-not $dsh) {
    Add-Result "DeepSeek Harness" "跳过" "没找到 dsh 命令（npm i -g @deepseek-ai/dsh 装好之后再点一次）"
} elseif (-not $Uninstall -and -not $tgz) {
    Add-Result "DeepSeek Harness" "失败" "压缩包里缺 dsh-pet-plugin-*.tgz"
} else {
    $dshArgs = if ($Uninstall) { @("plugin", "--profile", "web", "remove", "dsh-pet-plugin") } else { @("plugin", "--profile", "web", "add", $tgz.FullName) }
    $out = & $dsh.Source @dshArgs 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Add-Result "DeepSeek Harness" $(if ($Uninstall) { "已断开" } else { "已接入" }) "重启 dsh 后生效"
    } else {
        Add-Result "DeepSeek Harness" "失败" (($out -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1))
    }
}

# ─── 启动桌宠 ───
if (-not $Uninstall -and (Test-Path $exe)) {
    if (-not (Get-Process DeepseekPet -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
        Add-Result "桌宠" "已启动" "在屏幕右下角"
    } else {
        Add-Result "桌宠" "运行中" ""
    }
}

foreach ($r in $results) {
    $color = switch ($r.State) { "已接入" { "Green" } "已断开" { "Green" } "已启动" { "Green" } "运行中" { "Green" } "失败" { "Red" } default { "DarkGray" } }
    Write-Host ("  {0,-18}" -f $r.Name) -NoNewline
    Write-Host ("{0,-6}" -f $r.State) -ForegroundColor $color -NoNewline
    Write-Host ("  " + $r.Detail)
}
Write-Host ""
# 双击打开的窗口停住给人看结果；被别的程序调用（输入被重定向）时直接结束。
if (-not [Console]::IsInputRedirected) { Read-Host "  按回车关闭" | Out-Null }
