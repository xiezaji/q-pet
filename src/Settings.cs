// ============================================================================
// Q Pet · 设置（独立 exe，不需要 PowerShell）
// 设计：Fluent 浅色底 + 工业排版语言 ——
//       配色沿用原来的基准（#F3F3F3 底 / #FFFFFF 卡片 / #E4000000 正文 / #005FB8 主题蓝）；
//       形状是直角与切角（切角 = 可点击）、1px 细线分区、// 眉标 + 粗体中文、宽体拉丁（Saira Wide）。
//       主题色只做填充（导航当前项、开关、滑块、选中态），由 ApplyThemeColor 换肤。
// 编译：dotnet build src/PetSettings.csproj -c Release   （产物 build\settings\PetSettings.exe）
//       旧方式仍可用：csc /target:winexe /out:PetSettings.exe /r:PresentationFramework.dll
//        /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll Settings.cs
// 运行：双击 PetSettings.exe（或托盘双击）；--selftest 自检；--page N 直达某页（8 = 首页）；
//        --shot <png> --page N 无界面渲染某页；--dialog-test 弹一次退出确认框
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

static partial class Settings
{
    static string Here;
    static Dictionary<string, string> Cur = new Dictionary<string, string>();
    static Window Win;

    // 给内部类（例如取色器弹窗）拿主窗口资源用：Win 本身是 private
    internal static Window MainWin { get { return Win; } }

    // 控件
    static ListBox Nav;
    // 首页（左摘要 / 中枢纽 / 右八格）
    static Grid PageHome;
    static Border NavHost;
    static Grid ContentHost;
    static Button BtnHome, BtnHomeShow, BtnHomeFolder;
    // 中栏是内容视口：hubView（角色枢纽）和 contentHost（点开的那一页）互斥显示
    static FrameworkElement HubView, HubPlate, MidHost, HubViewport;
    static ScaleTransform HubScale;
    static TranslateTransform HubShift;
    static TextBlock TxtHubZoom;
    // 枢纽页立绘的预览倍率。只影响这一屏的显示，不写进配置（桌宠本身的尺寸走「形象高度」）
    static double _hubZoom = 1.0;
    // 立绘自身的基准大小：×1.0 是「铺满中栏」的满格尺寸，铺满时她比窗口里其它东西都大，
    // 一进来像贴脸。0.6 是看着舒服的初始尺度，滚轮放大到 1.0 才是铺满。
    // 只缩图，不缩白板 —— 板要一直铺满中栏，靠 hubViewport 的底色画，不在变换里。
    const double HubArtScale = 0.6;
    static string _hubZoomChar = "";
    // 放大之后才有意义的平移量（视口坐标，像素）与视口实际尺寸（立绘板铺满中栏，所以是长方形）
    static double _hubPanX, _hubPanY, _hubW = 300, _hubH = 300;
    static bool _hubDragging;
    static Point _hubDragStart;
    static double _hubDragPanX, _hubDragPanY;
    static ToggleButton[] Tiles = new ToggleButton[8];
    // 标题栏读数 / 左栏六格刻度 / 仪表带新增读数
    static TextBlock TxtRunState, TxtRunPos, TxtTopChar, TxtTopVoice, PbFrames;
    static Border BdRunDot, PbDot;
    static Grid GsChar, GsSize, GsBub, GsVol;
    static TextBlock UBubble, TxtCharDim, TxtSizeDim;
    static StackPanel SizePresets, PaletteBlock;
    static Button BtnCharFolder;
    static Button[] SizePresetBtns = new Button[0];
    // 内容模式要让宽度给中栏：摘要收起、八格收窄
    static Border SummaryHost;
    static ColumnDefinition ColSummary, ColTiles;
    static Image ImgHomeChar;
    static TextBlock TxtHomeChar, TxtHomeCharNote, TxtHomeSize, TxtHomeBubble,
                     TxtHomeVol, TxtHomeVolNote, TxtHomeTheme, TxtHomeRun, TxtHomePos;
    // 左栏脚注那一项（版本号）
    static TextBlock TxtSumVer;
    // 左栏调色板图例里那个 hex（原来写死 #005FB8，换主题色后就跟读数对不上）
    static TextBlock TxtPalAccent;
    // 左栏紧凑模式：620 高窗口下压行距，一屏放完五条读数
    static bool _summaryDense;
    static ComboBox CmbChar, CmbSet, CmbEvery, CmbFont, CmbClickMode;
    static TextBox TxtVoiceSearch;
    static TextBlock PhVoiceSearch;
    // 气泡颜色 / 字号
    static Button BtnBubbleColor;
    static WrapPanel BubbleColorHost;
    static Button BtnBubbleInk;
    static WrapPanel BubbleInkHost;
    static string _bubbleInkSel = "auto";                          // "auto" 或 #RRGGBB
    static ComboBox CmbFontSize;                          // 字号：Word 风格下拉（可选可输）
    static TextBlock LblFontSize;
    // 「有未保存的更改」
    static TextBlock LblDirty, LblSaved;
    static DispatcherTimer _savedNoteTimer;
    // 截图/自检时关掉动效，否则会抓到动画中间帧（淡入到一半 = 半透明）
    static bool _motionOff;
    // 换肤渐变时长（ms）：0 = 用默认 220。--themetest --motion 会临时拉长以便截到中间帧
    static int _themeFadeMs;
    // 截图前空转多少毫秒。调小（--pump 55）就能抓到动画中间帧，用来验收动效
    static int _shotPumpMs = 320;
    static string _savedSnapshot = "";
    static DispatcherTimer _dirtyTimer;
    // 界面缩放（Word / 浏览器那套：Ctrl+滚轮、Ctrl+±、Ctrl+0）
    static TextBlock LblBubSize;
    static Color _bubbleColorSel = Color.FromRgb(0x8F, 0xA6, 0xD8);   // 当前选中的气泡主色
    static double _bubbleFontPtSel = 10.5;                            // 选中的气泡字号
    // 通用设置
    static StackPanel UiContent;
    static WrapPanel ThemeColorHost;
    static CheckBox ChkAutoStart;
    static CheckBox ChkShowPet;                                        // 桌面显示：是否打开桌宠
    static bool _showPetSel = true;                                    // 与上面的开关同步（切换即生效）
    static CheckBox ChkHideFs;                                          // 全屏隐藏：前台全屏时让开
    static bool _hideFsSel = true;                                      // 与上面的开关同步（切换即生效）
    internal static FontFamily UiFace;                                  // 主窗字体（弹窗也要用同一套）
    static Color _themeColor = Color.FromRgb(0x00, 0x5F, 0xB8);        // 界面主题色（Fluent 蓝）
    // 「恢复默认」用的值：与 make-pet-config.ps1 里 themeColor 的默认一致
    static readonly Color ThemeDefault = Color.FromRgb(0x00, 0x5F, 0xB8);
    // 点主题色块时只重建那面墙（把选中框挪过去），不整页重建 —— 整页重建会打断渐变动画和滚动位置
    static Action<Color> _themePicker;
    static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // 显示名统一为 Q Pet；exe 文件名保留 Pet/PetSettings，兼容启动脚本与配置
    public const string PetExeName = "Pet.exe";
    static readonly string StartupScriptName = "start-pet";   // 与实际脚本 start-pet.vbs / .cmd 一致
    static readonly string RunValueName = "Pet 桌宠";          // 沿用旧值，避免已有开机自启失效
    static StackPanel VoiceListPanel;
    static TextBlock LblVoiceCount;
    static Button BtnRandomLine;
    static Button BtnAddVoice;
    static List<VoiceLine> VoiceLines = new List<VoiceLine>();

    class VoiceLine { public string Text, Path; }

    // 气泡外框样式
    static WrapPanel BubbleStyleHost;
    static Button BtnUpBubble;
    static TextBlock LblBubHint;
    static string _bubbleStyleSel = "card";   // 当前选中的样式
    static string _bubbleFrameSel = "";        // 自定义外框图片（绝对/相对都行，随时规范成相对）
    static string BubbleDir { get { return Path.Combine(Here, "bubble-frames"); } }
    static Button BtnSave, BtnCancel, BtnQuit, BtnMin, BtnClose, BtnMax, BtnPreview;
    static Image ImgChar, ImgBub, ImgSize;
    static Slider SldSize, SldBub, SldVol;
    static TextBlock LblSize, LblBub, LblSizeCap, LblVol, LblVoiceHint;
    static CheckBox ChkClick, ChkBub;
    // 中栏顶部仪表带
    static TextBlock PbNum, PbName, PbChar, PbSize, PbVoice, PbState;
    static StackPanel PageChar, PageVoice, PageBubble, PageAnim, PageStats, PageUi, PageAbout;
    static ScrollViewer Scroller, VoiceScroller;
    static MediaPlayer PreviewPlayer;

    class CharEntry
    {
        public string Name, Dir, Image, Voices, Lines, Frames, Meta;
        public int CharH = 219, VoiceCount;
        public double AmpIdle = 1, AmpPress = 1, AmpClick = 1, AmpDrag = 1;
        public bool HasFrames { get { return !string.IsNullOrEmpty(Frames) && File.Exists(Frames); } }
    }
    static List<CharEntry> ChList = new List<CharEntry>();
    static CharEntry SelectedChar { get { return CmbChar != null && CmbChar.SelectedIndex >= 0 && CmbChar.SelectedIndex < ChList.Count ? ChList[CmbChar.SelectedIndex] : null; } }

    // 动画页的卡片（每类动画：开关 / 幅度 / 倍速 / 预览）
    class AnimCard
    {
        public string Key, Title;
        public CheckBox Toggle;
        public Slider Amp, Speed;
        public TextBlock AmpLbl, SpeedLbl;
        public AnimPreview Preview;
    }
    static List<AnimCard> AnimCards = new List<AnimCard>();
    static string _animCardsDir = "";
    static readonly string[] AnimKeys = { "idle", "press", "click", "drag" };

    // 真帧预览：把 frames.bin 里的某一段循环播放（PBGRA 直接做成 BitmapSource）
    class AnimPreview
    {
        public Image View;
        byte[] _data;
        int _w, _h, _fb, _start, _count, _idx, _hold;
        double _acc, _speed = 1;
        bool _pause;
        readonly DispatcherTimer _timer;

        public AnimPreview(double boxH)
        {
            View = new Image { Stretch = Stretch.Uniform, Height = boxH, Margin = new Thickness(4) };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (s, e) => Step();
        }

        public void Load(byte[] data, int w, int h, int start, int count, double speed, bool pauseAtEnd)
        {
            _data = data; _w = w; _h = h; _fb = w * h * 4;
            _start = start; _count = Math.Max(1, count);
            _speed = speed <= 0 ? 1 : speed; _pause = pauseAtEnd;
            _idx = 0; _acc = 0; _hold = 0;
            Show(0);
            _timer.Start();
        }

        void Show(int i)
        {
            if (_data == null || _w <= 0) return;
            int off = _fb * (_start + i);
            if (off < 0 || off + _fb > _data.Length) return;
            // 用 IntPtr 重载做单帧 BitmapSource：不依赖 WritePixels 的缓冲区校验
            System.Runtime.InteropServices.GCHandle gh =
                System.Runtime.InteropServices.GCHandle.Alloc(_data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr p = new IntPtr(gh.AddrOfPinnedObject().ToInt64() + off);
                // ⚠️ stride 是**一行**的字节数（w*4），不是整帧大小；
                //    传成整帧会让 WIC 以为要 stride*h 字节，直接报「缓冲区不足」。
                BitmapSource bs = BitmapSource.Create(_w, _h, 96, 96, PixelFormats.Pbgra32, null, p, _fb, _w * 4);
                bs.Freeze();
                View.Source = bs;
            }
            finally { gh.Free(); }
        }

        void Step()
        {
            if (_data == null) return;
            if (_hold > 0) { _hold--; return; }
            _acc += _speed;
            if (_acc < 1) return;
            int adv = (int)_acc; _acc -= adv;
            _idx += adv;
            if (_idx >= _count)
            {
                _idx = 0;
                if (_pause) _hold = 14;      // 按压/点击/拖动播完停半秒再重播
            }
            Show(_idx);
        }

        public void SetSpeed(double v) { _speed = v <= 0 ? 1 : v; }
        public void Stop() { _timer.Stop(); }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    static readonly int[] EveryVals = { 0, 300000, 600000, 1800000 };
    static readonly string[] EveryNames = { "不主动冒", "5 分钟", "10 分钟", "30 分钟" };

