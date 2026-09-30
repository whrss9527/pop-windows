using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 点任务栏上的 Pop 图标弹出的面板：常用开关、截图和剪贴板历史的入口、更新、设置和退出。
/// 毛玻璃底和其他浮窗一样；点面板外面或者按 Esc 关闭
internal sealed class TrayFlyout : Window
{
    private const double PanelWidth = 340;

    /// 和任务栏之间的空隙（DIP）
    private const double Gap = 12;

    private readonly App app;
    private readonly Theme theme = Theme.Current();
    private readonly Grid root = new();
    private readonly TranslateTransform slide = new();
    private readonly StackPanel body = new() { Margin = new Thickness(16, 16, 16, 10) };
    private bool closing;

    public TrayFlyout(App app)
    {
        this.app = app;
        Title = "Pop";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = Theme.TextFont;
        FontSize = Theme.Body;
        Foreground = theme.Brush(theme.Text);
        UseLayoutRounding = true;
        Theme.ApplyTextOptions(this);
        root.RenderTransform = slide;
        Content = root;
        Native.AddExStyle(new WindowInteropHelper(this).EnsureHandle(), Native.WS_EX_TOOLWINDOW);

        Deactivated += (_, _) => CloseAnimated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseAnimated();
        };
        app.SettingsChanged += Fill;
        app.Updater.Changed += Fill;
        Closed += (_, _) =>
        {
            app.SettingsChanged -= Fill;
            app.Updater.Changed -= Fill;
        };
    }

    /// 在任务栏图标旁边弹出来；x、y 是点击的位置（物理像素）
    public void ShowNear(int x, int y)
    {
        Fill();
        body.Measure(new Size(PanelWidth, double.PositiveInfinity));
        var height = Math.Ceiling(body.DesiredSize.Height);

        var (bounds, work, scale) = Native.MonitorDetailsAt(x, y);
        var w = (int)Math.Ceiling(PanelWidth * scale);
        var h = (int)Math.Ceiling(height * scale);
        var gap = (int)Math.Round(Gap * scale);
        // 任务栏在哪一边，面板就贴着那一边弹出来
        int left, top;
        double fromX = 0, fromY = 0;
        if (y >= work.Bottom || (y > work.Top && work.Bottom < bounds.Bottom && y > work.Bottom - 4 * scale))
        {
            left = (int)Math.Clamp(x - w / 2.0, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
            top = (int)work.Bottom - h - gap;
            fromY = 16;
        }
        else if (y < work.Top)
        {
            left = (int)Math.Clamp(x - w / 2.0, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
            top = (int)work.Top + gap;
            fromY = -16;
        }
        else if (x >= work.Right)
        {
            left = (int)work.Right - w - gap;
            top = (int)Math.Clamp(y - h / 2.0, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
            fromX = 16;
        }
        else if (x < work.Left)
        {
            left = (int)work.Left + gap;
            top = (int)Math.Clamp(y - h / 2.0, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
            fromX = -16;
        }
        else
        {
            // 图标在任务栏的溢出区里，或者不知道任务栏在哪：放在工作区右下角
            left = (int)work.Right - w - gap;
            top = (int)work.Bottom - h - gap;
            fromY = 16;
        }

        var shot = Frost.Capture(left, top, w, h, scale);
        var panel = Frost.Panel(shot, left, top, PanelWidth, height, Theme.OverlayRadius, theme, body);
        panel.Margin = new Thickness(Theme.ShadowPad);
        root.Children.Clear();
        root.Children.Add(panel);

        var pad = (int)Math.Round(Theme.ShadowPad * scale);
        Width = PanelWidth + 2 * Theme.ShadowPad;
        Height = height + 2 * Theme.ShadowPad;
        root.Opacity = 0;
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left - pad, top - pad, w + 2 * pad, h + 2 * pad, 0);
        Show();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left - pad, top - pad, w + 2 * pad, h + 2 * pad, 0);
        // 缩放比例不同的显示器上 WPF 会按系统建议的位置重新摆一次，这里再放回来
        Dispatcher.BeginInvoke(() => Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left - pad, top - pad, w + 2 * pad, h + 2 * pad, 0),
            System.Windows.Threading.DispatcherPriority.Loaded);
        Activate();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Motion.Ms(220)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Motion.Ms(220)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Ms(150)));
        Log.Info("托盘面板已显示");
    }

    public void CloseAnimated()
    {
        if (closing) return;
        closing = true;
        var fade = new DoubleAnimation(0, Motion.Ms(90));
        fade.Completed += (_, _) => Close();
        root.BeginAnimation(OpacityProperty, fade);
    }

    /// 关掉面板再做事：截图之类的不能把面板也截进去
    private void Then(Action action)
    {
        CloseAnimated();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160 * Motion.Scale) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void Fill()
    {
        body.Children.Clear();
        body.Children.Add(Header());
        if (UpdateBanner() is { } banner) body.Children.Add(banner);
        body.Children.Add(Tiles());
        body.Children.Add(Divider());
        body.Children.Add(Row(WpfUi.SymbolRegular.ClipboardTextLtr24, "剪贴板历史", app.HistoryHotKey, () => Then(app.ShowClipboardHistory)));
        body.Children.Add(Row(WpfUi.SymbolRegular.ScanText24, "截图识字", app.OcrHotKey, () => Then(app.CaptureText)));
        body.Children.Add(Row(WpfUi.SymbolRegular.Pin24, "截图贴图", app.PinHotKey, () => Then(app.CapturePin)));
        if (PinWindow.Count > 0)
            body.Children.Add(Row(WpfUi.SymbolRegular.PinOff24, $"关闭所有贴图（{PinWindow.Count}）", null, () => Then(PinWindow.CloseAll)));
        body.Children.Add(Divider());
        body.Children.Add(Row(WpfUi.SymbolRegular.Settings24, "设置", null, () => Then(app.ShowSettings)));
        body.Children.Add(Row(WpfUi.SymbolRegular.Power24, "退出 Pop", null, () => Then(app.Quit)));
    }

    private UIElement Header()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = Icons.AppIcon(36);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "Pop",
            FontFamily = Theme.DisplayFont,
            FontSize = Theme.BodyLarge + 2,
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.Brush(theme.Text),
        });
        titles.Children.Add(new TextBlock
        {
            Text = StatusText(),
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.SecondaryText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);

        var check = IconButton(WpfUi.SymbolRegular.ArrowSync24, "检查更新", async () =>
        {
            await app.CheckForUpdatesAsync(userInitiated: true);
            Fill();
        });
        check.IsEnabled = app.Updater.Available is null;
        Grid.SetColumn(check, 2);
        grid.Children.Add(check);
        return grid;
    }

    private string StatusText()
    {
        var version = $"版本 {Updater.CurrentVersion}";
        if (app.Updater.Status is { } status) return $"{version} · {status}";
        if (!app.Settings.Enabled) return $"{version} · 长按唤起已关闭";
        return $"{version} · 选中文字后长按右键";
    }

    private UIElement? UpdateBanner()
    {
        if (app.Updater.Available is not { } release) return null;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Make(WpfUi.SymbolRegular.ArrowDownload24, 20, theme.Brush(theme.Accent));
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = $"Pop {release.Version} 可以更新了", FontWeight = FontWeights.Medium, Foreground = theme.Brush(theme.Text) });
        text.Children.Add(new TextBlock { Text = "更新后自动重新启动", FontSize = Theme.Caption, Foreground = theme.Brush(theme.SecondaryText) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var install = new WpfUi.Button
        {
            Content = "更新",
            Appearance = WpfUi.ControlAppearance.Primary,
            Padding = new Thickness(14, 5, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = app.Updater.Status is not { } s || !s.StartsWith("正在", StringComparison.Ordinal),
        };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            await app.InstallUpdateAsync(release);
        };
        Grid.SetColumn(install, 2);
        grid.Children.Add(install);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(8),
            Background = theme.Brush(theme.AccentSoft),
        };
    }

    /// 三个开关：样子和系统的快速设置一样，开着的是主题色
    private UIElement Tiles()
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 10) };
        grid.Children.Add(Tile(WpfUi.SymbolRegular.CursorClick24, "长按唤起", app.Settings.Enabled, () => app.UpdateSettings(s => s.Enabled = !s.Enabled)));
        grid.Children.Add(Tile(WpfUi.SymbolRegular.Flash24, "直接出结果", app.Settings.DirectResults, () => app.UpdateSettings(s => s.DirectResults = !s.DirectResults)));
        grid.Children.Add(Tile(WpfUi.SymbolRegular.History24, "剪贴板记录", app.Settings.ClipboardHistory, () => app.UpdateSettings(s => s.ClipboardHistory = !s.ClipboardHistory)));
        return grid;
    }

    private FrameworkElement Tile(WpfUi.SymbolRegular symbol, string title, bool on, Action toggle)
    {
        var face = new Border
        {
            Height = 48,
            CornerRadius = new CornerRadius(Theme.ControlRadius),
            Background = theme.Brush(on ? theme.Accent : theme.Field),
            BorderBrush = theme.Brush(on ? Color.FromArgb(0x14, 0, 0, 0) : theme.FieldStroke),
            BorderThickness = new Thickness(1),
            Child = Icons.Make(symbol, 20, theme.Brush(on ? theme.AccentText : theme.Text), filled: on),
        };
        ((FrameworkElement)face.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)face.Child).VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock
        {
            Text = title,
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        stack.Children.Add(face);
        stack.Children.Add(label);
        AutomationProperties(stack, title, on);
        var normal = face.Background;
        var hover = theme.Brush(on ? Mix(theme.Accent, theme.Dark ? Colors.White : Colors.Black, 0.08) : theme.Selected);
        stack.MouseEnter += (_, _) => face.Background = hover;
        stack.MouseLeave += (_, _) => face.Background = normal;
        stack.MouseLeftButtonUp += (_, _) => toggle();
        return stack;
    }

    private static void AutomationProperties(FrameworkElement element, string name, bool on) =>
        System.Windows.Automation.AutomationProperties.SetName(element, $"{name}：{(on ? "开" : "关")}");

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// 一行入口：图标、名字，右边是快捷键
    private FrameworkElement Row(WpfUi.SymbolRegular symbol, string title, string? keys, Action run)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Make(symbol, 18, theme.Brush(theme.SecondaryText));
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Foreground = theme.Brush(theme.Text) };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (keys is not null)
        {
            var hint = new TextBlock { Text = keys, FontSize = Theme.Caption, Foreground = theme.Brush(theme.TertiaryText), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }
        var row = new Border
        {
            Child = grid,
            Height = 38,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(-8, 0, -8, 0),
            CornerRadius = new CornerRadius(Theme.ControlRadius),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        var hover = theme.Brush(theme.Hover);
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => run();
        return row;
    }

    private WpfUi.Button IconButton(WpfUi.SymbolRegular symbol, string tip, Action run)
    {
        var button = new WpfUi.Button
        {
            Icon = new WpfUi.SymbolIcon { Symbol = symbol, FontSize = 16 },
            Appearance = WpfUi.ControlAppearance.Transparent,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => run();
        return button;
    }

    private Border Divider() => new()
    {
        Height = 1,
        Background = theme.Brush(theme.Divider),
        Margin = new Thickness(-16, 6, -16, 6),
    };
}
