using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static partial class Settings
{
        // ==================== 气泡外框 / 配色 / 文字色 ====================
        static Color _bubAccent = Color.FromRgb(0x8F, 0xA6, 0xD8);   // 气泡主色（空 = 默认蓝）

        static byte Clamp255(double v) { return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v)); }

        static Color WpfLighten(Color c, double t) { return Color.FromRgb(Clamp255(c.R + (255 - c.R) * t), Clamp255(c.G + (255 - c.G) * t), Clamp255(c.B + (255 - c.B) * t)); }

        static Color WpfDarken(Color c, double t) { return Color.FromRgb(Clamp255(c.R * (1 - t)), Clamp255(c.G * (1 - t)), Clamp255(c.B * (1 - t))); }

        static Color WpfInkFor(Color accent)
        {
            double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
            return lum > 0.62 ? WpfDarken(accent, 0.72) : WpfLighten(accent, 0.90);
        }

        static Brush Br(byte a, byte r, byte g, byte b) { return new SolidColorBrush(Color.FromArgb(a, r, g, b)); }

        static Brush Br(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }

        static Pen Pn(Brush b, double w) { return new Pen(b, w); }

        // 只重建主题色块墙：换色后把选中框挪到新色上。
        // 走 BeginInvoke 是因为这段代码常在「色块自己的点击处理里」被调到，
        // 当场把父面板清空会把正在冒泡的那个元素从树上摘掉。
        static void RefreshThemeTiles()
        {
            if (ThemeColorHost == null) return;
            Action act = () => BuildColorTiles(ThemeColorHost, _themeColor, _themePicker, HexOf(_themeColor), _themeColor);
            if (Win != null && Win.Dispatcher != null) Win.Dispatcher.BeginInvoke(act);
            else act();
        }

        static void BuildColorTiles(WrapPanel host, Color cur, Action<Color> onPick, string checkHex, Color? selColor = null)
        {
            if (host == null) return;
            host.Children.Clear();
            foreach (string[] p in ColorPresets)
            {
                Color c = WpfParseHex(p[0], cur);
                host.Children.Add(MakeColorTile(c, p[1], string.Equals(p[0], checkHex, StringComparison.OrdinalIgnoreCase), onPick, selColor));
            }
        }

        static Border MakeColorTile(Color c, string name, bool selected, Action<Color> onPick, Color? selColor = null)
        {
            // 选中态：墨色描边 + 中性浅底 + 墨字。选框是控件本体，不跟主题色走
            //（主题色只出现在色块自己身上、左栏读数、导航当前项）
            var selBr = C("#212529");
            var b = new Border
            {
                Width = 62, Height = 52, Margin = new Thickness(0, 0, 8, 8),
                Background = selected ? C("#ECF0F4") : Brushes.White,
                BorderBrush = selected ? selBr : C("#E1E5EB"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(0), Cursor = Cursors.Hand,
                ToolTip = name + " #" + HexOf(c)
            };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center,
                BorderBrush = C("#00000022"), BorderThickness = new Thickness(1)
            });
            sp.Children.Add(new TextBlock
            {
                Text = name, FontSize = 10.5,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = selected ? selBr : C("#7A8290"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
            });
            b.Child = sp;
            b.MouseLeftButtonUp += (s, e) => { onPick(c); e.Handled = true; };
            b.MouseEnter += (s, e) => { if (!selected) b.Background = C("#F3F5F8"); };
            b.MouseLeave += (s, e) => { if (!selected) b.Background = Brushes.White; };
            return b;
        }

        static void BuildBubbleColorTiles()
        {
            BuildColorTiles(BubbleColorHost, _bubbleColorSel, c =>
            {
                _bubbleColorSel = c;
                _bubAccent = c;
                BuildBubbleColorTiles();
                BuildBubbleStyleTiles();
                DrawBubblePreview();
            }, HexOf(_bubbleColorSel));
        }

        static void BuildBubbleInkTiles()
        {
            if (BubbleInkHost == null) return;
            BubbleInkHost.Children.Clear();
            string cur = string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : _bubbleInkSel.ToUpperInvariant();
            foreach (string[] p in InkPresets)
            {
                bool sel = string.Equals(p[0], cur, StringComparison.OrdinalIgnoreCase);
                bool isAuto = p[0] == "auto";
                var b = new Border
                {
                    Width = 62, Height = 52, Margin = new Thickness(0, 0, 8, 8),
                    Background = sel ? C("#ECF0F4") : Brushes.White,
                    BorderBrush = sel ? C("#212529") : C("#E1E5EB"),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(0), Cursor = Cursors.Hand,
                    ToolTip = isAuto ? "按气泡底色自动选深浅" : "文字颜色 #" + p[0]
                };
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var sw = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BorderBrush = C("#00000022"), BorderThickness = new Thickness(1),
                    Background = isAuto
                        ? (Brush)new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x33, 0x33, 0x33), 90)
                        : new SolidColorBrush(WpfParseHex(p[0], Color.FromRgb(0x1A, 0x1A, 0x1A)))
                };
                sp.Children.Add(sw);
                sp.Children.Add(new TextBlock
                {
                    Text = p[1], FontSize = 11,
                    FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = sel ? C("#212529") : C("#7A8290"),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
                });
                b.Child = sp;
                string key = p[0];
                b.MouseLeftButtonUp += (s, e) =>
                {
                    _bubbleInkSel = key;
                    BuildBubbleInkTiles();
                    DrawBubblePreview();
                    e.Handled = true;
                };
                b.MouseEnter += (s, e) => { if (!sel) b.Background = C("#F3F5F8"); };
                b.MouseLeave += (s, e) => { if (!sel) b.Background = Brushes.White; };
                BubbleInkHost.Children.Add(b);
            }
        }

        // ---- 自定义外框文件 ----
        static List<string> ListBubbleFrames()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(BubbleDir))
                    foreach (string f in Directory.GetFiles(BubbleDir))
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp") list.Add(f);
                    }
            }
            catch (Exception ex) { LogErr("?", ex); }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { LogErr("?", ex); return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        static string RelBubbleFrame(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try
            {
                string full = Path.GetFullPath(p), here = Path.GetFullPath(Here);
                if (full.StartsWith(here, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(here.Length).TrimStart('\\', '/');
            }
            catch (Exception ex) { LogErr("?", ex); }
            return p;
        }

        static readonly Dictionary<string, BitmapImage> _frameThumbCache = new Dictionary<string, BitmapImage>();

        static BitmapSource LoadBubbleFrameImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                BitmapImage cached;
                if (_frameThumbCache.TryGetValue(path, out cached)) return cached;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                _frameThumbCache[path] = bi;
                return bi;
            }
            catch (Exception ex) { LogErr("?", ex); return null; }
        }

        static readonly Dictionary<string, int> _frameAlphaCache = new Dictionary<string, int>();

        static int CenterAlpha(BitmapSource img)
        {
            string key = img.ToString();
            int cached;
            if (_frameAlphaCache.TryGetValue(key, out cached)) return cached;
            int res = 255;
            try
            {
                int W = img.PixelWidth, H = img.PixelHeight, stride = (W * 4 + 3) & ~3;
                byte[] buf = new byte[Math.Max(4, stride * H)];
                img.CopyPixels(buf, stride, 0);
                double a = 0; int n = 0;
                for (int y = H * 3 / 10; y < H * 7 / 10; y += 2)
                    for (int x = W * 3 / 10; x < W * 7 / 10; x += 2)
                    {
                        int o = y * stride + x * 4;
                        if (o + 3 >= buf.Length) break;
                        a += buf[o + 3] / 255.0; n++;
                    }
                if (n > 0) res = (int)Math.Round(a / n * 255.0);
            }
            catch (Exception ex) { LogErr("?", ex); }
            _frameAlphaCache[key] = res;
            return res;
        }

        static readonly Dictionary<string, Color> _frameInkCache = new Dictionary<string, Color>();

        static Brush BubbleInk(BitmapSource img)
        {
            string key = img.ToString();
            Color cached;
            if (_frameInkCache.TryGetValue(key, out cached)) return new SolidColorBrush(cached);
            double lum = 255;
            try
            {
                int W = img.PixelWidth, H = img.PixelHeight, stride = (W * 4 + 3) & ~3;
                byte[] buf = new byte[Math.Max(4, stride * H)];
                img.CopyPixels(buf, stride, 0);
                double l = 0, a = 0;
                for (int y = H * 3 / 10; y < H * 7 / 10; y += 2)
                    for (int x = W * 3 / 10; x < W * 7 / 10; x += 2)
                    {
                        int o = y * stride + x * 4;
                        if (o + 3 >= buf.Length) break;
                        double al = buf[o + 3] / 255.0;
                        l += (0.299 * buf[o + 2] + 0.587 * buf[o + 1] + 0.114 * buf[o]) * al;   // BGRA
                        a += al;
                    }
                if (a > 0.5) lum = l / a;
            }
            catch (Exception ex) { LogErr("?", ex); }
            Color ink = lum > 132 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF7, 0xF9, 0xFF);
            _frameInkCache[key] = ink;
            return new SolidColorBrush(ink);
        }

        // 九宫格：相邻块共用整数边界，不会有重叠发暗或缝隙亮线
        static void DrawNineSliceWpf(DrawingContext dc, BitmapSource img, Rect dst, double insX, double insY)
        {
            int W = img.PixelWidth, H = img.PixelHeight;
            double scale = Math.Min(dst.Width / W, dst.Height / H);
            int cw = Math.Max(1, (int)Math.Round(Math.Min(insX * W * scale, dst.Width / 2.0 - 1)));
            int ch = Math.Max(1, (int)Math.Round(Math.Min(insY * H * scale, dst.Height / 2.0 - 1)));
            int x0 = (int)Math.Round(dst.Left), y0 = (int)Math.Round(dst.Top);
            int w = Math.Max(2, (int)Math.Round(dst.Width)), h = Math.Max(2, (int)Math.Round(dst.Height));
            int scw = Math.Max(1, (int)Math.Round(insX * W)), sch = Math.Max(1, (int)Math.Round(insY * H));
            int[] dxs = { x0, x0 + cw, x0 + w - cw, x0 + w };
            int[] dys = { y0, y0 + ch, y0 + h - ch, y0 + h };
            int[] sxs = { 0, scw, W - scw, W };
            int[] sys = { 0, sch, H - sch, H };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    int dw = dxs[i + 1] - dxs[i], dh = dys[j + 1] - dys[j];
                    int srw = sxs[i + 1] - sxs[i], srh = sys[j + 1] - sys[j];
                    if (dw <= 0 || dh <= 0 || srw <= 0 || srh <= 0) continue;
                    var crop = new CroppedBitmap(img, new Int32Rect(sxs[i], sys[j], srw, srh));
                    crop.Freeze();
                    dc.DrawImage(crop, new Rect(dxs[i], dys[j], dw, dh));
                }
        }

        static void BubbleLayout(double w, double h, out double l, out double t, out double r, out double b,
                                 out double rad, out double sw, out double tipX, out double tipY,
                                 out double tailL, out double tailR)
        {
            sw = Math.Max(1.4, w * 4.6 / 1026.0 * 1.6);
            double tailH = Math.Min(Math.Max(7, h * 0.17), 16);
            l = sw + 1.5; r = w - sw - 1.5; t = sw + 1.5; b = h - tailH - sw - 1.5;
            rad = Math.Max(6, Math.Min(15, (b - t) * 0.32));
            double cx = (l + r) / 2.0;
            tipY = b + tailH;
            tailL = cx - Math.Max(9, w * 0.05);
            tailR = cx + Math.Max(4, w * 0.022);
            tipX = cx - Math.Max(2, w * 0.014);
        }

        static Geometry BubbleGeo(double l, double t, double r, double b, double rad,
                                  double tailL, double tailR, double tipX, double tipY)
        {
            return BubbleGeometry(l, t, r, b, Math.Min(rad, Math.Min((r - l) / 2.0, (b - t) / 2.0)), tailL, tailR, tipX, tipY);
        }

        static void DrawCardWpf(DrawingContext dc, double l, double t, double r, double b, double rad,
                                double tailL, double tailR, double tipX, double tipY, double sw)
        {
            Geometry geo = BubbleGeo(l, t, r, b, rad, tailL, tailR, tipX, tipY);
            Brush sh1 = Br(24, 0x2A, 0x3A, 0x66);
            Brush sh2 = Br(15, 0x2A, 0x3A, 0x66);
            Brush fill = new LinearGradientBrush(Colors.White, WpfLighten(_bubAccent, 0.78), 90);
            Pen pen = Pn(Br(_bubAccent.R, _bubAccent.G, _bubAccent.B), sw);
            Pen hl = Pn(Br(130, 0xFF, 0xFF, 0xFF), Math.Max(1.0, sw * 0.7));
            dc.PushTransform(new TranslateTransform(0, 2.5)); dc.DrawGeometry(sh1, null, geo); dc.Pop();
            dc.PushTransform(new TranslateTransform(0, 5)); dc.DrawGeometry(sh2, null, geo); dc.Pop();
            dc.DrawGeometry(fill, pen, geo);
            double ins = rad + 4;
            if (r - l > ins * 2) dc.DrawLine(hl, new Point(l + ins, t + sw * 1.2), new Point(r - ins, t + sw * 1.2));
        }

        static void DrawClassicWpf(DrawingContext dc, double l, double t, double r, double b,
                                   double w, double h, out Rect textRect)
        {
            double rx = (r - l) / 2.0, ry = (b - t) / 2.0;
            double cx = (l + r) / 2.0, cy = (t + b) / 2.0;
            double r1 = Math.Max(3, w * 0.048), x1 = cx - rx * 0.74, y1 = h - r1 * 1.25;
            double r2 = Math.Max(2, w * 0.031), x2 = cx - rx * 0.33, y2 = h - r2 * 0.85;
            Color lineC = WpfDarken(_bubAccent, 0.30);
            Color shC = WpfDarken(_bubAccent, 0.45);
            Brush stroke = Br(lineC.R, lineC.G, lineC.B);
            Brush sh = Br(22, shC.R, shC.G, shC.B);
            Pen pen = Pn(stroke, Math.Max(1.8, w * 0.012));
            dc.DrawEllipse(sh, null, new Point(cx, cy + 3), rx, ry);
            dc.DrawEllipse(sh, null, new Point(x1, y1 + 2), r1, r1 * 0.7);
            dc.DrawEllipse(sh, null, new Point(x2, y2 + 2), r2, r2 * 0.7);
            dc.DrawEllipse(Brushes.White, pen, new Point(cx, cy), rx, ry);
            dc.DrawEllipse(Brushes.White, pen, new Point(x1, y1), r1, r1 * 0.7);
            dc.DrawEllipse(Brushes.White, pen, new Point(x2, y2), r2, r2 * 0.7);
            textRect = new Rect(cx - rx * 0.80, cy - ry * 0.74, rx * 1.60, ry * 1.48);
        }

        static void DrawComicWpf(DrawingContext dc, double l, double t, double r, double b,
                                 double tailL, double tailR, double tipX, double tipY, double sw)
        {
            Geometry geo = BubbleGeo(l, t, r, b, 4, tailL, tailR, tipX, tipY);
            Color cSh = WpfDarken(_bubAccent, 0.62);
            Color cLn = WpfDarken(_bubAccent, 0.55);
            Brush sh = Br(46, cSh.R, cSh.G, cSh.B);
            Brush line = Br(cLn.R, cLn.G, cLn.B);
            dc.PushTransform(new TranslateTransform(4.5, 4.5));
            dc.DrawGeometry(sh, null, geo);
            dc.Pop();
            dc.DrawGeometry(Brushes.White, Pn(line, Math.Max(2.2, sw * 1.4)), geo);
        }

        static void DrawGlassWpf(DrawingContext dc, double l, double t, double r, double b, double rad,
                                 double tailL, double tailR, double tipX, double tipY, double sw)
        {
            Geometry geo = BubbleGeo(l, t, r, b, rad, tailL, tailR, tipX, tipY);
            Brush sh = Br(20, 0x2A, 0x3A, 0x66);
            Color gEnd = WpfLighten(_bubAccent, 0.55);
            Brush fill = new LinearGradientBrush(Color.FromArgb(236, 0xFF, 0xFF, 0xFF),
                                                  Color.FromArgb(196, gEnd.R, gEnd.G, gEnd.B), 90);
            Pen pen = Pn(Br(238, 0xFF, 0xFF, 0xFF), Math.Max(1.6, sw * 1.2));
            Pen hl = Pn(Br(150, 0xFF, 0xFF, 0xFF), Math.Max(1.0, sw * 0.8));
            dc.PushTransform(new TranslateTransform(0, 3)); dc.DrawGeometry(sh, null, geo); dc.Pop();
            dc.PushTransform(new TranslateTransform(0, 6)); dc.DrawGeometry(sh, null, geo); dc.Pop();
            dc.PushTransform(new TranslateTransform(0, 9)); dc.DrawGeometry(sh, null, geo); dc.Pop();
            dc.DrawGeometry(fill, pen, geo);
            double ins = rad + 5;
            if (r - l > ins * 2) dc.DrawLine(hl, new Point(l + ins, t + sw * 1.3), new Point(r - ins, t + sw * 1.3));
        }

        static void DrawCustomBubbleWpf(DrawingContext dc, string framePath, double w, double h,
                                        out Rect textRect, out Brush ink)
        {
            textRect = new Rect(w * 0.115, h * 0.13, Math.Max(16, w * 0.77), Math.Max(10, h * 0.60));
            ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            BitmapSource img = LoadBubbleFrameImage(framePath);
            if (img == null) return;
            DrawNineSliceWpf(dc, img, new Rect(0, 0, w, h), 0.26, 0.30);
            ink = BubbleInk(img);
            if (CenterAlpha(img) < 110)
            {
                Rect back = new Rect(textRect.Left - 3, textRect.Top - 3, textRect.Width + 6, textRect.Height + 6);
                dc.DrawRoundedRectangle(Br(150, 0xFF, 0xFF, 0xFF), null, back, 7, 7);
            }
        }

        // 在给定矩形里画气泡外框；textRect 返回文字该放的位置
        static void DrawBubbleWpf(DrawingContext dc, string style, string framePath,
                                  double w, double h, out Rect textRect, out Brush ink)
        {
            double l, t, r, b, rad, sw, tipX, tipY, tailL, tailR;
            BubbleLayout(w, h, out l, out t, out r, out b, out rad, out sw, out tipX, out tipY, out tailL, out tailR);
            double bw = r - l, bh = b - t;
            double pad = Math.Max(6, Math.Min(w, h) * 0.10);
            textRect = new Rect(l + pad, t + pad * 0.85, Math.Max(16, bw - pad * 2), Math.Max(12, bh - pad * 1.7));
            ink = new SolidColorBrush(WpfInkFor(_bubAccent));

            if (style == "classic") { DrawClassicWpf(dc, l, t, r, b, w, h, out textRect); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.55)); return; }
            if (style == "comic") { DrawComicWpf(dc, l, t, r, b, tailL, tailR, tipX, tipY, sw); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.78)); return; }
            if (style == "glass") { DrawGlassWpf(dc, l, t, r, b, rad, tailL, tailR, tipX, tipY, sw); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.72)); return; }
            if (style == "custom") { DrawCustomBubbleWpf(dc, framePath, w, h, out textRect, out ink); return; }
            DrawCardWpf(dc, l, t, r, b, rad, tailL, tailR, tipX, tipY, sw);
        }

        static BitmapSource RenderBubble(string style, string framePath, int w, int h, string text, double fontPt)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                Rect tr;
                Brush ink;
                DrawBubbleWpf(dc, style, framePath, w, h, out tr, out ink);
                if (!string.IsNullOrEmpty(text) && tr.Width > 14 && tr.Height > 8)
                {
                    string fn = CmbFont != null && CmbFont.SelectedIndex >= 0
                        ? CmbFont.Items[CmbFont.SelectedIndex].ToString() : CurFont;
                    Typeface tf = new Typeface(new FontFamily(fn), FontStyles.Normal,
                        FontWeights.Normal, FontStretches.Normal);
                    FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, tf, fontPt, ink, 1.0);
                    ft.MaxTextWidth = Math.Max(20, tr.Width);
                    ft.MaxTextHeight = Math.Max(8, tr.Height);
                    ft.TextAlignment = TextAlignment.Center;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(ft, new Point(tr.Left, tr.Top + Math.Max(0, (tr.Height - ft.Height) / 2.0)));
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // 读当前角色的某一帧（气泡预览里垫一个"宠物本体"，用来看高度对比）
        static BitmapSource PetFrameShot(int frameIndex)
        {
            try
            {
                CharEntry ce = SelectedChar;
                if (ce == null || !ce.HasFrames) return null;
                byte[] data = File.ReadAllBytes(ce.Frames);
                int fw = 0, fh = 0;
                if (File.Exists(ce.Meta))
                    foreach (string raw in File.ReadAllLines(ce.Meta))
                    {
                        int eq = raw.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                        if (k == "w") int.TryParse(v, out fw);
                        else if (k == "h") int.TryParse(v, out fh);
                    }
                if (fw <= 0 || fh <= 0) return null;
                int fb = fw * fh * 4;
                int off = fb * Math.Max(0, frameIndex);
                if (off + fb > data.Length) off = 0;
                if (off + fb > data.Length) return null;
                System.Runtime.InteropServices.GCHandle gh =
                    System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    // stride 是一行字节数（fw*4），不是整帧大小
                    IntPtr p = new IntPtr(gh.AddrOfPinnedObject().ToInt64() + off);
                    BitmapSource bs = BitmapSource.Create(fw, fh, 96, 96, PixelFormats.Pbgra32, null, p, fb, fw * 4);
                    bs.Freeze();
                    return bs;
                }
                finally { gh.Free(); }
            }
            catch (Exception ex) { LogErr("petshot", ex); return null; }
        }

        // 外框卡片：缩略图 + 名称，选中项是墨色描边（同主题色色块，选框不跟主题色）
        static Border StyleTile(string key, string title, string tip, string framePath)
        {
            bool sel = _bubbleStyleSel == key && (key != "custom"
                || (!string.IsNullOrEmpty(framePath) && PathsEqual(framePath, _bubbleFrameSel)));
            // 同理：粗细固定 2px，只换颜色，避免选中瞬间缩略图位移
            var b = new Border
            {
                Width = 168, Margin = new Thickness(0, 0, 10, 10),
                Background = sel ? C("#ECF0F4") : Brushes.White,
                BorderBrush = sel ? C("#212529") : C("#E1E5EB"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                ToolTip = tip
            };
            var sp = new StackPanel();
            sp.Children.Add(new Image
            {
                Source = RenderBubble(key, framePath, 150, 58, "你好呀", 11),
                Width = 150, Height = 58, Stretch = Stretch.Uniform
            });
            sp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12,
                FontWeight = sel ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = sel ? C("#212529") : C("#3A3F49"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            });
            b.Child = sp;
            b.MouseLeftButtonUp += (s, e) =>
            {
                _bubbleStyleSel = key;
                _bubbleFrameSel = framePath;
                BuildBubbleStyleTiles();
                DrawBubblePreview();
                e.Handled = true;
            };
            b.MouseEnter += (s, e) => { if (!sel) { b.Background = C("#F3F5F8"); b.BorderBrush = C("#C9CED6"); } };
            b.MouseLeave += (s, e) => { if (!sel) { b.Background = Brushes.White; b.BorderBrush = C("#E1E5EB"); } };
            if (key == "custom" && !string.IsNullOrEmpty(framePath))
            {
                var del = new MenuItem { Header = "删除这个外框" };
                del.Click += (s, e) => DeleteBubbleFrame(framePath);
                b.ContextMenu = new ContextMenu();
                b.ContextMenu.Items.Add(del);
            }
            return b;
        }

        static void BuildBubbleStyleTiles()
        {
            if (BubbleStyleHost == null) return;
            BubbleStyleHost.Children.Clear();
            for (int i = 0; i < BubbleStyleDefs.Length; i++)
                BubbleStyleHost.Children.Add(StyleTile(BubbleStyleDefs[i][0], BubbleStyleDefs[i][1], BubbleStyleDefs[i][2], ""));
            foreach (string fp in ListBubbleFrames())
                BubbleStyleHost.Children.Add(StyleTile("custom", Path.GetFileNameWithoutExtension(fp),
                    "自定义外框（右键删除）", fp));
            var up = new Border
            {
                Width = 168, Height = 78, Margin = new Thickness(0, 0, 10, 10),
                Background = C("#FDFEFF"), BorderBrush = C("#D8DDE5"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0), Cursor = Cursors.Hand, ToolTip = "上传自己的气泡外框图片"
            };
            var ut = new TextBlock
            {
                Text = "+ 上传外框", FontSize = 12, Foreground = C("#6B7280"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            up.Child = ut;
            up.MouseLeftButtonUp += (s, e) => { UploadBubbleFrame(); e.Handled = true; };
            up.MouseEnter += (s, e) => { up.Background = C("#F4F8FF"); up.BorderBrush = C("#9CC2F0"); };
            up.MouseLeave += (s, e) => { up.Background = C("#FDFEFF"); up.BorderBrush = C("#D8DDE5"); };
            BubbleStyleHost.Children.Add(up);
        }

        static void UploadBubbleFrame()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*",
                Title = "选择气泡外框图片（透明 PNG 最佳）"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Directory.CreateDirectory(BubbleDir);
                string baseName = SanitizeName(Path.GetFileNameWithoutExtension(dlg.FileName));
                if (string.IsNullOrEmpty(baseName)) baseName = "外框";
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                string dst = Path.Combine(BubbleDir, baseName + ext);
                int n = 2;
                while (File.Exists(dst) && !PathsEqual(dst, dlg.FileName))
                {
                    dst = Path.Combine(BubbleDir, baseName + " (" + n + ")" + ext);
                    n++;
                }
                if (!PathsEqual(dst, dlg.FileName)) File.Copy(dlg.FileName, dst, true);
                _bubbleStyleSel = "custom";
                _bubbleFrameSel = dst;
                BuildBubbleStyleTiles();
                DrawBubblePreview();
                LblBubHint.Text = "已添加外框：" + Path.GetFileName(dst) + "。点「保存更改」重启桌宠后生效。";
            }
            catch (Exception ex) { LogErr("upbubble", ex); Dlg.Info("上传失败", ex.Message, "好"); }
        }

        static void DeleteBubbleFrame(string path)
        {
            if (!Dlg.Ask("删除这个外框？",
                    Path.GetFileName(path) + " 会从 bubble-frames 里删掉，且无法恢复。",
                    "删除", "算了", true)) return;
            try
            {
                File.Delete(path);
                if (PathsEqual(path, _bubbleFrameSel)) { _bubbleStyleSel = "card"; _bubbleFrameSel = ""; }
                BuildBubbleStyleTiles();
                DrawBubblePreview();
                LblBubHint.Text = "外框已删除。";
            }
            catch (Exception ex) { LogErr("delbubble", ex); }
        }

        // 下拉框一点就展开。默认行为里点在文字区只把焦点给它、要再点一次（或点右侧箭头），
        // 这里统一接管：点框内任意位置都展开，展开时再点是收起。
        static void HookComboOneClick(object root)
        {
            var dep = root as DependencyObject;
            if (dep == null) return;
            var cb = root as ComboBox;
            if (cb != null)
            {
                cb.PreviewMouseLeftButtonDown -= ComboOneClick;
                cb.PreviewMouseLeftButtonDown += ComboOneClick;
            }
            foreach (object child in LogicalTreeHelper.GetChildren(dep))
                if (child is DependencyObject) HookComboOneClick(child);
        }

        static bool ComboToggle(ComboBox cb)
        {
            if (cb == null || cb.IsEditable || !cb.IsEnabled) return false;
            bool open = !cb.IsDropDownOpen;
            cb.IsDropDownOpen = open;
            if (open) cb.Focus();
            return open;
        }

        static void ComboOneClick(object sender, MouseButtonEventArgs e)
        {
            var cb = sender as ComboBox;
            if (cb == null || cb.IsEditable || !cb.IsEnabled) return;   // 可编辑的下拉交给文本框
            // 关键：下拉列表在独立 Popup 里，但预览事件是沿逻辑树隧道的，所以点选项时这里也会被调到。
            // 之前一律 Toggle + Handled=true → 列表刚展开就被关掉、选中项也永远收不到点击（「选了不生效」）。
            // 只接管「点在框身上」的那次点击，来自列表内部的点击原样放行。
            if (!ShouldToggleCombo(cb, e.OriginalSource as DependencyObject)) return;
            ComboToggle(cb);
            e.Handled = true;        // 吃掉这次点击，否则右侧 ToggleButton 会反手把它关掉
        }

        // 这次点击该不该由我们接管：只有点在框身上才接管；点在弹出列表里的项上必须放行，
        // 否则列表会被翻成收起、选中项也收不到点击（「选了不生效」就是这么来的）。
        static bool ShouldToggleCombo(ComboBox cb, DependencyObject src)
        {
            return src == null || IsInsideVisual(src, cb);
        }

        // node 是否位于 root 的视觉子树里（Popup 内容是独立视觉树，会返回 false）
        static bool IsInsideVisual(DependencyObject node, DependencyObject root)
        {
            while (node != null)
            {
                if (node == root) return true;
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

}