    [STAThread]
    static void Main(string[] args)
    {
        Here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        // 开发用：--genframes <图片> <高度> <输出目录> 直接出整套动画帧然后退出。
        // 用途：在没有 Python/Pillow 的环境里和旧 sprites.py 的产物做逐像素比对（回归验证）。
        for (int gi = 0; gi < args.Length; gi++)
            if (args[gi] == "--genframes" && gi + 3 < args.Length)
            {
                string genOut = args[gi + 3], msg;
                try
                {
                    int gh;
                    if (!int.TryParse(args[gi + 2], out gh) || gh <= 0) gh = 300;
                    // 可选 4 个幅度参数：--genframes img 200 out 1 1 2 1
                    double[] am = { 1, 1, 1, 1 };
                    for (int k = 0; k < 4 && gi + 4 + k < args.Length; k++)
                        double.TryParse(args[gi + 4 + k], NumberStyles.Float, CultureInfo.InvariantCulture, out am[k]);
                    QSprite.Pack gp = QSprite.Generate(args[gi + 1], gh, am[0], am[1], am[2], am[3]);
                    QSprite.WritePack(genOut, gp, true);
                    msg = "OK " + gp.W + "x" + gp.H + " " + gp.Total + " 帧";
                }
                catch (Exception ex) { msg = "FAIL " + ex.Message; }
                try
                {
                    Directory.CreateDirectory(genOut);
                    File.WriteAllText(Path.Combine(genOut, "_genframes.txt"), msg + "\r\n", new UTF8Encoding(false));
                }
                catch (Exception) { }
                Environment.Exit(msg.StartsWith("OK") ? 0 : 1);
                return;
            }

        // 开发用：--packtest <apply|reset> <角色包目录> <槽位> [源图] 验证自定义动画拼接
        for (int pi = 0; pi < args.Length; pi++)
            if (args[pi] == "--packtest" && pi + 3 < args.Length)
            {
                string cmd = args[pi + 1], dir = args[pi + 2], slot = args[pi + 3];
                string msg;
                try
                {
                    if (cmd == "apply") QSprite.ApplyCustom(dir, slot, pi + 4 < args.Length ? args[pi + 4] : "");
                    else QSprite.ResetSlot(dir, slot);
                    QSprite.Pack rp = QSprite.ReadPack(dir);
                    msg = "OK idle=" + rp.Counts[0] + " press=" + rp.Counts[1]
                        + " click=" + rp.Counts[2] + " drag=" + rp.Counts[3]
                        + " custom=" + (QSprite.ReadManifest(dir).ContainsKey("custom" + char.ToUpperInvariant(slot[0]) + slot.Substring(1)) ? "1" : "0");
                }
                catch (Exception ex) { msg = "FAIL " + ex.Message; }
                try { File.WriteAllText(Path.Combine(dir, "_packtest.txt"), msg + "\r\n", new UTF8Encoding(false)); }
                catch (Exception) { }
                Environment.Exit(msg.StartsWith("OK") ? 0 : 1);
                return;
            }

        bool selfTest = false;
        bool dialogTest = false;
        bool saveNoteTest = false;
        bool motionShot = false;              // --motion：截图时保留动效（配合小 --pump 抓中间帧）
        double zoomTest = 0, panTestX = 0, panTestY = 0;

        int page = 0, shotH = 780, shotScroll = 0, shotW = 1180;
        string shotPath = "";
        string themeTest = "";
        string showPetTest = "";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--selftest" || args[i] == "-selftest") selfTest = true;
            else if (args[i] == "--page" && i + 1 < args.Length) int.TryParse(args[++i], out page);
            else if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
            else if (args[i] == "--shot-height" && i + 1 < args.Length) int.TryParse(args[++i], out shotH);
            else if (args[i] == "--shot-width" && i + 1 < args.Length) int.TryParse(args[++i], out shotW);
            else if (args[i] == "--shot-scroll" && i + 1 < args.Length) int.TryParse(args[++i], out shotScroll);
            else if (args[i] == "--shot-zoom" && i + 1 < args.Length) int.TryParse(args[++i], out _shotZoom);
            else if (args[i] == "--shot-crop" && i + 1 < args.Length)
            {
                string[] cp = args[++i].Split(',');
                if (cp.Length == 4)
                {
                    int.TryParse(cp[0], out _cropX); int.TryParse(cp[1], out _cropY);
                    int.TryParse(cp[2], out _cropW); int.TryParse(cp[3], out _cropH);
                }
            }
            else if (args[i] == "--dialog-test") dialogTest = true;
            else if (args[i] == "--dlgshot" && i + 1 < args.Length) _dlgShotPath = args[++i];
            else if (args[i] == "--themetest" && i + 1 < args.Length) themeTest = args[++i];
            else if (args[i] == "--showpet" && i + 1 < args.Length) showPetTest = args[++i];
            else if (args[i] == "--savenote") saveNoteTest = true;
            else if (args[i] == "--zoom" && i + 1 < args.Length)
                double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out zoomTest);
            else if (args[i] == "--motion") motionShot = true;
            else if (args[i] == "--pump" && i + 1 < args.Length) int.TryParse(args[++i], out _shotPumpMs);
            else if (args[i] == "--pan" && i + 1 < args.Length)
            {
                string[] xy = args[++i].Split(',');
                if (xy.Length == 2)
                {
                    double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out panTestX);
                    double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out panTestY);
                }
            }
        }
        LoadOverrides();
        RepairAutoStart();        // 注册表里那条路径失效了就按当前位置重写（见函数注释）
        try
        {
            Win = (Window)System.Windows.Markup.XamlReader.Parse(XamlText);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(Here, "pet-settings-crash.log"),
                    DateTime.Now.ToString("s") + " 界面解析失败\r\n" + ex, new UTF8Encoding(false));
            }
            catch (Exception) { }
            MessageBox.Show("界面加载失败：" + ex.Message, "Q Pet 设置");
            return;
        }
        ApplyUiFont();
        try { Bind(); }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Here, "pet-settings-crash.log"), DateTime.Now.ToString("s") + "\r\n" + ex); }
            catch (Exception) { }
            return;
        }
        // 圆角窗口：Win11 用 DWM 官方圆角（DWMWA_WINDOW_CORNER_PREFERENCE = 33, ROUND = 2）
        Win.SourceInitialized += (s, e) =>
        {
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(Win).Handle;
                int pref = 2;
                DwmSetWindowAttribute(h, 33, ref pref, sizeof(int));
            }
            catch (Exception ex) { LogErr("Main", ex); }
        };
        if (selfTest) { SelfTest(); return; }
        // 开发自验：确认框样式（渲染卡片，不进 ShowDialog）
        if (!string.IsNullOrEmpty(_dlgShotPath))
        {
            _motionOff = true;
            Dlg.Ask("还有没保存的更改",
                    "你改了设置但还没点「保存更改」。现在关掉的话，这些改动不会生效。",
                    "不保存退出", "继续编辑", true);
            return;
        }
        // 开发自验：「桌面显示」开关不做界面也能验一遍（写盘 + 启停进程 + 结果落文件）
        if (showPetTest != "")
        {
            SetShowPet(showPetTest == "1");
            System.Threading.Thread.Sleep(1500);        // 等进程真的起来 / 退掉
            var sb = new StringBuilder();
            sb.AppendLine("桌面显示开关实测：" + (showPetTest == "1" ? "打开" : "关闭"));
            sb.AppendLine("  pet-config.txt     showPet=" + ReadKey(Path.Combine(Here, "pet-config.txt"), "showPet"));
            sb.AppendLine("  pet-overrides.txt  showPet=" + ReadKey(Path.Combine(Here, "pet-overrides.txt"), "showPet"));
            sb.AppendLine("  运行的 Pet.exe 进程数 = " + Process.GetProcessesByName("Pet").Length);
            File.WriteAllText(Path.Combine(Here, "pet-showpet-test.txt"), sb.ToString(), new UTF8Encoding(false));
            return;
        }
        // 无界面截图：给「改完 UI 想确认有没有被挤掉」用的（不弹窗、不依赖 DWM 缩放）
        if (shotPath != "")
        {
            _motionOff = true;                 // 截图只抓终态：动效淡入到一半会拍成半透明
            if (motionShot) _motionOff = false;   // --motion 时保留动效，用来验收
            SelectPage(page);
            _shotScroll = shotScroll;
            // 开发自验：底栏「已保存并应用」状态条长什么样（真保存会重启桌宠，截图只验样式）
            if (saveNoteTest) ShowSavedNote("已保存并应用  │  狗egg  │  219px  │  音量 20%");
            // 开发自验：枢纽立绘的预览倍率（滚轮效果，截图里摆一个指定倍率）
            if (zoomTest > 0)
            {
                _hubZoom = zoomTest; _hubPanX = panTestX; _hubPanY = panTestY;
                ApplyHubZoom();
            }
            Shot(shotPath, Math.Max(1000, shotW), Math.Max(400, shotH));
            return;
        }
        if (page > 0) SelectPage(page);
        // 开发自验：--themetest E06C8B 直接套一个主题色并截图（验证换肤是否生效）
        // 带 --motion 时保留渐变动画，并且**真把窗口跑起来**再在 400ms 处取一帧 ——
        // 动画时钟靠渲染循环推进，App.Run 之前怎么泵 dispatcher 都不会走（读到的永远是起点色）
        if (themeTest != "")
        {
            _motionOff = true;
            ShowPage("ui");
            Color tc = WpfParseHex(themeTest, _themeColor);
            if (!motionShot)
            {
                ApplyThemeColor(tc);
                BuildGeneralPage();
                Shot(Path.Combine(Here, "pet-theme-test.png"), 960, 740);
                return;
            }
            _themeFadeMs = 1200;                 // 拉长，好在中间截一帧看渐变
            Win.Loaded += (s, e) =>
            {
                _motionOff = false;
                ApplyThemeColor(tc, true);
                var t = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal, Win.Dispatcher);
                t.Interval = TimeSpan.FromMilliseconds(400);
                t.Tick += (s2, e2) =>
                {
                    t.Stop();
                    SolidColorBrush pr = Win.Resources["Accent"] as SolidColorBrush;
                    Console.WriteLine("fade mid: #" + (pr == null ? "—" : HexOf(pr.Color))
                        + " → 目标 #" + HexOf(tc));
                    Shot(Path.Combine(Here, "pet-theme-test.png"), 960, 740);
                    try { Win.Close(); } catch (Exception) { }
                };
                t.Start();
            };
        }
        // 自检用：启动后直接弹「退出」确认框（只为了截图看样式，取消后自己关掉）
        if (dialogTest) Win.Loaded += (s, e) => { try { QuitPet(); } catch (Exception ex) { LogErr("dlgtest", ex); } Win.Close(); };
        var app = new Application();
        app.Run(Win);
    }

    // ---------------- 读取当前覆盖值 ----------------
    static string CurOf(string key, string def)
    {
        string v;
        return Cur.TryGetValue(key, out v) && v != "" ? v : def;
    }
    static void LoadOverrides()
    {
        Cur.Clear();                     // 先清：这个函数同时被「还原配置」复用，不能只追加
        string p = Path.Combine(Here, "pet-overrides.txt");
        if (!File.Exists(p)) return;
        string txt = File.ReadAllText(p, Encoding.UTF8);
        foreach (Match m in Regex.Matches(txt, @"([A-Za-z0-9_]+)=([^\r\n]*?)(?=\s+[A-Za-z0-9_]+=|\s*$)"))
            Cur[m.Groups[1].Value] = m.Groups[2].Value.Trim();
    }

    // 读 key=value 文件里的某一个键（开发自验用：确认开关真的写进配置文件了）
    static string ReadKey(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return "（文件不存在）";
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                if (raw.Substring(0, eq).Trim() == key) return raw.Substring(eq + 1).Trim();
            }
            return "（没有这个键）";
        }
        catch (Exception ex) { LogErr("ReadKey", ex); return "（读取失败）"; }
    }

    static int CurSize
    {
        get
        {
            CharEntry e = SelectedChar;
            if (e != null && e.CharH > 0) return e.CharH;
            int v; return int.TryParse(CurOf("charH", "219"), out v) ? v : 219;
        }
    }
    static int CurBubH { get { int v; return int.TryParse(CurOf("bubbleH", "110"), out v) ? v : 110; } }
    static int CurEvery { get { int v; return int.TryParse(CurOf("bubbleEveryMs", "300000"), out v) ? v : 300000; } }
    static string CurCharDir { get { return CurOf("charDir", ""); } }
    static string CurFont { get { return CurOf("bubbleFont", "YouYuan"); } }
    static bool CurClick { get { return CurOf("clickVoice", "0") == "1"; } }
    static int CurVol { get { int v; return int.TryParse(CurOf("volume", "100"), out v) ? Math.Max(0, Math.Min(100, v)) : 100; } }
    static bool CurAnim(string key) { return CurOf(key, "1") == "1"; }
    static string CurBubbleStyle { get { string s = CurOf("bubbleStyle", "card"); return s == "" ? "card" : s; } }
    static string CurBubbleFrame
    {
        get
        {
            string p = CurOf("bubbleFrame", "");
            if (p == "") return "";
            if (!Path.IsPathRooted(p)) p = Path.Combine(Here, p);
            return p;
        }
    }
    static int CurSpeedPct(string key)
    {
        int v;
        if (!int.TryParse(CurOf(key, "100"), out v)) v = 100;
        return Math.Max(50, Math.Min(200, v));
    }

    // ---------------- 页面切换 ----------------
    static bool _navBusy = false;
    static string _curPage = "home";   // 当前页 key；磁贴据此判断「再点一次回枢纽」

    static void SelectPage(int index)
    {
        string[] keys = { "bubble", "ui", "char", "anim", "voice", "import", "stats", "about" };
        if (index == 8) { ShowPage("home"); return; }              // 8 = 首页（--page 8 / --shot）
        if (index >= 0 && index < keys.Length) ShowPage(keys[index]);
    }

    // 顶部仪表带：一行讲完「在哪一页 / 当前角色 / 高度 / 语音」，内容页不再从一大片空开始
    static readonly string[] PageKeys = { "bubble", "ui", "char", "anim", "voice", "import", "stats", "about" };
    static readonly string[] PageNums = { "01", "02", "03", "04", "05", "06", "07", "08" };
    static readonly string[] PageNames = { "气泡", "通用设置", "形象与大小", "动画", "语音", "导入", "统计", "关于" };

    static void RefreshPageBar(string key)
    {
        int idx = Array.IndexOf(PageKeys, key);
        if (PbNum == null) return;
        PbNum.Text = idx >= 0 ? PageNums[idx] : "—";
        PbName.Text = idx >= 0 ? PageNames[idx] : "—";

        // 仪表带只放值，标签是写在 XAML 里的灰眉标（CHAR / H / FRAMES / VOICE）
        CharEntry ce = SelectedChar;
        PbChar.Text = ce != null && !string.IsNullOrEmpty(ce.Name) ? ce.Name : "—";
        PbSize.Text = (SldSize != null ? (int)Math.Round(SldSize.Value) : 0) + " px";
        if (PbFrames != null) PbFrames.Text = FramesOf(ce).ToString();
        if (PbVoice != null) PbVoice.Text = (ce != null ? ce.VoiceCount : 0).ToString();
        SetPageBarState();
    }

    // 帧序列里有几帧：frames.bin 大小 ÷ (w×h×4)，宽高记在 frames.txt
    static int FramesOf(CharEntry ce)
    {
        if (ce == null || !ce.HasFrames) return 0;
        try
        {
            int fw = 0, fh = 0;
            if (File.Exists(ce.Meta))
            {
                foreach (string raw in File.ReadAllLines(ce.Meta))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    if (k == "w") int.TryParse(v, out fw);
                    else if (k == "h") int.TryParse(v, out fh);
                }
            }
            if (fw <= 0 || fh <= 0) return 0;
            return (int)(new FileInfo(ce.Frames).Length / (fw * fh * 4));
        }
        catch (Exception ex) { LogErr("FramesOf", ex); return 0; }
    }

    // 保存态：跟底栏那条 UNSAVED 用同一份快照判定，不另开定时器
    static void SetPageBarState()
    {
        if (PbState == null) return;
        bool dirty = false;
        try { dirty = Snapshot() != _savedSnapshot; } catch (Exception) { }
        PbState.Text = dirty ? "未保存" : "已保存";
        PbState.Foreground = dirty ? (Brush)Win.Resources["Accent"] : (Brush)Win.Resources["Ink2"];
        // 小方块跟着变色：绿 = 已落盘，主题色 = 还没落盘
        if (PbDot != null)
            PbDot.Background = dirty ? (Brush)Win.Resources["Accent"]
                                     : new SolidColorBrush(Color.FromRgb(0x12, 0xA1, 0x50));
    }

    static void ShowPage(string key)
    {
        if (_navBusy) return;
        _navBusy = true;
        try
        {
            // 首页常驻：右栏八格就是导航，中栏是内容视口。
            // home ⇒ 中栏显示角色枢纽；点某一格 ⇒ 枢纽收起、中栏换成那一页。
            bool home = key == "home";
            _curPage = key;
            if (PageHome != null) PageHome.Visibility = Visibility.Visible;
            if (NavHost != null) NavHost.Visibility = Visibility.Collapsed;   // 左栏列表已被八格取代
            // 左摘要常驻（用户在内容页也要看得到这几项）；只有右栏八格在内容页收窄让宽度给中栏
            if (SummaryHost != null) SummaryHost.Visibility = Visibility.Visible;
            if (ColSummary != null) ColSummary.Width = new GridLength(216);
            if (ColTiles != null) ColTiles.Width = new GridLength(300);
            if (HubView != null) HubView.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            if (ContentHost != null) ContentHost.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (BtnHome != null) BtnHome.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            HighlightTile(home ? null : key);
            if (home) { RefreshHome(); FitHubPlate(); }

            foreach (object o in Nav.Items)
            {
                ListBoxItem it = o as ListBoxItem;
                if (it != null && (it.Tag as string) == key) { if (Nav.SelectedItem != it) Nav.SelectedItem = it; break; }
            }
            PageBubble.Visibility = key == "bubble" ? Visibility.Visible : Visibility.Collapsed;
            PageUi.Visibility = key == "ui" ? Visibility.Visible : Visibility.Collapsed;
            PageChar.Visibility = key == "char" ? Visibility.Visible : Visibility.Collapsed;
            PageAnim.Visibility = key == "anim" ? Visibility.Visible : Visibility.Collapsed;
            PageVoice.Visibility = key == "voice" ? Visibility.Visible : Visibility.Collapsed;
            PageImport.Visibility = key == "import" ? Visibility.Visible : Visibility.Collapsed;
            PageStats.Visibility = key == "stats" ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;

            if (key == "stats") { BuildStatsPage(); StartLiveStats(); } else StopLiveStats();
            if (key == "home") StartLivePos(); else StopLivePos();
            if (key == "import") RefreshImport();
            if (key == "voice") LoadVoiceLibrary();
            if (key == "bubble") { BuildBubbleColorTiles(); BuildBubbleInkTiles(); DrawBubblePreview(); }
            if (key == "ui") BuildGeneralPage();
            if (key == "about") BuildAboutPage();
            if (key == "bubble") DrawBubblePreview();
            if (key == "anim") BuildAnimPage(); else StopAnimPreviews();
            RefreshPageBar(key);
            Scroller.ScrollToTop();
            AnimatePageIn();
        }
        finally { _navBusy = false; }
    }

    // ---------------- 首页 ----------------
    // 摘要里全部取内存里的当前值（不重读配置），所以没保存的改动也看得到
    static void RefreshHome()
    {
        try
        {
            if (PageHome == null) return;
            CharEntry ce = SelectedChar;
            int frames = FramesOf(ce);
            bool hasArt = ce != null && !string.IsNullOrEmpty(ce.Image) && File.Exists(ce.Image);
            SetReadout(TxtHomeChar, ce != null ? ce.Name : "未找到角色包");
            TxtHomeCharNote.Text = ce == null ? "—"
                : ((ce.VoiceCount > 0 ? ce.VoiceCount + " 条语音" : "0 条语音")
                   + " │ " + (frames > 0 ? frames + " 帧动画" : "无动画")
                   + " │ 立绘 " + (hasArt ? "1" : "0"));
            if (ImgHomeChar != null) ImgHomeChar.Source = ImgChar != null ? ImgChar.Source : null;

            // 换角色就把预览倍率放回 1.0：上一个角色放大着看，切过去还糊一脸不合适
            string cn = ce != null ? ce.Name : "";
            if (cn != _hubZoomChar) { _hubZoomChar = cn; _hubZoom = 1.0; }
            ApplyHubZoom();

            int sz = SldSize != null ? (int)Math.Round(SldSize.Value) : 0;
            SetReadout(TxtHomeSize, sz.ToString());

            int bh = 79;
            int.TryParse(CurOf("bubbleH", "79"), out bh);
            bool bubOn = ChkBub != null && ChkBub.IsChecked == true;
            SetReadout(TxtHomeBubble, bubOn ? bh.ToString() : "已关闭");
            if (UBubble != null) UBubble.Visibility = bubOn ? Visibility.Visible : Visibility.Collapsed;

            int vol = CurVol;
            SetReadout(TxtHomeVol, vol.ToString());
            TxtHomeVolNote.Text = vol == 0 ? "静音：只做动画" : "台词与音效";

            string hexTheme = "#" + _themeColor.R.ToString("X2")
                                   + _themeColor.G.ToString("X2")
                                   + _themeColor.B.ToString("X2");
            SetReadout(TxtHomeTheme, hexTheme);
            if (TxtPalAccent != null) TxtPalAccent.Text = hexTheme;
            // 05 主题色那个小方块 / 调色板方块**不要**在这里赋值：
            // XAML 里它们是 {DynamicResource Accent}，一旦在这里写本地值就把绑定盖掉了，
            // 之后换主题色方块会一直停在旧色（用户反馈的「左侧示例不变」就是这个）。

            TxtHomeRun.Text = _showPetSel ? "RUNNING" : "已隐藏";
            if (BtnHomeShow != null) BtnHomeShow.Content = _showPetSel ? "在桌面隐藏" : "在桌面显示";
            TxtHomePos.Text = HomePosText();

            // 六格刻度：把数值压成长度，一眼看出「大不大 / 满不满」
            if (TxtRunState != null) TxtRunState.Text = _showPetSel ? "RUN" : "HIDDEN";
            if (TxtRunPos != null) TxtRunPos.Text = HomePosText();
            if (TxtTopChar != null) TxtTopChar.Text = ce != null ? ce.Name : "—";
            if (TxtTopVoice != null) TxtTopVoice.Text = (ce != null ? ce.VoiceCount : 0).ToString();
            if (BdRunDot != null)
                BdRunDot.Background = new SolidColorBrush(_showPetSel ? Color.FromRgb(0x12, 0xA1, 0x50)
                                                                     : Color.FromRgb(0x8C, 0x8C, 0x8C));
            SetGauge(GsChar, (ce == null ? 0 : ((ce.VoiceCount > 0 ? 2 : 0) + (frames > 0 ? 2 : 0) + (hasArt ? 2 : 0))) / 6.0);
            SetGauge(GsSize, (sz - 180) / 220.0);
            SetGauge(GsBub, bubOn ? bh / 220.0 : 0);
            SetGauge(GsVol, vol / 100.0);

            if (TxtSumVer != null && TxtSumVer.Text == "Q PET")
            {
                Version v = typeof(Settings).Assembly.GetName().Version;
                TxtSumVer.Text = v == null ? "Q PET" : ("Q PET " + v.Major + "." + v.Minor + "." + v.Build);
            }
        }
        catch (Exception ex) { LogErr("home", ex); }
    }

    // 记每个刻度的当前百分比：换主题色时按原值重涂（SetGauge 赋的是 Brush 实例，
    // 不重涂的话那几条会一直停在上一个有颜色上，换肤只换了一半）
    static readonly Dictionary<Grid, double> _gaugePct = new Dictionary<Grid, double>();

    static void RepaintGauges()
    {
        // 必须先把键抄出来：SetGauge 一进门就 `_gaugePct[g] = pct`，
        // 直接 foreach 字典会在第一次迭代就抛「集合已修改」，只有第一条刻度换得上色
        foreach (var kv in _gaugePct.ToList())
        {
            if (kv.Key != null && kv.Key.Children.Count > 0) SetGauge(kv.Key, kv.Value);
        }
    }

    // 六格刻度：整格 = 主题色，跨格的那一格按比例降透明度，空格 = 分割线灰
    static void SetGauge(Grid g, double pct)
    {
        if (g == null || g.Children.Count == 0) return;
        _gaugePct[g] = pct;
        if (pct < 0) pct = 0; else if (pct > 1) pct = 1;
        Brush on = (Brush)Win.Resources["Accent"], off = (Brush)Win.Resources["Rule"];
        double exact = pct * g.Children.Count;
        for (int i = 0; i < g.Children.Count; i++)
        {
            Border b = g.Children[i] as Border;
            if (b == null) continue;
            double cover = Math.Max(0, Math.Min(1, exact - i));
            b.Background = cover > 0.02 ? on : off;
            double target = cover >= 0.98 ? 1.0 : (cover <= 0.02 ? 1.0 : 0.32 + 0.6 * cover);
            if (_motionOff) { b.BeginAnimation(UIElement.OpacityProperty, null); b.Opacity = target; continue; }
            // 逐格点亮：每格延迟 26ms 起跑，读数变化时像仪表指针扫过去，而不是整排一起闪。
            // 基准值必须留在「起点」(cur)，否则 BeginTime 的等待期内会先跳到终点闪一帧。
            double cur = b.Opacity;
            if (Math.Abs(cur - target) <= 0.001) { b.Opacity = target; continue; }
            b.BeginAnimation(UIElement.OpacityProperty, null);
            b.Opacity = cur;
            DoubleAnimation tick = Fade(cur, target, 240);
            tick.BeginTime = TimeSpan.FromMilliseconds(i * 26);
            b.BeginAnimation(UIElement.OpacityProperty, tick);
        }
    }

    // 桌宠当前落点：pet-pos.txt 是 x=1652 / y=150 两行
    static string HomePosText()
    {
        try
        {
            string f = Path.Combine(Here, "pet-pos.txt");
            if (!File.Exists(f)) return "—";
            int x = 0, y = 0;
            foreach (string line in File.ReadAllLines(f))
            {
                string t = line.Trim();
                if (t.StartsWith("x=")) int.TryParse(t.Substring(2), out x);
                else if (t.StartsWith("y=")) int.TryParse(t.Substring(2), out y);
            }
            return x + " │ " + y;
        }
        catch (Exception ex) { LogErr("HomePosText", ex); return "—"; }
    }

    static void Tile_Click(object sender, RoutedEventArgs e)
    {
        // 磁贴是 ToggleButton，不是 Button：这里必须收 ButtonBase，否则 as Button 直接得 null
        ButtonBase b = sender as ButtonBase;
        if (b == null) return;
        // Tag 只放编号（模板里当编号用），页面 key 走 CommandParameter
        string key = b.CommandParameter as string;
        if (string.IsNullOrEmpty(key)) return;
        // 再点一次当前页就回角色枢纽，省得去够标题栏那个小链接
        ShowPage(key == _curPage ? "home" : key);
    }

    // 当前页那一格：IsChecked ⇒ 模板给整块墨底 + 白斜纹 + 提亮编号。
    // 比「描边 + 淡底」更清楚，而且不用在代码里逐个改文字颜色（Foreground 会继承）。
    static void HighlightTile(string key)
    {
        for (int i = 0; i < Tiles.Length; i++)
        {
            ToggleButton b = Tiles[i];
            if (b == null) continue;
            bool on = key != null && (b.CommandParameter as string) == key;
            if (b.IsChecked != on) b.IsChecked = on;
        }
        PaintTiles();
    }

    // ---------------- 右栏配色方案（A/B/C/D） ----------------
    // 右栏：整列 Desk 灰（和左栏同色），只有当前页是主题色块 + 白字 + 左白条。
    // 色彩面积从 100% 收到 12%，八行靠 icon + 编号 + 标题左右的轮廓区分，不靠色块堆。
    static Border TileHost;


    // 右栏八项的线性图标（16×16 坐标系，只描边不填充）。
    // 顺序必须和 Tiles[] 一致：气泡 / 通用 / 形象 / 动画 / 语音 / 导入 / 统计 / 关于。
    static readonly string[] TileIcons = new string[]
    {
        "M2.5,3.5 H13.5 V11 H8.5 L5.5,13.5 V11 H2.5 Z",                       // 气泡：带小尾巴的框
        "M2.6,5.5 H13.4 M2.6,10.5 H13.4 M4.6,3.6 H8.2 V7.4 H4.6 Z M7.8,8.6 H11.4 V12.4 H7.8 Z",  // 通用：两条滑轨 + 两个滑块
        "M8,7.5 A2.4,2.4 0 1,1 8,2.7 A2.4,2.4 0 1,1 8,7.5 Z M3.8,13.5 C3.8,10.9 5.6,9.4 8,9.4 C10.4,9.4 12.2,10.9 12.2,13.5",  // 形象：头 + 肩
        "M2.5,4.5 H13.5 V11.5 H2.5 Z M5.5,4.5 V11.5 M10.5,4.5 V11.5",        // 动画：胶片
        "M6,4.6 A2,2 0 0,1 10,4.6 V8.4 A2,2 0 0,1 6,8.4 Z M8,10.4 V13 M5.5,13 H10.5",  // 语音：话筒
        "M8,2.6 L13.2,5.4 V10.6 L8,13.4 L2.8,10.6 V5.4 Z M2.8,5.4 L8,8.2 L13.2,5.4 M8,8.2 V13.4",  // 导入：纸箱
        "M4,12.4 V7.4 M8,12.4 V3.8 M12,12.4 V9.4 M2.5,14 H13.5",              // 统计：柱 + 基线
        "M8,2.2 A5.8,5.8 0 1,1 8,13.8 A5.8,5.8 0 1,1 8,2.2 Z M8,7.2 V11 M8,5 V5.2"   // 关于：圆圈 i
    };

    static void PaintTiles()
    {
        if (TileHost == null) return;
        try
        {
            Brush solid = (Brush)Win.Resources["AccentSolid"];
            Brush onSolid = (Brush)Win.Resources["OnAccentSolid"];
            Brush ink = (Brush)Win.Resources["Ink"];
            Brush ink2 = (Brush)Win.Resources["Ink2"];
            Brush desk = (Brush)Win.Resources["Desk"];
            Brush panel = (Brush)Win.Resources["Panel"];
            // 浅底上白色悬停雾看不见，用 5% 墨
            Brush hover = new SolidColorBrush(Color.FromArgb(0x0C, 0, 0, 0));

            TileHost.Background = desk;

            for (int i = 0; i < Tiles.Length; i++)
            {
                ToggleButton t = Tiles[i];
                if (t == null) continue;
                t.ApplyTemplate();
                bool on = t.IsChecked == true;

                t.Background = on ? solid : desk;
                Border dim = t.Template.FindName("dim", t) as Border;
                if (dim != null) dim.Background = desk;      // 未选中时用列底色把这行盖平
                Border bar = t.Template.FindName("selBar", t) as Border;
                if (bar != null) bar.Background = panel;     // 左白条：整行唯一的「当前位置」标记
                System.Windows.Shapes.Path arrow = t.Template.FindName("arrow", t) as System.Windows.Shapes.Path;
                if (arrow != null) arrow.Stroke = on ? panel : ink2;
                Border hov = t.Template.FindName("hoverFill", t) as Border;
                if (hov != null) hov.Background = hover;
                t.Foreground = on ? onSolid : ink;

                System.Windows.Shapes.Path ico = t.Template.FindName("ico", t) as System.Windows.Shapes.Path;
                if (ico != null)
                {
                    ico.Visibility = Visibility.Visible;     // 模板里默认折叠，这里必须是唯一放开的入口
                    ico.Data = Geometry.Parse(TileIcons[i % TileIcons.Length]);
                    ico.Stroke = on ? onSolid : ink;
                }

                StackPanel sp = t.Content as StackPanel;
                if (sp != null && sp.Children.Count > 1)
                {
                    TextBlock sub = sp.Children[1] as TextBlock;
                    if (sub != null) sub.Opacity = on ? 0.78 : 0.62;
                }
            }
        }
        catch (Exception ex) { LogErr("tiles", ex); }
    }


    // 枢纽页的整块预览：白板（边框 + 立绘 + 切角）一起缩放，再整体平移。
    // 缩放原点是板中心（RenderTransformOrigin=0.5,0.5），溢出部分由 hubViewport 裁掉，
    // 所以标题、状态行、按钮的位置永远是稳的。
    static void ApplyHubZoom(bool animate = false)
    {
        if (HubScale == null || HubShift == null) return;
        double fromZoom = HubScale.ScaleX;
        // 基准值先落到目标：动画结束后不会回弹到上一次的倍率
        HubScale.ScaleX = _hubZoom;
        HubScale.ScaleY = _hubZoom;
        if (animate && !_motionOff && Math.Abs(fromZoom - _hubZoom) > 0.0001)
        {
            HubScale.BeginAnimation(ScaleTransform.ScaleXProperty, Fade(fromZoom, _hubZoom, 120));
            HubScale.BeginAnimation(ScaleTransform.ScaleYProperty, Fade(fromZoom, _hubZoom, 120));
        }
        else
        {
            // 拖动时要跟手，不能有 120ms 延迟
            HubScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            HubScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        // 平移夹取：板放大到 w*z，视口只有 w，所以最多能挪出去 (z-1)*w/2（横竖各算各的）。
        // 缩到 1 以下时范围是 0，板自动回正中间。
        double maxX = Math.Max(0, _hubW * (_hubZoom - 1.0) / 2.0);
        double maxY = Math.Max(0, _hubH * (_hubZoom - 1.0) / 2.0);
        if (_hubPanX > maxX) _hubPanX = maxX;
        if (_hubPanX < -maxX) _hubPanX = -maxX;
        if (_hubPanY > maxY) _hubPanY = maxY;
        if (_hubPanY < -maxY) _hubPanY = -maxY;
        HubShift.X = _hubPanX;
        HubShift.Y = _hubPanY;

        if (TxtHubZoom != null)
        {
            TxtHubZoom.Text = "×" + _hubZoom.ToString("0.0", CultureInfo.InvariantCulture);
            TxtHubZoom.Foreground = Math.Abs(_hubZoom - 1.0) < 0.01
                ? (Brush)Win.Resources["Ink2"] : (Brush)Win.Resources["Ink"];
        }
    }

    static void HubWheel(object sender, MouseWheelEventArgs e)
    {
        // 内容页滚轮归 ScrollViewer，别抢
        if (HubView == null || HubView.Visibility != Visibility.Visible) return;
        double z = _hubZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1);
        // 离 1.0 只差一点点就吸回去，省得滚回来永远回不到原大
        if (Math.Abs(z - 1.0) < 0.06) z = 1.0;
        z = Math.Max(0.4, Math.Min(3.0, z));
        e.Handled = true;
        if (Math.Abs(z - _hubZoom) < 0.0001) return;
        _hubZoom = z;
        if (_hubZoom <= 1.0001) { _hubPanX = 0; _hubPanY = 0; }   // 回到 1 倍就居中
        ApplyHubZoom(true);
    }

    // 左键按住拖动整块；双击复位（缩放回 1、位置回正中）
    static void HubDragStart(object sender, MouseButtonEventArgs e)
    {
        if (HubView == null || HubView.Visibility != Visibility.Visible) return;
        if (e.ClickCount == 2)
        {
            _hubZoom = 1.0; _hubPanX = 0; _hubPanY = 0;
            ApplyHubZoom();
            e.Handled = true;
            return;
        }
        if (_hubZoom <= 1.001) return;          // 没放大就没得拖，别把点击吃掉
        _hubDragging = true;
        _hubDragStart = e.GetPosition(HubViewport);
        _hubDragPanX = _hubPanX;
        _hubDragPanY = _hubPanY;
        if (HubViewport != null) HubViewport.CaptureMouse();
        e.Handled = true;
    }

    static void HubDragMove(object sender, MouseEventArgs e)
    {
        if (!_hubDragging || HubViewport == null) return;
        Point p = e.GetPosition(HubViewport);
        _hubPanX = _hubDragPanX + (p.X - _hubDragStart.X);
        _hubPanY = _hubDragPanY + (p.Y - _hubDragStart.Y);
        ApplyHubZoom();
        e.Handled = true;
    }

    static void HubDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_hubDragging) return;
        _hubDragging = false;
        if (HubViewport != null) HubViewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    // 立绘板铺满中栏：不再算「方形边长」，只记下视口的实际宽高给平移夹取用。
    // 窗口缩放 / 切回枢纽时都要重算，否则放大后能拖出黑边。
    static void FitHubPlate()
    {
        if (HubViewport == null || HubPlate == null || HubView == null) return;
        double w = HubViewport.ActualWidth, h = HubViewport.ActualHeight;
        if (w <= 0 || h <= 0) return;           // 枢纽收起时量不到尺寸，等下次显示再算
        _hubW = w; _hubH = h;
        ApplyHubZoom();                         // 视口变了，位移的夹取范围也要重算
    }

    // 自检用：某一格的描边是不是当前主题色
    static bool TileHighlighted(string key)
    {
        foreach (ToggleButton b in Tiles)
        {
            if (b == null || (b.CommandParameter as string) != key) continue;
            return b.IsChecked == true;
        }
        return false;
    }

    static void StopAnimPreviews()
    {
        foreach (AnimCard c in AnimCards) if (c.Preview != null) c.Preview.Stop();
    }

    static void Bind()
    {
        Nav = (ListBox)Win.FindName("nav");
        PageHome = (Grid)Win.FindName("pageHome");
        NavHost = (Border)Win.FindName("navHost");
        ContentHost = (Grid)Win.FindName("contentHost");
        HubView = Win.FindName("hubView") as FrameworkElement;
        HubPlate = Win.FindName("hubPlate") as FrameworkElement;
        MidHost = Win.FindName("midHost") as FrameworkElement;
        TxtHubZoom = Win.FindName("txtHubZoom") as TextBlock;
        // 滚轮挂在整张中栏：空白处滚也算，不用非得把指针对准立绘
        if (MidHost != null) MidHost.PreviewMouseWheel += HubWheel;
        SummaryHost = Win.FindName("summaryHost") as Border;
        ColSummary = Win.FindName("colSummary") as ColumnDefinition;
        ColTiles = Win.FindName("colTiles") as ColumnDefinition;
        // ColumnDefinition 不在视觉树里，FindName 偶尔取不到，回落到索引
        if (ColSummary == null && PageHome != null && PageHome.ColumnDefinitions.Count > 0)
            ColSummary = PageHome.ColumnDefinitions[0];
        if (ColTiles == null && PageHome != null && PageHome.ColumnDefinitions.Count > 2)
            ColTiles = PageHome.ColumnDefinitions[2];
        BtnHome = (Button)Win.FindName("btnHome");
        // 窗口拉宽拉窄时枢纽底板跟着变；内容页量不到宽度，跳过
        Win.SizeChanged += (s2, e2) => { if (_curPage == "home") FitHubPlate(); ApplySummaryDensity(); };
        BtnHomeShow = (Button)Win.FindName("btnHomeShow");
        BtnHomeFolder = (Button)Win.FindName("btnHomeFolder");
        ImgHomeChar = (Image)Win.FindName("imgHomeChar");
        // 立绘基准缩到 0.6：变换只加在这张图上，白板尺寸、平移夹取、读数全按满格算，
        // 所以 ×1.0 仍然是「铺满中栏」，只是她现在只占六成。
        if (ImgHomeChar != null)
        {
            ImgHomeChar.RenderTransformOrigin = new Point(0.5, 0.5);
            ImgHomeChar.RenderTransform = new ScaleTransform(HubArtScale, HubArtScale);
        }

        // 缩放/平移的变换在代码里新建：XAML 里那份可能被 Freeze，改 ScaleX 会抛只读异常
        HubViewport = Win.FindName("hubViewport") as FrameworkElement;
        HubScale = new ScaleTransform(1, 1);
        HubShift = new TranslateTransform(0, 0);
        if (HubPlate != null)
        {
            var tg = new TransformGroup();
            tg.Children.Add(HubScale);
            tg.Children.Add(HubShift);
            HubPlate.RenderTransformOrigin = new Point(0.5, 0.5);
            HubPlate.RenderTransform = tg;
        }
        if (HubViewport != null)
        {
            HubViewport.MouseLeftButtonDown += HubDragStart;
            HubViewport.MouseMove += HubDragMove;
            HubViewport.MouseLeftButtonUp += HubDragEnd;
        }
        ApplyHubZoom();
        TxtHomeChar = (TextBlock)Win.FindName("txtHomeChar");
        TxtHomeCharNote = (TextBlock)Win.FindName("txtHomeCharNote");
        TxtHomeSize = (TextBlock)Win.FindName("txtHomeSize");
        TxtHomeBubble = (TextBlock)Win.FindName("txtHomeBubble");
        TxtHomeVol = (TextBlock)Win.FindName("txtHomeVol");
        TxtHomeVolNote = (TextBlock)Win.FindName("txtHomeVolNote");
        TxtHomeTheme = (TextBlock)Win.FindName("txtHomeTheme");
        TxtHomeRun = (TextBlock)Win.FindName("txtHomeRun");
        TxtHomePos = (TextBlock)Win.FindName("txtHomePos");
        TxtSumVer = (TextBlock)Win.FindName("txtSumVer");
        TxtRunState = (TextBlock)Win.FindName("txtRunState");
        TxtRunPos = (TextBlock)Win.FindName("txtRunPos");
        TxtTopChar = (TextBlock)Win.FindName("txtTopChar");
        TxtTopVoice = (TextBlock)Win.FindName("txtTopVoice");
        BdRunDot = Win.FindName("bdRunDot") as Border;
        TxtPalAccent = Win.FindName("txtPalAccent") as TextBlock;
        PbFrames = (TextBlock)Win.FindName("pbFrames");
        PbDot = Win.FindName("pbDot") as Border;
        GsChar = Win.FindName("gsChar") as Grid;
        GsSize = Win.FindName("gsSize") as Grid;
        GsBub = Win.FindName("gsBub") as Grid;
        GsVol = Win.FindName("gsVol") as Grid;
        UBubble = (TextBlock)Win.FindName("uBubble");
        TxtCharDim = (TextBlock)Win.FindName("txtCharDim");
        TxtSizeDim = (TextBlock)Win.FindName("txtSizeDim");
        SizePresets = Win.FindName("sizePresets") as StackPanel;
        PaletteBlock = Win.FindName("paletteBlock") as StackPanel;
        TileHost = Win.FindName("tilesHost") as Border;
        BtnCharFolder = Win.FindName("btnCharFolder") as Button;
        CmbChar = (ComboBox)Win.FindName("cmbChar"); CmbSet = (ComboBox)Win.FindName("cmbSet");
        CmbEvery = (ComboBox)Win.FindName("cmbEvery"); CmbFont = (ComboBox)Win.FindName("cmbFont");
        BtnSave = (Button)Win.FindName("btnSave");
        BtnCancel = (Button)Win.FindName("btnCancel"); BtnQuit = (Button)Win.FindName("btnQuit");
        BtnMin = (Button)Win.FindName("btnMin"); BtnClose = (Button)Win.FindName("btnClose");
        BtnMax = (Button)Win.FindName("btnMax");
        ImgChar = (Image)Win.FindName("imgChar"); ImgBub = (Image)Win.FindName("imgBub");
        ImgSize = (Image)Win.FindName("imgSize");
        SldSize = (Slider)Win.FindName("sldSize"); SldBub = (Slider)Win.FindName("sldBub");
        LblSize = (TextBlock)Win.FindName("lblSize"); LblBub = (TextBlock)Win.FindName("lblBub");
        LblSizeCap = (TextBlock)Win.FindName("lblSizeCap");
        ChkClick = (CheckBox)Win.FindName("chkClick"); ChkBub = (CheckBox)Win.FindName("chkBub");
        SldVol = (Slider)Win.FindName("sldVol");
        LblVol = (TextBlock)Win.FindName("lblVol");
        LblVoiceHint = (TextBlock)Win.FindName("lblVoiceHint");
        BtnPreview = (Button)Win.FindName("btnPreview");
        BtnRandomLine = (Button)Win.FindName("btnRandomLine");
        BtnAddVoice = (Button)Win.FindName("btnAddVoice");
        CmbClickMode = (ComboBox)Win.FindName("cmbClickMode");
        TxtVoiceSearch = (TextBox)Win.FindName("txtVoiceSearch");
        PhVoiceSearch = (TextBlock)Win.FindName("phVoiceSearch");
        VoiceListPanel = (StackPanel)Win.FindName("voiceList");
        LblVoiceCount = (TextBlock)Win.FindName("lblVoiceCount");
        BubbleStyleHost = (WrapPanel)Win.FindName("bubbleStyleHost");
        BtnBubbleColor = (Button)Win.FindName("btnBubbleColor");
        BubbleColorHost = (WrapPanel)Win.FindName("bubbleColorHost");
        BtnBubbleInk = (Button)Win.FindName("btnBubbleInk");
        BubbleInkHost = (WrapPanel)Win.FindName("bubbleInkHost");
        CmbFontSize = (ComboBox)Win.FindName("cmbFontSize");
        LblFontSize = (TextBlock)Win.FindName("lblFontSize");
        LblBubSize = (TextBlock)Win.FindName("lblBubSize");
        LblDirty = (TextBlock)Win.FindName("lblDirty");
        LblSaved = (TextBlock)Win.FindName("lblSaved");
        PbNum = (TextBlock)Win.FindName("pbNum"); PbName = (TextBlock)Win.FindName("pbName");
        PbChar = (TextBlock)Win.FindName("pbChar"); PbSize = (TextBlock)Win.FindName("pbSize");
        PbVoice = (TextBlock)Win.FindName("pbVoice"); PbState = (TextBlock)Win.FindName("pbState");
        BtnUpBubble = (Button)Win.FindName("btnUpBubble");
        LblBubHint = (TextBlock)Win.FindName("lblBubHint");
        BtnUpBubble.Click += (s, e) => UploadBubbleFrame();
        _themeColor = WpfParseHex(CurOf("themeColor", ""), _themeColor);
        _showPetSel = CurOf("showPet", "1") != "0";
        _hideFsSel = CurOf("hideFullscreen", "1") != "0";
        ApplyThemeColor(_themeColor);
        InstallButtonChamfer();      // 主按钮的右下切角（真裁切，不依赖父容器底色）
        HookComboOneClick(Win);      // 所有下拉框：点一下就展开（含各页里静态声明的那些）
        BtnBubbleColor.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(_bubbleColorSel, out picked))
            {
                _bubbleColorSel = picked;
                _bubAccent = picked;
                BuildBubbleColorTiles();
                BuildBubbleStyleTiles();
                DrawBubblePreview();
            }
        };
        _bubbleInkSel = CurOf("bubbleInk", "auto");
        BtnBubbleInk.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(WpfParseHex(_bubbleInkSel, Color.FromRgb(0x1A, 0x1A, 0x1A)), out picked))
            {
                _bubbleInkSel = HexOf(picked);
                BuildBubbleInkTiles();
                DrawBubblePreview();
            }
        };
        _bubbleStyleSel = CurBubbleStyle;
        _bubbleColorSel = WpfParseHex(CurOf("bubbleColor", ""), _bubbleColorSel);
        _bubAccent = _bubbleColorSel;
        {
            float fsz;
            if (float.TryParse(CurOf("bubbleFontSize", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out fsz))
                _bubbleFontPtSel = Math.Max(4, Math.Min(72, fsz));
            else _bubbleFontPtSel = 10.5f;
        }
        SetupFontSize();     // 解析完配置再填字号下拉框（顺序反了会显示默认值）
        _bubbleFrameSel = CurBubbleFrame;
        if (_bubbleStyleSel == "custom" && (_bubbleFrameSel == "" || !File.Exists(_bubbleFrameSel)))
            _bubbleStyleSel = "card";
        BtnPreview.Click += (s, e) => PreviewVoice();
        BtnRandomLine.Click += (s, e) => PlayRandomLine();
        BtnAddVoice.Click += (s, e) => AddVoices();
        TxtVoiceSearch.TextChanged += (s, e) =>
        {
            if (PhVoiceSearch != null)
                PhVoiceSearch.Visibility = string.IsNullOrEmpty(TxtVoiceSearch.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            CancelVoiceEdit();
            RefreshVoiceList();
        };
        PageChar = (StackPanel)Win.FindName("pageChar");
        PageVoice = (StackPanel)Win.FindName("pageVoice"); PageBubble = (StackPanel)Win.FindName("pageBubble");
        PageAnim = (StackPanel)Win.FindName("pageAnim");
        PageStats = (StackPanel)Win.FindName("pageStats");
        PageUi = (StackPanel)Win.FindName("pageUi");
        PageAbout = (StackPanel)Win.FindName("pageAbout");
        UiContent = (StackPanel)Win.FindName("uiContent");
        PageImport = (StackPanel)Win.FindName("pageImport");
        ImportList = (StackPanel)Win.FindName("importList");
        TxtName = (TextBox)Win.FindName("txtName");
        LblImg = (TextBlock)Win.FindName("lblImg"); LblVoices = (TextBlock)Win.FindName("lblVoices");
        LblImportMsg = (TextBlock)Win.FindName("lblImportMsg");
        BtnPickImg = (Button)Win.FindName("btnPickImg"); BtnPickVoices = (Button)Win.FindName("btnPickVoices");
        BtnImport = (Button)Win.FindName("btnImport");
        BtnPickImg.Click += (s, e) => PickImportImage();
        BtnPickVoices.Click += (s, e) => PickImportVoices();
        BtnImport.Click += (s, e) => DoImport();
        Scroller = (ScrollViewer)Win.FindName("scroller");
        VoiceScroller = (ScrollViewer)Win.FindName("voiceScroller");
        // 不接管主内容区的滚轮：外层滚动容器已移除，scroller 是唯一滚动区，
        // WPF 原生滚轮处理在单层场景下最稳（不会和外层空转打架）。

        // 窗口 / 任务栏 / 标题栏图标：用她的大头照（PNG 解码最稳；ICO 有些解码器不认）
        try
        {
            string png = Path.Combine(Here, "pet.png");
            if (!File.Exists(png)) png = Path.Combine(Here, "icon-final.png");
            if (!File.Exists(png)) png = Path.Combine(Here, "pet-placeholder.png");
            if (File.Exists(png))
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(png); bi.EndInit(); bi.Freeze();
                Win.Icon = bi;
                var iconImg = (Image)Win.FindName("imgIcon");
                if (iconImg != null) iconImg.Source = bi;
            }
        }
        catch (Exception ex) { LogErr("icon", ex); }

        // 角色候选（默认 + 导入的角色包 + 本目录其它 PNG）
        ReloadChars();
        ReloadSfxGroups();
        CmbClickMode.Items.Add("随机一句");
        CmbClickMode.Items.Add("顺序播放");
        CmbClickMode.SelectedIndex = CurOf("clickLineMode", "random") == "seq" ? 1 : 0;

        foreach (string e in EveryNames) CmbEvery.Items.Add(e);
        int ei = 1; for (int i = 0; i < EveryVals.Length; i++) if (EveryVals[i] == CurEvery) ei = i;
        CmbEvery.SelectedIndex = ei;

        // 字体候选（只列本机真装了的）
        string[] cands = { "YouYuan", "Noto Sans SC Medium", "Noto Sans SC", "DengXian", "Microsoft YaHei UI", "KaiTi", "SimHei", "FangSong" };
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamily ff in Fonts.SystemFontFamilies) installed.Add(ff.Source);
        var fonts = new List<string>();
        if (installed.Contains(CurFont)) fonts.Add(CurFont); else fonts.Add(CurFont);
        foreach (string c in cands) if (installed.Contains(c) && !fonts.Contains(c)) fonts.Add(c);
        foreach (string f in fonts) CmbFont.Items.Add(f);
        CmbFont.SelectedIndex = 0;

        int sz = Math.Max(180, Math.Min(400, CurSize));
        SldSize.Value = sz; LblSize.Text = sz.ToString();
        int bh = Math.Max(70, Math.Min(220, CurBubH));
        SldBub.Value = bh; LblBub.Text = bh + " px";
        ChkClick.IsChecked = CurClick;
        ChkBub.IsChecked = CurBubH > 0;
        SyncBubbleEnabled();

        // 音量（语音页）
        int vol = CurVol;
        SldVol.Value = vol; LblVol.Text = vol + "%";
        LblVoiceHint.Text = vol == 0 ? "当前静音：只做动画，不出声" : "所有台词与音效都会按这个音量播放";

        // 形象页：五个高度预设按钮
        BuildSizePresets();

        // 动画页：卡片在代码里生成（每类动画一块：预览 / 开关 / 幅度 / 倍速）
        BuildAnimPage();

        // 事件
        Nav.SelectionChanged += (s, e) =>
        {
            try
            {
                ListBoxItem it = Nav.SelectedItem as ListBoxItem;
                string key = it != null ? it.Tag as string : null;
                if (!string.IsNullOrEmpty(key)) ShowPage(key);
            }
            catch (Exception ex) { LogErr("nav", ex); }
        };

        // 首页：返回、八格入口、两个主操作
        if (BtnHome != null) BtnHome.Click += (s, e) => ShowPage("home");
        string[] tileNames = { "tileBubble", "tileUi", "tileChar", "tileAnim",
                               "tileVoice", "tileImport", "tileStats", "tileAbout" };
        for (int i = 0; i < tileNames.Length; i++)
        {
            ToggleButton tb = Win.FindName(tileNames[i]) as ToggleButton;
            if (tb != null) { Tiles[i] = tb; tb.Click += Tile_Click; }
        }
        if (BtnHomeShow != null)
            BtnHomeShow.Click += (s, e) => { try { SetShowPet(!_showPetSel); RefreshHome(); } catch (Exception ex) { LogErr("homeshow", ex); } };
        if (BtnHomeFolder != null)
            BtnHomeFolder.Click += (s, e) =>
            {
                try
                {
                    CharEntry ce = SelectedChar;
                    if (ce != null && !string.IsNullOrEmpty(ce.Dir) && Directory.Exists(ce.Dir))
                        Process.Start("explorer.exe", "\"" + ce.Dir + "\"");
                }
                catch (Exception ex) { LogErr("homefolder", ex); }
            };
        if (BtnCharFolder != null)
            BtnCharFolder.Click += (s, e) =>
            {
                try
                {
                    CharEntry ce = SelectedChar;
                    if (ce != null && !string.IsNullOrEmpty(ce.Dir) && Directory.Exists(ce.Dir))
                        Process.Start("explorer.exe", "\"" + ce.Dir + "\"");
                }
                catch (Exception ex) { LogErr("charfolder", ex); }
            };
        CmbChar.SelectionChanged += (s, e) =>
        {
            try
            {
                // 每个角色有自己的高度：切角色时滑块跟着走，保存时只重出这个角色的动画
                CharEntry ce = SelectedChar;
                if (ce != null && ce.Dir != "" && ce.CharH >= 160 && ce.CharH <= 520)
                {
                    SldSize.Value = ce.CharH;
                    LblSize.Text = ce.CharH.ToString();
                }
                ShowCharPreview(); ShowSizePreview();
                if (PageAnim != null && PageAnim.Visibility == Visibility.Visible) BuildAnimPage();
                if (PageVoice != null && PageVoice.Visibility == Visibility.Visible) LoadVoiceLibrary();
            }
            catch (Exception ex) { LogErr("charsel", ex); }
        };
        CmbFont.SelectionChanged += (s, e) => DrawBubblePreview();
        SldSize.ValueChanged += (s, e) =>
        {
            LblSize.Text = ((int)SldSize.Value).ToString();
            SyncSizePresets();
            ShowSizePreview();
        };
        SldBub.ValueChanged += (s, e) => { LblBub.Text = (int)SldBub.Value + " px"; };
        // 气泡关掉后，下面这些（字体 / 颜色 / 字号 / 外框）改了也看不到 ⇒ 整体置灰
        ChkBub.Checked += (s, e) => SyncBubbleEnabled();
        ChkBub.Unchecked += (s, e) => SyncBubbleEnabled();

        // 音量：拖动时实时改预览音量（如果正在试听，能立刻听出差别）
        SldVol.ValueChanged += (s, e) =>
        {
            int v = (int)SldVol.Value;
            LblVol.Text = v + "%";
            LblVoiceHint.Text = v == 0 ? "当前静音：只做动画，不出声" : "所有台词与音效都会按这个音量播放";
            if (PreviewPlayer != null) PreviewPlayer.Volume = v / 100.0;
        };
        BtnPreview.Click += (s, e) => PreviewVoice();

        BtnCancel.Click += (s, e) => Win.Close();
        BtnMin.Click += (s, e) => Win.WindowState = WindowState.Minimized;
        BtnMax.Click += (s, e) => Win.WindowState = (Win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        BtnClose.Click += (s, e) => Win.Close();
        Win.StateChanged += (s, e) =>
        {
            if (BtnMax != null) BtnMax.Content = (Win.WindowState == WindowState.Maximized) ? "\uE923" : "\uE922";
        };
        BtnQuit.Click += (s, e) => QuitPet();
        BtnSave.Click += (s, e) => Save();

        ShowCharPreview();
        ShowSizePreview();
        BuildBubbleStyleTiles();
        BuildBubbleColorTiles();
        BuildBubbleInkTiles();
        DrawBubblePreview();
        ShowPage("home");
        StartDirtyWatch();          // 底部「有未保存的更改」提示

        // Esc = 取消（等于点「取消」）；下拉展开时先让 Esc 收下拉
        Win.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Escape) return;
            ComboBox cb = Keyboard.FocusedElement as ComboBox;
            if (cb != null && cb.IsDropDownOpen) return;
            e.Handled = true;
            Win.Close();
        };

        // 有改动没保存就关窗 → 先问一句，别白改
        Win.Closing += (s, e) =>
        {
            try
            {
                if (_savedSnapshot == "" || Snapshot() == _savedSnapshot) return;
                if (!Dlg.Ask("还有没保存的更改",
                        "你改了设置但还没点「保存更改」。现在关掉的话，这些改动不会生效。",
                        "不保存退出", "继续编辑", true))
                    e.Cancel = true;
            }
            catch (Exception ex) { LogErr("Bind", ex); }
        };

        Win.Closed += (s, e) =>
        {
            StopLiveStats();
            if (_dirtyTimer != null) { _dirtyTimer.Stop(); _dirtyTimer = null; }
            if (_savedNoteTimer != null) { _savedNoteTimer.Stop(); _savedNoteTimer = null; }
            try { if (PreviewPlayer != null) { PreviewPlayer.Close(); PreviewPlayer = null; } } catch (Exception) { }
        };
    }

    // internal：同文件里其它顶层类（弹窗等）也要能记错误
    internal static void LogErr(string where, Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Here, "pet-settings-err.log"), DateTime.Now.ToString("HH:mm:ss") + "  [" + where + "] " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0] + "\r\n"); }
        catch (Exception) { }
    }

    // ---------------- 预览 ----------------
    static BitmapImage LoadImg(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex) { LogErr("LoadImg", ex); return null; }
    }
    // 高度预设：180 / 219 / 260 / 320 / 400。当前值落在哪一档，那一格就整块墨底白字
    static readonly int[] SizePresetValues = { 180, 219, 260, 320, 400 };

    static void BuildSizePresets()
    {
        if (SizePresets == null) return;
        SizePresets.Children.Clear();
        Style st = Win.TryFindResource("PresetButton") as Style;
        List<Button> list = new List<Button>();
        foreach (int v in SizePresetValues)
        {
            Button b = new Button { Content = v.ToString(), Style = st, Tag = v, Margin = new Thickness(0, 0, 6, 0) };
            int val = v;
            b.Click += (s2, e2) => { if (SldSize != null) SldSize.Value = val; };
            SizePresets.Children.Add(b);
            list.Add(b);
        }
        SizePresetBtns = list.ToArray();
        SyncSizePresets();
    }

    static void SyncSizePresets()
    {
        if (SizePresetBtns == null || SldSize == null) return;
        // 选中态走主题色填充：这一排是「按钮」，不是色块，用墨色就变成界面里唯一的黑块
        Brush onFill = (Brush)Win.Resources["AccentSolid"];
        Brush onInk = (Brush)Win.Resources["OnAccentSolid"];
        Brush offFill = (Brush)Win.Resources["Panel"];
        Brush ink = (Brush)Win.Resources["InkSolid"];
        Brush line = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6));
        int cur = (int)Math.Round(SldSize.Value);
        for (int i = 0; i < SizePresetBtns.Length; i++)
        {
            Button b = SizePresetBtns[i];
            if (b == null) continue;
            bool on = Math.Abs(cur - (int)b.Tag) <= 3;
            b.Background = on ? onFill : offFill;
            b.Foreground = on ? onInk : ink;
            b.BorderBrush = on ? (Brush)Win.Resources["AccentEdge"] : line;
        }
    }

    // 窗口矮到 620 时左栏放不下「五条读数 + 调色板」：把调色板收起来，
    // 读数优先（版本号那一行始终留着）。780 以上再放回来。
    static void ApplySummaryDensity()
    {
        // 调色板图例已按用户要求删掉（PaletteBlock 恒为 null），这里不能再靠它做前置判断，
        // 否则矮窗口的压行距整套逻辑会被一并跳过。
        if (SummaryHost == null) return;
        double h = SummaryHost.ActualHeight;
        if (h <= 0) return;
        bool dense = h < 660;

        // 620 高时五条读数总高 560 > 视口 513，首屏就露出半行被切（看着像排版坏了）。
        // 压行距 + 缩读数，一屏放完；滚动条只留作保险，不指望用户去滚。
        if (_summaryDense == dense) return;
        _summaryDense = dense;
        Thickness pad = dense ? new Thickness(14, 7, 14, 9) : new Thickness(14, 11, 14, 13);
        StackPanel rows = SummaryRows(SummaryHost);
        if (rows != null)
            foreach (object o in rows.Children)
                if (o is Border) ((Border)o).Padding = pad;
        double fs = dense ? 21 : 24;
        if (TxtHomeChar != null) TxtHomeChar.FontSize = fs;
        if (TxtHomeSize != null) TxtHomeSize.FontSize = fs;
        if (TxtHomeBubble != null) TxtHomeBubble.FontSize = fs;
        if (TxtHomeVol != null) TxtHomeVol.FontSize = fs;
    }

    // 左栏 ScrollViewer 里那一列「行」：找第一个 StackPanel 就是它（结构固定：Border > Grid > ScrollViewer > StackPanel）
    static StackPanel SummaryRows(DependencyObject root)
    {
        if (root == null) return null;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            DependencyObject c = VisualTreeHelper.GetChild(root, i);
            StackPanel sp = c as StackPanel;
            if (sp != null && sp.Children.Count >= 5) return sp;
            StackPanel deep = SummaryRows(c);
            if (deep != null) return deep;
        }
        return null;
    }

    static void ShowCharPreview()
    {
        // 列表刷新时会短暂出现 SelectedIndex = -1（之前没防越界 ⇒ 删角色会崩窗口）
        int i = CmbChar.SelectedIndex;
        if (ImgChar == null || i < 0 || i >= ChList.Count) return;
        ImgChar.Source = LoadImg(ChList[i].Image);
        if (TxtCharDim != null)
        {
            BitmapSource bmp = ImgChar.Source as BitmapSource;
            TxtCharDim.Text = bmp == null ? "—" : bmp.PixelWidth + " × " + bmp.PixelHeight;
        }
    }

    // 大小预览：按选择的像素高度等比展示，并给出实际角色/窗口尺寸
    static void ShowSizePreview()
    {
        try
        {
            int i = CmbChar.SelectedIndex;
            if (i < 0 || i >= ChList.Count) return;
            int h = (int)SldSize.Value;
            var src = LoadImg(ChList[i].Image);
            if (ImgSize == null) return;
            ImgSize.Source = src;
            // 预览容器 236 高（含 10 内距）：400px 时占满
            double ph = 212.0 * h / 400.0;
            ImgSize.Height = ph;
            if (TxtSizeDim != null)
            {
                BitmapSource bmp = src as BitmapSource;
                TxtSizeDim.Text = bmp == null ? "—" : bmp.PixelWidth + " × " + bmp.PixelHeight;
            }
            if (LblSizeCap != null)
            {
                int w = (int)Math.Round(h * 320.0 / 300.0);
                int fh = (int)Math.Round(h * 324.0 / 300.0);
                int bub = ChkBub.IsChecked == true ? (int)SldBub.Value : 0;
                LblSizeCap.Text = string.Format("角色 {0} × {1} px　│　桌宠窗口 {2} × {3} px（含气泡 {4}）", w, fh, w, fh + bub, bub);
            }
        }
        catch (Exception ex) { LogErr("sizeprev", ex); }
    }

    // 气泡轮廓（预览用）：圆角矩形 + 尾巴，一条闭合路径 ⇒ 描边不会在内部留接缝
    static Geometry BubbleGeometry(double l, double t, double r, double b, double rad,
                                   double tailL, double tailR, double tipX, double tipY)
    {
        var g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(l + rad, t), true, true);
            c.LineTo(new Point(r - rad, t), true, false);
            c.ArcTo(new Point(r, t + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r, b - rad), true, false);
            c.ArcTo(new Point(r - rad, b), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(tailR, b), true, false);
            c.LineTo(new Point(tipX, tipY), true, false);
            c.LineTo(new Point(tailL, b), true, false);
            c.LineTo(new Point(l + rad, b), true, false);
            c.ArcTo(new Point(l, b - rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(l, t + rad), true, false);
            c.ArcTo(new Point(l + rad, t), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    // ---------------- 动画页（真实帧预览 + 幅度 / 倍速 / 开关） ----------------
    static string Cap(string s)
    {
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    static double EntryAmp(CharEntry e, string key)
    {
        if (e == null) return 1;
        if (key == "idle") return e.AmpIdle;
        if (key == "press") return e.AmpPress;
        if (key == "click") return e.AmpClick;
        return e.AmpDrag;
    }

    static void SetEntryAmp(CharEntry e, string key, double v)
    {
        if (e == null) return;
        if (key == "idle") e.AmpIdle = v;
        else if (key == "press") e.AmpPress = v;
        else if (key == "click") e.AmpClick = v;
        else e.AmpDrag = v;
    }








    public static string HexOf(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    internal static Color ThemeColor { get { return _themeColor; } }      // 供 Dlg/ColorDlg 共用
    public static Color WpfParseHex(string s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        s = s.Trim().TrimStart('#');
        if (s.Length != 6) return fallback;
        byte r, g, b;
        if (byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
            byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
            byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            return Color.FromRgb(r, g, b);
        return fallback;
    }

    static readonly string[][] BubbleStyleDefs = {
        new[] { "card",    "圆角卡片", "渐变卡片 + 小尾巴（默认）" },
        new[] { "classic", "经典椭圆", "插件同款：白色椭圆 + 两个小圆点" },
        new[] { "comic",   "漫画描边", "粗黑描边 + 硬阴影" },
        new[] { "glass",   "半透明",   "磨砂玻璃感，能透出桌面" },
    };

    // ---- 颜色预设（气泡颜色 / 主题色共用）----
    static readonly string[][] ColorPresets = {
        new[] { "8FA6D8", "默认蓝" }, new[] { "005FB8", "群青" },
        new[] { "3B9BE0", "天蓝" },   new[] { "2FA8A0", "青绿" },
        new[] { "5FA855", "草绿" },   new[] { "C8A227", "芥黄" },
        new[] { "E08A3C", "落日" },   new[] { "E06C8B", "玫粉" },
        new[] { "9B7BD8", "紫罗兰" }, new[] { "6B7280", "石墨" },
        new[] { "3F4A5A", "深灰蓝" }, new[] { "2F3542", "墨黑" },
    };





    // ---- 文字颜色 ----
    static readonly string[][] InkPresets = {
        new[] { "auto",   "自动" },   new[] { "1A1A1A", "纯黑" },
        new[] { "FFFFFF", "纯白" },   new[] { "203170", "深蓝" },
        new[] { "C2426B", "玫粉" },   new[] { "8A6D1F", "芥黄" },
        new[] { "2E6B4F", "墨绿" },   new[] { "6B4E8A", "紫褐" },
    };























    // ---------------- 通用设置：主题色 + 开机自启动 ----------------
    // 主题色：Accent 画刷会被样式封箱冻结（Style.Seal 会冻结 Setter 里的 Freezable），
    // 直接改 Color 会抛「只读状态」异常。正确做法：XAML 里全用 {DynamicResource Accent}，
    // 这里整体替换 Win.Resources["Accent"] 实例，所有引用自动跟着换。
    // 界面字体：Saira Wide（可变字体切到 125% 宽，SIL OFL）打包在 exe 资源里。
    // 用代码加载而不是在 XAML 里写 pack URI：加载失败时能退回系统字体，
    // 不让整个面板因为一个字体白屏；同时把结果记进自检，避免"以为生效了"。
    static string _uiFontInfo = "未加载";







    // 压在信号色上的字：亮底用墨、深底用白。黄底配白字等于没字。
    internal static Color SignalInk(Color c)
    {
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 140
            ? Color.FromRgb(0x16, 0x16, 0x16) : Colors.White;
    }

    internal static double RelLum(Color c)
    {
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
    internal static double Contrast(Color a, Color b)
    {
        double la = RelLum(a), lb = RelLum(b);
        if (la < lb) { double t = la; la = lb; lb = t; }
        return (la + 0.05) / (lb + 0.05);
    }
    // 往黑里压，直到与背景（bg）的对比度达到 want；压不动了退回深墨。
    internal static Color Darken(Color c, Color bg, double want)
    {
        for (double f = 1.0; f > 0.18; f -= 0.02)
        {
            Color t = Color.FromRgb((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f),
                                    (byte)Math.Round(c.B * f));
            if (Contrast(t, bg) >= want) return t;
        }
        return Color.FromRgb(0x1A, 0x1D, 0x21);
    }

    // 往白里调：给选中态做浅底。amount = 白色占比（0.9 = 九成白）
    internal static Color Tint(Color c, double amount)
    {
        if (amount < 0) amount = 0;
        if (amount > 1) amount = 1;
        return Color.FromRgb(
            (byte)Math.Round(c.R + (255 - c.R) * amount),
            (byte)Math.Round(c.G + (255 - c.G) * amount),
            (byte)Math.Round(c.B + (255 - c.B) * amount));
    }


    // 按钮三态：默认 / 悬停 / 按下。深色主题靠提亮，其余靠压深；
    // 提亮幅度夹在「看得出差别」和「白字还能读」之间，扫描见 --selftest 的色域一行。
    internal static void BtnTones(Color accent, out Color fill, out Color hover, out Color press)
    {
        fill = Darken(accent, Colors.White, 4.5);
        bool deep = RelLum(fill) < 0.05;
        hover = deep ? Shade(fill, 0.15) : Shade(fill, -0.18);
        press = deep ? Shade(fill, 0.28) : Shade(fill, -0.34);
    }

    // ---- 主按钮的右下切角 ----
    // 旧的切角是「盖一块父容器同色的三角」：按钮一放到 Desk / Paper 上就露出一块白，
    // 换底色还得跟着改三角颜色。这里改成真裁切 —— Clip 只影响按钮自己的绘制，
    // FocusRing 画在 AdornerLayer 上（不是按钮的子元素），不受影响。
    // 用 SizeChanged 的类处理器而不是 XAML 里的 EventSetter：XamlText 是运行时解析的字符串，
    // EventSetter 绑不到代码里的方法。
    static Style _primaryBtnStyle;
    static bool _chamferHooked;








    // 注册值里指向的 exe（去掉引号与参数）
    static string AutoStartExeOf(string val)
    {
        try
        {
            if (string.IsNullOrEmpty(val)) return "";
            string s = val.Trim();
            if (s.StartsWith("\""))
            {
                int e = s.IndexOf('"', 1);
                s = e > 0 ? s.Substring(1, e - 1) : s.Trim('"');
            }
            else
            {
                int sp = s.IndexOf(' ');
                if (sp > 0) s = s.Substring(0, sp);
            }
            return s;
        }
        catch (Exception ex) { LogErr("AutoStartExeOf", ex); return ""; }
    }

    static string AutoStartValue()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (k != null)
                {
                    object v = k.GetValue(RunValueName);
                    if (v != null) return v.ToString();
                }
            }
        }
        catch (Exception ex) { LogErr("AutoStartValue", ex); }
        return "";
    }

    // 启动时自检：注册表里那条路径早就不存在（文件夹被挪走 / 删了 / 换机器）就按当前位置重写。
    // 以前不管 ⇒ 用户把整个文件夹搬到别处后开机自启静默失效，界面上却还是「已开启」。
    static string _autoStartNote = "";
    static void RepairAutoStart()
    {
        try
        {
            string val = AutoStartValue();
            if (val == "") return;
            string exe = AutoStartExeOf(val);
            if (exe == "") return;
            string want = Path.Combine(Here, PetExeName);
            if (string.Equals(exe, want, StringComparison.OrdinalIgnoreCase)) return;
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) return;
                k.SetValue(RunValueName, "\"" + want + "\"", Microsoft.Win32.RegistryValueKind.String);
                _autoStartNote = File.Exists(exe)
                    ? "开机自启指向的是另一份副本，已改成当前文件夹"
                    : "开机自启的路径已失效（文件夹挪过位置），已按当前位置重写";
            }
        }
        catch (Exception ex) { LogErr("RepairAutoStart", ex); }
    }

    static bool AutoStartEnabled()
    {
        try
        {
            string val = AutoStartValue();
            if (val != "")
            {
                // 路径已经不存在 ⇒ 这条启动项是废的，界面上就不该显示「已开启」
                string exe = AutoStartExeOf(val);
                if (exe == "" || File.Exists(exe)) return true;
            }
        }
        catch (Exception ex) { LogErr("AutoStartEnabled", ex); }
        // 旧的「启动文件夹」方式也算开机自启
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (File.Exists(Path.Combine(dir, StartupScriptName + ".vbs"))) return true;
            if (File.Exists(Path.Combine(dir, StartupScriptName + ".vbs.disabled"))) return true;
        }
        catch (Exception ex) { LogErr("AutoStartEnabled", ex); }
        return false;
    }

    static void SetAutoStart(bool on)
    {
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string vbs = Path.Combine(dir, StartupScriptName + ".vbs");
            string dis = vbs + ".disabled";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) return;
                if (on)
                {
                    k.SetValue(RunValueName, "\"" + Path.Combine(Here, PetExeName) + "\"",
                        Microsoft.Win32.RegistryValueKind.String);
                    // 启动文件夹里那份挪开，免得开机起来两个
                    if (File.Exists(vbs)) File.Move(vbs, dis);
                }
                else
                {
                    k.DeleteValue(RunValueName, false);
                }
            }
        }
        catch (Exception ex) { LogErr("autostart", ex); Dlg.Info("设置开机自启动失败", ex.Message, "好"); }
    }

    static void BuildGeneralPage()
    {
        if (UiContent == null) return;
        UiContent.Children.Clear();

        // ---- 桌面显示：总开关（关掉后 Pet.exe 根本不启动）----
        // 这个开关**切换即生效**：直接写盘 + 启停进程，所以不进「未保存」判定（见 Snapshot）。
        var cardShow = new Border { Style = (Style)Win.FindResource("Card") };
        var spShow = new StackPanel();
        spShow.Children.Add(Txt("桌面显示", 14, "#E4000000", true));
        spShow.Children.Add(Txt("关掉后她不在桌面出现", 12, "#99000000"));
        ChkShowPet = new CheckBox
        {
            Content = "在桌面显示桌宠", FontSize = 13, Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Win.FindResource("ToggleSwitch")
        };
        ChkShowPet.IsChecked = _showPetSel;      // 先设值再挂事件，避免重建页面时误触发
        ChkShowPet.Checked += (s, e) => SetShowPet(true);
        ChkShowPet.Unchecked += (s, e) => SetShowPet(false);
        spShow.Children.Add(ChkShowPet);
        var tipShow = Txt("切换立即生效", 11, "#99000000");
        tipShow.ToolTip = "关掉后桌宠进程会退出，不再占内存、也不统计按键。\n想再打开：双击 PetSettings.exe → 通用设置 → 桌面显示。";
        tipShow.Margin = new Thickness(0, 6, 0, 0);
        spShow.Children.Add(tipShow);
        cardShow.Child = spShow;
        UiContent.Children.Add(cardShow);

        // ---- 全屏隐藏：看视频 / 打游戏时前台铺满整块屏幕，她就让开 ----
        var cardFs = new Border { Style = (Style)Win.FindResource("Card") };
        var spFs = new StackPanel();
        spFs.Children.Add(Txt("全屏隐藏", 14, "#E4000000", true));
        spFs.Children.Add(Txt("看视频、打游戏时自动让开", 12, "#99000000"));
        ChkHideFs = new CheckBox
        {
            Content = "全屏时隐藏桌宠", FontSize = 13, Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Win.FindResource("ToggleSwitch")
        };
        ChkHideFs.IsChecked = _hideFsSel;        // 先设值再挂事件，避免重建页面时误触发
        ChkHideFs.Checked += (s, e) => SetHideFs(true);
        ChkHideFs.Unchecked += (s, e) => SetHideFs(false);
        spFs.Children.Add(ChkHideFs);
        var tipFs = Txt("切换立即生效", 11, "#99000000");
        tipFs.ToolTip = "前台窗口铺满整块屏幕（不含任务栏的全屏程序）时自动隐藏，退出全屏立刻回来。\n"
                      + "仍是后台统计按键，只是不画在屏幕上。";
        tipFs.Margin = new Thickness(0, 6, 0, 0);
        spFs.Children.Add(tipFs);
        cardFs.Child = spFs;
        UiContent.Children.Add(cardFs);

        var card1 = new Border { Style = (Style)Win.FindResource("Card") };
        var sp1 = new StackPanel();
        sp1.Children.Add(Txt("主题颜色", 14, "#E4000000", true));
        sp1.Children.Add(Txt("导航、开关、当前项的填充色", 12, "#99000000"));
        ThemeColorHost = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        sp1.Children.Add(ThemeColorHost);
        var pickRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var bCustom = new Button { Content = "自定义颜色…", Style = (Style)Win.FindResource("SecondaryButton") };
        bCustom.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(_themeColor, out picked)) { ApplyThemeColor(picked, true); RefreshThemeTiles(); }
        };
        pickRow.Children.Add(bCustom);
        var bReset = new Button { Content = "恢复默认", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        // 默认 = make-pet-config.ps1 里的 themeColor（群青 #005FB8）。这里以前写的是 #FFFA00（荧光黄），显然不对
        bReset.Click += (s, e) => { ApplyThemeColor(ThemeDefault, true); RefreshThemeTiles(); };
        pickRow.Children.Add(bReset);
        sp1.Children.Add(pickRow);
        card1.Child = sp1;
        UiContent.Children.Add(card1);

        _themePicker = c => { ApplyThemeColor(c, true); RefreshThemeTiles(); };
        BuildColorTiles(ThemeColorHost, _themeColor, _themePicker, HexOf(_themeColor), _themeColor);

        var card2 = new Border { Style = (Style)Win.FindResource("Card") };
        var sp2 = new StackPanel();
        sp2.Children.Add(Txt("开机自启动", 14, "#E4000000", true));
        sp2.Children.Add(Txt("开机后自动出现在桌面", 12, "#99000000"));
        ChkAutoStart = new CheckBox
        {
            Content = "开机自动启动桌宠", FontSize = 13, Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Win.FindResource("ToggleSwitch")
        };
        ChkAutoStart.IsChecked = AutoStartEnabled();
        ChkAutoStart.Checked += (s, e) => SetAutoStart(true);
        ChkAutoStart.Unchecked += (s, e) => SetAutoStart(false);
        sp2.Children.Add(ChkAutoStart);
        var tipAuto = Txt("设置立即生效", 11, "#99000000");
        tipAuto.ToolTip = "写入当前用户的注册表启动项；原来放在「启动」文件夹里的旧方式会自动挪开，避免开机起两个。";
        tipAuto.Margin = new Thickness(0, 6, 0, 0);
        sp2.Children.Add(tipAuto);
        if (_autoStartNote != "")
        {
            var note = Txt("已自动修复： " + _autoStartNote, 11, "#0F7B43");
            note.Margin = new Thickness(0, 6, 0, 0);
            sp2.Children.Add(note);
        }
        card2.Child = sp2;
        UiContent.Children.Add(card2);
    }

    // ---------------- 字号（Word 风格下拉） ----------------
    // Word 的字号档位（磅值），可选也可自己敲
    static readonly string[] FontSizePresets = {
        "8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "36"
    };

    static double CurFontPt
    {
        get
        {
            string s = CmbFontSize != null ? (CmbFontSize.Text ?? "").Trim() : "";
            s = s.Replace("pt", "").Replace("PT", "").Trim();
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = _bubbleFontPtSel;
            return Math.Max(4, Math.Min(72, v));
        }
    }

    static void SetupFontSize()
    {
        if (CmbFontSize == null) return;
        CmbFontSize.Items.Clear();
        foreach (string s in FontSizePresets) CmbFontSize.Items.Add(s);
        string cur = _bubbleFontPtSel.ToString("0.0", CultureInfo.InvariantCulture);
        if (cur.EndsWith(".0", StringComparison.Ordinal)) cur = cur.Substring(0, cur.Length - 2);
        CmbFontSize.Text = cur;
        CmbFontSize.SelectionChanged += (s, e) =>
        {
            if (CmbFontSize.SelectedIndex < 0) return;
            _bubbleFontPtSel = (float)CurFontPt;
            DrawBubblePreview();
        };
        CmbFontSize.LostFocus += (s, e) => { _bubbleFontPtSel = (float)CurFontPt; DrawBubblePreview(); };
        CmbFontSize.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { _bubbleFontPtSel = (float)CurFontPt; DrawBubblePreview(); }
        };
    }

    // 气泡关掉时把跟它有关的控件一起置灰（改了也看不到，别让人白调）
    static void SyncBubbleEnabled()
    {
        bool on = ChkBub != null && ChkBub.IsChecked == true;
        if (SldBub != null) SldBub.IsEnabled = on;
        if (CmbEvery != null) CmbEvery.IsEnabled = on;
        if (CmbFont != null) CmbFont.IsEnabled = on;
        if (CmbFontSize != null) CmbFontSize.IsEnabled = on;
        if (BtnBubbleColor != null) BtnBubbleColor.IsEnabled = on;
        if (BtnBubbleInk != null) BtnBubbleInk.IsEnabled = on;
        if (BtnUpBubble != null) BtnUpBubble.IsEnabled = on;
        if (BubbleColorHost != null) { BubbleColorHost.IsEnabled = on; BubbleColorHost.Opacity = on ? 1 : 0.45; }
        if (BubbleInkHost != null) { BubbleInkHost.IsEnabled = on; BubbleInkHost.Opacity = on ? 1 : 0.45; }
        if (BubbleStyleHost != null) { BubbleStyleHost.IsEnabled = on; BubbleStyleHost.Opacity = on ? 1 : 0.45; }
        if (LblBubHint != null) LblBubHint.Opacity = on ? 1 : 0.45;
        if (LblFontSize != null) LblFontSize.Opacity = on ? 1 : 0.45;
    }

    // ---------------- 未保存提醒 ----------------
    // 把界面上所有设置拍成一个字符串，跟"上次保存的样子"比一比就知道脏不脏
    static string Snapshot()
    {
        try
        {
            CharEntry ce = SelectedChar;
            var d = CollectOverrides(ce != null && !string.IsNullOrEmpty(ce.Dir) ? RelToHere(ce.Dir) : CurCharDir);
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in d)
            {
                // showPet 是「切换即生效」的开关（SetShowPet 立刻写盘），不算未保存的改动
                if (kv.Key == "showPet" || kv.Key == "hideFullscreen") continue;
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            // 角色高度、动画幅度存在角色包 char.json 里，不走 overrides —— 单独拼进快照，
            // 否则拖这两组滑杆底栏一直显示「已保存」，用户以为没生效。
            sb.Append("charH=")
              .Append(SldSize != null ? ((int)Math.Round(SldSize.Value)).ToString(CultureInfo.InvariantCulture) : "")
              .Append(';');
            foreach (string ak in AnimKeys)
            {
                AnimCard card = null;
                foreach (AnimCard c in AnimCards) if (c.Key == ak) { card = c; break; }
                sb.Append("amp").Append(ak).Append('=')
                  .Append(card != null
                        ? ((int)card.Amp.Value).ToString(CultureInfo.InvariantCulture)
                        : ((int)Math.Round(EntryAmp(ce, ak) * 100)).ToString(CultureInfo.InvariantCulture))
                  .Append(';');
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            // 取不到（比如某个下拉还没选）就当作"没变"，宁可漏报也别一直误报"未保存"
            return _savedSnapshot;
        }
    }

    static void StartDirtyWatch()
    {
        _savedSnapshot = Snapshot();
        if (LblDirty == null) return;
        _dirtyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _dirtyTimer.Tick += (s, e) =>
        {
            try
            {
                bool dirty = Snapshot() != _savedSnapshot;
                LblDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
                if (dirty && LblSaved != null && LblSaved.Visibility == Visibility.Visible)
                {
                    if (_savedNoteTimer != null) { _savedNoteTimer.Stop(); _savedNoteTimer = null; }
                    HideSavedNote(false);
                }
                SetPageBarState();
            }
            catch (Exception ex) { LogErr("StartDirtyWatch", ex); }
        };
        _dirtyTimer.Start();
    }

    static void MarkSaved() { _savedSnapshot = Snapshot(); }

    // ---------------- 动效 ----------------
    // 全程序一套节奏：位移/淡入 180~200ms、叠层淡色 140~160ms，一律 CubicEase.EaseOut，不回弹。
    // 只做「淡入」和「位移」两类 —— 颜色动画在这种动态资源刷子上容易踩冻结问题，一律用叠层 Opacity 代替。
    static DoubleAnimation Fade(double from, double to, int ms)
    {
        return new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
    }

    // 左栏读数的「数字交接」：旧值换新值时从「略靠下 + 微透明」落到位。
    // 关键是 300ms 节流：滑块拖动时 RefreshHome 每帧都来，动画每次重启会闪成频闪灯；
    // 有节流后连续变化直接落地，只有离散的一次性变化（切角色、选预设）才有交接动画。
    static DateTime _lastReadoutAnim = DateTime.MinValue;
    static void SetReadout(TextBlock tb, string val)
    {
        if (tb == null || tb.Text == val) return;
        tb.Text = val;
        if (_motionOff) return;
        if ((DateTime.Now - _lastReadoutAnim).TotalMilliseconds < 300) return;
        _lastReadoutAnim = DateTime.Now;
        TranslateTransform tt = tb.RenderTransform as TranslateTransform;
        if (tt == null) { tt = new TranslateTransform(0, 0); tb.RenderTransform = tt; }
        tb.BeginAnimation(UIElement.OpacityProperty, null);
        tt.BeginAnimation(TranslateTransform.YProperty, null);
        tb.Opacity = 0.35; tt.Y = 4;
        DoubleAnimation a = Fade(0.35, 1, 180);
        a.Completed += (s2, e2) => { tb.BeginAnimation(UIElement.OpacityProperty, null); tb.Opacity = 1; };
        tb.BeginAnimation(UIElement.OpacityProperty, a);
        DoubleAnimation b = Fade(4, 0, 200);
        b.Completed += (s2, e2) => { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = 0; };
        tt.BeginAnimation(TranslateTransform.YProperty, b);
    }

    // 切页：中栏（枢纽或内容页）从下方 9px 处淡入。宿主固定，标题栏、左栏、右栏磁贴不动，
    // 所以动效只在视口里发生，不会让整屏抖。
    static void AnimatePageIn()
    {
        if (_motionOff) return;
        FrameworkElement host = _curPage == "home" ? HubView : (FrameworkElement)ContentHost;
        if (host == null || host.Visibility != Visibility.Visible) return;
        TranslateTransform tt = host.RenderTransform as TranslateTransform;
        if (tt == null)
        {
            tt = new TranslateTransform(0, 0);
            host.RenderTransform = tt;
        }
        // 基准值必须先落到「起点」：动画时钟要到下一个渲染帧才启动，
        // 那之前读到的是基准值，基准留 1 就会闪一帧全不透明再从头淡入。
        // 动画结束时把基准改回终点，之后即使动画被清掉也不会跳回透明/偏移。
        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.Opacity = 0;
        tt.BeginAnimation(TranslateTransform.YProperty, null);
        tt.Y = 9;
        DoubleAnimation fadeIn = Fade(0, 1, 190);
        fadeIn.Completed += (s2, e2) =>
        {
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.Opacity = 1;
        };
        host.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        DoubleAnimation rise = Fade(9, 0, 210);
        rise.Completed += (s2, e2) =>
        {
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = 0;
        };
        tt.BeginAnimation(TranslateTransform.YProperty, rise);

        // 卡片错峰入场：宿主淡入负责「页面来了」，错峰负责「内容一层层落定」。
        // 只动 Opacity>0.99 的卡：上一轮没播完的不重置，避免连环重启。
        try
        {
            int ci = 0;
            StaggerCards(host, ref ci);
        }
        catch (Exception ex) { LogErr("AnimatePageIn", ex); }

        // 护栏：窗口最小化 / 被遮挡时动画时钟可能不推进，Completed 不触发就会永远半透明。
        // 400ms 后无条件落到终态（此时正常动画早就结束了）。
        DispatcherTimer guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        guard.Tick += (s2, e2) =>
        {
            guard.Stop();
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.Opacity = 1;
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = 0;
        };
        guard.Start();
    }

    // 卡片错峰：从可视树里收 Card，逐张延迟 45ms 淡入 + 上移 7px，最多 7 张（再往后不加延迟）。
    // 每张都带 400ms 护栏：动画时钟不推进时也能落到终态，不会留下一页半透明卡片。
    static void StaggerCards(DependencyObject root, ref int i)
    {
        if (root == null || i >= 7) return;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int k = 0; k < n && i < 7; k++)
        {
            DependencyObject c = VisualTreeHelper.GetChild(root, k);
            if (c is Border bd && bd.Style == Win.Resources["Card"] && bd.Opacity > 0.99)
            {
                int delay = 45 * i;
                TranslateTransform tt = new TranslateTransform(0, 0);
                bd.RenderTransform = tt;
                bd.Opacity = 0; tt.Y = 7;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                t.Tick += (s2, e2) =>
                {
                    t.Stop();
                    DoubleAnimation a = Fade(0, 1, 170);
                    a.Completed += (s3, e3) => { bd.BeginAnimation(UIElement.OpacityProperty, null); bd.Opacity = 1; };
                    bd.BeginAnimation(UIElement.OpacityProperty, a);
                    DoubleAnimation r = Fade(7, 0, 190);
                    r.Completed += (s3, e3) => { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = 0; };
                    tt.BeginAnimation(TranslateTransform.YProperty, r);
                    var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    guard.Tick += (s3, e3) =>
                    {
                        guard.Stop();
                        bd.BeginAnimation(UIElement.OpacityProperty, null); bd.Opacity = 1;
                        tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = 0;
                    };
                    guard.Start();
                };
                t.Start();
                i++;
            }
            StaggerCards(c, ref i);
        }
    }

    // 底栏那条「已保存并应用 │ 角色 │ 219px │ 音量 20%」。不打断操作：6 秒后自己淡出。
    // 期间再做改动会立刻换回 UNSAVED（见 StartDirtyWatch）。
    // 收起状态条：先淡出 150ms 再隐藏；新改动进来时走同样的淡出（不留两条状态并存）
    static void HideSavedNote(bool animate)
    {
        if (LblSaved == null) return;
        LblSaved.BeginAnimation(UIElement.OpacityProperty, null);
        if (animate && !_motionOff && LblSaved.Visibility == Visibility.Visible)
        {
            double fromOp = LblSaved.Opacity;
            DoubleAnimation da = Fade(fromOp, 0, 150);
            da.Completed += (s2, e2) => { if (LblSaved != null) LblSaved.Visibility = Visibility.Collapsed; };
            LblSaved.BeginAnimation(UIElement.OpacityProperty, da);
        }
        else
        {
            LblSaved.Opacity = 1;                      // 恢复基准值，下次出现还是全不透明
            LblSaved.Visibility = Visibility.Collapsed;
        }
    }

    static void ShowSavedNote(string text)
    {
        if (LblSaved == null) return;
        if (_savedNoteTimer != null) { _savedNoteTimer.Stop(); _savedNoteTimer = null; }
        LblSaved.Text = text;
        LblSaved.Visibility = Visibility.Visible;
        if (LblDirty != null) LblDirty.Visibility = Visibility.Collapsed;
        // 淡入 + 从下方 5px 上浮：比硬切更像「刚刚发生了一件事」，但只用 180ms，不打扰
        if (!_motionOff)
        {
            TranslateTransform tt = LblSaved.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(0, 0); LblSaved.RenderTransform = tt; }
            LblSaved.BeginAnimation(UIElement.OpacityProperty, null);
            LblSaved.Opacity = 0;                       // 同 AnimatePageIn：基准先落起点，避免闪一帧
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = 5;
            DoubleAnimation noteIn = Fade(0, 1, 180);
            noteIn.Completed += (s2, e2) =>
            {
                if (LblSaved == null) return;
                LblSaved.BeginAnimation(UIElement.OpacityProperty, null);
                LblSaved.Opacity = 1;
            };
            LblSaved.BeginAnimation(UIElement.OpacityProperty, noteIn);
            DoubleAnimation noteRise = Fade(5, 0, 190);
            noteRise.Completed += (s2, e2) =>
            {
                if (tt == null) return;
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.Y = 0;
            };
            tt.BeginAnimation(TranslateTransform.YProperty, noteRise);
            // 同 AnimatePageIn：状态条也不能卡在半透明
            DispatcherTimer ng = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            ng.Tick += (s2, e2) =>
            {
                ng.Stop();
                if (LblSaved == null || LblSaved.Visibility != Visibility.Visible) return;
                LblSaved.BeginAnimation(UIElement.OpacityProperty, null);
                LblSaved.Opacity = 1;
                if (tt != null) { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = 0; }
            };
            ng.Start();
        }

        _savedNoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _savedNoteTimer.Tick += (s, e) =>
        {
            if (_savedNoteTimer != null) { _savedNoteTimer.Stop(); _savedNoteTimer = null; }
            HideSavedNote(true);
        };
        _savedNoteTimer.Start();
        SetPageBarState();
    }

    static void DrawBubblePreview()
    {
        try
        {
            int bh = (ChkBub != null && ChkBub.IsChecked == true) ? (int)SldBub.Value : 0;
            BitmapSource pet = PetFrameShot(6);
            int pw = pet != null ? pet.PixelWidth : 0, ph = pet != null ? pet.PixelHeight : 0;
            int bw = 234;                                   // 桌宠气泡区的实际宽度（按角色宽度）
            int w = Math.Max(bw, pw);
            int h = (bh > 0 ? bh : 0) + (ph > 0 ? 10 + ph : 0);
            if (h < 60) h = 60;

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(C("#FCFDFE"), null, new Rect(0, 0, w, h));
                if (bh > 0)
                {
                    Rect tr; Brush inkB;
                    dc.PushTransform(new TranslateTransform((w - bw) / 2.0, 0));
                    DrawBubbleWpf(dc, _bubbleStyleSel, _bubbleFrameSel, bw, bh, out tr, out inkB);
                    // 文字颜色：用户指定了就用指定的，否则跟着外框自动
                    if (!string.IsNullOrEmpty(_bubbleInkSel) && _bubbleInkSel != "auto")
                        inkB = new SolidColorBrush(WpfParseHex(_bubbleInkSel, Colors.Black));
                    string fn = CmbFont.SelectedIndex >= 0 ? CmbFont.Items[CmbFont.SelectedIndex].ToString() : CurFont;
                    Typeface tf = new Typeface(new FontFamily(fn), FontStyles.Normal,
                        FontWeights.Normal, FontStretches.Normal);
                    double pt = CurFontPt;
                    FormattedText ft = new FormattedText("你好呀", CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, tf, pt, inkB, 1.0);
                    ft.MaxTextWidth = Math.Max(30, tr.Width);
                    ft.MaxTextHeight = Math.Max(12, tr.Height);
                    ft.TextAlignment = TextAlignment.Center;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(ft, new Point(tr.Left, tr.Top + Math.Max(0, (tr.Height - ft.Height) / 2.0)));
                    dc.Pop();
                }
                if (pet != null)
                {
                    dc.DrawImage(pet, new Rect((w - pw) / 2.0, (bh > 0 ? bh + 10 : 0), pw, ph));
                    // 角色脚底画一条基准线，标明"气泡是从她头顶上长出来的"
                    Pen line = new Pen(C("#C9D2E0"), 1) { DashStyle = DashStyles.Dash };
                    double baseY = (bh > 0 ? bh + 10 : 0) + ph - 1;
                    dc.DrawLine(line, new Point(4, baseY), new Point(w - 4, baseY));
                }
            }
            var rtb = new RenderTargetBitmap(Math.Max(2, w), Math.Max(2, h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            if (ImgBub != null) ImgBub.Source = rtb;
            if (LblBubSize != null)
                LblBubSize.Text = ph > 0
                    ? "气泡 " + (bh > 0 ? bh + " px" : "已关闭") + " ＋ 角色 " + ph + " px（上下各留 10 px 间距）"
                    : "气泡 " + (bh > 0 ? bh + " px" : "已关闭");
        }
        catch (Exception ex) { LogErr("bubprev", ex); }
    }

    // ---------------- 语音库 / 试听 ----------------
    static readonly Random _rndUI = new Random();

    static Border _voiceRowPlaying;







    // 把界面上的全部设置收成 key=value（写入 pet-overrides.txt，同时用来补丁 pet-config.txt）
    static Dictionary<string, string> CollectOverrides(string charDir)
    {
        var d = new Dictionary<string, string>();
        d["charDir"] = charDir;
        d["showPet"] = _showPetSel ? "1" : "0";
        d["hideFullscreen"] = _hideFsSel ? "1" : "0";
        d["soundSet"] = "furina";
        d["clickVoice"] = ChkClick.IsChecked == true ? "1" : "0";
        d["bubbleH"] = (ChkBub.IsChecked == true ? (int)SldBub.Value : 0).ToString(CultureInfo.InvariantCulture);
        d["bubbleEveryMs"] = EveryVals[CmbEvery.SelectedIndex].ToString(CultureInfo.InvariantCulture);
        d["bubbleFont"] = CmbFont.Items[CmbFont.SelectedIndex].ToString();
        d["volume"] = ((int)SldVol.Value).ToString(CultureInfo.InvariantCulture);
        d["sfxGroup"] = (CmbSet != null && CmbSet.SelectedIndex > 0)
            ? CmbSet.Items[CmbSet.SelectedIndex].ToString() : "auto";
        d["clickLineMode"] = (CmbClickMode != null && CmbClickMode.SelectedIndex == 1) ? "seq" : "random";
        d["bubbleColor"] = HexOf(_bubbleColorSel);
        d["bubbleInk"] = string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : _bubbleInkSel;
        d["themeColor"] = HexOf(_themeColor);
        d["bubbleFontSize"] = CurFontPt.ToString("0.0", CultureInfo.InvariantCulture);
        d["bubbleStyle"] = string.IsNullOrEmpty(_bubbleStyleSel) ? "card" : _bubbleStyleSel;
        d["bubbleFrame"] = (_bubbleStyleSel == "custom") ? RelBubbleFrame(_bubbleFrameSel) : "";
        foreach (AnimCard c in AnimCards)
        {
            d["anim" + Cap(c.Key)] = (c.Toggle != null && c.Toggle.IsChecked == true) ? "1" : "0";
            d["speed" + Cap(c.Key)] = ((int)c.Speed.Value).ToString(CultureInfo.InvariantCulture);
        }
        return d;
    }

    static void WriteOverrides(Dictionary<string, string> d)
    {
        WriteOverridesTo(Path.Combine(Here, "pet-overrides.txt"), d);
    }

    static void WriteOverridesTo(string path, Dictionary<string, string> d)
    {
        var lines = new List<string>();
        foreach (KeyValuePair<string, string> kv in d) lines.Add(kv.Key + "=" + kv.Value);
        File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        // pet-overrides.txt 是「当前配置」的唯一真源，Cur 只是它的内存副本。
        // 以前这里只写盘不更新副本 ⇒ 保存后 CurCharDir 还是旧角色，导入页的「使用中」
        // 要等整个设置窗口重启才肯挪位（用户实测反馈）。整份替换，和刚写下的内容逐字一致。
        Cur.Clear();
        foreach (KeyValuePair<string, string> kv in d) Cur[kv.Key] = kv.Value;
    }

    // ---------------- 角色包读写 ----------------
    static Dictionary<string, string> ReadManifest(string dir)
    {
        var m = new Dictionary<string, string>();
        string p = Path.Combine(dir, "char.json");
        if (!File.Exists(p)) return m;
        foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line == "" || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            m[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return m;
    }

    static void WriteManifest(string dir, Dictionary<string, string> m)
    {
        string[] order = { "name", "image", "frames", "meta", "lines", "voicelines", "press", "release", "charH", "voices", "created" };
        var lines = new List<string>();
        foreach (string k in order) if (m.ContainsKey(k)) lines.Add(k + "=" + m[k]);
        foreach (KeyValuePair<string, string> kv in m) if (Array.IndexOf(order, kv.Key) < 0) lines.Add(kv.Key + "=" + kv.Value);
        File.WriteAllLines(Path.Combine(dir, "char.json"), lines.ToArray(), new UTF8Encoding(false));
    }

    static string ResolvePack(string dir, Dictionary<string, string> m, string key)
    {
        string v;
        if (!m.TryGetValue(key, out v) || v == "") return "";
        return Path.IsPathRooted(v) ? v : Path.Combine(dir, v);
    }

    static double ReadAmp(Dictionary<string, string> m, string key)
    {
        string s;
        double v;
        if (m.TryGetValue(key, out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return Math.Max(0.2, Math.Min(2.0, v));
        return 1.0;
    }

    static string RelToHere(string p)
    {
        string h = Here.TrimEnd('\\') + "\\";
        return p.StartsWith(h, StringComparison.OrdinalIgnoreCase) ? p.Substring(h.Length) : p;
    }

    static string FindPackImage(string dir)
    {
        foreach (string sub in new string[] { "图片", "" })
            foreach (string ext in new string[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
            {
                string p = Path.Combine(dir, sub, "image" + ext);
                if (File.Exists(p)) return p;
            }
        return "";
    }

    // 动画生成失败的真实原因（设置面板直接显示给用户，不再只说「请确认 Python 可用」）
    static string _framesErr = "";

    // 只重出这个角色自己的动画（写到 chars\<角色>\动画\），不再动全局帧
    static bool BuildPackFrames(CharEntry e, int size)
    {
        try
        {
            string animDir = Path.Combine(e.Dir, "动画");
            Directory.CreateDirectory(animDir);
            // 进程内生成（SpriteGen.cs）：以前是 Process.Start("python sprites.py")，
            // 用户没装 Python + Pillow 就必然失败 —— 普通用户装不了，功能等于不存在。
            _framesErr = "";
            QSprite.Pack pack = QSprite.Generate(e.Image, size, e.AmpIdle, e.AmpPress, e.AmpClick, e.AmpDrag);
            QSprite.WritePack(animDir, pack, true);
            string frames = Path.Combine(animDir, "frames.bin"), meta = Path.Combine(animDir, "frames.txt");
            // 落盘后再核对一次：写失败时旧文件还在，只看存在会误判成功
            if (!File.Exists(frames) || !File.Exists(meta)) return false;
            if (new FileInfo(frames).Length < (long)pack.W * pack.H * 4 * pack.Total) return false;
            e.Frames = frames; e.Meta = meta; e.CharH = size;
            _framesErr = "";

            Dictionary<string, string> m = ReadManifest(e.Dir);
            m["name"] = e.Name;
            m["image"] = "图片/" + Path.GetFileName(e.Image);
            m["frames"] = "动画/frames.bin";
            m["meta"] = "动画/frames.txt";
            if (!m.ContainsKey("lines")) m["lines"] = "台词.txt";
            if (!m.ContainsKey("voicelines")) m["voicelines"] = "音效/voices.tsv";
            m["charH"] = size.ToString(CultureInfo.InvariantCulture);
            m["ampIdle"] = e.AmpIdle.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampPress"] = e.AmpPress.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampClick"] = e.AmpClick.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampDrag"] = e.AmpDrag.ToString("0.00", CultureInfo.InvariantCulture);
            WriteManifest(e.Dir, m);
            return true;
        }
        catch (Exception ex) { _framesErr = ex.Message; LogErr("frames", ex); return false; }
    }

    static void Save()
    {
        try
        {
            BtnSave.IsEnabled = false; BtnSave.Content = "应用中…";
            CharEntry ce = SelectedChar;
            if (ce == null || string.IsNullOrEmpty(ce.Dir))
            {
                BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
                Dlg.Info("没有可选角色", "chars 目录里还没有角色包。\n先去「导入」页新建一个角色吧。", "好");
                return;
            }
            int size = (int)SldSize.Value;

            // 幅度滑块 → 角色包里的动画参数（只有真的改了才重新生成）
            // 只有动画页上展示的确实是当前角色时，才把幅度滑块同步进角色包
            bool ampChanged = false;
            if (_animCardsDir == ce.Dir)
            {
                foreach (AnimCard c in AnimCards)
                {
                    double v = Math.Max(0.2, Math.Min(2.0, c.Amp.Value / 100.0));
                    if (Math.Abs(v - EntryAmp(ce, c.Key)) > 0.001) { SetEntryAmp(ce, c.Key, v); ampChanged = true; }
                }
            }

            // 大小 / 幅度变了（或动画缺失）→ 只重出**这个角色**的动画
            if (size != ce.CharH || ampChanged || !ce.HasFrames)
            {
                if (!BuildPackFrames(ce, size))
                {
                    BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
                    Dlg.Info("动画生成失败",
                        "没能生成「" + ce.Name + "」的动画帧。\n" + (string.IsNullOrEmpty(_framesErr) ? "图片可能已经损坏，换一张 png/jpg 再试。" : _framesErr), "好");
                    return;
                }
            }

            Dictionary<string, string> ov = CollectOverrides(RelToHere(ce.Dir));
            WriteOverrides(ov);
            PatchConfig(ov);
            // 「当前角色」换了：导入页的方框卡和首页左栏都是照 Cur 画的，保存后立刻重画，
            // 不用等用户切页（切页时才刷新的旧逻辑在这里会让人以为没生效）。
            RefreshImport();
            RefreshHome();
            MarkSaved();                       // 保存后清掉「未保存」提示，关窗不再拦
            RestartPet();

            // 保存后不关窗：留在这里可以接着调，改动在桌宠上已生效。
            // 反馈改成底栏一条状态，6 秒后自己收；比每次弹模态框少一次点击。
            BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
            ShowSavedNote("已保存并应用  │  " + ce.Name + "  │  " + size + "px  │  音量 "
                + ov["volume"] + "%" + (_showPetSel ? "" : "  │  桌宠当前隐藏"));
        }
        catch (Exception ex)
        {
            BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
            LogErr("save", ex);
            Dlg.Info("保存失败", ex.Message, "好");
        }
    }

    // 把设置补丁进 pet-config.txt：已有的键就地改，缺的键追加（老配置文件也能平滑升级）
    static void PatchConfig(Dictionary<string, string> kv)
    {
        string p = Path.Combine(Here, "pet-config.txt");
        var outLines = File.Exists(p) ? new List<string>(File.ReadAllLines(p, Encoding.UTF8)) : new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < outLines.Count; i++)
        {
            string line = outLines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v;
            if (kv.TryGetValue(k, out v) && !seen.Contains(k)) { outLines[i] = k + "=" + v; seen.Add(k); }
        }
        foreach (KeyValuePair<string, string> e in kv)
            if (!seen.Contains(e.Key)) outLines.Add(e.Key + "=" + e.Value);
        File.WriteAllLines(p, outLines.ToArray(), new UTF8Encoding(false));
    }

    // 只把指定的几个键补进 pet-overrides.txt（不动其它行；没有就追加）。
    // 给「切换即生效」的开关用：不能走 CollectOverrides→WriteOverrides，
    // 否则会把界面上其它还没保存的改动一起写盘。
    static void PatchOverrides(Dictionary<string, string> kv)
    {
        string p = Path.Combine(Here, "pet-overrides.txt");
        var outLines = File.Exists(p) ? new List<string>(File.ReadAllLines(p, Encoding.UTF8)) : new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < outLines.Count; i++)
        {
            string line = outLines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v;
            if (kv.TryGetValue(k, out v) && !seen.Contains(k)) { outLines[i] = k + "=" + v; seen.Add(k); }
        }
        foreach (KeyValuePair<string, string> e in kv)
            if (!seen.Contains(e.Key)) outLines.Add(e.Key + "=" + e.Value);
        File.WriteAllLines(p, outLines.ToArray(), new UTF8Encoding(false));
        // 同一份「当前配置」的内存副本也要跟着走，否则读 Cur 的地方（IsCurrent 等）还是旧值
        foreach (KeyValuePair<string, string> e in kv) Cur[e.Key] = e.Value;
    }

    // 桌面显示总开关：写盘 + 立即启停桌宠（不等「保存更改」，因为这个开关不涉及角色/动画）
    static void SetShowPet(bool on)
    {
        _showPetSel = on;
        try
        {
            var one = new Dictionary<string, string>();
            one["showPet"] = on ? "1" : "0";
            PatchOverrides(one);
            PatchConfig(one);
            if (on) RestartPet(); else StopPet();
            if (LblDirty != null) LblDirty.Visibility = Snapshot() != _savedSnapshot ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { LogErr("showpet", ex); }
    }

    // 全屏隐藏：同样「切换即生效」。Pet.exe 只在启动时读配置，所以写盘后重启一次进程，
    // 否则用户切了开关要等下次开机才生效。
    static void SetHideFs(bool on)
    {
        _hideFsSel = on;
        try
        {
            var one = new Dictionary<string, string>();
            one["hideFullscreen"] = on ? "1" : "0";
            PatchOverrides(one);
            PatchConfig(one);
            RestartPet();
            if (LblDirty != null) LblDirty.Visibility = Snapshot() != _savedSnapshot ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { LogErr("hidefs", ex); }
    }

    // 只停不启（关闭桌宠 / 重启前的第一步）
    static void StopPet()
    {
        try
        {
            string pidf = Path.Combine(Here, "pet.pid");
            if (File.Exists(pidf))
            {
                int pid;
                if (int.TryParse(File.ReadAllText(pidf).Trim(), out pid))
                {
                    try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
                }
            }
            foreach (var pr in Process.GetProcessesByName("Pet"))
            {
                try { pr.Kill(); } catch (Exception) { }
            }
        }
        catch (Exception ex) { LogErr("stoppet", ex); }
    }

    static void RestartPet()
    {
        try
        {
            StopPet();
            System.Threading.Thread.Sleep(600);
            if (!_showPetSel) return;          // 桌宠已被关掉：只停不启，否则又冒出来了
            string exe = Path.Combine(Here, PetExeName);
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Here });
        }
        catch (Exception ex) { LogErr("restart", ex); }
    }

    static void QuitPet()
    {
        if (!Dlg.Ask("让她先休息一下？",
            "退出后她会从桌面消失，按键统计和语音也会一起停下。\n下次双击 Pet.exe，她就又回来了。",
            "退出桌宠", "再陪一会儿", true)) return;
        StopPet();
        Win.Close();
    }

    // ---------------- 导入页（角色包：图像 + 语音 + 名字） ----------------
    static StackPanel PageImport, ImportList;
    static TextBox TxtName;
    static TextBlock LblImg, LblVoices, LblImportMsg;
    static Button BtnPickImg, BtnPickVoices, BtnImport;
    static string _importImg = "", _importVoiceDir = "";










    // 一张角色卡：上半是立绘（浅底盘，底边对齐 ⇒ 不同身高也像站在同一层架子上），
    // 下半是名字 / 219 px │ 语音 12 / 删除。当前在用的那张：主题色描边 + 顶部 3px 主题条 + 「使用中」角标，
    // 和下拉框选中项是同一套语言（描边 + 浅底 + 主题色字），不靠改线宽来表达选中（否则会抖 1px）。
    const double CharCardW = 168, CharCardArtH = 128;
    static string _importMarked = "";        // 导入页上绿框标在哪个角色上（自检探针）
    static readonly Brush CharCardCurFill =
        new SolidColorBrush(Mix(Color.FromRgb(0xFF, 0xFF, 0xFF), CharCardCurGreen, 0.06));





    internal static Brush C(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.Transparent;
        string mapped;
        if (TokenMap.TryGetValue(hex, out mapped)) hex = mapped;
        try { return (Brush)new BrushConverter().ConvertFromString(hex); } catch (Exception) { return Brushes.Black; }
    }
    // 取主题资源画笔（Accent / AccentInk / AccentSoft …）。换肤时这些键会被换成新实例，
    // 所以这里是「当场取」而不是缓存；长期持有的控件需要在 ApplyThemeColor 里重建或重涂。
    internal static Brush R(string key)
    {
        try
        {
            Brush b = Win != null ? Win.Resources[key] as Brush : null;
            return b ?? Brushes.Black;
        }
        catch (Exception ex) { LogErr("R", ex); return Brushes.Black; }
    }






    static TextBlock LblStatDay, LblStatScreen, LblStatActive, LblStatIdle, LblStatKeys, LblStatUpdated;
    static ComboBox _dateCombo;                  // 日期下拉：标准 ComboBox，和「主动冒泡」同一套样式
    static bool _dateFilling;                    // 重建列表时置位，此时 SelectedIndex 变化不算用户选择
    static TextBlock _dateNote;
    static TextBlock _liveTxt;
    static Ellipse _liveDot;
    static string _statViewDate = "";   // "" = 今天（实时刷新）
    static DispatcherTimer _liveTimer;











    // ---------------- 关于 ----------------
    // 版本号来自两个 exe 的文件属性（csproj 里的 FileVersion / InformationalVersion）。
    // 这里刻意同时显示 Pet.exe 与 PetSettings.exe：只要两者不一致，就说明有一个没跟着更新。
    const string RepoUrl = "https://github.com/xiezaji/q-pet";
    const string WebUrl = "https://xiezaji.github.io/q-pet/";











    // 角色枢纽「位置」读数：桌宠拖动时会节流写 pet-pos.txt（150ms 一次），
    // 这里只轮询那个小文件（15 字节），不碰 RefreshHome —— 整页重算会打断立绘缩放动画。
    static DispatcherTimer _livePosTimer;
    static string _livePosCache = "";




    // 无界面截图（--shot 路径）：把指定页渲染成 PNG，用来确认布局没被挤掉
    static int _shotScroll = 0;
    internal static int _shotZoom = 1;                          // 放大倍率（见 Shot）
    internal static int _cropX, _cropY, _cropW, _cropH;         // 裁剪区（见 Shot）
    // 开发自验：把确认框的卡片单独渲染成 PNG（弹窗是独立 HWND，整窗截图截不到）
    internal static string _dlgShotPath;


    static void Shot(string path, int w, int h)
    {
        try
        {
            Win.WindowStartupLocation = WindowStartupLocation.Manual;
            Win.Left = -4000; Win.Top = -4000;
            Win.Width = w; Win.Height = h;
            Win.ShowInTaskbar = false;
            Win.Show();
            Win.UpdateLayout();
            // 把 Loaded / Render 优先级的排队任务都跑完，再抓
            Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
            Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
            if (_shotScroll > 0 && Scroller != null) Scroller.ScrollToVerticalOffset(_shotScroll);
            Win.UpdateLayout();
            if (_shotPumpMs > 0) PumpFor(_shotPumpMs);   // 让 XAML 触发器动画跑完（见 PumpFor 注释）
            var root = (FrameworkElement)Win.Content;
            int rw = (int)Math.Round(root.ActualWidth), rh = (int)Math.Round(root.ActualHeight);
            if (rw <= 0 || rh <= 0) { rw = w; rh = h; }
            // --shot-zoom N：按 N 倍 DPI 渲染（RenderTargetBitmap 的 dpi 直接决定渲染倍率，
            // 是矢量重绘不是位图放大）。--shot-crop x,y,w,h 再从放大后的图里裁一块，
            // 用来验 8px 切角、1px 描边这种肉眼在整窗图里看不出来的细节。
            int zoom = _shotZoom < 1 ? 1 : _shotZoom;
            var rtb = new RenderTargetBitmap(rw * zoom, rh * zoom, 96.0 * zoom, 96.0 * zoom, PixelFormats.Pbgra32);
            rtb.Render(root);
            BitmapSource frame = rtb;
            if (_cropW > 0 && _cropH > 0)
            {
                int cx = Math.Max(0, Math.Min(rw * zoom - 1, _cropX));
                int cy = Math.Max(0, Math.Min(rh * zoom - 1, _cropY));
                int cw = Math.Min(_cropW, rw * zoom - cx), ch = Math.Min(_cropH, rh * zoom - cy);
                if (cw > 0 && ch > 0) frame = new CroppedBitmap(rtb, new Int32Rect(cx, cy, cw, ch));
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(frame));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("shot " + path + " " + frame.PixelWidth + "x" + frame.PixelHeight
                              + (zoom > 1 ? " @" + zoom + "x" : ""));
        }
        catch (Exception ex) { LogErr("shot", ex); }
        finally { try { Win.Close(); } catch (Exception) { } }
    }


    // ---------------- 自检 ----------------
    static void SelfTest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("自检通过（独立 exe │ WPF │ Win11 Fluent）：");
        sb.AppendLine("  窗口 " + Win.Width + "x" + Win.Height + "，最小 " + Win.MinWidth + "x" + Win.MinHeight + "，可缩放=" + Win.ResizeMode);
        sb.AppendLine("  导航项 " + Nav.Items.Count + " 个（含分组标题）");
        var rows = new List<string>();
        for (int i = 0; i < Nav.Items.Count; i++)
        {
            ListBoxItem it = Nav.Items[i] as ListBoxItem;
            string key = it != null ? it.Tag as string : null;
            if (string.IsNullOrEmpty(key)) continue;
            ShowPage(key);
            rows.Add(key + "["
                + (PageChar.Visibility == Visibility.Visible ? "形象" : "")
                + (PageVoice.Visibility == Visibility.Visible ? "语音" : "")
                + (PageBubble.Visibility == Visibility.Visible ? "气泡" : "")
                + (PageUi.Visibility == Visibility.Visible ? "通用" : "")
                + (PageAnim.Visibility == Visibility.Visible ? "动画" : "")
                + (PageStats.Visibility == Visibility.Visible ? "统计" : "")
                + (PageAbout.Visibility == Visibility.Visible ? "关于" : "")
                + (PageImport.Visibility == Visibility.Visible ? "导入" : "") + "]");
        }
        sb.AppendLine("  导航切换: " + string.Join(" ", rows.ToArray()));

        // ---- 首页自检：磁贴齐不齐、进首页时导航/内容区/返回链接是不是都收起来了 ----
        try
        {
            ShowPage("home");
            int tiles = 0;
            foreach (string tn in new string[] { "tileBubble", "tileUi", "tileChar", "tileAnim",
                                                 "tileVoice", "tileImport", "tileStats", "tileAbout" })
            {
                if (Win.FindName(tn) as ButtonBase != null) tiles++;
            }
            sb.AppendLine("  首页: 磁贴 " + tiles + "/8，首页可见=" + (PageHome.Visibility == Visibility.Visible)
                + "，枢纽可见=" + (HubView != null && HubView.Visibility == Visibility.Visible)
                + "，内容区收起=" + (ContentHost.Visibility == Visibility.Collapsed)
                + "，导航收起=" + (NavHost.Visibility == Visibility.Collapsed)
                + "，返回链接收起=" + (BtnHome.Visibility == Visibility.Collapsed));

            // 接线实测：真抛一次 Click，看页面有没有切过去（只查得到控件不算过）
            ToggleButton probe = Win.FindName("tileAbout") as ToggleButton;
            if (probe != null) probe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sb.AppendLine("  磁贴接线: 点「08 关于」→ " + (PageAbout.Visibility == Visibility.Visible ? "已切到关于页 ✓" : "没反应 ✗"));
            ShowPage("char");
            sb.AppendLine("  内容页: 首页仍可见=" + (PageHome.Visibility == Visibility.Visible)
                + "，枢纽收起=" + (HubView != null && HubView.Visibility == Visibility.Collapsed)
                + "，内容区出现=" + (ContentHost.Visibility == Visibility.Visible)
                + "，返回链接出现=" + (BtnHome.Visibility == Visibility.Visible)
                + "，磁贴高亮=" + TileHighlighted("char"));

            // 立绘板铺满中栏 = 视口尺寸交给布局，代码不再写死宽高（写死就会退回「小方块」）
            ShowPage("home");
            FitHubPlate();
            bool plateFills = HubViewport != null
                              && double.IsNaN(HubViewport.Width) && double.IsNaN(HubViewport.Height)
                              && HubPlate != null && double.IsNaN(HubPlate.Width);
            sb.AppendLine("  枢纽底板: 铺满中栏=" + (plateFills ? "是 ✓" : "否 ✗")
                + " │ 视口 " + Math.Round(_hubW) + " × " + Math.Round(_hubH) + "px"
                + " │ 中栏 " + Math.Round(HubView != null ? HubView.ActualWidth : 0) + "px"
                + (_hubW > 0 && HubView != null && _hubW <= HubView.ActualWidth + 0.5 ? " → 不溢出 ✓" : " → 未布局，跳过"));

            // 枢纽缩放：整块缩放走 ScaleTransform，位移超范围要被夹住，读数跟着走
            _hubZoom = 1.0; _hubPanX = 0; _hubPanY = 0; ApplyHubZoom();
            double z0 = HubScale != null ? HubScale.ScaleX : -1;
            string t0 = TxtHubZoom != null ? TxtHubZoom.Text : "无";
            _hubZoom = 1.5; _hubPanX = 9999; _hubPanY = -9999; ApplyHubZoom();
            double z1 = HubScale != null ? HubScale.ScaleX : -1;
            double p1x = HubShift != null ? HubShift.X : 0;
            double p1y = HubShift != null ? HubShift.Y : 0;
            double limX = _hubW * 0.25, limY = _hubH * 0.25;   // (1.5-1)/2 * 宽高
            string t1 = TxtHubZoom != null ? TxtHubZoom.Text : "无";
            _hubZoom = 1.0; _hubPanX = 0; _hubPanY = 0; ApplyHubZoom();
            double z2 = HubScale != null ? HubScale.ScaleX : -1;
            bool zoomOk = Math.Abs(z0 - 1) < 0.001 && Math.Abs(z1 - 1.5) < 0.001 && Math.Abs(z2 - 1) < 0.001
                          && Math.Abs(p1x - limX) < 1.0 && Math.Abs(p1y + limY) < 1.0
                          && t0 == "×1.0" && t1 == "×1.5"
                          && HubViewport != null && HubViewport.ClipToBounds && HubShift != null;
            sb.AppendLine("  枢纽缩放: 整块 " + z0 + " → " + z1 + " → " + z2
                + " │ 位移夹取 " + Math.Round(p1x) + "/" + Math.Round(limX)
                + " · " + Math.Round(p1y) + "/" + Math.Round(limY) + "px"
                + " │ 读数 " + t1
                + " │ 剪裁 " + (HubViewport != null && HubViewport.ClipToBounds ? "开 ✓" : "关 ✗")
                + " │ " + (zoomOk ? "正常 ✓" : "异常 ✗"));

            // 密度相关的三件：仪表带、斜纹笔刷、台词库按条数收放
            bool barOk = PbNum != null && PbName != null && PbChar != null
                         && PbSize != null && PbFrames != null && PbState != null;
            // 斜纹已按用户要求全部清除：三块笔刷都不该再存在，中栏就是纯纸底
            bool hatchOk = !Win.Resources.Contains("HatchTile") && !Win.Resources.Contains("WorkHatch")
                        && !Win.Resources.Contains("HatchTileDark");
            // 左栏五条读数 + 六格刻度 + 调色板（矮窗口允许收起调色板，不算失败）
            int gaugeOk = (GsChar != null ? 1 : 0) + (GsSize != null ? 1 : 0)
                        + (GsBub != null ? 1 : 0) + (GsVol != null ? 1 : 0);
            bool sumOk = TxtHomeChar != null && TxtHomeSize != null && TxtHomeBubble != null
                         && TxtHomeVol != null && TxtHomeTheme != null && gaugeOk == 4;
            // 换肤后刻度必须全部跟着走：这里踩过「边遍历字典边改字典」的坑，
            // 表现是只有第一条刻度换色、其余停在旧色，肉眼很容易漏
            bool gaugeFollow = false;
            Color themeBak = _themeColor;
            try
            {
                if (GsSize != null && GsSize.Children.Count > 0)
                {
                    ApplyThemeColor(Color.FromRgb(0xE0, 0x4F, 0x5F));
                    Border b0 = GsSize.Children[0] as Border;
                    SolidColorBrush s0 = b0 == null ? null : b0.Background as SolidColorBrush;
                    gaugeFollow = s0 != null && s0.Color.R > 0xB0 && s0.Color.G < 0x70;
                }
            }
            finally { ApplyThemeColor(themeBak); }
            sb.AppendLine("  左栏: 五条读数 " + (sumOk ? "齐 ✓" : "缺 ✗") + " │ 六格刻度 "
                + gaugeOk + "/4 │ 换色跟随 " + (gaugeFollow ? "✓" : "✗")
                + " │ 调色板图例 " + (PaletteBlock == null ? "已移除 ✓" : "仍在 ✗"));
            sb.AppendLine("  密度: 仪表带 " + (barOk ? "6/6 ✓" : "缺失 ✗")
                + " │ 斜纹 " + (hatchOk ? "已清除 ✓" : "仍有残留 ✗")
                + " │ 台词库框 " + (VoiceScroller != null ? (int)VoiceScroller.Height + "px" : "—"));
        }
        catch (Exception ex) { sb.AppendLine("  首页自检失败: " + ex.Message); }

        // ---- 日期下拉自检：必须是「和主动冒泡同一套」的标准 ComboBox，且能展开、有选中 ----
        try
        {
            ShowPage("stats");
            // 自检里窗口可能还没真正显示，必须把布局优先级全部排空，
            // 否则 _dateCombo.ActualWidth 还是 0（BuildStatsPage 刚创建的控件）
            Win.Show();
            Win.UpdateLayout();
            for (int i = 0; i < 3; i++)
            {
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Win.UpdateLayout();
            }
            if (_dateCombo != null)
            {
                FillDateCombo();
                Win.UpdateLayout();
                int total = _dateCombo.Items.Count, off = 0;
                foreach (ComboBoxItem ci in _dateCombo.Items) if (!ci.IsEnabled) off++;
                _dateCombo.IsDropDownOpen = true;
                Win.UpdateLayout();
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Win.UpdateLayout();
                // 高 34 = 全局 ComboBox 样式的 Height（本控件没自己设高）⇒ 确实复用了同一套，
                // 而不是另画一个「看起来像」的下拉（之前是 Button + 自绘 Popup，高宽都得手对齐）
                bool hOk = Math.Abs(_dateCombo.ActualHeight - 34) < 1;
                bool wOk = _dateCombo.ActualWidth > 0;
                bool openOk = _dateCombo.IsDropDownOpen;
                bool selOk = _dateCombo.SelectedIndex >= 0 && _dateCombo.SelectedIndex < total;
                bool todayOk = total > 0 && ((ComboBoxItem)_dateCombo.Items[0]).IsEnabled;   // 今天永远可选
                bool all = hOk && wOk && openOk && selOk && todayOk;
                sb.AppendLine("  日期下拉: " + total + "项（无记录 " + off + " 项置灰）"
                    + " 选中=" + _dateCombo.SelectedIndex
                    + " 框=" + (int)_dateCombo.ActualWidth + "x" + (int)_dateCombo.ActualHeight);
                sb.AppendLine("            展开=" + openOk + " 有选中=" + selOk + " 今天可选=" + todayOk
                    + " 同款高34=" + hOk + (all ? "  ✓" : "  ✗"));
                _dateCombo.IsDropDownOpen = false;
            }
            else sb.AppendLine("  日期下拉: 控件未就绪 ✗");
        }
        catch (Exception ex) { sb.AppendLine("  日期下拉自检异常: " + ex.Message); }
        sb.AppendLine("  角色候选 " + ChList.Count + "，字体候选 " + CmbFont.Items.Count);
        sb.AppendLine("  大小=" + (int)SldSize.Value + "px  点击出声=" + (ChkClick.IsChecked == true)
            + "  气泡=" + (ChkBub.IsChecked == true) + " 高" + (int)SldBub.Value);
        sb.AppendLine("  音量=" + (int)SldVol.Value + "%  试听按钮=" + (BtnPreview != null));
        sb.AppendLine("  气泡: 样式=" + _bubbleStyleSel + " 颜色=#" + HexOf(_bubbleColorSel)
            + " 字号=" + CurFontPt.ToString("0.0", CultureInfo.InvariantCulture) + "pt"
            + "  外框文件 " + ListBubbleFrames().Count + " 个");
        sb.AppendLine("  文字颜色=" + (string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : "#" + _bubbleInkSel));
        sb.AppendLine("  主题色=#" + HexOf(_themeColor) + "  开机自启动=" + AutoStartEnabled());
        // 主题色派生出来的两档颜色：色相不变，只压明度，够不够读看这两个比值
        {
            Color solidC = Darken(_themeColor, Colors.White, 4.5);
            Color inkC = Darken(_themeColor, Color.FromRgb(0xE1, 0xE5, 0xEA), 4.5);
            sb.AppendLine("  主题色派生: 原色 #" + HexOf(_themeColor)
                + " │ 填充(按钮/开关) " + HexOf(solidC) + " 白字 "
                + Contrast(solidC, Colors.White).ToString("0.00", CultureInfo.InvariantCulture) + ":1"
                + " │ 读数(左栏) " + HexOf(inkC) + " 压灰 "
                + Contrast(inkC, Color.FromRgb(0xE1, 0xE5, 0xEA)).ToString("0.00", CultureInfo.InvariantCulture) + ":1");
        }
        var animInfo = new List<string>();
        foreach (AnimCard c in AnimCards)
            animInfo.Add(c.Key + "=" + (c.Toggle.IsChecked == true ? "开" : "关") + "/" + (int)c.Amp.Value + "%/" + (int)c.Speed.Value + "%");
        sb.AppendLine("  动画卡片 " + AnimCards.Count + " 张: " + string.Join(" ", animInfo.ToArray()));
        var sfxNames = new List<string>();
        foreach (object o in CmbSet.Items) sfxNames.Add(o.ToString());
        sb.AppendLine("  音效组 " + CmbSet.Items.Count + " 个: " + string.Join(" / ", sfxNames.ToArray()));
        sb.AppendLine("  台词库 " + VoiceLines.Count + " 条；点击模式=" + (CmbClickMode.SelectedIndex == 1 ? "顺序" : "随机"));
        sb.AppendLine("  按键卡: 热力格=" + _keyCells.Count
            + " 数据=" + _keyData.Count
            + "  方格=" + _appTiles.Count + "  实时定时器=" + (_liveTimer != null));
        sb.AppendLine("  立绘预览=" + (ImgChar.Source != null) + "  大小预览=" + (ImgSize.Source != null) + "  气泡预览=" + (ImgBub.Source != null));
        // 未保存提示：四路都要认得出来 —— 音量（走 overrides）、角色高度（存 char.json）、
        // 动画幅度（存 char.json）、下拉框选择（角色目录）。任一路漏了，用户就会觉得「改了没反应」。
        try
        {
            var known = new List<string>();
            // 逐个改 → 判定 → 还原（还原用各控件自己的初值，别让快照漂）
            int v0 = (int)SldVol.Value, s0 = (int)SldSize.Value, e0 = CmbEvery.SelectedIndex;
            string snap0 = Snapshot();
            SldVol.Value = v0 == 100 ? 88 : 100;
            if (Snapshot() != snap0) known.Add("音量");
            SldVol.Value = v0;
            SldSize.Value = s0 == 400 ? 219 : 400;
            if (Snapshot() != snap0) known.Add("角色高度");
            SldSize.Value = s0;
            if (AnimCards.Count > 0)
            {
                int a0 = (int)AnimCards[0].Amp.Value;
                AnimCards[0].Amp.Value = a0 == 200 ? 40 : 200;
                if (Snapshot() != snap0) known.Add("动画幅度");
                AnimCards[0].Amp.Value = a0;
            }
            CmbEvery.SelectedIndex = (e0 + 1) % Math.Max(1, CmbEvery.Items.Count);
            if (Snapshot() != snap0) known.Add("下拉选择");
            CmbEvery.SelectedIndex = e0;
            bool back = Snapshot() == snap0;
            sb.AppendLine("  未保存检测: 认出 " + known.Count + "/4 [" + string.Join(" ", known.ToArray()) + "]"
                + "  还原归零=" + back + "  提示标签=" + (LblDirty != null)
                + (known.Count >= 3 && back ? "  ✓" : "  ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  未保存检测异常: " + ex.Message); }
        var icoImg = (Image)Win.FindName("imgIcon");
        sb.AppendLine("  窗口图标=" + (Win.Icon != null) + "  标题栏图标=" + (icoImg != null && icoImg.Source != null));
        // 通用设置 → 桌面显示开关（没有它就没法关掉桌宠）
        ShowPage("ui");
        sb.AppendLine("  桌面显示开关=" + (ChkShowPet != null
            ? (ChkShowPet.IsChecked == true ? "已打开" : "已关闭") : "✗ 未创建"));
        sb.AppendLine("  全屏隐藏开关=" + (ChkHideFs != null
            ? (ChkHideFs.IsChecked == true ? "已打开" : "已关闭") : "✗ 未创建")
            + "  pet-config=" + ReadKey(Path.Combine(Here, "pet-config.txt"), "hideFullscreen"));
        // 下拉框：点一下就展开（真发事件走一遍，不调内部函数，避免断言假绿）
        try
        {
            var me = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
            me.RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent;
            CmbChar.RaiseEvent(me);
            bool opened = CmbChar.IsDropDownOpen;
            CmbChar.IsDropDownOpen = false;
            sb.AppendLine("  下拉一点展开: " + (opened ? "✓" : "✗")
                + "（点文字区即弹出，收起也走同一点击）");
            // 点列表里的选项：不能被「一点就展开」的处理器顺手关掉 —— 之前就是这么把选中吞掉的。
            // 列表项容器在无桌面会话里生成不出来，直接断言守卫：框身上要接管、别的视觉树里要放行。
            bool onBox = ShouldToggleCombo(CmbEvery, CmbEvery);
            bool onItem = ShouldToggleCombo(CmbEvery, new Border());
            sb.AppendLine("  下拉点选项: 框身接管=" + onBox + " 列表放行=" + (!onItem)
                + (onBox && !onItem ? "  ✓" : "  ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  下拉一点展开: 异常 " + ex.Message); }
        // 承重线颜色：比填充再深一档，且和填充能区分开（否则等于没有这条线）
        try
        {
            Color edge = ((SolidColorBrush)Win.Resources["AccentEdge"]).Color;
            Color fill = ((SolidColorBrush)Win.Resources["AccentSolid"]).Color;
            sb.AppendLine("  承重线: #" + HexOf(edge) + " 白字对比 "
                + Contrast(edge, Colors.White).ToString("0.0", CultureInfo.InvariantCulture)
                + ":1 │ 与填充 #" + HexOf(fill) + " 对比 "
                + Contrast(edge, fill).ToString("0.00", CultureInfo.InvariantCulture) + ":1"
                + (Contrast(edge, Colors.White) >= 8.0 && Contrast(edge, fill) >= 1.85 ? "  ✓" : "  ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  承重线: 异常 " + ex.Message); }

        // 主按钮三态：默认 → 悬停 → 按下必须逐级可分（等价于「看得见反馈」），
        // 并且切角必须真的裁上了（Clip 为空说明 InstallButtonChamfer 没接上）。
        try
        {
            Color fillB = ((SolidColorBrush)Win.Resources["AccentSolid"]).Color;
            Color hovB = ((SolidColorBrush)Win.Resources["AccentHover"]).Color;
            Color prsB = ((SolidColorBrush)Win.Resources["AccentPress"]).Color;
            Color softB = ((SolidColorBrush)Win.Resources["AccentSoft"]).Color;
            Color inkB = ((SolidColorBrush)Win.Resources["AccentInk"]).Color;
            bool stepB = HexOf(hovB) != HexOf(fillB) && HexOf(prsB) != HexOf(hovB);
            bool readB = Contrast(inkB, softB) >= 4.5;
            Button probeB = Win.FindName("btnSave") as Button;
            ApplyChamferIfPrimary(probeB);
            bool clipB = probeB != null && probeB.Clip != null
                         && probeB.Clip.Bounds.Width > 0 && probeB.Clip.Bounds.Height > 0;
            sb.AppendLine("  按钮三态: " + HexOf(fillB) + " → " + HexOf(hovB) + " → " + HexOf(prsB)
                + " │ 选中底 " + HexOf(softB) + " 主题字 "
                + Contrast(inkB, softB).ToString("0.00", CultureInfo.InvariantCulture) + ":1"
                + " │ 切角裁切=" + (clipB ? "已应用" : "缺失")
                + (stepB && readB && clipB ? "  ✓" : "  ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  按钮三态: 异常 " + ex.Message); }

        // 色域扫描：三态在任意主题色下都必须「逐级可分」且「白字 ≥ 4.5:1」。
        // 灰阶从墨黑扫到中灰（最容易翻车的区间）+ 六个饱和色相 + 黄（最亮）。
        try
        {
            var probes = new List<Color>();
            for (int g = 0x16; g <= 0x90; g += 6) probes.Add(Color.FromRgb((byte)g, (byte)g, (byte)g));
            byte[] sat = new byte[] { 0xC0, 0x00, 0x00,  0x00, 0xC0, 0x00,  0x00, 0x00, 0xC0 };
            probes.Add(Color.FromRgb(sat[0], sat[1], sat[2]));
            probes.Add(Color.FromRgb(sat[3], sat[4], sat[5]));
            probes.Add(Color.FromRgb(sat[6], sat[7], sat[8]));
            probes.Add(Color.FromRgb(0xC0, 0xC0, 0x00));
            probes.Add(Color.FromRgb(0x00, 0xC0, 0xC0));
            probes.Add(Color.FromRgb(0xC0, 0x00, 0xC0));
            probes.Add(Color.FromRgb(0xFF, 0xFA, 0x00));
            probes.Add(Color.FromRgb(0x3B, 0x9B, 0xE0));
            int bad = 0, deepN = 0;
            double worst = 99;
            foreach (Color t in probes)
            {
                Color f0, h0, p0;
                BtnTones(t, out f0, out h0, out p0);
                if (RelLum(f0) < 0.05) deepN++;
                double c1 = Contrast(h0, Colors.White), c2 = Contrast(p0, Colors.White);
                if (c1 < worst) worst = c1;
                if (c2 < worst) worst = c2;
                if (HexOf(h0) == HexOf(f0) || HexOf(p0) == HexOf(h0) || c1 < 4.5 || c2 < 4.5) bad++;
            }
            sb.AppendLine("  按钮三态扫描: " + probes.Count + " 个主题色（含墨黑" + deepN + "档提亮）│ 最差白字 "
                + worst.ToString("0.00", CultureInfo.InvariantCulture) + ":1 │ 不合格 " + bad
                + (bad == 0 ? "  ✓" : "  ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  按钮三态扫描: 异常 " + ex.Message); }

        // 弹窗按钮：三种各建一个，确认没有 2px 重线、线宽是 1、焦点圈在
        try { sb.AppendLine("  弹窗按钮: " + Dlg.Probe()); }
        catch (Exception ex) { sb.AppendLine("  弹窗按钮: 异常 " + ex.Message); }

        // 首页「位置」实时刷新：轮询 pet-pos.txt，文件一变读数就跟着变（备份→改→读→还原）
        // ⚠ 这里会切到首页，必须还原当前页 —— 后面几条自检（磁贴换肤）依赖停留在「通用设置」
        try
        {
            string pf = Path.Combine(Here, "pet-pos.txt");
            string bakPage = _curPage;
            string bak = File.Exists(pf) ? File.ReadAllText(pf) : null;
            string got = "无";
            try
            {
                ShowPage("home");
                StartLivePos();
                File.WriteAllText(pf, "x=1111\r\ny=222\r\n");
                _livePosCache = "";                       // 模拟定时器发现文件变了
                SyncLivePos();
                got = TxtHomePos != null ? TxtHomePos.Text : "无";
            }
            finally
            {
                if (bak != null) File.WriteAllText(pf, bak); else { try { File.Delete(pf); } catch (Exception) { } }
                _livePosCache = ""; SyncLivePos(); StopLivePos();
                if (_curPage != bakPage) ShowPage(bakPage);
            }
            bool posOk = got.Replace(" ", "") == "1111│222";
            sb.AppendLine("位置实时: 文件写 1111/222 → 读数 " + got + " │ 轮询 400ms"
                + " │ " + (posOk ? "跟随 ✓" : "没跟上 ✗"));
        }
        catch (Exception ex) { sb.AppendLine("位置实时: 异常 " + ex.Message); }

        // 换肤动效：点色块时资源上要挂 ColorAnimation（起点 = 旧色，终点 = 新色），而不是硬切。
        // 自检默认把动效关掉了（截图只抓终态），这里临时打开再读一次画笔的动画状态。
        try
        {
            Color bakTheme = _themeColor; bool bakMotion = _motionOff;
            Color demo = Color.FromRgb(0xE0, 0x6C, 0x8B);
            Color want = Darken(demo, Colors.White, 4.5);
            _motionOff = false;
            ApplyThemeColor(demo, true);
            SolidColorBrush ab = Win.Resources["AccentSolid"] as SolidColorBrush;
            bool hasAnim = ab != null && ab.HasAnimatedProperties;
            Color startCol = ab != null ? ab.Color : Colors.Transparent;
            _motionOff = bakMotion;
            ApplyThemeColor(bakTheme);          // 复位（ms=0 ⇒ 硬切回去，不留动画残留）
            bool okA = hasAnim && HexOf(startCol) != HexOf(want);
            sb.AppendLine("  换肤动效: ColorAnimation " + (hasAnim ? "已挂 ✓" : "没挂 ✗")
                + " │ 起点 #" + HexOf(startCol) + "（旧色）→ 终点 #" + HexOf(want)
                + " │ " + (okA ? "渐变 ✓" : "等于硬切 ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  换肤动效: 异常 " + ex.Message); }

        // 换肤后右栏磁贴必须跟着变色（用户反馈过的「右侧按钮不变色」）：
        // 它的填充是构建时抓的 Brush 实例，靠 ApplyThemeColor 里的 PaintTiles 重涂
        try
        {
            Color bak2 = _themeColor;
            Color demo2 = Color.FromRgb(0xE0, 0x4F, 0x5F);          // 洋红
            ApplyThemeColor(demo2);
            SolidColorBrush tileB = Tiles[1] != null ? Tiles[1].Background as SolidColorBrush : null;
            bool tileOk = tileB != null && tileB.Color.R > 0xB0 && tileB.Color.G < 0x70 && tileB.Color.B < 0x90;
            string hexLbl = TxtHomeTheme != null ? TxtHomeTheme.Text : "—";
            bool lblOk = string.Equals(hexLbl, "#" + HexOf(demo2), StringComparison.OrdinalIgnoreCase);
            ApplyThemeColor(bak2);
            sb.AppendLine("  磁贴换肤: 当前项填充 " + (tileB == null ? "—" : "#" + HexOf(tileB.Color))
                + " │ " + (tileOk ? "跟随 ✓" : "没跟上 ✗")
                + " │ 左栏读数 " + hexLbl + " " + (lblOk ? "✓" : "✗"));
        }
        catch (Exception ex) { sb.AppendLine("  磁贴换肤: 异常 " + ex.Message); }

        // 导入页的角色方框卡：立绘有没有真贴上、「使用中」是不是标在当前角色上
        try
        {
            ShowPage("import");
            RefreshImport();
            int cards = 0, arts = 0;
            foreach (CharEntry ce in ChList)
            {
                if (string.IsNullOrEmpty(ce.Dir)) continue;
                cards++;
                if (Portrait(ce.Image) != null) arts++;
            }
            sb.AppendLine("  导入卡片: " + cards + " 张 │ 立绘已载入 " + arts + " │ 当前标记 "
                + (_importMarked == "" ? "无" : _importMarked)
                + " │ " + (cards > 0 && arts == cards && _importMarked != "" ? "正常 ✓" : "异常 ✗"));

            // 换角色保存后，导入页的「使用中」必须立刻换人：
            // Cur 是 pet-overrides.txt 的内存副本，以前 WriteOverrides 只写盘不动副本
            // ⇒ 卡片一直指着旧角色，得重启整个设置窗口才变（用户实测反馈的那个 bug）。
            CharEntry other = null;
            foreach (CharEntry ce in ChList)
                if (!string.IsNullOrEmpty(ce.Dir) && !IsCurrent(ce)) { other = ce; break; }
            if (other == null) sb.AppendLine("  导入同步: 只有一个角色包，跳过");
            else
            {
                string before = _importMarked;
                string tmp = Path.Combine(Here, "pet-overrides-selftest.txt");
                try
                {
                    WriteOverridesTo(tmp, CollectOverrides(RelToHere(other.Dir)));
                    bool moved = IsCurrent(other);
                    RefreshImport();
                    string after = _importMarked;
                    sb.AppendLine("  导入同步: " + before + " → " + after
                        + " │ 内存副本 " + (moved ? "跟随 ✓" : "没跟上 ✗")
                        + " │ " + (moved && after == other.Name ? "同步 ✓" : "不同步 ✗"));
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                    LoadOverrides();                  // 从真配置重读，撤销这次试写
                    RefreshImport();
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("  导入卡片: 异常 " + ex.Message); }

        // 角色陪伴方格：头部进图、尾巴进文字行，画布要铺得开
        try
        {
            ShowPage("stats");
            BuildStatsPage();
            UpdateStats(true);
            double w0 = _charMap == null ? 0 : _charMap.ActualWidth;
            sb.AppendLine("  角色方格: 进图 " + _charItems.Count + " 格 │ 其余 " + _charTailItems.Count
                + " 个进文字行 │ 画布 " + (w0 > 0 ? Math.Round(w0) + "×" + (int)CharMapH : "未布局（按 600 排）")
                + " │ " + (_charItems.Count > 0 ? "已铺格 ✓" : "空 ✗"));
        }
        catch (Exception ex) { sb.AppendLine("  角色方格: 异常 " + ex.Message); }

        // 快速预设的选中块必须跟着主题色（曾经写死墨色，全界面就那一处黑）
        if (SizePresetBtns != null && SizePresetBtns.Length > 0)
        {
            double keep = SldSize.Value;
            SldSize.Value = 260;            // 对准一个预设值，才该有且只有一个块高亮
            SyncSizePresets();
            Color want = Darken(_themeColor, Colors.White, 4.5);
            int onN = 0, inkN = 0;
            foreach (Button b in SizePresetBtns)
            {
                var sb2 = b.Background as SolidColorBrush;
                if (sb2 == null) continue;
                if (sb2.Color == want) onN++;
                if (sb2.Color == Color.FromRgb(0x21, 0x25, 0x29)) inkN++;
            }
            SldSize.Value = keep;
            SyncSizePresets();
            sb.AppendLine("  快速预设跟随主题: 选中块=AccentSolid " + onN + "/" + SizePresetBtns.Length
                + " │ 墨色块=" + inkN + (onN == 1 && inkN == 0 ? "  ✓" : "  ✗"));
        }
        // ---- 动效自检：切页淡入、开关滑块、磁贴墨底、状态条 ----
        // 动画是按合成时钟推进的，必须让 Dispatcher 真的转起来才看得到终值；
        // 所以这里先触发、再空转 300ms、最后断言「稳态」（而不是断言中间帧）。
        try
        {
            ShowPage("ui");
            // 首帧必须是「透明 + 下移」：基准值落在起点，否则会闪一帧全不透明
            double pop0 = ContentHost != null ? ContentHost.Opacity : -1;
            TranslateTransform ptt0 = ContentHost != null ? ContentHost.RenderTransform as TranslateTransform : null;
            double pty0 = ptt0 != null ? ptt0.Y : -1;
            PumpFor(300);
            TranslateTransform ptt = ContentHost != null ? ContentHost.RenderTransform as TranslateTransform : null;
            double pop = ContentHost != null ? ContentHost.Opacity : -1;
            bool pageAnim = ptt != null && pop0 < 0.2 && pty0 > 1.0
                            && Math.Abs(ptt.Y) < 0.01 && Math.Abs(pop - 1.0) < 0.01;

            string knobInfo = "—";
            bool knobOk = false;
            if (ChkShowPet != null)
            {
                ChkShowPet.ApplyTemplate();
                TranslateTransform kt = ChkShowPet.Template.FindName("knobX", ChkShowPet) as TranslateTransform;
                Border tb2 = ChkShowPet.Template.FindName("trackOn", ChkShowPet) as Border;
                bool on = ChkShowPet.IsChecked == true;
                double kx = kt != null ? kt.X : -1;
                double to2 = tb2 != null ? tb2.Opacity : -1;
                knobInfo = "X=" + kx.ToString("0.0") + " 轨道=" + to2.ToString("0.00");
                knobOk = kt != null && tb2 != null
                         && Math.Abs(kx - (on ? 20 : 0)) < 0.6
                         && Math.Abs(to2 - (on ? 1 : 0)) < 0.05;
            }

            ToggleButton tl = Win.FindName("tileUi") as ToggleButton;
            Border dimFill = null;
            ScaleTransform sbs = null;
            if (tl != null)
            {
                tl.ApplyTemplate();
                dimFill = tl.Template.FindName("dim", tl) as Border;
                sbs = tl.Template.FindName("sbs", tl) as ScaleTransform;
            }
            // 列表行：未选中靠 30% 黑压暗，选中把 dim 透明度收到 0 + 左侧白条展开
            bool tileOk = dimFill != null && sbs != null
                          && dimFill.Opacity < 0.01 && sbs.ScaleY > 0.99;

            // 八行图标：漏一次 Visibility 就整列隐形，而编译和上面那条断言都不会报错
            int icoShown = 0;
            foreach (ToggleButton tt in Tiles)
            {
                if (tt == null) continue;
                tt.ApplyTemplate();
                System.Windows.Shapes.Path ip = tt.Template.FindName("ico", tt) as System.Windows.Shapes.Path;
                if (ip != null && ip.Visibility == Visibility.Visible && ip.Data != null) icoShown++;
            }

            // 新动效的接线完备性：按压缩放（磁贴/主按钮）、导航选中淡入、滑块拇指悬停放大
            ScaleTransform tsTl = tl != null ? tl.Template.FindName("ts", tl) as ScaleTransform : null;
            Button btnP = Win.FindName("btnHomeShow") as Button;
            ScaleTransform psBtn = null;
            if (btnP != null) { btnP.ApplyTemplate(); psBtn = btnP.Template.FindName("ps", btnP) as ScaleTransform; }
            // NavHost 常年 Collapsed（左栏列表已被八格磁贴取代），不能用 SelectedItem 取容器；
            // 用探针项套同一份样式验证模板接线
            ListBoxItem navProbe = new ListBoxItem { Style = (Style)Win.Resources["NavItem"], Content = "probe" };
            Border navSel = null;
            try { navProbe.ApplyTemplate(); navSel = navProbe.Template.FindName("sel", navProbe) as Border; }
            catch (Exception ex) { LogErr("SelfTest", ex); }
            ScaleTransform thS = null;
            if (SldVol != null)
            {
                SldVol.ApplyTemplate();
                Track trk = SldVol.Template.FindName("PART_Track", SldVol) as Track;
                Thumb th = trk != null ? trk.Thumb : null;
                if (th != null) { th.ApplyTemplate(); thS = th.Template.FindName("ths", th) as ScaleTransform; }
            }

            ShowSavedNote("自检  │  TEST");
            PumpFor(220);
            bool noteShow = LblSaved != null && LblSaved.Visibility == Visibility.Visible
                            && LblSaved.Opacity > 0.99;
            HideSavedNote(false);
            bool noteHide = LblSaved == null || LblSaved.Visibility != Visibility.Visible;

            sb.AppendLine("  动效: 切页淡入 " + (pageAnim ? "✓" : "✗")
                + "(" + pop0.ToString("0.00") + "→" + pop.ToString("0.00")
                + " / Y" + pty0.ToString("0.0") + "→" + (ptt != null ? ptt.Y.ToString("0.0") : "?") + ")"
                + " │ 开关滑动 " + knobInfo + (knobOk ? " ✓" : " ✗")
                + " │ 列表选中 " + (dimFill == null ? "缺 ✗"
                    : "压暗=" + dimFill.Opacity.ToString("0.00")
                      + " 白条=" + (sbs != null ? sbs.ScaleY.ToString("0.00") : "?")
                      + (tileOk ? " ✓" : " ✗")
                + " │ 右栏图标 " + icoShown + "/8 " + (icoShown == 8 ? "✓" : "✗"))
                + " │ 按压缩放 " + (tsTl != null && psBtn != null ? "✓" : "✗")
                + " │ 导航淡入 " + (navSel != null ? "✓" : "✗")
                + " │ 滑块拇指 " + (thS != null ? "✓" : "✗")
                + " │ 状态条 " + (noteShow && noteHide ? "出现/收起 ✓" : "✗"));
            ShowPage("home");
        }
        catch (Exception ex) { sb.AppendLine("  动效自检异常: " + ex.Message); }
        string elog = Path.Combine(Here, "pet-settings-err.log");
        int errs = File.Exists(elog) ? File.ReadAllLines(elog).Length : 0;
        sb.AppendLine("  界面字体 = " + _uiFontInfo);
        // 字体审计：窗口里每个文字控件都必须用同一支字体族（图标用 MDL2、hex 用 Consolas，属例外）。
        // 之前「下拉框里的汉字是宋体、标签是雅黑」的根因就是只写了 Saira Wide、汉字各自由系统回退。
        try
        {
            string want = UiFace != null ? UiFace.Source : "";
            int total = 0, other = 0;
            var samples = new List<string>();
            Action<DependencyObject> walk = null;
            walk = d =>
            {
                var fe = d as FrameworkElement;
                if (fe != null && (d is TextBlock || d is Button || d is ComboBox || d is TextBox))
                {
                    total++;
                    string got = System.Windows.Documents.TextElement.GetFontFamily(d).Source;
                    if (got != want && got != "Segoe MDL2 Assets" && got != "Consolas")
                    {
                        other++;
                        if (samples.Count < 3) samples.Add(got);
                    }
                }
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) walk(VisualTreeHelper.GetChild(d, i));
            };
            walk(Win);
            sb.AppendLine("  字体审计: " + total + " 个文字控件 │ 与界面字体不一致 " + other
                + (other == 0 ? " ✓" : " ✗ " + string.Join(", ", samples.ToArray())));
        }
        catch (Exception ex) { sb.AppendLine("  字体审计异常: " + ex.Message); }
        sb.AppendLine("  错误日志条数 = " + errs + (errs == 0 ? " ✓ 无异常" : " ✗"));
        string outp = Path.Combine(Here, "pet-settings-selftest.txt");
        try { File.WriteAllText(outp, sb.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
        Console.WriteLine(sb.ToString());
        Win.Close();
    }

    // ---------------- XAML ----------------
    const string XamlText = @"<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Q Pet · 设置"" Width=""1180"" Height=""780"" MinWidth=""1080"" MinHeight=""620""
        WindowStartupLocation=""CenterScreen"" Background=""{DynamicResource Desk}""
        WindowStyle=""None"" ResizeMode=""CanResize""
        FontSize=""13""
        UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight=""52"" ResizeBorderThickness=""6"" GlassFrameThickness=""0""
                  CornerRadius=""0"" UseAeroCaptionButtons=""False""/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <!-- ============================================================
         令牌：纸 / 板 / 墨 / 信号色。
         信号色（Accent）只做填充，含义只有三种：当前位置、可点击、醒目提醒。
         它上面的字必须用 OnAccent，由 ApplyThemeColor 按亮度切换墨或白。
         浅色主题里不能拿信号色当线或字（黄在白底上只有 1.1:1）。
         ============================================================ -->
    <!-- 复合字体：拉丁 Saira Wide、汉字雅黑。直接写 Saira Wide 的话汉字会由系统各自回退，
         粗体落雅黑、常规落宋体，看起来像两套字（见 Fonts/QPetUI.CompositeFont）。 -->
    <FontFamily x:Key=""UiFont"">pack://application:,,,/Fonts/#QPet UI</FontFamily>
    <!-- 三层明度，从上到下：桌（标题栏/左右栏/底栏）-> 纸（中栏工作面）-> 白（板/输入框）。
         白只留给「压在最上面那一层」，所以整屏不会是一片白：白永远被灰纸框住。
         色相取中性偏冷，不用暖米：信号色是 #005FB8、墨是近黑，都在冷端；
         底色一发黄，蓝就成了补色冲突，看着像两套配色硬拼。
         明度差靠 Desk 压深拿到、不靠色相，所以「白被框住」和「不和蓝黑打架」能同时成立。 -->
    <SolidColorBrush x:Key=""Desk"" Color=""#E1E5EA""/>
    <SolidColorBrush x:Key=""Paper"" Color=""#EDF0F4""/>
    <SolidColorBrush x:Key=""Panel"" Color=""#FFFFFF""/>
    <SolidColorBrush x:Key=""Ink"" Color=""#E4212429""/>
    <SolidColorBrush x:Key=""Ink2"" Color=""#995A5F68""/>
    <SolidColorBrush x:Key=""Rule"" Color=""#D9DDE4""/>
    <SolidColorBrush x:Key=""Chip"" Color=""#E6EAF0""/>
    <SolidColorBrush x:Key=""Accent"" Color=""#005FB8""/>
    <!-- 主题色的两个「深一档」。原色只适合小面积（编号、刻度、当前行），
         一铺大面积就压不住字：青绿 #2FA8A0 上白字只有 2.9:1，黄更惨。
         所以按用途各压一档，色相不变，仍一眼认得出是哪个主题：
           AccentSolid = 压到「白字 ≥ 4.5:1」→ 主按钮 / 开关轨道 / 滑块 / 选中的行
           AccentInk   = 压到「压在 Desk 上 ≥ 4.5:1」→ 左栏大号读数
         两个值由 ApplyThemeColor 现算，换主题色自动跟着走。 -->
    <SolidColorBrush x:Key=""AccentSolid"" Color=""#005FB8""/>
    <!-- 承重线：比 AccentSolid 再深一档，按钮底边用它压住整块填充（换主题时由 C# 重算） -->
    <SolidColorBrush x:Key=""AccentEdge"" Color=""#1D4D6F""/>
    <SolidColorBrush x:Key=""AccentInk"" Color=""#005FB8""/>
    <SolidColorBrush x:Key=""OnAccent"" Color=""#FFFFFF""/>
    <!-- 压深后的主题色底上固定用白字：这一档底色一定够深，白字才稳 -->
    <SolidColorBrush x:Key=""OnAccentSolid"" Color=""#FFFFFF""/>
    <!-- 主按钮的两档交互色：由 ApplyThemeColor 现算（AccentSolid 压深 4.5:1，
         再往下压到 6:1 / 9:1），换主题自动跟着走，不会露出写死的蓝。 -->
    <SolidColorBrush x:Key=""AccentHover"" Color=""#0B5394""/>
    <SolidColorBrush x:Key=""AccentPress"" Color=""#0A4276""/>
    <!-- 选中行的浅底：主题色往白里调 88%，只做底色，不参与对比度 -->
    <SolidColorBrush x:Key=""AccentSoft"" Color=""#E6EFF8""/>
    <SolidColorBrush x:Key=""Danger"" Color=""#C42B1C""/>
    <SolidColorBrush x:Key=""DangerSoft"" Color=""#FDECEA""/>
    <!-- 深色填充：磁贴当前态、主按钮、未保存标签。
         用 #212529 暖调近黑而不是纯黑：纯黑在屏幕上吸光、没层次，大面积铺开像贴纸；
         暖墨在白底上更像「深色纸墨」。每块深色都在模板里加一条 #1FFFFFFF 发丝边，
         有了这条边深色块才有厚度，不闷。 -->
    <SolidColorBrush x:Key=""InkSolid"" Color=""#212529""/>


    <!-- 字号：眉标 10.5 / 脚注 11 / 说明 12 / 正文 13 / 卡片标题 15 / 页面标题 30 -->
    <Style x:Key=""Eyebrow"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""10""/>
      <Setter Property=""FontWeight"" Value=""Bold""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
    </Style>
    <Style x:Key=""H1"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""34""/>
      <Setter Property=""FontWeight"" Value=""Bold""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
    </Style>
    <Style x:Key=""H2"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""17""/>
      <Setter Property=""FontWeight"" Value=""Bold""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
    </Style>
    <Style x:Key=""Sub"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""12""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
      <Setter Property=""TextWrapping"" Value=""Wrap""/>
      <Setter Property=""Margin"" Value=""0,2,0,0""/>
    </Style>

    <!-- 磁贴上半段的分区斜纹：7px 一格、1px 细线、4% 黑。把它从「空白」变成「待装载」的分区 -->
    <!-- 中栏工作面的斜纹：只画线，底色交给容器的 Background（笔刷自带的填充在 Tile 模式下不上色） -->
    <!-- 深色块上的斜纹：同一套 7px 网格，线换成 7% 白，压在墨底上才看得见 -->
    <!-- 白板：直角、细线、无投影。相邻两块用 -1 外边距叠成一根线。 -->
    <Style x:Key=""Card"" TargetType=""Border"">
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""BorderBrush"" Value=""#D9DDE4""/>
      <Setter Property=""BorderThickness"" Value=""0,0,0,1""/>
      <Setter Property=""CornerRadius"" Value=""0""/>
      <!-- 不画盒子，只画一条底线：六行设置在一屏里靠线分，不靠卡片堆 -->
      <Setter Property=""Padding"" Value=""0,13""/>
      <Setter Property=""Margin"" Value=""0,0,0,-1""/>
    </Style>

    <!-- 虚线取景框：立绘 / 气泡预览。边框不能靠 Border 画（Border 不支持虚线），用 Rectangle 叠在最上层 -->
    <Style x:Key=""DashedBox"" TargetType=""ContentControl"">
      <Setter Property=""Background"" Value=""{DynamicResource Panel}""/>
      <Setter Property=""Padding"" Value=""10""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ContentControl"">
        <Grid>
          <Border Background=""{TemplateBinding Background}""/>
          <ContentPresenter Margin=""{TemplateBinding Padding}""/>
          <Rectangle Stroke=""#C7CCD4"" StrokeThickness=""1"" StrokeDashArray=""3 3""
                     Fill=""Transparent"" SnapsToDevicePixels=""True""/>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""Divider"" TargetType=""Border"">
      <Setter Property=""Height"" Value=""1""/>
      <Setter Property=""Background"" Value=""#D9DDE4""/>
      <Setter Property=""Margin"" Value=""0,6""/>
    </Style>
    <!-- 页头下面那根线用墨：有意义的线不淡化 -->
    <Style x:Key=""HeadRule"" TargetType=""Border"">
      <Setter Property=""Height"" Value=""1""/>
      <Setter Property=""Background"" Value=""{DynamicResource Rule}""/>
      <Setter Property=""Margin"" Value=""0,14,0,0""/>
    </Style>

    <!-- 键盘焦点圈：方角、2px 墨，画在控件外围不影响布局 -->
    <Style x:Key=""FocusRing"">
      <Setter Property=""Control.Template"">
        <Setter.Value><ControlTemplate>
          <Rectangle Margin=""-3"" StrokeThickness=""2"" RadiusX=""0"" RadiusY=""0""
                     Stroke=""{DynamicResource Ink}"" SnapsToDevicePixels=""True""/>
        </ControlTemplate></Setter.Value>
      </Setter>
    </Style>

    <!-- 输入框：白底、1px 细线、直角；聚焦时描边由细线变墨（不改粗细，内容不抖） -->
    <Style TargetType=""TextBox"">
      <Setter Property=""Height"" Value=""34""/>
      <Setter Property=""Padding"" Value=""11,0""/>
      <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""Background"" Value=""{DynamicResource Panel}""/>
      <Setter Property=""CaretBrush"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TextBox"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"" BorderBrush=""{DynamicResource Rule}""
                BorderThickness=""1"" CornerRadius=""0"">
          <ScrollViewer x:Name=""PART_ContentHost"" Margin=""{TemplateBinding Padding}""
                        VerticalAlignment=""Center"" Focusable=""False""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""BorderBrush"" Value=""{DynamicResource Ink2}""/>
          </Trigger>
          <Trigger Property=""IsKeyboardFocused"" Value=""True"">
            <Setter TargetName=""b"" Property=""BorderBrush"" Value=""{DynamicResource Ink}""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{DynamicResource Paper}""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 主按钮：主题色填充 + 右下 8px 切角。
         切角是真裁切（C# 里 InstallButtonChamfer 给 PrimaryButton 上 Clip），
         不再是「盖一块父容器同色的三角」，所以按钮放到任何底色上都成立。
         装饰只留切角这一个信号；悬停/按下用主题色现算的两档深色，白擦入和底部重线都已删掉。 -->
    <Style x:Key=""PrimaryButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""{DynamicResource AccentSolid}""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource OnAccentSolid}""/>
      <Setter Property=""Height"" Value=""34""/>
      <Setter Property=""Padding"" Value=""22,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Grid RenderTransformOrigin=""0.5,0.5"" UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
          <Grid.RenderTransform><ScaleTransform x:Name=""ps"" ScaleX=""1"" ScaleY=""1""/></Grid.RenderTransform>
          <Border x:Name=""bg"" Background=""{TemplateBinding Background}""/>
          <!-- 发丝边：深色块靠这 12% 的一条白线才有厚度 -->
          <Border BorderBrush=""#1FFFFFFF"" BorderThickness=""1"" IsHitTestVisible=""False""/>
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bg"" Property=""Background"" Value=""{DynamicResource AccentHover}""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""bg"" Property=""Background"" Value=""{DynamicResource AccentPress}""/>
            <!-- ScaleTransform 是 Freezable，属性不能用 Setter 直接写，必须走动画 -->
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ps"" Storyboard.TargetProperty=""ScaleX"" To=""0.98"" Duration=""0:0:0.08""/>
                <DoubleAnimation Storyboard.TargetName=""ps"" Storyboard.TargetProperty=""ScaleY"" To=""0.98"" Duration=""0:0:0.08""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ps"" Storyboard.TargetProperty=""ScaleX"" To=""1"" Duration=""0:0:0.14"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""ps"" Storyboard.TargetProperty=""ScaleY"" To=""1"" Duration=""0:0:0.14"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
            <Setter TargetName=""bg"" Property=""Background"" Value=""{DynamicResource Chip}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 次级按钮：白底 + 1px 细线。悬停只换线色和底色（BorderThickness 不动，内容不抖）；
         原来那条「悬停长出主题色底线」和压在底部的 2px 墨线都已删掉，次级按钮不再跟主按钮抢注意力。 -->
    <Style x:Key=""SecondaryButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""{DynamicResource Panel}""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""Height"" Value=""34""/>
      <Setter Property=""Padding"" Value=""18,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Grid RenderTransformOrigin=""0.5,0.5"" UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
          <Grid.RenderTransform><ScaleTransform x:Name=""ss"" ScaleX=""1"" ScaleY=""1""/></Grid.RenderTransform>
          <Border x:Name=""bg"" Background=""{TemplateBinding Background}"" BorderBrush=""#C9CED6"" BorderThickness=""1""/>
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""#8A8F98""/>
            <Setter TargetName=""bg"" Property=""Background"" Value=""#FAFAFA""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""#8A8F98""/>
            <Setter TargetName=""bg"" Property=""Background"" Value=""{DynamicResource Chip}""/>
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ss"" Storyboard.TargetProperty=""ScaleX"" To=""0.98"" Duration=""0:0:0.08""/>
                <DoubleAnimation Storyboard.TargetName=""ss"" Storyboard.TargetProperty=""ScaleY"" To=""0.98"" Duration=""0:0:0.08""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ss"" Storyboard.TargetProperty=""ScaleX"" To=""1"" Duration=""0:0:0.14"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""ss"" Storyboard.TargetProperty=""ScaleY"" To=""1"" Duration=""0:0:0.14"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
            <Setter Property=""Opacity"" Value=""0.6""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 纯文字按钮：退出这类次要操作，悬停才出墨字和下划线 -->
    <Style x:Key=""GhostButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
      <Setter Property=""Height"" Value=""34""/>
      <Setter Property=""Padding"" Value=""10,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""bg"" Background=""Transparent"">
          <ContentPresenter x:Name=""cp"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 标题栏按钮：细线图标，悬停出 chip 底；关闭键悬停用危险红（系统惯例，唯一放行处） -->
    <Style x:Key=""CaptionButton"" TargetType=""Button"">
      <Setter Property=""Width"" Value=""46""/>
      <Setter Property=""Height"" Value=""52""/>
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
      <Setter Property=""FontFamily"" Value=""Segoe MDL2 Assets""/>
      <Setter Property=""FontSize"" Value=""10""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{DynamicResource Chip}""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key=""CaptionClose"" TargetType=""Button"" BasedOn=""{StaticResource CaptionButton}"">
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{DynamicResource Danger}""/>
            <Setter Property=""Foreground"" Value=""White""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 滚动条：方角，墨色 thumb。滚动条是工具不是内容，用中性灰不用信号色。
         (1) 绝不能给 ScrollBar 设 Width/Height —— 会把整条压成方块，厚度只在 Orientation Trigger 里给。
         (2) Track 必须有 Decrease/IncreaseRepeatButton，否则点轨道不翻页。
         (3) 纵向必须 IsDirectionReversed=True，否则往下滚滑块往上走。
         (4) Thumb 要 MinHeight，内容长时不能缩成一条线。 -->
    <Style TargetType=""ScrollBar"">
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ScrollBar"">
        <Grid x:Name=""Bg"" Background=""Transparent"">
          <Track x:Name=""PART_Track"" Focusable=""False"" IsDirectionReversed=""False"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""ScrollBar.PageUpCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb x:Name=""Thumb"" Background=""#C2C6CE"">
                <Thumb.Template><ControlTemplate TargetType=""Thumb"">
                  <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" SnapsToDevicePixels=""True""/>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""Bd"" Property=""Background"" Value=""#9AA0AA""/>
                    </Trigger>
                    <Trigger Property=""IsDragging"" Value=""True"">
                      <Setter TargetName=""Bd"" Property=""Background"" Value=""#868C96""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate></Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""ScrollBar.PageDownCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""Orientation"" Value=""Horizontal"">
            <Setter Property=""Width"" Value=""Auto""/>
            <Setter Property=""Height"" Value=""10""/>
            <Setter TargetName=""PART_Track"" Property=""Height"" Value=""10""/>
            <Setter TargetName=""PART_Track"" Property=""VerticalAlignment"" Value=""Bottom""/>
            <Setter TargetName=""Thumb"" Property=""Height"" Value=""10""/>
            <Setter TargetName=""Thumb"" Property=""MinWidth"" Value=""28""/>
            <Setter TargetName=""Thumb"" Property=""VerticalAlignment"" Value=""Center""/>
          </Trigger>
          <Trigger Property=""Orientation"" Value=""Vertical"">
            <Setter TargetName=""PART_Track"" Property=""IsDirectionReversed"" Value=""True""/>
            <Setter TargetName=""PART_Track"" Property=""Width"" Value=""10""/>
            <Setter TargetName=""PART_Track"" Property=""HorizontalAlignment"" Value=""Right""/>
            <Setter TargetName=""Thumb"" Property=""Width"" Value=""10""/>
            <Setter TargetName=""Thumb"" Property=""MinHeight"" Value=""28""/>
            <Setter TargetName=""Thumb"" Property=""HorizontalAlignment"" Value=""Right""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""Bg"" Property=""Opacity"" Value=""0.3""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 下拉框：直角、1px 细线、细折角箭头 + 一条竖分隔。
         ⚠ IsEditable=True 时隐式 TextBox 样式会钻进 PART_EditableTextBox 变成「框中框」，
           所以内部再套一套透明模板（本地 Setter 打不过样式里的 Template）。
         ⚠ 下拉面板不用投影，用 1px 细线撑起层次。
         状态分工：悬停换线色和底色（中性），展开才上主题色、箭头翻面 ——
         主题色始终只表示「当前 / 展开」，不参与悬停。 -->
    <Style TargetType=""ComboBox"">
      <Setter Property=""Height"" Value=""34""/>
      <Setter Property=""MinWidth"" Value=""120""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""Background"" Value=""{DynamicResource Panel}""/>
      <Setter Property=""BorderBrush"" Value=""#C9CED6""/>
      <Setter Property=""BorderThickness"" Value=""1""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ComboBox"">
        <Grid>
          <Border x:Name=""bg"" Background=""{DynamicResource Panel}""
                  BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}""
                  CornerRadius=""0"" SnapsToDevicePixels=""True"">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""1""/>
                <ColumnDefinition Width=""28""/>
              </Grid.ColumnDefinitions>
              <ContentPresenter x:Name=""selBox"" Grid.Column=""0"" Margin=""11,0,8,0""
                                Content=""{TemplateBinding SelectionBoxItem}""
                                VerticalAlignment=""Center"" IsHitTestVisible=""False""/>
              <TextBox x:Name=""PART_EditableTextBox"" Grid.Column=""0"" Margin=""11,0,8,0""
                       VerticalAlignment=""Center"" Visibility=""Collapsed""
                       Background=""Transparent"" BorderThickness=""0"" Padding=""0""
                       FontSize=""{TemplateBinding FontSize}""
                       Foreground=""{TemplateBinding Foreground}""
                       CaretBrush=""{DynamicResource Ink}""
                       IsReadOnly=""{Binding IsReadOnly, RelativeSource={RelativeSource TemplatedParent}}"">
                <TextBox.Resources>
                  <Style TargetType=""TextBox"">
                    <Setter Property=""Height"" Value=""Auto""/>
                    <Setter Property=""Padding"" Value=""0""/>
                    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TextBox"">
                      <Border Background=""Transparent"" BorderThickness=""0"" Padding=""0"">
                        <ScrollViewer x:Name=""PART_ContentHost"" VerticalAlignment=""Center"" Focusable=""False""/>
                      </Border>
                    </ControlTemplate></Setter.Value></Setter>
                  </Style>
                </TextBox.Resources>
              </TextBox>
              <Border Grid.Column=""1"" Width=""1"" Height=""14"" VerticalAlignment=""Center""
                      Background=""{DynamicResource Rule}""/>
              <!-- 箭头用细折角而不是 8x4 实心三角：实心块和整屏的 1px 细线语言打架。
                   展开时换一套 Points 而不是旋转 Path，省得半像素发虚。 -->
              <ToggleButton x:Name=""tg"" Grid.Column=""2"" Focusable=""False"" ClickMode=""Press"" Width=""28""
                            IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
                <ToggleButton.Template><ControlTemplate TargetType=""ToggleButton"">
                  <Border Background=""Transparent"">
                    <Grid HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
                      <Polyline x:Name=""arDown"" Points=""0,0 4.2,4.2 8.4,0"" Stroke=""{DynamicResource Ink2}""
                                StrokeThickness=""1.4"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""
                                StrokeLineJoin=""Round"" SnapsToDevicePixels=""True""/>
                      <Polyline x:Name=""arUp"" Points=""0,4.2 4.2,0 8.4,4.2"" Stroke=""{DynamicResource AccentSolid}""
                                StrokeThickness=""1.4"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""
                                StrokeLineJoin=""Round"" SnapsToDevicePixels=""True"" Visibility=""Collapsed""/>
                    </Grid>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""arDown"" Property=""Stroke"" Value=""{DynamicResource Ink}""/>
                    </Trigger>
                    <Trigger Property=""IsChecked"" Value=""True"">
                      <Setter TargetName=""arDown"" Property=""Visibility"" Value=""Collapsed""/>
                      <Setter TargetName=""arUp"" Property=""Visibility"" Value=""Visible""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
            </Grid>
          </Border>
          <Popup x:Name=""PART_Popup"" IsOpen=""{TemplateBinding IsDropDownOpen}""
                 Placement=""Bottom"" AllowsTransparency=""True"" Focusable=""False""
                 PopupAnimation=""Fade"" VerticalOffset=""3"">
            <Border Background=""{DynamicResource Panel}"" BorderBrush=""#C9CED6"" BorderThickness=""1""
                    CornerRadius=""0"" MinWidth=""{TemplateBinding ActualWidth}"">
              <ScrollViewer MaxHeight=""280""><ItemsPresenter/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsEditable"" Value=""True"">
            <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible""/>
            <Setter TargetName=""selBox"" Property=""Visibility"" Value=""Hidden""/>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bg"" Property=""Background"" Value=""#FAFAFA""/>
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""#8A8F98""/>
          </Trigger>
          <Trigger Property=""IsKeyboardFocusWithin"" Value=""True"">
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""#8A8F98""/>
          </Trigger>
          <!-- 展开态放最后：它要压过悬停和键盘焦点 -->
          <Trigger Property=""IsDropDownOpen"" Value=""True"">
            <Setter TargetName=""bg"" Property=""Background"" Value=""{DynamicResource Panel}""/>
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""{DynamicResource AccentSolid}""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""bg"" Property=""Opacity"" Value=""0.5""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType=""ComboBoxItem"">
      <Setter Property=""Padding"" Value=""11,8""/>
      <Setter Property=""FontSize"" Value=""13""/>
      <Setter Property=""MinHeight"" Value=""34""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ComboBoxItem"">
        <Grid MinHeight=""34"">
          <Border x:Name=""b"" Background=""Transparent""/>
          <!-- 选中：浅主题底整层盖住悬停灰，再叠 3px 竖条。
               竖条是覆盖层不是布局元素，选中前后文字不会左右跳。 -->
          <Border x:Name=""selBg"" Background=""{DynamicResource AccentSoft}"" Visibility=""Collapsed""/>
          <Border x:Name=""bar"" Width=""3"" HorizontalAlignment=""Left"" IsHitTestVisible=""False""
                  Background=""{DynamicResource AccentSolid}"" Visibility=""Collapsed""/>
          <ContentPresenter VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsHighlighted"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{DynamicResource Chip}""/>
          </Trigger>
          <!-- 选中：主题底 + 左侧 3px 条 + 主题字 + 加粗，四种线索同时出现（不靠颜色单一线索） -->
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""selBg"" Property=""Visibility"" Value=""Visible""/>
            <Setter TargetName=""bar"" Property=""Visibility"" Value=""Visible""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource AccentInk}""/>
            <Setter Property=""FontWeight"" Value=""Bold""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 导航项：当前项整块信号色填充 + 右下切角 + 加粗，三种线索同时出现 -->
    <Style x:Key=""NavItem"" TargetType=""ListBoxItem"">
      <Setter Property=""Padding"" Value=""0""/>
      <Setter Property=""Margin"" Value=""8,1""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem"">
        <Grid Height=""38"">
          <Border x:Name=""b"" Background=""Transparent""/>
          <!-- 选中填充走叠层淡入：整块色一闪而现会显得「跳」，淡入才像「落下来」 -->
          <Border x:Name=""sel"" Background=""{DynamicResource Accent}"" BorderBrush=""#1FFFFFFF""
                  BorderThickness=""1"" Opacity=""0"" IsHitTestVisible=""False""/>
          <ContentPresenter Margin=""20,0,12,0"" VerticalAlignment=""Center""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{DynamicResource Chip}""/>
          </Trigger>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter Property=""Foreground"" Value=""{DynamicResource OnAccent}""/>
            <Setter Property=""FontWeight"" Value=""Bold""/>
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""sel"" Storyboard.TargetProperty=""Opacity""
                                 To=""1"" Duration=""0:0:0.16"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""sel"" Storyboard.TargetProperty=""Opacity""
                                 To=""0"" Duration=""0:0:0.14""/>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""NavHeader"" TargetType=""ListBoxItem"">
      <Setter Property=""IsEnabled"" Value=""False""/>
      <Setter Property=""Focusable"" Value=""False""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem"">
        <TextBlock Text=""{TemplateBinding Content}"" FontSize=""10.5"" FontWeight=""SemiBold""
                   Foreground=""{DynamicResource Ink2}"" Margin=""20,16,0,6""/>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 滑块：2px 灰轨 + 已填充段主题色 + 方形色块；不用胶囊也不用圆点 -->
    <Style x:Key=""WinSlider"" TargetType=""Slider"">
      <Setter Property=""Height"" Value=""26""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Slider"">
        <Grid VerticalAlignment=""Center"">
          <Border Height=""2"" Background=""{DynamicResource Rule}""/>
          <Track x:Name=""PART_Track"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""Slider.DecreaseLarge"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""{DynamicResource AccentSolid}"" Height=""2""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""Slider.IncreaseLarge"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent"" Height=""2""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Width=""5"" Height=""24"" Cursor=""Hand"">
                <Thumb.Template><ControlTemplate TargetType=""Thumb"">
                  <Border x:Name=""tb"" Background=""{DynamicResource AccentSolid}"" SnapsToDevicePixels=""True""
                          RenderTransformOrigin=""0.5,0.5"">
                    <Border.RenderTransform><ScaleTransform x:Name=""ths"" ScaleX=""1"" ScaleY=""1""/></Border.RenderTransform>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Trigger.EnterActions>
                        <BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName=""ths"" Storyboard.TargetProperty=""ScaleY""
                                           To=""1.22"" Duration=""0:0:0.12"">
                            <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                          </DoubleAnimation>
                          <DoubleAnimation Storyboard.TargetName=""ths"" Storyboard.TargetProperty=""ScaleX""
                                           To=""1.6"" Duration=""0:0:0.12"">
                            <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                          </DoubleAnimation>
                        </Storyboard></BeginStoryboard>
                      </Trigger.EnterActions>
                      <Trigger.ExitActions>
                        <BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName=""ths"" Storyboard.TargetProperty=""ScaleY"" To=""1"" Duration=""0:0:0.12""/>
                          <DoubleAnimation Storyboard.TargetName=""ths"" Storyboard.TargetProperty=""ScaleX"" To=""1"" Duration=""0:0:0.12""/>
                        </Storyboard></BeginStoryboard>
                      </Trigger.ExitActions>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate></Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 开关：方角轨道 + 方块滑块；打开 = 主题色填充 + 滑块滑到右侧。
         开关读的是「状态」，和主按钮同一套填充色 —— 只有它还是墨色，界面就成了两套皮。 -->
    <Style x:Key=""ToggleSwitch"" TargetType=""CheckBox"">
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""CheckBox"">
        <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
          <Grid Width=""38"" Height=""18"" VerticalAlignment=""Center"">
            <Border x:Name=""track"" Background=""#C9CED6""/>
            <Border x:Name=""trackOn"" Background=""{DynamicResource AccentSolid}""
                    BorderBrush=""#1FFFFFFF"" BorderThickness=""1"" Opacity=""0""/>
            <Border x:Name=""knob"" Width=""12"" Height=""12"" HorizontalAlignment=""Left""
                    Margin=""3,0,0,0"" Background=""{DynamicResource Panel}"">
              <Border.RenderTransform><TranslateTransform x:Name=""knobX"" X=""0""/></Border.RenderTransform>
            </Border>
          </Grid>
          <ContentPresenter VerticalAlignment=""Center"" Margin=""10,0,0,0"" RecognizesAccessKey=""True""/>
        </StackPanel>
        <ControlTemplate.Triggers>
          <!-- 打开：滑块从左侧滑到右侧（20px = 38 - 3 - 12 - 3），轨道墨色淡入。
               不用 Setter 换 HorizontalAlignment —— 那样是瞬移，没有过程。 -->
          <Trigger Property=""IsChecked"" Value=""True"">
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""knobX"" Storyboard.TargetProperty=""X"" To=""20"" Duration=""0:0:0.16"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""trackOn"" Storyboard.TargetProperty=""Opacity"" To=""1"" Duration=""0:0:0.16""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""knobX"" Storyboard.TargetProperty=""X"" To=""0"" Duration=""0:0:0.16"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""trackOn"" Storyboard.TargetProperty=""Opacity"" To=""0"" Duration=""0:0:0.14""/>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""track"" Property=""Opacity"" Value=""0.88""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Opacity"" Value=""0.5""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 右栏入口是「列表」，不是棋盘：一行一条，编号 │ 标题 + 一句说明 │ ›。
         整行铺主题色（换主题色时整列自动跟着变），未选中压暗 30%，当前页恢复全亮 + 左边缘白条。 -->
    <Style x:Key=""TileButton"" TargetType=""ToggleButton"">
      <Setter Property=""Background"" Value=""{DynamicResource Accent}""/>
      <Setter Property=""Foreground"" Value=""#FFFFFF""/>
      <Setter Property=""BorderBrush"" Value=""#26FFFFFF""/>
      <Setter Property=""BorderThickness"" Value=""0,0,0,1""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ToggleButton"">
        <Grid ClipToBounds=""True"" RenderTransformOrigin=""0.5,0.5"" UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
          <Grid.RenderTransform><ScaleTransform x:Name=""ts"" ScaleX=""1"" ScaleY=""1""/></Grid.RenderTransform>
          <Border x:Name=""bg"" Background=""{TemplateBinding Background}""/>
          <!-- 未选中压暗：盖一层 34% 黑，而不是写死一个更深的蓝 —— 换主题色时整列自动跟着变 -->
          <Border x:Name=""dim"" Background=""#57000000"" IsHitTestVisible=""False""/>
          <Border x:Name=""hoverFill"" Background=""#1AFFFFFF"" Opacity=""0"" IsHitTestVisible=""False""/>
          <!-- 当前页：左边缘 3px 白条，从中间展开 -->
          <Border x:Name=""selBar"" Width=""3"" HorizontalAlignment=""Left"" Background=""#FFFFFF""
                  RenderTransformOrigin=""0.5,0.5"" IsHitTestVisible=""False"">
            <Border.RenderTransform><ScaleTransform x:Name=""sbs"" ScaleY=""0""/></Border.RenderTransform>
          </Border>
          <Grid Margin=""15,0,13,0"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition x:Name=""numCol"" Width=""26""/><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name=""num"" Grid.Column=""0"" Text=""{TemplateBinding Tag}"" FontSize=""11""
                       FontWeight=""Bold"" VerticalAlignment=""Center"" Opacity=""0.5""/>
            <!-- 图标位：列宽 Auto，折叠时整列归零，所以不用为别的方案改列宽 -->
            <Path x:Name=""ico"" Grid.Column=""1"" Width=""16"" Height=""16"" Stretch=""Uniform""
                  VerticalAlignment=""Center"" Margin=""0,0,10,0"" Visibility=""Collapsed""
                  StrokeThickness=""1.3"" StrokeLineJoin=""Round""
                  StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""/>
            <ContentPresenter x:Name=""cp"" Grid.Column=""2"" VerticalAlignment=""Center"">
              <ContentPresenter.RenderTransform><TranslateTransform x:Name=""ct"" X=""0""/></ContentPresenter.RenderTransform>
            </ContentPresenter>
            <Path x:Name=""arrow"" Grid.Column=""3"" VerticalAlignment=""Center""
                  Data=""M0,0 L4,4.5 L0,9"" Stroke=""#FFFFFF"" StrokeThickness=""1.5""
                  Width=""4"" Height=""9"" Stretch=""None"" Opacity=""0.45""/>
          </Grid>
          <!-- 行分隔线压在最上层：放在压暗层底下会被一起压暗 -->
          <Border BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}""
                  IsHitTestVisible=""False""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""hoverFill"" Storyboard.TargetProperty=""Opacity""
                                 To=""1"" Duration=""0:0:0.12""/>
                <DoubleAnimation Storyboard.TargetName=""ct"" Storyboard.TargetProperty=""X""
                                 To=""3"" Duration=""0:0:0.18"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""arrow"" Storyboard.TargetProperty=""Opacity""
                                 To=""0.9"" Duration=""0:0:0.18""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""hoverFill"" Storyboard.TargetProperty=""Opacity""
                                 To=""0"" Duration=""0:0:0.16""/>
                <DoubleAnimation Storyboard.TargetName=""ct"" Storyboard.TargetProperty=""X""
                                 To=""0"" Duration=""0:0:0.20"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""arrow"" Storyboard.TargetProperty=""Opacity""
                                 To=""0.45"" Duration=""0:0:0.18""/>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
          <Trigger Property=""IsChecked"" Value=""True"">
            <Setter Property=""FontWeight"" Value=""Bold""/>
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""dim"" Storyboard.TargetProperty=""Opacity""
                                 To=""0"" Duration=""0:0:0.20"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""sbs"" Storyboard.TargetProperty=""ScaleY""
                                 To=""1"" Duration=""0:0:0.22"">
                  <DoubleAnimation.EasingFunction><CubicEase EasingMode=""EaseOut""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""num"" Storyboard.TargetProperty=""Opacity""
                                 To=""1"" Duration=""0:0:0.20""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""dim"" Storyboard.TargetProperty=""Opacity""
                                 To=""1"" Duration=""0:0:0.16""/>
                <DoubleAnimation Storyboard.TargetName=""sbs"" Storyboard.TargetProperty=""ScaleY""
                                 To=""0"" Duration=""0:0:0.14""/>
                <DoubleAnimation Storyboard.TargetName=""num"" Storyboard.TargetProperty=""Opacity""
                                 To=""0.5"" Duration=""0:0:0.16""/>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ts"" Storyboard.TargetProperty=""ScaleX""
                                 To=""0.985"" Duration=""0:0:0.09""/>
                <DoubleAnimation Storyboard.TargetName=""ts"" Storyboard.TargetProperty=""ScaleY""
                                 To=""0.985"" Duration=""0:0:0.09""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""ts"" Storyboard.TargetProperty=""ScaleX""
                                 To=""1"" Duration=""0:0:0.24"">
                  <DoubleAnimation.EasingFunction><BackEase EasingMode=""EaseOut"" Amplitude=""0.5""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
                <DoubleAnimation Storyboard.TargetName=""ts"" Storyboard.TargetProperty=""ScaleY""
                                 To=""1"" Duration=""0:0:0.24"">
                  <DoubleAnimation.EasingFunction><BackEase EasingMode=""EaseOut"" Amplitude=""0.5""/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 高度预设：方角小格，选中整块墨底白字，不加切角（一排里五个切角会碎） -->
    <Style x:Key=""PresetButton"" TargetType=""Button"">
      <Setter Property=""Height"" Value=""32""/>
      <Setter Property=""MinWidth"" Value=""56""/>
      <Setter Property=""Padding"" Value=""10,0""/>
      <Setter Property=""Background"" Value=""{DynamicResource Panel}""/>
      <Setter Property=""BorderBrush"" Value=""#C9CED6""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
      <Setter Property=""FontSize"" Value=""12""/>
      <Setter Property=""FontWeight"" Value=""Bold""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Grid UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
          <Border x:Name=""bg"" Background=""{TemplateBinding Background}""
                  BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""1""/>
          <Border x:Name=""hoverLine"" BorderBrush=""{DynamicResource Ink}"" BorderThickness=""1"" Opacity=""0""/>
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
          <Border VerticalAlignment=""Stretch"" IsHitTestVisible=""False""
                  BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""0,0,0,2""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Trigger.EnterActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""hoverLine"" Storyboard.TargetProperty=""Opacity""
                                 To=""1"" Duration=""0:0:0.14""/>
              </Storyboard></BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard><Storyboard>
                <DoubleAnimation Storyboard.TargetName=""hoverLine"" Storyboard.TargetProperty=""Opacity""
                                 To=""0"" Duration=""0:0:0.12""/>
              </Storyboard></BeginStoryboard>
            </Trigger.ExitActions>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 返回主菜单：纯文字，不给标题栏添重量 -->
    <Style x:Key=""BackLink"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""Foreground"" Value=""{DynamicResource Ink2}""/>
      <Setter Property=""FontSize"" Value=""12""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border Background=""Transparent"" Padding=""10,6"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter Property=""Foreground"" Value=""{DynamicResource Ink}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height=""0""/>
      <RowDefinition Height=""52""/>
      <RowDefinition Height=""*""/>
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <Grid Grid.Row=""1"" Background=""{DynamicResource Desk}"">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/>
      </Grid.ColumnDefinitions>
      <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""16,0,0,0"">
        <Image x:Name=""imgIcon"" Width=""16"" Height=""16"" RenderOptions.BitmapScalingMode=""HighQuality""/>
        <TextBlock Text=""// Q PET"" Style=""{StaticResource Eyebrow}"" Margin=""10,1,0,0"" VerticalAlignment=""Center""/>
        <Border Width=""1"" Height=""12"" Background=""{DynamicResource Rule}"" Margin=""10,0,10,0""/>
        <TextBlock Text=""设置"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource Ink}"" VerticalAlignment=""Center""/>
        <!-- 运行读数：绿点 + 状态 + 落点 + 当前角色 + 语音条数，一行讲完，省掉左栏四条 -->
        <Border Width=""1"" Height=""12"" Background=""{DynamicResource Rule}"" Margin=""14,0,13,0""/>
        <Border x:Name=""bdRunDot"" Width=""6"" Height=""6"" Background=""#12A150"" VerticalAlignment=""Center""/>
        <TextBlock x:Name=""txtRunState"" Text=""RUN"" Style=""{StaticResource Eyebrow}"" Margin=""7,1,0,0"" VerticalAlignment=""Center""/>
        <TextBlock x:Name=""txtRunPos"" Text=""—"" FontSize=""11.5"" FontWeight=""SemiBold"" Margin=""9,0,0,0"" VerticalAlignment=""Center""/>
        <Border Width=""1"" Height=""12"" Background=""{DynamicResource Rule}"" Margin=""14,0,13,0""/>
        <TextBlock Text=""CHAR"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center""/>
        <TextBlock x:Name=""txtTopChar"" Text=""—"" FontSize=""11.5"" FontWeight=""SemiBold"" Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
        <Border Width=""1"" Height=""12"" Background=""{DynamicResource Rule}"" Margin=""14,0,13,0""/>
        <TextBlock Text=""VOICE"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center""/>
        <TextBlock x:Name=""txtTopVoice"" Text=""0"" FontSize=""11.5"" FontWeight=""SemiBold"" Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
      </StackPanel>
      <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center""
                  Margin=""0,0,10,0"" WindowChrome.IsHitTestVisibleInChrome=""True"">
        <Button x:Name=""btnHome"" Content=""角色枢纽 &#x203A;"" Style=""{StaticResource BackLink}"" Visibility=""Collapsed""/>
      </StackPanel>
      <StackPanel Grid.Column=""2"" Orientation=""Horizontal"" WindowChrome.IsHitTestVisibleInChrome=""True"">
        <Button x:Name=""btnMin"" Content=""&#xE921;"" Style=""{StaticResource CaptionButton}"" FontSize=""9""/>
        <Button x:Name=""btnMax"" Content=""&#xE922;"" Style=""{StaticResource CaptionButton}"" FontSize=""9""/>
        <Button x:Name=""btnClose"" Content=""&#xE8BB;"" Style=""{StaticResource CaptionClose}""
                WindowChrome.IsHitTestVisibleInChrome=""True""/>
      </StackPanel>
      <Border VerticalAlignment=""Bottom"" Height=""1"" Background=""{DynamicResource Rule}"" Grid.ColumnSpan=""3""/>
    </Grid>

    <!-- 外层只做布局（导航 | 内容），不做滚动：真滚动的只有右侧 scroller。
         双层 ScrollViewer 会让滚轮空转，这是踩过的坑。 -->
    <Grid Grid.Row=""2"">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width=""216""/>
        <ColumnDefinition Width=""*""/>
      </Grid.ColumnDefinitions>

      <!-- 左导航：白底，和灰纸内容区拉开层次 -->
      <Border x:Name=""navHost"" Grid.Column=""0"" Background=""{DynamicResource Desk}""
              BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,0,1,0"" Visibility=""Collapsed"">
        <StackPanel Margin=""0,16,0,0"">
          <TextBlock Text=""// SECTIONS"" Style=""{StaticResource Eyebrow}"" Margin=""20,0,0,8""/>
          <ListBox x:Name=""nav"" BorderThickness=""0"" Background=""Transparent""
                   ItemContainerStyle=""{StaticResource NavItem}"">
            <ListBoxItem Content=""// 常规"" Style=""{StaticResource NavHeader}""/>
            <ListBoxItem Content=""气泡"" Tag=""bubble""/>
            <ListBoxItem Content=""通用设置"" Tag=""ui""/>
            <ListBoxItem Content=""// 角色"" Style=""{StaticResource NavHeader}""/>
            <ListBoxItem Content=""形象与大小"" Tag=""char""/>
            <ListBoxItem Content=""动画"" Tag=""anim""/>
            <ListBoxItem Content=""语音"" Tag=""voice""/>
            <ListBoxItem Content=""导入"" Tag=""import""/>
            <ListBoxItem Content=""// 数据"" Style=""{StaticResource NavHeader}""/>
            <ListBoxItem Content=""使用统计"" Tag=""stats""/>
            <ListBoxItem Content=""// 其他"" Style=""{StaticResource NavHeader}""/>
            <ListBoxItem Content=""关于"" Tag=""about""/>
          </ListBox>
        </StackPanel>
      </Border>

      <!-- 右侧内容 -->

      <!-- 首页：左摘要 / 中角色枢纽 / 右八页入口。
           和导航、内容区同格叠加，进详情页时整块隐藏。
           整屏白底是刻意的：磁贴的右下切角要盖一块「父容器同色」小三角，父容器不是白的就露馅。 -->
      <Grid x:Name=""pageHome"" Grid.Column=""0"" Grid.ColumnSpan=""2"">
        <Grid.ColumnDefinitions>
          <!-- 两栏宽度由代码按模式改：枢纽 216/*/348，内容页 0/*/300（左摘要收起、右栏收窄） -->
          <ColumnDefinition x:Name=""colSummary"" Width=""216""/>
          <ColumnDefinition Width=""*""/>
          <ColumnDefinition x:Name=""colTiles"" Width=""348""/>
        </Grid.ColumnDefinitions>

        <!-- 左：当前配置摘要。五条读数 + 调色板 + 版本脚注。
             每条 = 编号 / 名称 / 英文码 / 数值 / 说明 / 六格刻度；刻度不是装饰，是把数值压成可比的长度。 -->
        <Border x:Name=""summaryHost"" Grid.Column=""0"" Background=""{DynamicResource Desk}""
                BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,0,1,0"">
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/><RowDefinition Height=""*""/><RowDefinition Height=""Auto""/>
            </Grid.RowDefinitions>

            <Grid Grid.Row=""0"" Height=""34"">
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""14,0,0,0"">
                <Border Width=""3"" Height=""11"" Background=""{DynamicResource Accent}""/>
                <TextBlock Text=""SUMMARY"" Style=""{StaticResource Eyebrow}"" Margin=""9,1,0,0"" VerticalAlignment=""Center""/>
              </StackPanel>
              <TextBlock Text=""05"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Margin=""0,0,14,0"" Opacity=""0.5""/>
              <Border VerticalAlignment=""Bottom"" Height=""1"" Background=""{DynamicResource Rule}""/>
            </Grid>

            <ScrollViewer Grid.Row=""1"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled""
                          Focusable=""False"">
              <StackPanel>
        <Border BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""14,11,14,13"">
          <StackPanel>
            <Grid>
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                <TextBlock Text=""01"" FontSize=""11"" FontWeight=""Bold"" Foreground=""{DynamicResource Accent}""/>
                <TextBlock Text=""当前角色"" FontSize=""12.5"" FontWeight=""SemiBold"" Margin=""8,0,0,0""/>
              </StackPanel>
              <TextBlock Text=""CHAR"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Opacity=""0.5""/>
            </Grid>
            <StackPanel Orientation=""Horizontal"" Margin=""0,7,0,0"">
              <TextBlock x:Name=""txtHomeChar"" Text=""—"" FontSize=""24"" FontWeight=""Bold"" Foreground=""{DynamicResource Ink}""
                         TextTrimming=""CharacterEllipsis""/>
            </StackPanel>
            <TextBlock x:Name=""txtHomeCharNote"" Text=""0 条语音 │ 0 帧动画 │ 立绘 1"" FontSize=""11"" Foreground=""{DynamicResource Ink2}"" Margin=""0,3,0,0""
                       TextTrimming=""CharacterEllipsis""/>
          <Grid x:Name=""gsChar"" Height=""4"" Margin=""0,9,0,0"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column=""0"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""1"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""2"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""3"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""4"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""5"" Background=""{DynamicResource Rule}""/>
          </Grid>
          </StackPanel>
        </Border>
        <Border BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""14,11,14,13"">
          <StackPanel>
            <Grid>
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                <TextBlock Text=""02"" FontSize=""11"" FontWeight=""Bold"" Foreground=""{DynamicResource Accent}""/>
                <TextBlock Text=""形象高度"" FontSize=""12.5"" FontWeight=""SemiBold"" Margin=""8,0,0,0""/>
              </StackPanel>
              <TextBlock Text=""PX"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Opacity=""0.5""/>
            </Grid>
            <StackPanel Orientation=""Horizontal"" Margin=""0,7,0,0"">
              <TextBlock x:Name=""txtHomeSize"" Text=""219"" FontSize=""24"" FontWeight=""Bold"" Foreground=""{DynamicResource Ink}""
                         TextTrimming=""CharacterEllipsis""/>
              <TextBlock Text=""PX"" FontSize=""10.5"" FontWeight=""Bold""
                         Foreground=""{DynamicResource Ink2}"" Margin=""5,0,0,5"" VerticalAlignment=""Bottom""/>
            </StackPanel>
            <TextBlock Text=""屏幕上从头顶到脚底"" FontSize=""11"" Foreground=""{DynamicResource Ink2}"" Margin=""0,3,0,0""
                       TextTrimming=""CharacterEllipsis""/>
          <Grid x:Name=""gsSize"" Height=""4"" Margin=""0,9,0,0"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column=""0"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""1"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""2"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""3"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""4"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""5"" Background=""{DynamicResource Rule}""/>
          </Grid>
          </StackPanel>
        </Border>
        <Border BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""14,11,14,13"">
          <StackPanel>
            <Grid>
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                <TextBlock Text=""03"" FontSize=""11"" FontWeight=""Bold"" Foreground=""{DynamicResource Accent}""/>
                <TextBlock Text=""气泡"" FontSize=""12.5"" FontWeight=""SemiBold"" Margin=""8,0,0,0""/>
              </StackPanel>
              <TextBlock Text=""ENABLED"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Opacity=""0.5""/>
            </Grid>
            <StackPanel Orientation=""Horizontal"" Margin=""0,7,0,0"">
              <TextBlock x:Name=""txtHomeBubble"" Text=""79"" FontSize=""24"" FontWeight=""Bold"" Foreground=""{DynamicResource Ink}""
                         TextTrimming=""CharacterEllipsis""/>
              <TextBlock x:Name=""uBubble"" Text=""PX"" FontSize=""10.5"" FontWeight=""Bold""
                         Foreground=""{DynamicResource Ink2}"" Margin=""5,0,0,5"" VerticalAlignment=""Bottom""/>
            </StackPanel>
            <TextBlock Text=""字体 │ 颜色 │ 频率"" FontSize=""11"" Foreground=""{DynamicResource Ink2}"" Margin=""0,3,0,0""
                       TextTrimming=""CharacterEllipsis""/>
          <Grid x:Name=""gsBub"" Height=""4"" Margin=""0,9,0,0"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column=""0"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""1"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""2"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""3"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""4"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""5"" Background=""{DynamicResource Rule}""/>
          </Grid>
          </StackPanel>
        </Border>
        <Border BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""14,11,14,13"">
          <StackPanel>
            <Grid>
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                <TextBlock Text=""04"" FontSize=""11"" FontWeight=""Bold"" Foreground=""{DynamicResource Accent}""/>
                <TextBlock Text=""音量"" FontSize=""12.5"" FontWeight=""SemiBold"" Margin=""8,0,0,0""/>
              </StackPanel>
              <TextBlock Text=""VOL"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Opacity=""0.5""/>
            </Grid>
            <StackPanel Orientation=""Horizontal"" Margin=""0,7,0,0"">
              <TextBlock x:Name=""txtHomeVol"" Text=""20"" FontSize=""24"" FontWeight=""Bold"" Foreground=""{DynamicResource Ink}""
                         TextTrimming=""CharacterEllipsis""/>
              <TextBlock Text=""%"" FontSize=""10.5"" FontWeight=""Bold""
                         Foreground=""{DynamicResource Ink2}"" Margin=""5,0,0,5"" VerticalAlignment=""Bottom""/>
            </StackPanel>
            <TextBlock x:Name=""txtHomeVolNote"" Text=""台词与音效"" FontSize=""11"" Foreground=""{DynamicResource Ink2}"" Margin=""0,3,0,0""
                       TextTrimming=""CharacterEllipsis""/>
          <Grid x:Name=""gsVol"" Height=""4"" Margin=""0,9,0,0"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
              <ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column=""0"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""1"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""2"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""3"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""4"" Background=""{DynamicResource Rule}"" Margin=""0,0,3,0""/>
            <Border Grid.Column=""5"" Background=""{DynamicResource Rule}""/>
          </Grid>
          </StackPanel>
        </Border>
        <Border BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""14,11,14,13"">
          <StackPanel>
            <Grid>
              <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                <TextBlock Text=""05"" FontSize=""11"" FontWeight=""Bold"" Foreground=""{DynamicResource Accent}""/>
                <TextBlock Text=""主题色"" FontSize=""12.5"" FontWeight=""SemiBold"" Margin=""8,0,0,0""/>
              </StackPanel>
              <TextBlock Text=""ACCENT"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Opacity=""0.5""/>
            </Grid>
            <StackPanel Orientation=""Horizontal"" Margin=""0,7,0,0"">
              <Border Width=""16"" Height=""16"" VerticalAlignment=""Center""
                      Background=""{DynamicResource Accent}""/>
              <TextBlock x:Name=""txtHomeTheme"" Text=""—"" FontSize=""20"" FontWeight=""Bold"" Margin=""8,0,0,0""
                         Foreground=""{DynamicResource Ink}""/>
            </StackPanel>
            <TextBlock Text=""当前项 │ 主按钮 │ 滑块"" FontSize=""11"" Foreground=""{DynamicResource Ink2}"" Margin=""0,3,0,0""
                       TextTrimming=""CharacterEllipsis""/>
          </StackPanel>
        </Border>
              </StackPanel>
            </ScrollViewer>

            <!-- 调色板 + 版本：把「这一版用哪三种色」和「这是哪一版」钉在最低处 -->
            <StackPanel Grid.Row=""2"">
              <Border Height=""1"" Background=""{DynamicResource Rule}""/>
              <Grid Margin=""14,8,14,10"">
                <TextBlock x:Name=""txtSumVer"" Text=""Q PET"" Style=""{StaticResource Eyebrow}"" Opacity=""0.6""/>
                <TextBlock Text=""LOCAL ONLY"" Style=""{StaticResource Eyebrow}"" Opacity=""0.4"" HorizontalAlignment=""Right""/>
              </Grid>
            </StackPanel>
          </Grid>
        </Border>

        <!-- 中：角色枢纽。中栏和内容页共用同一张「工作面」（纸色 + 斜纹），
             底板改用白板 —— 白板压纸面才立得住形状，Chip 压 Paper 对比只有 1.02 会糊。 -->
        <Grid x:Name=""midHost"" Grid.Column=""1"" Background=""{DynamicResource Paper}"">
          <!-- 枢纽视图：右侧磁贴点了某一页就收起，让位给下面的内容视口 -->
          <!-- 角色枢纽 = 整列立绘板：白板铺满中栏，立绘按比例吃满剩下的空间。
               眉标和底部操作条是叠在上层的两个窄条（不设 Background ⇒ 滚轮和拖动穿透到底下的板），
               所以「中间一列全是立绘」，控件只是浮在上面的一条细带。 -->
          <Grid x:Name=""hubView"" Margin=""0"">
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/><RowDefinition Height=""*""/><RowDefinition Height=""Auto""/>
            </Grid.RowDefinitions>

            <!-- 视口：铺满三行（白板 + 1px 线框），溢出部分裁在这里，
                 放大时不会压到上面的眉标和下面的操作条。 -->
            <Border x:Name=""hubViewport"" Grid.Row=""0"" Grid.RowSpan=""3""
                    Background=""{DynamicResource Panel}""
                    BorderBrush=""{DynamicResource Rule}"" BorderThickness=""1""
                    ClipToBounds=""True"" Cursor=""Hand""
                    ToolTip=""滚轮缩放立绘，按住左键拖动，双击复位"">
              <Grid x:Name=""hubPlate"" RenderTransformOrigin=""0.5,0.5"">
                <Grid.RenderTransform>
                  <TransformGroup>
                    <ScaleTransform x:Name=""hubScale"" ScaleX=""1"" ScaleY=""1""/>
                    <TranslateTransform x:Name=""hubShift"" X=""0"" Y=""0""/>
                  </TransformGroup>
                </Grid.RenderTransform>
                <Image x:Name=""imgHomeChar"" Margin=""26,42,26,66"" Stretch=""Uniform""/>
              </Grid>
            </Border>

            <!-- 上层：眉标行 + 底部操作条。整块不设 Background，空白处点击直接落到立绘板上 -->
            <Grid Grid.Row=""0"" Grid.RowSpan=""3"">
              <Grid.RowDefinitions>
                <RowDefinition Height=""Auto""/><RowDefinition Height=""*""/><RowDefinition Height=""Auto""/>
              </Grid.RowDefinitions>

              <Grid Grid.Row=""0"" Margin=""20,13,20,0"">
                <TextBlock Text=""// CURRENT CHARACTER"" Style=""{StaticResource Eyebrow}""
                           Opacity=""0.45"" VerticalAlignment=""Center""/>
                <TextBlock x:Name=""txtHubZoom"" Text=""×1.0"" FontSize=""10"" FontWeight=""Bold""
                           Foreground=""{DynamicResource Ink2}"" Background=""{DynamicResource Panel}""
                           Padding=""6,3,5,3"" HorizontalAlignment=""Right"" VerticalAlignment=""Center""
                           IsHitTestVisible=""False""/>
              </Grid>

              <Grid Grid.Row=""2"" Margin=""20,0,20,13"">
                <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                  <TextBlock Text=""状态 "" FontSize=""11"" Foreground=""{DynamicResource Ink2}""/>
                  <TextBlock x:Name=""txtHomeRun"" Text=""RUNNING"" FontSize=""11"" FontWeight=""SemiBold""/>
                  <TextBlock Text=""    位置 "" FontSize=""11"" Foreground=""{DynamicResource Ink2}""/>
                  <TextBlock x:Name=""txtHomePos"" Text=""—"" FontSize=""11"" FontWeight=""SemiBold""/>
                </StackPanel>
                <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"">
                  <Button x:Name=""btnHomeShow"" Content=""在桌面显示"" Style=""{StaticResource PrimaryButton}""/>
                  <Button x:Name=""btnHomeFolder"" Content=""打开角色文件夹"" Style=""{StaticResource SecondaryButton}""
                          Margin=""10,0,0,0""/>
                </StackPanel>
              </Grid>
            </Grid>
          </Grid>

          <Grid x:Name=""contentHost"" Grid.Column=""1"" Visibility=""Collapsed""
                Background=""{DynamicResource Paper}"">
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/><RowDefinition Height=""*""/><RowDefinition Height=""Auto""/>
            </Grid.RowDefinitions>
            <!-- 顶部仪表带：SECTION / 页名 / 角色 / 高度 / 帧数 / 语音 / 保存态。
                 每个读数都是「灰眉标 + 墨值」，扫一眼就知道现在是谁、多大、有没有动画。 -->
            <Border Grid.Row=""0"" Background=""{DynamicResource Paper}""
                    BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,0,0,1"" Padding=""24,0,20,0"">
              <Grid Height=""30"">
                <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                  <TextBlock Text=""SECTION"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center"" Opacity=""0.6""/>
                  <!-- 编号用墨：这是一条数据带，主题色只留给「未保存/已保存」，编号再上色就三处抢眼 -->
                  <TextBlock x:Name=""pbNum"" Text=""01"" FontSize=""11"" FontWeight=""Bold""
                             Foreground=""{DynamicResource Ink}"" Margin=""9,0,0,0"" VerticalAlignment=""Center""/>
                  <TextBlock x:Name=""pbName"" Text=""气泡"" FontSize=""12"" FontWeight=""Bold""
                             Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
                  <Border Width=""1"" Height=""10"" Background=""{DynamicResource Rule}"" Margin=""14,0""/>
                  <TextBlock Text=""CHAR"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center"" Opacity=""0.6""/>
                  <TextBlock x:Name=""pbChar"" Text=""—"" FontSize=""11.5"" FontWeight=""SemiBold""
                             Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
                  <Border Width=""1"" Height=""10"" Background=""{DynamicResource Rule}"" Margin=""14,0""/>
                  <TextBlock Text=""H"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center"" Opacity=""0.6""/>
                  <TextBlock x:Name=""pbSize"" Text=""—"" FontSize=""11.5"" FontWeight=""SemiBold""
                             Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
                  <Border Width=""1"" Height=""10"" Background=""{DynamicResource Rule}"" Margin=""14,0""/>
                  <TextBlock Text=""FRAMES"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Center"" Opacity=""0.6""/>
                  <TextBlock x:Name=""pbFrames"" Text=""—"" FontSize=""11.5"" FontWeight=""SemiBold""
                             Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
                </StackPanel>
                <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"">
                  <TextBlock x:Name=""pbState"" Text=""已保存"" FontSize=""10.5"" FontWeight=""SemiBold""
                             Foreground=""{DynamicResource Ink2}"" VerticalAlignment=""Center""/>
                  <Border x:Name=""pbDot"" Width=""5"" Height=""5"" Margin=""7,0,0,0"" Background=""#12A150""
                          VerticalAlignment=""Center""/>
                </StackPanel>
              </Grid>
            </Border>

            <ScrollViewer x:Name=""scroller"" Grid.Row=""1"" VerticalScrollBarVisibility=""Auto"" Padding=""32,20,28,24"">
              <StackPanel>
                <!-- 形象 -->
                <StackPanel x:Name=""pageChar"">
                  <StackPanel Orientation=""Horizontal"">
                    <TextBlock Text=""形象与大小"" Style=""{StaticResource H1}""/>
                    <TextBlock Text=""// CHARACTER / SCALE"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Bottom""
                               Margin=""14,0,0,9"" Opacity=""0.7""/>
                  </StackPanel>
                  <TextBlock Text=""选择角色，并决定她在桌面上占多大"" Style=""{StaticResource Sub}"" Margin=""0,6,0,0""/>
                  <Grid Height=""2"" Margin=""0,16,0,2"" SnapsToDevicePixels=""True"">
                    <Border Background=""#000000""/>
                  </Grid>

                  <!-- 角色：选中框用「粗左边 + 下边」的字段式外框，一眼看出这是可换的字段 -->
                  <Border Style=""{StaticResource Card}"">
                    <StackPanel>
                      <Grid>
                        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                        <StackPanel VerticalAlignment=""Center"">
                          <TextBlock Text=""角色"" Style=""{StaticResource H2}""/>
                          <TextBlock Text=""切换后立即换成这个角色的立绘与台词"" Style=""{StaticResource Sub}""/>
                        </StackPanel>
                        <Border Grid.Column=""1"" VerticalAlignment=""Center""
                                BorderBrush=""{DynamicResource AccentSolid}"" BorderThickness=""3,0,0,1"">
                          <ComboBox x:Name=""cmbChar"" Width=""252"" Height=""32"" BorderThickness=""0""/>
                        </Border>
                      </Grid>
                      <ContentControl Style=""{StaticResource DashedBox}"" Height=""150"" Margin=""0,12,0,0"">
                        <Grid>
                          <Image x:Name=""imgChar"" Stretch=""Uniform""/>
                          <TextBlock x:Name=""txtCharDim"" Text=""—"" Style=""{StaticResource Eyebrow}""
                                     HorizontalAlignment=""Left"" VerticalAlignment=""Top"" Opacity=""0.45""/>
                          <TextBlock Text=""PNG · ALPHA"" Style=""{StaticResource Eyebrow}""
                                     HorizontalAlignment=""Right"" VerticalAlignment=""Top"" Opacity=""0.45""/>
                        </Grid>
                      </ContentControl>
                    </StackPanel>
                  </Border>

                  <!-- 角色高度 -->
                  <Border Style=""{StaticResource Card}"">
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                      <StackPanel VerticalAlignment=""Center"">
                        <TextBlock Text=""角色高度"" Style=""{StaticResource H2}""/>
                        <TextBlock Text=""屏幕上从头顶到脚底占多少像素"" Style=""{StaticResource Sub}""/>
                      </StackPanel>
                      <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"">
                        <Slider x:Name=""sldSize"" Width=""190"" Minimum=""180"" Maximum=""400"" TickFrequency=""20""
                                Style=""{StaticResource WinSlider}"" VerticalAlignment=""Center""/>
                        <TextBlock x:Name=""lblSize"" Text=""219"" FontSize=""16"" FontWeight=""Bold"" Width=""40""
                                   TextAlignment=""Right"" VerticalAlignment=""Center"" Margin=""12,0,0,0""/>
                        <TextBlock Text=""PX"" Style=""{StaticResource Eyebrow}"" Opacity=""0.5""
                                   VerticalAlignment=""Center"" Margin=""5,1,0,0""/>
                      </StackPanel>
                    </Grid>
                  </Border>

                  <!-- 快速预设 -->
                  <Border Style=""{StaticResource Card}"">
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                      <StackPanel VerticalAlignment=""Center"">
                        <TextBlock Text=""快速预设"" Style=""{StaticResource H2}""/>
                        <TextBlock Text=""点一下套用常用高度"" Style=""{StaticResource Sub}""/>
                      </StackPanel>
                      <StackPanel x:Name=""sizePresets"" Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center""/>
                    </Grid>
                  </Border>

                  <!-- 立绘 -->
                  <Border Style=""{StaticResource Card}"">
                    <StackPanel>
                      <Grid>
                        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                        <StackPanel VerticalAlignment=""Center"">
                          <TextBlock Text=""立绘"" Style=""{StaticResource H2}""/>
                          <TextBlock Text=""透明 PNG，按高度等比缩放"" Style=""{StaticResource Sub}""/>
                        </StackPanel>
                        <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"">
                          <Button x:Name=""btnCharFolder"" Content=""文件夹"" Style=""{StaticResource SecondaryButton}""
                                  Height=""30"" Padding=""14,0,20,0""/>
                        </StackPanel>
                      </Grid>
                      <ContentControl Style=""{StaticResource DashedBox}"" Height=""236"" Margin=""0,12,0,0"">
                        <Grid>
                          <Image x:Name=""imgSize"" Stretch=""Uniform""/>
                          <TextBlock x:Name=""txtSizeDim"" Text=""—"" Style=""{StaticResource Eyebrow}""
                                     HorizontalAlignment=""Left"" VerticalAlignment=""Top"" Opacity=""0.45""/>
                          <TextBlock Text=""PNG · ALPHA"" Style=""{StaticResource Eyebrow}""
                                     HorizontalAlignment=""Right"" VerticalAlignment=""Top"" Opacity=""0.45""/>
                        </Grid>
                      </ContentControl>
                      <TextBlock x:Name=""lblSizeCap"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""—""/>
                    </StackPanel>
                  </Border>
                </StackPanel>

                <!-- 语音 -->
                <StackPanel x:Name=""pageVoice"" Visibility=""Collapsed"">
                  <StackPanel Orientation=""Horizontal"">
                    <TextBlock Text=""语音"" Style=""{StaticResource H1}""/>
                    <TextBlock Text=""// VOICE"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Bottom""
                               Margin=""14,0,0,9"" Opacity=""0.7""/>
                  </StackPanel>
                  <TextBlock Text=""点她时说什么、音量多大"" Style=""{StaticResource Sub}"" Margin=""0,4,0,0""/>
                  <Grid Height=""2"" Margin=""0,16,0,2"" SnapsToDevicePixels=""True"">
                    <Border Background=""#000000""/>
                  </Grid>
                  <StackPanel Margin=""0,12,0,0"">
                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""音效组"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""双击、拖拽这类动作音效的整套替换"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <ComboBox x:Name=""cmbSet"" Grid.Column=""1"" Width=""240"" VerticalAlignment=""Center""/>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""点击模式"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""点她时的说话方式"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <ComboBox x:Name=""cmbClickMode"" Grid.Column=""1"" Width=""180"" VerticalAlignment=""Center""/>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <CheckBox x:Name=""chkClick"" Content=""点击她时播放语音"" FontSize=""13"" Style=""{StaticResource ToggleSwitch}""
                                  ToolTip=""不勾选则只播放动画，不出声。""/>
                      </StackPanel>
                    </Border>

                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""音量"" Style=""{StaticResource H2}""/>
                            <TextBlock x:Name=""lblVoiceHint"" Text="""" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                            <Slider x:Name=""sldVol"" Width=""210"" Minimum=""0"" Maximum=""100"" TickFrequency=""5"" Style=""{StaticResource WinSlider}""/>
                            <TextBlock x:Name=""lblVol"" Text=""100%"" FontSize=""11"" Foreground=""{DynamicResource Ink2}""
                                       FontWeight=""SemiBold"" HorizontalAlignment=""Right"" Margin=""0,2,0,0""/>
                          </StackPanel>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <StackPanel Orientation=""Horizontal"">
                          <Button x:Name=""btnPreview"" Content=""试听"" Style=""{StaticResource SecondaryButton}""/>
                        </StackPanel>
                      </StackPanel>
                    </Border>

                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                            <TextBlock Text=""台词库"" Style=""{StaticResource H2}""/>
                            <TextBlock x:Name=""lblVoiceCount"" Text=""—"" Style=""{StaticResource Sub}""
                                       Margin=""10,0,0,0"" VerticalAlignment=""Center""/>
                          </StackPanel>
                          <Button x:Name=""btnRandomLine"" Content=""随机来一句"" Grid.Column=""1""
                                  Style=""{StaticResource SecondaryButton}"" Margin=""0,0,8,0""/>
                          <Button x:Name=""btnAddVoice"" Content=""新增语音"" Grid.Column=""2""
                                  Style=""{StaticResource PrimaryButton}""/>
                        </Grid>
                        <Grid Margin=""0,12,0,0"">
                          <TextBox x:Name=""txtVoiceSearch"" Height=""34"" Padding=""11,0"" VerticalContentAlignment=""Center""/>
                          <TextBlock x:Name=""phVoiceSearch"" Text=""搜索台词…"" IsHitTestVisible=""False""
                                     Margin=""12,0,0,0"" VerticalAlignment=""Center"" FontSize=""12.5""
                                     Foreground=""{DynamicResource Ink2}""/>
                        </Grid>
                        <Border Background=""{DynamicResource Panel}"" BorderBrush=""{DynamicResource Rule}""
                                BorderThickness=""1"" Padding=""0"" Margin=""0,12,0,0"">
                          <ScrollViewer x:Name=""voiceScroller"" Height=""260"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"">
                            <StackPanel x:Name=""voiceList""/>
                          </ScrollViewer>
                        </Border>
                        <TextBlock Text=""改名 / 删除 / 新增都会写回这个角色包的台词清单；删除只移除条目，音频文件保留。""
                                   Style=""{StaticResource Sub}"" Margin=""0,8,0,0""/>
                      </StackPanel>
                    </Border>
                  </StackPanel>
                </StackPanel>

                <StackPanel x:Name=""pageAnim"" Visibility=""Collapsed""/>

                <!-- 通用设置 -->
                <StackPanel x:Name=""pageUi"" Visibility=""Collapsed"">
                  <StackPanel Orientation=""Horizontal"">
                    <TextBlock Text=""通用设置"" Style=""{StaticResource H1}""/>
                    <TextBlock Text=""// GENERAL"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Bottom""
                               Margin=""14,0,0,9"" Opacity=""0.7""/>
                  </StackPanel>
                  <TextBlock Text=""桌面显示、界面主色、开机启动"" Style=""{StaticResource Sub}"" Margin=""0,4,0,0""/>
                  <Grid Height=""2"" Margin=""0,16,0,2"" SnapsToDevicePixels=""True"">
                    <Border Background=""#000000""/>
                  </Grid>
                  <StackPanel x:Name=""uiContent"" Margin=""0,12,0,0""/>
                </StackPanel>

                <!-- 气泡 -->
                <StackPanel x:Name=""pageBubble"" Visibility=""Collapsed"">
                  <StackPanel Orientation=""Horizontal"">
                    <TextBlock Text=""气泡"" Style=""{StaticResource H1}""/>
                    <TextBlock Text=""// BUBBLE"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Bottom""
                               Margin=""14,0,0,9"" Opacity=""0.7""/>
                  </StackPanel>
                  <TextBlock Text=""她说话时那一小块地方的样子"" Style=""{StaticResource Sub}"" Margin=""0,4,0,0""/>
                  <Grid Height=""2"" Margin=""0,16,0,2"" SnapsToDevicePixels=""True"">
                    <Border Background=""#000000""/>
                  </Grid>
                  <StackPanel Margin=""0,12,0,0"">
                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <CheckBox x:Name=""chkBub"" Content=""启用气泡"" FontSize=""13"" Style=""{StaticResource ToggleSwitch}""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""高度"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""气泡区域能容纳几行字"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                            <Slider x:Name=""sldBub"" Width=""210"" Minimum=""70"" Maximum=""220"" TickFrequency=""10"" Style=""{StaticResource WinSlider}""/>
                            <TextBlock x:Name=""lblBub"" Text=""110 px"" FontSize=""11"" Foreground=""{DynamicResource Ink2}""
                                       FontWeight=""SemiBold"" HorizontalAlignment=""Right"" Margin=""0,2,0,0""/>
                          </StackPanel>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""主动冒泡"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""隔多久自己说一句"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <ComboBox x:Name=""cmbEvery"" Grid.Column=""1"" Width=""180"" VerticalAlignment=""Center""/>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""字体"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""气泡里的字用哪种字形"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <ComboBox x:Name=""cmbFont"" Grid.Column=""1"" Width=""220"" Height=""34"" VerticalAlignment=""Center""/>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""颜色"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""气泡的底色"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <Button x:Name=""btnBubbleColor"" Content=""自定义颜色…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                        </Grid>
                        <WrapPanel x:Name=""bubbleColorHost"" Margin=""0,12,0,0""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""文字颜色"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""留空则按底色自动选一个清楚的"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <Button x:Name=""btnBubbleInk"" Content=""自定义文字颜色…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                        </Grid>
                        <WrapPanel x:Name=""bubbleInkHost"" Margin=""0,12,0,0""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""字号"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""固定磅值，可直接输入"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"" HorizontalAlignment=""Right"">
                            <ComboBox x:Name=""cmbFontSize"" Width=""120"" IsEditable=""True"" VerticalAlignment=""Center""
                                      HorizontalContentAlignment=""Left""
                                      ToolTip=""字号（pt）。不随气泡高度缩放；长文本不会自动滚动，鼠标移上去滚轮查看。""/>
                            <TextBlock x:Name=""lblFontSize"" Text=""pt"" FontSize=""12"" Foreground=""{DynamicResource Ink2}""
                                       VerticalAlignment=""Center"" Margin=""6,0,0,0""/>
                          </StackPanel>
                        </Grid>
                      </StackPanel>
                    </Border>

                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""外框样式"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""内置 4 种，也可以上传自己的"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <Button x:Name=""btnUpBubble"" Content=""上传外框…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                        </Grid>
                        <Border Style=""{StaticResource Divider}""/>
                        <WrapPanel x:Name=""bubbleStyleHost""/>
                        <TextBlock x:Name=""lblBubHint"" Style=""{StaticResource Sub}"" Margin=""0,4,0,0""
                                   Text=""四边画框效果最好""/>
                      </StackPanel>
                    </Border>

                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <TextBlock Text=""预览"" Style=""{StaticResource H2}""/>
                        <TextBlock Text=""按真实比例，1:1 显示"" Style=""{StaticResource Sub}""/>
                        <ContentControl Style=""{StaticResource DashedBox}"" Height=""300"" Margin=""0,12,0,0"">
                          <Image x:Name=""imgBub"" Stretch=""Uniform""/>
                        </ContentControl>
                        <TextBlock x:Name=""lblBubSize"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""—""/>
                      </StackPanel>
                    </Border>
                  </StackPanel>
                </StackPanel>

                <!-- 统计（内容由 C# 动态生成）-->
                <StackPanel x:Name=""pageStats"" Visibility=""Collapsed""/>

                <!-- 关于（内容由 BuildAboutPage 生成） -->
                <StackPanel x:Name=""pageAbout"" Visibility=""Collapsed""/>

                <!-- 新建角色：一个角色一个文件夹 -->
                <StackPanel x:Name=""pageImport"" Visibility=""Collapsed"">
                  <StackPanel Orientation=""Horizontal"">
                    <TextBlock Text=""新建角色"" Style=""{StaticResource H1}""/>
                    <TextBlock Text=""// IMPORT"" Style=""{StaticResource Eyebrow}"" VerticalAlignment=""Bottom""
                               Margin=""14,0,0,9"" Opacity=""0.7""/>
                  </StackPanel>
                  <TextBlock Text=""把立绘、语音和动画装进一个新角色包"" Style=""{StaticResource Sub}"" Margin=""0,4,0,0""/>
                  <Grid Height=""2"" Margin=""0,16,0,2"" SnapsToDevicePixels=""True"">
                    <Border Background=""#000000""/>
                  </Grid>
                  <StackPanel Margin=""0,12,0,0"">
                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <TextBlock Text=""角色名称"" Style=""{StaticResource H2}""/>
                        <TextBlock Text=""显示在角色列表里的名字"" Style=""{StaticResource Sub}""/>
                        <TextBox x:Name=""txtName"" Height=""34"" Padding=""11,0"" VerticalContentAlignment=""Center""
                                 MaxLength=""40"" Margin=""0,10,0,0""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""角色图像"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""透明背景 PNG"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <Button x:Name=""btnPickImg"" Content=""选择图片…"" Style=""{StaticResource SecondaryButton}"" Grid.Column=""1"" VerticalAlignment=""Center""/>
                        </Grid>
                        <TextBlock x:Name=""lblImg"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""未选择""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Grid Margin=""0,9"">
                          <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                          <StackPanel VerticalAlignment=""Center"">
                            <TextBlock Text=""语音文件夹"" Style=""{StaticResource H2}""/>
                            <TextBlock Text=""仅支持 wav"" Style=""{StaticResource Sub}""/>
                          </StackPanel>
                          <Button x:Name=""btnPickVoices"" Content=""选择文件夹…"" Style=""{StaticResource SecondaryButton}"" Grid.Column=""1"" VerticalAlignment=""Center""/>
                        </Grid>
                        <TextBlock x:Name=""lblVoices"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""未选择""/>
                        <Border Style=""{StaticResource Divider}""/>
                        <Button x:Name=""btnImport"" Content=""创建角色"" Style=""{StaticResource PrimaryButton}"" HorizontalAlignment=""Left""/>
                        <TextBlock x:Name=""lblImportMsg"" Style=""{StaticResource Sub}"" Margin=""0,10,0,0""
                                   Text=""会重启桌宠""/>
                      </StackPanel>
                    </Border>
                    <Border Style=""{StaticResource Card}"">
                      <StackPanel>
                        <TextBlock Text=""已有角色"" Style=""{StaticResource H2}"" Margin=""0,0,0,4""/>
                        <StackPanel x:Name=""importList""/>
                      </StackPanel>
                    </Border>
                  </StackPanel>
                </StackPanel>
              </StackPanel>
            </ScrollViewer>

            <!-- 底部操作栏：白底 + 顶部细线，右边一个主要操作 -->
            <Border Grid.Row=""2"" Background=""{DynamicResource Desk}""
                    BorderBrush=""{DynamicResource Rule}"" BorderThickness=""0,1,0,0"" Padding=""24,12"">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/>
                </Grid.ColumnDefinitions>
                <Button x:Name=""btnQuit"" Content=""退出桌宠"" Style=""{StaticResource GhostButton}"" Grid.Column=""0""/>
                <!-- 底栏状态：左边「未保存」墨块，右边「已保存」主题色块，两者互斥 -->
                <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""14,0,0,0"">
                  <TextBlock x:Name=""lblDirty"" Text=""UNSAVED"" FontSize=""10""
                             FontWeight=""Bold"" Background=""{DynamicResource InkSolid}""
                             Foreground=""#FFFFFF"" Padding=""9,5"" Visibility=""Collapsed""/>
                  <TextBlock x:Name=""lblSaved"" Text="""" FontSize=""10""
                             FontWeight=""Bold"" Background=""{DynamicResource Accent}""
                             Foreground=""{DynamicResource OnAccent}"" Padding=""9,5""
                             Margin=""8,0,0,0"" Visibility=""Collapsed""/>
                </StackPanel>
                <StackPanel Grid.Column=""2"" Orientation=""Horizontal"" HorizontalAlignment=""Right"">
                  <Button x:Name=""btnCancel"" Content=""取消"" Style=""{StaticResource SecondaryButton}"" Margin=""0,0,8,0""/>
                  <Button x:Name=""btnSave"" Content=""保存更改"" Style=""{StaticResource PrimaryButton}""/>
                </StackPanel>
              </Grid>
            </Border>
          </Grid>
        </Grid>

        <!-- 右：八页入口。整格铺斜纹、编号在左上、标签压底，四行两列不留缝 -->
        <Border x:Name=""tilesHost"" Grid.Column=""2"" Background=""{DynamicResource Desk}""
                BorderBrush=""{DynamicResource Rule}"" BorderThickness=""1,0,0,0"">
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/><RowDefinition Height=""*""/>
            </Grid.RowDefinitions>
            <Grid Grid.Row=""0"" Height=""34"">
              <TextBlock Text=""SECTIONS"" Style=""{StaticResource Eyebrow}"" Margin=""14,1,0,0""
                         VerticalAlignment=""Center""/>
              <TextBlock Text=""08"" Style=""{StaticResource Eyebrow}"" HorizontalAlignment=""Right""
                         VerticalAlignment=""Center"" Margin=""0,1,14,0"" Opacity=""0.5""/>
              <Border VerticalAlignment=""Bottom"" Height=""1"" Background=""{DynamicResource Rule}""/>
            </Grid>
            <Grid Grid.Row=""1"" Margin=""12,0,12,10"">
              <Grid.RowDefinitions>
                <RowDefinition Height=""*""/><RowDefinition Height=""*""/>
                <RowDefinition Height=""*""/><RowDefinition Height=""*""/>
                <RowDefinition Height=""*""/><RowDefinition Height=""*""/>
                <RowDefinition Height=""*""/><RowDefinition Height=""*""/>
              </Grid.RowDefinitions>

              <ToggleButton x:Name=""tileBubble"" Grid.Row=""0"" Tag=""01"" CommandParameter=""bubble""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""气泡"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""字体 │ 颜色 │ 频率"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileUi"" Grid.Row=""1"" Tag=""02"" CommandParameter=""ui""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""通用设置"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""显示 │ 开屏 │ 主题色"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileChar"" Grid.Row=""2"" Tag=""03"" CommandParameter=""char""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""形象与大小"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""角色 │ 高度 │ 立绘"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileAnim"" Grid.Row=""3"" Tag=""04"" CommandParameter=""anim""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""动画"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""待机 │ 点击 │ 拖动"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileVoice"" Grid.Row=""4"" Tag=""05"" CommandParameter=""voice""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""语音"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""音量 │ 试听 │ 台词"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileImport"" Grid.Row=""5"" Tag=""06"" CommandParameter=""import""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""导入"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""角色包 │ 素材"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileStats"" Grid.Row=""6"" Tag=""07"" CommandParameter=""stats""
                            Style=""{StaticResource TileButton}"">
                <StackPanel>
                  <TextBlock Text=""使用统计"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""陪伴时长 │ 点击"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>

              <ToggleButton x:Name=""tileAbout"" Grid.Row=""7"" Tag=""08"" CommandParameter=""about""
                            Style=""{StaticResource TileButton}"" BorderThickness=""0"">
                <StackPanel>
                  <TextBlock Text=""关于"" FontSize=""15"" FontWeight=""Bold""/>
                  <TextBlock Text=""版本 │ 许可"" FontSize=""11.5"" Opacity=""0.62"" Margin=""0,3,0,0""/>
                </StackPanel>
              </ToggleButton>
            </Grid>
          </Grid>
        </Border>
      </Grid>
    </Grid>
  </Grid>
</Window>
";
}

// ============================================================================
// 自绘对话框（替代系统 MessageBox）
// 圆角卡片 + 柔和投影 + 淡入放大 + Esc 取消；危险操作（退出/删除/清空）用红色主按钮
// ============================================================================
static class Dlg
{
    public static void Info(string title, string body, string okText = "好")
    {
        Show(title, body, string.IsNullOrEmpty(okText) ? "好" : okText, null, false);
    }

    public static bool Ask(string title, string body, string okText, string cancelText, bool danger = false)
    {
        return Show(title, body, string.IsNullOrEmpty(okText) ? "确定" : okText,
            string.IsNullOrEmpty(cancelText) ? "取消" : cancelText, danger);
    }

    public static Window ActiveOwnerPublic() { return ActiveOwner(); }

    static Window ActiveOwner()
    {
        try
        {
            if (Application.Current != null)
                foreach (Window w in Application.Current.Windows)
                    if (w.IsActive) return w;
        }
        catch (Exception ex) { Settings.LogErr("ActiveOwner", ex); }
        return null;
    }

    // 弹窗按钮：和主界面 PrimaryButton / SecondaryButton 同一套语言 —— 34 高、直角、
    // 1px 细线（白底款）/ 12% 白发丝边（填充款），按下缩 0.97，键盘焦点有内缩圈。
    // 旧版那条「底部 2px 承重线」删了：它是 90 年代键帽的影子，而且浅灰底配近黑线太重，
    // 一进弹窗就把两个按钮变成两块砖。
    enum BtnKind { Secondary, Primary, Danger }

    static Button MakeButton(string text, BtnKind kind)
    {
        Color bg, hot, press;
        if (kind == BtnKind.Secondary)
        {
            bg = Colors.White;
            hot = Color.FromRgb(0xFA, 0xFA, 0xFA);
            press = Color.FromRgb(0xF0, 0xF0, 0xF0);
        }
        else if (kind == BtnKind.Danger)
        {
            // 危险红不走主题色，但两档交互色和主按钮同一条规则：往深里压（白字始终够读）
            bg = Color.FromRgb(0xC4, 0x2B, 0x1C);
            hot = Settings.Shade(bg, -0.12);
            press = Settings.Shade(bg, -0.24);
        }
        else
        {
            // 直接用主按钮那三档，换主题色时弹窗和「保存更改」是同一个色
            Color bf, bh, bp;
            Settings.BtnTones(Settings.ThemeColor, out bf, out bh, out bp);
            bg = bf; hot = bh; press = bp;
        }
        bool filled = kind != BtnKind.Secondary;
        Color fg = filled ? Colors.White : Color.FromRgb(0x21, 0x24, 0x29);
        Color line = filled ? Colors.Transparent : Color.FromRgb(0xC9, 0xCE, 0xD6);
        Color lineHot = filled ? Colors.Transparent : Color.FromRgb(0x8A, 0x8F, 0x98);

        var b = new Button
        {
            Content = text, Height = 34, MinWidth = 96, Padding = new Thickness(20, 0, 20, 0),
            FontSize = 12.5, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(bg), Foreground = new SolidColorBrush(fg),
            BorderBrush = new SolidColorBrush(line),
            FontFamily = Settings.UiFace ?? new FontFamily("Microsoft YaHei"),
            FocusVisualStyle = RingStyle(),     // 焦点圈画在按钮外面（-3px），不啃按钮内部
            SnapsToDevicePixels = true, UseLayoutRounding = true,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1, 1);
        b.RenderTransform = scale;

        var tpl = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "bd";
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        grid.AppendChild(bd);
        if (filled)
        {
            // 深色块靠这 12% 的一条白线才有厚度，和主按钮同一招
            var hair = new FrameworkElementFactory(typeof(Border));
            hair.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));
            hair.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            hair.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(hair);
        }
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
        grid.AppendChild(cp);

        tpl.VisualTree = grid;
        // 白底按钮不再套内框（之前那圈 2px 墨线像在按钮里又画了一个输入框）：
        // 键盘焦点只把描边压深一档，和悬停同一档灰，不当框看。
        tpl.Triggers.Add(new Trigger
        {
            Property = UIElement.IsKeyboardFocusedProperty,
            Value = true,
            Setters = { new Setter(Border.BorderBrushProperty, new SolidColorBrush(lineHot), "bd") }
        });
        b.Template = tpl;

        // 填充款带右下 8px 切角，和主界面「保存更改」同一套语言（真裁切，不是盖三角）
        if (filled)
            b.SizeChanged += (s2, e2) => Chamfer(b, 8);

        bool over = false, down = false;
        Action sync = () =>
        {
            b.Background = new SolidColorBrush(down ? press : over ? hot : bg);
            b.BorderBrush = new SolidColorBrush(over || down ? lineHot : line);
        };
        b.MouseEnter += (s2, e2) => { over = true; sync(); };
        b.MouseLeave += (s2, e2) => { over = false; down = false; sync(); Snap(scale, 1, 140); };
        b.PreviewMouseLeftButtonDown += (s2, e2) => { down = true; sync(); Snap(scale, 0.97, 80); };
        b.PreviewMouseLeftButtonUp += (s2, e2) => { down = false; sync(); Snap(scale, 1, 140); };
        b.LostMouseCapture += (s2, e2) => { if (!down) return; down = false; sync(); Snap(scale, 1, 140); };
        return b;
    }

    // 按下 / 松开的缩放：和主界面 0.98 一个手感，弹窗小一号按钮收得稍深一点（0.97）
    static void Snap(ScaleTransform t, double to, int ms)
    {
        var an = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, an);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, an);
    }

    // 键盘焦点圈：画在按钮外面 3px，和主界面的 FocusRing 一致。
    // 只给键盘用户看（鼠标点不会出现），弹窗打开时也不再默认焦点到某个按钮上。
    static Style _ringStyle;
    static Style RingStyle()
    {
        if (_ringStyle != null) return _ringStyle;
        var tpl = new ControlTemplate();
        var r = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
        r.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
        r.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.0);
        r.SetValue(System.Windows.Shapes.Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
        r.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        tpl.VisualTree = r;
        _ringStyle = new Style(typeof(Control));
        _ringStyle.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        _ringStyle.Seal();
        return _ringStyle;
    }

    // 右下 8px 真裁切（弹窗按钮自建模板，不走主界面的 ApplyChamferIfPrimary）
    static void Chamfer(Button b, double cut)
    {
        double w = b.ActualWidth, h = b.ActualHeight;
        if (w <= 12 || h <= 12) return;
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(w, 0), false, false);
            ctx.LineTo(new Point(w, h - cut), false, false);
            ctx.LineTo(new Point(w - cut, h), false, false);
            ctx.LineTo(new Point(0, h), false, false);
        }
        g.Freeze();
        b.Clip = g;
    }

    // 自检探针：三种按钮各建一个，回报底色 / 线色与线宽 / 焦点圈 / 切角 / 有没有 2px 重线
    internal static string Probe()
    {
        var rows = new List<string>();
        foreach (BtnKind k in new[] { BtnKind.Secondary, BtnKind.Primary, BtnKind.Danger })
        {
            Button b = MakeButton("×", k);
            b.Measure(new Size(400, 80));
            b.Arrange(new Rect(0, 0, 120, 34));
            b.UpdateLayout();
            SolidColorBrush bg = b.Background as SolidColorBrush;
            Border bd = b.Template.FindName("bd", b) as Border;
            bool wantCut = k != BtnKind.Secondary;
            SolidColorBrush lb = bd == null ? null : bd.BorderBrush as SolidColorBrush;
            string line = (lb == null || bd == null) ? "—"
                : "#" + Settings.HexOf(lb.Color) + (lb.Color.A == 0 ? "透明" : "") + " " + bd.BorderThickness.Left.ToString("0") + "px";
            rows.Add(k + "底 #" + (bg == null ? "—" : Settings.HexOf(bg.Color)) + "·线" + line
                + "·焦点圈" + (b.FocusVisualStyle != null ? "外圈 ✓" : "缺 ✗")
                + "·切角" + (b.Clip != null ? (wantCut ? "有 ✓" : "多余 ✗") : (wantCut ? "缺 ✗" : "无 ✓"))
                + "·底线" + (bd != null && bd.BorderThickness.Bottom > 1 ? "有 ✗" : "无 ✓"));
        }
        return string.Join(" ／ ", rows.ToArray());
    }

    static bool Show(string title, string body, string okText, string cancelText, bool danger)
    {
        bool result = false;
        Window owner = ActiveOwner();
        var win = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        if (owner != null) win.Owner = owner;
        // 弹窗是独立 Window，继承不到主窗的 FontFamily：不显式给，中文会掉回雅黑、
        // 数字和英文会和面板里长得不一样（就是之前「弹窗字体不统一」的原因）
        if (Settings.UiFace != null) win.FontFamily = Settings.UiFace;

        var card = new Border
        {
            Width = 430,
            Margin = new Thickness(26),
            Background = Brushes.White,
            BorderBrush = Settings.C("#C9CED6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(26, 24, 26, 20)
        };

        var root = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(danger ? Color.FromRgb(0xF7, 0xE7, 0xE5) : Settings.ThemeColor),
            Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top
        };
        badge.Child = new TextBlock
        {
            Text = danger ? "\uE7E8" : "\uE946",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 19,
            Foreground = new SolidColorBrush(danger ? Color.FromRgb(0xB3, 0x26, 0x1E) : Settings.SignalInk(Settings.ThemeColor)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(badge);

        var textCol = new StackPanel();
        textCol.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), TextWrapping = TextWrapping.Wrap
        });
        textCol.Children.Add(new TextBlock
        {
            Text = body, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0x00, 0x00, 0x00)),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 8, 0, 0)
        });
        Grid.SetColumn(textCol, 1);
        head.Children.Add(textCol);
        root.Children.Add(head);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        Button cancelBtn = null;
        if (cancelText != null)
        {
            // 白底 + 1px 细线：和主界面次级按钮一致（旧版是灰底 + 2px 近黑重线，太重）
            cancelBtn = MakeButton(cancelText, BtnKind.Secondary);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.IsCancel = true;
            cancelBtn.Click += (s, e) => { result = false; win.Close(); };
            btns.Children.Add(cancelBtn);
        }
        // 主按钮跟着主题色的「白字 4.5:1」那一档走，和「保存更改」同一个色
        Button okBtn = MakeButton(okText, danger ? BtnKind.Danger : BtnKind.Primary);
        okBtn.Click += (s, e) => { result = true; win.Close(); };
        btns.Children.Add(okBtn);
        root.Children.Add(btns);

        card.Child = root;
        win.Content = card;

        // 开发自验：只渲染卡片，不弹窗（弹窗是独立 HWND，整窗截图抓不到）
        if (!string.IsNullOrEmpty(Settings._dlgShotPath))
        {
            try
            {
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size sz = card.DesiredSize;
                card.Arrange(new Rect(0, 0, Math.Max(1, sz.Width), Math.Max(1, sz.Height)));
                card.UpdateLayout();
                int rw = (int)Math.Ceiling(sz.Width), rh = (int)Math.Ceiling(sz.Height);
                // 和 --shot 同一套：N 倍 DPI 矢量重绘 + 可选裁剪（验 1px 线、2px 焦点圈用）
                int zoom = Settings._shotZoom < 1 ? 1 : Settings._shotZoom;
                var rtb = new RenderTargetBitmap(rw * zoom, rh * zoom, 96.0 * zoom, 96.0 * zoom, PixelFormats.Pbgra32);
                rtb.Render(card);
                BitmapSource frame = rtb;
                if (Settings._cropW > 0 && Settings._cropH > 0)
                {
                    int cx = Math.Max(0, Math.Min(rw * zoom - 1, Settings._cropX));
                    int cy = Math.Max(0, Math.Min(rh * zoom - 1, Settings._cropY));
                    int cw = Math.Min(Settings._cropW, rw * zoom - cx), ch = Math.Min(Settings._cropH, rh * zoom - cy);
                    if (cw > 0 && ch > 0) frame = new CroppedBitmap(rtb, new Int32Rect(cx, cy, cw, ch));
                }
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(frame));
                using (FileStream fs = File.Create(Settings._dlgShotPath)) enc.Save(fs);
                Console.WriteLine("dlgshot " + Settings._dlgShotPath + " " + frame.PixelWidth + "x" + frame.PixelHeight
                                  + (zoom > 1 ? " @" + zoom + "x" : ""));
            }
            catch (Exception ex) { Settings.LogErr("Show", ex); }
            return false;
        }

        // 进场像快门：从上方 6px 硬移入 + 淡入。不缩放、不回弹。
        card.Opacity = 0;
        var st = new TranslateTransform(0, -6);
        card.RenderTransform = st;

        win.Loaded += (s, e) =>
        {
            card.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            st.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // 不把焦点给某个按钮：焦点一落上去就会带出焦点圈，看着像按钮里嵌了个框。
            // Esc / Enter 由下面的 PreviewKeyDown 统一接管，不依赖焦点在哪。
            win.Focus();
        };
        win.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { result = false; win.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && !danger) { result = true; win.Close(); e.Handled = true; }
        };

        win.ShowDialog();
        return result;
    }
}

