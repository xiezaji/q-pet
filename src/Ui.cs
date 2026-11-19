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
                // 直接写系统字体族名。不要用 pack://application:,,,/Fonts/#QPet UI 那套复合字体：
                // 这个工程 EnableDefaultCompileItems=false、没有任何 Page 项，WPF 不会生成 .g.resources，
                // 复合字体根本没进 exe —— FontFamily 静默回落到默认字体，改映射表毫无效果（2026-11-19 查实）。
                // 图标不受影响：MDL2 的字形都在各自的 Style/代码里显式指定了 FontFamily。
                var ff = new FontFamily("Microsoft YaHei");
                int n = 0;
                foreach (Typeface tf in ff.GetTypefaces()) n++;
                if (n > 0)
                {
                    Win.FontFamily = ff;
                    UiFace = ff;                    // 弹窗是独立 Window，拿不到主窗的继承值
                    _uiFontInfo = "Microsoft YaHei ×" + n + "（汉字与拉丁同一套）";
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
                // 八页图标块：比选中行浅底再实一档，才看得出一块「底」而不是一点「色」。
                // 悬停时压深，配合卡片上浮，鼠标扫过去有回应但不闪。
                FadeRes("IconTile", Tint(c, 0.84), ms);
                FadeRes("IconTileHover", Tint(c, 0.74), ms);
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
                // 色板格子的选中环是 AccentInk 的实例（建的时候抓的），换肤后得重涂；
                // 那两墙格子只在气泡页可见，但驻留在树上，不重涂会一直停在旧主题色上。
                if (BubbleColorHost != null && BubbleColorHost.Children.Count > 0) BuildBubbleColorTiles();
                if (BubbleInkHost != null && BubbleInkHost.Children.Count > 0) BuildBubbleInkTiles();
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

        // 按圆角裁掉子元素：Border.CornerRadius 只画边框，不裁里面的图片/画布，
        // 图像一旦贴边就会把四个圆角糊成方角（导入卡、统计页方格图都踩过）。
        internal static void ClipRounded(FrameworkElement el, double r)
        {
            if (el == null) return;
            var g = new RectangleGeometry(new Rect(0, 0, 1, 1), r, r);
            el.Clip = g;
            el.SizeChanged += (s2, e2) =>
            {
                if (e2.NewSize.Width > 0 && e2.NewSize.Height > 0)
                    g.Rect = new Rect(0, 0, e2.NewSize.Width, e2.NewSize.Height);
            };
        }

        static TextBlock Txt(string text, double size, string hex, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, Foreground = C(hex), TextWrapping = TextWrapping.Wrap };
            if (bold) t.FontWeight = FontWeights.Bold;   // 卡片标题是粗体，不是半粗：和页面标题同一族
            return t;
        }

        // 卡片：白底 + 1px 细边 + 12px 圆角，和首页 deck 上那八张卡是同一张皮。
        // 旧版是「透明底 + 只画一根下划线」的分节纸样式：统计 / 动画 / 关于 / 导入四页都靠它，
        // 所以那四页看起来像上一个版本。改这里等于四页一起换皮，页面代码不用动。
        static Border Card(UIElement child)
        {
            var b = new Border
            {
                Background = C("#FFFFFF"), BorderBrush = C("#E6E8ED"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            b.Child = child;
            return b;
        }

        // 页面头：小眉标（// ENGLISH）+ 22px 粗体标题 + 一句说明。
        // 动态生成的页面（动画 / 统计 / 关于）用它；静态页在 XAML 里写的是同一套字号。
        // 旧的 34px 大标题 + 标题下 2px 黑粗线已删：那根线是这一版最像「旧皮肤」的东西，
        // 而且和首页卡片墙的 12px 圆角语言完全不搭 —— 现在靠字重和留白分层，不靠画线。
        static void AddPageHead(Panel host, string eyebrow, string title, string sub, string tip = null)
        {
            if (host == null) return;
            var eye = new TextBlock
            {
                Text = eyebrow, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = C("#9AA1AC"), Margin = new Thickness(1, 0, 0, 6)
            };
            host.Children.Add(eye);
            host.Children.Add(new TextBlock
            {
                Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = C("#1F2430")
            });
            var s = new TextBlock
            {
                Text = sub, FontSize = 12, Foreground = C("#995A5F68"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 18)
            };
            if (!string.IsNullOrEmpty(tip)) s.ToolTip = tip;
            host.Children.Add(s);
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
