using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

static partial class Settings
{
        static void BuildAboutPage()
        {
            if (PageAbout == null) return;
            PageAbout.Children.Clear();

            AddPageHead(PageAbout, "// ABOUT", "关于", "版本、目录与链接");
            PageAbout.Children.Add(new Border { Height = 20 });

            // ---- 版本 ----
            string vSet = ExeVersion("PetSettings.exe");
            string vPet = ExeVersion("Pet.exe");
            var spVer = new StackPanel();
            spVer.Children.Add(new TextBlock { Text = "版本", Style = (Style)Win.FindResource("H2") });
            spVer.Children.Add(VersionRow("设置面板", "PetSettings.exe", vSet, BuiltAt("PetSettings.exe")));
            spVer.Children.Add(new Border { Style = (Style)Win.FindResource("Divider") });
            spVer.Children.Add(VersionRow("桌宠本体", "Pet.exe", vPet, BuiltAt("Pet.exe")));
            if (vSet != vPet || vSet == "未找到" || vPet == "未找到")
            {
                var warn = Txt("两个 exe 版本不一致，重新运行 build.cmd 可对齐", 11, "#C42B1C");
                warn.Margin = new Thickness(0, 10, 0, 0);
                warn.ToolTip = "根目录的 Pet.exe 与 PetSettings.exe 应当来自同一次编译。\n不一致时，界面里的新功能可能对桌宠本体不生效。";
                spVer.Children.Add(warn);
            }
            PageAbout.Children.Add(Card(spVer));

            // ---- 上次异常 ----
            // 桌宠崩了会往同目录写 pet-crash.log，平时没人看 ⇒ 用户只觉得「她不见了」。
            // 这里把它捞出来：看得见才知道去查。
            var spCrash = LastCrashCard();
            if (spCrash != null) PageAbout.Children.Add(spCrash);

            // ---- 程序目录 ----
            var spDir = new StackPanel();
            spDir.Children.Add(new TextBlock { Text = "程序目录", Style = (Style)Win.FindResource("H2") });
            var tipDir = Txt("配置、角色包、统计都在这里", 12, "#99000000");
            tipDir.Margin = new Thickness(0, 3, 0, 0);
            spDir.Children.Add(tipDir);
            var tbPath = new TextBox
            {
                Text = Here, IsReadOnly = true, Height = 32, Margin = new Thickness(0, 12, 0, 0),
                ToolTip = "可以选中复制。整个文件夹能直接拷到别的电脑或 U 盘，配置全是相对路径。"
            };
            spDir.Children.Add(tbPath);
            var btnDir = new Button
            {
                Content = "打开文件夹", Style = (Style)Win.FindResource("SecondaryButton"),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0)
            };
            btnDir.Click += (s, e) => OpenExternal(Here);
            spDir.Children.Add(btnDir);
            PageAbout.Children.Add(Card(spDir));

            // ---- 链接 ----
            var spLink = new StackPanel();
            spLink.Children.Add(new TextBlock { Text = "链接", Style = (Style)Win.FindResource("H2") });
            var linkTip = Txt("遇到问题先看仓库里的说明", 12, "#99000000");
            linkTip.Margin = new Thickness(0, 3, 0, 0);
            spLink.Children.Add(linkTip);
            var linkRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            AddLinkButton(linkRow, "GitHub 仓库", RepoUrl);
            AddLinkButton(linkRow, "在线试玩", WebUrl);
            AddLinkButton(linkRow, "查看许可证", RepoUrl + "/blob/main/LICENSE");
            spLink.Children.Add(linkRow);
            PageAbout.Children.Add(Card(spLink));

