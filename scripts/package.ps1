# 打发布包：dist\deepseek-pet-desktop-<版本>-win64.zip（版本取仓库根的 VERSION）。
#
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1                 # 先用 Unity 打包，再压缩
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -SkipBuild      # 直接压缩现有的 Build\
#   ... -DshPluginTgz <路径>   指定 dsh 插件包（默认找 ..\deepseek-pet\dist\dsh-pet-plugin-*.tgz，找不到就不放）
#   ... -Unity <Unity.exe>     指定 Unity 编辑器（默认 Unity Hub 装的 2022.3.15f1）
#
# zip 里：桌宠程序、连接AI助手.cmd / 断开AI助手.cmd（+ tools\connect.ps1）、dsh 插件包、使用说明、许可。
param(
    [switch]$SkipBuild,
    [string]$DshPluginTgz,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.15f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $repo "VERSION")).Trim()
$build = Join-Path $repo "Build"
$dist = Join-Path $repo "dist"
$name = "deepseek-pet-desktop-$version-win64"

if (-not $SkipBuild) {
    Write-Host "Unity 打包……"
    $log = Join-Path $env:TEMP "deepseek-pet-build.log"
    $p = Start-Process -FilePath $Unity -ArgumentList @("-batchmode", "-nographics", "-quit", "-projectPath", "`"$(Join-Path $repo 'DeepseekPet')`"",
        "-executeMethod", "DeepseekPet.EditorTools.PetBuild.BuildWindows", "-logFile", "`"$log`"") -PassThru -Wait -NoNewWindow
    if ($p.ExitCode -ne 0) { throw "Unity 打包失败（退出码 $($p.ExitCode)），日志：$log" }
}
if (-not (Test-Path (Join-Path $build "DeepseekPet.exe"))) { throw "Build\DeepseekPet.exe 不存在：先去掉 -SkipBuild 打一次包" }

if (-not $DshPluginTgz) {
    $sibling = Join-Path (Split-Path -Parent $repo) "deepseek-pet\dist"
    $found = Get-ChildItem -Path $sibling -Filter "dsh-pet-plugin-*.tgz" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($found) { $DshPluginTgz = $found.FullName }
}

$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
# Unity 的产物；Burst 调试信息不发（名字里就写着 DoNotShip）。
Get-ChildItem $build | Where-Object { $_.Name -notlike "*DoNotShip*" } | Copy-Item -Destination $stage -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot "zip\*") -Destination $stage -Recurse
Copy-Item (Join-Path $repo "LICENSE"), (Join-Path $repo "THIRD_PARTY.md") -Destination $stage
if ($DshPluginTgz) {
    Copy-Item $DshPluginTgz -Destination $stage
    Write-Host "带上 dsh 插件：$DshPluginTgz"
} else {
    Write-Warning "没找到 dsh 插件包：zip 里不带，「连接AI助手」会跳过 DeepSeek Harness"
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# 显式用 UTF-8 写文件名：「连接AI助手.cmd」这种中文名在别的机器上解压出来才不乱码。
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true, [Text.Encoding]::UTF8)
Remove-Item $stage -Recurse -Force
Write-Host ("已打包：{0}（{1:N1} MB）" -f $zip, ((Get-Item $zip).Length / 1MB))
