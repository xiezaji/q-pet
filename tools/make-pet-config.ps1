# 生成桌宠配置（key=value，C# 侧直接读；改完重跑本脚本即可）
# 值来源优先级：pet-overrides.txt（设置面板写的） > 角色包 char.json > 这里的默认值
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # %~dp0\.. -> 仓库根目录

# ---------- 设置面板的覆盖值 ----------
# 解析覆盖文件：既支持「一行一个 key=value」，也容忍整份被写成一整行。
# 用正则按 "key=" 切分，所以值里带空格（比如含空格的路径）也不会切错。
$ov = @{}
$ovPath = Join-Path $here 'pet-overrides.txt'
if (Test-Path $ovPath) {
  $txt = Get-Content $ovPath -Raw -Encoding utf8
  foreach ($m in [regex]::Matches($txt, '([A-Za-z0-9_]+)=([^\r\n]*?)(?=\s+[A-Za-z0-9_]+=|\s*$)')) {
    $ov[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
  }
}
function Ov($k, $d) { if ($ov.ContainsKey($k) -and $ov[$k] -ne '') { $ov[$k] } else { $d } }

# ---------- 角色包：chars\<角色>\char.json ----------
function Read-Manifest($dir) {
  $m = @{}
  $p = Join-Path $dir 'char.json'
  if (Test-Path $p) {
    foreach ($raw in Get-Content $p -Encoding utf8) {
      $l = $raw.Trim()
      if ($l -eq '' -or $l.StartsWith('#')) { continue }
      $eq = $l.IndexOf('=')
      if ($eq -le 0) { continue }
      $m[$l.Substring(0, $eq).Trim()] = $l.Substring($eq + 1).Trim()
    }
  }
  return $m
}
function Pack-Path($dir, $rel) {
  if ([string]::IsNullOrEmpty($rel)) { return '' }
  if ([System.IO.Path]::IsPathRooted($rel)) { return $rel }
  return Join-Path $dir $rel
}
# 输出相对路径：整个文件夹可以拷到任意机器 / 任意目录
function Rel-Path($p) {
  if ([string]::IsNullOrEmpty($p)) { return '' }
  $full = if ([System.IO.Path]::IsPathRooted($p)) { [System.IO.Path]::GetFullPath($p) } else { [System.IO.Path]::GetFullPath((Join-Path $here $p)) }
  $root = [System.IO.Path]::GetFullPath($here).TrimEnd('\') + '\'
  if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
    return $full.Substring($root.Length)
  }
  return $full
}

$charsRoot = Join-Path $here 'chars'
$charDirRel = Ov 'charDir' 'chars\芙宁娜'
$charDir = Join-Path $here ($charDirRel -replace '/', '\')
if (-not (Test-Path (Join-Path $charDir 'char.json'))) {
  # 角色包不见了：回退到芙宁娜，再不行就用 chars 下第一个带 char.json 的目录
  $alt = Join-Path $charsRoot '芙宁娜'
  if (Test-Path (Join-Path $alt 'char.json')) { $charDir = $alt }
  elseif (Test-Path $charsRoot) {
    foreach ($d in Get-ChildItem $charsRoot -Directory) {
      if (Test-Path (Join-Path $d.FullName 'char.json')) { $charDir = $d.FullName; break }
    }
  }
}
$charDirRel = $charDir.Substring($here.Length).TrimStart('\', '/')
$man = Read-Manifest $charDir
$charName = if ($man.ContainsKey('name') -and $man['name'] -ne '') { $man['name'] } else { Split-Path $charDir -Leaf }

# 语音清单：角色包里没有就沿用上次生成的（避免重新生成配置把导入角色的语音清单改回默认）
$prevConfig = Join-Path $here 'pet-config.txt'
$prevVoicelines = ''
if (Test-Path $prevConfig) {
  foreach ($raw in Get-Content $prevConfig -Encoding utf8) {
    if ($raw -like 'voicelines=*') { $prevVoicelines = $raw.Substring('voicelines='.Length).Trim() }
  }
}
$voicelines = Pack-Path $charDir $man['voicelines']
if ((-not $voicelines -or -not (Test-Path $voicelines)) -and $prevVoicelines) {
  $pv = if ([System.IO.Path]::IsPathRooted($prevVoicelines)) { $prevVoicelines } else { Join-Path $here $prevVoicelines }
  if (Test-Path $pv) { $voicelines = $pv }
}

# 按压 / 松开音：跟随角色；sfxGroup 指定了别的角色就借那个包的
$sfxGroup = Ov 'sfxGroup' 'auto'
$pressSrc = $charDir; $relSrc = $charDir
if ($sfxGroup -ne 'auto' -and $sfxGroup -ne '') {
  $g = Join-Path $charsRoot $sfxGroup
  if (Test-Path (Join-Path $g 'char.json')) { $pressSrc = $g; $relSrc = $g }
}
$manP = Read-Manifest $pressSrc
$manR = Read-Manifest $relSrc
$pressPath = Pack-Path $pressSrc $manP['press']
$relPath = Pack-Path $relSrc $manR['release']
# 包里没有按压/松开音时，退回用语音清单前两条（小维/奶蛙就是这么配的）
if ((-not $pressPath -or -not (Test-Path $pressPath)) -and $voicelines -and (Test-Path $voicelines)) {
  $n = 0
  foreach ($raw in Get-Content $voicelines -Encoding utf8) {
    if ($raw -match "`t") {
      $p = ($raw -split "`t", 2)[1].Trim()
      if ($p -ne '') {
        if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $charDir $p }
        if ($n -eq 0) { $pressPath = $p; $n = 1 } else { $relPath = $p; break }
      }
    }
  }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Q Pet 配置 —— 由 make-pet-config.ps1 生成（值来自 pet-overrides.txt + 角色包）')
$lines.Add('# 全部是相对路径：整个文件夹可以拷到任意机器 / 任意目录运行')
$lines.Add("charDir=$charDirRel")
$lines.Add("frames=$(Rel-Path (Pack-Path $charDir $man['frames']))")
$lines.Add("meta=$(Rel-Path (Pack-Path $charDir $man['meta']))")
$lines.Add('posfile=pet-pos.txt')
$lines.Add('langfile=pet-lang.txt')
$lines.Add('statusfile=pet-status.txt')
$lines.Add("lines=$(Rel-Path (Pack-Path $charDir $man['lines']))")
$lines.Add("voicelines=$(Rel-Path $voicelines)")
$lines.Add('clickLine=' + (Ov 'clickLine' '1'))
$lines.Add('icopath=pet.png')
$lines.Add("holdMs=" + (Ov 'holdMs' '110'))
$lines.Add('clickVoice=' + (Ov 'clickVoice' '0'))
$lines.Add('bubbleH=' + (Ov 'bubbleH' '118'))
$lines.Add('bubbleEveryMs=' + (Ov 'bubbleEveryMs' '600000'))
$lines.Add('bubbleFont=' + (Ov 'bubbleFont' 'YouYuan'))
$lines.Add('bubbleStyle=' + (Ov 'bubbleStyle' 'card'))
$lines.Add('bubbleFrame=' + (Rel-Path (Ov 'bubbleFrame' '')))
$lines.Add('bubbleColor=' + (Ov 'bubbleColor' '8FA6D8'))
$lines.Add('bubbleFontSize=' + (Ov 'bubbleFontSize' '10.5'))
$lines.Add('themeColor=' + (Ov 'themeColor' '005FB8'))
$lines.Add('statsfile=pet-stats.txt')
$lines.Add('statsKeys=' + (Ov 'statsKeys' '1'))
# 音量与四类动画开关 / 速度（设置面板「语音」「动画」页）
$lines.Add('volume=' + (Ov 'volume' '100'))
$lines.Add('animIdle=' + (Ov 'animIdle' '1'))
$lines.Add('animPress=' + (Ov 'animPress' '1'))
$lines.Add('animClick=' + (Ov 'animClick' '1'))
$lines.Add('animDrag=' + (Ov 'animDrag' '1'))
$lines.Add('animSpeed=' + (Ov 'animSpeed' '1.00'))
# 四类动画各自倍速（百分数，缺省 100）
$lines.Add('speedIdle=' + (Ov 'speedIdle' '100'))
$lines.Add('speedPress=' + (Ov 'speedPress' '100'))
$lines.Add('speedClick=' + (Ov 'speedClick' '100'))
$lines.Add('speedDrag=' + (Ov 'speedDrag' '100'))
# 语音语言：只保留一组（中文）
$lines.Add("lang0name=$charName")
$lines.Add("lang0press=$(Rel-Path $pressPath)")
$lines.Add("lang0release=$(Rel-Path $relPath)")
$lines.Add('soundSet=' + (Ov 'soundSet' 'furina'))
$lines.Add('sfxGroup=' + $sfxGroup)
$lines.Add('clickLineMode=' + (Ov 'clickLineMode' 'random'))
# 桌面显示总开关（设置面板「通用设置 → 桌面显示」）：0 = 桌宠不启动
$lines.Add('showPet=' + (Ov 'showPet' '1'))
# 全屏隐藏（设置面板「通用设置 → 全屏隐藏」）：1 = 前台全屏时自动让开
$lines.Add('hideFullscreen=' + (Ov 'hideFullscreen' '1'))
# 文字颜色
$lines.Add('bubbleInk=' + (Ov 'bubbleInk' 'auto'))

$out = Join-Path $here 'pet-config.txt'
[System.IO.File]::WriteAllLines($out, $lines, (New-Object System.Text.UTF8Encoding($false)))
"已生成 $out（角色 = $charName）"
"--- 缺文件检查 ---"
foreach ($l in $lines) {
  $m = [regex]::Match($l, '^([A-Za-z0-9]+)=(.*)$')
  if ($m.Success) {
    $v = $m.Groups[2].Value
    if ($v -match '\\' -or $v -match '^[A-Za-z]:') {
      $vp = if ([System.IO.Path]::IsPathRooted($v)) { $v } else { Join-Path $here $v }
      if (-not (Test-Path $vp)) { "  缺: $($m.Groups[1].Value) -> $v" }
    }
  }
}
