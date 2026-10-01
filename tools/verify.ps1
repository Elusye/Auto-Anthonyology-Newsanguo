# 一次性跑完四道门禁；任何一道失败 → 非 0 退出。
#
# 为什么需要它：覆盖率会因为**无关改动**悄悄下降 —— 实测掉过 2 条，
# 而真凶是我自己为了修卡面显示、把阈值文案从阿拉伯数字改成了汉字，
# 打断了反推工具的字面匹配。当时我先把责任归给了触发原子（错了一次）。
# 单条命令 + 覆盖率下限，能让这种回归当场暴露，而不是靠事后回忆。
#
# 用法：
#   pwsh -File tools\verify.ps1              # 只验证
#   pwsh -File tools\verify.ps1 -Deploy      # 验证并部署到 mods（需要 danger-full-access）

param(
    [switch]$Deploy,
    # 覆盖率下限（百分比）。**只能升不能降**；覆盖率提升后记得一起提高这个数字。
# 52 -> 54：把"未解析占位符当值通配"之后，触发类子句终于能匹配上了。
    [int]$MinRate = 54,
    [string]$Python = "C:\Users\chief\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path $Python)) { $Python = 'python' }

$failed = @()

function Step($name, [scriptblock]$body) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

Step "1/4 构建" {
    $args = @("build", "$root\Auto-Anthonyology-Newsanguo.csproj", "-c", "Release", "-v", "minimal", "--no-incremental")
    if ($Deploy) { $args += "/p:DeployToMods=true" }
    dotnet @args 2>&1 | Select-String -Pattern 'error|warning CS|Deploying|已成功|失败' | Select-Object -First 8
}

Step "2/4 冒烟检查（48 项 + 12 种子）" {
    $out = dotnet run --project "$root\tools\SmokeTest\SmokeTest.csproj" -c Release -- `
        "$root\bin\Release\autoanthony_newsanguo.dll" 2>&1
    $p = ($out | Select-String -Pattern '^\s+PASS').Count
    $f = ($out | Select-String -Pattern '^\s+FAIL').Count
    Write-Host "  PASS=$p FAIL=$f"
    $out | Select-String -Pattern 'FAIL|不同种子|==>' | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
    if ($f -gt 0) { $global:LASTEXITCODE = 1 }
}

Step "3/4 反推覆盖率（下限 $MinRate%）" {
    & $Python "$root\tools\derive_catalog.py" --min-rate $MinRate 2>&1 | ForEach-Object { Write-Host "  $_" }
}

Step "4/4 基线审计（文案数字 / 缺基线 必须为 0）" {
    & $Python "$root\tools\audit_baselines.py" 2>&1 | ForEach-Object { Write-Host "  $_" }
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "全部门禁通过。" -ForegroundColor Green
    exit 0
} else {
    Write-Host "失败的门禁：$($failed -join '、')" -ForegroundColor Red
    exit 1
}
