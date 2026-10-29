// ============================================================================
// Q Pet · 设置 / 使用统计 → 「按键使用」卡片
//   左：键盘热力图，按真实键盘布局铺满 75 个键位，颜色越深 = 按得越多
//   右：TOP 8 榜单（退格 / 反斜杠不参与：键名太长，读数会被挤得参差不齐）
// 颜色不走主题色原色，走 KeyRamp()：从 #EFF2F6 按对数曲线走到 AccentSolid
// （压深到白字 4.5:1 那一档）。对数是为了让 60 次的 Q 不被 800 次的空格压成纯白。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

static partial class Settings
{
    // 当前数据的一份快照，重涂时按 vk 查次数
    static Dictionary<int, int> _keyMap = new Dictionary<int, int>();
    static StackPanel KeyHost;
    static List<KeyCell> _keyCells = new List<KeyCell>();
    static List<KeyValuePair<int, int>> _keyData = new List<KeyValuePair<int, int>>();
    static string _keySig2 = "";
    static int _keyMax = 1;

    class KeyCell
    {
        public int Vk;
        public Border Box;
        public TextBlock Cap;
    }

    // 一行里一个键：显示字、虚拟键码、宽度（1u = 一个字母键，每行合计 15u）
    class KeyDef
    {
        public string Label; public int Vk; public double U;
        public KeyDef(string l, int v, double u) { Label = l; Vk = v; U = u; }
    }

    static KeyDef K(string l, int v) { return new KeyDef(l, v, 1); }
    static KeyDef K(string l, int v, double u) { return new KeyDef(l, v, u); }

    static readonly KeyDef[][] KeyRows = new KeyDef[][]
    {
        // 13 个 1u + 2u 退格 = 15u。少一个反引号时第一行只有 14u，
        // 退格的右边缘就比下面那排的 \ Enter 早收一格
        new KeyDef[] { K("`",0xC0), K("1",0x31), K("2",0x32), K("3",0x33), K("4",0x34), K("5",0x35),
                       K("6",0x36), K("7",0x37), K("8",0x38), K("9",0x39), K("0",0x30),
                       K("-",0xBD), K("=",0xBB), K("⌫",0x08,2) },
        new KeyDef[] { K("Tab",0x09,1.5), K("Q",0x51), K("W",0x57), K("E",0x45), K("R",0x52),
                       K("T",0x54), K("Y",0x59), K("U",0x55), K("I",0x49), K("O",0x4F),
                       K("P",0x50), K("[",0xDB), K("]",0xDD), K("\\",0xDC,1.5) },
        new KeyDef[] { K("Caps",0x14,1.75), K("A",0x41), K("S",0x53), K("D",0x44), K("F",0x46),
                       K("G",0x47), K("H",0x48), K("J",0x4A), K("K",0x4B), K("L",0x4C),
                       K(";",0xBA), K("'",0xDE), K("Enter",0x0D,2.25) },
        new KeyDef[] { K("Shift",0xA0,2.25), K("Z",0x5A), K("X",0x58), K("C",0x43), K("V",0x56),
                       K("B",0x42), K("N",0x4E), K("M",0x4D), K(",",0xBC), K(".",0xBE),
                       K("/",0xBF), K("Shift",0xA1,2.75) },
        new KeyDef[] { K("Ctrl",0xA2,1.25), K("Win",0x5B,1.25), K("Alt",0xA4,1.25), K("空格",0x20,6.25),
                       K("Alt",0xA5,1.25), K("Win",0x5C,1.25), K("≡",0x5D,1.25), K("Ctrl",0xA3,1.25) },
    };

    // ---------- 颜色 ----------
    static Color KeyAccent() { return Darken(_themeColor, Colors.White, 4.5); }
    static Color KeyEmpty() { return Color.FromRgb(0xF2, 0xF5, 0xF9); }

    // 0 = 没按过，1 = 最常用。三种视图的取值曲线不一样：
    //   热力图用对数（不然 60 次的 Q 会被 800 次的空格压成纯白）
    //   条形图用线性（长度已经在表达次数，颜色只需要跟着走一档）
    //   树图用平方根（面积已经在表达占比，颜色做纹理）
    static double KeyT(int v)
    {
        if (v <= 0) return 0;
        return Math.Log(1.0 + v) / Math.Log(1.0 + Math.Max(1, _keyMax));
    }

    static Color KeyColor(double t)
    {
        if (t <= 0) return KeyEmpty();
        if (t > 1) t = 1;
        return Mix(Color.FromRgb(0xEF, 0xF2, 0xF6), KeyAccent(), 0.06 + 0.94 * t);
    }

    static Color KeyRamp(int v) { return KeyColor(KeyT(v)); }            // 热力图

    static Brush KeyInk(Color bg)
    {
        Color dark = Color.FromRgb(0x1A, 0x1D, 0x21);
        return Contrast(bg, Colors.White) >= Contrast(bg, dark)
            ? Brushes.White : new SolidColorBrush(dark);
    }

