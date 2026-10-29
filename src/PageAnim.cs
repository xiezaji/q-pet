using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

static partial class Settings
{
        static void BuildAnimPage()
        {
            if (PageAnim == null) return;
            StopAnimPreviews();
            AnimCards.Clear();
            PageAnim.Children.Clear();
            _animCardsDir = SelectedChar != null ? SelectedChar.Dir : "";

            AddPageHead(PageAnim, "// ANIMATION", "动画", "每类动画单独调幅度与倍速，预览是真的帧");
            PageAnim.Children.Add(new Border { Height = 20 });

            CharEntry ce = SelectedChar;
            Dictionary<string, string> cm = (ce != null && !string.IsNullOrEmpty(ce.Dir))
                ? ReadManifest(ce.Dir) : new Dictionary<string, string>();
            byte[] data = null;
            int fw = 0, fh = 0, idleN = 24, pressN = 6, clickN = 6, dragN = 8;
            if (ce != null && ce.HasFrames)
            {
                try
                {
                    data = File.ReadAllBytes(ce.Frames);
                    if (File.Exists(ce.Meta))
                    {
                        foreach (string raw in File.ReadAllLines(ce.Meta))
                        {
                            int eq = raw.IndexOf('=');
                            if (eq <= 0) continue;
                            string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                            if (k == "w") int.TryParse(v, out fw);
                            else if (k == "h") int.TryParse(v, out fh);
                            else if (k == "idle") int.TryParse(v, out idleN);
                            else if (k == "press") int.TryParse(v, out pressN);
                            else if (k == "click") int.TryParse(v, out clickN);
                            else if (k == "drag") int.TryParse(v, out dragN);
                        }
                    }
                }
                catch (Exception ex) { LogErr("animload", ex); data = null; }
            }
            if (fw <= 0 || fh <= 0) data = null;
            int totalFrames = fw > 0 ? data.Length / (fw * fh * 4) : 0;

            string[] titles = { "待机动画", "按键动画", "点击动画", "拖动动画" };
            string[] descs = {
                "呼吸浮动，平时一直在播",
                "键盘 / 鼠标按下时的挤压",
                "点她时的挤压 + 小跳",
                "拖动时的悬空摆动"
            };
            int[] starts = { 0, idleN, idleN + pressN, idleN + pressN + clickN };
            int[] counts = { idleN, pressN, clickN, dragN };

            var grid = new UniformGrid { Columns = 2 };
            for (int i = 0; i < AnimKeys.Length; i++)
            {
                string key = AnimKeys[i];
                double amp = EntryAmp(ce, key);
                bool enabled = CurAnim("anim" + Cap(key));
                int pct = CurSpeedPct("speed" + Cap(key));

                var card = new AnimCard { Key = key, Title = titles[i] };
                var root = new StackPanel();

                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition());
                var box = new Border
                {
                    Width = 104, Height = 104, Background = C("#FAFBFD"),
                    CornerRadius = new CornerRadius(0), Margin = new Thickness(0, 0, 12, 0)
                };
                AnimPreview pv = new AnimPreview(96);
                box.Child = pv.View;
                head.Children.Add(box);

                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                right.Children.Add(Txt(titles[i], 14, "#E4000000", true));
                right.Children.Add(Txt(descs[i], 12, "#99000000"));
                var chk = new CheckBox
                {
                    Content = "启用", Style = (Style)Win.FindResource("ToggleSwitch"),
                    IsChecked = enabled, Margin = new Thickness(0, 10, 0, 0)
                };
                right.Children.Add(chk);
                Grid.SetColumn(right, 1); head.Children.Add(right);
                root.Children.Add(head);
                root.Children.Add(new Border { Height = 10 });

                // 幅度
                var ampRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                ampRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                ampRow.ColumnDefinitions.Add(new ColumnDefinition());
                ampRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                var al = Txt("幅度", 12, "#99000000"); al.VerticalAlignment = VerticalAlignment.Center;
                ampRow.Children.Add(al);
                var ampSld = new Slider
                {
                    Minimum = 20, Maximum = 200, TickFrequency = 10,
                    Value = Math.Max(20, Math.Min(200, amp * 100)),
                    Style = (Style)Win.FindResource("WinSlider")
                };
                Grid.SetColumn(ampSld, 1); ampRow.Children.Add(ampSld);
                var ampVal = Txt((int)ampSld.Value + "%", 11, "#99000000");
                ampVal.TextAlignment = TextAlignment.Right; ampVal.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(ampVal, 2); ampRow.Children.Add(ampVal);
                ampSld.ValueChanged += (s, e) => ampVal.Text = (int)ampSld.Value + "%";
                root.Children.Add(ampRow);

                // 倍速
                var spdRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                spdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                spdRow.ColumnDefinitions.Add(new ColumnDefinition());
                spdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                var sl = Txt("倍速", 12, "#99000000"); sl.VerticalAlignment = VerticalAlignment.Center;
                spdRow.Children.Add(sl);
                var spdSld = new Slider
                {
                    Minimum = 50, Maximum = 200, TickFrequency = 10, Value = pct,
                    Style = (Style)Win.FindResource("WinSlider")
                };
                Grid.SetColumn(spdSld, 1); spdRow.Children.Add(spdSld);
                var spdVal = Txt("×" + (pct / 100.0).ToString("0.0", CultureInfo.InvariantCulture), 11, "#99000000");
                spdVal.TextAlignment = TextAlignment.Right; spdVal.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(spdVal, 2); spdRow.Children.Add(spdVal);
                spdSld.ValueChanged += (s, e) =>
                {
                    double v = spdSld.Value / 100.0;
                    spdVal.Text = "×" + v.ToString("0.0", CultureInfo.InvariantCulture);
                    pv.SetSpeed(v);
                };
                root.Children.Add(spdRow);

                root.Children.Add(Txt("幅度需保存后生效", 11, "#99000000"));

                var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var customBtn = new Button
                {
                    Content = "自定义动画…", Style = (Style)Win.FindResource("SecondaryButton"),
                    Padding = new Thickness(14, 4, 22, 4), MinWidth = 0, Height = 28, FontSize = 12
                };
                BuildCustomMenu(customBtn, ce, key);
                foot.Children.Add(customBtn);
                if (!string.IsNullOrEmpty(ce.Dir) && cm.ContainsKey("custom" + Cap(key)))
                {
                    var tag = Txt("已自定义", 11, "#0F7B43");
                    tag.Margin = new Thickness(10, 0, 0, 0);
                    tag.VerticalAlignment = VerticalAlignment.Center;
                    foot.Children.Add(tag);
                }
                root.Children.Add(foot);

                var border = new Border
                {
                    Background = (Brush)Win.Resources["Panel"],
                    BorderBrush = C("#D9D9D6"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 14, 16, 14),
                    Margin = new Thickness(0, 0, 10, 12)
                };
                border.Child = root;
                grid.Children.Add(border);

                card.Toggle = chk; card.Amp = ampSld; card.Speed = spdSld;
                card.AmpLbl = ampVal; card.SpeedLbl = spdVal; card.Preview = pv;
                AnimCards.Add(card);

                if (data != null && starts[i] + counts[i] <= totalFrames)
                {
                    try { pv.Load(data, fw, fh, starts[i], counts[i], pct / 100.0, key != "idle"); }
                    catch (Exception ex)
                    {
                        LogErr("animprev " + key + " w=" + fw + " h=" + fh + " fb=" + (fw * fh * 4)
                            + " len=" + data.Length + " start=" + starts[i] + " n=" + counts[i], ex);
                    }
                }
            }
            PageAnim.Children.Add(grid);

