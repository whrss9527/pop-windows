using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pop.Core;

namespace Pop;

/// 设置窗口。改动立刻生效并保存
internal sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly StackPanel ringSlots = new();
    private readonly TextBlock clipboardStats = new();
    private readonly TextBlock updateStatus = new();

    public SettingsWindow(App app)
    {
        this.app = app;
        Title = "Pop 设置";
        Width = 580;
        Height = 560;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.TextFont;
        FontSize = 13;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Pop.ico"));
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        var tabs = new TabControl { Margin = new Thickness(12) };
        tabs.Items.Add(Tab("通用", General()));
        tabs.Items.Add(Tab("圆盘", RingPage()));
        tabs.Items.Add(Tab("剪贴板", ClipboardPage()));
        tabs.Items.Add(Tab("更新", UpdatePage()));
        Content = tabs;
    }

    private AppSettings Settings => app.Settings;

    private static TabItem Tab(string title, UIElement content) => new()
    {
        Header = title,
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 16, 12) },
    };

    private static StackPanel Page() => new();

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 6),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Gray,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 6),
    };

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private void Update(Action<AppSettings> change) => app.UpdateSettings(change);

    private UIElement General()
    {
        var page = Page();
        page.Children.Add(Heading("唤起"));
        page.Children.Add(Check("在任意 App 里长按鼠标右键唤起 Pop", Settings.Enabled, v => Update(s => s.Enabled = v)));

        var holdLabel = new TextBlock { Width = 80, VerticalAlignment = VerticalAlignment.Center };
        var hold = new Slider
        {
            Minimum = AppSettings.MinHold,
            Maximum = AppSettings.MaxHold,
            TickFrequency = 50,
            IsSnapToTickEnabled = true,
            Value = Settings.HoldMilliseconds,
            Width = 260,
            VerticalAlignment = VerticalAlignment.Center,
        };
        holdLabel.Text = $"{Settings.HoldMilliseconds} 毫秒";
        hold.ValueChanged += (_, e) =>
        {
            var ms = (int)e.NewValue;
            holdLabel.Text = $"{ms} 毫秒";
            Update(s => s.HoldMilliseconds = ms);
        };
        var holdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        holdRow.Children.Add(new TextBlock { Text = "按住多久算长按", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        holdRow.Children.Add(hold);
        holdRow.Children.Add(holdLabel);
        page.Children.Add(holdRow);
        page.Children.Add(Note("比这个时间短的右键点击照常弹出系统右键菜单。"));

        bool autoStart;
        try
        {
            autoStart = AutoStart.IsEnabled;
        }
        catch (System.Security.SecurityException)
        {
            autoStart = false;
        }
        page.Children.Add(Check("开机时启动", autoStart, v =>
        {
            try
            {
                AutoStart.Set(v);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                MessageBox.Show(this, $"设置开机启动失败：{e.Message}", "Pop");
            }
        }));

        page.Children.Add(Heading("直接出结果"));
        var kinds = new StackPanel { Margin = new Thickness(20, 0, 0, 0), IsEnabled = Settings.DirectResults };
        page.Children.Add(Check("选中这些内容时直接弹出结果卡片，不弹圆盘", Settings.DirectResults, v =>
        {
            kinds.IsEnabled = v;
            Update(s => s.DirectResults = v);
        }));
        foreach (var (name, _, title) in DirectResults.Kinds)
        {
            kinds.Children.Add(Check(title, Settings.DirectKinds.Contains(name, StringComparer.OrdinalIgnoreCase), v => Update(s =>
            {
                s.DirectKinds.RemoveAll(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (v) s.DirectKinds.Add(name);
            })));
        }
        page.Children.Add(kinds);
        page.Children.Add(Note("没勾选的内容照常弹出圆盘，结果可以从「全部功能」里打开。"));
        return page;
    }

    private UIElement RingPage()
    {
        var page = Page();
        page.Children.Add(Heading("格子数"));
        var count = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        for (var n = RingItems.MinSlots; n <= RingItems.MaxSlots; n++) count.Items.Add($"{n} 格");
        count.SelectedIndex = Settings.RingSlots.Count - RingItems.MinSlots;
        count.SelectionChanged += (_, _) =>
        {
            var n = count.SelectedIndex + RingItems.MinSlots;
            Update(s =>
            {
                var slots = s.RingSlots.Take(n).ToList();
                // 多出来的格子从默认的里面挑还没用上的
                foreach (var id in RingItems.DefaultIds.Concat(Actions.List.Select(a => a.Id)))
                {
                    if (slots.Count >= n) break;
                    if (!slots.Contains(id)) slots.Add(id);
                }
                s.RingSlots = slots;
            });
            FillSlots();
        };
        page.Children.Add(count);

        page.Children.Add(Heading("每一格的功能"));
        page.Children.Add(Note("从正上方开始顺时针排列。长按右键后往这个方向划、松开就执行；右键按着时也可以按数字键直选。选中链接、邮箱或路径时，「搜索」会换成「打开」。"));
        page.Children.Add(ringSlots);
        FillSlots();

        var reset = new Button { Content = "恢复默认", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            Update(s => s.RingSlots = [.. RingItems.DefaultIds]);
            count.SelectedIndex = RingItems.DefaultIds.Count - RingItems.MinSlots;
            FillSlots();
        };
        page.Children.Add(reset);
        return page;
    }

    private void FillSlots()
    {
        ringSlots.Children.Clear();
        var choices = RingItems.Choices;
        var slots = RingItems.Build(Settings.RingSlots);
        for (var i = 0; i < slots.Count; i++)
        {
            var index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = $"{i + 1}. {Direction(RingGeometry.SectorAngle(i, slots.Count))}", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            var box = new ComboBox { Width = 220 };
            foreach (var action in choices) box.Items.Add(action.Title);
            box.SelectedIndex = choices.ToList().FindIndex(a => a.Id == slots[i].Id);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                var id = choices[box.SelectedIndex].Id;
                Update(s =>
                {
                    var current = RingItems.Build(s.RingSlots).Select(a => a.Id).ToList();
                    current[index] = id;
                    s.RingSlots = current;
                });
            };
            row.Children.Add(box);
            ringSlots.Children.Add(row);
        }
    }

    private static string Direction(double degrees)
    {
        string[] names = ["上", "右上", "右", "右下", "下", "左下", "左", "左上"];
        return names[(int)Math.Round(degrees / 45) % 8];
    }

    private UIElement ClipboardPage()
    {
        var page = Page();
        page.Children.Add(Heading("剪贴板历史"));
        page.Children.Add(Check("记录剪贴板历史（只存在这台电脑上）", Settings.ClipboardHistory, v => Update(s => s.ClipboardHistory = v)));
        page.Children.Add(Note(app.HistoryHotKey is { } key ? $"按 {key} 打开剪贴板历史。" : "快捷键都被别的程序占用了，可以从托盘菜单或「全部功能」里打开剪贴板历史。"));

        var days = new ComboBox { Width = 120 };
        int[] dayChoices = [1, 7, 30, 90, 365];
        foreach (var d in dayChoices) days.Items.Add($"{d} 天");
        days.SelectedIndex = Math.Max(0, Array.IndexOf(dayChoices, Settings.ClipboardRetentionDays));
        days.SelectionChanged += (_, _) => Update(s => s.ClipboardRetentionDays = dayChoices[days.SelectedIndex]);
        page.Children.Add(Row("保存时间", days));

        var max = new TextBox { Width = 120, Text = Settings.ClipboardMaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        max.LostFocus += (_, _) =>
        {
            if (int.TryParse(max.Text, out var n)) Update(s => s.ClipboardMaxItems = Math.Clamp(n, 10, 100_000));
            max.Text = Settings.ClipboardMaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture);
        };
        page.Children.Add(Row("最多保存（条）", max));
        page.Children.Add(Note("固定的记录不受保存时间和条数限制。"));

        page.Children.Add(Heading("不记录这些 App 复制的内容"));
        var excluded = new TextBox
        {
            Text = string.Join(Environment.NewLine, Settings.ClipboardExcludedApps),
            AcceptsReturn = true,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        excluded.LostFocus += (_, _) => Update(s => s.ClipboardExcludedApps = excluded.Text
            .Split(['\r', '\n', ',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
        page.Children.Add(excluded);
        page.Children.Add(Note("一行一个程序名（不带 .exe）。密码管理器标了「不要记录」的内容本来就不会记。"));

        page.Children.Add(Heading("已保存"));
        page.Children.Add(clipboardStats);
        RefreshClipboardStats();
        var clear = new Button { Content = "清空（保留固定的）", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "清空剪贴板历史？固定的记录会保留。", "Pop", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            app.ClearClipboardHistory();
            RefreshClipboardStats();
        };
        page.Children.Add(clear);
        return page;
    }

    private void RefreshClipboardStats()
    {
        var (count, bytes) = app.ClipboardStatistics();
        clipboardStats.Text = $"{count} 条，占用 {bytes / 1024.0 / 1024.0:0.0} MB";
    }

    private static StackPanel Row(string label, UIElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private UIElement UpdatePage()
    {
        var page = Page();
        page.Children.Add(Heading($"Pop {Updater.CurrentVersion}"));
        page.Children.Add(Check("自动检查更新", Settings.CheckForUpdates, v => Update(s => s.CheckForUpdates = v)));
        page.Children.Add(Check("接收测试版", Settings.IncludePrerelease, v => Update(s => s.IncludePrerelease = v)));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var check = new Button { Content = "检查更新", Padding = new Thickness(12, 4, 12, 4) };
        var install = new Button { Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
        void Refresh()
        {
            if (app.Updater.Available is { } release)
            {
                install.Content = $"更新到 {release.Version}";
                install.Visibility = Visibility.Visible;
                updateStatus.Text = $"发现新版本 {release.Version}";
            }
            else
            {
                install.Visibility = Visibility.Collapsed;
            }
        }
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            updateStatus.Text = "正在检查……";
            await app.CheckForUpdatesAsync(userInitiated: true);
            if (app.Updater.Available is null) updateStatus.Text = $"{Updater.CurrentVersion} 已是最新版本";
            Refresh();
            check.IsEnabled = true;
        };
        install.Click += async (_, _) =>
        {
            if (app.Updater.Available is { } release) await app.InstallUpdateAsync(release);
        };
        buttons.Children.Add(check);
        buttons.Children.Add(install);
        page.Children.Add(buttons);
        updateStatus.Margin = new Thickness(0, 8, 0, 0);
        page.Children.Add(updateStatus);
        Refresh();

        page.Children.Add(Heading("其他"));
        var logs = new Button { Content = "打开日志文件夹", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true });
        };
        page.Children.Add(logs);
        page.Children.Add(Note($"设置保存在 {Paths.Settings}"));
        return page;
    }
}