    // ---------- 卡片骨架 ----------
    static UIElement BuildKeyCard()
    {
        var sp = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Txt("按键使用", 14, "#E4000000", true));
        LblStatKeys = Txt("共 0 次", 12, "#99000000");
        LblStatKeys.VerticalAlignment = VerticalAlignment.Center;
        LblStatKeys.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(LblStatKeys, 1); head.Children.Add(LblStatKeys);
        sp.Children.Add(head);

        sp.Children.Add(Txt("颜色越深按得越多 │ 悬停看次数", 12, "#99000000"));
        sp.Children.Add(new Border { Height = 10 });

        KeyHost = new StackPanel();
        sp.Children.Add(KeyHost);
        return sp;
    }

    static void BuildKeyView()
    {
        if (KeyHost == null) return;
        KeyHost.Children.Clear();
        _keyCells.Clear();
        BuildHeat();
    }

    // ---------- 方案 1：键盘热力图 ----------
    static void BuildHeat()
    {
        KeyHost.Children.Add(HeatBoard());
        KeyHost.Children.Add(HeatLegend());
    }

    // 热力图（左）+ 前 8 名（右）并排：上下叠会被窗口底部截断，左右并排一屏放得下
    static Grid HeatBoard()
    {
        // 每行 15u，网格按 0.25u 一格切成 60 列，这样 1.25u / 1.75u 这些也能对齐
        var board = new Grid();
        for (int c = 0; c < 60; c++) board.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < KeyRows.Length; r++)
            board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

        for (int r = 0; r < KeyRows.Length; r++)
        {
            int col = 0;
            foreach (KeyDef k in KeyRows[r])
            {
                var box = new Border
                {
                    Margin = new Thickness(1.5),
                    Background = C("#F2F5F9"),
                    BorderBrush = C("#E9ECF1"),
                    BorderThickness = new Thickness(1),
                    SnapsToDevicePixels = true
                };
                var cap = new TextBlock
                {
                    Text = k.Label, FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = C("#B4BAC4")
                };
                box.Child = cap;
                box.ToolTip = k.Label + " —";
                Grid.SetRow(box, r);
                Grid.SetColumn(box, col);
                int span = (int)Math.Round(k.U * 4);
                if (span > 1) Grid.SetColumnSpan(box, span);
                board.Children.Add(box);
                _keyCells.Add(new KeyCell { Vk = k.Vk, Box = box, Cap = cap });
                col += span;
            }
        }
        return board;
    }

    static Grid HeatLegend()
    {
        var legend = new Grid { Margin = new Thickness(2, 11, 2, 0) };
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        legend.ColumnDefinitions.Add(new ColumnDefinition());
        var lo = Txt("少", 11, "#99000000");
        lo.VerticalAlignment = VerticalAlignment.Center;
        legend.Children.Add(lo);
        var strip = new Border
        {
            Width = 86, Height = 6, Margin = new Thickness(7, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(
                KeyColor(0.0), KeyAccent(), 0)
        };
        Grid.SetColumn(strip, 1); legend.Children.Add(strip);
        var hi = Txt("多", 11, "#99000000");
        hi.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hi, 2); hi.HorizontalAlignment = HorizontalAlignment.Left;
        legend.Children.Add(hi);
        return legend;
    }

    // ---------- 数据入口 ----------
    // 每秒被 UpdateStats 调一次：键集合不变就只重涂颜色，避免滚动位置和顺序抖动
    static void UpdateKeyView(Dictionary<int, int> keys)
    {
        var ordered = keys.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
        _keyData = ordered;
        _keyMap = keys;             // 符号不变时也要刷新这张快照，否则读数停在上一次
        int total = 0;
        foreach (var kv in ordered) total += kv.Value;
        _keyMax = ordered.Count > 0 ? Math.Max(1, ordered[0].Value) : 1;

        if (LblStatKeys != null)
            LblStatKeys.Text = "共 " + total.ToString("0", CultureInfo.InvariantCulture)
                             + " 次 │ " + ordered.Count + " 个按键";

        string sig = string.Join(",", ordered.Select(x => x.Key.ToString()).ToArray());
        bool rebuild = sig != _keySig2 || KeyHost == null || KeyHost.Children.Count == 0;
        _keySig2 = sig;
        if (rebuild) BuildKeyView();
        RepaintKeyView();       // 建完统一涂一遍：热力图的格子是空壳，颜色全在这里给
    }

    static void RepaintKeyView()
    {
        if (_keyCells.Count > 0)
        {
            foreach (KeyCell cell in _keyCells)
            {
                int v; _keyMap.TryGetValue(cell.Vk, out v);
                Color bg = KeyRamp(v);
                cell.Box.Background = new SolidColorBrush(bg);
                cell.Box.BorderBrush = v > 0 ? Brushes.Transparent : C("#E9ECF1");
                cell.Cap.Foreground = v > 0 ? KeyInk(bg) : C("#B4BAC4");
                cell.Box.ToolTip = VkName(cell.Vk) + (v > 0 ? "  " + v + " 次" : "  没按过");
            }
        }
    }

}
