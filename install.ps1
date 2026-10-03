#requires -Version 5.1
<#
.SYNOPSIS
  Kimi Code CLI 中文汉化离线安装包（离线优先，国内镜像自动回退）
.DESCRIPTION
  - 全部用户级安装，不需要管理员权限
  - 内嵌 Node.js 22 运行时 + 汉化版 kimi-code 2.1.1（TUI/CLI 全量中文）+ PortableGit 2.56.0
  - 内嵌资源缺失/校验失败时自动回退国内镜像下载
  - 安装位置遵循官方约定 %USERPROFILE%\.kimi-code
  - 自动写入 mainland-cn 区域标记与全局中文回复指令（~/.kimi-code/AGENTS.md）
.PARAMETER Repair
  忽略已安装的同版本，强制重装
.PARAMETER OfflineOnly
  禁止任何联网下载；内嵌资源损坏时直接报错退出
.PARAMETER GlobalRegion
  写入 global 区域标记（默认 mainland-cn，影响首次登录的 OAuth 端点）
.PARAMETER NoPath
  不修改用户 PATH
#>
param(
  [switch]$Repair,
  [switch]$OfflineOnly,
  [switch]$GlobalRegion,
  [switch]$NoPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:BundleRoot = $PSScriptRoot
if (-not $script:BundleRoot) { $script:BundleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$ResDir  = Join-Path $script:BundleRoot 'resources'
$LogFile = Join-Path $env:TEMP 'kimi-zh-bundle-install.log'
$Versions = Get-Content -LiteralPath (Join-Path $script:BundleRoot 'versions.json') -Raw -Encoding UTF8 | ConvertFrom-Json

# —— 安装位置（全部在用户目录下，无需管理员）——
$Home0    = Join-Path $env:USERPROFILE '.kimi-code'
$BinDir   = Join-Path $Home0 'bin'
$AppDir   = Join-Path $Home0 'app'
$NodeRoot = Join-Path $Home0 'runtime'
$NodeDir  = Join-Path $NodeRoot 'node'
$MainJs   = Join-Path $AppDir 'node_modules\@moonshot-ai\kimi-code\dist\main.mjs'
$GitDir   = Join-Path $env:LOCALAPPDATA 'Programs\Git'

$script:UsedMirror = New-Object System.Collections.Generic.List[string]

# ============================ 基础工具 ============================

function Write-Log {
  param([string]$Message, [string]$Level = 'INFO', [string]$Color = 'Gray')
  $line = '[{0}] [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
  try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch { }
  Write-Host $Message -ForegroundColor $Color
}
function Write-Step { param([string]$m) Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan; Add-Content -LiteralPath $LogFile -Value "`n==> $m" -Encoding UTF8 }
function Write-Ok   { param([string]$m) Write-Log -Message $m -Level 'OK' -Color 'Green' }
function Write-Warn { param([string]$m) Write-Log -Message $m -Level 'WARN' -Color 'Yellow' }
function Write-Err  { param([string]$m) Write-Log -Message $m -Level 'ERROR' -Color 'Red' }

function Get-Sha256 {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return '' }
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-Resource {
  param([string]$RelativePath, [string]$ExpectedSha256)
  $full = Join-Path $ResDir $RelativePath
  if (-not (Test-Path -LiteralPath $full)) { return $false }
  if (-not $ExpectedSha256) { return $true }
  return ((Get-Sha256 -Path $full) -eq $ExpectedSha256.ToLowerInvariant())
}

function Invoke-Download {
  param([string[]]$Urls, [string]$OutFile, [string]$Label)
  $tmp = "$OutFile.downloading"
  if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
  foreach ($u in $Urls) {
    Write-Log "  尝试下载: $u"
    try {
      if ($curl) {
        & $curl.Source -L --fail --silent --show-error --connect-timeout 20 --max-time 3600 -o $tmp $u 2>$null
        $ok = ($LASTEXITCODE -eq 0)
      } else {
        Invoke-WebRequest -Uri $u -OutFile $tmp -UseBasicParsing -TimeoutSec 3600
        $ok = $true
      }
      if ($ok -and (Test-Path -LiteralPath $tmp) -and ((Get-Item -LiteralPath $tmp).Length -gt 0)) {
        Move-Item -LiteralPath $tmp -Destination $OutFile -Force
        Write-Ok "  下载完成: $Label ($('{0:N1}' -f ((Get-Item -LiteralPath $OutFile).Length / 1MB)) MB)"
        return $true
      }
    } catch {
      Write-Warn "  下载失败: $($_.Exception.Message)"
    }
  }
  if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  return $false
}

function Add-UserPath {
  param([string]$Directory)
  $cur = [Environment]::GetEnvironmentVariable('Path', 'User')
  if (-not $cur) { $cur = '' }
  $parts = @($cur -split ';' | Where-Object { $_ -and $_.Trim() })
  if ($parts -notcontains $Directory) {
    $new = (@($Directory) + $parts) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $new, 'User')
    Write-Ok "已写入用户 PATH: $Directory"
  }
  if (-not (($env:Path -split ';') -contains $Directory)) { $env:Path = "$Directory;$env:Path" }
}

function Remove-UserPath {
  param([string]$Directory)
  $cur = [Environment]::GetEnvironmentVariable('Path', 'User')
  if (-not $cur) { return }
  $parts = @($cur -split ';' | Where-Object { $_ -and $_.Trim() -and ($_.TrimEnd('\') -ne $Directory.TrimEnd('\')) })
  if ($parts.Count -ne @($cur -split ';' | Where-Object { $_ -and $_.Trim() }).Count) {
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
    Write-Ok "已从用户 PATH 移除: $Directory"
  }
}

function Expand-ZipSafe {
  param([string]$ZipPath, [string]$Destination)
  if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

# ============================ 主程序：kimi-code 汉化版 ============================

function Install-Kimi {
  Write-Step "安装 Kimi Code CLI $($Versions.kimiCode.version) 中文汉化版（内嵌 Node $($Versions.node.version)）"

  # 已装同版本且资源完好则跳过
  $marker = Join-Path $AppDir '.kimi-zh-version'
  if (-not $Repair -and (Test-Path -LiteralPath $MainJs) -and (Test-Path -LiteralPath $marker)) {
    $installedVer = (Get-Content -LiteralPath $marker -Raw -ErrorAction SilentlyContinue).Trim()
    if ($installedVer -eq $Versions.kimiCode.version) {
      Write-Ok "检测到 Kimi Code $installedVer 中文版已安装，跳过（如需重装请加 -Repair 参数）"
      return
    }
  }

  # 路径 1：内嵌资源离线安装
  $appZip = Join-Path $ResDir "kimi-code\$($Versions.appZip.file)"
  if (Test-Resource -RelativePath "kimi-code\$($Versions.appZip.file)" -ExpectedSha256 $Versions.appZip.sha256) {
    Expand-ZipSafe -ZipPath $appZip -Destination $AppDir
    Write-Ok "已从内嵌资源离线安装汉化版 kimi-code（未联网）"
  }
  # 路径 2：镜像回退（在线 npm 安装英文原版，界面将不是中文）
  elseif ($OfflineOnly) {
    throw "内嵌 app.zip 缺失或校验失败，且已指定 -OfflineOnly 禁止联网，无法继续。"
  }
  else {
    Write-Warn "内嵌 app.zip 缺失或校验失败，自动回退：用内嵌 Node 的 npm 从镜像安装英文原版..."
    $script:UsedMirror.Add('kimi-code（npm 镜像回退，英文界面）')
    Install-NodeFromResources | Out-Null
    $npmCli = Join-Path $NodeDir 'node_modules\npm\bin\npm-cli.js'
    $env:PATH = "$NodeDir;$env:PATH"
    $reg = $Versions.mirrors.npmRegistry
    & (Join-Path $NodeDir 'node.exe') $npmCli install --global --prefix $AppDir "$($Versions.kimiCode.npmPackage)@$($Versions.kimiCode.version)" --registry=$reg --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
      & (Join-Path $NodeDir 'node.exe') $npmCli install --global --prefix $AppDir "$($Versions.kimiCode.npmPackage)@$($Versions.kimiCode.version)" --registry=https://registry.npmjs.org --no-audit --no-fund
      if ($LASTEXITCODE -ne 0) { throw "npm 镜像与官方源均安装失败。" }
    }
    Write-Warn "已安装英文原版 kimi-code（回退路径无汉化）。"
  }

  Set-Content -LiteralPath $marker -Value $Versions.kimiCode.version -Encoding ASCII

  # Node 运行时（汉化版始终使用内嵌运行时，保证行为一致）
  Install-NodeFromResources

  # 写入启动器
  New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
  $nodeExe = Join-Path $NodeDir 'node.exe'
  $kimiCmd = Join-Path $BinDir 'kimi.cmd'
  @'
@echo off
"%~dp0..\runtime\node\node.exe" "%~dp0..\app\node_modules\@moonshot-ai\kimi-code\dist\main.mjs" %*
exit /b %ERRORLEVEL%
'@ | Set-Content -LiteralPath $kimiCmd -Encoding ASCII
  $kimiSh = Join-Path $BinDir 'kimi'
  @'
#!/bin/sh
exec "$(dirname "$0")/../runtime/node/node.exe" "$(dirname "$0")/../app/node_modules/@moonshot-ai/kimi-code/dist/main.mjs" "$@"
'@ | Set-Content -LiteralPath $kimiSh -Encoding ASCII

  # 旧的原生 kimi.exe 会让 .cmd 启动器被 PATHEXT 遮蔽，先备份
  $nativeExe = Join-Path $BinDir 'kimi.exe'
  if (Test-Path -LiteralPath $nativeExe) {
    $bak = "$nativeExe.zh-bak"
    try { Move-Item -LiteralPath $nativeExe -Destination $bak -Force; Write-Warn "检测到官方原生 kimi.exe，已备份为 kimi.exe.zh-bak（删除 .zh-bak 可还原）" } catch { Write-Warn "无法备份 kimi.exe: $($_.Exception.Message)" }
  }

  if (-not $NoPath) { Add-UserPath -Directory $BinDir }

  # 区域标记（与官方安装器一致：只在不存在时写入）
  $region = if ($GlobalRegion) { 'global' } else { 'mainland-cn' }
  $regionMarker = Join-Path $Home0 'region'
  if (-not (Test-Path -LiteralPath $regionMarker)) {
    [System.IO.File]::WriteAllText($regionMarker, "$region`n")
    Write-Ok "已写入区域标记: $region（决定首次登录使用的 OAuth 端点）"
  }

  # 便携版 npm 默认走国内镜像（只影响该便携 npm，不改系统配置）
  $npmrc = Join-Path $NodeDir 'node_modules\npm\npmrc'
  try { Set-Content -LiteralPath $npmrc -Value "registry=$($Versions.mirrors.npmRegistry)" -Encoding ASCII } catch { }

  # 验证
  $verOut = (& $kimiCmd --version 2>&1 | Out-String)
  if ($verOut -notlike "*$($Versions.kimiCode.version)*") { throw "kimi 启动器无法运行（$($verOut.Trim())）。" }
  Write-Ok "Kimi Code 中文版就绪: kimi $($verOut.Trim())"
}

function Install-NodeFromResources {
  if (Test-Path -LiteralPath (Join-Path $NodeDir 'node.exe')) { return }
  $v = $Versions.node.version
  $zipName = $Versions.node.file
  $zip = Join-Path $ResDir "node\$zipName"
  if (Test-Resource -RelativePath "node\$zipName" -ExpectedSha256 $Versions.node.sha256) {
    Write-Log "  使用内嵌便携版 Node.js v$v（未联网）"
  } elseif ($OfflineOnly) {
    throw "内嵌 Node.js 缺失或校验失败，且已指定 -OfflineOnly。"
  } else {
    Write-Warn "内嵌 Node.js 缺失或校验失败，自动回退：从国内镜像下载 Node.js v$v ..."
    $script:UsedMirror.Add("Node.js v$v（镜像下载）")
    $dl = Join-Path $env:TEMP $zipName
    $ok = Invoke-Download -Label "Node.js v$v" -OutFile $dl -Urls @(
      "$($Versions.mirrors.nodeDist)/v$v/$zipName",
      "$($Versions.mirrors.nodeDistAlt)/v$v/$zipName",
      "$($Versions.mirrors.nodeDistOfficial)/v$v/$zipName"
    )
    if (-not $ok) { throw "Node.js 安装包获取失败。" }
    $zip = $dl
  }
  $stage = Join-Path $NodeRoot 'stage'
  Expand-ZipSafe -ZipPath $zip -Destination $stage
  $inner = Get-ChildItem -LiteralPath $stage -Directory | Select-Object -First 1
  if (-not $inner) { throw "Node.js 压缩包结构异常。" }
  if (Test-Path -LiteralPath $NodeDir) { Remove-Item -LiteralPath $NodeDir -Recurse -Force }
  Move-Item -LiteralPath $inner.FullName -Destination $NodeDir
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
  Write-Ok "Node.js v$v 便携版就绪: $NodeDir"
}

# ============================ 全局中文回复 ============================

function Set-ChineseReply {
  Write-Step "配置全局简体中文回复（~/.kimi-code/AGENTS.md）"
  New-Item -ItemType Directory -Force -Path $Home0 | Out-Null
  $md = Join-Path $Home0 'AGENTS.md'
  $block = @"
## 语言 / Language
<!-- kimi-zh-bundle:language BEGIN -->
- 始终使用简体中文回复（代码、命令、专有名词除外），除非用户明确要求其他语言。
- Always respond in Simplified Chinese unless the user explicitly asks for another language.
<!-- kimi-zh-bundle:language END -->
"@
  if (Test-Path -LiteralPath $md) {
    $content = Get-Content -LiteralPath $md -Raw -Encoding UTF8
    if ($content -notmatch 'kimi-zh-bundle:language BEGIN') {
      Add-Content -LiteralPath $md -Value "`n$block" -Encoding UTF8
      Write-Ok "已在全局 AGENTS.md 追加中文回复规则（幂等标记块）"
    } else {
      Write-Log "  全局 AGENTS.md 已包含中文回复规则，跳过"
    }
  } else {
    Set-Content -LiteralPath $md -Value $block -Encoding UTF8
    Write-Ok "已创建全局 AGENTS.md 并写入中文回复规则"
  }
}

# ============================ 依赖：Git Bash ============================

function Test-GitBashAvailable {
  if (Get-Command bash.exe -ErrorAction SilentlyContinue) { return $true }
  if (Get-Command git.exe -ErrorAction SilentlyContinue) {
    # git 存在时 kimi 会从 git 路径推导 Git Bash（MinGit 除外）
    return $true
  }
  foreach ($p in @(
    (Join-Path $GitDir 'bin\bash.exe'),
    'C:\Program Files\Git\bin\bash.exe',
    'C:\Program Files (x86)\Git\bin\bash.exe'
  )) { if (Test-Path -LiteralPath $p) { return $true } }
  return $false
}

function Install-GitBashIfNeeded {
  Write-Step "检查 Git Bash（kimi-code 在 Windows 上的 shell 依赖）"
  if (Test-GitBashAvailable) {
    $g = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($g) { Write-Ok "检测到 Git: $((& git --version 2>$null))（kimi 将自动使用其 Git Bash）" }
    else { Write-Ok "检测到 Git Bash" }
    return
  }

  $bashExe = Join-Path $GitDir 'bin\bash.exe'
  $sfxName = $Versions.git.file
  $sfx = Join-Path $ResDir "git\$sfxName"

  # 曾经由本安装包装过
  if (-not (Test-Path -LiteralPath $sfx) -and (Test-Path -LiteralPath $bashExe)) {
    Write-Ok "Git Bash 已存在: $bashExe"
    return
  }

  if (Test-Resource -RelativePath "git\$sfxName" -ExpectedSha256 $Versions.git.sha256) {
    Write-Log "  使用内嵌 PortableGit $($Versions.git.version)（未联网，静默解压约需 1 分钟）"
  } elseif ($OfflineOnly) {
    Write-Warn "内嵌 PortableGit 缺失且已指定 -OfflineOnly。请手动安装 Git for Windows: https://gitforwindows.org/"
    return
  } else {
    Write-Warn "内嵌 PortableGit 缺失或校验失败，自动回退：从国内镜像下载..."
    $script:UsedMirror.Add("PortableGit $($Versions.git.version)（镜像下载）")
    $dl = Join-Path $env:TEMP $sfxName
    $gh = "$($Versions.mirrors.gitForWindowsOfficial)/v$($Versions.git.version).windows.1/$sfxName"
    $urls = @(
      "$($Versions.mirrors.gitForWindows)/v$($Versions.git.version).windows.1/$sfxName"
    ) + @($Versions.mirrors.githubProxies | ForEach-Object { $_ + $gh }) + @($gh)
    if (-not (Invoke-Download -Label "PortableGit $($Versions.git.version)" -OutFile $dl -Urls $urls)) {
      Write-Warn "PortableGit 下载失败。请手动安装 Git for Windows: https://gitforwindows.org/"
      return
    }
    $sfx = $dl
  }

  New-Item -ItemType Directory -Force -Path $GitDir | Out-Null
  $proc = Start-Process -FilePath $sfx -ArgumentList "-o`"$GitDir`"","-y" -Wait -PassThru
  if (-not (Test-Path -LiteralPath $bashExe)) { Write-Warn "PortableGit 解压后未找到 bash.exe，请手动安装 Git for Windows。"; return }
  Set-Content -LiteralPath (Join-Path $GitDir '.kimi-zh-installed') -Value $Versions.git.version -Encoding ASCII

  # KIMI_SHELL_PATH 是 kimi-code 官方支持的 bash 指定方式（优先级最高）
  [Environment]::SetEnvironmentVariable('KIMI_SHELL_PATH', $bashExe, 'User')
  Add-UserPath -Directory (Join-Path $GitDir 'cmd')
  Write-Ok "PortableGit $($Versions.git.version) 已安装: $GitDir"
  Write-Ok "已设置用户环境变量 KIMI_SHELL_PATH=$bashExe"
}

# ============================ 主流程 ============================

function Show-Summary {
  Write-Host ''
  Write-Host '================== 安装结果 ==================' -ForegroundColor Cyan
  $rows = @()
  if (Test-Path -LiteralPath $MainJs) {
    $rows += [pscustomobject]@{ 组件 = 'Kimi Code CLI (中文版)'; 位置 = $AppDir; 状态 = "v$($Versions.kimiCode.version)" }
  }
  if (Test-Path -LiteralPath (Join-Path $NodeDir 'node.exe')) {
    $rows += [pscustomobject]@{ 组件 = 'Node.js (内嵌运行时)'; 位置 = $NodeDir; 状态 = "v$($Versions.node.version)" }
  }
  if (Test-Path -LiteralPath (Join-Path $GitDir 'bin\bash.exe')) {
    $rows += [pscustomobject]@{ 组件 = 'PortableGit'; 位置 = $GitDir; 状态 = "v$($Versions.git.version)" }
  }
  $rows | Format-Table -AutoSize | Out-Host
  if ($script:UsedMirror.Count -gt 0) {
    Write-Warn "本次安装以下组件走了镜像下载回退: $($script:UsedMirror -join '、')"
  } else {
    Write-Ok "本次安装全程使用内嵌资源，未联网下载。"
  }
  Write-Host '----------------------------------------------'
  Write-Host '使用提示:' -ForegroundColor Cyan
  Write-Host '  1. 新开一个终端窗口，输入 kimi 即可启动（当前已打开的终端需重开才能识别 PATH）。'
  Write-Host '  2. 界面已全量汉化（菜单/对话框/状态栏/帮助/CLI 帮助）。'
  Write-Host '  3. 对话回复语言已通过 ~/.kimi-code/AGENTS.md 设为简体中文。'
  Write-Host '  4. 首次使用请在 kimi 中运行 /login 登录（Kimi 账号 OAuth 或 API Key）。'
  Write-Host '  5. 更新版本：重新运行本安装包（内嵌新版即可），或 npm i -g @moonshot-ai/kimi-code@latest（将失去汉化）。'
  Write-Host "  6. 安装日志: $LogFile"
  Write-Host '==============================================' -ForegroundColor Cyan
}

try {
  Write-Host ''
  Write-Host '  Kimi Code CLI 中文汉化离线安装包 v' -NoNewline -ForegroundColor Cyan
  Write-Host $Versions.bundleVersion -ForegroundColor Cyan
  Write-Host '  （离线优先 · 国内镜像自动回退 · 界面全量汉化 · 对话中文）' -ForegroundColor DarkCyan
  Write-Host "  资源目录: $ResDir"

  if (-not (Test-Path -LiteralPath $ResDir)) { throw "未找到 resources 资源目录，请确认解压完整后再运行。" }

  Install-Kimi
  Set-ChineseReply
  Install-GitBashIfNeeded

  Show-Summary
  exit 0
} catch {
  Write-Err "安装失败: $($_.Exception.Message)"
  Write-Err "详细日志: $LogFile"
  exit 1
}
