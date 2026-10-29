using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

static partial class Settings
{
        static int _voiceEditing = -1;
        static DispatcherTimer _voiceSyncTimer;

        static void ReloadSfxGroups()
        {
            if (CmbSet == null) return;
            string cur = CurOf("sfxGroup", "auto");
            CmbSet.Items.Clear();
            CmbSet.Items.Add("跟随角色（推荐）");
            int sel = 0;
            foreach (CharEntry e in ChList)
            {
                if (string.IsNullOrEmpty(e.Dir)) continue;
                CmbSet.Items.Add(e.Name);
                if (string.Equals(cur, e.Name, StringComparison.CurrentCulture)) sel = CmbSet.Items.Count - 1;
            }
            CmbSet.SelectedIndex = sel;
        }

        static void LoadVoiceLibrary()
        {
            VoiceLines.Clear();
            CancelVoiceEdit();
            try
            {
                CharEntry ce = SelectedChar;
                if (BtnAddVoice != null)
                    BtnAddVoice.IsEnabled = ce != null && !string.IsNullOrEmpty(ce.Dir);
                if (ce != null && !string.IsNullOrEmpty(ce.Voices) && File.Exists(ce.Voices))
                {
                    string bd = string.IsNullOrEmpty(ce.Dir) ? Here : ce.Dir;
                    foreach (string ln in File.ReadAllLines(ce.Voices))
                    {
                        int tab = ln.IndexOf('\t');
                        if (tab <= 0) continue;
                        string text = ln.Substring(0, tab).Trim();
                        string p = ln.Substring(tab + 1).Trim();
                        if (p != "" && !Path.IsPathRooted(p)) p = Path.Combine(bd, p);
                        if (text != "" && File.Exists(p)) VoiceLines.Add(new VoiceLine { Text = text, Path = p });
                    }
                }
            }
            catch (Exception ex) { LogErr("voicelib", ex); }
            if (LblVoiceCount != null) LblVoiceCount.Text = "共 " + VoiceLines.Count + " 条";
            RefreshVoiceList();
        }

