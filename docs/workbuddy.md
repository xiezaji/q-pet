# WorkBuddy 开发实录

> 本文件记录 Q Pet 桌面版与网页版从零到一的完整开发过程，用于核验
> 「真实使用腾讯 WorkBuddy 智能体开发」这一参赛条件。

- **项目名称**：Q Pet（网页版：Q Pet · Web）
- **仓库**：<https://github.com/xiezaji/q-pet>
- **开发环境**：腾讯 WorkBuddy（Agent 模式）
- **开发日期**：2026-10-14 ~ 2026-10-21

---

## 一、需求起点

用户最初的需求只有一句话：「做一个桌宠，能放在桌面上的那种」。

我先问了三件事，而不是直接写代码：

1. **角色是谁** —— 已有素材还是需要生成？
2. **要不要能互动** —— 只做动画展示，还是要点击有反应、会说话？
3. **设置要不要图形界面** —— 还是改配置文件就行？

得到确认后开工。这个「先问清楚再动手」的判断贯穿了整个项目。

---

## 二、桌面版：从 0 到能跑

### 2.1 技术选型

用户在 Windows 上用，我评估了三个方案：

| 方案 | 优点 | 否决理由 |
|---|---|---|
| Electron | 生态好 | 体积 100MB+，桌宠要常驻太重 |
| Python + PyQt | 开发快 | 要额外装 Python 环境，动画帧渲染性能差 |
| **C# / .NET Framework 4.8 + GDI+** | Windows 自带运行时，60KB 单文件，Win32 完全可控 | —— |

选了 C#。核心难点是**透明窗口 + 分层渲染**，用 `UpdateLayeredWindow` 实现。

### 2.2 动画帧格式

角色的待机/按下/点击/拖动四组动画要打包成单文件。
我设计的是**无文件头的裸 BGRA 像素数组**：

```
frames.bin = [idle 帧][press 帧][click 帧][drag 帧]
每帧 w*h*4 字节（BGRA），帧数与尺寸写在 frames.json 里
```

好处是零解析开销（`File.ReadAllBytes` + `Marshal.Copy` 直接 blit）。
这一点在网页移植时帮了大忙——因为格式足够简单，
用 20 行 Python 就能转成 WebP。

### 2.3 气泡渲染

气泡是整个项目视觉上最讲究的部分，复刻了桌面端的实现：
圆角矩形 + 尾巴的路径生成、三种皮肤、170ms 弹出缩放、240ms 淡出、
文字装不下时**手动滚动**（配一条极细滚动条提示）。

---

## 三、WorkBuddy 帮我避开的坑

这一节记录了几个**如果没有人提醒、一定会反复踩**的坑。

### 3.1 低级鼠标钩子的 lParam 陷阱（同一个 bug 犯了两次）

桌宠窗口是 `WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE` 且从不激活，
而 `WM_MOUSEWHEEL` **只投递给有键盘焦点的窗口** ⇒ 窗口永远收不到滚轮。
必须用 `WH_MOUSE_LL` 全局钩子转发。

我第一次写对了，但第二次改代码时又写错了：

```csharp
// ✗ 错误：读的是「指针值」的高位（内存地址碎片），跟滚轮量毫无关系
int delta = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
```

低级钩子回调里 `lParam` **不是消息的 lParam**，而是指向 `MSLLHOOKSTRUCT` 的指针：

```
offset 0 : POINT pt (x,y)      — 8 字节
offset 8 : UINT  mouseData     — 滚轮增量在【高 16 位】★
offset 12: DWORD flags
offset 16: DWORD time
offset 24: ULONG_PTR dwExtraInfo
```

正确写法：

```csharp
int mx = Marshal.ReadInt32(lParam, 0);            // pt.x
int my = Marshal.ReadInt32(lParam, 4);            // pt.y
int mouseData = Marshal.ReadInt32(lParam, 8);
int delta = (short)((mouseData >> 16) & 0xFFFF);  // 高 16 位才是滚轮量
```

**这个坑后来在网页版移植时又出现过一次**（`MSLLHOOKSTRUCT` 布局是跨语言通用的知识，
我把它写进了项目记忆，第二次直接避开了）。

