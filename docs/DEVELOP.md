# Q Pet 开发说明

给要**改代码、自己编译、打包分发**的人看的。只是想把桌宠用起来的，看 [README](../README.md) 就够了。

## 目录结构

```text
q-pet/
├─ README.md / LICENSE / NOTICE.md
├─ start-pet.cmd / start-pet.vbs     启动桌宠（双击这里）
├─ start-settings.cmd                打开设置面板
├─ pet.ico / pet.png                 图标（运行时会用到）
├─ pet-placeholder.png               角色立绘缺失时的兜底图（运行时读）
├─ src/                              C# 源码与工程文件
│  ├─ Pet.cs                         桌宠本体（Win32 分层窗口 + GDI+）
│  ├─ Program.cs                     入口、单实例、崩溃提示
│  ├─ Settings.cs                    设置面板：流程与配置
│  ├─ Ui.cs / Bubble.cs / Voice.cs   主题、气泡、语音
│  ├─ Page*.cs                       设置面板各页
│  ├─ Core.cs / KeyStats.cs / CharMap.cs / AppTreemap.cs   纯函数（与单测共用）
│  ├─ SpriteGen.cs                   动画帧生成
│  ├─ Fonts/                         界面内置 Saira Wide（SIL OFL 1.1，编译期打进 exe）
│  └─ *.csproj                       三个工程
├─ tools/
│  ├─ build.cmd                      一键：校验 → 单测 → 编两个 exe → 复制到根目录
│  ├─ xamlgen.py                     XAML 反解 / 注入 / 校验
│  ├─ make-pet-config.ps1            由 pet-overrides.txt 重建 pet-config.txt
│  ├─ pack-for-friend.ps1            打成品包（解压即用 zip）
│  ├─ pack-source-for-friend.ps1     打源码包（对方本机编译，不触发 SmartScreen）
│  ├─ restart-pet.ps1                重启桌宠
│  └─ pack/                          批量制作角色包（charpack / sprites / anim_custom）
├─ tests/CoreTests.cs                纯函数单测
├─ chars/                            角色包
├─ docs/                             文档与展示图
└─ web/                              网页版（原生 JS + Canvas，独立部署到 Pages）
```

## 构建

要求：.NET SDK（6.0+）。两个 csproj 已带 `Microsoft.NETFramework.ReferenceAssemblies`，
**没装 .NET Framework 4.8 开发包也能编**。

### 一键（推荐）

双击 `tools\build.cmd`。它会依次做：

1. 从 git tag 算版本号（没有 tag 退回 `2.0.0`，第 4 位是提交数）
2. XAML 校验（没装 Python 就跳过）
3. 纯函数单测
4. 编 `Pet.exe`、`PetSettings.exe`
5. 把两个 exe 复制到仓库根目录

### 分开编

在**仓库根目录**执行（路径都写成了 `src/...`）：

```bat
dotnet build src/Tests.csproj -c Release
dotnet build src/Pet.csproj -c Release
dotnet build src/PetSettings.csproj -c Release
```

产物固定在仓库根的 `build\pet\`、`build\settings\`、`build\tests\`，由各 csproj 的 `OutputPath` 决定。

### 单元测试

```bat
dotnet build src/Tests.csproj -c Release
build\tests\Tests.exe
```

退出码 0 = 全过。覆盖帧清单解析、时长格式化、统计聚合等纯函数。

### 设置面板自检

```bat
PetSettings.exe --selftest          :: 结果写 pet-settings-selftest.txt，出现 ✗ 就是回归
PetSettings.exe --shot a.png --page 3   :: 指定页截图（0 气泡 / 1 通用 / 2 形象 / 3 动画 / 4 语音
                                        :: 5 导入 / 6 统计 / 7 关于 / 8 首页）
