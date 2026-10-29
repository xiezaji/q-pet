// ============================================================================
// Q Pet · 精灵帧生成（纯 C# / WPF 成像，不依赖 Python + Pillow）
// ----------------------------------------------------------------------------
// 来历：这段逻辑原来是 sprites.py + anim_custom.py（Pillow + numpy）。问题是
// 「新建角色 / 改大小 / 改幅度 / 换自定义动画」全都得先装 Python 和 Pillow，
// 而这三件事都是设置面板上的普通按钮 —— 普通用户点了只会看到「生成失败」。
// 现在搬到进程内：WPF 自带 WIC 解码 + 高质量缩放，同样的数学，同样的输出格式。
//
// 输出约定（与 sprites.py 完全一致，Pet.exe 不用改）：
//   frames.bin  裸 BGRA、**预乘 alpha**（UpdateLayeredWindow 要求）、按 待机→按键→点击→拖动 拼接
//   frames.txt  w/h/idle/press/click/drag/fps
//   frames.json 同上，供网页版读
//   preview.png 四态拼图（可选）
//
// 与 Python 版的已知差异（都只在亚像素级）：
//   · 重采样滤镜 Lanczos → WIC Fant；预乘四舍五入 (v*a+127)/255 → WIC 的 (v*a)/255
//   · 旋转用 WPF 光栅器（双线性），PIL 用 BICUBIC —— 拖动帧角度只有 ±5°，肉眼无差
// 校验方式见 —_ref 里的对比脚本说明：对小维（charH=200）重出一次应与原 frames.bin
// 逐像素平均误差 < 1/255。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class QSprite
{
    // ---------------- 常量：与 sprites.py 一一对应 ----------------
    public const int IdleN = 24, PressN = 6, ClickN = 6, DragN = 8, Fps = 30;
    const int BottomPad = 6;                       // 脚底留边，防最后一行被窗口边界切掉
    const double CwK = 320.0 / 300.0;              // 画布宽 = charH * 320/300
    const double ChK = 324.0 / 300.0;              // 画布高 = charH * 324/300
    const double DragLift = 14, DragShrink = 0.95;

    static readonly double[,] PressSeq = { { 1.00, 1.00 }, { 1.06, 0.93 }, { 1.10, 0.86 }, { 1.08, 0.85 }, { 1.03, 0.95 }, { 1.00, 1.00 } };
    static readonly double[,] ClickSeq = { { 1.00, 1.00 }, { 1.09, 0.88 }, { 1.15, 0.81 }, { 1.11, 0.84 }, { 1.03, 0.96 }, { 1.00, 1.00 } };
    static readonly double[] ClickDy = { 0, -2, -5, -4, -1, 0 };
    static readonly double[] DragAngle = { -5, -3, 0, 3, 5, 3, 0, -3 };
    static readonly double[] DragDx = { 6, 3, 0, -3, -6, -3, 0, 3 };

    public static readonly string[] Slots = { "idle", "press", "click", "drag" };
    static readonly int[] MinFrames = { 2, 4, 4, 1 };   // 帧数不够就重复最后一帧，保证状态机能回弹
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    // ---------------- 内存里的一个角色包 ----------------
    public class Pack
    {
        public int W, H;
        public readonly List<byte[]> Frames = new List<byte[]>();
        public readonly int[] Counts = new int[4];       // idle / press / click / drag
        public int Total { get { return Frames.Count; } }

        public List<byte[]> Segment(int idx)
        {
            int start = 0;
            for (int i = 0; i < idx; i++) start += Counts[i];
            return Frames.GetRange(start, Counts[idx]);
        }
    }

    // ========================================================================
    // 入口 1：按立绘生成整套帧（原 sprites.py build）
    // ========================================================================
    public static Pack Generate(string srcPath, int charH, double ampIdle, double ampPress, double ampClick, double ampDrag)
    {
        if (!File.Exists(srcPath)) throw new Exception("找不到角色立绘：" + srcPath);
        charH = (int)QCore.ClampInt(charH, 24, 1200);
        double aIdle = Math.Max(0, ampIdle), aPress = Math.Max(0, ampPress);
        double aClick = Math.Max(0, ampClick), aDrag = Math.Max(0, ampDrag);

        var p = new Pack();
        p.W = (int)Math.Round(charH * CwK);
        p.H = (int)Math.Round(charH * ChK);
        if (p.W < 8 || p.H < 8) throw new Exception("角色尺寸太小：" + charH + "px");

        BitmapSource src = LoadImage(srcPath);
        // fit：等比缩到高度 charH，宽度按比例（PIL 的 fit）
        int cw = (int)Math.Round(src.PixelWidth * (double)charH / src.PixelHeight);
        BitmapSource ch = Scale(src, cw, charH);

        p.Counts[0] = IdleN; p.Counts[1] = PressN; p.Counts[2] = ClickN; p.Counts[3] = DragN;

        // ---- 待机：浮动 + 呼吸 ----
        for (int i = 0; i < IdleN; i++)
        {
            double t = 2 * Math.PI * i / IdleN;
            double sy = 1.0 + 0.010 * aIdle * (0.5 + 0.5 * Math.Sin(t));
            BitmapSource f = Scale(ch, (int)Math.Round(ch.PixelWidth * 1.0), (int)Math.Round(ch.PixelHeight * sy));
            p.Frames.Add(Pixels(Place(f, p.W, p.H, 0, 2.5 * aIdle * Math.Sin(t))));
        }
        // ---- 按键：挤压-拉伸，脚底锚定 ----
        for (int i = 0; i < PressN; i++)
        {
            double sx = 1.0 + (PressSeq[i, 0] - 1.0) * aPress;
            double sy = 1.0 + (PressSeq[i, 1] - 1.0) * aPress;
            p.Frames.Add(Pixels(Place(Squash(ch, sx, sy), p.W, p.H, 0, 0)));
        }
        // ---- 点击：压得更深 + 小跳 ----
        for (int i = 0; i < ClickN; i++)
        {
            double sx = 1.0 + (ClickSeq[i, 0] - 1.0) * aClick;
            double sy = 1.0 + (ClickSeq[i, 1] - 1.0) * aClick;
            p.Frames.Add(Pixels(Place(Squash(ch, sx, sy), p.W, p.H, 0, ClickDy[i] * aClick)));
        }
        // ---- 拖动：缩小 + 旋转 + 左右摆 + 整体提起（悬空感）----
        BitmapSource small = Scale(ch,
            (int)Math.Round(ch.PixelWidth * DragShrink), (int)Math.Round(ch.PixelHeight * DragShrink));
        for (int i = 0; i < DragN; i++)
        {
            BitmapSource rot = Rotate(small, DragAngle[i] * aDrag);
            p.Frames.Add(Pixels(Place(rot, p.W, p.H, DragDx[i] * aDrag, -DragLift * aDrag)));
        }
        return p;
    }

    // 写整套：frames.bin / frames.txt / frames.json（+ preview.png）到角色包的「动画」目录
    public static void WritePack(string animDir, Pack p, bool preview)
    {
        Directory.CreateDirectory(animDir);
        using (var fs = new FileStream(Path.Combine(animDir, "frames.bin"), FileMode.Create, FileAccess.Write))
            foreach (byte[] f in p.Frames) fs.Write(f, 0, f.Length);

        string meta = "w=" + p.W + "\r\nh=" + p.H
            + "\r\nidle=" + p.Counts[0] + "\r\npress=" + p.Counts[1]
            + "\r\nclick=" + p.Counts[2] + "\r\ndrag=" + p.Counts[3] + "\r\nfps=" + Fps + "\r\n";
        File.WriteAllText(Path.Combine(animDir, "frames.txt"), meta, new UTF8Encoding(false));

        string json = "{\r\n  \"w\": " + p.W + ",\r\n  \"h\": " + p.H
            + ",\r\n  \"idle\": " + p.Counts[0] + ",\r\n  \"press\": " + p.Counts[1]
            + ",\r\n  \"click\": " + p.Counts[2] + ",\r\n  \"drag\": " + p.Counts[3]
            + ",\r\n  \"fps\": " + Fps + ",\r\n  \"frameBytes\": " + ((long)p.W * p.H * 4) + "\r\n}\r\n";
        File.WriteAllText(Path.Combine(animDir, "frames.json"), json, new UTF8Encoding(false));

        if (preview)
        {
            int i0 = 0;
            int i1 = Math.Min(p.Total - 1, IdleN + 2);
            int i2 = Math.Min(p.Total - 1, IdleN + PressN + 2);
            int i3 = Math.Min(p.Total - 1, IdleN + PressN + ClickN + 4);
            SavePreview(Path.Combine(animDir, "preview.png"), p, new int[] { i0, i1, i2, i3 });
        }
    }

    // ========================================================================
    // 入口 2：给某一槽换自定义帧（原 anim_custom.py apply）
    // src 可以是 GIF / 单张图 / 一个装满图片的文件夹
    // ========================================================================
    public static void ApplyCustom(string packDir, string slot, string srcPath)
    {
        int idx = SlotIndex(slot);
        Pack p = ReadPack(packDir);                          // 现状（含各槽帧数）
        List<byte[]>[] segs = SplitSegments(packDir, p);
        List<BitmapSource> imgs = LoadFrames(srcPath);
        if (imgs.Count == 0) throw new Exception("这个文件里没有可用的画面");
        while (imgs.Count < MinFrames[idx]) imgs.Add(imgs[imgs.Count - 1]);

        var frames = new List<byte[]>();
        foreach (BitmapSource im in imgs) frames.Add(Pixels(FitFrame(im, p.W, p.H)));
        segs[idx] = frames;
        p.Counts[idx] = frames.Count;
        WriteSegments(packDir, p, segs, false);

        Dictionary<string, string> m = ReadManifest(packDir);
        m["custom" + Cap(slot)] = "1";
        WriteManifest(packDir, m);
    }

    // ========================================================================
    // 入口 3：某一槽恢复默认（原 anim_custom.py reset）
    // 用角色自己的立绘和幅度重算，只把这一槽切回来
    // ========================================================================
    public static void ResetSlot(string packDir, string slot)
    {
        int idx = SlotIndex(slot);
        Dictionary<string, string> m = ReadManifest(packDir);
        string img = FindPackImage(packDir);
        if (img == "") throw new Exception("角色包里找不到立绘（图片/image.png）");
        int charH;
        if (!int.TryParse(Str(m, "charH", "219"), out charH) || charH <= 0) charH = 219;

        Pack def = Generate(img, charH, Amp(m, "ampIdle"), Amp(m, "ampPress"), Amp(m, "ampClick"), Amp(m, "ampDrag"));

        Pack p = ReadPack(packDir);
        List<byte[]>[] segs = SplitSegments(packDir, p);
        List<byte[]> want = def.Segment(idx);
        if (def.W != p.W || def.H != p.H)
            throw new Exception("角色尺寸和现有动画不一致（" + def.W + "x" + def.H + " vs " + p.W + "x" + p.H
                + "），请先在「形象与大小」里保存一次重建动画");
        segs[idx] = want;
        p.Counts[idx] = want.Count;
        WriteSegments(packDir, p, segs, false);

        m.Remove("custom" + Cap(slot));
        WriteManifest(packDir, m);
    }

    // ========================================================================
    // 成像底层
    // ========================================================================
    static BitmapSource LoadImage(string path)
    {
        var bi = new BitmapImage();
        using (FileStream fs = File.OpenRead(path))
        {
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bi.StreamSource = fs;
            bi.EndInit();
        }
        bi.Freeze();
        return ToPbgra(bi);
    }

    static BitmapSource ToPbgra(BitmapSource s)
    {
        if (s.Format == PixelFormats.Pbgra32) return s;
        var fc = new FormatConvertedBitmap(s, PixelFormats.Pbgra32, null, 0);
        fc.Freeze();
        return fc;
    }

    // 等比缩放（WIC 的 Fant 重采样，比 PIL 的 LANCZOS 略软，肉眼无差）
    static BitmapSource Scale(BitmapSource src, int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        if (src.PixelWidth == w && src.PixelHeight == h) return src;
        var tb = new TransformedBitmap(src, new ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
        tb.Freeze();
        if (tb.PixelWidth == w && tb.PixelHeight == h) return tb;
        // 变换后的尺寸是向上取整的，可能差 1px：裁到目标尺寸，保证居中/锚定算得准
        var crop = new CroppedBitmap(ToPbgra(tb), new Int32Rect(0, 0, Math.Min(w, tb.PixelWidth), Math.Min(h, tb.PixelHeight)));
        crop.Freeze();
        return crop;
    }

    static BitmapSource Squash(BitmapSource src, double sx, double sy)
    {
        return Scale(src, (int)Math.Round(src.PixelWidth * sx), (int)Math.Round(src.PixelHeight * sy));
    }

    // 居中 + 底部锚定；dx/dy 额外偏移。画布尺寸不变 → 缩放与贴图彻底分开，不会裁到角色。
    static BitmapSource Place(BitmapSource img, int cw, int ch, double dx, double dy)
    {
        double x = (cw - img.PixelWidth) / 2 + Math.Round(dx);
        double y = ch - img.PixelHeight + Math.Round(dy) - BottomPad;
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = dv.RenderOpen())
            dc.DrawImage(img, new Rect(x, y, img.PixelWidth, img.PixelHeight));
        return Render(dv, cw, ch);
    }

    // 绕图心旋转、画布不放大 → 等价于 PIL 的 rotate(expand=False)：四角自然被裁掉
    static BitmapSource Rotate(BitmapSource img, double deg)
    {
        if (deg == 0) return img;
        int w = img.PixelWidth, h = img.PixelHeight;
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = dv.RenderOpen())
        {
            // PIL 的 rotate(angle) 逆时针为正，WPF 的 RotateTransform 顺时针为正 → 取反
            dc.PushTransform(new RotateTransform(-deg, w / 2.0, h / 2.0));
            dc.DrawImage(img, new Rect(0, 0, w, h));
            dc.Pop();
        }
        return Render(dv, w, h);
    }

    static BitmapSource Render(DrawingVisual dv, int w, int h)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // 取裸 BGRA（RTB 是 Pbgra32 → 已经是预乘 alpha，正好是 UpdateLayeredWindow 要的格式）
    static byte[] Pixels(BitmapSource b)
    {
        int stride = b.PixelWidth * 4;
        var buf = new byte[stride * b.PixelHeight];
        b.CopyPixels(buf, stride, 0);
        return buf;
    }

    // 自定义帧：等比装进画布、底部居中（和默认动画的脚底锚定一致）
    static BitmapSource FitFrame(BitmapSource im, int w, int h)
    {
        double k = Math.Min((double)w / im.PixelWidth, (double)h / im.PixelHeight);
        int nw = Math.Max(1, (int)Math.Round(im.PixelWidth * k));
        int nh = Math.Max(1, (int)Math.Round(im.PixelHeight * k));
        BitmapSource s = Scale(im, nw, nh);

        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = dv.RenderOpen())
            dc.DrawImage(s, new Rect((w - nw) / 2, h - nh, nw, nh));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // GIF / 文件夹 / 单张图 → 帧序列
    static List<BitmapSource> LoadFrames(string src)
    {
        var list = new List<BitmapSource>();
        if (Directory.Exists(src))
        {
            var files = new List<string>();
            foreach (string f in Directory.GetFiles(src))
                if (Array.IndexOf(ImageExt, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
            if (files.Count == 0) throw new Exception("文件夹里没有图片（png/jpg/webp/bmp）");
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files) list.Add(LoadImage(f));
            return list;
        }
        if (!File.Exists(src)) throw new Exception("找不到文件：" + src);
        if (Path.GetExtension(src).ToLowerInvariant() == ".gif")
        {
            try
            {
                using (FileStream fs = File.OpenRead(src))
                {
                    var dec = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    foreach (BitmapFrame fr in dec.Frames) list.Add(ToPbgra(fr));
                }
                if (list.Count > 0) return list;
            }
            catch (Exception) { list.Clear(); }   // 不是标准 GIF 就按单张图走
        }
        list.Add(LoadImage(src));
        return list;
    }

    static void SavePreview(string path, Pack p, int[] picks)
    {
        int gap = 8, n = picks.Length;
        int cw = p.W * n + gap * (n + 1), ch = p.H + 2 * gap;
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 24, 48)), null,
                new Rect(0, 0, cw, ch));                      // 深色底，浅色角色才看得清轮廓
            for (int i = 0; i < n; i++)
            {
                BitmapSource bmp = FrameBitmap(p.Frames[picks[i]], p.W, p.H);
                dc.DrawImage(bmp, new Rect(gap + i * (p.W + gap), gap, p.W, p.H));
            }
        }
        var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (FileStream fs = File.Create(path)) enc.Save(fs);
    }

    static BitmapSource FrameBitmap(byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bgra, w * 4);
        bmp.Freeze();
        return bmp;
    }

    // ========================================================================
    // 角色包读写（不依赖 Settings 里的那一套，保持本文件可单独测试）
    // ========================================================================
    public static Pack ReadPack(string packDir)
    {
        string anim = Path.Combine(packDir, "动画");
        string txt = Path.Combine(anim, "frames.txt"), bin = Path.Combine(anim, "frames.bin");
        if (!File.Exists(txt) || !File.Exists(bin)) throw new Exception("角色包缺动画文件（动画/frames.bin 或 frames.txt）");
        var p = new Pack();
        foreach (string raw in File.ReadAllLines(txt))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            int v;
            if (!int.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) continue;
            string k = line.Substring(0, eq).Trim();
            if (k == "w") p.W = v;
            else if (k == "h") p.H = v;
            else if (k == "idle") p.Counts[0] = v;
            else if (k == "press") p.Counts[1] = v;
            else if (k == "click") p.Counts[2] = v;
            else if (k == "drag") p.Counts[3] = v;
        }
        if (p.W <= 0 || p.H <= 0) throw new Exception("frames.txt 里的画布尺寸不合法");
        int fb = p.W * p.H * 4;
        byte[] blob = File.ReadAllBytes(bin);
        int total = 0;
        for (int i = 0; i < 4; i++) total += p.Counts[i];
        if (total <= 0) throw new Exception("frames.txt 里没有任何帧数");
        if ((long)blob.Length < (long)fb * total)
            throw new Exception("frames.bin 大小不足（要 " + ((long)fb * total) + " 字节，实际 " + blob.Length + "）");
        for (int i = 0; i < total; i++)
        {
            var f = new byte[fb];
            Buffer.BlockCopy(blob, i * fb, f, 0, fb);
            p.Frames.Add(f);
        }
        return p;
    }

    static List<byte[]>[] SplitSegments(string packDir, Pack p)
    {
        var segs = new List<byte[]>[4];
        int pos = 0;
        for (int i = 0; i < 4; i++)
        {
            segs[i] = new List<byte[]>(p.Counts[i]);
            for (int k = 0; k < p.Counts[i]; k++) segs[i].Add(p.Frames[pos++]);
        }
        return segs;
    }

    static void WriteSegments(string packDir, Pack p, List<byte[]>[] segs, bool preview)
    {
        for (int i = 0; i < 4; i++) p.Counts[i] = segs[i].Count;
        p.Frames.Clear();
        for (int i = 0; i < 4; i++) p.Frames.AddRange(segs[i]);
        WritePack(Path.Combine(packDir, "动画"), p, preview);
    }

    public static int SlotIndex(string slot)
    {
        int i = Array.IndexOf(Slots, (slot ?? "").Trim().ToLowerInvariant());
        if (i < 0) throw new Exception("未知的动画槽位：" + slot);
        return i;
    }

    static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

    static string Str(Dictionary<string, string> m, string k, string def)
    {
        string v;
        return m.TryGetValue(k, out v) && v != "" ? v : def;
    }

    static double Amp(Dictionary<string, string> m, string k)
    {
        double v;
        if (double.TryParse(Str(m, k, "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return QCore.ClampD(v, 0.2, 2.0);
        return 1.0;
    }

    public static Dictionary<string, string> ReadManifest(string dir)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        string[] order = { "name", "image", "frames", "meta", "lines", "voicelines", "press", "release",
                           "charH", "ampIdle", "ampPress", "ampClick", "ampDrag",
                           "customIdle", "customPress", "customClick", "customDrag", "voices", "created" };
        var lines = new List<string>();
        foreach (string k in order) { string v; if (m.TryGetValue(k, out v)) lines.Add(k + "=" + v); }
        foreach (KeyValuePair<string, string> kv in m)
            if (Array.FindIndex(order, x => string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase)) < 0)
                lines.Add(kv.Key + "=" + kv.Value);
        File.WriteAllLines(Path.Combine(dir, "char.json"), lines.ToArray(), new UTF8Encoding(false));
    }

    public static string FindPackImage(string dir)
    {
        foreach (string sub in new string[] { "图片", "" })
            foreach (string ext in ImageExt)
            {
                string p = Path.Combine(dir, sub, "image" + ext);
                if (File.Exists(p)) return p;
            }
        return "";
    }
}
