// ============================================================================
// Q Pet · 设置 / 使用统计 → 「软件使用」方格图（treemap）
//   面积 = 使用时长占比，格内显示软件图标 + 名称 + 时长。
//   图标从 exe 里取（SHGetFileInfo），按进程名找路径，找不到就退化成首字母块。
//   布局用 squarified treemap：长宽比尽量接近 1，不会出现又细又长的条。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static partial class Settings
{
    const double AppMapH = 258;

    static Canvas _appMap;
    static string _appMapSig = "";
    static DateTime _appMapAt = DateTime.MinValue;
    static Size _appMapSize;
    static List<KeyValuePair<string, int>> _appApps = new List<KeyValuePair<string, int>>();

    class AppTile
    {
        public string Name;
        public Border Box;
        public TextBlock Cap;
        public TextBlock Val;
    }
    static readonly List<AppTile> _appTiles = new List<AppTile>();

    // 进程名 → exe 路径 / 图标。取过一次就不再碰磁盘，40 个应用的首帧也就几十毫秒。
    static readonly Dictionary<string, BitmapSource> _appIconCache = new Dictionary<string, BitmapSource>();
    static readonly Dictionary<string, string> _exeCache = new Dictionary<string, string>();

    // ---------- 卡片里的那块画布 ----------
    static Canvas MakeAppMap()
    {
        _appMap = new Canvas { Height = AppMapH, ClipToBounds = true };
        _appMap.SizeChanged += (s, e) =>
        {
            // 首帧 ActualWidth 是 0，等真布局完再排一次；拖动窗口时避免每 1px 重排
            if (Math.Abs(e.NewSize.Width - _appMapSize.Width) > 6)
            {
                _appMapSize = e.NewSize;
                LayoutAppMap();
            }
        };
        return _appMap;
    }

    // 每 4 秒调一次（和桌宠落盘同一步长）：签名没变就只改读数；面积也跟着重排，
    // 闸门取 3.5 秒而不是 4 —— 计时误差会让某一拍空转，面积就停在上一拍。
    static void UpdateAppMap(Dictionary<string, int> apps, bool rebuild)
    {
        if (_appMap == null) return;
        var list = apps.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
        string sig = string.Join(",", list.Take(14).Select(x => x.Key).ToArray());
        bool stale = (DateTime.Now - _appMapAt).TotalSeconds >= 3.5;
        if (!rebuild && sig == _appMapSig && _appTiles.Count > 0)
        {
            if (!stale) { PaintAppMap(apps); return; }
        }
        _appMapSig = sig;
        _appMapAt = DateTime.Now;

        // 只画真实软件：不要「其他 N 个」这种合成块（它没有图标、也不代表任何程序）。
        // 面积按这 14 家的**相对**占比铺满画布 —— 这张图回答的是「头部软件里谁更占时间」，
        // 悬停里的百分比仍按全机总时长算，读数不会骗人。
        _appAllTotal = Math.Max(1, list.Sum(x => x.Value));
        _appApps = list.Take(14).ToList();
        LayoutAppMap();
        PaintAppMap(apps);
    }

    static double _appTotal;
    static double _appAllTotal;      // 全机总时长（只有当悬停读数的分母，不参与铺面积）

    static void LayoutAppMap()
    {
        if (_appMap == null || _appApps.Count == 0) return;
        double w = _appMap.ActualWidth;
        if (w < 80) w = _appMapSize.Width;
        if (w < 80) w = 600;                       // 还没布局：先按 600 排，SizeChanged 会再来
        double h = AppMapH;
        _appTotal = Math.Max(1, _appApps.Sum(x => x.Value));

        var rects = Squarify(_appApps.Select(x => (double)Math.Max(1, x.Value)).ToList(), new Rect(0, 0, w, h));

        _appMap.Children.Clear();
        _appTiles.Clear();
        if (_snapPaths == null || _snapPaths.Count == 0) SnapshotProcessPaths();

        for (int i = 0; i < _appApps.Count && i < rects.Count; i++)
        {
            Rect r = rects[i];
            if (r.Width < 2 || r.Height < 2) continue;
            string name = _appApps[i].Key;
            int sec = _appApps[i].Value;
            double t = (double)sec / _appTotal;

            // 面积越大颜色越沉；同一套色阶用在高频键那套上，两张卡看起来是一家人
            Color bg = Mix(Color.FromRgb(0xF1, 0xF4, 0xF8), Darken(_themeColor, Colors.White, 4.5),
                           0.10 + 0.72 * Math.Pow(t / Math.Max(0.0001, (double)_appApps[0].Value / _appTotal), 0.45));
            Brush ink = KeyInk(bg);

            var box = new Border
            {
                Background = new SolidColorBrush(bg),
                Margin = new Thickness(1),
                ToolTip = name + "   " + FmtSec(sec) + "   "
                    + (sec * 100 / Math.Max(1, _appAllTotal)).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            };
            Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
            box.Width = Math.Max(1, r.Width - 2); box.Height = Math.Max(1, r.Height - 2);

            var inner = new Grid();
            double side = Math.Min(r.Width, r.Height);
            bool big = r.Width > 150 && r.Height > 130;
            double ico = big ? 56 : Math.Min(46, Math.Max(0, side * 0.42 - 4));
            bool showName = r.Width > 74 && r.Height > 40;
            bool showSec = r.Width > 92 && r.Height > 66;
            double capSize = big ? 14.5 : 12.5;
            double valSize = big ? 12 : 11;

            if (showName)
            {
                var stack = new StackPanel
                {
                    Margin = new Thickness(9, 7, 7, 6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var icoRow = new Grid { Height = Math.Max(16, ico), HorizontalAlignment = HorizontalAlignment.Left };
                icoRow.Children.Add(AppIconBox(name, ico, ink));
                stack.Children.Add(icoRow);
                if (showSec)
                {
                    var cap = new TextBlock
                    {
                        Text = name, FontSize = capSize, FontWeight = FontWeights.Bold, Foreground = ink,
                        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0)
                    };
                    stack.Children.Add(cap);
                    var val = new TextBlock
                    {
                        Text = FmtSec(sec), FontSize = valSize, Foreground = ink, Opacity = 0.82,
                        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
                    };
                    stack.Children.Add(val);
                    _appTiles.Add(new AppTile { Name = name, Box = box, Cap = cap, Val = val });
                }
                else
                {
                    var cap = new TextBlock
                    {
                        Text = name, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ink,
                        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0)
                    };
                    stack.Children.Add(cap);
                    _appTiles.Add(new AppTile { Name = name, Box = box, Cap = cap });
                }
                inner.Children.Add(stack);
            }
            else
            {
                // 小格子只放图标，居中：这么小还写字，等于把字糊成一团
                var icoBox = AppIconBox(name, Math.Max(12, Math.Min(28, side * 0.5)), ink);
                icoBox.HorizontalAlignment = HorizontalAlignment.Center;
                icoBox.VerticalAlignment = VerticalAlignment.Center;
                inner.Children.Add(icoBox);
            }

            box.Child = inner;
            _appMap.Children.Add(box);
        }
    }

    // 图标位：有真图标就画图，没有就把首字母做成一个方块（比空白好认）
    static FrameworkElement AppIconBox(string name, double size, Brush ink)
    {
        BitmapSource src = AppIcon(name);
        if (src != null)
        {
            var img = new Image
            {
                Source = src, Width = size, Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            // 附加属性不能写在初始化器里
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        string ch = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        var bd = new Border
        {
            Width = size, Height = size,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock
            {
                Text = ch, Foreground = ink, FontSize = Math.Max(9, size * 0.5),
                FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        return bd;
    }

    // 每秒只更新读数（面积 5 秒才重排），文字颜色不变
    static void PaintAppMap(Dictionary<string, int> apps)
    {
        foreach (AppTile t in _appTiles)
        {
            int v;
            if (!apps.TryGetValue(t.Name, out v)) continue;
            if (t.Val != null) t.Val.Text = FmtSec(v);
            if (t.Box != null)
                t.Box.ToolTip = t.Name + "   " + FmtSec(v) + "   "
                    + ((double)v * 100 / Math.Max(1, _appAllTotal)).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }

    // ---------- squarified treemap ----------
    static double WorstRatio(List<double> areas, int start, int end, double sum, double side)
    {
        double maxA = 0, minA = double.MaxValue;
        for (int i = start; i <= end; i++)
        {
            if (areas[i] > maxA) maxA = areas[i];
            if (areas[i] < minA) minA = areas[i];
        }
        if (sum <= 0 || minA <= 0) return double.MaxValue;
        double s2 = side * side;
        return Math.Max(s2 * maxA / (sum * sum), (sum * sum) / (s2 * minA));
    }

    static List<Rect> Squarify(List<double> values, Rect rect)
    {
        var outRects = new List<Rect>(values.Count);
        if (values.Count == 0) return outRects;
        double total = 0;
        foreach (double v in values) total += v;
        if (total <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            for (int i = 0; i < values.Count; i++) outRects.Add(rect);
            return outRects;
        }
        double scale = rect.Width * rect.Height / total;
        var areas = new List<double>(values.Count);
        foreach (double v in values) areas.Add(Math.Max(0.0001, v * scale));

        Rect r = rect;
        int idx = 0;
        while (idx < areas.Count)
        {
            bool vertical = r.Width >= r.Height;         // 条子贴短边铺
            double side = vertical ? r.Height : r.Width;
            double sum = 0;
            int start = idx;
            double best = double.MaxValue;
            while (idx < areas.Count)
            {
                double next = sum + areas[idx];
                double w1 = WorstRatio(areas, start, idx, next, side);
                if (w1 > best && idx > start) break;
                best = w1;
                sum = next;
                idx++;
            }
            double thick = side > 0 ? sum / side : 0;
            if (thick <= 0) break;
            if (vertical)
            {
                double y = r.Y;
                for (int i = start; i < idx; i++)
                {
                    double len = areas[i] / thick;
                    outRects.Add(new Rect(r.X, y, thick, len));
                    y += len;
                }
                r = new Rect(r.X + thick, r.Y, Math.Max(0, r.Width - thick), r.Height);
            }
            else
            {
                double x = r.X;
                for (int i = start; i < idx; i++)
                {
                    double len = areas[i] / thick;
                    outRects.Add(new Rect(x, r.Y, len, thick));
                    x += len;
                }
                r = new Rect(r.X, r.Y + thick, r.Width, Math.Max(0, r.Height - thick));
            }
            if (r.Width <= 0.5 || r.Height <= 0.5) break;
        }
        while (outRects.Count < values.Count) outRects.Add(new Rect(0, 0, 0, 0));
        return outRects;
    }

    // ---------- 找 exe + 取图标 ----------
    static Dictionary<string, string> _snapPaths;

    // 在跑的进程 → exe 路径（一次快照，之后都用它）。读 MainModule 可能被权限拦，逐个 try。
    static void SnapshotProcessPaths()
    {
        _snapPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string f = null;
                    try { f = p.MainModule.FileName; } catch (Exception) { }
                    if (!string.IsNullOrEmpty(f) && !_snapPaths.ContainsKey(p.ProcessName))
                        _snapPaths[p.ProcessName] = f;
                }
                catch (Exception ex) { LogErr("SnapshotProcessPaths", ex); }
                finally { try { p.Dispose(); } catch (Exception) { } }
            }
        }
        catch (Exception ex) { LogErr("SnapshotProcessPaths", ex); }
    }
    static Dictionary<string, List<string>> _muiFile, _muiName;

    // HKCU\...\MuiCache 存着「运行过的 exe 完整路径」。两个坑：
    //   ① 键名带 `.FriendlyAppName` / `.ApplicationCompany` 后缀，不能直接 EndsWith(".exe")
    //   ② 显示名和进程名经常不一样（Marvis 的进程叫 MarvisLauncher），所以要建两套映射
    static void ScanMuiCache()
    {
        _muiFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _muiName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\MuiCache"))
            {
                if (k == null) return;
                string[] names;
                try { names = k.GetValueNames(); } catch (Exception) { return; }
                foreach (string v in names)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(v)) continue;
                        int ex = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (ex < 0) continue;
                        string path = v.Substring(0, ex + 4);
                        string fn = Path.GetFileNameWithoutExtension(path);
                        // 同名会命中多条（旧版本、不同盘），全都留着，取路径时轮到存在的那个
                        if (!string.IsNullOrEmpty(fn)) AddPath(_muiFile, fn, path);
                        if (v.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase))
                        {
                            string disp = null;
                            try { disp = k.GetValue(v) as string; } catch (Exception) { }
                            if (!string.IsNullOrEmpty(disp))
                            {
                                disp = disp.Trim();
                                if (disp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    disp = disp.Substring(0, disp.Length - 4);
                                if (disp.Length > 0) AddPath(_muiName, disp, path);
                            }
                        }
                    }
                    catch (Exception ex) { LogErr("ScanMuiCache", ex); }
                }
            }
        }
        catch (Exception ex) { LogErr("ScanMuiCache", ex); }
    }

    static void AddPath(Dictionary<string, List<string>> d, string key, string path)
    {
        List<string> l;
        if (!d.TryGetValue(key, out l)) { l = new List<string>(); d[key] = l; }
        if (!l.Contains(path)) l.Add(path);
    }

    static string FirstExisting(Dictionary<string, List<string>> d, string key)
    {
        List<string> l;
        if (d == null || !d.TryGetValue(key, out l)) return null;
        foreach (string p in l)
        {
            try { if (File.Exists(p)) return p; } catch (Exception) { }
        }
        return null;
    }

    // 名字对不上时退一步：MuiCache 里有没有「文件名以它开头」的（ZenlessZoneZero → ZenlessZoneZero_2.0.exe）
    static string FirstPrefix(Dictionary<string, List<string>> d, string name)
    {
        if (d == null || name.Length < 4) return null;
        foreach (var kv in d)
        {
            if (!kv.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string p in kv.Value)
            {
                try { if (File.Exists(p)) return p; } catch (Exception) { }
            }
        }
        return null;
    }

    static string FindExe(string name)
    {
        string hit;
        if (_exeCache.TryGetValue(name, out hit)) return hit;
        hit = null;
        try
        {
            if (_snapPaths != null && _snapPaths.TryGetValue(name, out hit) && File.Exists(hit)) { }
            else hit = null;

            if (hit == null)
            {
                if (_muiFile == null) ScanMuiCache();
                hit = FirstExisting(_muiFile, name);
                if (hit == null) hit = FirstExisting(_muiName, name);
                if (hit == null) hit = FirstPrefix(_muiFile, name);
            }

            if (hit == null)
            {
                // 没在跑：先问注册表 App Paths，再在几个常见目录里找一层
                string[] roots =
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\",
                };
                foreach (string root in roots)
                {
                    foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    {
                        try
                        {
                            using (var k = hive.OpenSubKey(root + name + ".exe"))
                            {
                                string v = k == null ? null : k.GetValue(null) as string;
                                if (!string.IsNullOrEmpty(v))
                                {
                                    v = v.Trim('"');
                                    if (File.Exists(v)) { hit = v; break; }
                                }
                            }
                        }
                        catch (Exception ex) { LogErr("FindExe", ex); }
                    }
                    if (hit != null) break;
                }
            }

            if (hit == null)
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string[] dirs =
                {
                    pf, pf86, Path.Combine(lad, "Programs"), ad, win, Path.Combine(win, "System32"),
                    Path.Combine(pf, "WindowsApps"), Path.Combine(win, "SysWOW64")
                };
                foreach (string d in dirs)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    try
                    {
                        string c = Path.Combine(d, name + ".exe");
                        if (File.Exists(c)) { hit = c; break; }
                    }
                    catch (Exception ex) { LogErr("FindExe", ex); }
                }
            }
        }
        catch (Exception ex) { LogErr("FindExe", ex); }
        _exeCache[name] = hit;
        return hit;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
                                       ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    // ---- 高清图标通道 ----
    // SHGetFileInfo(大图标) 只给 32×32，放大到 56px 就是糊的。IShellItemImageFactory
    // 走的是系统缩略图/图标仓库，能按需给到 256px，还是原始 alpha，画多大都清楚。
    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx; public int cy; }

    [Flags]
    enum SIIGBF { ResizeToFit = 0, BiggerSizeOk = 1, MemoryOnly = 2, IconOnly = 4, ThumbnailOnly = 8, InCacheOnly = 16 }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    static readonly Guid IID_IShellItemImageFactory = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    // 128px 足够：卡片里最大画到 56，留 2 倍余量给高 DPI
    static BitmapSource HiResShellIcon(string exe)
    {
        IShellItemImageFactory fac = null;
        try
        {
            try { SHCreateItemFromParsingName(exe, IntPtr.Zero, IID_IShellItemImageFactory, out fac); }
            catch (Exception ex) { LogErr("HiResShellIcon", ex); return null; }
            if (fac == null) return null;
            IntPtr hbm;
            int hr = fac.GetImage(new SIZE { cx = 128, cy = 128 }, SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                BitmapSource bmp = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                if (bmp != null) { bmp.Freeze(); return bmp; }
            }
            finally { DeleteObject(hbm); }
        }
        catch (Exception ex) { LogErr("HiResShellIcon", ex); }
        finally
        {
            try { if (fac != null) Marshal.ReleaseComObject(fac); } catch (Exception) { }
        }
        return null;
    }

    const uint SHGFI_ICON = 0x100;
    const uint SHGFI_LARGEICON = 0x0;

    static BitmapSource AppIcon(string name)
    {
        BitmapSource src;
        if (_appIconCache.TryGetValue(name, out src)) return src;
        src = null;
        try
        {
            string exe = FindExe(name);
            if (!string.IsNullOrEmpty(exe))
            {
                src = HiResShellIcon(exe);
                if (src != null) { _appIconCache[name] = src; return src; }
                var shfi = new SHFILEINFO();
                IntPtr r = SHGetFileInfo(exe, 0, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                                         SHGFI_ICON | SHGFI_LARGEICON);
                if (r != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        src = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty,
                                                                  BitmapSizeOptions.FromEmptyOptions());
                        if (src != null) src.Freeze();
                    }
                    finally { DestroyIcon(shfi.hIcon); }
                }
            }
        }
        catch (Exception ex) { LogErr("AppIcon", ex); }
        _appIconCache[name] = src;             // 找不到也缓存，别每秒重搜一遍目录
        return src;
    }
}