            if (data == null)
                PageAnim.Children.Add(Txt("保存后自动生成", 12, "#C42B1C"));

            var tip = new Border { Style = (Style)Win.FindResource("Card") };
            tip.Child = Txt("幅度需保存；倍速即时生效", 12, "#99000000");
            PageAnim.Children.Add(tip);
        }

        // ---------------- 自定义动画帧 ----------------
        static string SlotName(string slot)
        {
            if (slot == "idle") return "待机动画";
            if (slot == "press") return "按键动画";
            if (slot == "click") return "点击动画";
            return "拖动动画";
        }

        static void BuildCustomMenu(Button btn, CharEntry ce, string slot)
        {
            var menu = new ContextMenu();
            var miFile = new MenuItem { Header = "用 GIF / 图片替换…" };
            miFile.Click += (s, e) => PickCustomAnim(ce, slot, false);
            var miDir = new MenuItem { Header = "用帧文件夹替换…" };
            miDir.Click += (s, e) => PickCustomAnim(ce, slot, true);
            var miReset = new MenuItem { Header = "恢复默认动画" };
            miReset.Click += (s, e) => ResetCustomAnim(ce, slot);
            menu.Items.Add(miFile);
            menu.Items.Add(miDir);
            menu.Items.Add(new Separator());
            menu.Items.Add(miReset);
            btn.Click += (s, e) => { menu.PlacementTarget = btn; menu.IsOpen = true; };
        }

        static void PickCustomAnim(CharEntry ce, string slot, bool folder)
        {
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) return;
            string src = "";
            if (folder)
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "选择一帧一张图的文件夹（按文件名排序）";
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    src = dlg.SelectedPath;
                }
            }
            else
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "动画/图片 (*.gif;*.png;*.jpg;*.jpeg;*.webp)|*.gif;*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*"
                };
                if (dlg.ShowDialog() != true) return;
                src = dlg.FileName;
            }
            string err;
            if (RunAnimCustom("apply", ce.Dir, slot, src, out err))
            {
                ReloadChars();
                BuildAnimPage();
                Dlg.Info("已替换动画",
                    "「" + ce.Name + "」的" + SlotName(slot) + "已换成自定义帧。\n点「保存更改」重启桌宠后生效。", "知道了");
            }
            else
            {
                Dlg.Info("替换失败", string.IsNullOrEmpty(err) ? "这个文件读不出画面，换一张图或一个 GIF 再试。" : err, "好");
            }
        }

        static void ResetCustomAnim(CharEntry ce, string slot)
        {
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) return;
            if (!Dlg.Ask("恢复默认动画？", "「" + ce.Name + "」的" + SlotName(slot) + "会按立绘重新生成，自定义帧会被覆盖。", "恢复", "算了", true)) return;
            string err;
            if (RunAnimCustom("reset", ce.Dir, slot, "", out err))
            {
                ReloadChars();
                BuildAnimPage();
                Dlg.Info("已恢复默认", SlotName(slot) + "已按立绘重新生成。\n点「保存更改」重启桌宠后生效。", "知道了");
            }
            else
            {
                Dlg.Info("恢复失败", string.IsNullOrEmpty(err) ? "按立绘重新生成失败。" : err, "好");
            }
        }

        // apply / reset 都在进程内做完（SpriteGen.cs）：原先调 anim_custom.py，
        // 没有 Python + Pillow 的用户点这两个按钮只会看到「执行失败」。
        static bool RunAnimCustom(string cmd, string packDir, string slot, string src, out string err)
        {
            err = "";
            try
            {
                if (cmd == "apply") QSprite.ApplyCustom(packDir, slot, src);
                else QSprite.ResetSlot(packDir, slot);
                return true;
            }
            catch (Exception ex) { err = ex.Message; LogErr("customanim", ex); return false; }
        }

}
