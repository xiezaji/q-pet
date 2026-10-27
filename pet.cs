// ============================================================================
// Q Pet（完全独立，不依赖 DSH）
// ============================================================================
// 原理：Win32 layered window（WS_EX_LAYERED + WS_EX_TOPMOST + WS_EX_TOOLWINDOW）
//   用 UpdateLayeredWindow 贴**预乘 alpha** 的 32bpp DIB ⇒ 真透明、无边框、不进任务栏。
//
// 两个行为（对应用户需求 #1/#2）：
//   #1 独立进程：不依赖任何宿主程序，开机自启、随时可关。
//   #2 全局键盘/鼠标输入 ⇒ 播放「静默按压」动画（挤压-拉伸），**不出声**；
//      只有点击桌宠本体才播放语音。
//
// 🔒 隐私边界（务必保留这段）：全局钩子挂的是 WH_KEYBOARD_LL / WH_MOUSE_LL，
//   回调里**只判断 wParam 是不是「按下类」消息，然后置一个标志**。
//   绝不读取 KBDLLHOOKSTRUCT / MSLLHOOKSTRUCT 的内容（即：不取键码、不取字符、
//   不记录、不落盘、不外传）。整个文件里没有一处 Marshal.Read* 读取钩子 lParam。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PetApp
{
    public static class Pet
    {
        // ---------------- Win32 结构与常量 ----------------
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight;
            public ushort biPlanes; public ushort biBitCount; public uint biCompression;
            public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize; public uint style; public IntPtr lpfnWndProc;
            public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance;
            public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
            public string lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
            public uint time; public int ptX; public int ptY; public uint lPrivate;
        }

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint elapse, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, IntPtr id);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr h, uint flags, IntPtr id, string text);
        [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr h, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int crKey, ref BLENDFUNCTION blend, uint flags);
        // MCI：比 SoundPlayer 强的地方 —— ① close 是**即时**掐断（PlaySync 拦不住 Stop）
        //                          ② 能读到「实际播放位置」，这才是"到底播了多久"的硬证据
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int mciSendStringW(string cmd, System.Text.StringBuilder ret, int len, IntPtr hwnd);

        const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
        const int SW_SHOWNOACTIVATE = 4;
        const uint WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
        const uint ULW_ALPHA = 0x02;
        const byte AC_SRC_OVER = 0x00, AC_SRC_ALPHA = 0x01;
        const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        const int WM_DESTROY = 0x0002, WM_TIMER = 0x0113, WM_NCHITTEST = 0x0084;
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEMOVE = 0x0200, WM_RBUTTONUP = 0x0205;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_COMMAND = 0x0111, WM_APP = 0x8000;
        // 系统事件：显示器布局/分辨率变化、DPI 变化、休眠唤醒。不处理的话副屏一拔桌宠就消失。
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_DPICHANGED = 0x02E0;
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_APMSUSPEND = 0x0004, PBT_APMRESUMEAUTOMATIC = 0x0012, PBT_APMRESUME = 0x0007;
        const int WM_PET_ACTIVATE = 0x8005;      // 第二个实例发来的「把你叫出来」
        const int HTTRANSPARENT = -1, HTCLIENT = 1;
        const int SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        // ---- 全屏隐藏：前台窗口是否铺满整块屏幕 ----
        const int SW_HIDE = 0;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const uint MONITOR_DEFAULTTONULL = 0;
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr hMon, ref MONITORINFO mi);
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        }
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint MF_STRING = 0x0000;

        // ---------------- 运行时状态 ----------------
        static IntPtr _hwnd = IntPtr.Zero, _memDc = IntPtr.Zero, _hBitmap = IntPtr.Zero, _screenDc = IntPtr.Zero;
        static IntPtr _bits = IntPtr.Zero;
        static IntPtr _kbHook = IntPtr.Zero, _mouseHook = IntPtr.Zero;
        static WndProcDelegate _wndProc;   // 必须持有引用，否则被 GC 回收 ⇒ 回调野指针
        static HookProc _kbProc, _mouseProc;

        static Dictionary<string, string> _cfg = new Dictionary<string, string>();
        static string _baseDir = "";
        static string _charName = "", _charDir = "";   // 当前角色包（chars\<角色>）
        static byte[] _frames;
        static int _w, _h, _idleN, _pressN, _clickN, _dragN, _frameBytes;
        static int _frameIndex = 0, _tick = 0;
        static int _lang = 0, _langCount = 0;
        static bool _dragging = false, _dragMoved = false;
        static POINT _dragOrigin; static RECT _dragRect;
        static int _lastInputAt = 0;
        static string _phase = "idle";
        static string _pressKind = "press";          // press / click：两套挤压帧
        static int _phaseStart = 0;
        static double _dragPhase = 0;                // 拖动帧累加器（支持倍速）
        static int _schedAt = 0; static string _schedPath = "";
        static int _posSavedAt = 0;               // 上次把位置写进 pet-pos.txt 的时刻（拖动中节流用）

        // ---------------- 使用统计（🔒 本地、只计数、不联网） ----------------
        // 键盘：只累计「每个虚拟键码被按了多少次」——不记录顺序、不记录字符、不还原输入内容。
        // 软件：每秒取前台窗口的**进程名**，累计时长。
        // 屏幕：用 GetLastInputInfo 的空闲时间判断"在用/挂机"，累计秒数。
        // 全部只写入本地 pet-stats.txt，设置页可一键清空。
        static bool _statsKeys = true;
        static string _statsFile = "";
        static Dictionary<int, int> _keyCount = new Dictionary<int, int>();
        static Dictionary<string, int> _appSec = new Dictionary<string, int>();
        static Dictionary<string, int> _charDaySec = new Dictionary<string, int>();    // 今日：每个角色显示了多久
        static Dictionary<string, int> _charTotalSec = new Dictionary<string, int>();  // 累计：跨天不清零
        static string _charTimeFile = "";
        static int _screenSec = 0, _activeSec = 0, _idleSec = 0;
        static int _statsLastTick = 0, _statsLastFlush = 0;
        static bool _statsPaused = false;      // 系统休眠期间不累计（唤醒后也不会把睡眠时长补记进来）
        static bool _statsDirty = false;           // 有变化才落盘（设置面板靠这个文件实时刷新）
        static string _statsDay = "";
        static int _lastFgPid = 0; static string _lastFgName = "";

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        static int _holdMs = 110;
        static bool _hideFs = true;         // 全屏隐藏（设置面板「通用设置 → 全屏隐藏」）
        static bool _fsHidden = false;      // 当前是否正因全屏而隐藏

        // ---------------- 音量 / 动画（设置面板可调） ----------------
        // 音量：0–100，>0 且 <100 时把 wav 采样按比例衰减后播放（缓存到 pet-vol-cache），
        //       这样不依赖声卡/驱动是否支持 MCI 的 setaudio volume。
        static int _volume = 100;
        static readonly Dictionary<string, string> _volCache = new Dictionary<string, string>();

        static bool _animIdle = true;      // 待机呼吸
        static bool _animPress = true;     // 全局按键/鼠标按下的挤压反馈
        static bool _animClick = true;     // 点她时的挤压反馈
        static bool _animDrag = true;      // 拖动时的悬空摆动
        static double _animSpeed = 1.0;    // 旧版全局倍率（只作为四项的兜底默认值）
        static double _speedIdle = 1.0, _speedPress = 1.0, _speedClick = 1.0, _speedDrag = 1.0;
        static double _idlePhase = 0;      // 待机帧累加器（支持小数速度）
        static int _bubbleShownAt = 0;     // 气泡弹出动画的起点

        // ---- 对话框（动态气泡）----
        static int _bubbleH = 118;                 // 窗口上方给气泡预留的高度（0 = 关闭气泡）
        static byte[] _zeroTop;                    // 气泡区的清零缓冲（每帧复用，别每帧 new）
        static string _bubbleText = "";
        static int _bubbleUntil = 0;
        static string[] _lines = new string[0];
        static string[] _lineText = new string[0];   // 带语音的台词：文字与音频一一对应
        static string[] _linePath = new string[0];
        static Random _rnd = new Random();
        static int _bubbleEveryMs = 600000;        // 隔多久自己冒一句（0 = 不主动冒）
        static string _bubbleFont = "YouYuan";      // 气泡字体（幼圆；可在设置面板里换）
        static string _bubbleStyle = "card";        // 气泡外框：card/classic/comic/glass/custom
        static string _bubbleFrame = "";            // 自定义外框图片（bubbleStyle=custom 时用）
        static Bitmap _bubbleImg;                   // 已解码的自定义外框
        static Color _customInk = Color.FromArgb(0x1A, 0x1A, 0x1A);   // 自定义外框上的字色（按底色自动选深浅）
        static int _customCenterAlpha = 255;        // 外框中间的平均不透明度（低 = 镂空，需要垫底色）
        // 气泡配色（#RRGGBB；空 = 默认蓝）
        static Color _bubbleAccent = Color.FromArgb(0x8F, 0xA6, 0xD8);   // 描边 / 渐变副色
        static float _bubbleFontPt = 10.5f;         // 气泡字号（pt，可调；长文本不缩字号，改为滚动）
        static Color? _bubbleInkFix = null;         // 固定文字颜色（bubbleInk=#RRGGBB；auto = 跟着底色自动）
        static float _bubbleScroll = 0f;            // 长文本滚动偏移（px）：滚轮或拖动驱动，不自动滚
        static bool _bubbleDragging = false;        // 在气泡上按住拖动 = 滚动文本（不是拖角色）
        static int _bubbleDragLastY = 0;
        static int _voiceStartedAt = 0, _voiceDurMs = 0;   // 当前正在放的声音（自动音要靠它排队，别互相截断）
        static string _voicePath = "";                     // 当前正在放的是哪个文件（打断时好写清楚凶手）
        static bool _lineVoicePlaying = false;             // 当前放的是不是"点击台词"（决定松开音能不能放）
        // —— 连击稳定性（用户反馈：连击时屏幕会卡）——
        // 根因三条：① 每次点击都 File.ReadAllBytes 整个 wav 算时长（同步 I/O）
        //          ② 每次播放都 new Thread ③ 每次点击写好几次日志（同步 I/O）
        // 对策：时长缓存 / 单一播放线程串行播 / 日志进内存队列由定时器批量落盘。
        static Dictionary<string, int> _durCache = new Dictionary<string, int>();
        static string _playReq = null;                     // 待播文件（播放线程来取）
        static readonly object _playLock = new object();
        static System.Threading.Thread _playThread = null;
        static string _playingPath = "";                   // 播放线程当前在处理哪个文件
        static int _playSeq = 0, _playingSeq = 0;          // 请求序号（用于丢弃过期的完成日志）
        static readonly List<string> _logQueue = new List<string>();
        static int _lastLineIdx = -1;                      // 上一次播的台词下标（连点避免重复同一句）
        static int _seqLine = 0;                           // 顺序模式的下标
        static int _testClicksLeft = 0;                    // clickTest=N：启动后模拟 N 次连击
        static int _testClickNextAt = 0;
        static int _testWheelLeft = 0;                     // wheelTest=N：启动后模拟 N 次滚轮（验证气泡滚动）
        static int _testWheelNextAt = 0;
        static float _baseFontSize = 10.5f;               // 气泡基准字号（自动缩字号后每帧复位用）
        static bool _linePlayedOnDown = false;     // 本次点击是否已播过台词语音（决定松开音要不要放）
        static int _lastAutoBubble = 0;
        static Bitmap _bmp;                        // 预乘 alpha 离屏位图（GDI+ 画气泡用）
        static Graphics _g;
        static Font _font;
        static StringFormat _sf, _sfTop;
        const int FRAME_MS = 33;

        static int Now { get { return Environment.TickCount; } }

        static string Cfg(string key, string def)
        {
            string v;
            return _cfg.TryGetValue(key, out v) ? v : def;
        }

        // ---------------- WAV 时长（用于把「松开音」排在「按压音」结束前 40ms） ----------------
        static int WavDurationMs(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                int cached;
                if (_durCache.TryGetValue(path, out cached)) return cached;   // 连击时不再反复读文件
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44) return 0;
                int pos = 12, byteRate = 0, dataSize = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt " && pos + 8 + 16 <= b.Length) byteRate = BitConverter.ToInt32(b, pos + 16);
                    else if (id == "data") { dataSize = size; break; }
                    if (size <= 0) break;
                    pos += 8 + size + (size & 1);
                }
                if (byteRate <= 0 || dataSize <= 0) return 0;
                int ms = (int)(1000L * dataSize / byteRate);
                _durCache[path] = ms;
                return ms;
            }
            catch (Exception ex) { LogErr("WavDurationMs", ex); return 0; }
        }

        // 所有播放都走这里。同一时刻只放一个音 => 每次起播都会掐掉上一个。
        // 所以只要"上一个还没播完"，就写一条带来源的日志 —— 这样"没播完"到底被谁掐的，一眼可查。
        static void VoiceEx(string path, string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                int free = VoiceFreeInMs();
                if (free > 0 && _voicePath != "")
                    Status("⚠ 打断: " + reason + " 掐掉了「" + Path.GetFileName(_voicePath) + "」（它还应剩 " + free + "ms）");
                _voiceStartedAt = Now;
                _voiceDurMs = WavDurationMs(path);
                _voicePath = path;
                // 交给单一播放线程串行处理：不再每次点击 new Thread（连击时这是主要卡顿源之一）。
                // 新请求会先 close 掉上一句的 MCI 别名（即时掐断）再播 => 点一下立刻换一句。
                lock (_playLock)
                {
                    _playSeq++;
                    _playReq = path;
                }
            }
            catch (Exception ex) { LogErr("VoiceEx", ex); }
        }

        static void Voice(string path)
        {
            VoiceEx(path, "未标注来源");
        }

        // 唯一的播放线程（MCI）：
        //   取请求 → close 旧别名（即时掐断）+ open/play 新文件 → 轮询 mode/position
        //   → 播完时把**实际播放位置**写进日志（位置 < 文件时长 ⇒ 真被截，≥ ⇒ 播完整）
        static string Mci(string cmd)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(300);
                mciSendStringW(cmd, sb, 300, IntPtr.Zero);
                return sb.ToString();
            }
            catch (Exception ex) { LogErr("Mci", ex); return ""; }
        }

        // ---------------- 音量：按比例衰减 wav 采样 ----------------
        // 为什么不直接用 MCI 的 setaudio volume：waveaudio 驱动不一定实现它（实现了也常被
        // Windows 的每应用音量覆盖），而且音量变了还要能立刻听到。直接改采样最稳：
        //   100% → 原文件；0–99% → 生成一份衰减副本，按 (原路径 + 音量) 缓存。
        // 实现在 Core.cs（与设置面板共用一份；Tests 工程对它跑断言）
        static string FnvHash(string s) { return QCore.FnvHash(s); }

        // 缓存文件名是「源路径哈希 + 音量」，反推不出源文件 ⇒ 单独记一份索引，
        // 启动时按索引清理：源语音没了（换角色 / 删角色 / 素材改名）就把它那份缓存删掉。
        // 以前从不清 ⇒ 音量档 × 语音数，几十兆 wav 会一直堆在目录里。
        static string _volDir = "";
        static readonly Dictionary<string, string> _volIndex = new Dictionary<string, string>();   // 缓存文件名 → 源 wav

        static string VolIndexPath { get { return Path.Combine(_volDir, "index.txt"); } }

        static void NoteVolIndex(string dstName, string src)
        {
            try
            {
                if (_volIndex.ContainsKey(dstName)) return;
                _volIndex[dstName] = src;
                File.AppendAllText(VolIndexPath, dstName + "=" + src + "\r\n");
            }
            catch (Exception ex) { LogErr("NoteVolIndex", ex); }
        }

        static void LoadVolIndex()
        {
            try
            {
                if (_volIndex.Count > 0 || !File.Exists(VolIndexPath)) return;
                foreach (string line in File.ReadAllLines(VolIndexPath))
                {
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    _volIndex[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch (Exception) { }
        }

        static void CleanVolCache()
        {
            try
            {
                if (!Directory.Exists(_volDir)) return;
                LoadVolIndex();
                int dropped = 0;
                List<string> dead = new List<string>();
                foreach (KeyValuePair<string, string> kv in new List<KeyValuePair<string, string>>(_volIndex))
                {
                    string dst = Path.Combine(_volDir, kv.Key);
                    bool stale = false;
                    try { stale = (DateTime.Now - File.GetLastWriteTime(dst)).TotalDays > 30; } catch (Exception) { }
                    // 源文件没了 / 缓存本身没了 / 30 天没用过 ⇒ 都不用留
                    if (!File.Exists(kv.Value) || !File.Exists(dst) || stale)
                    {
                        try { if (File.Exists(dst)) File.Delete(dst); } catch (Exception) { }
                        dead.Add(kv.Key); dropped++;
                    }
                }
                foreach (string k in dead) _volIndex.Remove(k);
                // 没有索引记录的孤儿 wav（旧版本留下来的）也清掉
                foreach (string f in Directory.GetFiles(_volDir, "*.wav"))
                {
                    string n = Path.GetFileName(f);
                    if (_volIndex.ContainsKey(n)) continue;
                    try { File.Delete(f); dropped++; } catch (Exception) { }
                }
                if (dropped > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<string, string> kv in _volIndex) sb.AppendLine(kv.Key + "=" + kv.Value);
                    File.WriteAllText(VolIndexPath, sb.ToString());
                    Status("音量缓存清理: 删除 " + dropped + " 个失效副本，保留 " + _volIndex.Count + " 个");
                }
            }
            catch (Exception) { }
        }

        static string PrepareVolumeFile(string path, int vol)
        {
            if (vol >= 100 || vol < 0) return path;
            try
            {
                string key = vol + "|" + path;
                string hit;
                if (_volCache.TryGetValue(key, out hit) && File.Exists(hit)) return hit;
                if (_volDir == "") _volDir = Path.Combine(_baseDir, "pet-vol-cache");
                Directory.CreateDirectory(_volDir);
                string name = FnvHash(path) + ".v" + vol + ".wav";
                string dst = Path.Combine(_volDir, name);
                if (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(path))
                {
                    if (!WriteScaledWav(path, dst, vol)) return path;
                    NoteVolIndex(name, path);          // 只在新生成时记一笔
                }
                _volCache[key] = dst;
                return dst;
            }
            catch (Exception ex) { LogErr("PrepareVolumeFile", ex); return path; }
        }

        // 只处理 16bit / 8bit PCM（本项目所有语音都是 pcm_s16le）；
        // 其它格式原样返回，宁可音量不变也不要放不出来。
        static bool WriteScaledWav(string src, string dst, int vol)
        {
            try
            {
                byte[] b = File.ReadAllBytes(src);
                if (b.Length < 44) return false;
                int pos = 12, dataOff = -1, dataLen = 0, bits = 0, fmt = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt " && pos + 8 + 16 <= b.Length)
                    {
                        fmt = BitConverter.ToInt16(b, pos + 8);
                        bits = BitConverter.ToInt16(b, pos + 22);
                    }
                    else if (id == "data")
                    {
                        dataOff = pos + 8;
                        dataLen = Math.Min(size, b.Length - dataOff);
                        break;
                    }
                    if (size <= 0) break;
                    pos += 8 + size + (size & 1);
                }
                if (dataOff < 0 || dataLen <= 0 || fmt != 1) return false;
                float g = vol / 100f;
                if (bits == 16)
                {
                    for (int i = 0; i + 1 < dataLen; i += 2)
                    {
                        int v = (int)(BitConverter.ToInt16(b, dataOff + i) * g);
                        if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                        b[dataOff + i] = (byte)(v & 0xFF);
                        b[dataOff + i + 1] = (byte)((v >> 8) & 0xFF);
                    }
                }
                else if (bits == 8)
                {
                    for (int i = 0; i < dataLen; i++)
                    {
                        int v = (int)((b[dataOff + i] - 128) * g) + 128;
                        if (v > 255) v = 255; else if (v < 0) v = 0;
                        b[dataOff + i] = (byte)v;
                    }
                }
                else return false;
                File.WriteAllBytes(dst, b);
                return true;
            }
            catch (Exception) { return false; }
        }

        static void InitPlayThread()
        {
            if (_playThread != null) return;
            _playThread = new System.Threading.Thread(delegate()
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(40);
                    string req = null;
                    int seq = 0;
                    lock (_playLock) { req = _playReq; _playReq = null; if (req != null) { seq = _playSeq; _playingPath = req; _playingSeq = seq; } }
                    try
                    {
                        if (req != null)
                        {
                            string playPath = PrepareVolumeFile(req, _volume);
                            Mci("close petvoice");                     // 即时掐断上一句
                            Mci("open \"" + playPath + "\" type waveaudio alias petvoice");
                            Mci("play petvoice");
                            continue;
                        }
                        string pth;
                        int cur;
                        lock (_playLock) { pth = _playingPath; cur = _playingSeq; }
                        if (pth == "") continue;
                        string mode = Mci("status petvoice mode");
                        if (mode != "stopped") continue;
                        int pos = 0, len = 0;
                        int.TryParse(Mci("status petvoice position"), out pos);
                        int.TryParse(Mci("status petvoice length"), out len);
                        if (pos < 200 && len > 600) continue;          // 刚 open 还没真正开始时不算播完
                        lock (_playLock)
                        {
                            if (cur != _playSeq) { _playingPath = ""; continue; }   // 已经被新请求取代
                            _playingPath = "";
                            Mci("close petvoice");
                        }
                        Status("音频层播完: " + Path.GetFileName(pth) + "  实际位置 " + pos + "ms / 文件时长 "
                               + (len > 0 ? len : WavDurationMs(pth)) + "ms");
                    }
                    catch (Exception ex) { LogErr("InitPlayThread", ex); }
                }
            });
            _playThread.IsBackground = true;
            _playThread.Start();
        }

        static int VoiceFreeInMs()
        {
            return (_voiceStartedAt + _voiceDurMs + 250) - Now;   // >0 表示还有话没说完
        }

        static string LangVoice(string slot)
        {
            return Cfg("lang" + _lang + slot, "");
        }

        // 动画倍速：配置里存百分数（50–200），运行时换成倍率
        static double ReadSpeed(string key, double fallback)
        {
            string s = Cfg(key, "");
            double v;
            if (s != "" && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return Math.Max(0.5, Math.Min(2.0, v / 100.0));
            return fallback;
        }

        static string FmtSpeed(double v)
        {
            return (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        static string FmtSec(int s) { return QCore.FmtSec(s); }   // 实现在 Core.cs

        // ---------------- 动画状态机 ----------------
        // 四类动画各有一个开关（设置面板「动画」页）：
        //   待机呼吸 / 按键反馈（全局键盘鼠标）/ 点击反馈（点她本人）/ 拖动摆动
        // 帧布局：待机 [0,idle) → 按键 [idle,+press) → 点击 [+press,+click) → 拖动 [+click,+drag)
        static int PressStart { get { return _idleN; } }
        static int ClickStart { get { return _idleN + _pressN; } }
        static int DragStart { get { return _idleN + _pressN + _clickN; } }
        static int TotalFrames { get { return _idleN + _pressN + _clickN + _dragN; } }

        static void TriggerPress(bool fromClick)
        {
            _lastInputAt = Now;
            if (fromClick) { if (!_animClick) return; _pressKind = "click"; }
            else { if (!_animPress) return; _pressKind = "press"; }
            if (_phase == "idle" || _phase == "rebound") { _phase = "squash"; _phaseStart = Now; }
            // 已在 squash/hold 阶段：只刷新 _lastInputAt（连续输入时保持压扁）
        }

        static void AdvanceAnim()
        {
            // 拖动中：整组切到「被拎起来」的悬空摆动帧（脚不再锚定，见 sprites.py 的 DRAG_*）
            if (_animDrag && _dragging && _dragMoved && _dragN > 0)
            {
                _dragPhase += _speedDrag;
                if (_dragPhase >= _dragN * 1000.0) _dragPhase = 0;
                _frameIndex = DragStart + ((int)_dragPhase % _dragN);
                return;
            }
            // 按压和点击各有自己的帧段与速度；老角色包没有点击帧时退回按压帧
            bool isClick = (_pressKind == "click" && _clickN > 0);
            int baseIdx = isClick ? ClickStart : PressStart;
            int count = isClick ? _clickN : _pressN;
            double speed = isClick ? _speedClick : _speedPress;
            int deep = count - 3;                         // 最扁的那一帧

            if (_phase == "squash")
            {
                int step = (int)((Now - _phaseStart) * speed / FRAME_MS);
                int idx = 1 + step;                       // 1..deep
                if (idx >= deep) { idx = deep; _phase = "hold"; _phaseStart = Now; }
                _frameIndex = baseIdx + Math.Min(idx, count - 1);
            }
            else if (_phase == "hold")
            {
                _frameIndex = baseIdx + deep;
                if (Now - _lastInputAt > _holdMs) { _phase = "rebound"; _phaseStart = Now; }
            }
            else if (_phase == "rebound")
            {
                int step = (int)((Now - _phaseStart) * speed / FRAME_MS);
                int idx = deep + 1 + step;                // deep+1..count-1
                if (idx >= count - 1) { _frameIndex = baseIdx + (count - 1); _phase = "idle"; }
                else _frameIndex = baseIdx + idx;
            }
            else
            {
                // 待机：呼吸浮动循环；关掉时定格在第一帧（不再抖动）
                if (!_animIdle) { _frameIndex = 0; }
                else
                {
                    _idlePhase += _speedIdle;
                    if (_idlePhase >= _idleN * 1000.0) _idlePhase = 0;
                    _frameIndex = ((int)_idlePhase) % _idleN;
                }
            }
            if (_frameIndex < 0 || _frameIndex >= TotalFrames) _frameIndex = 0;
        }

        static void Blit()
        {
            if (_frames == null || _bits == IntPtr.Zero) return;
            int winH = _h + _bubbleH;
            if (_bubbleH > 0)
            {
                // 顶部气泡区先清零（清掉上一次的气泡残留），再把角色帧贴到下方
                Marshal.Copy(_zeroTop, 0, _bits, _zeroTop.Length);
                Marshal.Copy(_frames, _frameIndex * _frameBytes, IntPtr.Add(_bits, _zeroTop.Length), _frameBytes);
                if (_bubbleText != "" && Now < _bubbleUntil) DrawBubbleInto();
            }
            else
            {
                Marshal.Copy(_frames, _frameIndex * _frameBytes, _bits, _frameBytes);
            }
            POINT dst = new POINT();
            RECT r; GetWindowRect(_hwnd, out r);
            dst.X = r.Left; dst.Y = r.Top;
            SIZE size = new SIZE(); size.cx = _w; size.cy = winH;
            POINT src = new POINT(); src.X = 0; src.Y = 0;
            BLENDFUNCTION bf = new BLENDFUNCTION();
            bf.BlendOp = AC_SRC_OVER; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = AC_SRC_ALPHA;
            UpdateLayeredWindow(_hwnd, _screenDc, ref dst, ref size, _memDc, ref src, 0, ref bf, ULW_ALPHA);
        }

        // ---------------- 位置持久化 ----------------
        static void LoadPos(out int x, out int y)
        {
            x = int.MinValue; y = int.MinValue;
            try
            {
                string p = Cfg("posfile", "");
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    foreach (string line in File.ReadAllLines(p))
                    {
                        string[] kv = line.Split('=');
                        if (kv.Length == 2)
                        {
                            int v;
                            if (int.TryParse(kv[1].Trim(), out v)) { if (kv[0].Trim() == "x") x = v; else if (kv[0].Trim() == "y") y = v; }
                        }
                    }
                }
            }
            catch (Exception ex) { LogErr("LoadPos", ex); }
            if (x == int.MinValue || y == int.MinValue)
            {
                int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
                x = sw - _w - 40; y = sh - _h - 80;
            }
        }

        static void SavePos()
        {
            try
            {
                string p = Cfg("posfile", "");
                if (string.IsNullOrEmpty(p)) return;
                RECT r; GetWindowRect(_hwnd, out r);
                File.WriteAllText(p, "x=" + r.Left + "\r\ny=" + (r.Top + _bubbleH) + "\r\n");   // 存「角色」位置
            }
            catch (Exception ex) { LogErr("SavePos", ex); }
        }

        // ---------------- 位置可见性：老位置可能落在已经拔掉的显示器上 ----------------
        // 以前只按「主屏分辨率」夹一次：副屏拔了 / 改了分辨率 / 换了显示器排布，
        // 桌宠就可能停在屏幕外，用户只能手删 pet-pos.txt。这里改成按「任一显示器的工作区」判。
        static bool WorkAreaAt(POINT pt, out RECT wa)
        {
            wa = new RECT();
            try
            {
                IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONULL);
                if (mon == IntPtr.Zero) return false;
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfoW(mon, ref mi)) return false;
                wa = mi.rcWork;
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool ClampToVisible(ref int x, ref int y, bool log)
        {
            bool changed = false;
            try
            {
                POINT c = new POINT();
                c.X = x + _w / 2; c.Y = y + _h / 2;
                RECT wa;
                bool moved = false;
                if (!WorkAreaAt(c, out wa))
                {
                    // 不在任何显示器上 → 回主屏右下角（和首次启动的默认位一致）
                    POINT home = new POINT(); home.X = 0; home.Y = 0;
                    if (!WorkAreaAt(home, out wa))
                    {
                        wa.Left = 0; wa.Top = 0;
                        wa.Right = GetSystemMetrics(0); wa.Bottom = GetSystemMetrics(1);
                    }
                    x = Math.Max(wa.Left, wa.Right - _w - 40);
                    y = Math.Max(wa.Top + _bubbleH, wa.Bottom - _h - 80);
                    moved = true;
                }
                int ox = x, oy = y;
                if (x + _w > wa.Right) x = wa.Right - _w;
                if (x < wa.Left) x = wa.Left;
                if (y - _bubbleH < wa.Top) y = wa.Top + _bubbleH;      // 窗口上沿 = 角色 Y - 气泡高
                if (y + _h > wa.Bottom) y = wa.Bottom - _h;
                changed = moved || x != ox || y != oy;
                if (changed && log) Status("位置夹取: " + ox + "," + oy + " → " + x + "," + y
                        + "（可视工作区 " + wa.Left + "," + wa.Top + "–" + wa.Right + "," + wa.Bottom + "）");
            }
            catch (Exception ex) { LogErr("ClampToVisible", ex); }
            return changed;
        }

        // 显示器/分辨率变了之后把桌宠拉回可视区（并返回是否动过）
        static bool RepositionToVisible(string why)
        {
            try
            {
                RECT wr; GetWindowRect(_hwnd, out wr);
                int nx = wr.Left, ny = wr.Top + _bubbleH;
                bool changed = ClampToVisible(ref nx, ref ny, false);
                if (changed)
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, nx, ny - _bubbleH, _w, _h + _bubbleH, SWP_NOACTIVATE);
                    Status(why + " → 位置已夹回可视区: " + nx + "," + ny);
                    SavePos();
                    Blit();
                }
                else Status(why + " → 位置无需调整");
                return changed;
            }
            catch (Exception ex) { LogErr("RepositionToVisible", ex); return false; }
        }

        // DPI 变了：精灵图保持原始像素（放大只会糊），但气泡文字要跟着放大才看得清
        static void ApplyDpiToBubble(uint dpi)
        {
            try
            {
                if (_bubbleH <= 0 || _g == null) return;
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                float fs = (float)Math.Max(6.0, Math.Min(36.0, _bubbleFontPt * scale));
                if (_font != null) { try { _font.Dispose(); } catch (Exception) { } }
                _font = new Font(_bubbleFont, fs, FontStyle.Regular, GraphicsUnit.Point);
                Status("气泡字号随 DPI 调整: " + _bubbleFontPt.ToString("0.0") + "pt → "
                       + fs.ToString("0.0", CultureInfo.InvariantCulture) + "pt（" + dpi + " dpi）");
            }
            catch (Exception ex) { LogErr("ApplyDpiToBubble", ex); }
        }

        // ---------------- 托盘图标（右键菜单已按要求**完全取消**） ----------------
        // 左键点托盘图标 → 打开设置面板；桌宠身上的右键不做任何事。
        const int WM_TRAY = WM_APP + 3;
        const uint NIM_ADD = 0, NIM_DELETE = 2;
        const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr LoadImageW(IntPtr hinst, string name, uint type, int cx, int cy, uint load);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static IntPtr _trayIcon = IntPtr.Zero;
        static bool _trayAdded = false;

        // 把图片文件做成带 alpha 的 HICON（托盘用）。
        // .ico 直接 LoadImageW；png/jpg/bmp 走 GDI+ 解码后 GetHicon——
        // LoadImageW(IMAGE_ICON) 只认 .ico/.cur，喂 PNG 会返回 NULL（托盘就空白了）。
        static IntPtr IconFromImageFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return IntPtr.Zero;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".ico" || ext == ".cur")
                    return LoadImageW(IntPtr.Zero, path, 1 /*IMAGE_ICON*/, 0, 0, 0x10 /*LR_LOADFROMFILE*/);
                using (Bitmap src = new Bitmap(path))
                {
                    int sz = Math.Min(32, Math.Max(16, Math.Min(src.Width, src.Height)));
                    using (Bitmap bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, new Rectangle(0, 0, sz, sz), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
                        return bmp.GetHicon();   // 32bpp ARGB ⇒ 保留透明
                    }
                }
            }
            catch (Exception ex) { LogErr("IconFromImageFile", ex); return IntPtr.Zero; }
        }

        static void AddTrayIcon()
        {
            try
            {
                if (_trayIcon != IntPtr.Zero) { DestroyIcon(_trayIcon); _trayIcon = IntPtr.Zero; }
                string ico = Cfg("icopath", "");
                if (string.IsNullOrEmpty(ico) || !File.Exists(ico)) ico = Path.Combine(_baseDir, "pet.png");
                _trayIcon = IconFromImageFile(ico);
                if (_trayIcon == IntPtr.Zero) _trayIcon = IconFromImageFile(Path.Combine(_baseDir, "pet.ico"));
                NOTIFYICONDATAW d = new NOTIFYICONDATAW();
                d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
                d.hWnd = _hwnd;
                d.uID = 1;
                d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                d.uCallbackMessage = (uint)WM_TRAY;
                d.hIcon = _trayIcon;
                d.szTip = "Q Pet · 双击打开设置 · 右键菜单";
                _trayAdded = Shell_NotifyIconW(NIM_ADD, ref d);
                Status("托盘图标: " + (_trayAdded ? "已添加（双击打开设置）" : "添加失败")
                    + " 图标句柄=" + (_trayIcon != IntPtr.Zero ? "有效" : "无效（将显示空白）"));
            }
            catch (Exception ex) { Status("托盘图标异常: " + ex.Message); }
        }

        static void RemoveTrayIcon()
        {
            try
            {
                if (!_trayAdded) return;
                NOTIFYICONDATAW d = new NOTIFYICONDATAW();
                d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
                d.hWnd = _hwnd; d.uID = 1;
                Shell_NotifyIconW(NIM_DELETE, ref d);
                _trayAdded = false;
                if (_trayIcon != IntPtr.Zero) { DestroyIcon(_trayIcon); _trayIcon = IntPtr.Zero; }
            }
            catch (Exception ex) { LogErr("RemoveTrayIcon", ex); }
        }

        // 右键托盘图标 → 菜单：打开设置 / 关闭桌宠
        const uint TPM_RIGHTBUTTON = 0x0002;
        const int IDM_OPEN = 0x8F01, IDM_QUIT = 0x8F02;

        static void ShowTrayMenu()
        {
            IntPtr hMenu = IntPtr.Zero;
            try
            {
                hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return;
                AppendMenuW(hMenu, MF_STRING, (IntPtr)IDM_OPEN, "打开设置");
                AppendMenuW(hMenu, MF_STRING, (IntPtr)IDM_QUIT, "关闭桌宠");
                POINT pt; GetCursorPos(out pt);
                Status("托盘菜单: 打开设置 / 关闭桌宠（" + pt.X + "," + pt.Y + "）");
                // TrackPopupMenu 要把主窗口弄到前台，否则菜单点完不消失；它是模态的，会阻塞到菜单关闭
                SetForegroundWindow(_hwnd);
                TrackPopupMenu(hMenu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            }
            catch (Exception ex) { Status("托盘菜单异常: " + ex.Message); }
            finally
            {
                if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            }
        }

        // 双击托盘图标 → 打开设置面板（**单实例**：已经开着就不再开一个）
        static System.Diagnostics.Process _settingsProc = null;

        static void OpenSettings()
        {
            try
            {
                if (_settingsProc != null && !_settingsProc.HasExited)
                {
                    Status("设置面板已经开着，忽略本次双击");
                    return;
                }
                // 优先启动**独立设置程序**（不需要 PowerShell）
                // 注意：帧文件现在在 chars\<角色>\动画\ 里，所以设置程序要按程序目录找，不能按 frames 的目录找
                string dir = _baseDir;
                string setExe = Path.Combine(dir, "PetSettings.exe");
                if (!string.IsNullOrEmpty(setExe) && File.Exists(setExe))
                {
                    _settingsProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setExe)
                    {
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    });
                    Status("托盘双击：打开设置（PetSettings.exe）");
                    return;
                }
                // 兜底：旧版 PowerShell 脚本
                string sp = Cfg("settings", "");
                if (string.IsNullOrEmpty(sp) || !File.Exists(sp)) { Status("设置面板路径未配置（settings=）"); return; }
                _settingsProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + sp + "\"")
                {
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
                Status("打开设置面板（PowerShell 兜底，托盘双击）");
            }
            catch (Exception ex) { Status("打开设置面板失败: " + ex.Message); }
        }

        // ================= 对话框（动态气泡） =================
        // 数据源全部是本地文件，不碰网络、不依赖任何宿主：
        //   pet-lines.txt -> 气泡随机台词（由她的真实台词筛出）
        //   pet-lines.tsv -> 台词 <TAB> 语音文件（点击时播放）
        static void LoadLines()
        {
            try
            {
                string p = Cfg("lines", "");
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    _lines = File.ReadAllLines(p);
            }
            catch (Exception ex) { LogErr("LoadLines", ex); }
            // 带语音的台词清单（每行：文字 <TAB> wav 路径）。
            // 点她 = 气泡显示这句 + 播**这句自己的**语音 ⇒ 文字与声音必然一致。
            try
            {
                string vp = Cfg("voicelines", "");
                if (!string.IsNullOrEmpty(vp) && File.Exists(vp))
                {
                    List<string> tx = new List<string>(), fp = new List<string>();
                    foreach (string ln in File.ReadAllLines(vp))
                    {
                        int tab = ln.IndexOf('\t');
                        if (tab <= 0) continue;
                        string path = ln.Substring(tab + 1).Trim();
                        // 相对路径：角色包里的清单按角色目录解析，老全局清单按程序目录解析
                        if (path != "" && !Path.IsPathRooted(path))
                            path = Path.Combine(string.IsNullOrEmpty(_charDir) ? _baseDir : _charDir, path);
                        if (path != "" && File.Exists(path)) { tx.Add(ln.Substring(0, tab)); fp.Add(path); }
                    }
                    _lineText = tx.ToArray(); _linePath = fp.ToArray();
                }
            }
            catch (Exception ex) { LogErr("LoadLines", ex); }
            Status("气泡台词池 " + _lines.Length + " 条；带语音 " + _lineText.Length + " 条；气泡区高 " + _bubbleH + "px");
        }

        // 主动冒泡：优先从「当前角色带语音的台词」里选，保证气泡里那句在她的语音库里；
        // 只有语音清单为空时才退回纯文字台词。
        static string RandomLine()
        {
            if (_lineText.Length > 0) return _lineText[_rnd.Next(_lineText.Length)];
            return _lines.Length == 0 ? "" : _lines[_rnd.Next(_lines.Length)];
        }

        // 点她 = 气泡显示一句台词 + 播**这句台词自己的**语音（同一份清单 ⇒ 文字与声音必然一致）。
        // 返回是否播了音（供「已播台词就不再叠按压/松开音」判断）。
        static bool DoClickLine()
        {
            if (_lineText.Length == 0)
            {
                ShowBubble(RandomLine(), 5000);              // 清单缺失 → 退回纯文字
                return false;
            }
            int li;
            if (Cfg("clickLineMode", "random") == "seq")
            {
                li = _seqLine % _lineText.Length;
                _seqLine = (li + 1) % _lineText.Length;
            }
            else
            {
                li = _rnd.Next(_lineText.Length);
                // 连点时尽量别连着播同一句（手气不好重复时换下一句）
                if (_lineText.Length > 1 && li == _lastLineIdx) li = (li + 1) % _lineText.Length;
            }
            _lastLineIdx = li;
            // 气泡存在时长 = max(6 秒, 语音时长 + 1.5 秒) —— 长语音不会再"没播完气泡就没了"
            bool play = Cfg("clickLine", "1") == "1";
            int dur = play ? WavDurationMs(_linePath[li]) : 0;
            if (play)
            {
                VoiceEx(_linePath[li], "点击台词");   // 点击台词
                _lineVoicePlaying = true;
            }
            ShowBubble(_lineText[li], Math.Max(6000, dur + 1500));
            if (play) Status("台词语音: " + _lineText[li] + "   ->   " + Path.GetFileName(_linePath[li])
                   + "（时长 " + dur + "ms，气泡 " + Math.Max(6000, dur + 1500) + "ms）");
            return play;
        }

        static void ShowBubble(string text, int ms)
        {
            if (_bubbleH <= 0 || string.IsNullOrEmpty(text)) return;
            _bubbleText = text;
            _bubbleUntil = Now + ms;
            _bubbleShownAt = Now;
            _bubbleScroll = 0; _bubbleDragging = false;   // 换一句就回到顶部，等用户自己滚
            Status("气泡: " + text.Replace("\n", " / "));
        }

        // 气泡轮廓：圆角矩形 + 底部一个小尾巴（一条闭合路径 ⇒ 描边不会在内部留下接缝）
        static GraphicsPath BubblePath(float l, float t, float r, float b, float rad,
                                       float tailL, float tailR, float tipX, float tipY)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(l, t, d, d, 180, 90);            // 左上
            p.AddArc(r - d, t, d, d, 270, 90);        // 右上
            p.AddArc(r - d, b - d, d, d, 0, 90);      // 右下
            p.AddLine(new PointF(tailR, b), new PointF(tipX, tipY));   // 底边 → 尾巴尖
            p.AddLine(new PointF(tipX, tipY), new PointF(tailL, b));   // 尾巴尖 → 左根
            p.AddArc(l, b - d, d, d, 90, 90);          // 左下
            p.CloseFigure();
            return p;
        }

        // ---------------- 气泡外框（内置样式 / 自定义图片） ----------------
        // 外框（投影 + 底色 + 描边，不含文字）只在「尺寸 / 样式 / 外框图」变化时渲染一次，
        // 之后每帧只按 alpha 把这张图贴上去 —— 云朵、自绘图这类重活也只做一次，不影响帧率。
        static Bitmap _bubbleSkin;
        static string _bubbleSkinKey = "";
        static RectangleF _textRect = RectangleF.Empty;
        static Color _bubbleInk = Color.FromArgb(255, 0x3F, 0x4F, 0x7D);
        static int _bubbleSkinCount = 0;

        static GraphicsPath RoundRectPath(float l, float t, float r, float b, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r - l, b - t));
            if (d <= 0.5f) { p.AddRectangle(new RectangleF(l, t, Math.Max(1f, r - l), Math.Max(1f, b - t))); return p; }
            p.AddArc(l, t, d, d, 180, 90);
            p.AddArc(r - d, t, d, d, 270, 90);
            p.AddArc(r - d, b - d, d, d, 0, 90);
            p.AddArc(l, b - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 九宫格拉伸：四角按比例、只拉伸边和中间。分块用整数像素对齐，
        // 相邻块共用边界 ⇒ 不会出现重叠发暗或缝隙亮线。
        static void DrawNineSlice(Graphics g, Image img, RectangleF dst, float insX, float insY, float alpha)
        {
            int W = img.Width, H = img.Height;
            float scale = Math.Min(dst.Width / W, dst.Height / H);
            int cw = Math.Max(1, (int)Math.Round(Math.Min(insX * W * scale, dst.Width * 0.5f - 1)));
            int ch = Math.Max(1, (int)Math.Round(Math.Min(insY * H * scale, dst.Height * 0.5f - 1)));
            int x0 = (int)Math.Round(dst.Left), y0 = (int)Math.Round(dst.Top);
            int w = Math.Max(2, (int)Math.Round(dst.Width)), h = Math.Max(2, (int)Math.Round(dst.Height));
            int scw = Math.Max(1, (int)Math.Round(insX * W)), sch = Math.Max(1, (int)Math.Round(insY * H));

            int[] dxs = { x0, x0 + cw, x0 + w - cw, x0 + w };
            int[] dys = { y0, y0 + ch, y0 + h - ch, y0 + h };
            int[] sxs = { 0, scw, W - scw, W };
            int[] sys = { 0, sch, H - sch, H };

            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = alpha;
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        int dw = dxs[i + 1] - dxs[i], dh = dys[j + 1] - dys[j];
                        int sw = sxs[i + 1] - sxs[i], sh = sys[j + 1] - sys[j];
                        if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) continue;
                        g.DrawImage(img, new Rectangle(dxs[i], dys[j], dw, dh),
                            sxs[i], sys[j], sw, sh, GraphicsUnit.Pixel, ia);
                    }
            }
        }

        // ---------- 气泡配色小工具 ----------
        static Color Mix(Color a, Color b, double t)
        {
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t),
                                  (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t),
                                  (int)(a.B + (b.B - a.B) * t));
        }
        static Color Lighten(Color c, double t) { return Mix(c, Color.FromArgb(0xFF, 0xFF, 0xFF), t); }
        static Color Darken(Color c, double t) { return Mix(c, Color.FromArgb(0x00, 0x00, 0x00), t); }
        static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }
        // 按主色推导文字色：亮主色 → 深字，暗主色 → 浅字
        static Color InkFor(Color accent) { return Lum(accent) > 0.62 ? Darken(accent, 0.72) : Lighten(accent, 0.90); }

        static string ColorToHex(Color c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        static Color ParseHexColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().TrimStart('#');
            if (s.Length == 6)
            {
                int r, g, b;
                if (int.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                    int.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                    int.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    return Color.FromArgb(0xFF, r, g, b);
            }
            return fallback;
        }

        // 读自定义外框：拷一份进内存（不锁文件，用户随时能替换），并按中间区域自动选字色
        static void LoadBubbleFrame()
        {
            if (_bubbleImg != null) { _bubbleImg.Dispose(); _bubbleImg = null; }
            _bubbleSkinKey = "";
            try
            {
                if (string.IsNullOrEmpty(_bubbleFrame) || !File.Exists(_bubbleFrame))
                {
                    Status("⚠ 自定义气泡外框不存在: " + _bubbleFrame);
                    return;
                }
                using (Bitmap raw = new Bitmap(_bubbleFrame))
                    _bubbleImg = new Bitmap(raw);
                AnalyzeBubbleFrame(_bubbleImg);
                Status("自定义气泡外框: " + Path.GetFileName(_bubbleFrame)
                    + "  " + _bubbleImg.Width + "x" + _bubbleImg.Height
                    + "  中间不透明度=" + _customCenterAlpha
                    + "  字色=" + (_customInk.R > 128 ? "浅" : "深"));
            }
            catch (Exception ex) { Status("⚠ 读自定义气泡外框失败: " + ex.Message); }
        }

        static void AnalyzeBubbleFrame(Bitmap b)
        {
            int x0 = b.Width * 3 / 10, x1 = b.Width * 7 / 10;
            int y0 = b.Height * 3 / 10, y1 = b.Height * 7 / 10;
            double lum = 0, aSum = 0;
            int n = 0;
            for (int y = y0; y < y1; y += 2)
                for (int x = x0; x < x1; x += 2)
                {
                    Color c = b.GetPixel(x, y);
                    double a = c.A / 255.0;
                    lum += (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * a;
                    aSum += a;
                    n++;
                }
            _customCenterAlpha = n == 0 ? 255 : (int)Math.Round(aSum / n * 255.0);
            double avgL = aSum < 0.5 ? 255 : lum / aSum;        // 中间是镂空的 → 当作亮底
            _customInk = avgL > 132
                ? Color.FromArgb(0x1A, 0x1A, 0x1A)
                : Color.FromArgb(0xF7, 0xF9, 0xFF);
        }

        static void BuildBubbleSkin()
        {
            string style = string.IsNullOrEmpty(_bubbleStyle) ? "card" : _bubbleStyle;
            if (style == "custom" && _bubbleImg == null) style = "card";
            long stamp = 0;
            if (style == "custom" && !string.IsNullOrEmpty(_bubbleFrame))
                try { stamp = File.GetLastWriteTimeUtc(_bubbleFrame).Ticks; } catch (Exception) { }
            string key = _w + "x" + _bubbleH + "|" + style + "|" + _bubbleFrame + "|" + stamp
                + "|" + _bubbleAccent.ToArgb().ToString(CultureInfo.InvariantCulture);
            if (_bubbleSkin != null && key == _bubbleSkinKey) return;

            if (_bubbleSkin != null) { _bubbleSkin.Dispose(); _bubbleSkin = null; }
            Bitmap b = new Bitmap(_w, _bubbleH, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float sw = Math.Max(1.4f, _w * 4.6f / 1026f);          // 描边随窗口宽度折算
                float pad = Math.Max(9f, _w * 0.052f);
                float tailH = Math.Min(15f, _bubbleH * 0.15f);
                float bodyL = sw + 1.5f, bodyR = _w - sw - 1.5f;
                float bodyT = sw + 1.5f, bodyB = _bubbleH - tailH - sw - 1.5f;
                float radius = Math.Max(8f, Math.Min(15f, (bodyB - bodyT) * 0.32f));
                float cx = (bodyL + bodyR) / 2f;
                float cy = (bodyT + bodyB) / 2f;
                float tailTipY = bodyB + tailH;
                float tailL = cx - 15f, tailR = cx + 7f, tipX = cx - 5f;

                _bubbleInk = InkFor(_bubbleAccent);
                _textRect = new RectangleF(bodyL + pad, bodyT + pad * 0.72f,
                    Math.Max(20f, (bodyR - bodyL) - pad * 2f), Math.Max(16f, (bodyB - bodyT) - pad * 1.45f));

                if (style == "custom" && _bubbleImg != null)
                {
                    DrawNineSlice(g, _bubbleImg, new RectangleF(0, 0, _w, _bubbleH), 0.26f, 0.30f, 1f);
                    // 外框自带尾巴在底部，文字区底部多留 16% 免得压在尾巴上
                    float insetX = pad * 1.15f, insetT = pad * 0.95f;
                    float insetB = Math.Max(pad * 1.2f, _bubbleH * 0.16f);
                    _textRect = new RectangleF(insetX, insetT,
                        Math.Max(20f, _w - insetX * 2f), Math.Max(16f, _bubbleH - insetT - insetB));
                    // 中间是镂空的（透明）就垫一层半透明白，否则字压在桌面上看不清
                    if (_customCenterAlpha < 110)
                    {
                        using (SolidBrush fb = new SolidBrush(Color.FromArgb(158, 0xFF, 0xFF, 0xFF)))
                        using (GraphicsPath bg = RoundRectPath(_textRect.Left - 5, _textRect.Top - 5,
                                   _textRect.Right + 5, _textRect.Bottom + 5, 10f))
                            g.FillPath(fb, bg);
                    }
                    _bubbleInk = _customInk;
                }
                else if (style == "classic")
                {
                    float rx = (bodyR - bodyL) / 2f, ry = (bodyB - bodyT) / 2f;
                    float ecx = cx, ecy = (bodyT + bodyB) / 2f;
                    float csw = Math.Max(2.4f, sw * 2.1f);
                    float r1 = _w * 0.048f, x1 = cx - rx * 0.74f, y1 = _bubbleH - r1 * 1.25f;
                    float r2 = _w * 0.031f, x2 = cx - rx * 0.33f, y2 = _bubbleH - r2 * 0.85f;
                    Color stroke = Darken(_bubbleAccent, 0.30);
                    Color shd = Darken(_bubbleAccent, 0.45);
                    using (SolidBrush sh = new SolidBrush(Color.FromArgb(22, shd.R, shd.G, shd.B)))
                    {
                        g.FillEllipse(sh, ecx - rx, ecy - ry + 3, rx * 2, ry * 2);
                        g.FillEllipse(sh, x1 - r1, y1 - r1 * 0.7f + 2, r1 * 2, r1 * 1.4f);
                        g.FillEllipse(sh, x2 - r2, y2 - r2 * 0.7f + 2, r2 * 2, r2 * 1.4f);
                    }
                    using (SolidBrush fb = new SolidBrush(Color.White))
                    using (Pen pen = new Pen(stroke, csw))
                    {
                        g.FillEllipse(fb, ecx - rx, ecy - ry, rx * 2, ry * 2);
                        g.DrawEllipse(pen, ecx - rx, ecy - ry, rx * 2, ry * 2);
                        g.FillEllipse(fb, x1 - r1, y1 - r1 * 0.7f, r1 * 2, r1 * 1.4f);
                        g.DrawEllipse(pen, x1 - r1, y1 - r1 * 0.7f, r1 * 2, r1 * 1.4f);
                        g.FillEllipse(fb, x2 - r2, y2 - r2 * 0.7f, r2 * 2, r2 * 1.4f);
                        g.DrawEllipse(pen, x2 - r2, y2 - r2 * 0.7f, r2 * 2, r2 * 1.4f);
                    }
                    _bubbleInk = Darken(_bubbleAccent, 0.55);
                    _textRect = new RectangleF(ecx - rx * 0.80f, ecy - ry * 0.74f, rx * 1.60f, ry * 1.48f);
                }
                else if (style == "comic")
                {
                    using (GraphicsPath path = BubblePath(bodyL, bodyT, bodyR, bodyB, 4f, tailL, tailR, tipX, tailTipY))
                    {
                        Color cLine = Darken(_bubbleAccent, 0.55);
                        Color cSh = Darken(_bubbleAccent, 0.62);
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(40, cSh.R, cSh.G, cSh.B)))
                        {
                            g.TranslateTransform(5f, 5f);
                            g.FillPath(sh, path);
                            g.TranslateTransform(-5f, -5f);
                        }
                        using (SolidBrush fb = new SolidBrush(Color.White))
                        using (Pen pen = new Pen(cLine, Math.Max(2.4f, sw * 1.7f)))
                        {
                            g.FillPath(fb, path);
                            g.DrawPath(pen, path);
                        }
                    }
                    _bubbleInk = Darken(_bubbleAccent, 0.78);
                }
                else if (style == "glass")
                {
                    using (GraphicsPath path = BubblePath(bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY))
                    {
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(20, 0x2A, 0x3A, 0x66)))
                        {
                            g.TranslateTransform(0, 3f); g.FillPath(sh, path);
                            g.TranslateTransform(0, 3f); g.FillPath(sh, path);
                            g.TranslateTransform(0, 3f); g.FillPath(sh, path);
                            g.TranslateTransform(0, -9f);
                        }
                        using (LinearGradientBrush lg = new LinearGradientBrush(
                            new PointF(0, bodyT), new PointF(0, tailTipY),
                            Color.FromArgb(236, 0xFF, 0xFF, 0xFF),
                            Color.FromArgb(196, Lighten(_bubbleAccent, 0.55))))
                        using (Pen pen = new Pen(Color.FromArgb(238, 0xFF, 0xFF, 0xFF), Math.Max(1.6f, sw * 1.35f)))
                        {
                            g.FillPath(lg, path);
                            g.DrawPath(pen, path);
                        }
                        using (Pen hl = new Pen(Color.FromArgb(150, 0xFF, 0xFF, 0xFF), Math.Max(1f, sw * 0.8f)))
                        {
                            float inset = radius + 5f;
                            if (bodyR - bodyL > inset * 2f)
                                g.DrawLine(hl, bodyL + inset, bodyT + sw * 1.3f, bodyR - inset, bodyT + sw * 1.3f);
                        }
                    }
                    _bubbleInk = Darken(_bubbleAccent, 0.72);
                }
                else
                {
                    // card：柔和投影 + 白→淡蓝渐变 + 细描边 + 顶部高光
                    using (GraphicsPath path = BubblePath(bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY))
                    {
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(24, 0x2A, 0x3A, 0x66)))
                        using (SolidBrush sh2 = new SolidBrush(Color.FromArgb(15, 0x2A, 0x3A, 0x66)))
                        {
                            g.TranslateTransform(0, 2.5f); g.FillPath(sh, path);
                            g.TranslateTransform(0, 2.5f); g.FillPath(sh2, path);
                            g.TranslateTransform(0, -5f);
                        }
                        using (LinearGradientBrush lg = new LinearGradientBrush(
                            new PointF(0, bodyT), new PointF(0, tailTipY),
                            Color.FromArgb(0xFF, 0xFF, 0xFF), Lighten(_bubbleAccent, 0.78)))
                        using (Pen pen = new Pen(Color.FromArgb(215, _bubbleAccent.R, _bubbleAccent.G, _bubbleAccent.B), sw))
                        {
                            g.FillPath(lg, path);
                            g.DrawPath(pen, path);
                        }
                        using (Pen hl = new Pen(Color.FromArgb(130, 0xFF, 0xFF, 0xFF), Math.Max(1f, sw * 0.7f)))
                        {
                            float inset = radius + 4f;
                            if (bodyR - bodyL > inset * 2f)
                                g.DrawLine(hl, bodyL + inset, bodyT + sw * 1.2f, bodyR - inset, bodyT + sw * 1.2f);
                        }
                    }
                }
            }
            if (_bubbleInkFix.HasValue) _bubbleInk = _bubbleInkFix.Value;   // 用户指定了文字色就固定用它
            _bubbleSkin = b;
            _bubbleSkinKey = key;
            _bubbleSkinCount++;
            Status("气泡外框: " + style
                + (style == "custom" ? " → " + Path.GetFileName(_bubbleFrame) : "")
                + "  " + _w + "x" + _bubbleH + "  #" + _bubbleSkinCount);
        }

        // 气泡：外框贴图 + 弹出/淡出 + 文字自动缩字号。
        // ⚠️ 必须画进 Format32bppPArgb 的位图（GDI+ 会输出**预乘 alpha**），否则半透明边缘会发灰。
        static void DrawBubbleInto()
        {
            if (_bmp == null) return;
            _g.ResetTransform();
            _g.Clear(Color.Transparent);
            if (_bubbleText == "") return;

            // 出现：170ms 内轻微放大 + 淡入；消失：最后 240ms 淡出
            float appear = Math.Min(1f, (Now - _bubbleShownAt) / 170f);
            int left = _bubbleUntil - Now;
            float fadeOut = left < 240 ? Math.Max(0f, left / 240f) : 1f;
            float alpha = Math.Min(1f, appear * 1.6f) * fadeOut;
            if (alpha <= 0.02f) return;
            float ease = 1f - (1f - appear) * (1f - appear);       // easeOutQuad
            float scale = 0.94f + 0.06f * ease;

            BuildBubbleSkin();
            if (_bubbleSkin == null) return;

            // 以尾巴尖为轴做弹出缩放
            float cx = _w / 2f, axisY = _bubbleH;
            _g.TranslateTransform(cx, axisY);
            _g.ScaleTransform(scale, scale);
            _g.TranslateTransform(-cx, -axisY);

            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = alpha;                     // 整体透明度（预乘图也能正确淡入淡出）
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                _g.DrawImage(_bubbleSkin, new Rectangle(0, 0, _w, _bubbleH),
                    0, 0, _w, _bubbleH, GraphicsUnit.Pixel, ia);
            }

            // 文字：字号固定不缩放。装得下就居中；装不下就裁到气泡区内**滚动**显示。
            // 滚动只由用户手动驱动（鼠标在气泡上滚轮），不做任何自动滚动。
            float innerW = _textRect.Width, innerH = _textRect.Height;
            if (Math.Abs(_font.Size - _baseFontSize) > 0.01f)
            {
                _font.Dispose();
                _font = new Font(_bubbleFont, _baseFontSize, FontStyle.Regular, GraphicsUnit.Point);
            }
            SizeF need = _g.MeasureString(_bubbleText, _font, (int)Math.Max(12f, innerW));
            // MeasureString 返回的高度就是排版高度（含行距），可以直接用来判断要不要滚
            float total = need.Height + 6f;
            bool scroll = total > innerH;
            float scrollY = 0f;
            if (scroll)
            {
                // 只在用户滚轮/拖动时偏移；停下就停在那儿，不再自动回卷
                float maxScroll = Math.Max(0f, total - innerH + 8f);
                if (_bubbleScroll > maxScroll) _bubbleScroll = maxScroll;
                if (_bubbleScroll < 0) _bubbleScroll = 0;
                scrollY = _bubbleScroll;
            }
            else _bubbleScroll = 0f;

            using (SolidBrush inkb = new SolidBrush(Color.FromArgb((int)(255 * alpha), _bubbleInk)))
            {
                if (scroll)
                {
                    _g.SetClip(new RectangleF(_textRect.X, _textRect.Y - 1f, innerW, innerH + 2f));
                    _g.DrawString(_bubbleText, _font, inkb,
                        new RectangleF(_textRect.X, _textRect.Y - scrollY, innerW, total), _sfTop);
                    _g.ResetClip();

                    // 右侧一条极细滚动条，让用户知道「还有内容、现在读到哪」
                    // （这是「可手动滚动」最直观的视觉暗示，没有它用户根本不知道能滚）
                    float trackX = _textRect.Right - 2.5f;
                    float trackY = _textRect.Y;
                    float trackH = _textRect.Height;
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(46 * alpha), _bubbleInk)))
                        _g.FillRectangle(tb, trackX, trackY, 2f, trackH);
                    float ratio = innerH / total;
                    if (ratio > 0.12f && ratio < 1f)
                    {
                        float th = Math.Max(18f, trackH * ratio);
                        float avail = Math.Max(1f, total - innerH + 8f);
                        float ty = trackY + (trackH - th) * (scrollY / avail);
                        using (SolidBrush tb2 = new SolidBrush(Color.FromArgb((int)(165 * alpha), _bubbleInk)))
                            _g.FillRectangle(tb2, trackX, ty, 2f, th);
                    }
                }
                else
                {
                    _g.DrawString(_bubbleText, _font, inkb,
                        new RectangleF(_textRect.X, _textRect.Y - innerH * 0.05f, innerW, innerH), _sf);
                }
            }
            _g.ResetTransform();

            // 整块拷进 layered window 的 DIB 顶部（DIB 是自上而下，_bits 起点就是顶部）
            BitmapData bd = _bmp.LockBits(new Rectangle(0, 0, _w, _bubbleH),
                                          ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            byte[] buf = new byte[_w * 4 * _bubbleH];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            _bmp.UnlockBits(bd);
            Marshal.Copy(buf, 0, _bits, buf.Length);
        }

        // ---- 全屏隐藏 ----
        // 判据只有一条：前台窗口的矩形盖住了它所在显示器的**整块**屏幕（含任务栏那条）。
        // 最大化窗口只盖工作区、压不到任务栏，所以不会被误判；无边框全屏游戏 / 播放器两条都盖住。
        static bool ForegroundIsFullscreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return false;
            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfoW(mon, ref mi)) return false;
            RECT m = mi.rcMonitor;
            const int tol = 2;      // 无边框窗口常见 ±1px 误差
            return r.Left <= m.Left + tol && r.Top <= m.Top + tol
                && r.Right >= m.Right - tol && r.Bottom >= m.Bottom - tol;
        }

        static void CheckFullscreen()
        {
            bool fs = _hideFs && ForegroundIsFullscreen();
            if (fs == _fsHidden) return;              // 状态没变：每秒只做一次判定，别碰窗口
            _fsHidden = fs;
            ShowWindow(_hwnd, fs ? SW_HIDE : SW_SHOWNOACTIVATE);
            if (!fs)
            {
                // 回来时重新顶一次：退出全屏的程序常把 z 序改乱，不顶就被压在下面
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                Blit();
            }
            Status("全屏隐藏: " + (fs ? "已隐藏（前台全屏）" : "已恢复显示"));
        }

        // ---------------- 窗口过程 ----------------
        static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch ((int)msg)
            {
                // 第二个实例启动 → 把设置面板叫出来（双击两次不再毫无反应）
                case WM_PET_ACTIVATE:
                    Status("收到「叫出来」请求（第二个实例启动）→ 打开设置面板");
                    OpenSettings();
                    return (IntPtr)0;

                // 显示器分辨率 / 排布变了（拔副屏、投影、改缩放）→ 把桌宠拉回可视区
                case WM_DISPLAYCHANGE:
                    RepositionToVisible("显示器变化 " + ((int)wParam.ToInt64() & 0xFFFF)
                        + "x" + (((int)wParam.ToInt64() >> 16) & 0xFFFF));
                    return (IntPtr)0;

                // 窗口被拖到另一块 DPI 不同的显示器 / 系统缩放被改
                case WM_DPICHANGED:
                {
                    uint dpi = (uint)(wParam.ToInt64() & 0xFFFF);
                    RECT sug = new RECT();
                    try { sug = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT)); } catch (Exception) { }
                    Status("DPI 变化 → " + dpi + "（" + (dpi / 96.0).ToString("0.00", CultureInfo.InvariantCulture)
                        + "x），建议窗口 " + sug.Left + "," + sug.Top);
                    int nx = sug.Left, ny = sug.Top + _bubbleH;
                    ClampToVisible(ref nx, ref ny, true);
                    SetWindowPos(_hwnd, HWND_TOPMOST, nx, ny - _bubbleH, _w, _h + _bubbleH, SWP_NOACTIVATE);
                    ApplyDpiToBubble(dpi);
                    Blit();
                    SavePos();
                    return (IntPtr)0;
                }

                // 休眠：统计先落盘再暂停；唤醒：重置计时基准，别把睡着的几小时补记成使用时长
                case WM_POWERBROADCAST:
                {
                    int ev = (int)wParam.ToInt64();
                    if (ev == PBT_APMSUSPEND)
                    {
                        _statsPaused = true;
                        FlushStats();
                        Status("系统休眠 → 统计暂停并已落盘");
                    }
                    else if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUME)
                    {
                        _statsPaused = false;
                        _statsLastTick = 0;          // 下次 StatsTick 的 dt 从 0 起算
                        Status("系统唤醒 → 统计恢复（休眠时长不计入）");
                    }
                    return (IntPtr)1;
                }

                case WM_TIMER:
                    _tick++;
                    if (_tick % 30 == 0) StatsTick();                  // ~每秒：屏幕/软件使用统计
                    if (_tick % 30 == 0) CheckFullscreen();            // ~每秒：全屏就让开
                    if (_tick % 10 == 0) FlushLog();                  // 日志批量落盘（不占点击路径）
                    if (_testClicksLeft > 0 && Now >= _testClickNextAt)   // 自检：模拟连击
                    {
                        _testClickNextAt = Now + 350;
                        _testClicksLeft--;
                        DoClickLine();
                    }
                    if (_testWheelLeft > 0 && Now >= _testWheelNextAt)    // 自检：模拟滚轮
                    {
                        _testWheelNextAt = Now + 300;
                        _testWheelLeft--;
                        // 走钩子→窗口的同一条判定链：确认「命中判定」本身是通的
                        RECT wr; GetWindowRect(_hwnd, out wr);
                        int cx = wr.Left + (wr.Right - wr.Left) / 2;
                        int cy = wr.Top + _bubbleH / 2;
                        bool hit = WheelOverBubble(cx, cy);
                        Status("自检: 气泡中心=(" + cx + "," + cy + ") rect=" + wr.Left + "," + wr.Top
                            + " 命中=" + (hit ? "是" : "否"));
                        HandleBubbleWheel(-120, cx, cy);   // 向下滚，验证 _bubbleScroll 正向累积
                        Status("自检滚轮 → _bubbleScroll=" + (int)_bubbleScroll
                            + "px  (需要滚动=" + (BubbleNeedsScroll() ? "是" : "否")
                            + ", 可滚高度=" + (int)ClampBubbleScrollMax() + "px)");
                    }
                    AdvanceAnim();
                    // 隔一阵自己冒一句（bubbleEveryMs；0 = 不主动冒）
                    if (_bubbleH > 0 && _bubbleEveryMs > 0 && _bubbleUntil < Now && Now - _lastAutoBubble > _bubbleEveryMs)
                    {
                        _lastAutoBubble = Now;
                        ShowBubble(RandomLine(), 7000);
                    }
                    Blit();
                    if (_tick % 60 == 0) SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    if (_schedPath != "" && Now >= _schedAt)
                    {
                        string sp2 = _schedPath; _schedPath = "";
                        Status("定时播放: " + Path.GetFileName(sp2));
                        VoiceEx(sp2, "排期播出");
                    }
                    // 「播放完毕」标记：**不需要耳朵**就能判断有没有播完 ——
                    // 如果某条语音后面没有这行，说明它被中途掐了（或被进程退出带走了声音）。
                    if (_voicePath != "" && Now >= _voiceStartedAt + _voiceDurMs + 60)
                    {
                        Status("语音播放完毕: " + Path.GetFileName(_voicePath) + "（" + _voiceDurMs + "ms）");
                        _voicePath = ""; _lineVoicePlaying = false;
                    }
                    return IntPtr.Zero;

                case WM_APP + 1:            // 键盘钩子：只表示「有按键按下」
                case WM_APP + 2:            // 鼠标钩子：只表示「有点击」
                    TriggerPress(false);
                    return IntPtr.Zero;

                case WM_APP + 4:            // 鼠标钩子转发来的滚轮（唯一能收到 WM_MOUSEWHEEL 的途径）
                    {
                        // 命中判定已在钩子里做完，这里只按 lParam 里的 delta 滚动
                        HandleBubbleWheel(lParam.ToInt32(), 0, 0);
                        return IntPtr.Zero;
                    }

                case WM_TRAY:                // 托盘图标：双击 → 打开设置；右键 → 菜单（打开 / 关闭）
                    {
                        int ev = (int)lParam;
                        if (ev == 0x0203 /*WM_LBUTTONDBLCLK*/) OpenSettings();
                        else if (ev == WM_RBUTTONUP) ShowTrayMenu();
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDOWN:
                    // 点在气泡上：如果是「长文本需要滚动」的气泡，就变成拖动滚动文本，
                    // 而不是拖动角色（否则想看后面的文字却把桌宠拖跑了）。
                    {
                        POINT hp; GetCursorPos(out hp);
                        RECT br; GetWindowRect(hWnd, out br);
                        bool onBubble = hp.Y >= br.Top && hp.Y < br.Top + _bubbleH
                                     && hp.X >= br.Left && hp.X < br.Right;
                        if (onBubble && BubbleNeedsScroll())
                        {
                            _bubbleDragging = true;
                            _bubbleDragLastY = hp.Y;
                            SetCapture(hWnd);
                            return IntPtr.Zero;
                        }
                    }
                    TriggerPress(true);
                    // 按要求：**每次点击都播新的一条**（不再用防抖丢弃点击）。
                    // 连击的流畅性靠三件事保证：时长缓存（不再反复读 wav）、单一播放线程、日志批量落盘。
                    bool playedLine = DoClickLine();           // 气泡 + 与之一一对应的语音（会打断上一句）
                    _linePlayedOnDown = playedLine;            // 记住：本次点击已播台词 ⇒ 松开时别再放松开音
                    // 若还挂着待播的自动音（例如松开音），把它顺延到这句台词之后，而不是直接丢掉
                    if (_schedPath != "")
                    {
                        int w = VoiceFreeInMs();
                        _schedAt = Now + (w > 0 ? w : 0);
                        Status("待播自动音顺延 " + (w > 0 ? w : 0) + "ms（先让台词语音说完）");
                    }
                    // 点击音效开关：配置 clickVoice（默认 "0" = 静音，只做动画；改 "1" 恢复语音）
                    if (!playedLine && Cfg("clickVoice", "0") == "1")   // 已播台词语音就不再叠按压/松开音
                    {
                        Voice(LangVoice("press"));
                        int dur = WavDurationMs(LangVoice("press"));
                        _schedPath = "";                                  // 取消上一条排期
                        if (dur > 0) { _schedPath = LangVoice("release"); _schedAt = Now + Math.Max(0, dur - 40); }
                    }
                    _dragging = true; _dragMoved = false;
                    GetCursorPos(out _dragOrigin);
                    GetWindowRect(hWnd, out _dragRect);
                    SetCapture(hWnd);
                    return IntPtr.Zero;

                case WM_MOUSEMOVE:
                    if (_bubbleDragging)
                    {
                        // 拖动滚动：手指往上拖 → 看后面的内容（_bubbleScroll 增大）
                        POINT bp; GetCursorPos(out bp);
                        _bubbleScroll += (_bubbleDragLastY - bp.Y);
                        if (_bubbleScroll < 0) _bubbleScroll = 0;
                        _bubbleDragLastY = bp.Y;
                    }
                    else if (_dragging)
                    {
                        POINT p; GetCursorPos(out p);
                        int dx = p.X - _dragOrigin.X, dy = p.Y - _dragOrigin.Y;
                        if (!_dragMoved && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) { _dragMoved = true; Status("drag start -> 悬空摆动帧"); }
                        if (_dragMoved)
                        {
                            SetWindowPos(hWnd, HWND_TOPMOST, _dragRect.Left + dx, _dragRect.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                            // 拖动中每 150ms 落一次位置：设置面板的「位置」读数是轮询这个文件的，
                            // 只在松手时写的话，界面上要等拖完才跳数字。
                            if (unchecked(Now - _posSavedAt) > 150) { _posSavedAt = Now; SavePos(); }
                        }
                    }
                    return IntPtr.Zero;

                case WM_LBUTTONUP:
                    if (_bubbleDragging)
                    {
                        _bubbleDragging = false; ReleaseCapture();
                        ClampBubbleScroll();
                        Status("气泡拖动滚动结束 → " + (int)_bubbleScroll + "px");
                        return IntPtr.Zero;
                    }
                    _dragging = false; ReleaseCapture();
                    if (_dragMoved) { SavePos(); Status("drag end -> 回到待机帧"); }
                    // ⚠️ 已播台词语音时**绝不能**再放松开音：PlaySound 一次只放一个音，
                    //    松开音会把刚起播的台词当场掐断（用户反馈"松开音把台词盖住了"）。
                    // ⚠️ 只要**台词语音还在播**就绝不放松开音。
                    //    踩过的坑：双击时第二次按下的"防抖提前 return"跳过了 _linePlayedOnDown 赋值，
                    //    于是第二次松开时放了个松开音，把第一句**当场掐断**。
                    //    所以这里用"当前放的是不是台词"判断，比用一次性的标志稳。
                    else if (!_lineVoicePlaying && !_linePlayedOnDown && Cfg("clickVoice", "0") == "1") { _schedPath = ""; VoiceEx(LangVoice("release"), "松开音"); }
                    _linePlayedOnDown = false;
                    return IntPtr.Zero;

                case WM_MOUSEWHEEL:
                    {
                        int d = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                        POINT wp; GetCursorPos(out wp);
                        RECT wr; GetWindowRect(hWnd, out wr);
                        // 只在气泡区域内滚才有效（免得误改别的窗口）
                        if (wp.X >= wr.Left && wp.X < wr.Right && wp.Y >= wr.Top && wp.Y < wr.Top + _bubbleH)
                        {
                            _bubbleScroll += (d > 0 ? -18 : 18);
                            if (_bubbleScroll < 0) _bubbleScroll = 0;
                        }
                        return IntPtr.Zero;
                    }

                case WM_RBUTTONUP:
                    // 右键菜单：打开设置 / 退出桌宠（复用托盘菜单，同一套逻辑）
                    ShowTrayMenu();
                    return IntPtr.Zero;      // 右键菜单已按要求完全取消：桌面宠身上点右键不做任何事

                case WM_NCHITTEST:
                    {
                        // 命中测试：透明像素穿透到下层，免得一块看不见的方块挡住桌面
                        POINT p; GetCursorPos(out p);
                        RECT r; GetWindowRect(hWnd, out r);
                        int lx = p.X - r.Left, ly = p.Y - r.Top - _bubbleH;   // 换算到「角色帧」坐标（气泡区在上方，穿透）
                        if (lx < 0 || ly < 0 || lx >= _w || ly >= _h) return new IntPtr(HTTRANSPARENT);
                        if (_frames != null)
                        {
                            int a = _frames[_frameIndex * _frameBytes + (ly * _w + lx) * 4 + 3];
                            if (a < 24) return new IntPtr(HTTRANSPARENT);
                        }
                        return new IntPtr(HTCLIENT);
                    }

                case WM_COMMAND:             // 托盘右键菜单的选择
                    {
                        int id = (int)(wParam.ToInt64() & 0xFFFF);
                        if (id == IDM_OPEN) OpenSettings();
                        else if (id == IDM_QUIT) { Status("托盘菜单: 关闭桌宠"); Environment.Exit(0); }
                        return IntPtr.Zero;
                    }

                case WM_DESTROY:
                    FlushStats();
                    RemoveTrayIcon();
                    SavePos();
                    if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
                    if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // ---------------- 全局钩子（🔒 按键频率统计只在开启时读 vkCode，且只计数） ----------------
        static IntPtr KbHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int m = wParam.ToInt32();
                if (m == 0x0100 || m == 0x0104)      // WM_KEYDOWN / WM_SYSKEYDOWN
                {
                    // ⚠️ 隐私边界：只有 _statsKeys 开启时才碰 lParam，且只做「这个键 +1」。
                    //    不记录按键顺序、不保存字符内容、不写别处、不联网。
                    if (_statsKeys && lParam != IntPtr.Zero)
                    {
                        int vk = Marshal.ReadInt32(lParam, 0) & 0xFFFF;   // KBDLLHOOKSTRUCT.vkCode
                        if (vk > 0)
                        {
                            int c;
                            _keyCount.TryGetValue(vk, out c);
                            _keyCount[vk] = c + 1;
                            _statsDirty = true;
                        }
                    }
                    PostMessageW(_hwnd, (uint)(WM_APP + 1), IntPtr.Zero, IntPtr.Zero);
                }
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int m = wParam.ToInt32();
                // 只认「按下 / 抬起 / 滚轮」；**刻意不含 WM_MOUSEMOVE**（否则鼠标一动就抖，且费 CPU）
                if (m == 0x0201 || m == 0x0204 || m == 0x0207)
                    PostMessageW(_hwnd, (uint)(WM_APP + 2), IntPtr.Zero, IntPtr.Zero);
                else if (m == WM_MOUSEWHEEL)
                {
                    // ⚠ 气泡长文本的滚动必须走这里。
                    // 窗口是 WS_EX_NOACTIVATE 且从不激活，而 WM_MOUSEWHEEL **只投递给有键盘焦点的窗口**，
                    // 所以 WndProc 里那个 case WM_MOUSEWHEEL 实际上永远收不到。
                    // 全局钩子是唯一可靠来源：把滚轮增量取出来，连同光标屏幕坐标一起转发。
                    //
                    // ⚠⚠ lParam 在 **低级钩子(WH_MOUSE_LL)** 里不是消息的 lParam，
                    // 而是指向 MSLLHOOKSTRUCT 的指针，布局是：
                    //     offset 0 : POINT pt (x, y)   —— 8 字节
                    //     offset 8 : UINT  mouseData    —— 滚轮增量在它的高 16 位
                    //     offset 12: DWORD flags
                    //     offset 16: DWORD time
                    //     offset 24: ULONG_PTR dwExtraInfo
                    // 之前误写成 (lParam.ToInt64() >> 16) —— 那是��**指针本身**的高位，
                    // 拿到的是垃圾数，所以滚轮完全不起作用（用户实测反馈"滚不动"就是这个原因）。
                    if (lParam == IntPtr.Zero) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    int mx = Marshal.ReadInt32(lParam, 0);           // pt.x
                    int my = Marshal.ReadInt32(lParam, 4);           // pt.y
                    int mouseData = Marshal.ReadInt32(lParam, 8);
                    int delta = (short)((mouseData >> 16) & 0xFFFF);  // 高 16 位才是滚轮量
                    if (delta == 0) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    // 诊断开关：debugWheel=1 时记录每次滚轮的解析结果，用来验证接线对不对
                    if (Cfg("debugWheel", "0") == "1")
                        Status("钩子滚轮: pt=(" + mx + "," + my + ") delta=" + delta
                            + " 命中气泡=" + (WheelOverBubble(mx, my) ? "是" : "否"));
                    // 命中判定放在钩子里做，只把 delta 传过去。
                    // 别用 16+16 位打包坐标（多屏/负坐标会被截断），
                    // 直接用消息自带的空间：lParam 放 y，wParam 放 (delta << 16) | x低16位 仍然脆弱，
                    // 所以改为「钩子里判断命中 → 只传 delta」，坐标判断复用同一个辅助函数。
                    if (!WheelOverBubble(mx, my)) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    PostMessageW(_hwnd, (uint)(WM_APP + 4), IntPtr.Zero, (IntPtr)delta);
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // 气泡是否真的需要滚动（文字排不开才需要）。
        // 注意：MeasureString 必须在 _g（画到气泡位图那个 Graphics）上跑，和绘制时保持一致。
        static bool BubbleNeedsScroll()
        {
            if (_bubbleH <= 0 || string.IsNullOrEmpty(_bubbleText)) return false;
            if (_font == null || _g == null) return false;
            if (_textRect.Width <= 1 || _textRect.Height <= 1) return false;
            try
            {
                SizeF need = _g.MeasureString(_bubbleText, _font, (int)Math.Max(12f, _textRect.Width));
                return need.Height + 6f > _textRect.Height;
            }
            catch (Exception ex) { LogErr("BubbleNeedsScroll", ex); return false; }
        }

        // 把滚动偏移夹到合法范围。需要时重新测量文字高度（与绘制同一套逻辑）。
        static float ClampBubbleScrollMax()
        {
            if (_bubbleH <= 0 || _font == null || _g == null) return 0f;
            if (_textRect.Width <= 1 || _textRect.Height <= 1) return 0f;
            try
            {
                SizeF need = _g.MeasureString(_bubbleText, _font, (int)Math.Max(12f, _textRect.Width));
                float total = need.Height + 6f;
                if (total <= _textRect.Height) return 0f;
                return Math.Max(0f, total - _textRect.Height + 8f);
            }
            catch (Exception ex) { LogErr("ClampBubbleScrollMax", ex); return 0f; }
        }

        static void ClampBubbleScroll()
        {
            float max = ClampBubbleScrollMax();
            if (_bubbleScroll > max) _bubbleScroll = max;
            if (_bubbleScroll < 0) _bubbleScroll = 0;
        }

        // 屏幕坐标是否落在气泡区内。钩子与消息处理共用这一个判定，避免两处逻辑跑偏。
        static bool WheelOverBubble(int scrX, int scrY)
        {
            if (_bubbleH <= 0 || _hwnd == IntPtr.Zero) return false;
            RECT wr; GetWindowRect(_hwnd, out wr);
            return scrX >= wr.Left && scrX < wr.Right
                && scrY >= wr.Top && scrY < wr.Top + _bubbleH;
        }

        // 处理钩子转发过来的滚轮（命中判定已在钩子里做过，这里只管滚多少）
        static void HandleBubbleWheel(int delta, int scrX, int scrY)
        {
            if (_bubbleH <= 0 || delta == 0) return;
            if (!BubbleNeedsScroll()) return;
            // 一格滚轮走 42px，120 = 一格，方向与系统一致（向上滚 d>0 看上文）
            _bubbleScroll += (delta > 0 ? -42f : 42f);
            // 上下都要夹：只夹下限会导致超出可滚范围（滚动条指示错位、文字滚过头留白）
            ClampBubbleScroll();
            Status("气泡手动滚动 → " + (int)_bubbleScroll + "px");
        }

        static void LoadStats()
        {
            try
            {
                if (string.IsNullOrEmpty(_statsFile) || !File.Exists(_statsFile)) return;
                foreach (string line in File.ReadAllLines(_statsFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "day") { if (v == DateTime.Now.ToString("yyyy-MM-dd")) _statsDay = v; else continue; }
                    else if (k == "screen") int.TryParse(v, out _screenSec);
                    else if (k == "active") int.TryParse(v, out _activeSec);
                    else if (k == "idle") int.TryParse(v, out _idleSec);
                    else if (k == "app") { int bar = v.LastIndexOf('|'); if (bar > 0) { int n; if (int.TryParse(v.Substring(bar + 1), out n)) _appSec[v.Substring(0, bar)] = n; } }
                    else if (k == "key") { int bar = v.LastIndexOf('|'); if (bar > 0) { int vk, n; if (int.TryParse(v.Substring(0, bar), out vk) && int.TryParse(v.Substring(bar + 1), out n)) _keyCount[vk] = n; } }
                    else if (k == "char") { int bar = v.LastIndexOf('|'); if (bar > 0) { int n; if (int.TryParse(v.Substring(bar + 1), out n)) _charDaySec[v.Substring(0, bar)] = n; } }
                }
                if (_statsDay == "") _statsDay = DateTime.Now.ToString("yyyy-MM-dd");
            }
            catch (Exception ex) { LogErr("LoadStats", ex); }
        }

        static void LoadCharTime()
        {
            try
            {
                _charTimeFile = Path.Combine(_baseDir, "pet-char-time.txt");
                if (!File.Exists(_charTimeFile)) return;
                foreach (string line in File.ReadAllLines(_charTimeFile))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) continue;
                    int n;
                    if (int.TryParse(line.Substring(eq + 1).Trim(), out n))
                        _charTotalSec[line.Substring(0, eq).Trim()] = n;
                }
            }
            catch (Exception ex) { LogErr("LoadCharTime", ex); }
        }

        static void FlushStats()
        {
            try
            {
                if (string.IsNullOrEmpty(_statsFile)) return;
                _statsLastFlush = Now;
                if (_statsDay == "") _statsDay = DateTime.Now.ToString("yyyy-MM-dd");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("day=" + _statsDay);
                sb.AppendLine("screen=" + _screenSec);
                sb.AppendLine("active=" + _activeSec);
                sb.AppendLine("idle=" + _idleSec);
                foreach (KeyValuePair<string, int> kv in _appSec) sb.AppendLine("app=" + kv.Key + "|" + kv.Value);
                foreach (KeyValuePair<int, int> kv in _keyCount) sb.AppendLine("key=" + kv.Key + "|" + kv.Value);
                foreach (KeyValuePair<string, int> kv in _charDaySec) sb.AppendLine("char=" + kv.Key + "|" + kv.Value);
                File.WriteAllText(_statsFile, sb.ToString(), Encoding.UTF8);
                // 按天归档：每天一份（pet-stats-YYYY-MM-DD.txt），设置面板可回看历史
                try { File.WriteAllText(Path.Combine(_baseDir, "pet-stats-" + _statsDay + ".txt"), sb.ToString(), Encoding.UTF8); }
                catch (Exception ex) { LogErr("FlushStats", ex); }

                // 角色累计时长单独一个文件：跨天/清空今日统计都不会丢
                if (_charTimeFile != "")
                {
                    StringBuilder tb = new StringBuilder();
                    foreach (KeyValuePair<string, int> kv in _charTotalSec) tb.AppendLine(kv.Key + "=" + kv.Value);
                    File.WriteAllText(_charTimeFile, tb.ToString(), Encoding.UTF8);
                }
                _statsDirty = false;
            }
            catch (Exception ex) { LogErr("FlushStats", ex); }
        }

        // 每秒一次：屏幕使用 / 前台软件 / 定期落盘
        static void StatsTick()
        {
            try
            {
                if (_statsPaused) return;          // 系统休眠中：不累计也不补记
                int now = Now;
                // 设置面板点「清空统计」→ 写 pet-stats-reset.txt；这里连内存计数一起清零，
                // 否则几秒后又会把旧数据写回文件。
                try
                {
                    string rf = Path.Combine(_baseDir, "pet-stats-reset.txt");
                    if (File.Exists(rf))
                    {
                        File.Delete(rf);
                        _screenSec = 0; _activeSec = 0; _idleSec = 0;
                        _appSec.Clear(); _keyCount.Clear(); _charDaySec.Clear();
                        _statsDay = DateTime.Now.ToString("yyyy-MM-dd");
                        _statsDirty = true;
                        FlushStats();
                        Status("统计已按设置面板要求清空");
                    }
                }
                catch (Exception ex) { LogErr("StatsTick", ex); }
                int dt = _statsLastTick == 0 ? 0 : (now - _statsLastTick) / 1000;
                if (dt < 0 || dt > 60) dt = 0;
                _statsLastTick = now;
                // 4 秒一次（原来是 30 秒）：设置面板的「按键使用」要能实时刷新。
                if (_statsDirty && now - _statsLastFlush > 4000) FlushStats();

                uint idleMs = 0;
                LASTINPUTINFO li = new LASTINPUTINFO();
                li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (GetLastInputInfo(ref li)) idleMs = (uint)(Environment.TickCount - (int)li.dwTime);
                bool active = idleMs < 300000;      // 5 分钟内有输入 = 在用

                _screenSec += dt;
                if (active) _activeSec += dt; else _idleSec += dt;
                // 角色陪伴时长：今日（pet-stats.txt 的 char=）+ 累计（pet-char-time.txt）
                if (dt > 0 && !string.IsNullOrEmpty(_charName))
                {
                    int c;
                    _charDaySec.TryGetValue(_charName, out c);
                    _charDaySec[_charName] = c + dt;
                    _charTotalSec.TryGetValue(_charName, out c);
                    _charTotalSec[_charName] = c + dt;
                }
                if (dt > 0) _statsDirty = true;

                // 跨天：必须在「挂机就 return」之前判 —— 以前挂在后面，机器一整晚没人动
                // 就永远翻不了天，第二天的数据全算在前一天头上。
                string today0 = DateTime.Now.ToString("yyyy-MM-dd");
                if (_statsDay != today0)
                {
                    FlushStats();                      // 先把昨天的数据存盘
                    _screenSec = 0; _activeSec = 0; _idleSec = 0;
                    _appSec.Clear(); _keyCount.Clear(); _charDaySec.Clear();
                    _statsDay = today0;
                    _statsDirty = true;
                    Status("统计已跨天重置: " + today0);
                }

                if (!active || dt == 0) return;

                IntPtr fg = GetForegroundWindow();
                uint pid = 0;
                if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out pid);
                if (pid == 0) return;
                string name;
                if ((int)pid == _lastFgPid && _lastFgName != "") name = _lastFgName;
                else
                {
                    try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                    catch (Exception) { name = "unknown"; }
                    _lastFgPid = (int)pid; _lastFgName = name;
                }
                int cur;
                _appSec.TryGetValue(name, out cur);
                _appSec[name] = cur + dt;
            }
            catch (Exception ex) { LogErr("StatsTick", ex); }
        }

        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);

        // ---------------- 空 catch 的兜底日志 ----------------
        // 以前 catch {} 全裸 ⇒ 出问题只能靠猜。这里统一落 pet-error.log（只记一行，不刷屏）。
        static string _errFile = "";
        static readonly System.Collections.Generic.HashSet<string> _errOnce =
            new System.Collections.Generic.HashSet<string>();
        static void LogErr(string where, Exception ex)
        {
            try
            {
                string key = where;                       // 同一处只记一次，避免每 33ms 刷一次盘
                lock (_errOnce) { if (!_errOnce.Add(key)) return; }
                string p = _errFile != "" ? _errFile : Path.Combine(_baseDir, "pet-error.log");
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where
                    + "  " + (ex != null ? ex.GetType().Name + ": " + ex.Message : "") + "\r\n");
            }
            catch (Exception) { }      // 日志本身写不了就真没办法了，别递归
        }

        // ---------------- 第二个实例：把它「叫出来」，别静默退出 ----------------
        // 以前拿不到互斥体直接 return ⇒ 用户双击两次以为程序坏了，且毫无痕迹。
        public static void NotifyExistingInstance(string dir)
        {
            try
            {
                // 这个进程马上就退出了，先把落盘路径补齐，日志才写得进 exe 同目录
                if (_baseDir == "") _baseDir = dir;
                if (_statusFile == "") _statusFile = Path.Combine(dir, "pet-status.txt");
                Status("已有实例在运行 → 本次不启动（已请它打开设置面板）");
                FlushLog();
                IntPtr h = FindWindowW("PetWndClass", "Pet");
                if (h != IntPtr.Zero) PostMessageW(h, (uint)WM_PET_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                else Status("⚠ 互斥体被占用但没找到桌宠窗口（可能是残留进程）");
                FlushLog();
            }
            catch (Exception ex) { LogErr("NotifyExistingInstance", ex); }
        }

        // ---------------- 状态文件（可验证通道：DPI 是否生效、窗口矩形、钩子句柄） ----------------
        // 轮转：pet-status.txt 永远只放「今天」，跨天时把昨天的挪成 pet-status-YYYY-MM-DD.txt，
        // 归档只留最近 7 天。以前纯追加不清理 ⇒ 几个月能涨到几 MB。
        static string _statusFile = "";
        static string _statusDay = "";

        static void RotateStatusLog()
        {
            try
            {
                if (string.IsNullOrEmpty(_statusFile)) _statusFile = Cfg("statusfile", "pet-status.txt");
                string p = _statusFile;
                if (string.IsNullOrEmpty(p)) return;
                if (!Path.IsPathRooted(p)) p = Path.Combine(_baseDir, p);
                string dir = Path.GetDirectoryName(p);
                string stem = Path.GetFileNameWithoutExtension(p);
                string ext = Path.GetExtension(p);
                if (dir == "" || stem == "") return;
                Directory.CreateDirectory(dir);
                if (File.Exists(p))
                {
                    DateTime w = File.GetLastWriteTime(p);
                    if (w.Date != DateTime.Now.Date)
                    {
                        string dst = Path.Combine(dir, stem + "-" + w.ToString("yyyy-MM-dd") + ext);
                        int i = 1;
                        while (File.Exists(dst))
                        {
                            dst = Path.Combine(dir, stem + "-" + w.ToString("yyyy-MM-dd") + "-" + i + ext);
                            i++;
                        }
                        File.Move(p, dst);
                    }
                }
                // 归档只留 7 天
                foreach (string f in Directory.GetFiles(dir, stem + "-*" + ext))
                {
                    try
                    {
                        DateTime w = File.GetLastWriteTime(f);
                        if ((DateTime.Now.Date - w.Date).TotalDays > 7) File.Delete(f);
                    }
                    catch (Exception ex) { LogErr("RotateStatusLog", ex); }
                }
                _statusDay = DateTime.Now.ToString("yyyy-MM-dd");
            }
            catch (Exception ex) { LogErr("RotateStatusLog", ex); }
        }

        static void Status(string s)
        {
            try { lock (_logQueue) { _logQueue.Add(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); } }
            catch (Exception) { }
        }

        // 把日志从"点击路径"挪走：先进内存队列，定时器里批量落盘一次（连击不卡的关键之一）
        static void FlushLog()
        {
            try
            {
                string[] batch;
                lock (_logQueue)
                {
                    if (_logQueue.Count == 0) return;
                    batch = _logQueue.ToArray();
                    _logQueue.Clear();
                }
                // 跨天了 → 先把昨天的归档，再往新的「今天」文件里写
                if (_statusDay != DateTime.Now.ToString("yyyy-MM-dd")) RotateStatusLog();
                string p = _statusFile != "" ? _statusFile : Cfg("statusfile", "pet-status.txt");
                if (string.IsNullOrEmpty(p)) return;
                if (!Path.IsPathRooted(p)) p = Path.Combine(_baseDir, p);
                File.AppendAllLines(p, batch);
            }
            catch (Exception ex) { LogErr("FlushLog", ex); }
        }

        // ---------------- 角色包 ----------------
        // chars\<角色>\char.json 里的路径都是相对角色目录的（图片/动画/音效各一个子文件夹），
        // 整个 chars 目录可以直接拷到别的机器。配置里只记 charDir。
        static Dictionary<string, string> ReadCharJson(string dir)
        {
            var m = new Dictionary<string, string>();
            try
            {
                string man = Path.Combine(dir, "char.json");
                if (!File.Exists(man)) return m;
                foreach (string raw in File.ReadAllLines(man))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    m[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { LogErr("ReadCharJson", ex); }
            return m;
        }

        static void ApplyCharPack()
        {
            string cd = Cfg("charDir", "");
            if (string.IsNullOrEmpty(cd)) return;
            if (!Path.IsPathRooted(cd)) cd = Path.Combine(_baseDir, cd);
            Dictionary<string, string> m = ReadCharJson(cd);
            if (m.Count == 0) { Status("角色包无效（缺 char.json）: " + cd); return; }

            foreach (string k in new string[] { "frames", "meta", "lines", "voicelines" })
            {
                string v;
                if (!m.TryGetValue(k, out v) || v == "") continue;
                if (!Path.IsPathRooted(v)) v = Path.Combine(cd, v);
                _cfg[k] = v;
                if (!File.Exists(v)) Status("⚠ 角色包缺少 " + k + ": " + v);
            }
            foreach (string k in new string[] { "press", "release" })
            {
                string v;
                if (!m.TryGetValue(k, out v) || v == "") { _cfg["lang0" + k] = ""; continue; }
                if (!Path.IsPathRooted(v)) v = Path.Combine(cd, v);
                _cfg["lang0" + k] = v;
            }
            _charDir = cd;
            _charName = (m.ContainsKey("name") && m["name"] != "") ? m["name"] : Path.GetFileName(cd);
            _cfg["lang0name"] = _charName;
            string vs; int vc = 0;
            if (m.TryGetValue("voices", out vs)) int.TryParse(vs, out vc);
            Status("角色包: " + _charName + "（语音 " + vc + " 条）  " + cd);

            // 音效组：auto = 跟着角色走；否则借用另一个角色包的按压/松开音（音效和形象可以分开玩）
            string grp = Cfg("sfxGroup", "auto");
            if (!string.IsNullOrEmpty(grp) && grp != "auto")
            {
                string gd = Path.IsPathRooted(grp) ? grp : Path.Combine(Path.Combine(_baseDir, "chars"), grp);
                Dictionary<string, string> gm = ReadCharJson(gd);
                if (gm.Count == 0) { Status("音效组不存在: " + grp); return; }
                bool any = false;
                foreach (string k in new string[] { "press", "release" })
                {
                    string v;
                    if (!gm.TryGetValue(k, out v) || v == "") continue;
                    if (!Path.IsPathRooted(v)) v = Path.Combine(gd, v);
                    if (!File.Exists(v)) continue;
                    _cfg["lang0" + k] = v;
                    any = true;
                }
                string pf = Cfg("lang0press", "");
                Status("音效组: " + grp + (any ? "  →  " + Path.GetFileName(pf) : "（该角色没有按压/松开音）"));
            }
        }

        // ---------------- 启动 ----------------
        public static void Run(string configPath)
        {
            foreach (string raw in File.ReadAllLines(configPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                _cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            // ---- 相对路径支持：整个程序文件夹可以拷到任何机器/任意路径 ----
            // 配置里写相对路径（如 frames=pet-frames.bin）时，按 pet-config.txt 所在目录解析。
            _baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
            {
                string[] pathKeys = { "charDir", "frames", "meta", "posfile", "langfile", "statusfile", "statsfile",
                                      "lines", "voicelines", "settings", "icopath", "bubbleFrame" };
                foreach (string k in pathKeys)
                {
                    string v;
                    if (!_cfg.TryGetValue(k, out v) || string.IsNullOrEmpty(v)) continue;
                    if (!Path.IsPathRooted(v)) _cfg[k] = Path.Combine(_baseDir, v);
                }
                for (int i = 0; i < 16; i++)
                {
                    foreach (string suf in new string[] { "press", "release" })
                    {
                        string k = "lang" + i + suf, v;
                        if (_cfg.TryGetValue(k, out v) && !string.IsNullOrEmpty(v) && !Path.IsPathRooted(v))
                            _cfg[k] = Path.Combine(_baseDir, v);
                    }
                }
            }
            // ---- 日志 / 缓存的生命周期：都在启动早期处理，连 showPet=0 也走得到 ----
            _errFile = Path.Combine(_baseDir, "pet-error.log");
            _statusFile = Cfg("statusfile", "pet-status.txt");
            RotateStatusLog();                 // pet-status.txt 只留今天，旧归档留 7 天
            _volDir = Path.Combine(_baseDir, "pet-vol-cache");

            // ---- 总开关：设置面板「通用设置 → 桌面显示」关掉时，桌宠根本不启动 ----
            // 卡在建窗口、装钩子、开定时器之前：关掉后连一帧都不渲染，也不占全局钩子和 CPU。
            // 想再看到她：打开 PetSettings.exe → 通用设置 → 打开「在桌面显示桌宠」。
            if (Cfg("showPet", "1") == "0")
            {
                Status("showPet=0：桌宠已关闭（设置面板「通用设置 → 桌面显示」可重新打开），本次不启动");
                FlushLog();
                return;
            }

            // 角色包：charDir 指向 chars\<角色>，帧 / 台词 / 语音 / 按压音全部换成这个角色的
            ApplyCharPack();
            CleanVolCache();                   // 源语音已不在（换角色/删素材）的音量缓存，这次就清掉

            string metaPath = Cfg("meta", "");
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
                throw new Exception("找不到动画帧清单 meta=" + metaPath);
            foreach (string raw in File.ReadAllLines(metaPath))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "w") _w = int.Parse(v);
                else if (k == "h") _h = int.Parse(v);
                else if (k == "idle") _idleN = int.Parse(v);
                else if (k == "press") _pressN = int.Parse(v);
                else if (k == "click") _clickN = int.Parse(v);
                else if (k == "drag") _dragN = int.Parse(v);
            }
            _frameBytes = _w * _h * 4;
            _frames = File.ReadAllBytes(Cfg("frames", ""));
            if (_frames.Length < _frameBytes * TotalFrames) throw new Exception("精灵帧文件大小不足");

            _langCount = 0;
            while (_cfg.ContainsKey("lang" + _langCount + "name")) _langCount++;
            string lf = Cfg("langfile", "");
            try { if (File.Exists(lf)) foreach (string l in File.ReadAllLines(lf)) { string[] kv = l.Split('='); if (kv.Length == 2 && kv[0].Trim() == "lang") { int v; if (int.TryParse(kv[1].Trim(), out v) && v >= 0 && v < _langCount) _lang = v; } } }
            catch (Exception ex) { LogErr("Run", ex); }
            string hg = Cfg("holdMs", ""); if (hg != "") int.TryParse(hg, out _holdMs);
            _hideFs = Cfg("hideFullscreen", "1") == "1";
            Status("全屏隐藏=" + (_hideFs ? "开（前台全屏时让开）" : "关"));
            string bg = Cfg("bubbleH", ""); if (bg != "") int.TryParse(bg, out _bubbleH);
            string be = Cfg("bubbleEveryMs", ""); if (be != "") int.TryParse(be, out _bubbleEveryMs);
            string bf = Cfg("bubbleFont", ""); if (bf != "") _bubbleFont = bf;
            string bs = Cfg("bubbleStyle", ""); if (bs != "") _bubbleStyle = bs;
            _bubbleFrame = Cfg("bubbleFrame", "");
            if (_bubbleStyle == "custom") LoadBubbleFrame();
            _bubbleAccent = ParseHexColor(Cfg("bubbleColor", ""), _bubbleAccent);
            {
                string ink = Cfg("bubbleInk", "auto");
                _bubbleInkFix = (ink == "" || ink == "auto") ? (Color?)null : ParseHexColor(ink, Color.FromArgb(0x1A, 0x1A, 0x1A));
            }
            {
                float fsz;
                if (float.TryParse(Cfg("bubbleFontSize", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out fsz))
                    _bubbleFontPt = Math.Max(6f, Math.Min(36f, fsz));
            }
            Status("气泡: 样式=" + _bubbleStyle + " 配色=" + ColorToHex(_bubbleAccent)
                + " 字号=" + _bubbleFontPt.ToString("0.0", CultureInfo.InvariantCulture) + "pt"
                + " 字色=" + (_bubbleInkFix.HasValue ? ColorToHex(_bubbleInkFix.Value) : "auto"));
            // 音量（0–100）与四类动画开关 / 速度
            {
                int v;
                if (int.TryParse(Cfg("volume", "100"), out v)) _volume = Math.Max(0, Math.Min(100, v));
                _animIdle = Cfg("animIdle", "1") == "1";
                _animPress = Cfg("animPress", "1") == "1";
                _animClick = Cfg("animClick", "1") == "1";
                _animDrag = Cfg("animDrag", "1") == "1";
                double sp;
                if (double.TryParse(Cfg("animSpeed", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out sp))
                    _animSpeed = Math.Max(0.5, Math.Min(2.0, sp));
                // 四类动画各自的倍速（百分数；缺省沿用旧版全局 animSpeed）
                _speedIdle = ReadSpeed("speedIdle", _animSpeed);
                _speedPress = ReadSpeed("speedPress", _animSpeed);
                _speedClick = ReadSpeed("speedClick", _animSpeed);
                _speedDrag = ReadSpeed("speedDrag", _animSpeed);
                Status("音量=" + _volume + "%  动画: 待机=" + _animIdle + " 按键=" + _animPress
                    + " 点击=" + _animClick + " 拖动=" + _animDrag
                    + " 速度=" + FmtSpeed(_speedIdle) + "/" + FmtSpeed(_speedPress)
                    + "/" + FmtSpeed(_speedClick) + "/" + FmtSpeed(_speedDrag));
                Status("气泡外框: " + _bubbleStyle
                    + (_bubbleStyle == "custom" ? " → " + Path.GetFileName(_bubbleFrame) : ""));
            }
            _statsFile = Cfg("statsfile", "");
            string stk = Cfg("statsKeys", "1");
            if (stk != "") _statsKeys = (stk == "1");
            if (!string.IsNullOrEmpty(_statsFile)) { LoadStats(); Status("使用统计: 已加载 " + _appSec.Count + " 个软件 / " + _keyCount.Count + " 个按键（本地）"); }
            LoadCharTime();
            if (!string.IsNullOrEmpty(_charName))
            {
                int ct; _charTotalSec.TryGetValue(_charName, out ct);
                Status("角色时长: " + _charName + " 累计 " + FmtSec(ct));
            }

            // DPI：必须让进程「每显示器感知」。否则本进程拿到的是 1536×864 这种虚拟坐标，
            // DWM 会把 layered window 按 125% 拉伸 ⇒ 桌宠糊掉。
            bool dpiOk = SetProcessDpiAwarenessContext(new IntPtr(-4));   // PER_MONITOR_AWARE_V2
            if (!dpiOk) dpiOk = SetProcessDPIAware();
            Status("dpiAware=" + dpiOk + "  screen=" + GetSystemMetrics(0) + "x" + GetSystemMetrics(1) + "  lang=" + _lang);

            if (_bubbleH > 0)
            {
                // 预乘 alpha 的离屏位图：GDI+ 画上去就是预乘结果，整块拷进 DIB 即可
                _bmp = new Bitmap(_w, _bubbleH, PixelFormat.Format32bppPArgb);
                _g = Graphics.FromImage(_bmp);
                _g.SmoothingMode = SmoothingMode.AntiAlias;
                _g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                // 字号来自设置面板（pt），不随气泡高度缩放：长文本靠滚动看，不缩字号
                float fs = Math.Max(6f, Math.Min(36f, _bubbleFontPt));
                _baseFontSize = fs;
                _font = new Font(_bubbleFont, fs, FontStyle.Regular, GraphicsUnit.Point);
                _sf = new StringFormat();
                _sf.Alignment = StringAlignment.Center;      // 气泡文字水平居中
                _sf.LineAlignment = StringAlignment.Center;
                _sfTop = new StringFormat();                 // 滚动模式：顶端对齐（配合 SetClip 裁切）
                _sfTop.Alignment = StringAlignment.Center;
                _sfTop.LineAlignment = StringAlignment.Near;
                _zeroTop = new byte[_w * 4 * _bubbleH];
            }
            LoadLines();
            _lastAutoBubble = Now;

            _screenDc = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(_screenDc);
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = _w; bmi.bmiHeader.biHeight = -(_h + _bubbleH);   // 负数 = 自上而下
            bmi.bmiHeader.biPlanes = 1; bmi.bmiHeader.biBitCount = 32; bmi.bmiHeader.biCompression = 0;
            _hBitmap = CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            SelectObject(_memDc, _hBitmap);

            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.style = 0; wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc = new WndProcDelegate(WndProc));
            wc.hInstance = GetModuleHandleW(null);
            wc.lpszClassName = "PetWndClass";
            RegisterClassExW(ref wc);

            int x0, y0; LoadPos(out x0, out y0);
            // 夹到屏幕内：加了气泡区之后窗口更高，老位置可能顶出屏幕上边或右边；
            // 还要处理「位置落在已拔掉的显示器上」（见 ClampToVisible）
            ClampToVisible(ref x0, ref y0, true);
            // posfile 存的是**角色**的左上角；窗口把气泡区加在上方，所以窗口 Y = 角色 Y - 气泡高。
            // 这样加气泡不会让角色在屏幕上跳位置。
            int winH = _h + _bubbleH, winY = y0 - _bubbleH;
            _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                "PetWndClass", "Pet", WS_POPUP | WS_VISIBLE, x0, winY, _w, winH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { Status("ERROR CreateWindowEx 失败"); throw new Exception("CreateWindowEx 失败"); }
            Status("hwnd=" + _hwnd + "  角色位置=" + x0 + "," + y0 + "  窗口=" + _w + "x" + winH + "（含气泡 " + _bubbleH + "px）");

            // WS_POPUP 不含 WS_VISIBLE；再显式 ShowWindow 一次（不抢焦点），否则窗口一直不可见
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_hwnd, HWND_TOPMOST, x0, winY, _w, winH, SWP_NOACTIVATE);
            Blit();
            {
                RECT rr; GetWindowRect(_hwnd, out rr);
                Status("shown  rect=" + rr.Left + "," + rr.Top + "," + rr.Right + "," + rr.Bottom
                    + "  dpi=" + GetDpiForWindow(_hwnd));
                // 进程是「每显示器感知」，气泡位图按 96dpi 画 ⇒ 高 DPI 屏上文字会偏小。
                // 按实际 DPI 把字号放大回去，和系统缩放下的其它文字一样大。
                ApplyDpiToBubble(GetDpiForWindow(_hwnd));
            }

            IntPtr hMod = GetModuleHandleW(null);
            _kbHook = SetWindowsHookExW(WH_KEYBOARD_LL, _kbProc = new HookProc(KbHook), hMod, 0);
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc = new HookProc(MouseHook), hMod, 0);
            Status("hooks kb=" + _kbHook + "  mouse=" + _mouseHook);
            Status("ready frames=" + TotalFrames + " (待机" + _idleN + "/按键" + _pressN + "/点击" + _clickN + "/拖动" + _dragN + ") 语音语言数=" + _langCount);
            AddTrayIcon();          // 托盘图标：左键打开设置（右键菜单已完全取消）
            InitPlayThread();
            int nTest = 0;
            if (int.TryParse(Cfg("clickTest", "0"), out nTest) && nTest > 0)
            {
                Status("== 启动自检：模拟 " + nTest + " 次连击（间隔 350ms）==");
                _testClicksLeft = nTest;
                _testClickNextAt = Now + 500;
            }

            // wheelTest=N：启动后模拟 N 次滚轮打在气泡上，验证「长文本能否手动滚动」。
            // 真机验证不了鼠标钩子转发时，用它走一遍完整链路（钩子→WM_APP+4→HandleBubbleWheel→_bubbleScroll）。
            int nWheel = 0;
            if (int.TryParse(Cfg("wheelTest", "0"), out nWheel) && nWheel > 0)
            {
                Status("== 启动自检：模拟 " + nWheel + " 次滚轮（间隔 300ms）==");
                _testWheelLeft = nWheel;
                _testWheelNextAt = Now + 3500;   // 等连击先弹出长文本气泡
            }

            SetTimer(_hwnd, new IntPtr(1), FRAME_MS, IntPtr.Zero);

            MSG msg;
            while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
    }
}