        // 台词库列表：一行一张"卡片"，行与行之间有明确间隔；整行可点即试听
        static void RefreshVoiceList()
        {
            if (VoiceListPanel == null) return;
            if (_voiceEditing >= VoiceLines.Count) _voiceEditing = -1;
            VoiceListPanel.Children.Clear();
            _voiceRowPlaying = null;
            string q = TxtVoiceSearch != null ? TxtVoiceSearch.Text.Trim() : "";
            int shown = 0;
            for (int i = 0; i < VoiceLines.Count; i++)
            {
                VoiceLine v = VoiceLines[i];
                if (q != "" && v.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                int idx = i;
                bool editing = i == _voiceEditing;
                var row = new Border
                {
                    Background = editing ? C("#F7FAFF") : Brushes.White,
                    BorderBrush = editing ? C("#9CC2F0") : C("#DFE3E9"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 8, 8, 8),
                    Cursor = editing ? Cursors.Arrow : Cursors.Hand
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var no = new TextBlock
                {
                    Text = shown.ToString("00"), FontFamily = new FontFamily("Consolas"), FontSize = 11,
                    Foreground = C("#8A93A3"), VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                g.Children.Add(no);

                var acts = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(acts, 2);
                g.Children.Add(acts);

                if (editing)
                {
                    var box = new TextBox
                    {
                        Text = v.Text, FontSize = 13, Height = 30, MaxLength = 80,
                        Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
                        BorderBrush = C("#9CC2F0"), BorderThickness = new Thickness(1),
                        Background = Brushes.White, Margin = new Thickness(2, 0, 10, 0),
                        ToolTip = "回车保存，Esc 取消"
                    };
                    Grid.SetColumn(box, 1);
                    box.KeyDown += (s, e) =>
                    {
                        if (e.Key == Key.Enter) { CommitVoiceRename(idx, box.Text); e.Handled = true; }
                        else if (e.Key == Key.Escape) { CancelVoiceEdit(); RefreshVoiceList(); e.Handled = true; }
                    };
                    box.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
                    g.Children.Add(box);

                    var save = new Button
                    {
                        Content = "保存", Height = 30, Padding = new Thickness(12, 0, 12, 0), FontSize = 12,
                        Style = (Style)Win.FindResource("PrimaryButton"), VerticalAlignment = VerticalAlignment.Center
                    };
                    save.Click += (s, e) => CommitVoiceRename(idx, box.Text);
                    acts.Children.Add(save);

                    var cancel = new Button
                    {
                        Content = "取消", Height = 30, Padding = new Thickness(10, 0, 10, 0), FontSize = 12,
                        Style = (Style)Win.FindResource("SecondaryButton"),
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0)
                    };
                    cancel.Click += (s, e) => { CancelVoiceEdit(); RefreshVoiceList(); };
                    acts.Children.Add(cancel);
                }
                else
                {
                    var t = Txt(v.Text, 13, "#212529");
                    t.TextTrimming = TextTrimming.CharacterEllipsis;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Margin = new Thickness(2, 0, 10, 0);
                    t.ToolTip = v.Text;
                    Grid.SetColumn(t, 1); g.Children.Add(t);

                    Action act = () =>
                    {
                        PlayVoiceFile(v.Path);
                        LblVoiceHint.Text = "正在试听：" + v.Text;
                        if (_voiceRowPlaying != null && _voiceRowPlaying != row)
                        {
                            _voiceRowPlaying.Background = Brushes.White;
                            _voiceRowPlaying.BorderBrush = C("#DFE3E9");
                        }
                        _voiceRowPlaying = row;
                        row.Background = C("#F4F9FF");
                        row.BorderBrush = C("#9CC2F0");
                    };

                    row.MouseLeftButtonUp += (s, e) => { act(); e.Handled = true; };
                    row.MouseEnter += (s, e) =>
                    {
                        if (row == _voiceRowPlaying) return;
                        row.Background = C("#F8FBFF");
                        row.BorderBrush = C("#CFE0F7");
                    };
                    row.MouseLeave += (s, e) =>
                    {
                        if (row == _voiceRowPlaying) return;
                        row.Background = Brushes.White;
                        row.BorderBrush = C("#DFE3E9");
                    };

                    var edit = VoiceIconButton("\uE70F", "改名",
                        Color.FromRgb(0x8A, 0x93, 0xA3), Color.FromRgb(0x25, 0x63, 0xAB),
                        Colors.Transparent, Color.FromRgb(0xEA, 0xF1, 0xFA),
                        () => { _voiceEditing = idx; RefreshVoiceList(); });
                    edit.Margin = new Thickness(2, 0, 0, 0);
                    acts.Children.Add(edit);

                    var del = VoiceIconButton("\uE74D", "删除这一条",
                        Color.FromRgb(0x8A, 0x93, 0xA3), Color.FromRgb(0xC4, 0x2B, 0x1C),
                        Colors.Transparent, Color.FromRgb(0xFD, 0xEC, 0xEA),
                        () => DeleteVoice(idx));
                    del.Margin = new Thickness(2, 0, 0, 0);
                    acts.Children.Add(del);

                    var play = VoiceIconButton("\uE768", "试听",
                        Color.FromRgb(0x25, 0x63, 0xAB), Color.FromRgb(0x25, 0x63, 0xAB),
                        Color.FromRgb(0xF1, 0xF5, 0xFC), Color.FromRgb(0xE3, 0xEE, 0xFB), act);
                    play.Margin = new Thickness(2, 0, 0, 0);
                    acts.Children.Add(play);
                }
                row.Child = g;
                VoiceListPanel.Children.Add(row);
                if (shown >= 400) break;
            }
            if (LblVoiceCount != null)
                LblVoiceCount.Text = q == ""
                    ? "共 " + VoiceLines.Count + " 条"
                    : "匹配 " + shown + " / " + VoiceLines.Count + " 条";
            if (VoiceScroller != null)
                VoiceScroller.Height = VoiceLines.Count == 0
                    ? 96 : Math.Max(150, Math.Min(260, VoiceLines.Count * 46 + 10));
            if (shown == 0)
            {
                TextBlock tip = Txt(VoiceLines.Count == 0
                    ? "当前角色还没有语音。点右上角「新增语音」加一条，或在「导入」里建角色时带上语音文件夹" : "没有匹配的台词", 12, "#99000000");
                tip.HorizontalAlignment = HorizontalAlignment.Center;
                tip.Margin = new Thickness(0, VoiceLines.Count == 0 ? 26 : 8, 0, 0);
                VoiceListPanel.Children.Add(tip);
            }
        }

        // 行内动作键：30×30 图标砖，悬停才出底色；危险操作（删除）用红
        static Button VoiceIconButton(string glyph, string tip, Color idle, Color hot, Color idleBg, Color hotBg, Action act)
        {
            var ico = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Foreground = new SolidColorBrush(idle),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Button
            {
                Content = ico, Width = 30, Height = 30, Padding = new Thickness(0),
                Background = new SolidColorBrush(idleBg), BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip,
                FocusVisualStyle = (Style)Win.FindResource("FocusRing"),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true, UseLayoutRounding = true
            };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);

            var tpl = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
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
                b.Background = new SolidColorBrush(down ? hot : over ? hotBg : idleBg);
                b.BorderBrush = new SolidColorBrush(down ? hot : Colors.Transparent);
                ico.Foreground = new SolidColorBrush(down ? Colors.White : over ? hot : idle);
            };
            b.MouseEnter += (s, e) => { over = true; sync(); };
            b.MouseLeave += (s, e) => { over = false; down = false; sync(); };
            b.PreviewMouseLeftButtonDown += (s, e) => { down = true; sync(); };
            b.PreviewMouseLeftButtonUp += (s, e) => { down = false; sync(); };
            b.LostMouseCapture += (s, e) => { if (!down) return; down = false; sync(); };
            b.Click += (s, e) => act();
            return b;
        }

