using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

static partial class Settings
{
        // 把本机已有的「按天统计文件」列进日期选择器（含今天），没有历史时只有今天可选
        // 扫出所有真实有归档记录的日期（+ 今天），新 -> 旧。
        // 存进 _statDays 供列表用；不直接改当前选择，避免切页回来时被强行拉回今天。
        static List<DateTime> _statDays = new List<DateTime>();

        static Dictionary<string, bool> _statHasData = new Dictionary<string, bool>();

        static void RefreshStatDates()
        {
            var dates = new List<DateTime>();
            try
            {
                foreach (string f in Directory.GetFiles(Here, "pet-stats-*.txt"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (name.Length < 10) continue;
                    string d = name.Substring(name.Length - 10);
                    DateTime dt;
                    if (DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                        dates.Add(dt);
                }
            }
            catch (Exception ex) { LogErr("?", ex); }
            DateTime todayDt = DateTime.Today;

            // 列表要同时包含「最近 N 天」和「所有有归档的历史日期」：
            //  - 有归档的：可点，能看真实数据
            //  - 没归档的：置灰不可点（用户明确要求显示出来，而不是直接不列）
            // 近 N 天保证日历是连续的（不会 10/18 之后直接跳到 8/01，看不出中间断了多久）
            const int recentDays = 30;
            var all = new List<DateTime>();
            var seen = new HashSet<string>();
            for (int i = 0; i < recentDays; i++)
            {
                DateTime d = todayDt.AddDays(-i);
                if (seen.Add(d.ToString("yyyy-MM-dd"))) all.Add(d);
            }
            foreach (var d in dates)
                if (seen.Add(d.ToString("yyyy-MM-dd"))) all.Add(d);
            all.Sort(); all.Reverse();                            // 新 -> 旧
            _statDays = all;
            // 记录哪些日期真有归档（用于置灰不可点的那些）
            _statHasData.Clear();
            foreach (var d in dates) _statHasData[d.ToString("yyyy-MM-dd")] = true;
            _statHasData[todayDt.ToString("yyyy-MM-dd")] = true;   // 今天一定有（实时文件）

            if (_dateNote != null)
                _dateNote.Text = dates.Count > 1 ? "共 " + dates.Count + " 天可查" : "今天还没有历史记录";
            FillDateCombo();
        }

        static string StatDateLabel(DateTime d)
        {
            bool isToday = d.Date == DateTime.Today;
            string wk = WeekCn(d.DayOfWeek);
            // 往年的日期补上年份：只写「8月1日」会和今年混，看不出是哪一年
            string md = d.Year == DateTime.Today.Year ? d.ToString("M月d日") : d.ToString("yyyy年M月d日");
            return (isToday ? "今天 │ " : "") + md + " " + wk;
        }

        static string WeekCn(DayOfWeek w)
        {
            switch (w)
            {
                case DayOfWeek.Sunday: return "周日";
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                default: return "周六";
            }
        }

        // 填下拉列表。选中项 = 当前查看的日期（今天时 _statViewDate 为空）
        // 列表项一律用 ComboBoxItem：选中高亮（浅主题底 + 左侧 3px 条 + 加粗）由全局
        // ComboBoxItem 样式负责，这里只管「有没有记录」——有记录才可选。
        static void FillDateCombo()
        {
            if (_dateCombo == null) return;
            string view = string.IsNullOrEmpty(_statViewDate) ? DateTime.Now.ToString("yyyy-MM-dd") : _statViewDate;
            _dateFilling = true;
            _dateCombo.Items.Clear();
            int selIdx = -1;
            foreach (var d in _statDays)
            {
                string key = d.ToString("yyyy-MM-dd");
                bool hasData = _statHasData.ContainsKey(key);
                var item = new ComboBoxItem
                {
                    Content = StatDateLabel(d) + (hasData ? "" : "  │  无记录"),
                    Tag = key,
                    IsEnabled = hasData      // 没归档的：置灰。WPF 不会选中禁用项，鼠标键盘都绕开
                };
                if (!hasData)
                {
                    item.ToolTip = "这一天没有统计记录";
                    ToolTipService.SetShowOnDisabled(item, true);   // 禁用项默认不弹提示，得显式开
                }
                if (hasData && key == view) selIdx = _dateCombo.Items.Count;
                _dateCombo.Items.Add(item);
            }
            _dateCombo.SelectedIndex = selIdx >= 0 ? selIdx : 0;   // 兜底选第一个（今天）
            _dateFilling = false;
        }

        // 统一切换查看的日期。切页重建时不会调它，所以用户的选择能跨页保留。
        static void ApplyStatDate(string key)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            _statViewDate = (key == today) ? "" : key;      // 空串 = 今天（走实时刷新）
            UpdateStats(true);
        }

        static void BuildStatsPage()
        {
            if (PageStats == null) return;
            PageStats.Children.Clear();

            // 标题 / 说明：与其它页共用同一个页头组件，间距不会走形
            AddPageHead(PageStats, "// STATS", "使用统计", "只存本机，不联网",
                        "数据全部写在 pet-stats.txt，不上传。");
            PageStats.Children.Add(new Border { Height = 20 });

            // ---- 日期选择：回看历史 ----
            var dateBar = new Grid();
            dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dateLbl = Txt("查看日期", 13, "#E4000000", true);
            dateLbl.VerticalAlignment = VerticalAlignment.Center;
            // 不用 DatePicker：它的下拉是「月/日网格」，而这里只能选「有记录的日期」，
            // 用日历反而要在一堆空白格里翻找。列表只列真实日期，没记录的置灰不可点。
            // 也不用自绘 Button+Popup 了：直接复用全局 ComboBox 样式，跟「主动冒泡」那一个是同一套
            //（1px 描边 + 右侧 28px 箭头区、悬停只换线色、展开才上主题色并翻箭头）。
            _dateCombo = new ComboBox
            {
                Width = 190, FontSize = 13, Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _dateCombo.SelectionChanged += (s, e) =>
            {
                if (_dateFilling) return;              // 重建列表时赋的 SelectedIndex 不算用户选择
                var it = _dateCombo.SelectedItem as ComboBoxItem;
                string key = it == null ? null : it.Tag as string;
                if (string.IsNullOrEmpty(key)) return;   // 置灰项：IsEnabled=false，本来就点不到
                ApplyStatDate(key);
            };
            HookComboOneClick(_dateCombo);              // 和其它下拉框一致：点框内任意处就展开
            Grid.SetColumn(dateLbl, 0); Grid.SetColumn(_dateCombo, 1);
            _dateNote = Txt("", 12, "#99000000");
            _dateNote.VerticalAlignment = VerticalAlignment.Center;
            _dateNote.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(_dateNote, 2);
            dateBar.Children.Add(dateLbl); dateBar.Children.Add(_dateCombo); dateBar.Children.Add(_dateNote);

            PageStats.Children.Add(dateBar);
            PageStats.Children.Add(new Border { Height = 12 });
            RefreshStatDates();

            // ---- 今日概览（带实时角标）----
            var wrap = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LblStatDay = Txt("今天 │ —", 14, "#E4000000", true);
            head.Children.Add(LblStatDay);
            var live = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = C("#12A150");
            var pulse = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            var liveTxt = Txt("实时刷新", 12, "#0F7B43");
            _liveTxt = liveTxt;
            _liveDot = dot;
            liveTxt.VerticalAlignment = VerticalAlignment.Center;
            live.Children.Add(dot); live.Children.Add(liveTxt);
            Grid.SetColumn(live, 1); head.Children.Add(live);
            wrap.Children.Add(head);
            wrap.Children.Add(new Border { Height = 12 });

            var g3 = new Grid();
            for (int c = 0; c < 3; c++) g3.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels = { "屏幕使用", "活跃", "挂机" };
            LblStatScreen = Txt("—", 22, "#E4000000", true);
            LblStatActive = Txt("—", 22, "#E4000000", true);
            LblStatIdle = Txt("—", 22, "#E4000000", true);
            TextBlock[] vals = { LblStatScreen, LblStatActive, LblStatIdle };
            for (int c = 0; c < 3; c++)
            {
                var sp = new StackPanel();
                sp.Children.Add(Txt(labels[c], 12, "#99000000"));
                sp.Children.Add(vals[c]);
                Grid.SetColumn(sp, c);
                g3.Children.Add(sp);
            }
            wrap.Children.Add(g3);
            wrap.Children.Add(new Border { Height = 10 });
            LblStatUpdated = Txt("—", 11, "#99000000");
            wrap.Children.Add(LblStatUpdated);
            PageStats.Children.Add(Card(wrap));

            // ---- 按键使用（热力图 / 树图 / 精修条，见 KeyStats.cs）----
            PageStats.Children.Add(Card(BuildKeyCard()));

            // ---- 角色陪伴（方格图：面积 = 今日时长，和下面「软件使用」同一套设计）----
            var spChar = new StackPanel();
            var chTitle = Txt("角色陪伴", 14, "#E4000000", true);
            chTitle.ToolTip = "只统计当天：数据来自 pet-stats.txt 的 char= 行，清空统计即归零。";
            spChar.Children.Add(chTitle);
            spChar.Children.Add(Txt("面积 = 今日陪伴时长 │ 悬停看占比", 12, "#99000000"));
            spChar.Children.Add(new Border { Height = 8 });
            spChar.Children.Add(new Border
            {
                Background = C("#FDFEFF"), BorderBrush = C("#EEF0F4"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0), Padding = new Thickness(4), Child = MakeCharMap()
            });
            // 头部之外的那几个：一行字收着，不去抢面积（面积只代表真实时长）
            _charTail = Txt("", 11.5, "#99000000");
            _charTail.Margin = new Thickness(2, 7, 0, 0);
            _charTail.Visibility = Visibility.Collapsed;
            spChar.Children.Add(_charTail);
            PageStats.Children.Add(Card(spChar));

            // ---- 软件使用（方格图，面积 = 时长）----
            var spApp = new StackPanel();
            spApp.Children.Add(Txt("软件使用", 14, "#E4000000", true));
            spApp.Children.Add(Txt("面积 = 使用时长 │ 悬停看占比", 12, "#99000000"));
            spApp.Children.Add(new Border { Height = 8 });
            spApp.Children.Add(new Border
            {
                Background = C("#FDFEFF"), BorderBrush = C("#EEF0F4"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0), Padding = new Thickness(4), Child = MakeAppMap()
            });
            PageStats.Children.Add(Card(spApp));

            // ---- 操作 ----
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var bRef = new Button { Content = "立即刷新", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
            bRef.Click += (s, e) => UpdateStats(true);
            var bExp = new Button { Content = "导出 CSV", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
            bExp.Click += (s, e) => ExportStatsCsv();
            var bClr = new Button { Content = "清空统计", Style = (Style)Win.FindResource("SecondaryButton") };
            bClr.Click += (s, e) =>
            {
                if (!Dlg.Ask("清空今天的统计？", "屏幕时长、软件使用、按键次数和角色今日时长都会归零（角色累计时长保留）。", "清空", "算了", true)) return;
                try { File.WriteAllText(Path.Combine(Here, "pet-stats-reset.txt"), DateTime.Now.ToString("s")); }
                catch (Exception ex) { LogErr("clrstats", ex); }
                // 桌宠 1 秒内会看到重置请求；这里等它清完再刷新界面
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                t.Tick += (s2, e2) => { t.Stop(); UpdateStats(true); };
                t.Start();
            };
            btns.Children.Add(bRef); btns.Children.Add(bExp); btns.Children.Add(bClr);
            PageStats.Children.Add(btns);

            UpdateStats(true);
        }

        // 一天的统计快照。解析逻辑从 UpdateStats 里抽出来，导出 CSV 时复用同一份，
        // 免得两处解析规则各写一遍、哪天加了字段就只对了一半。
        class StatDay
        {
            public string Day = "—";
            public int Screen, Active, Idle;
            public Dictionary<string, int> Apps = new Dictionary<string, int>();
            public Dictionary<int, int> Keys = new Dictionary<int, int>();
            public Dictionary<string, int> CharDay = new Dictionary<string, int>();
        }

        static StatDay LoadStatDay(string path)
        {
            var d = new StatDay();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return d;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "day") d.Day = v;
                    else if (k == "screen") int.TryParse(v, out d.Screen);
                    else if (k == "active") int.TryParse(v, out d.Active);
                    else if (k == "idle") int.TryParse(v, out d.Idle);
                    else if (k == "app") { int b = v.LastIndexOf('|'); if (b > 0) { int n; if (int.TryParse(v.Substring(b + 1), out n)) d.Apps[v.Substring(0, b)] = n; } }
                    else if (k == "key") { int b = v.LastIndexOf('|'); if (b > 0) { int vk, n; if (int.TryParse(v.Substring(0, b), out vk) && int.TryParse(v.Substring(b + 1), out n)) d.Keys[vk] = n; } }
                    else if (k == "char") { int b = v.LastIndexOf('|'); if (b > 0) { int n; if (int.TryParse(v.Substring(b + 1), out n)) d.CharDay[v.Substring(0, b)] = n; } }
                }
            }
            catch (Exception ex) { LogErr("LoadStatDay", ex); }
            return d;
        }

        // 当前查看的那一天：今天读 pet-stats.txt，历史读 pet-stats-<日期>.txt
        static string StatFilePathOf(string viewDay)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            bool isToday = string.IsNullOrEmpty(viewDay) || viewDay == today;
            return isToday ? Path.Combine(Here, "pet-stats.txt")
                           : Path.Combine(Here, "pet-stats-" + viewDay + ".txt");
        }

