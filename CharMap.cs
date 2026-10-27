// ============================================================================
// Q Pet · 使用统计 → 「角色陪伴」方格图（treemap）
//   和「软件使用」同一套设计语言：squarified 铺格，面积 = 陪伴时长，
//   颜色阶也是同一张（面积越大越沉），所以两张卡放在一页里像一对。
//   唯一的差别是格子里放的不是 exe 图标，而是角色包里的立绘 PNG。
//
//   只算「当天」：用户要的是今天陪了多久，不是建号以来的累计。
//   数据源就是 pet-stats.txt 里的 char=名字|秒（历史日期读归档快照，同一列）。
//   累计表 pet-char-time.txt 不进图 —— 那张表跨天不清零，一旦进图，
//   今天的时长永远被 "106148 秒" 这种老数字压成一条缝，读不出今天的变化。
//
//   为什么不用条形图：九行条形能读出「谁多谁少」，但读不出「占比」——
//   眼睛要在一列长度之间来回比；铺成面积之后，占比是一眼的事，
//   而且立绘本身能把「这是哪个角色」交代清楚，比名字更省一次辨认。
//
//   尾巴怎么处理：陪伴时长能差三个数量级。按真实面积铺，最小的那几个只有 0.01%，
//   是一根发丝。所以和「软件使用」一样只画头部，剩下的一行字排在图下面
//   （其余 N 个 │ 名字 时长 · …）：面积只代表真实时长，不靠放大面积去撒谎。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static partial class Settings
{
    const double CharMapH = 258;
    const double CharMapHeadShare = 0.012;    // 占 1.2% 以上才进图，其余进下面那行字
    const int CharMapHeadMax = 8;             // 再多格子就太碎了

    static Canvas _charMap;
    static TextBlock _charTail;                                   // 图下面那行「其余 N 个 …」
    static string _charMapSig = "";
    static DateTime _charMapAt = DateTime.MinValue;
    static Size _charMapSize;
    static double _charAllTotal = 1;                              // 当天全部角色的合计（占比分母）
    static string _charEmptyMsg = "今天还没有陪伴记录";            // 空格子时的一句话（今日 / 历史各一套）
    static List<KeyValuePair<string, int>> _charItems = new List<KeyValuePair<string, int>>();   // 进图的
    static List<KeyValuePair<string, int>> _charTailItems = new List<KeyValuePair<string, int>>();
    static readonly List<CharTile> _charTiles = new List<CharTile>();

    class CharTile
    {
        public string Name;
        public Border Box;
        public TextBlock Cap;        // 名字
        public TextBlock Val;        // 当天时长
    }

    // ---------- 立绘缓存 ----------
    // 每 4 秒重扫一遍统计都会重铺格，不缓存就等于每 4 秒解码一批 PNG。
    // 键里带写入时间：换了立绘自动失效，不用手动清缓存。
    static readonly Dictionary<string, BitmapSource> _portraitCache = new Dictionary<string, BitmapSource>();

    static BitmapSource Portrait(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string key = path;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            key = path + "|" + fi.LastWriteTimeUtc.Ticks;
        }
        catch (Exception ex) { LogErr("Portrait", ex); }
        BitmapSource hit;
        if (_portraitCache.TryGetValue(key, out hit)) return hit;

        BitmapSource src = null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;      // 立刻读完，不锁文件（否则删角色会失败）
            // 格子里最大画到 112px 高，按 2 倍余量解码足够，别把 2000px 原图整个读进内存
            bi.DecodePixelWidth = 300;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            src = bi;
        }
        catch (Exception ex) { LogErr("Portrait", ex); }
        _portraitCache[key] = src;                          // 读失败也缓存，别每次重试
        return src;
    }

    static CharEntry EntryOf(string name)
    {
        foreach (CharEntry e in ChList)
            if (string.Equals(e.Name, name, StringComparison.CurrentCulture)) return e;
        return null;
    }

    // ---------- 卡片里的那块画布 ----------
    static Canvas MakeCharMap()
    {
        _charMap = new Canvas { Height = CharMapH, ClipToBounds = true };
        _charMap.SizeChanged += (s, e) =>
        {
            // 首帧 ActualWidth 是 0，等真布局完再排；拖窗口时避免每 1px 重排
            if (Math.Abs(e.NewSize.Width - _charMapSize.Width) > 6)
            {
                _charMapSize = e.NewSize;
                LayoutCharMap();
            }
        };
        return _charMap;
    }

    // 换肤后让下一帧重铺一次（格子底色是按当时的主题色算好写进 Border 的）
    static void InvalidateCharMap() { _charMapSig = ""; _charMapAt = DateTime.MinValue; }

    // 每 4 秒调一次（和桌宠落盘同一个步长）。
    // today = 当天的 char= 表；比例变了就重铺格，比例没动就只改读数。
    static void UpdateCharMap(Dictionary<string, int> today, bool isToday, bool rebuild)
    {
        if (_charMap == null) return;
        var day = today ?? new Dictionary<string, int>();
        _charEmptyMsg = isToday ? "今天还没有陪伴记录" : "这一天没有陪伴记录";

        // 名单只取「当天有时长」的角色：没陪过的角色在这一格里是 0，
        // 铺进去只会多出一堆 0 秒的灰格子，反而看不出今天是谁陪的。
        var list = new List<KeyValuePair<string, int>>();
        double sum = 0;
        foreach (var kv in day)
        {
            if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
            list.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
            sum += kv.Value;
        }
        list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value)
                                              : string.Compare(a.Key, b.Key, StringComparison.CurrentCulture));
        _charAllTotal = Math.Max(1, sum);

        // 头部进图，尾巴进下面那行字
        _charItems = new List<KeyValuePair<string, int>>();
        _charTailItems = new List<KeyValuePair<string, int>>();
        foreach (var kv in list)
        {
            bool head = _charItems.Count < CharMapHeadMax && kv.Value >= sum * CharMapHeadShare;
            if (head) _charItems.Add(kv); else _charTailItems.Add(kv);
        }

        if (_charTail != null)
        {
            if (_charTailItems.Count == 0) _charTail.Visibility = Visibility.Collapsed;
            else
            {
                var parts = new List<string>();
                foreach (var kv in _charTailItems) parts.Add(kv.Key + " " + FmtShort(kv.Value));
                _charTail.Text = "其余 " + _charTailItems.Count + " 个 │ " + string.Join(" · ", parts.ToArray());
                _charTail.Visibility = Visibility.Visible;
            }
        }

        string sig = string.Join(",", _charItems.ConvertAll(x => x.Key).ToArray())
                   + "|" + _charTailItems.Count;
        // 3.5 秒的闸门配 4 秒的刷新节拍：每一拍都重铺，面积才跟着时长一起长。
        // 闸门比节拍短一点，否则计时误差会让某一拍空转（面积停在上一拍）。
        bool stale = (DateTime.Now - _charMapAt).TotalSeconds >= 3.5;
        if (!rebuild && sig == _charMapSig && _charTiles.Count > 0 && !stale)
        {
            PaintCharMap(list);
            return;
        }
        _charMapSig = sig;
        _charMapAt = DateTime.Now;
        LayoutCharMap();
        PaintCharMap(list);
    }

    static void LayoutCharMap()
    {
        if (_charMap == null) return;
        _charMap.Children.Clear();
        _charTiles.Clear();
        if (_charItems.Count == 0)
        {
            _charMap.Children.Add(new TextBlock
            {
                Text = _charEmptyMsg, FontSize = 12,
                Foreground = C("#99000000"), Margin = new Thickness(10, 10, 0, 0)
            });
            return;
        }

        double w = _charMap.ActualWidth;
        if (w < 80) w = _charMapSize.Width;
        if (w < 80) w = 600;                       // 还没布局：先按 600 排，SizeChanged 会再来
        double h = CharMapH;
        double top = Math.Max(1, _charItems[0].Value);

        var areas = new List<double>(_charItems.Count);
        foreach (var kv in _charItems) areas.Add(Math.Max(1, kv.Value));
        var rects = Squarify(areas, new Rect(0, 0, w, h));

        for (int i = 0; i < _charItems.Count && i < rects.Count; i++)
        {
            Rect r = rects[i];
            if (r.Width < 2 || r.Height < 2) continue;
            string name = _charItems[i].Key;
            int sec = _charItems[i].Value;

            // 与「软件使用」完全同一张色阶：面积越大越沉
            Color bg = Mix(Color.FromRgb(0xF1, 0xF4, 0xF8), Darken(_themeColor, Colors.White, 4.5),
                           0.10 + 0.72 * Math.Pow(Math.Min(1.0, (double)sec / top), 0.45));
            Brush ink = KeyInk(bg);

            var box = new Border
            {
                Background = new SolidColorBrush(bg),
                Margin = new Thickness(1),
                ToolTip = TipOf(name, sec)
            };
            Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
            box.Width = Math.Max(1, r.Width - 2);
            box.Height = Math.Max(1, r.Height - 2);

            var inner = new Grid();
            double side = Math.Min(r.Width, r.Height);
            bool big = r.Width > 150 && r.Height > 130;
            bool showName = r.Width > 76 && r.Height > 42;
            bool showVal = r.Width > 100 && r.Height > 86;
            double pic = big ? Math.Min(112, r.Height * 0.46)
                             : Math.Max(0, Math.Min(58, side * 0.55 - 6));

            FrameworkElement art = ArtOf(name, pic, ink, Math.Max(12, r.Width - 24));
            if (art != null && !showName)
            {
                // 小格子：只放立绘，居中（这么小还写字等于糊成一团）
                art.HorizontalAlignment = HorizontalAlignment.Center;
                art.VerticalAlignment = VerticalAlignment.Center;
                inner.Children.Add(art);
                _charTiles.Add(new CharTile { Name = name, Box = box });
            }
            else
            {
                var stack = new StackPanel
                {
                    Margin = new Thickness(9, 8, 9, 8),
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (art != null)
                {
                    var artRow = new Grid { Height = pic, HorizontalAlignment = HorizontalAlignment.Left };
                    artRow.Children.Add(art);
                    stack.Children.Add(artRow);
                }
                var cap = new TextBlock
                {
                    Text = name, FontSize = big ? 14.5 : 12.5, FontWeight = FontWeights.Bold,
                    Foreground = ink, TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, art != null ? 7 : 0, 0, 0)
                };
                stack.Children.Add(cap);
                TextBlock val = null;
                if (showVal)
                {
                    val = new TextBlock
                    {
                        Text = FmtSec(sec), FontSize = big ? 12 : 11, Foreground = ink, Opacity = 0.82,
                        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    stack.Children.Add(val);
                }
                inner.Children.Add(stack);
                _charTiles.Add(new CharTile { Name = name, Box = box, Cap = cap, Val = val });
            }

            box.Child = inner;
            _charMap.Children.Add(box);
        }
    }

    // 立绘；没有就退回首字块（和软件图里找不到图标时的做法一致）
    static FrameworkElement ArtOf(string name, double pic, Brush ink, double maxW)
    {
        if (pic < 12) return null;
        CharEntry ce = EntryOf(name);
        if (ce != null)
        {
            BitmapSource src = Portrait(ce.Image);
            if (src != null)
            {
                // 高度定死、宽度只给上限：立绘保持原比例，窄图不被拉宽，宽图不压到文字上
                var img = new Image
                {
                    Source = src, Height = pic, MaxWidth = maxW,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
        }
        return new Border
        {
            Width = pic, Height = pic,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
                Foreground = ink, FontSize = Math.Max(10, pic * 0.5), FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    static int SecOf(List<KeyValuePair<string, int>> list, string name)
    {
        foreach (var kv in list)
            if (string.Equals(kv.Key, name, StringComparison.CurrentCulture)) return kv.Value;
        return 0;
    }

    static string TipOf(string name, int sec)
    {
        return name + "   今日 " + FmtSec(sec) + "   "
            + (sec * 100 / Math.Max(1, _charAllTotal)).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    // 每 4 秒的轻量刷新：只改读数与悬停文字，不动布局
    static void PaintCharMap(List<KeyValuePair<string, int>> today)
    {
        foreach (CharTile t in _charTiles)
        {
            int v = SecOf(today, t.Name);
            if (t.Val != null) t.Val.Text = FmtSec(v);
            if (t.Box != null) t.Box.ToolTip = TipOf(t.Name, v);
        }
    }
}
