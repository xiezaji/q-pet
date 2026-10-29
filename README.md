<div align="center">
  <img src="docs/images/icon.png" width="104" alt="Q Pet">
</div>

<h1 align="center">Q Pet</h1>

<p align="center">Windows 桌面宠物</p>

<p align="center">
  <a href="https://xiezaji.github.io/q-pet/">
    <img alt="网页版" src="https://img.shields.io/badge/%E7%BD%91%E9%A1%B5%E7%89%88-%E7%82%B9%E5%BC%80%E5%B0%B1%E8%83%BD%E7%8E%A9-0078D4?style=for-the-badge&logo=googlechrome&logoColor=white">
  </a>
  &nbsp;
  <a href="https://github.com/xiezaji/q-pet/releases/latest">
    <img alt="下载桌面版" src="https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD%E6%A1%8C%E9%9D%A2%E7%89%88-4_MB-2ea44f?style=for-the-badge&logo=windows&logoColor=white">
  </a>
</p>

角色站在桌面上，透明置顶，点一下冒出台词并播放语音。拖动能挪位置，下次启动还在原处。
透明区域可以直接点穿到下面的窗口，也不抢焦点、不占任务栏。

用的是 Win32 + .NET Framework 4.8，不联网，不要账号，没有第三方依赖。

## 下载与运行

需要 Windows 10 / 11（x64）。.NET Framework 4.8 系统自带，不用另装。

