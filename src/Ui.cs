using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

static partial class Settings
{
        static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t),
                                 (byte)Math.Round(a.G + (b.G - a.G) * t),
                                 (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        static void ApplyUiFont()
        {
            try
            {
                // 注意：Saira Wide 只有拉丁和数字，没有汉字。放开让 WPF 自己找回退字体，
                // 不同控件会落到不同字体上 —— 实测按钮/标签是黑体、下拉框里却是宋体（截图肉眼可见）。
                // 所以显式写出回退链：拉丁数字用 Saira Wide，汉字统一落到雅黑。
                var ff = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#QPet UI");
                int n = 0;
                foreach (Typeface tf in ff.GetTypefaces()) n++;
                if (n > 0)
                {
                    Win.FontFamily = ff;
                    UiFace = ff;                    // 弹窗是独立 Window，拿不到主窗的继承值
                    _uiFontInfo = "QPet UI ×" + n + "（拉丁 Saira Wide │ 汉字 雅黑）";
                }
                else _uiFontInfo = "字体族为空，退回系统字体";
            }
            catch (Exception ex)
            {
                _uiFontInfo = "加载失败：" + ex.Message;
                LogErr("font", ex);
            }
        }

        static void SetRes(string key, object val)
        {
            if (Win.Resources.Contains(key)) Win.Resources[key] = val;
            else Win.Resources.Add(key, val);
        }

        // ---- 主题色的「深一档」 ----
        // 主题色是给「小面积标记」用的原色，直接铺大面积或当文字就会翻车：
        // 青绿 #2FA8A0 上白字 2.9:1（读不出）、当 24px 读数压在 Desk 上也才 2.3:1。
        // 所以按用途把同一个色相往黑里压，压到刚好够对比度为止 —— 色相不变，换主题色仍然认得出。
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        // 整体提亮/压深：t > 0 往白里调，t < 0 按比例压深（t=-0.18 即 -18%）。
        // 按钮的悬停/按下用它，而不是 Darken —— Darken 是「压到某个对比度为止」，
        // 底色本来就够深时它直接原样返回，三态会变成同一个颜色（石墨、墨黑主题踩过）。
        internal static Color Shade(Color c, double t)
        {
            if (t >= 0) return Tint(c, t);
            double f = 1 + t;
            if (f < 0) f = 0;
            return Color.FromRgb((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f), (byte)Math.Round(c.B * f));
        }

        static void InstallButtonChamfer()
        {
            try
            {
                _primaryBtnStyle = Win.FindResource("PrimaryButton") as Style;
                if (_primaryBtnStyle == null) return;
                if (!_chamferHooked)
                {
                    _chamferHooked = true;
                    EventManager.RegisterClassHandler(typeof(Button), FrameworkElement.SizeChangedEvent,
                        new SizeChangedEventHandler(OnBtnSizeForChamfer), true);
                }
                ApplyChamferIfPrimary(Win.FindName("btnSave") as Button);
                ApplyChamferIfPrimary(Win.FindName("btnHomeShow") as Button);
            }
            catch (Exception ex) { LogErr("chamfer", ex); }
        }

        static void OnBtnSizeForChamfer(object sender, SizeChangedEventArgs e)
        {
            ApplyChamferIfPrimary(sender as Button);
        }

        static void ApplyChamferIfPrimary(Button b)
        {
            if (b == null || _primaryBtnStyle == null) return;
            if (!ReferenceEquals(b.Style, _primaryBtnStyle)) return;
            double w = b.ActualWidth, h = b.ActualHeight;
            if (w <= 12 || h <= 12) return;      // 还没布局出来，等下一次 SizeChanged
            const double cut = 8;
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

        // 信号色（Accent）只做填充；它上面压的字必须跟着信号色亮度走。
        static void ApplyThemeColor(Color c) { ApplyThemeColor(c, false); }

        // animate = 用户刚点了色块：填充类资源从旧色渐变到新色（220ms），整屏一起走。
        static void ApplyThemeColor(Color c, bool animate)
        {
            try
            {
                _themeColor = c;
                // 220ms：够看出是渐变、又不至于让人等；--themetest --motion 时会拉长成 1200ms 取样
                int ms = animate && !_motionOff ? (_themeFadeMs > 0 ? _themeFadeMs : 220) : 0;
                // 两个深一档：填充（白字要够读）/ 读数（压在灰栏上要够读）
                Color solid = Darken(c, Colors.White, 4.5);
                // 按钮的两档交互色：跟着主题色现算（算法见 BtnTones）
                Color bf, bh, bp;
                BtnTones(c, out bf, out bh, out bp);

                FadeRes("Accent", c, ms);
                FadeRes("AccentSolid", solid, ms);
                // 承重线：比填充再深一档。
                // 基准必须是 solid（不是主题色）：Darken 是「压到某个对比度为止、压够了就原样返回」，
                // 主题色本身已经够深时（石墨 / 墨黑那类）Darken(c,…,8.5) 直接返回 c，
                // 于是线 = 填充色，这条 2px 承重线等于不存在（--selftest 报「与填充 1.00:1 ✗」）。
                Color edge = solid;
                for (double f = 0.96; f >= 0.20; f -= 0.04)
                {
                    edge = Shade(solid, f - 1.0);
                    if (Contrast(edge, solid) >= 1.85) break;
                }
                FadeRes("AccentEdge", edge, ms);
                FadeRes("AccentHover", bh, ms);
                FadeRes("AccentPress", bp, ms);
                // 选中行的浅底：主题色往白里调，只当底色用，不参与对比度计算
                FadeRes("AccentSoft", Tint(c, 0.90), ms);
                FadeRes("AccentInk", Darken(c, Color.FromRgb(0xE1, 0xE5, 0xEA), 4.5), ms);
                // 字色不渐变：渐变到一半可能落在浅底上，读不出来
                SetRes("OnAccent", new SolidColorBrush(SignalInk(c)));
                // 压深后的底色一定偏深，白字才稳；黄这种亮色压完也是深底，所以固定用白
                SetRes("OnAccentSolid", new SolidColorBrush(Colors.White));

                // 左栏那五条刻度的点亮色是 Brush 实例（不是 DynamicResource），必须重涂
                RepaintGauges();
                // 快速预设的选中块也是手涂的 Brush，换主题要跟着走
                SyncSizePresets();
                // 右栏八格磁贴的填充/字色/图标是构建时抓的 Brush 实例（PaintTiles 里 `t.Background = solid`），
                // 不重涂就永远停在上一个主题色上 —— 这就是「换色后右侧按钮不变色」的根因
                PaintTiles();
                // 仪表带右侧的「未保存/已保存」也是抓的实例
                SetPageBarState();
                // 左栏「05 主题色」的色值读数是手写文本，不跟着资源走，必须手动同步
                SyncAccentReadouts();
                // 导入页的角色方框卡（当前项描边 / 角标）和统计页的角色方格图，颜色也是建的时候抓的实例
                if (ImportList != null && ImportList.Children.Count > 0) RefreshImport();
                InvalidateCharMap();
            }
            catch (Exception ex) { LogErr("theme", ex); }
        }

        // 换主题色时让填充类资源从旧色渐变到新色。
        // 资源里那份 SolidColorBrush 是 XAML 里声明的、已被冻结（改 .Color 会抛只读），
        // 所以换成一个可动画的新实例、从旧色起步，DynamicResource 的消费者自动跟到新实例上。
        static void FadeRes(string key, Color to, int ms)
        {
            SolidColorBrush cur = Win != null ? Win.Resources[key] as SolidColorBrush : null;
            Color from = cur != null ? cur.Color : to;
            var nb = new SolidColorBrush(from);
            if (ms > 0 && from != to)
            {
                nb.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else nb.Color = to;
            // 顺序不能反：必须先设色/挂动画，再放进资源字典。
            // WPF 会把塞进 ResourceDictionary 且「能冻」的 Freezable 直接冻结，
            // 冻过之后再写 .Color 就是「处于只读状态」——先插后设会静默炸掉整次换肤（踩过）。
            // 挂了动画的画笔 CanFreeze=false，字典冻不动它，动画能正常跑完。
            SetRes(key, nb);
        }

        // 左栏 / 调色板里「#RRGGBB」这两处读数是手写文本，换肤时要一起改
        static void SyncAccentReadouts()
        {
            string hex = "#" + _themeColor.R.ToString("X2")
                               + _themeColor.G.ToString("X2")
                               + _themeColor.B.ToString("X2");
            if (TxtHomeTheme != null) TxtHomeTheme.Text = hex;
            if (TxtPalAccent != null) TxtPalAccent.Text = hex;
        }

        // ---------------- 统计页（内容动态生成 · 实时刷新） ----------------
        // 令牌值 → 落地色值。
        // 界面是按「纸 / 板 / 墨 / 信号色」这套令牌搭的（形状、字号、切角都依赖它），
        // 但落地配色跟原来的 Fluent 基准一致：令牌名不改，只带这一层映射。
        // 好处是几百处调用点不用动，改配色只改这一张表 + XAML 里的同名画笔。
        static readonly Dictionary<string, string> TokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#161616", "#E4282320" },   // 墨 → 主文字（暖调近黑，和白纸不打架）
            { "#5C5C5C", "#99574F42" },   // 次要墨 → 说明文字
            { "#D9D9D6", "#D9DDE4" },     // 线 → 分隔线
            { "#E4E4E0", "#E6EAF0" },     // 安静填充 → 悬停底色
            { "#F4F4F2", "#EDF0F4" },     // 纸 → 中栏工作面
            { "#B3261E", "#C42B1C" },     // 危险
            { "#F7E7E5", "#FDECEA" },     // 危险浅底
        };

        static TextBlock Txt(string text, double size, string hex, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, Foreground = C(hex), TextWrapping = TextWrapping.Wrap };
            if (bold) t.FontWeight = FontWeights.Bold;   // 卡片标题是粗体，不是半粗：和页面标题同一族
            return t;
        }

        static Border Card(UIElement child)
        {
            var b = new Border
            {
                Background = Brushes.Transparent, BorderBrush = C("#D9D9D6"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0), Padding = new Thickness(0, 13, 0, 13),
                // 相邻两块用 -1 叠成一根线，读起来是一张纸的分节，不是一堆卡片
                Margin = new Thickness(0, 0, 0, -1)
            };
            b.Child = child;
            return b;
        }

        // 页面头：眉标（// ENGLISH）+ 中文粗体标题 + 一句说明 + 一根墨线。
        // 动态生成的页面（动画 / 统计 / 关于）用它，静态页在 XAML 里写同样四行。
        static void AddPageHead(Panel host, string eyebrow, string title, string sub, string tip = null)
        {
            if (host == null) return;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock
            {
                Text = title, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = C("#161616")
            });
            head.Children.Add(new TextBlock
            {
                Text = eyebrow, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = C("#5C5C5C"),
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 9)
            });
            host.Children.Add(head);
            var s = new TextBlock
            {
                Text = sub, FontSize = 12, Foreground = C("#5C5C5C"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            if (!string.IsNullOrEmpty(tip)) s.ToolTip = tip;
            host.Children.Add(s);
            // 纯黑一条到底：原来右端那 72px 主题色补丁和左段黑线接不上，看着像两个人画的
            var rule = new Grid { Height = 2, Margin = new Thickness(0, 16, 0, 2) };
            rule.Children.Add(new Border { Background = Brushes.Black });
            host.Children.Add(rule);
        }

        // 实现在 Core.cs（与 Pet.exe 共用一份，Tests 工程直接对它跑断言）
        static string FmtSec(int s) { return QCore.FmtSec(s); }

        static string FmtShort(int s) { return QCore.FmtShort(s); }

        static string VkName(int vk)
        {
            if (vk >= 65 && vk <= 90) return ((char)vk).ToString();
            if (vk >= 48 && vk <= 57) return ((char)vk).ToString();
            if (vk >= 96 && vk <= 105) return "小键盘" + (vk - 96);
            if (vk >= 112 && vk <= 135) return "F" + (vk - 111);
            switch (vk)
            {
                case 8: return "Backspace"; case 9: return "Tab"; case 13: return "Enter";
                case 16: return "Shift"; case 17: return "Ctrl"; case 18: return "Alt";
                case 20: return "CapsLock"; case 27: return "Esc"; case 32: return "空格";
                case 37: return "←"; case 38: return "↑"; case 39: return "→"; case 40: return "↓";
                case 46: return "Delete"; case 45: return "Insert"; case 36: return "Home"; case 35: return "End";
                case 33: return "PageUp"; case 34: return "PageDown";
                case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-";
                case 190: return "."; case 191: return "/"; case 192: return "`"; case 219: return "[";
                case 220: return "\\"; case 221: return "]"; case 222: return "'";
                case 160: return "左Shift"; case 161: return "右Shift";
                case 162: return "左Ctrl"; case 163: return "右Ctrl";
                case 164: return "左Alt"; case 165: return "右Alt";
                case 91: return "Win"; case 92: return "Win(右)"; case 93: return "菜单键";
            }
            return "VK" + vk;
        }

}