### 3.2 WPF 隐藏控件会劫持模板内部的元素

「可编辑下拉框」外面套着一个框、里面又是一个框（框中框）——
这是 WPF 的经典陷阱：`ComboBox` 设 `IsEditable=True` 时，
WPF 塞进来的 `PART_EditableTextBox` 会**继承项目的隐式 `TextBox` 样式**
（`Height=34` + 白底 + 边框）。

而模板上写的 `Background="Transparent" BorderThickness="0"` 只是**本地 Setter**，
优先级低于样式 Template 里的 `Border` ⇒ 透明设置根本不生效。

修法是给 `PART_EditableTextBox` 内部**再套一套透明模板**（放进它的 `TextBox.Resources`）：

```xml
<TextBox.Resources>
  <Style TargetType="TextBox">
    <Setter Property="Height" Value="Auto"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
      <Border Background="Transparent" BorderThickness="0" Padding="0">
        <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center"/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</TextBox.Resources>
```

### 3.3 XAML 写在 C# 字符串里，编译通过 ≠ 能跑

项目把整个 WPF 界面写成一个 C# 字符串常量（单文件 exe 方便分发）。
代价是**编译期完全检查不出 XAML 错误**。

我因此踩了个大坑：批量精简文案时用 Python 做字符串替换，
把多行 XAML 标签的**前半截**替换掉了，只留下后半截属性：

```xml
<TextBlock Text="..." Style="{StaticResource Sub}"    ← 这半截没了
         Margin="0,0,0,12"/>                            ← 只剩这行
```

纯文本节点塞进 `UIElementCollection` ⇒ 启动即崩：
「向类型为 UIElementCollection 的集合中添加值引发异常」。

**而且一次只报一处**，修完第一个以为好了，实际还有 4 个等着。

更糟的是我一开始用 Python 写检测脚本，只能查出「属性续行孤立」，
查不出「标签起始丢失」（后者看起来是合法的属性行）。

**最终解法是写了个真解析器**（`_xamlcheck-export.py.txt` + `_xamlcheck-validator.cs.txt`）：
抽出 `XamlText` 字符串，用 `XamlReader.Load` 真解析，一次报出所有问题位置。
这个工具后来成了项目的标准验证步骤。

### 3.4 自绘 Popup 必须显式设 PlacementTarget

日期下拉列表弹到了**屏幕左上角**，而不是按钮下方。

原因：`ComboBox` 的下拉是 WPF 内部接管 `PlacementTarget` 的，
但**自己 `new Popup()` 出来的不会**。不设就退化到「相对鼠标/屏幕原点」。

```csharp
popup.PlacementTarget = _dateBtn;        // 缺这个就定位到屏幕原点
popup.Placement = PlacementMode.Bottom;
popup.VerticalOffset = 4;
// 每次打开前同步锚点矩形
popup.PlacementRectangle = new Rect(0, 0, btn.ActualWidth, btn.ActualHeight);
```

### 3.5 双层 ScrollViewer 会让滚轮「卡死」

缩放功能删了，但外层容器忘了删，留下了三层嵌套结构。
内层贴边时把滚轮交还父级，父级无处可滚 ⇒ 滚轮在两层空转，
用户感知就是「滚死了」。

**事件处理器只是症状，结构才是病因。** 怎么调 `PreviewMouseWheel` 都修不好。

---

## 四、网页版移植（A3 方案）

### 4.1 为什么必须做网页版

用户要投「1024 造物挑战」，而活动表单的**作品形态只有两个选项**：
「网页应用」和「小程序应用」。桌宠是 Windows 桌面程序，没有体验链接，
而表单第 06 项「作品体验链接」是**必填**。

所以不是「要不要做网页版」，而是「不做就投不了」。

### 4.2 资源压缩：139 MB → 8.3 MB

桌面端 8 个角色的动画帧是 139 MB 的裸像素。网页版直接用是没法接受的。

我先测了压缩比：从 `frames.bin` 取一帧导出 PNG（184 KB），
用 ffmpeg 转 WebP q82 → **9.4 KB，20 倍**，视觉上几乎无损。