// ============================================================================
// 自绘取色器：R/G/B 滑杆 + hex 输入 + 实时预览（WPF 4.8 没有内置 ColorPicker）
// ============================================================================
static class ColorDlg
{
    public static bool Ask(Color initial, out Color result)
    {
        Color picked = initial;
        bool okFlag = false;
        Window owner = Dlg.ActiveOwnerPublic();
        var win = new Window
        {
            Title = "选择颜色",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            UseLayoutRounding = true
        };
        if (owner != null) win.Owner = owner;
        if (Settings.UiFace != null) win.FontFamily = Settings.UiFace;

        var card = new Border
        {
            Width = 400, Margin = new Thickness(26), Background = Brushes.White,
            BorderBrush = Settings.C("#C9CED6"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0), Padding = new Thickness(24, 20, 24, 18)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "选择颜色", FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16))
        });

        Color cur = initial;
        var preview = new Border
        {
            Height = 56, Margin = new Thickness(0, 14, 0, 14), CornerRadius = new CornerRadius(0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), BorderThickness = new Thickness(1)
        };
        var hexBox = new TextBox { Height = 32, FontFamily = new FontFamily("Consolas"), MaxLength = 6 };
        Action upd = () =>
        {
            preview.Background = new SolidColorBrush(cur);
            if (!hexBox.IsFocused) hexBox.Text = Settings.HexOf(cur);
        };
        upd();
        root.Children.Add(preview);

        Slider mkSlider(byte v, Action<byte> set)
        {
            // 取色器里这三条也用 WinSlider，否则会是 WPF 默认的蓝色 Aero2 样式
            var sl = new Slider
            {
                Minimum = 0, Maximum = 255, Value = v,
                Style = (Settings.MainWin != null ? Settings.MainWin.TryFindResource("WinSlider") as Style : null),
                Margin = new Thickness(0, 4, 0, 0)
            };
            sl.ValueChanged += (s2, e2) => { set((byte)sl.Value); upd(); };
            return sl;
        }
        root.Children.Add(new TextBlock { Text = "R 红", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.R, v => cur = Color.FromRgb(v, cur.G, cur.B)));
        root.Children.Add(new TextBlock { Text = "G 绿", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.G, v => cur = Color.FromRgb(cur.R, v, cur.B)));
        root.Children.Add(new TextBlock { Text = "B 蓝", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.B, v => cur = Color.FromRgb(cur.R, cur.G, v)));

        var hexRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition());
        hexRow.Children.Add(new TextBlock { Text = "#", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        hexBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hexBox, 1); hexRow.Children.Add(hexBox);
        hexBox.TextChanged += (s2, e2) =>
        {
            Color c = Settings.WpfParseHex(hexBox.Text, cur);
            if (!Settings.HexOf(c).Equals(hexBox.Text.Trim().TrimStart('#'), StringComparison.OrdinalIgnoreCase)) return;
            cur = c; preview.Background = new SolidColorBrush(cur);
        };
        root.Children.Add(hexRow);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消", Height = 34, MinWidth = 92, Padding = new Thickness(18, 0, 18, 0),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFB)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.IsCancel = true;
        cancel.Click += (s2, e2) => { win.Close(); };
        btns.Children.Add(cancel);
        var ok = new Button
        {
            Content = "确定", Height = 34, MinWidth = 92, Padding = new Thickness(18, 0, 18, 0),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8)), Foreground = Brushes.White
        };
        ok.Click += (s2, e2) => { picked = cur; okFlag = true; win.DialogResult = true; win.Close(); };
        btns.Children.Add(ok);
        root.Children.Add(btns);

        card.Child = root;
        win.Content = card;
        card.Opacity = 0;
        win.Loaded += (s2, e2) => card.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        win.ShowDialog();
        result = picked;
        return okFlag;
    }
}