PetSettings.exe --themetest "#C0392B"   :: 换主题色跑一遍，查对比度与换肤
```

### XAML 校验

界面 XAML 写在 `Settings.cs` 的 verbatim 字符串里，**编译永远 0 错误**，只有运行时解析才会炸。
所以改界面要走工具，不要直接手改字符串：

```bat
python tools\xamlgen.py export   :: src/Settings.cs -> _ref/xaml.xaml
python tools\xamlgen.py inject   :: _ref/xaml.xaml -> src/Settings.cs（先做 XML 校验）
python tools\xamlgen.py check    :: 只校验当前字符串，不写文件
```

## 运行配置

配置保存在根目录 `pet-config.txt`（`key=value`，路径相对 `pet-config.txt` 所在目录）。
推荐用设置面板改：保存时同步更新 `pet-overrides.txt` 与 `pet-config.txt`，重启桌宠生效。
`pet-config.txt` 缺了会自动按第一个可用角色生成一份最小配置。

| 键 | 说明 |
|---|---|
| `charDir` | 当前角色包目录 |
| `volume` | 音量 0–100 |
| `clickLine` / `clickVoice` | 点击台词 / 按压松开音开关 |
| `bubbleEveryMs` | 主动冒泡间隔，0 为关闭 |
| `bubbleStyle` / `bubbleColor` / `bubbleFont` / `bubbleFontSize` / `bubbleInk` | 气泡外观 |
| `animIdle` / `animPress` / `animClick` / `animDrag` | 四类动画开关 |
| `speedIdle` / `speedPress` / `speedClick` / `speedDrag` | 四类动画倍速（百分比） |
| `statsKeys` | 是否统计按键次数，设为 `0` 关闭 |
| `themeColor` | 设置界面主题色 |

> 新增配置键要同时改三处：`tools/make-pet-config.ps1`、`Settings.cs` 的
> `CollectOverrides` / `WriteOverrides` / `PatchConfig`、以及 `web/js/store.js`。

## 打包分发

```powershell
# 成品包：解压即用（默认只带当前角色，约 4 MB）
powershell -ExecutionPolicy Bypass -File tools\pack-for-friend.ps1
# 带全部角色（体积看你放了多少角色）
powershell -ExecutionPolicy Bypass -File tools\pack-for-friend.ps1 -AllChars

# 源码包：对方本机编译，exe 不带「来自其他计算机」标记，不会触发 SmartScreen
powershell -ExecutionPolicy Bypass -File tools\pack-source-for-friend.ps1
```

产物在 `dist\`。**发出去之前先编译一次**：两个脚本都要根目录有 `Pet.exe` / `PetSettings.exe`。

## 持续集成

`.github/workflows/build.yml`（桌面端）+ `pages.yml`（网页版）。
桌面端每次 push 跑：XAML 校验 → 单测 → 编两个 exe。
「编译过了但界面起不来」这类问题，靠这一关拦在 CI 里，不用等手动 `build.cmd` 才发现。

## 角色包脚本（开发 / 批量用）

设置面板里点按钮就够了，下面这些是等价工具，**需要 Python 3 + numpy + Pillow**：

```bat
python tools\pack\charpack.py list
python tools\pack\charpack.py new --name 新角色 --image D:\pics\a.png --voices D:\voices --char-h 219
python tools\pack\charpack.py rebuild --name 新角色 --char-h 260
python tools\pack\anim_custom.py apply --pack "chars/小维" --slot click --src D:\anim.gif
python tools\pack\anim_custom.py reset --pack "chars/小维" --slot click
```

`--slot` 可选 `idle` / `press` / `click` / `drag`。

运行时**不走**这两个脚本（`SpriteGen.cs` 是 C# 实现）。改动画数学时两边要同步改，
输出必须能互相替换 —— `frames.bin` 格式完全一致。

## 网页版

源码在 `web/`，零框架零构建，直接 `python web/_serve.py` 本地预览，push 到 `main` 自动部署 Pages。
详见 [`web/README.md`](../web/README.md)。
