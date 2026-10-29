# ============================================================================
# 打包「源码版」给朋友，让他在自己电脑上编译
#
# 用法：powershell -ExecutionPolicy Bypass -File pack-source-for-friend.ps1
#       powershell -ExecutionPolicy Bypass -File pack-source-for-friend.ps1 -AllChars
#       powershell -ExecutionPolicy Bypass -File pack-source-for-friend.ps1 -Char 小维
# 产物：dist\QPet-源码-<日期>.zip
#
# 源码包：对方自己编译出来的 exe 是本机新文件，不带「来自其他计算机」标记，
# Windows 不弹「已保护你的电脑」，也不需要代码签名证书。代价是要先装 .NET SDK。
# ============================================================================
param(
    [string]$OutDir = 'dist',
    [string]$Char = '',      # 指定角色目录名（如 -Char 小维）；留空 = pet-config.txt 里的当前角色
    [switch]$AllChars
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # %~dp0\.. -> 仓库根目录
Set-Location $here

function Fail($msg) { Write-Host "✗ $msg" -ForegroundColor Red; exit 1 }

# ---------- 1. 源码文件检查 ----------
# 仓库布局：C# 源码在 src\、脚本在 tools\，根目录只留用户要双击的东西
$rootFiles = @('LICENSE', 'NOTICE.md', 'README.md', 'pet.ico', 'pet.png', 'pet-placeholder.png',
               'pet-config.txt', 'pet-overrides.txt', 'start-pet.cmd', 'start-pet.vbs', 'start-settings.cmd')
$srcFiles  = @('Pet.cs', 'Program.cs', 'Core.cs', 'Settings.cs', 'Ui.cs', 'Bubble.cs', 'Voice.cs',
               'PageAnim.cs', 'PageImport.cs', 'PageStats.cs', 'PageAbout.cs', 'CharMap.cs', 'KeyStats.cs',
               'AppTreemap.cs', 'SpriteGen.cs', 'Pet.csproj', 'PetSettings.csproj', 'Tests.csproj')
$toolFiles = @('build.cmd', 'xamlgen.py', 'make-pet-config.ps1')
foreach ($f in $rootFiles) { if (-not (Test-Path (Join-Path $here $f))) { Fail "缺少 $f" } }
foreach ($f in $srcFiles)  { if (-not (Test-Path (Join-Path $here ('src\' + $f)))) { Fail "缺少 src\$f" } }
foreach ($f in $toolFiles) { if (-not (Test-Path (Join-Path $here ('tools\' + $f)))) { Fail "缺少 tools\$f" } }

# ---------- 2. 当前角色 ----------
if ($Char) {
    $charRel = 'chars\' + $Char
} else {
    $charRel = 'chars\小维'
    foreach ($raw in Get-Content (Join-Path $here 'pet-config.txt') -Encoding utf8) {
        if ($raw -like 'charDir=*') { $charRel = $raw.Substring('charDir='.Length).Trim(); break }
    }
}
$charRel = $charRel -replace '/', '\'
$charFull = Join-Path $here $charRel
if (-not (Test-Path (Join-Path $charFull 'char.json'))) { Fail "角色包不存在：$charRel" }
$charName = Split-Path $charFull -Leaf

# ---------- 3. 暂存 ----------
$stage = Join-Path $here (Join-Path $OutDir '_stage_src')
if ([System.IO.Directory]::Exists($stage)) { [System.IO.Directory]::Delete($stage, $true) }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

foreach ($d in @('src', 'tools')) { New-Item -ItemType Directory -Path (Join-Path $stage $d) -Force | Out-Null }
foreach ($f in $rootFiles) { Copy-Item (Join-Path $here $f) (Join-Path $stage $f) -Force }
foreach ($f in $srcFiles)  { Copy-Item (Join-Path $here ('src\' + $f)) (Join-Path $stage ('src\' + $f)) -Force }
foreach ($f in $toolFiles) { Copy-Item (Join-Path $here ('tools\' + $f)) (Join-Path $stage ('tools\' + $f)) -Force }

# 包里的角色必须和配置一致：-Char 指定别的角色时改写 charDir
$cfgChar = 'chars\' + $charName
foreach ($cfgName in @('pet-config.txt', 'pet-overrides.txt')) {
    $cfgPath = Join-Path $stage $cfgName
    if (-not (Test-Path $cfgPath)) { continue }
    $lines = @(Get-Content $cfgPath -Encoding utf8)
    $found = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like 'charDir=*') { $lines[$i] = 'charDir=' + $cfgChar; $found = $true }
    }
    if (-not $found) { $lines += 'charDir=' + $cfgChar }
    [System.IO.File]::WriteAllLines($cfgPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

# 单测；内置字体随源码一起（现在在 src\Fonts）
Copy-Item (Join-Path $here 'tests') (Join-Path $stage 'tests') -Recurse -Force
Copy-Item (Join-Path $here 'src\Fonts') (Join-Path $stage 'src\Fonts') -Recurse -Force

# 角色：默认只带当前角色
New-Item -ItemType Directory -Path (Join-Path $stage 'chars') -Force | Out-Null
if ($AllChars) { Copy-Item (Join-Path $here 'chars\*') (Join-Path $stage 'chars') -Recurse -Force }
else { Copy-Item $charFull (Join-Path $stage 'chars') -Recurse -Force }
if (Test-Path (Join-Path $here 'bubble-frames')) {
    Copy-Item (Join-Path $here 'bubble-frames') (Join-Path $stage 'bubble-frames') -Recurse -Force
}

# ---------- 4. 说明 ----------
$guide = @"
Q Pet · 在你自己电脑上编译运行
========================================

发源码的原因：你电脑上现编出来的 exe 是本机新文件，
不带「来自其他计算机」标记，Windows 不会弹「已保护你的电脑」，
也不需要我买代码签名证书。

【第一步：装 .NET SDK（只需一次）】
到 https://dotnet.microsoft.com/download 下载「.NET SDK」（6.0 或更高都行），
一路下一步装完，然后**关掉所有命令行窗口/资源管理器**再重新打开（让 PATH 生效）。
已经装过 .NET SDK 或 Visual Studio 的话可以跳过。

【第二步：解压】
把整个压缩包解压到一个普通目录，例如 D:\QPet。
不要放在 C:\Program Files（那里没有写权限），也不要在压缩包预览里双击。

【第三步：双击 tools\build.cmd】
它会依次校验界面、跑单元测试、编译 Pet.exe 和 PetSettings.exe，
并自动把产物复制到上一层（也就是你解压出来的这个目录）。
看到 [OK] Done 就好了（第一次会联网从 NuGet 拉一个很小的引用程序集包）。

【第四步：双击 Pet.exe】
桌宠出现。双击 PetSettings.exe 是设置面板：
形象与大小 / 动画 / 气泡 / 语音 / 统计 / 通用设置（主题色、开机自启、是否显示桌宠）。

【常见问题】
· 提示「找不到 .NET SDK」：SDK 没装好，或装完没重开窗口。
· 提示「Unable to find package」或还原失败：第一次编译需要联网。
· 提示无法覆盖 Pet.exe：桌宠还在运行，先在托盘右键退出，或 taskkill /im Pet.exe /f 再编。
· 编译报错行号看不懂：直接把窗口内容截图发我。
· 跑起来后没有任何窗口：看目录里的 pet-crash.log / pet-status.txt。

【和直接运行成品包的区别】
成品包（QPet-日期.zip）解压后要先「右键 zip → 属性 → 解除锁定」再解压才不弹警告；
源码包不需要这一步，因为 exe 是在你本机生成的。功能完全一样。
"@
$utf8bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText((Join-Path $stage '编译说明-请先看这里.txt'), $guide, $utf8bom)

# ---------- 5. 压缩 ----------
$stamp = Get-Date -Format 'yyyyMMdd'
$zip = Join-Path $here (Join-Path $OutDir "QPet-源码-$stamp.zip")
if ([System.IO.File]::Exists($zip)) { [System.IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$stageSize = [math]::Round(((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
$zipSize = [math]::Round(((Get-Item $zip).Length / 1MB), 1)
if ([System.IO.Directory]::Exists($stage)) { [System.IO.Directory]::Delete($stage, $true) }

Write-Host ""
Write-Host "源码包已生成" -ForegroundColor Green
$scope = if ($AllChars) { '（全部角色）' } else { '（只带这一个；要全带加 -AllChars）' }
Write-Host ("  角色：" + $charName + $scope)
Write-Host "  解压后：$stageSize MB   zip：$zipSize MB"
Write-Host "  文件：$zip"