            // ---- 说明 ----
            var spAbout = new StackPanel();
            spAbout.Children.Add(new TextBlock { Text = "关于本程序", Style = (Style)Win.FindResource("H2") });
            var intro = Txt("Q Pet 是本地桌宠：不联网、无账号，也不需要安装。", 13, "#E4000000");
            intro.Margin = new Thickness(0, 8, 0, 0);
            spAbout.Children.Add(intro);
            var privacy = Txt("使用统计只写在本目录的 txt 里。", 12, "#99000000");
            privacy.Margin = new Thickness(0, 4, 0, 0);
            spAbout.Children.Add(privacy);
            var copy = Txt("© 2026 Q Pet contributors　│　代码 MIT，角色素材版权归原权利方", 11, "#99000000");
            copy.Margin = new Thickness(0, 10, 0, 0);
            spAbout.Children.Add(copy);
            PageAbout.Children.Add(Card(spAbout));
        }

        // 上次异常卡：pet-crash.log（桌宠进程崩溃）/ pet-error.log（可恢复的异常，各一处只记一次）
        // 都没有就返回 null（不占版面）。
        static Border LastCrashCard()
        {
            try
            {
                string crash = Path.Combine(Here, "pet-crash.log");
                string err = Path.Combine(Here, "pet-error.log");
                bool hasCrash = File.Exists(crash), hasErr = File.Exists(err);
                if (!hasCrash && !hasErr) return null;

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "上次异常", Style = (Style)Win.FindResource("H2") });
                string when = "";
                string head = "";
                if (hasCrash)
                {
                    try
                    {
                        string[] all = File.ReadAllLines(crash);
                        when = File.GetLastWriteTime(crash).ToString("yyyy-MM-dd HH:mm");
                        head = all.Length > 0 ? all[0] : "";
                        // 第一行是时间，第二行起才是异常；取第一句有内容的
                        for (int i = 1; i < all.Length && i < 6; i++)
                        {
                            if (all[i].Trim().Length == 0) continue;
                            head = all[i].Trim();
                            break;
                        }
                    }
                    catch (Exception ex) { LogErr("?", ex); }
                    var t1 = Txt("桌宠上次是异常退出的（" + when + "）", 13, "#C42B1C");
                    t1.Margin = new Thickness(0, 8, 0, 0);
                    sp.Children.Add(t1);
                    if (head != "")
                    {
                        var t2 = Txt(head, 11, "#99000000");
                        t2.TextWrapping = TextWrapping.Wrap;
                        t2.ToolTip = head;
                        t2.Margin = new Thickness(0, 4, 0, 0);
                        sp.Children.Add(t2);
                    }
                }
                int errLines = 0;
                if (hasErr)
                {
                    try { errLines = File.ReadAllLines(err).Length; } catch (Exception) { }
                    var t3 = Txt("桌宠记录到 " + errLines + " 条可恢复错误（" + "pet-error.log" + "）", 12, "#99000000");
                    t3.Margin = new Thickness(0, 6, 0, 0);
                    sp.Children.Add(t3);
                }
                var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                var bOpen = new Button { Content = "打开日志", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
                bOpen.Click += (s, e) => OpenExternal(hasCrash ? crash : err);
                var bDel = new Button { Content = "已看过，清掉", Style = (Style)Win.FindResource("SecondaryButton") };
                bDel.Click += (s, e) =>
                {
                    try
                    {
                        if (File.Exists(crash)) File.Delete(crash);
                        if (File.Exists(err)) File.Delete(err);
                        BuildAboutPage();
                    }
                    catch (Exception ex) { LogErr("clear-crashlog", ex); }
                };
                row.Children.Add(bOpen); row.Children.Add(bDel);
                sp.Children.Add(row);
                return Card(sp);
            }
            catch (Exception ex) { LogErr("LastCrashCard", ex); return null; }
        }

        // 版本行：四列固定宽度，两个 exe 的版本号与构建时间能对齐
        static Grid VersionRow(string label, string exe, string ver, string built)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Txt(label, 12, "#99000000");
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            var n = Txt(exe, 13, "#E4000000");
            Grid.SetColumn(n, 1);
            g.Children.Add(n);
            var v = Txt(ver, 13, "#E4000000");
            Grid.SetColumn(v, 2);
            g.Children.Add(v);
            var b = Txt(built, 12, "#99000000");
            Grid.SetColumn(b, 3);
            g.Children.Add(b);
            return g;
        }

        static void AddLinkButton(Panel host, string text, string url)
        {
            var b = new Button
            {
                Content = text, Style = (Style)Win.FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 8, 8)
            };
            b.Click += (s, e) => OpenExternal(url);
            host.Children.Add(b);
        }

        // 读 exe 的文件版本：ProductVersion 在有 InformationalVersion 时就是「1.0.0」这种短格式
        static string ExeVersion(string file)
        {
            try
            {
                string p = Path.Combine(Here, file);
                if (!File.Exists(p)) return "未找到";
                FileVersionInfo vi = FileVersionInfo.GetVersionInfo(p);
                string v = vi.ProductVersion;
                if (string.IsNullOrEmpty(v)) v = vi.FileVersion;
                if (string.IsNullOrEmpty(v)) return "未知";
                // 有些 SDK 版本会把「+提交号」拼进 ProductVersion，界面上不需要
                int plus = v.IndexOf('+');
                if (plus > 0) v = v.Substring(0, plus);
                return v.Trim();
            }
            catch (Exception ex) { LogErr("about-version", ex); return "读取失败"; }
        }

        static string BuiltAt(string file)
        {
            try
            {
                string p = Path.Combine(Here, file);
                if (!File.Exists(p)) return "";
                return "构建于 " + File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception ex) { LogErr("?", ex); return ""; }
        }

        static void OpenExternal(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { LogErr("open-external", ex); Dlg.Info("打不开", ex.Message, "好"); }
        }

}