1. 下载 [win64 压缩包](https://github.com/xiezaji/q-pet/releases/latest)，约 4 MB。
2. 解压前先解除锁定：右键 zip → 属性 → 勾选「解除锁定」→ 确定。
   这样出来的 exe 不带「来自其他计算机」标记，Windows 不会弹「已保护你的电脑」。
   已经解压了也一样，右键 `Pet.exe` → 属性 → 勾选即可。
3. 解压到任意目录，双击 `Pet.exe` 启动。双击 `PetSettings.exe` 打开设置面板，双击托盘图标也行。

不想下载的话，[网页版](https://xiezaji.github.io/q-pet/) 打开就能玩，手机也支持。

## 操作

| 操作 | 效果 |
|---|---|
| 左键点击角色 | 随机台词气泡 + 对应语音 |
| 按住拖动角色 | 移动位置，下次启动回原处 |
| 右键角色 / 右键托盘图标 | 菜单（设置 / 退出） |
| 双击托盘图标 | 打开设置面板 |
| 在气泡上滚轮或拖动 | 翻阅长台词 |

## 换角色

设置面板「导入」页：填名字、选一张立绘（png/jpg）、选语音文件夹，点创建。
动画帧在进程内生成，不需要 Python，也不需要 Pillow。之后到「形象与大小」页切换。

「动画」页可以把某一类动作换成自己的 GIF 或一组图片，也能一键恢复默认。

发布包只带一套角色，是为了把体积压在 4 MB。想要更多角色，自己拿立绘做一个，
或者从仓库的 `chars/` 里拷。

<details>
<summary>角色包结构（想手工做的话）</summary>

```text
chars/
└─ 角色名/
   ├─ char.json          角色清单
   ├─ 图片/image.png     立绘
   ├─ 动画/frames.bin    动画帧（预乘 BGRA）
   ├─ 动画/frames.txt    尺寸与帧数
   ├─ 动画/preview.png   预览图
   ├─ 台词.txt           气泡台词，一行一句
   └─ 音效/
      ├─ voices.tsv      台词 <TAB> 语音路径
      ├─ voices/*.wav
      ├─ press.wav       可选，按压音
      └─ release.wav     可选，松开音
```

`char.json` 示例：

```ini
name=小维
image=图片/image.png
frames=动画/frames.bin
meta=动画/frames.txt
lines=台词.txt
voicelines=音效/voices.tsv
press=音效/voices/xxx.wav
release=音效/voices/yyy.wav
charH=200
```

`frames.txt` 里的 `fps` 决定播放速度（30 就是每帧 33 ms），`w` 和 `h` 要和 `frames.bin` 对得上。

语音只支持 `.wav`。`.mp3`、`.m4a`、`.ogg` 要先转码：把 `ffmpeg.exe` 放在 `Pet.exe` 旁边
或同目录的 `tools\` 里，导入时会自动调用；没有 ffmpeg 就跳过这几个文件，wav 照常导入。

</details>

## 设置中心

八个分区，三栏布局：左边是当前配置摘要，中间改，右边跳分区。改完立即生效，不用重启。
图片可以点开看原尺寸。

| 气泡外观 | 动画调校 |
| :---: | :---: |
| <a href="docs/images/settings-bubble.png"><img src="docs/images/settings-bubble.png" width="400" alt="气泡外观设置"></a> | <a href="docs/images/settings-anim.png"><img src="docs/images/settings-anim.png" width="400" alt="动画设置"></a> |
| 四种外框，颜色、字体、字号都能改 | 待机 / 按键 / 点击 / 拖动 分别开关，幅度和倍速可调 |
| **语音台词** | **使用统计** |
| <a href="docs/images/settings-voice.png"><img src="docs/images/settings-voice.png" width="400" alt="语音设置"></a> | <a href="docs/images/settings-stats.png"><img src="docs/images/settings-stats.png" width="400" alt="使用统计"></a> |
| 台词和语音一一对应，可试听、编辑、删除 | 屏幕 / 活跃 / 软件 / 按键热力 / 陪伴时长，可导出 CSV |

## 常见问题

| 现象 | 处理 |
|---|---|
| 双击没反应 | 确认 zip 已完整解压，`Pet.exe` 和 `chars\` 在同一目录。启动失败会弹窗说明原因，不会静默退出 |
| 弹「Windows 已保护你的电脑」 | 按上面第 2 步解除锁定，或点「更多信息 → 仍要运行」 |
| 桌宠跑到屏幕外 | 拔显示器或改分辨率后会夹回可视区；也可以删掉 `pet-pos.txt` 让它回默认位置 |
| 重复双击开了好几只 | 不会。有单实例互斥体，已有实例时会把设置面板叫出来 |
| 日志越堆越多 | 不会。`pet-status.txt` 只留当天，跨天归档，归档保留 7 天 |
| 想把统计导出 | 设置面板「统计」页 →「导出 CSV」 |
| 换电脑继续用 | 复制整个文件夹。`pet-pos.txt`、`pet-stats*.txt` 可留可删 |
| 不想用预编译的 exe | 从源码自己编，见 [docs/DEVELOP.md](docs/DEVELOP.md) |

## 隐私

程序不联网，没有账号，没有云端同步，不上传任何数据。

全局键鼠钩子只用来触发动画。开了按键统计时读取虚拟键码（A、Ctrl 之类）累计次数，不记录输入内容。

统计只写本地：屏幕时长、活跃时长、前台软件的进程名、按键次数、角色陪伴时长，
存进 `pet-stats.txt` 并按天归档，不记录窗口标题。可以一键清空，`statsKeys=0` 关掉按键统计。

## 素材与版权

内置角色的立绘、语音等素材版权归原权利方，仅供个人学习自用，请勿商用或二次分发。

代码以 [MIT 许可证](LICENSE) 授权，角色素材不在 MIT 范围内，详见 [NOTICE.md](NOTICE.md)。

想基于本项目做自己的桌宠，请自备有使用权的立绘和语音，按上面的结构放进
`chars/你的角色/`，程序代码可以直接用。

## 开发

构建、单元测试、`--selftest`、XAML 编辑、配置键表、打包分发都在
[docs/DEVELOP.md](docs/DEVELOP.md)。

```bat
:: 需要 .NET SDK。一键跑完：版本号 → XAML 校验 → 单测 → 编两个 exe → 复制到根目录
tools\build.cmd
```