        // ---------------- 导出 CSV ----------------
        // 统计此前只能看不能带走：加了导出，按键/软件/角色陪伴三张图背后的数据都能拿去
        // 自己的表格里继续算。CSV 用 UTF-8 BOM，Excel 双击直接开不乱码。
        static void ExportStatsCsv()
        {
            try
            {
                string viewDay = _statViewDate;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                bool isToday = string.IsNullOrEmpty(viewDay) || viewDay == today;
                string day = isToday ? today : viewDay;
                StatDay d = LoadStatDay(StatFilePathOf(viewDay));

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = "qpet-stats-" + day + ".csv",
                    DefaultExt = ".csv",
                    Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
                    InitialDirectory = Here
                };
                if (sfd.ShowDialog(Win) != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("日期,屏幕使用(秒),活跃(秒),挂机(秒)");
                sb.AppendLine(QCore.CsvRow(day, d.Screen.ToString(), d.Active.ToString(), d.Idle.ToString()));
                sb.AppendLine();
                sb.AppendLine("软件,使用时长(秒),可读时长");
                foreach (var kv in d.Apps.OrderByDescending(x => x.Value))
                    sb.AppendLine(QCore.CsvRow(kv.Key, kv.Value.ToString(), FmtSec(kv.Value)));
                sb.AppendLine();
                sb.AppendLine("按键,次数");
                foreach (var kv in d.Keys.OrderByDescending(x => x.Value))
                    sb.AppendLine(QCore.CsvRow(VkName(kv.Key), kv.Value.ToString()));
                sb.AppendLine();
                sb.AppendLine("角色,今日陪伴(秒),可读时长");
                foreach (var kv in d.CharDay.OrderByDescending(x => x.Value))
                    sb.AppendLine(QCore.CsvRow(kv.Key, kv.Value.ToString(), FmtSec(kv.Value)));
                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
                ShowSavedNote("已导出 " + Path.GetFileName(sfd.FileName));
            }
            catch (Exception ex) { LogErr("ExportStatsCsv", ex); Dlg.Info("导出失败", ex.Message, "好"); }
        }

