# 用途：编译；加 -Install 时将 DLL 安装到游戏 Mods。
# 用法：pwsh -File build\build.ps1 -GameDir "<游戏目录>\Package" [-Install]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path (Join-Path $GameDir "Sinmai_Data"))) {
    throw "游戏目录不对：$GameDir（要指到 Package，里面有 Sinmai_Data）"
}

if ($Install -and (Get-Process Sinmai -ErrorAction SilentlyContinue)) {
    throw "请先退出游戏，再使用 -Install 安装。"
}

Write-Host "编译..."
Push-Location $root
try {
    & dotnet build MaiPlannerMod.csproj -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
}
finally {
    Pop-Location
}

$dll = Join-Path $root "bin\Release\MaiPlannerMod.dll"
if (-not (Test-Path $dll)) { throw "没找到产物：$dll" }
if (-not $Install) {
    Write-Host "编译完成：$dll"
    return
}

$mods = Join-Path $GameDir "Mods"
New-Item -ItemType Directory -Force -Path $mods | Out-Null
Copy-Item $dll (Join-Path $mods "MaiPlannerMod.dll") -Force

Write-Host ""
Write-Host "已安装：$(Join-Path $mods 'MaiPlannerMod.dll')" -ForegroundColor Green
Write-Host "起游戏后打开 http://127.0.0.1:8790/"
