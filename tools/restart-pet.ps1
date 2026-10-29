# 重启桌宠（设置面板改完配置后调用；也可手动运行）
# 直接拉起同目录的 Pet.exe，不需要编译或额外的启动器脚本。
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # %~dp0\.. -> 仓库根目录
$exe = Join-Path $here 'Pet.exe'

# 1) 停掉旧实例：优先按 pet.pid，再清掉可能残留的同名进程
$pidf = Join-Path $here 'pet.pid'
if (Test-Path $pidf) {
  $old = (Get-Content $pidf -Raw).Trim()
  if ($old -match '^\d+$') { Stop-Process -Id ([int]$old) -Force -ErrorAction SilentlyContinue }
}
Get-CimInstance Win32_Process -Filter "Name='Pet.exe'" -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 700

# 2) 用 WMI 创建：父进程是 WmiPrvSE ⇒ 脱离调用者进程树，关闭本窗口也不影响桌宠
if (-not (Test-Path $exe)) { "找不到 $exe"; exit 1 }
$cmdline = '"' + $exe + '"'
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmdline }
if ($r.ReturnValue -eq 0) { "桌宠已重启（PID $($r.ProcessId)）" } else { "启动失败，ReturnValue=$($r.ReturnValue)" }