        static void CancelVoiceEdit() { _voiceEditing = -1; }

        static void CommitVoiceRename(int index, string text)
        {
            if (index < 0 || index >= VoiceLines.Count) { CancelVoiceEdit(); RefreshVoiceList(); return; }
            string name = (text ?? "").Trim();
            if (name == "") { LblVoiceHint.Text = "名字不能为空"; return; }
            VoiceLine v = VoiceLines[index];
            if (name == v.Text) { CancelVoiceEdit(); RefreshVoiceList(); return; }
            string err;
            if (!EditVoiceFileLine(v, name, false, out err)) { LblVoiceHint.Text = err; return; }
            v.Text = name;
            CancelVoiceEdit();
            RefreshVoiceList();
            LblVoiceHint.Text = "已改名：" + name;
            SyncPetVoices();
        }

        static void DeleteVoice(int index)
        {
            if (index < 0 || index >= VoiceLines.Count) return;
            VoiceLine v = VoiceLines[index];
            string shortText = v.Text.Length > 24 ? v.Text.Substring(0, 24) + "…" : v.Text;
            if (!Dlg.Ask("删除「" + shortText + "」？",
                "只会从台词库移除这一条，音频文件仍留在角色包里。", "删除", "取消", true)) return;
            string err;
            if (!EditVoiceFileLine(v, null, true, out err)) { LblVoiceHint.Text = err; return; }
            VoiceLines.RemoveAt(index);
            CancelVoiceEdit();
            PersistVoiceCount();
            RefreshVoiceList();
            LblVoiceHint.Text = "已删除 1 条语音";
            SyncPetVoices();
        }

