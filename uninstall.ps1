#requires -Version 5.1
<#
.SYNOPSIS
  Kimi Code CLI 中文汉化离线安装包 - 卸载
.DESCRIPTION
  默认保留用户数据（config.toml / 会话 / AGENTS.md），只移除程序文件与环境变量。
.PARAMETER PurgeAll
  连同全部用户数据（配置、会话、登录态、区域标记、全局 AGENTS.md 标记块）与本安装包安装的 PortableGit 一并删除
#>
param([switch]$PurgeAll)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$Home0  = Join-Path $env:USERPROFILE '.kimi-code'
$BinDir = Join-Path $Home0 'bin'
$AppDir = Join-Path $Home0 'app'
$NodeRoot = Join-Path $Home0 'runtime'
$GitDir = Join-Path $env:LOCALAPPDATA 'Programs\Git'

function Write-Ok($m) { Write-Host $m -ForegroundColor Green }
function Write-Warn($m) { Write-Host $m -ForegroundColor Yellow }

# 1. 用户 PATH 清理
foreach ($dir in @($BinDir, (Join-Path $GitDir 'cmd'))) {
  $cur = [Environment]::GetEnvironmentVariable('Path', 'User')
  if (-not $cur) { continue }
  $parts = @($cur -split ';' | Where-Object { $_ -and $_.Trim() -and ($_.TrimEnd('\') -ne $dir.TrimEnd('\')) })
  if ($parts.Count -ne @($cur -split ';' | Where-Object { $_ -and $_.Trim() }).Count) {
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
    Write-Ok "已从用户 PATH 移除: $dir"
  }
}

# 2. KIMI_SHELL_PATH 清理（仅当指向本安装包安装的 PortableGit）
$marker = Join-Path $GitDir '.kimi-zh-installed'
if ((Test-Path -LiteralPath $marker) -or $PurgeAll) {
  $cur = [Environment]::GetEnvironmentVariable('KIMI_SHELL_PATH', 'User')
  if ($cur -and $cur.StartsWith($GitDir, [System.StringComparison]::OrdinalIgnoreCase)) {
    [Environment]::SetEnvironmentVariable('KIMI_SHELL_PATH', $null, 'User')
    Write-Ok "已移除用户环境变量 KIMI_SHELL_PATH"
  }
}

# 3. AGENTS.md 中文标记块（幂等移除）
$md = Join-Path $Home0 'AGENTS.md'
if (Test-Path -LiteralPath $md) {
  $content = Get-Content -LiteralPath $md -Raw -Encoding UTF8
  if ($content -match 'kimi-zh-bundle:language BEGIN') {
    $new = ($content -split "`n" | Out-String)
    $pattern = '(?s)\r?\n## 语言 / Language\r?\n<!-- kimi-zh-bundle:language BEGIN -->.*?<!-- kimi-zh-bundle:language END -->\r?\n?'
    $new = [regex]::Replace($content, $pattern, '')
    [System.IO.File]::WriteAllText($md, $new, (New-Object System.Text.UTF8Encoding($false)))
    Write-Ok "已从 ~/.kimi-code/AGENTS.md 移除中文回复标记块"
  }
}

# 4. 程序文件
foreach ($dir in @($BinDir, $AppDir, $NodeRoot)) {
  if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force; Write-Ok "已删除: $dir" }
}

# 5. 可选：全部用户数据 + PortableGit
if ($PurgeAll) {
  if (Test-Path -LiteralPath $Home0) { Remove-Item -LiteralPath $Home0 -Recurse -Force; Write-Ok "已删除全部用户数据: $Home0" }
  if (Test-Path -LiteralPath (Join-Path $GitDir '.kimi-zh-installed')) {
    Remove-Item -LiteralPath $GitDir -Recurse -Force
    Write-Ok "已删除本安装包安装的 PortableGit: $GitDir"
  }
} else {
  Write-Warn "已保留用户数据（配置/会话/登录态）。如需彻底清除请运行: uninstall.ps1 -PurgeAll"
}

Write-Ok "卸载完成。"
