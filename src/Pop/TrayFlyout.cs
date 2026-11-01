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
    private readonly ScrollViewer viewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
    private double maxPanelHeight = 650;
    private bool closing;
    private long refillVersion;
    private string? pendingFocus;
    private FrameworkElement? recoveryEntry;

    internal bool RecoveryNoticeIsVisible => recoveryEntry is { IsVisible: true, ActualHeight: > 0 };

    /// 截背景时多截的高度（DIP），够放下更新提示
    private const double ExtraHeight = 96;

    private Frost.Shot? shot;
    private double scale = 1;
    private int left;
    /// 面板固定的那条边（物理像素）：往上长时是下沿，否则是上沿
    private int edge;
    private bool growsUp;

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
        viewport.Content = body;
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(body, KeyboardNavigationMode.Cycle);
        Native.AddExStyle(new WindowInteropHelper(this).EnsureHandle(), Native.WS_EX_TOOLWINDOW);

        Deactivated += (_, _) => CloseAnimated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseAnimated(); }
        };
        app.SettingsChanged += Refill;
        app.SettingsSaveStateChanged += OnSettingsSaved;
        app.Updater.Changed += Refill;
        Closed += (_, _) =>
        {
            refillVersion++;
            app.SettingsChanged -= Refill;
            app.SettingsSaveStateChanged -= OnSettingsSaved;
            app.Updater.Changed -= Refill;
        };
    }

    private void OnSettingsSaved() { if (!app.SettingsSavePending) Refill(); }

    /// 在任务栏图标旁边弹出来；x、y 是点击的位置（物理像素）
    public void ShowNear(int x, int y, double? heightLimitForDemo = null)
    {
        if (heightLimitForDemo is not null && !app.IsDemo) throw new InvalidOperationException("受控托盘高度仅用于独立演示");
        Fill();
        var (bounds, work, monitorScale) = Native.MonitorDetailsAt(x, y);
        maxPanelHeight = Math.Max(120, (work.Bottom - work.Top) / monitorScale - 2 * (Gap + Theme.ShadowPad));
        if (heightLimitForDemo is { } limit) maxPanelHeight = Math.Min(maxPanelHeight, Math.Max(120, limit));
        var height = Measure();
        scale = monitorScale;
        var w = (int)Math.Ceiling(PanelWidth * scale);
        var h = (int)Math.Ceiling(height * scale);
        var gap = (int)Math.Round(Gap * scale);
        // 任务栏在哪一边，面板就贴着那一边弹出来。任务栏在下面时面板的下沿固定，内容变多时往上长
        double fromX = 0, fromY = 0;
        if (y >= work.Bottom || (y > work.Top && work.Bottom < bounds.Bottom && y > work.Bottom - 4 * scale))
        {
            left = (int)Math.Clamp(x - w / 2.0, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
            edge = (int)work.Bottom - gap;
            growsUp = true;
            fromY = 16;
        }
        else if (y < work.Top)
        {
            left = (int)Math.Clamp(x - w / 2.0, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
            edge = (int)work.Top + gap;
            fromY = -16;
        }
        else if (x >= work.Right || x < work.Left)
        {
            left = x >= work.Right ? (int)work.Right - w - gap : (int)work.Left + gap;
            edge = (int)Math.Clamp(y - h / 2.0, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
            fromX = x >= work.Right ? 16 : -16;
        }
        else
        {
            // 图标在任务栏的溢出区里，或者不知道任务栏在哪：放在工作区右下角
            left = (int)work.Right - w - gap;
            edge = (int)work.Bottom - gap;
            growsUp = true;
            fromY = 16;
        }

        // 背景多截一截，内容变多（比如出现了更新提示）时也够用
        var extra = (int)Math.Round(ExtraHeight * scale);
        var top = growsUp ? edge - h : edge;
        shot = Frost.Capture(left, growsUp ? top - extra : top, w, h + extra, scale);
        Layout(height);
        root.Opacity = 0;
        Show();
        Layout(height);
        Activate();
        Dispatcher.BeginInvoke(() => { if (!closing && IsActive) FindControl("tray-toggle-mouse")?.Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Motion.Ms(220)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Motion.Ms(220)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Ms(150)));
        Log.Info("托盘面板已显示");
    }

    /// 面板内容的高度（DIP）
    private double Measure()
    {
        body.Measure(new Size(PanelWidth, double.PositiveInfinity));
        return Math.Min(maxPanelHeight, Math.Ceiling(body.DesiredSize.Height));
    }

    /// 按内容的高度摆好毛玻璃面板和窗口
    private void Layout(double height)
    {
        var w = (int)Math.Ceiling(PanelWidth * scale);
        var h = (int)Math.Ceiling(height * scale);
        var top = growsUp ? edge - h : edge;
        if (viewport.Parent is Panel old) old.Children.Remove(viewport);
        viewport.Height = height;
        var panel = Frost.Panel(shot, left, top, PanelWidth, height, Theme.OverlayRadius, theme, viewport);
        panel.Margin = new Thickness(Theme.ShadowPad);
        root.Children.Clear();
        root.Children.Add(panel);

        var pad = (int)Math.Round(Theme.ShadowPad * scale);
        Width = PanelWidth + 2 * Theme.ShadowPad;
        Height = height + 2 * Theme.ShadowPad;
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left - pad, top - pad, w + 2 * pad, h + 2 * pad, 0);
        // 缩放比例不同的显示器上 WPF 会按系统建议的位置重新摆一次，这里再放回来
        Dispatcher.BeginInvoke(() => Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, left - pad, top - pad, w + 2 * pad, h + 2 * pad, 0),
            System.Windows.Threading.DispatcherPriority.Loaded);
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

    /// 重新填内容；已经显示出来的话按新的高度重新摆
    private void Refill()
    {
        if (closing) return;
        if (Keyboard.FocusedElement is DependencyObject focused && IsAncestorOf(focused))
        {
            var id = System.Windows.Automation.AutomationProperties.GetAutomationId(focused);
            if (id.Length > 0) pendingFocus = id;
        }
        var offset = viewport.VerticalOffset;
        var version = ++refillVersion;
        Fill();
        if (IsVisible) Layout(Measure());
        Dispatcher.BeginInvoke(() =>
        {
            if (closing || !IsActive || version != refillVersion) return;
            var id = pendingFocus; pendingFocus = null;
            if (id is not null) FindControl(id)?.Focus();
            viewport.ScrollToVerticalOffset(offset);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void Fill()
    {
        body.Children.Clear();
        body.Children.Add(Header());
        recoveryEntry = null;
        if (app.SettingsReadProblem)
        {
            recoveryEntry = Row(WpfUi.SymbolRegular.Warning24, "原设置需要处理", "查看", () => Then(app.ShowSettings), "recovery");
            recoveryEntry.ToolTip = "原设置未能读取。打开设置查看恢复方法，保存前会先保留原文件副本。";
            body.Children.Add(recoveryEntry);
        }
        if (app.SettingsSaveError is not null)
            body.Children.Add(Row(WpfUi.SymbolRegular.Warning24, "设置尚未保存", "重试", () => app.RetrySaveSettings(), "retry-save"));
        if (UpdateBanner() is { } banner) body.Children.Add(banner);
        body.Children.Add(Tiles());
        body.Children.Add(Divider());
        body.Children.Add(Row(WpfUi.SymbolRegular.ClipboardTextLtr24, "剪贴板历史", app.HistoryHotKey, () => Then(app.ShowClipboardHistory), "history"));
        body.Children.Add(Row(WpfUi.SymbolRegular.TextBulletListLtr24, "常用短语", app.ShortcutText("snippets"), () => Then(app.ShowSnippets), "snippets"));
        var awake = Row(WpfUi.SymbolRegular.WeatherSunny24, app.IsKeepingAwake ? "保持唤醒 · 已开启" : "保持唤醒", app.ShortcutText("keepAwake"), () => Then(app.ShowKeepAwake), "awake");
        awake.ToolTip = app.AwakeStatus; body.Children.Add(awake);
        body.Children.Add(Row(WpfUi.SymbolRegular.ScanText24, "截图识字", app.OcrHotKey, () => Then(app.CaptureText), "ocr"));
        body.Children.Add(Row(WpfUi.SymbolRegular.Pin24, "截图贴图", app.PinHotKey, () => Then(app.CapturePin), "pin"));
        body.Children.Add(Row(WpfUi.SymbolRegular.Edit24, "截图标注", app.AnnotateHotKey, () => Then(app.CaptureAnnotate), "annotate"));
        body.Children.Add(Row(WpfUi.SymbolRegular.Color24, "屏幕取色", app.ColorHotKey, () => Then(app.PickColor), "color"));
        body.Children.Add(Row(WpfUi.SymbolRegular.Ruler24, "屏幕标尺", null, () => Then(app.MeasureScreen), "measure"));
        if (PinWindow.Count > 0)
            body.Children.Add(Row(WpfUi.SymbolRegular.PinOff24, $"关闭所有贴图（{PinWindow.Count}）", null, () => Then(PinWindow.CloseAll), "close-pins"));
        body.Children.Add(Divider());
        body.Children.Add(Row(WpfUi.SymbolRegular.Settings24, "设置", null, () => Then(app.ShowSettings), "settings"));
        body.Children.Add(Row(WpfUi.SymbolRegular.Power24, "退出 Pop", null, () => Then(app.Quit), "quit"));
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
            Refill();
        });
        check.IsEnabled = app.Updater.Available is null;
        System.Windows.Automation.AutomationProperties.SetAutomationId(check, "tray-check-update");
        Grid.SetColumn(check, 2);
        grid.Children.Add(check);
        return grid;
    }

    private string StatusText()
    {
        var version = $"版本 {Updater.CurrentVersion}";
        if (app.Updater.Status is { } status) return $"{version} · {status}";
        if (!app.Settings.Enabled || app.Settings.TriggerMode == TriggerMode.Disabled) return $"{version} · 鼠标唤起已关闭";
        return app.Settings.TriggerMode switch
        {
            TriggerMode.MiddleClick => $"{version} · 点击中键唤起",
            TriggerMode.ModifierRightClick => $"{version} · 按修饰键并点击右键唤起",
            _ => $"{version} · 选中文字后长按右键",
        };
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
        text.Children.Add(new TextBlock { Text = app.Updater.IsBusy ? app.Updater.Status : $"Pop {release.Version} 可以更新了", FontWeight = FontWeights.Medium, Foreground = theme.Brush(theme.Text), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = app.Updater.IsBusy ? "可在设置 → 关于查看进度" : "更新后自动重新启动", FontSize = Theme.Caption, Foreground = theme.Brush(theme.SecondaryText) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var install = new WpfUi.Button
        {
            Content = app.Updater.IsBusy ? "进行中" : "更新",
            Appearance = WpfUi.ControlAppearance.Primary,
            Padding = new Thickness(14, 5, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !app.Updater.IsBusy,
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
        var mouseOn = app.Settings.Enabled && app.Settings.TriggerMode != TriggerMode.Disabled;
        grid.Children.Add(Tile("mouse", WpfUi.SymbolRegular.CursorClick24, "鼠标唤起", mouseOn, "控制鼠标唤起，功能仍可从托盘入口打开", value => app.UpdateSettings(s =>
        {
            s.Enabled = value;
            if (value && s.TriggerMode == TriggerMode.Disabled) s.TriggerMode = TriggerMode.LongPressRight;
        })));
        grid.Children.Add(Tile("direct", WpfUi.SymbolRegular.Flash24, "直接出结果", app.Settings.DirectResults, "按当前直达规则优先显示结果", value => app.UpdateSettings(s => s.DirectResults = value)));
        grid.Children.Add(Tile("recording", WpfUi.SymbolRegular.History24, "剪贴板记录", app.Settings.ClipboardHistory, "只控制新内容的记录，已有历史仍可查看", value => app.UpdateSettings(s => s.ClipboardHistory = value)));
        return grid;
    }

    private FrameworkElement Tile(string id, WpfUi.SymbolRegular symbol, string title, bool on, string help, Action<bool> toggle)
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(face, "tray-face-" + id);
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
        var state = new TextBlock
        {
            Text = on ? "已开启" : "已暂停", FontSize = Theme.Caption,
            Foreground = theme.Brush(on ? theme.Accent : theme.SecondaryText),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(state, "tray-state-" + id);
        stack.Children.Add(state);
        var button = new ToggleButton { Content = stack, IsChecked = on, ToolTip = help };
        PrepareControl(button, "tray-toggle-" + id);
        System.Windows.Automation.AutomationProperties.SetName(button, $"{title}：{state.Text}");
        System.Windows.Automation.AutomationProperties.SetHelpText(button, help);
        var normal = face.Background;
        var hover = theme.Brush(on ? Mix(theme.Accent, theme.Dark ? Colors.White : Colors.Black, 0.08) : theme.Selected);
        button.MouseEnter += (_, _) => face.Background = hover;
        button.MouseLeave += (_, _) => face.Background = normal;
        button.Checked += (_, _) => toggle(true);
        button.Unchecked += (_, _) => toggle(false);
        return button;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// 一行入口：图标、名字，右边是快捷键
    private FrameworkElement Row(WpfUi.SymbolRegular symbol, string title, string? keys, Action run, string id)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Make(symbol, 18, theme.Brush(theme.SecondaryText));
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Foreground = theme.Brush(theme.Text), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (keys is not null)
        {
            var hint = new TextBlock { Text = keys, FontSize = Theme.Caption, Foreground = theme.Brush(theme.TertiaryText), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetAutomationId(hint, "tray-shortcut-" + id);
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }
        var row = new Border
        {
            Child = grid,
            Height = 38,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(Theme.ControlRadius),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        var button = new Button { Content = row, Margin = new Thickness(-8, 0, -8, 0) };
        PrepareControl(button, "tray-action-" + id);
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        var hover = theme.Brush(theme.Hover);
        button.MouseEnter += (_, _) => row.Background = hover;
        button.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        button.Click += (_, _) => run();
        return button;
    }

    private void PrepareControl(ButtonBase control, string id)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(HorizontalAlignmentProperty, new System.Windows.Data.Binding(nameof(Control.HorizontalContentAlignment))
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        presenter.SetBinding(VerticalAlignmentProperty, new System.Windows.Data.Binding(nameof(Control.VerticalContentAlignment))
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        control.Template = new ControlTemplate(control.GetType()) { VisualTree = presenter };
        control.Background = Brushes.Transparent; control.BorderThickness = new Thickness(0); control.Padding = new Thickness(0);
        control.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        control.VerticalContentAlignment = VerticalAlignment.Stretch;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.FontFamily = Theme.TextFont; control.FontWeight = FontWeights.Normal; control.Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetAutomationId(control, id);
        var outline = new FrameworkElementFactory(typeof(Border));
        outline.SetValue(Border.BorderBrushProperty, theme.Brush(theme.Accent));
        outline.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(Theme.ControlRadius));
        outline.SetValue(MarginProperty, new Thickness(-2));
        control.FocusVisualStyle = new Style(typeof(Control))
        {
            Setters = { new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outline }) },
        };
    }

    private FrameworkElement? FindControl(string id) => Tree(this).OfType<FrameworkElement>()
        .FirstOrDefault(element => System.Windows.Automation.AutomationProperties.GetAutomationId(element) == id);
    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(parent, i))) yield return child;
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