        // 改名 / 删除只动匹配到的那一行，清单里其它行原样保留
        static bool EditVoiceFileLine(VoiceLine v, string newText, bool remove, out string error)
        {
            error = "";
            CharEntry ce = SelectedChar;
            string file = VoiceFileOf(ce);
            if (file == "" || !File.Exists(file)) { error = "没找到角色的台词清单"; return false; }
            string baseDir = ce != null && !string.IsNullOrEmpty(ce.Dir) ? ce.Dir : Here;
            try
            {
                var lines = new List<string>(File.ReadAllLines(file));
                int hit = QCore.FindVoiceLine(lines.ToArray(), v.Text, v.Path, baseDir);
                if (hit < 0) { error = "没找到对应的台词行，清单可能被外部改过"; return false; }
                if (remove) lines.RemoveAt(hit);
                else lines[hit] = QCore.RenameVoiceLine(lines[hit], newText);
                File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                LogErr("voicefile", ex);
                error = "写台词清单失败：" + ex.Message;
                return false;
            }
        }

        static string VoiceFileOf(CharEntry ce)
        {
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) return "";
            if (!string.IsNullOrEmpty(ce.Voices)) return ce.Voices;
            return Path.Combine(ce.Dir, "音效", "voices.tsv");
        }

        // 角色包里的 char.json 有个 voices 计数，首页 / 导入页都读它
        static void PersistVoiceCount()
        {
            CharEntry ce = SelectedChar;
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) return;
            ce.VoiceCount = VoiceLines.Count;
            try
            {
                Dictionary<string, string> m = ReadManifest(ce.Dir);
                if (!m.ContainsKey("voicelines") || m["voicelines"] == "") m["voicelines"] = "音效/voices.tsv";
                m["voices"] = VoiceLines.Count.ToString(CultureInfo.InvariantCulture);
                WriteManifest(ce.Dir, m);
            }
            catch (Exception ex) { LogErr("voicecount", ex); }
            RefreshPageBar(_curPage);
            RefreshHome();
            RefreshImport();
        }

        // Pet.exe 只在启动时读台词清单：改完顺手重启一次，连续改几条只重启一次
        static void SyncPetVoices()
        {
            if (!_showPetSel) return;
            if (_voiceSyncTimer == null)
            {
                _voiceSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _voiceSyncTimer.Tick += (s, e) =>
                {
                    _voiceSyncTimer.Stop();
                    System.Threading.Tasks.Task.Run((Action)(() => RestartPet()));
                };
            }
            _voiceSyncTimer.Stop();
            _voiceSyncTimer.Start();
        }

        static void AddVoices()
        {
            CharEntry ce = SelectedChar;
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) { LblVoiceHint.Text = "先选一个角色包，再新增语音"; return; }
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选要加入台词库的音频",
                Multiselect = true,
                Filter = "音频 (*.wav;*.mp3;*.m4a;*.ogg)|*.wav;*.mp3;*.m4a;*.ogg|所有文件|*.*"
            };
            if (d.ShowDialog() != true) return;
            AddVoiceFiles(d.FileNames);
        }

        // 新音频按台词名复制进 音效\voices；非 wav 先转 wav（和建角色时同一条路）
        static void AddVoiceFiles(string[] files)
        {
            CharEntry ce = SelectedChar;
            if (ce == null || string.IsNullOrEmpty(ce.Dir)) { LblVoiceHint.Text = "先选一个角色包，再新增语音"; return; }
            if (files == null || files.Length == 0) return;
            string vdir = Path.Combine(ce.Dir, "音效", "voices");
            string vfile = VoiceFileOf(ce);
            var rows = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            try
            {
                if (Directory.Exists(vdir))
                    foreach (string f in Directory.GetFiles(vdir))
                        used.Add(Path.GetFileNameWithoutExtension(f));
                Directory.CreateDirectory(vdir);
                var sorted = new List<string>(files);
                sorted.Sort(StringComparer.CurrentCulture);
                foreach (string f in sorted)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".mp3" && ext != ".m4a" && ext != ".ogg") { skipped++; continue; }
                    string text = Path.GetFileNameWithoutExtension(f).Trim();
                    if (text == "") { skipped++; continue; }
                    string stem = SafeFile(text);
                    if (stem == "") stem = "voice";
                    string uniq = stem;
                    for (int k = 2; used.Contains(uniq); k++) uniq = stem + "_" + k;
                    string dst = Path.Combine(vdir, uniq + ".wav");
                    bool good;
                    if (ext == ".wav")
                    {
                        try
                        {
                            if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                                File.Copy(f, dst, true);
                            good = File.Exists(dst);
                        }
                        catch (Exception ex) { LogErr("addvoice", ex); good = false; }
                    }
                    else good = ConvertToWav(f, dst);
                    if (good && File.Exists(dst))
                    {
                        used.Add(uniq);
                        rows.Add(text + "\t音效/voices/" + uniq + ".wav");
                    }
                    else skipped++;
                }
                if (rows.Count > 0)
                {
                    string dir = Path.GetDirectoryName(vfile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string existing = File.Exists(vfile) ? File.ReadAllText(vfile) : "";
                    string lead = existing.Length > 0 && !existing.EndsWith("\n") ? "\r\n" : "";
                    File.AppendAllText(vfile, lead + string.Join("\r\n", rows.ToArray()) + "\r\n",
                        new UTF8Encoding(false));
                    if (string.IsNullOrEmpty(ce.Voices)) ce.Voices = vfile;
                    LoadVoiceLibrary();
                    PersistVoiceCount();
                    LblVoiceHint.Text = "已新增 " + rows.Count + " 条语音"
                        + (skipped > 0 ? "，跳过 " + skipped + " 条（格式不支持或缺少 ffmpeg）" : "");
                    SyncPetVoices();
                }
                else
                {
                    LblVoiceHint.Text = "没有新增（格式不支持？mp3 / m4a / ogg 需要 ffmpeg）";
                }
            }
            catch (Exception ex)
            {
                LogErr("addvoices", ex);
                LblVoiceHint.Text = "新增失败：" + ex.Message;
            }
        }

        static void PlayVoiceFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                if (PreviewPlayer == null) PreviewPlayer = new MediaPlayer();
                PreviewPlayer.Close();
                PreviewPlayer.Open(new Uri(path));
                PreviewPlayer.Volume = SldVol.Value / 100.0;
                PreviewPlayer.Play();
            }
            catch (Exception ex) { LogErr("playvoice", ex); }
        }

        static void PlayRandomLine()
        {
            if (VoiceLines.Count == 0) return;
            VoiceLine v = VoiceLines[_rndUI.Next(VoiceLines.Count)];
            PlayVoiceFile(v.Path);
            LblVoiceHint.Text = "随机来一句：" + v.Text;
        }

        // 试听：从**当前角色的语音库**里随机抽一条来播（和桌宠自己点出来的同源）
        static void PreviewVoice()
        {
            try
            {
                if (VoiceLines.Count == 0) LoadVoiceLibrary();
                if (VoiceLines.Count == 0)
                {
                    LblVoiceHint.Text = "当前角色没有语音可试听";
                    return;
                }
                VoiceLine v = VoiceLines[_rndUI.Next(VoiceLines.Count)];
                PlayVoiceFile(v.Path);
                LblVoiceHint.Text = "正在试听：" + v.Text;
            }
            catch (Exception ex) { LogErr("preview", ex); }
        }

        // ---------------- 保存 ----------------
        // 这里原来的 FindPython() 已经删掉：动画生成改成进程内实现（SpriteGen.cs），
        // 设置面板对 Python 不再有任何依赖。

}
