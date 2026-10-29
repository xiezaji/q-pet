using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static partial class Settings
{
        static string CharsDir { get { return Path.Combine(Here, "chars"); } }

        static string SanitizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return QCore.SanitizeName(s);   // 实现在 Core.cs
        }

        // ffmpeg 只用来把 mp3/m4a/ogg 转成 wav（桌宠只播 wav）。它是**可选**的：
        // 找不到就把这几个文件跳过并说明原因，wav 照常导入。
        static string FindFfmpeg()
        {
            foreach (string c in new string[]
            {
                Path.Combine(Here, @"tools\ffmpeg.exe"),
                Path.Combine(Here, "ffmpeg.exe"),
            })
                if (File.Exists(c)) return c;

            string pf = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pf.Split(';'))
            {
                if (dir.Trim() == "") continue;
                try { string c = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(c)) return c; } catch (Exception) { }
            }
            return "";
        }

        static bool ConvertToWav(string src, string dst)
        {
            try
            {
                string ff = FindFfmpeg();
                if (ff == "") return false;
                var psi = new ProcessStartInfo(ff, "-y -v error -i \"" + src + "\" -ar 48000 -ac 1 -c:a pcm_s16le \"" + dst + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                var pr = Process.Start(psi); pr.WaitForExit(60000);
                return File.Exists(dst);
            }
            catch (Exception ex) { LogErr("?", ex); return false; }
        }

        static void PickImportImage()
        {
            var d = new Microsoft.Win32.OpenFileDialog();
            d.Filter = "图片 (*.png;*.jpg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*";
            if (d.ShowDialog() != true) return;
            _importImg = d.FileName;
            LblImg.Text = "已选择：" + Path.GetFileName(_importImg);
        }

        static void PickImportVoices()
        {
            // WPF 没有目录选择器：用 OpenFileDialog 的“假文件名”取所在目录
            var d = new Microsoft.Win32.OpenFileDialog
            {
                ValidateNames = false, CheckFileExists = false, CheckPathExists = true,
                FileName = "选择此文件夹"
            };
            if (d.ShowDialog() != true) return;
            string dir = Path.GetDirectoryName(d.FileName);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            _importVoiceDir = dir;
            int n = 0;
            foreach (string f in Directory.GetFiles(dir))
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e == ".wav" || e == ".mp3" || e == ".m4a" || e == ".ogg") n++;
            }
            LblVoices.Text = "已选择：" + dir + "（" + n + " 个音频）";
        }

        static string SafeFile(string s)
        {
            return QCore.SafeFile(s);   // 实现在 Core.cs
        }

        // 「新建角色」= 新建 chars\<名字>\，把用户给的图片 / 语音放进 图片 / 音效，
        // 生成 动画，再写一份相对路径的 char.json。以后加角色都走这里。
        static void DoImport()
        {
            try
            {
                string name = SanitizeName(TxtName.Text);
                if (name == "") { LblImportMsg.Text = "请先填写角色名称。"; return; }
                if (_importImg == "") { LblImportMsg.Text = "请选择一张角色图片（每个角色包都需要）。"; return; }

                string dir = Path.Combine(CharsDir, name);
                string imgDir = Path.Combine(dir, "图片");
                string animDir = Path.Combine(dir, "动画");
                string vdir = Path.Combine(dir, "音效", "voices");
                Directory.CreateDirectory(imgDir);
                Directory.CreateDirectory(animDir);
                Directory.CreateDirectory(vdir);

                string img = Path.Combine(imgDir, "image" + Path.GetExtension(_importImg).ToLowerInvariant());
                File.Copy(_importImg, img, true);

                var rows = new List<KeyValuePair<string, string>>();
                int ok = 0, skipped = 0;
                if (_importVoiceDir != "")
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string f in Directory.GetFiles(_importVoiceDir))
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e != ".wav" && e != ".mp3" && e != ".m4a" && e != ".ogg") continue;
                        string text = Path.GetFileNameWithoutExtension(f).Trim();
                        if (text == "") continue;
                        string stem = SafeFile(text);
                        string uniq = stem;
                        for (int k = 2; used.Contains(uniq); k++) uniq = stem + "_" + k;
                        used.Add(uniq);
                        string dst = Path.Combine(vdir, uniq + ".wav");
                        bool good;
                        if (e == ".wav") { try { File.Copy(f, dst, true); good = true; } catch (Exception) { good = false; } }
                        else good = ConvertToWav(f, dst);
                        if (good && File.Exists(dst))
                        {
                            rows.Add(new KeyValuePair<string, string>(text, "音效/voices/" + uniq + ".wav"));
                            ok++;
                        }
                        else skipped++;
                    }
                }
                using (var w = new StreamWriter(Path.Combine(dir, "音效", "voices.tsv"), false, new UTF8Encoding(false)))
                    foreach (var r in rows) w.Write(r.Key + "\t" + r.Value + "\r\n");
                using (var w = new StreamWriter(Path.Combine(dir, "台词.txt"), false, new UTF8Encoding(false)))
                    foreach (var r in rows) w.Write(r.Key + "\r\n");

                int size = (int)SldSize.Value;
                var m = new Dictionary<string, string>();
                m["name"] = name;
                m["image"] = "图片/" + Path.GetFileName(img);
                m["frames"] = "动画/frames.bin";
                m["meta"] = "动画/frames.txt";
                m["lines"] = "台词.txt";
                m["voicelines"] = "音效/voices.tsv";
                if (rows.Count > 0) m["press"] = rows[0].Value;
                if (rows.Count > 1) m["release"] = rows[1].Value;
                m["charH"] = size.ToString(CultureInfo.InvariantCulture);
                m["voices"] = rows.Count.ToString(CultureInfo.InvariantCulture);
                WriteManifest(dir, m);

                var entry = new CharEntry
                {
                    Name = name, Dir = dir, Image = img, Voices = Path.Combine(dir, "音效", "voices.tsv"),
                    Lines = Path.Combine(dir, "台词.txt"), Frames = Path.Combine(animDir, "frames.bin"),
                    Meta = Path.Combine(animDir, "frames.txt"), CharH = size, VoiceCount = rows.Count
                };
                LblImportMsg.Text = "正在生成动画…";
                if (!BuildPackFrames(entry, size))
                {
                    LblImportMsg.Text = "动画生成失败：" + (string.IsNullOrEmpty(_framesErr)
                        ? "图片可能损坏，换一张 png/jpg 再试。" : _framesErr);
                    return;
                }

                ApplyPack(entry);
                LblImportMsg.Text = "已创建并应用：" + name + "（语音 " + ok + " 条"
                    + (skipped > 0 ? "，跳过 " + skipped + " 条 —— mp3/m4a/ogg 要先转成 wav：把 ffmpeg.exe 放进 tools 目录再试，或自己在外面转好" : "") + "）";
                TxtName.Text = ""; _importImg = ""; _importVoiceDir = "";
                LblImg.Text = "未选择"; LblVoices.Text = "未选择";
                ReloadChars();
                RefreshImport();
            }
            catch (Exception ex)
            {
                LogErr("import", ex);
                LblImportMsg.Text = "导入失败：" + ex.Message;
            }
        }

        // 应用某个角色包：只写 charDir，帧 / 台词 / 语音都由角色自己的 char.json 决定
        static void ApplyPack(CharEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Dir)) return;
            Dictionary<string, string> ov = CollectOverrides(RelToHere(e.Dir));
            WriteOverrides(ov);
            PatchConfig(ov);
            RestartPet();
        }

        static bool IsCurrent(CharEntry e)
        {
            string cur = CurCharDir;
            if (string.IsNullOrEmpty(cur) || e == null || e.Dir == "") return false;
            string c = Path.IsPathRooted(cur) ? cur : Path.Combine(Here, cur);
            try
            {
                return string.Equals(Path.GetFullPath(c).TrimEnd('\\'), Path.GetFullPath(e.Dir).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { LogErr("?", ex); return false; }
        }

        static void DeletePack(CharEntry e)
        {
            if (e == null) return;
            if (!Dlg.Ask("删除角色「" + e.Name + "」？",
                "整个文件夹都会被删掉（图片 / 动画 / 音效），无法恢复。", "删除", "算了", true)) return;
            bool wasCurrent = IsCurrent(e);
            try { Directory.Delete(e.Dir, true); } catch (Exception ex) { LogErr("delpack", ex); }
            ReloadChars();
            if (wasCurrent && ChList.Count > 0)
            {
                CmbChar.SelectedIndex = 0;
                ApplyPack(ChList[0]);
                LblImportMsg.Text = "已删除，切到「" + ChList[0].Name + "」。";
            }
            RefreshImport();
        }

        // 角色候选：chars\* 里所有带 char.json 的文件夹（一个角色一个文件夹）
        static void ReloadChars()
        {
            string cur = CurCharDir;
            string curFull = "";
            if (!string.IsNullOrEmpty(cur))
            {
                string c = Path.IsPathRooted(cur) ? cur : Path.Combine(Here, cur);
                try { curFull = Path.GetFullPath(c); } catch (Exception) { }
            }
            ChList.Clear();
            try
            {
                if (Directory.Exists(CharsDir))
                {
                    foreach (string d in Directory.GetDirectories(CharsDir))
                    {
                        if (!File.Exists(Path.Combine(d, "char.json"))) continue;
                        Dictionary<string, string> m = ReadManifest(d);
                        string nm = m.ContainsKey("name") && m["name"] != "" ? m["name"] : Path.GetFileName(d);
                        string img = FindPackImage(d);
                        if (img == "") img = Path.Combine(Here, "pet-placeholder.png");
                        int h, vc;
                        if (!int.TryParse(m.ContainsKey("charH") ? m["charH"] : "", out h) || h <= 0) h = 219;
                        if (!int.TryParse(m.ContainsKey("voices") ? m["voices"] : "", out vc)) vc = 0;
                        ChList.Add(new CharEntry
                        {
                            Name = nm, Dir = d, Image = img,
                            Voices = ResolvePack(d, m, "voicelines"),
                            Lines = ResolvePack(d, m, "lines"),
                            Frames = ResolvePack(d, m, "frames"),
                            Meta = ResolvePack(d, m, "meta"),
                            CharH = h, VoiceCount = vc,
                            AmpIdle = ReadAmp(m, "ampIdle"), AmpPress = ReadAmp(m, "ampPress"),
                            AmpClick = ReadAmp(m, "ampClick"), AmpDrag = ReadAmp(m, "ampDrag")
                        });
                    }
                }
            }
            catch (Exception ex) { LogErr("?", ex); }
            ChList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

            if (ChList.Count == 0)
            {
                // 一个角色包都没有：别让面板空掉，给一个占位项（不能保存）
                ChList.Add(new CharEntry { Name = "（还没有角色包）", Dir = "", Image = Path.Combine(Here, "pet-placeholder.png") });
            }

            CmbChar.Items.Clear();
            int sel = 0;
            for (int i = 0; i < ChList.Count; i++)
            {
                CmbChar.Items.Add(ChList[i].Name);
                if (curFull != "" && ChList[i].Dir != "")
                {
                    try
                    {
                        if (string.Equals(Path.GetFullPath(ChList[i].Dir), curFull, StringComparison.OrdinalIgnoreCase)) sel = i;
                    }
                    catch (Exception ex) { LogErr("?", ex); }
                }
            }
            if (sel >= 0 && sel < CmbChar.Items.Count) CmbChar.SelectedIndex = sel;
            ShowCharPreview(); ShowSizePreview();
        }

        static void RefreshImport()
        {
            if (ImportList == null) return;
            ImportList.Children.Clear();
            ImportList.Children.Add(Txt("一个角色 = 一个文件夹 │ 换角色在「形象与大小」里选，保存后生效", 12, "#99000000"));
            ImportList.Children.Add(new Border { Height = 10 });

            // 方块卡：立绘 + 名字 + 像素高 + 语音数 + 删除。一行三张（内容列约 540px）。
            _importMarked = "";
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (CharEntry e in ChList)
            {
                if (string.IsNullOrEmpty(e.Dir)) continue;
                wrap.Children.Add(ImportCard(e));
            }
            if (wrap.Children.Count == 0)
                ImportList.Children.Add(Txt("还没有角色包，左边新建一个吧", 12, "#99000000"));
            else
                ImportList.Children.Add(wrap);
        }

        // 「当前在用」的绿：和首页 RUNNING 点、统计页实时点同一支 #12A150。
        // 不跟主题色走 —— 主题色管「可点 / 展开」，绿只管「在跑」。
        static readonly Color CharCardCurGreen = Color.FromRgb(0x12, 0xA1, 0x50);

        static readonly Brush CharCardCurLine = new SolidColorBrush(CharCardCurGreen);

        // 删除键：30px 宽、占两行高（名字行 + 读数行）的图标砖，不是红字链接。
        // 一排卡片上挂三四个红色「删除」，整页像在报警；所以红只在手指真落上去时才出现：
        // 常态灰线 → 悬停浅红底 + 红图标 → 按下实红底 + 白图标（和标题栏关闭键同一套手感）。
        // 做成整块而不是行尾小图标：点起来够得着，也不跟读数抢同一行的宽度。
        static Button ImportDeleteButton(CharEntry ce)
        {
            Color idle = Color.FromRgb(0x6E, 0x73, 0x7C);      // 常态：中性灰（比说明文字略深，才看得出是控件）
            Color hot = Color.FromRgb(0xC4, 0x2B, 0x1C);       // 危险红
            Color hotBg = Color.FromRgb(0xFD, 0xEC, 0xEA);     // DangerSoft
            Color hotLine = Color.FromRgb(0xF0, 0xD2, 0xCC);

            // 几何直接按 18×18 画：不靠 Viewbox 放大，1.4px 的线才不会描出毛边
            var ico = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M2.2,5.4 H15.8 M6.6,5.4 V2.6 H11.4 V5.4 "
                                    + "M4.1,5.4 L5.3,15.4 H12.7 L13.9,5.4 M7.3,8 V12.9 M10.7,8 V12.9"),
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = new SolidColorBrush(idle),
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var box = new Grid { Width = 18, Height = 18, Background = Brushes.Transparent };
            box.Children.Add(ico);

            var b = new Button
            {
                Content = box, Width = 30, Cursor = Cursors.Hand,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                FocusVisualStyle = (Style)Win.FindResource("FocusRing"),
                ToolTip = "删掉整个角色文件夹（图片 / 动画 / 音效），无法恢复",
                SnapsToDevicePixels = true, UseLayoutRounding = true
            };

            // 模板：1px 描边的方框（常态描边透明 → 不占视觉，悬停才显形）+ 居中的图标。
            // 描边厚度始终是 1，只换色不换值 —— 换值会让图标在悬停时挪 1px。
            var tpl = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.SnapsToDevicePixelsProperty, true);
            grid.AppendChild(bd);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);
            tpl.VisualTree = grid;
            b.Template = tpl;

            bool over = false, down = false;
            Action sync = () =>
            {
                b.Background = new SolidColorBrush(down ? hot : over ? hotBg : Colors.Transparent);
                b.BorderBrush = new SolidColorBrush(down ? hot : over ? hotLine : Colors.Transparent);
                ico.Stroke = new SolidColorBrush(down ? Colors.White : over ? hot : idle);
            };
            b.MouseEnter += (s2, e2) => { over = true; sync(); };
            b.MouseLeave += (s2, e2) => { over = false; down = false; sync(); };
            b.PreviewMouseLeftButtonDown += (s2, e2) => { down = true; sync(); };
            b.PreviewMouseLeftButtonUp += (s2, e2) => { down = false; sync(); };
            b.LostMouseCapture += (s2, e2) => { if (!down) return; down = false; sync(); };
            b.Click += (s2, e2) => DeletePack(ce);
            return b;
        }

        static FrameworkElement ImportCard(CharEntry e)
        {
            bool cur = IsCurrent(e);
            CharEntry ce = e;

            var outer = new Grid { Width = CharCardW, Margin = new Thickness(0, 0, 12, 12) };
            // 当前在用：**只说绿框**，不再写「使用中」三个字。绿 = 在跑（和首页那颗 RUNNING 绿点同色），
            // 底色跟着绿走一点点，卡在这一排里一眼认得出，又不用多一行字去解释。
            var card = new Border
            {
                Background = cur ? CharCardCurFill : Brushes.White,
                BorderBrush = cur ? CharCardCurLine : C("#E2E6EC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                SnapsToDevicePixels = true
            };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CharCardArtH) });
            g.RowDefinitions.Add(new RowDefinition());

            // ---- 立绘 ----
            var plate = new Border { Background = C("#F7F9FC") };
            BitmapSource art = Portrait(ce.Image);
            if (art != null)
                plate.Child = new Image
                {
                    Source = art, Stretch = Stretch.Uniform,
                    Margin = new Thickness(10, 12, 10, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
            else
                plate.Child = Txt("无立绘", 11, "#99000000");
            ((FrameworkElement)plate.Child).HorizontalAlignment = HorizontalAlignment.Center;
            if (plate.Child is TextBlock) ((TextBlock)plate.Child).VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(plate, 0); g.Children.Add(plate);

            // ---- 信息 ----
            // 「使用中」三个字删掉以后，这里只剩两行真内容：名字 / 读数。
            // 删除键跨的就是这两行（高 = 名字行 + 读数行），和左边一起构成一块完整的信息区：
            // 图标中心对得上文字块中心，不会挂在读数下面那片空地里。
            var info = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            info.ColumnDefinitions.Add(new ColumnDefinition());
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

            var nm = Txt(ce.Name, 13, "#E4000000", true);
            nm.TextWrapping = TextWrapping.NoWrap;              // 名字一律单行，否则长名字会把卡撑高
            nm.TextTrimming = TextTrimming.CharacterEllipsis;
            nm.ToolTip = ce.Dir;
            nm.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetRow(nm, 0); Grid.SetColumn(nm, 0);
            info.Children.Add(nm);

            var meta = Txt(ce.CharH + " px │ 语音 " + ce.VoiceCount, 11, "#99000000");
            meta.VerticalAlignment = VerticalAlignment.Center;
            meta.TextWrapping = TextWrapping.NoWrap;
            meta.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Margin = new Thickness(0, 0, 8, 0);
            meta.ToolTip = (ce.HasFrames ? "动画 " + ce.Name + " 已就绪" : "缺动画（保存时会重新生成）")
                + "\n" + ce.Dir;
            if (!ce.HasFrames) meta.Foreground = C("#B3261E");
            Grid.SetRow(meta, 1); Grid.SetColumn(meta, 0);
            info.Children.Add(meta);

            if (cur) _importMarked = ce.Name;        // 自检探针：绿框落在谁身上

            Button del = ImportDeleteButton(ce);
            Grid.SetColumn(del, 1);
            Grid.SetRow(del, 0); Grid.SetRowSpan(del, 2);     // 跨名字 + 读数两行
            del.VerticalAlignment = VerticalAlignment.Stretch;
            info.Children.Add(del);

            Grid.SetRow(info, 1); g.Children.Add(info);
            card.Child = g;
            outer.Children.Add(card);

            // 当前在用：顶部压一条 3px 绿（覆盖层，不占布局 → 不会把立绘顶下去 2px）。
            // 「使用中」那三个字删掉之后，靠这条 + 绿边 + 浅绿底三件一起顶住识别度，
            // 但都不占文字位置，卡片读起来还是「图 / 名 / 读数」三样。
            if (cur)
            {
                var bar = new Border
                {
                    Height = 3, Background = CharCardCurLine,
                    VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
                };
                outer.Children.Add(bar);
            }
            return outer;
        }

}
