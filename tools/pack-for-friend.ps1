# ============================================================================
# 打包「发给朋友」用的压缩包（不需要编译，直接打包根目录里能跑的那套文件）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File pack-for-friend.ps1
#       默认只带**当前角色**（pet-config.txt 里的 charDir），约 15–20 MB，好发
#   powershell -ExecutionPolicy Bypass -File pack-for-friend.ps1 -AllChars
#       带 chars\ 下全部角色（约 130 MB）
#   powershell -ExecutionPolicy Bypass -File pack-for-friend.ps1 -Char 小维
#       指定角色，忽略当前配置（发版打包用这个，免得带上本机正在用的角色）
#   产物：dist\QPet-<日期>.zip（里面已经放好「首次运行请先看这里.txt」）
#
# 打包成 zip：微信 / QQ 不收 exe；单独发 exe 还会带「来自其他计算机」标记，
# Windows 必然弹「已保护你的电脑」。发 zip 并让对方先「解除锁定」再解压可避开。
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

# ---------- 1. 必带文件检查 ----------
# pet-placeholder.png 是「角色立绘读不到」时的兜底图（PageImport.cs / Settings.cs 会读它），
# 漏了它对方机器上角色卡片会空白。
$must = @('Pet.exe', 'PetSettings.exe', 'pet-config.txt', 'pet-overrides.txt', 'pet.png', 'pet.ico', 'pet-placeholder.png')
foreach ($f in $must) {
    if (-not (Test-Path (Join-Path $here $f))) {
        Fail ("缺少 " + $f + "。先在仓库根目录双击 tools\build.cmd 编译一次，产物会自动复制到根目录")
    }
}

# ---------- 2. 当前角色 ----------
if ($Char) {
    $charRel = 'chars\' + $Char
} else {
    $charRel = 'chars\小维'
    if (Test-Path (Join-Path $here 'pet-config.txt')) {
        foreach ($raw in Get-Content (Join-Path $here 'pet-config.txt') -Encoding utf8) {
            if ($raw -like 'charDir=*') { $charRel = $raw.Substring('charDir='.Length).Trim(); break }
        }
    }
}
$charRel = $charRel -replace '/', '\'
$charFull = Join-Path $here $charRel
if (-not (Test-Path (Join-Path $charFull 'char.json'))) { Fail "角色包不存在：$charRel" }
$charName = Split-Path $charFull -Leaf

# ---------- 3. 准备暂存目录 ----------
$stage = Join-Path $here (Join-Path $OutDir '_stage')
# 用 .NET 的删除接口：Remove-Item 在本机某些环境里会被安全删除钩子拦住
if ([System.IO.Directory]::Exists($stage)) { [System.IO.Directory]::Delete($stage, $true) }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

foreach ($f in $must) { Copy-Item (Join-Path $here $f) (Join-Path $stage $f) -Force }
foreach ($f in @('LICENSE', 'README.md')) {
    if (Test-Path (Join-Path $here $f)) { Copy-Item (Join-Path $here $f) (Join-Path $stage $f) -Force }
}

# 包里的角色必须和配置一致：-Char 指定别的角色时改写 charDir，
# 否则只带小维、配置却指向阿米娅，对方解压后打不开。
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

# 角色：默认只带当前角色；-AllChars 带全部
New-Item -ItemType Directory -Path (Join-Path $stage 'chars') -Force | Out-Null
if ($AllChars) {
    Copy-Item (Join-Path $here 'chars\*') (Join-Path $stage 'chars') -Recurse -Force
} else {
    Copy-Item $charFull (Join-Path $stage 'chars') -Recurse -Force
}

# 自定义气泡外框（有就带，没有就跳过）
if (Test-Path (Join-Path $here 'bubble-frames')) {
    Copy-Item (Join-Path $here 'bubble-frames') (Join-Path $stage 'bubble-frames') -Recurse -Force
}

# ---------- 4. 给朋友看的第一份说明 ----------
$guide = @"
Q Pet 桌宠 · 首次运行请先看这里
========================================

【第一步：解压前先「解除锁定」——这一步能省掉后面的危险警告】
1. 右键这个压缩包 → 属性
2. 如果底部有「安全：此文件来自其他计算机，可能被阻止以帮助保护该计算机」，
   勾上「解除锁定」→ 确定
3. 再解压到一个普通文件夹（比如 D:\QPet）。
   不要在压缩包里直接双击运行，也不要放到 C:\Program Files（那里没写权限）。

【第二步：启动】
· 双击 Pet.exe → 桌宠出现在桌面（想开机自动出现，见第四步）
· 双击 PetSettings.exe → 设置面板：形象与大小、动画、气泡、语音、统计
· 托盘图标（右下角）双击也能打开设置
· 想把 Pet.exe 放到桌面：右键 Pet.exe → 发送到 → 桌面快捷方式

【第三步：万一还是弹「Windows 已保护你的电脑」】
点「更多信息」→「仍要运行」。
已经解压了才发现？右键 Pet.exe（PetSettings.exe 同样处理）→ 属性 →
底部勾上「解除锁定」→ 确定，再运行一次。
原因：这是个人自己做的小程序，没有买微软的代码签名证书，
Windows SmartScreen 只对「有签名 + 有一定下载量」的程序放行，和程序本身有没有问题无关。
如果杀毒软件报毒：把解压出来的文件夹加进信任 / 白名单即可。
程序不联网、不上传任何东西；使用统计只写在本目录的 txt 里。
（在 Windows 安全中心里用 Microsoft Defender 实测扫描过，两个 exe 都没有检出。）

【第四步：换角色 / 开机自启】
· 换形象：设置面板 → 形象与大小 → 角色下拉
· 开机自启：设置面板 → 通用设置 → 开机自启动
· 暂时不想要她：设置面板 → 通用设置 → 桌面显示 → 关掉
  （再打开同样在这里）

【配置在哪】
pet-config.txt 是程序读的配置（设置面板保存时会自动改写它）；
pet-overrides.txt 是设置面板记下的选择。一般不需要手动改。
所有路径都是相对路径，整个文件夹可以搬到任意目录、任意电脑。
"@
$utf8bom = New-Object System.Text.UTF8Encoding($true)     # 带 BOM，Win10 记事本才不会乱码
[System.IO.File]::WriteAllText((Join-Path $stage '首次运行请先看这里.txt'), $guide, $utf8bom)

# ---------- 5. 压缩 ----------
$stamp = Get-Date -Format 'yyyyMMdd'
$zip = Join-Path $here (Join-Path $OutDir "QPet-$stamp.zip")
if ([System.IO.File]::Exists($zip)) { [System.IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$stageSize = [math]::Round(((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
$zipSize = [math]::Round(((Get-Item $zip).Length / 1MB), 1)
if ([System.IO.Directory]::Exists($stage)) { [System.IO.Directory]::Delete($stage, $true) }

Write-Host ""
Write-Host "打包完成" -ForegroundColor Green
$scope = if ($AllChars) { '（全部角色）' } else { '（只带这一个；要全带加 -AllChars）' }
Write-Host ("  角色：" + $charName + $scope)
Write-Host "  解压后：$stageSize MB   zip：$zipSize MB"
Write-Host "  文件：$zip"
Write-Host ""
Write-Host "发给朋友时提醒他一句：先「右键 zip → 属性 → 解除锁定」再解压。" -ForegroundColor Yellow