于是写了 `web/tools/convert.py`：逐帧导出 PNG → ffmpeg 转 WebP，
wav → mp3（96k）。8 个角色 352 张帧，**总耗时 2 分 22 秒**。

| 角色 | 桌面端 | 网页端 |
|---|---|---|
| 奶蛙 | 31.9 MB | 1.7 MB |
| 芙宁娜 | 16.0 MB | 1.3 MB |
| 小维 | 14.5 MB | 1.2 MB |
| **全部 8 个** | **139 MB** | **8.3 MB** |

### 4.3 架构决策

| 决策 | 理由 |
|---|---|
| 原生 JS，零框架零构建 | 这是**移植**不是重写；桌宠是单窗口渲染循环，框架反而是负担 |
| Canvas `drawImage` 逐帧 | 帧数据已经是浏览器原生支持的图片格式，零转换 |
| Web Audio 替代 MCI | 对应关系清晰：MCI → AudioContext，音量 → GainNode |
| 角色懒加载 | 首屏只加载默认角色，其余点击时拉取 |
| IndexedDB 存用户导入的角色包 | 浏览器原生、容量够、不需要后端 |

### 4.4 移植中修的三个真实 bug

**① 角色跑到屏幕上方**
`_clampPos` 的边界计算在「画布比舞台高」时用了 `m - ch`，
把 y 推成了负数。修法是下限取 `max(0, m - ch)`。

**② 改了代码但浏览器还在跑旧版本**
`python -m http.server` 默认带缓存，我改了三轮代码浏览器纹丝不动。
写了个带 `Cache-Control: no-store` 的开发服务器才解决。
**这个坑排查花了最久**——一度以为是逻辑写错了。

**③ 角色被设置面板遮住**
角色位置是缓存在 `localStorage` 的，面板展开/收起或窗口缩放后位置就失效了。
加了 `resize` 监听 + 面板折叠后重新居中。

### 4.5 验证方式

没有装 Playwright，用 **Edge 的 CDP 协议**直接驱动（Node 自带 `WebSocket`，零依赖）：

```javascript
// 真实鼠标点击
await send('Input.dispatchMouseEvent', {type:'mousePressed', x, y, button:'left', clickCount:1});
// 读取运行时状态
await ev("(()=>{const p=window.__pet; return {...}})()");
```

最终验证结果：

```
1 init: 奶蛙 frames=true voice=1 chars=8
2 bubble: vis=true text=哈哈哈哈哈
3 xiaowei index: 2
4 switched: 小维 voice=9 ready=true
5 bubble2: vis=true text=喜欢我指甲的颜色嘛，猜猜看是用什么染
ERRORS: []
```

**这比只看截图可靠**——截图只能证明「看起来对」，CDP 能证明「状态真的对」。

---

## 五、WorkBuddy 的实际价值

复盘整个项目，WorkBuddy 帮上忙的地方：

1. **踩坑经验的沉淀** —— 上面那些坑（钩子 lParam、WPF 模板劫持、XAML 属性元素闭合）
   如果靠人肉试错，每个至少要花一天，而且大概率重复踩。
   让 AI 一次说清「为什么错 + 正确写法 + 为什么之前会错」，比记结论更有用。

2. **跨语言知识的迁移** —— `MSLLHOOKSTRUCT` 的内存布局是 Win32 知识，
   写 C# 和写 JS 时都要用。第二次遇到时直接避开了。

3. **规模化的机械工作** —— 8 个角色 352 帧的格式转换、30 处文案的批量精简，
   这些重复劳动脚本化后是秒级的事，人工要几小时。

4. **真实验证闭环** —— 「改完必须跑真实入口」这条纪律，
   在网页版开发中直接抓出了 3 个只有运行才会暴露的 bug。

5. **设计的可解释性** —— 视觉规范（字号 6 档、间距 4 的倍数、圆角 4/6/8、
   常驻说明 ≤ 14 字）写进项目记忆后，网页版直接沿用，两端视觉统一。

---

## 六、技术栈

**桌面版**：C# / .NET Framework 4.8 / GDI+ / Win32 分层窗口 / GDI+ 九宫格

**网页版**：原生 JavaScript（ES2018）/ Canvas 2D / Web Audio API /
IndexedDB / `DecompressionStream`（zip 解压）