        static void UpdateStats(bool rebuild)
        {
            if (PageStats == null) return;
            string today0 = DateTime.Now.ToString("yyyy-MM-dd");
            string viewDay0 = _statViewDate;
            bool isToday = string.IsNullOrEmpty(viewDay0) || viewDay0 == today0;
            StatDay st = LoadStatDay(StatFilePathOf(viewDay0));
            int screen = st.Screen, active = st.Active, idle = st.Idle;
            var apps = st.Apps;
            var keys = st.Keys;
            var charDay = st.CharDay;
            string day = st.Day;

            LblStatDay.Text = (isToday ? "今天 │ " : "历史 │ ") + day;
            LblStatScreen.Text = FmtSec(screen);
            LblStatActive.Text = FmtSec(active);
            LblStatIdle.Text = FmtSec(idle);
            if (_liveTxt != null)
            {
                if (isToday) { _liveTxt.Text = "每 4 秒刷新"; _liveTxt.Foreground = C("#0F7B43"); if (_liveDot != null) _liveDot.Visibility = Visibility.Visible; }
                else { _liveTxt.Text = "历史记录（只读）"; _liveTxt.Foreground = C("#99000000"); if (_liveDot != null) _liveDot.Visibility = Visibility.Collapsed; }
            }
            LblStatUpdated.Text = isToday
                ? "更新于 " + DateTime.Now.ToString("HH:mm:ss") + "（桌宠每 4 秒落盘一次）"
                : "历史快照 │ 该日已归档，可切回「今天」看实时数据";

            // 角色陪伴（方格图：面积 = 当天时长，铺格 / 上色都在 CharMap.cs 里）
            UpdateCharMap(charDay, isToday, rebuild);

            // 软件（方格图：面积 = 时长，见 AppTreemap.cs）
            UpdateAppMap(apps, rebuild);

            // 按键（三种可视化各自的刷新逻辑在 KeyStats.cs 的 UpdateKeyView 里）
            UpdateKeyView(keys);
        }

        static void StartLiveStats()
        {
            if (_liveTimer == null)
            {
                // 4 秒一更：桌宠本来也是 4 秒才落盘一次，1 秒读盘只会读到同一份数据，
                // 却把两张方格图反复重铺（数字每秒跳一次位置，眼睛没法盯）。
                // 现在整页跟着落盘节奏走，两张图的面积和读数同一步长。
                _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
                _liveTimer.Tick += (s, e) =>
                {
                    try { if (PageStats != null && PageStats.Visibility == Visibility.Visible) UpdateStats(false); }
                    catch (Exception ex) { LogErr("live", ex); }
                };
            }
            _liveTimer.Start();
        }

        static void StopLiveStats()
        {
            if (_liveTimer != null) _liveTimer.Stop();
        }

        static void StartLivePos()
        {
            if (_livePosTimer == null)
            {
                _livePosTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _livePosTimer.Tick += (s, e) =>
                {
                    try { SyncLivePos(); }
                    catch (Exception ex) { LogErr("livepos", ex); }
                };
            }
            SyncLivePos();
            _livePosTimer.Start();
        }

        static void StopLivePos()
        {
            if (_livePosTimer != null) _livePosTimer.Stop();
            _livePosCache = "";
        }

        static void SyncLivePos()
        {
            if (PageHome == null || PageHome.Visibility != Visibility.Visible) return;
            string pos = HomePosText();
            if (pos == _livePosCache) return;          // 没动就不碰控件，免得每 400ms 抖一次布局
            _livePosCache = pos;
            if (TxtHomePos != null) TxtHomePos.Text = pos;
            if (TxtRunPos != null) TxtRunPos.Text = pos;
        }

        // 让动画时钟真的往前走：Thread.Sleep 不解 dispatcher，动画值不会推进（读到的永远是起点）。
        // 每 8ms 用 Background 优先级泵一次 = 把更高优先级的 Render 也跑一遍。
        static void PumpFor(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                System.Threading.Thread.Sleep(8);
                try
                {
                    Win.Dispatcher.Invoke(new Action(() => { }),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception) { break; }
            }
        }

}
