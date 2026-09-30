using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Shape = System.Windows.Shapes.Shape;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

/// 设置窗口：左边分页，右边是一张张设置卡片。改动立刻生效并保存。
/// Windows 11 上窗口底是 Mica，Windows 10 上是纯色
internal sealed class SettingsWindow : WpfUi.FluentWindow
{
    public static readonly IReadOnlyList<(string Id, string Title, Symbol Icon)> Pages =
    [
        ("general", "通用", Symbol.Settings24),
        ("ring", "圆盘", Symbol.DataPie24),
        ("actions", "功能", Symbol.Apps24),
        ("translate", "翻译", Symbol.Translate24),
        ("clipboard", "剪贴板", Symbol.ClipboardTextLtr24),
        ("about", "关于", Symbol.Info24),
    ];

    private const string HomePage = "https://github.com/whrss9527/pop-windows";

    private readonly App app;
    private readonly Dictionary<string, (WpfUi.Button Button, Border Pill, WpfUi.SymbolIcon Icon)> nav = [];
    private readonly ScrollViewer scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false,
    };
    private string current = "general";
    private bool updating;
    private string? updateMessage;
    private System.Net.Http.HttpClient? http;

    public SettingsWindow(App app, string? page = null)
    {
        this.app = app;
        Title = "Pop 设置";
        Width = 960;
        Height = 720;
        MinWidth = 720;
        MinHeight = 520;
        FitToWorkArea(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WpfUi.WindowBackdropType.Mica;
        WindowCornerPreference = WpfUi.WindowCornerPreference.Round;
        FontFamily = Theme.TextFont;
        FontSize = Theme.Body;
        Theme.ApplyTextOptions(this);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Pop.ico"));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(new WpfUi.TitleBar
        {
            Title = "Pop 设置",
            Icon = new WpfUi.ImageIcon { Source = Icons.AppIcon(16).Source, Width = 16, Height = 16 },
        });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        body.Children.Add(Navigation());
        Grid.SetColumn(scroller, 1);
        body.Children.Add(scroller);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !(e.OriginalSource is ComboBoxItem or ComboBox { IsDropDownOpen: true })) Close();
        };
        app.SettingsChanged += OnSettingsChanged;
        app.Updater.Changed += OnUpdaterChanged;
        app.PluginsChanged += OnPluginsChanged;
        Closed += (_, _) =>
        {
            app.SettingsChanged -= OnSettingsChanged;
            app.Updater.Changed -= OnUpdaterChanged;
            app.PluginsChanged -= OnPluginsChanged;
            http?.Dispose();
        };
        Navigate(page ?? "general");
    }

    private AppSettings Settings => app.Settings;

    /// 自己改的设置不用刷新页面；托盘面板改了才刷新
    private void Update(Action<AppSettings> change)
    {
        updating = true;
        try
        {
            app.UpdateSettings(change);
        }
        finally
        {
            updating = false;
        }
    }

    private void OnSettingsChanged()
    {
        if (!updating) Dispatcher.BeginInvoke(() => ShowPage(animate: false));
    }

    /// 插件增加、删除或者改了：功能页和圆盘页要重画
    private void OnPluginsChanged()
    {
        if (current is "actions" or "ring") Dispatcher.BeginInvoke(() => ShowPage(animate: false));
    }

    private void OnUpdaterChanged()
    {
        if (current == "about") Dispatcher.BeginInvoke(() => ShowPage(animate: false));
    }

    // ── 导航 ────────────────────────────────────────────

    private UIElement Navigation()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 4, 12, 12) };
        var identity = new Grid { Margin = new Thickness(8, 12, 0, 24) };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Icons.AppIcon(44);
        icon.Margin = new Thickness(0, 0, 14, 0);
        identity.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Label("Pop", Theme.BodyLarge + 2, "TextFillColorPrimaryBrush", FontWeights.SemiBold, Theme.DisplayFont));
        names.Children.Add(Label($"版本 {Updater.CurrentVersion}", Theme.Caption, "TextFillColorSecondaryBrush"));
        Grid.SetColumn(names, 1);
        identity.Children.Add(names);
        panel.Children.Add(identity);

        foreach (var (id, title, symbol) in Pages)
        {
            var pill = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-12, 0, 0, 0),
                Visibility = Visibility.Hidden,
            };
            pill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            var glyph = new WpfUi.SymbolIcon { Symbol = symbol, FontSize = 18, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(glyph);
            row.Children.Add(new TextBlock { Text = title, FontSize = Theme.Body, VerticalAlignment = VerticalAlignment.Center });
            var content = new Grid();
            content.Children.Add(row);
            content.Children.Add(pill);
            var button = new WpfUi.Button
            {
                Content = content,
                Appearance = WpfUi.ControlAppearance.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 38,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(0),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += (_, _) => Navigate(id);
            nav[id] = (button, pill, glyph);
            panel.Children.Add(button);
        }
        return panel;
    }

    /// 打开某一页（Pages 里的 ID）
    public void Navigate(string id)
    {
        if (!Pages.Any(p => p.Id == id)) id = "general";
        current = id;
        foreach (var (key, (button, pill, glyph)) in nav)
        {
            var selected = key == id;
            if (selected) button.SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
            else button.ClearValue(BackgroundProperty);
            pill.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            glyph.Filled = selected;
        }
        ShowPage(animate: true);
        Log.Info($"设置：{Pages.First(p => p.Id == id).Title}");
    }

    private void ShowPage(bool animate)
    {
        var offset = animate ? 0 : scroller.VerticalOffset;
        var page = new StackPanel { Margin = new Thickness(40, 12, 40, 40), MaxWidth = 1000 };
        var title = Label(Pages.First(p => p.Id == current).Title, Theme.TitleSize, "TextFillColorPrimaryBrush", FontWeights.SemiBold, Theme.DisplayFont);
        title.Margin = new Thickness(0, 0, 0, 20);
        page.Children.Add(title);
        switch (current)
        {
            case "ring": RingPage(page); break;
            case "actions": ActionsPage(page); break;
            case "translate": TranslatePage(page); break;
            case "clipboard": ClipboardPage(page); break;
            case "about": AboutPage(page); break;
            default: GeneralPage(page); break;
        }
        scroller.Content = page;
        scroller.UpdateLayout();
        scroller.ScrollToVerticalOffset(offset);
        if (!animate) return;
        var slide = new TranslateTransform(0, 20);
        page.RenderTransform = slide;
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, Motion.Ms(320)) { EasingFunction = ease });
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Ms(200)));
    }

    // ── 通用 ────────────────────────────────────────────

    private void GeneralPage(StackPanel page)
    {
        var s = Settings;
        page.Children.Add(Section("唤起", first: true));
        page.Children.Add(Card(Symbol.CursorClick24, "长按鼠标右键唤起 Pop", "在任意 App 里选中文字，按住右键不放，圆盘就会出现",
            Toggle(s.Enabled, v => Update(x => x.Enabled = v))));

        var holdValue = Label($"{s.HoldMilliseconds} 毫秒", Theme.Body, "TextFillColorSecondaryBrush");
        holdValue.MinWidth = 64;
        holdValue.TextAlignment = TextAlignment.Right;
        holdValue.VerticalAlignment = VerticalAlignment.Center;
        holdValue.Margin = new Thickness(0, 0, 12, 0);
        var hold = new Slider
        {
            Minimum = AppSettings.MinHold,
            Maximum = AppSettings.MaxHold,
            TickFrequency = 50,
            IsSnapToTickEnabled = true,
            Value = s.HoldMilliseconds,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hold.ValueChanged += (_, e) =>
        {
            var ms = (int)e.NewValue;
            holdValue.Text = $"{ms} 毫秒";
            Update(x => x.HoldMilliseconds = ms);
        };
        page.Children.Add(Card(Symbol.Timer24, "按住多久算长按", "比这个时间短的右键点击照常弹出系统的右键菜单", Horizontal(holdValue, hold)));

        bool autoStart;
        try
        {
            autoStart = AutoStart.IsEnabled;
        }
        catch (System.Security.SecurityException)
        {
            autoStart = false;
        }
        page.Children.Add(Card(Symbol.Rocket24, "开机时启动", "登录 Windows 后在后台运行，任务栏右下角有 Pop 的图标",
            Toggle(autoStart, v =>
            {
                try
                {
                    AutoStart.Set(v);
                }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    Log.Error("设置开机启动失败", e);
                }
            })));

        page.Children.Add(Section("直接出结果"));
        var kindToggles = new List<(WpfUi.ToggleSwitch Toggle, bool Available)>();
        var master = Toggle(s.DirectResults, v =>
        {
            Update(x => x.DirectResults = v);
            foreach (var (toggle, available) in kindToggles) toggle.IsEnabled = v && available;
        });
        var rows = new List<UIElement>();
        foreach (var (name, kind, title) in DirectResults.Kinds)
        {
            var actionId = DirectResults.ActionFor(kind);
            var available = s.IsEnabled(actionId);
            var toggle = Toggle(s.DirectKinds.Contains(name, StringComparer.OrdinalIgnoreCase), v => Update(x =>
            {
                x.DirectKinds.RemoveAll(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (v) x.DirectKinds.Add(name);
            }));
            toggle.IsEnabled = s.DirectResults && available;
            kindToggles.Add((toggle, available));
            var description = available ? KindExample(name) : $"「{Actions.Find(actionId)?.Title}」功能已关闭，可以在「功能」里打开";
            rows.Add(Row(null, title, description, toggle, indent: 38));
        }
        page.Children.Add(Group(Row(Symbol.Flash24, "选中这些内容时直接出结果", "跳过圆盘，直接弹出结果卡片；没打开的照常弹出圆盘", master), rows));

        page.Children.Add(Section("快捷键"));
        page.Children.Add(Group(null,
        [
            ShortcutRow(Symbol.ClipboardTextLtr24, "剪贴板历史", app.HistoryHotKey),
            ShortcutRow(Symbol.ScanText24, "截图识字", app.OcrHotKey),
            ShortcutRow(Symbol.Pin24, "截图贴图", app.PinHotKey),
        ]));
    }

    private static string KindExample(string name) => name switch
    {
        "foreign" => "选中英文等外文时直接显示译文",
        "math" => "例如 128*3、(1+2)^3、sqrt(2)",
        "measurement" => "例如 5 km、72 °F、3 斤、1.5 TB",
        "color" => "例如 #FF8800、rgb(0, 120, 212)",
        "timestamp" => "例如 1735660800",
        "datetime" => "例如 2026-09-30 14:00",
        "number" => "例如 255、0xFF、1234567.89",
        _ => "",
    };

    private Grid ShortcutRow(Symbol symbol, string title, string? keys) =>
        Row(symbol, title, keys is null ? "快捷键被别的程序占用了，可以从托盘面板里打开" : null, Keys(keys));

    // ── 圆盘 ────────────────────────────────────────────

    private void RingPage(StackPanel page)
    {
        var s = Settings;
        var ring = RingItems.Build(s.RingSlots, s.IsEnabled);
        page.Children.Add(Note("长按右键后往某一格的方向划、松开就执行这一格的功能；按住右键时也可以按数字键直接选。选中链接、邮箱或路径时，「搜索」会换成「打开」。"));

        var count = new ComboBox { MinWidth = 140 };
        for (var n = RingItems.MinSlots; n <= RingItems.MaxSlots; n++) count.Items.Add($"{n} 格");
        count.SelectedIndex = ring.Count - RingItems.MinSlots;
        count.SelectionChanged += (_, _) =>
        {
            var n = count.SelectedIndex + RingItems.MinSlots;
            Update(x =>
            {
                var slots = RingItems.Build(x.RingSlots, x.IsEnabled).Select(a => a.Id).Take(n).ToList();
                // 多出来的格子从默认的里面挑还没用上的
                foreach (var id in RingItems.DefaultIds.Concat(Actions.List.Select(a => a.Id)))
                {
                    if (slots.Count >= n) break;
                    if (!slots.Contains(id) && x.IsEnabled(id)) slots.Add(id);
                }
                x.RingSlots = slots;
            });
            Dispatcher.BeginInvoke(() => ShowPage(animate: false));
        };
        page.Children.Add(Card(Symbol.DataPie24, "格子数", "4 到 8 格。格子越少，每一格越大，越容易划中", count));

        page.Children.Add(Section("每一格的功能"));
        var preview = new Canvas { Width = PreviewSize, Height = PreviewSize };
        int? highlighted = null;
        void Draw() => DrawPreview(preview, RingItems.Build(Settings.RingSlots, Settings.IsEnabled), highlighted);
        Draw();

        var choices = RingItems.Choices.Where(a => s.IsEnabled(a.Id)).ToList();
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < ring.Count; i++)
        {
            var index = i;
            var number = new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = Theme.Caption, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 0),
                Child = number,
            };
            badge.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
            var direction = Label(Direction(RingGeometry.SectorAngle(i, ring.Count)), Theme.Body, "TextFillColorSecondaryBrush");
            direction.Width = 52;
            direction.VerticalAlignment = VerticalAlignment.Center;
            var box = new ComboBox { MinWidth = 200, MaxDropDownHeight = 360 };
            foreach (var action in choices) box.Items.Add(action.Title);
            box.SelectedIndex = choices.FindIndex(a => a.Id == ring[i].Id);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                var id = choices[box.SelectedIndex].Id;
                Update(x =>
                {
                    var slots = RingItems.Build(x.RingSlots, x.IsEnabled).Select(a => a.Id).ToList();
                    // 选了别的格子已经有的功能：两格互换
                    var other = slots.IndexOf(id);
                    if (other >= 0 && other != index) slots[other] = slots[index];
                    slots[index] = id;
                    x.RingSlots = slots;
                });
                Dispatcher.BeginInvoke(() => ShowPage(animate: false));
            };
            var row = Horizontal(badge, direction, box);
            row.Margin = new Thickness(0, 4, 0, 4);
            row.Background = Brushes.Transparent;
            row.MouseEnter += (_, _) =>
            {
                highlighted = index;
                Draw();
            };
            row.MouseLeave += (_, _) =>
            {
                highlighted = null;
                Draw();
            };
            list.Children.Add(row);
        }

        var layout = new Grid { Margin = new Thickness(20) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        preview.Margin = new Thickness(0, 0, 36, 0);
        preview.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(preview);
        Grid.SetColumn(list, 1);
        layout.Children.Add(list);
        page.Children.Add(CardBorder(layout));

        var reset = ActionButton("恢复默认", Symbol.ArrowReset24, () =>
        {
            Update(x => x.RingSlots = [.. RingItems.DefaultIds]);
            ShowPage(animate: false);
        });
        reset.Margin = new Thickness(0, 12, 0, 0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(reset);
    }

    private const double PreviewSize = 232;

    /// 圆盘的缩略图：和真的圆盘一样的排列，鼠标指着右边某一行时高亮对应的格子
    private static void DrawPreview(Canvas canvas, IReadOnlyList<PopAction> ring, int? highlighted)
    {
        canvas.Children.Clear();
        const double c = PreviewSize / 2;
        const double outer = PreviewSize / 2 - 1;
        const double inner = 38;
        var disc = new Ellipse { Width = outer * 2, Height = outer * 2, StrokeThickness = 1 };
        disc.SetResourceReference(Shape.FillProperty, "CardBackgroundFillColorSecondaryBrush");
        disc.SetResourceReference(Shape.StrokeProperty, "CardStrokeColorDefaultBrush");
        Canvas.SetLeft(disc, c - outer);
        Canvas.SetTop(disc, c - outer);
        canvas.Children.Add(disc);

        var n = ring.Count;
        var half = 180.0 / n;
        if (highlighted is { } h && h < n)
        {
            var angle = RingGeometry.SectorAngle(h, n);
            var wedge = new System.Windows.Shapes.Path { Data = Wedge(c, inner + 3, outer - 3, angle - half + 1.5, angle + half - 1.5) };
            wedge.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
            canvas.Children.Add(wedge);
        }
        for (var i = 0; i < n; i++)
        {
            var edge = RingGeometry.SectorAngle(i, n) - half;
            var from = Point(c, inner, edge);
            var to = Point(c, outer, edge);
            var line = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "DividerStrokeColorDefaultBrush");
            canvas.Children.Add(line);
        }
        var center = new Ellipse { Width = inner * 2, Height = inner * 2, StrokeThickness = 1 };
        center.SetResourceReference(Shape.FillProperty, "CardBackgroundFillColorDefaultBrush");
        center.SetResourceReference(Shape.StrokeProperty, "CardStrokeColorDefaultBrush");
        Canvas.SetLeft(center, c - inner);
        Canvas.SetTop(center, c - inner);
        canvas.Children.Add(center);
        var logo = Icons.AppIcon(28);
        Canvas.SetLeft(logo, c - 14);
        Canvas.SetTop(logo, c - 14);
        canvas.Children.Add(logo);

        var labelRadius = (inner + outer) / 2 + 2;
        for (var i = 0; i < n; i++)
        {
            var p = Point(c, labelRadius, RingGeometry.SectorAngle(i, n));
            var on = highlighted == i;
            var icon = new WpfUi.SymbolIcon { Symbol = Icons.Parse(ring[i].Glyph), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Filled = on };
            var text = new TextBlock { Text = ring[i].Title, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var brush = on ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush";
            icon.SetResourceReference(ForegroundProperty, brush);
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            var label = new StackPanel { Width = 64 };
            label.Children.Add(icon);
            label.Children.Add(text);
            label.Measure(new Size(64, double.PositiveInfinity));
            Canvas.SetLeft(label, p.X - 32);
            Canvas.SetTop(label, p.Y - label.DesiredSize.Height / 2);
            canvas.Children.Add(label);
        }
    }

    /// 圆心在 (c, c)，从正上方顺时针转 degrees 度、离圆心 r 的点
    private static Point Point(double c, double r, double degrees)
    {
        var t = degrees * Math.PI / 180;
        return new Point(c + r * Math.Sin(t), c - r * Math.Cos(t));
    }

    private static Geometry Wedge(double c, double r0, double r1, double a0, double a1)
    {
        var large = a1 - a0 > 180;
        var figure = new PathFigure { StartPoint = Point(c, r1, a0), IsClosed = true };
        figure.Segments.Add(new ArcSegment(Point(c, r1, a1), new Size(r1, r1), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Point(c, r0, a1), true));
        figure.Segments.Add(new ArcSegment(Point(c, r0, a0), new Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static string Direction(double degrees)
    {
        string[] names = ["上", "右上", "右", "右下", "下", "左下", "左", "左上"];
        return names[(int)Math.Round(degrees / 45) % 8];
    }

    // ── 功能 ────────────────────────────────────────────

    private void ActionsPage(StackPanel page)
    {
        var s = Settings;
        page.Children.Add(Note("用不上的功能可以关掉：关掉的功能不出现在圆盘和「全部功能」列表里，圆盘上空出来的格子自动换成别的功能，选中内容时也不再直接出它的结果。"));
        PluginsSection(page);

        var builtIn = Actions.BuiltIn.Append(Actions.All).ToList();
        var on = builtIn.Count(a => s.IsEnabled(a.Id));
        page.Children.Add(Section("内置功能"));
        var enableAll = ActionButton("全部打开", Symbol.CheckmarkCircle24, () =>
        {
            Update(x => x.DisabledActions.RemoveAll(id => Actions.IsBuiltIn(id)));
            ShowPage(animate: false);
        });
        enableAll.IsEnabled = on < builtIn.Count;
        page.Children.Add(Card(Symbol.Apps24, $"已打开 {on} 个内置功能，共 {builtIn.Count} 个", null, enableAll));

        foreach (var category in Actions.Categories.Where(c => c != Actions.PluginCategory))
        {
            var actions = builtIn.Where(a => a.Category == category).ToList();
            if (actions.Count == 0) continue;
            page.Children.Add(Section(category));
            var rows = new List<UIElement>();
            foreach (var action in actions)
            {
                var canDisable = Actions.CanDisable(action);
                var toggle = EnableToggle(action.Id);
                toggle.IsEnabled = canDisable;
                var summary = canDisable ? action.Summary : $"{action.Summary}。一直打开，其他功能都能从这里找到";
                rows.Add(Row(Icons.Parse(action.Glyph), action.Title, summary, toggle));
            }
            page.Children.Add(Group(null, rows));
        }
    }

    private WpfUi.ToggleSwitch EnableToggle(string id) => Toggle(Settings.IsEnabled(id), v => Update(x =>
    {
        x.DisabledActions.RemoveAll(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
        if (!v) x.DisabledActions.Add(id);
    }));

    // ── 插件 ────────────────────────────────────────────

    /// 「我的插件」：每个插件一行（开关、编辑、更多），下面是新建、导入、打开插件文件夹
    private void PluginsSection(StackPanel page)
    {
        var store = app.Plugins;
        page.Children.Add(Section("我的插件"));
        var rows = new List<UIElement>();
        foreach (var manifest in store.Manifests)
        {
            var edit = ActionButton("编辑", null, () => EditPlugin(manifest, isNew: false));
            var more = new WpfUi.Button
            {
                Icon = new WpfUi.SymbolIcon { Symbol = Symbol.MoreHorizontal24, FontSize = 16 },
                Width = 34,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "更多",
            };
            System.Windows.Automation.AutomationProperties.SetName(more, $"{manifest.DisplayName}：更多");
            more.Click += (_, _) => PluginMenu(manifest, more);
            var toggle = EnableToggle(manifest.Id);
            toggle.Margin = new Thickness(0, 0, 16, 0);
            var detail = manifest.Summary.Length > 0 ? manifest.Summary : string.Join("、", manifest.Match.Kinds.Select(PluginKinds.Title));
            var subtitle = detail.Length == 0 ? PluginNames.Title(manifest.Action.Type) : $"{PluginNames.Title(manifest.Action.Type)} · {detail}";
            rows.Add(Row(Icons.Parse(PluginGlyphs.Resolve(manifest), Symbol.PuzzlePiece24), manifest.DisplayName, subtitle, Horizontal(toggle, edit, more)));
        }
        foreach (var (file, problem) in store.LoadErrors.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            var warning = Row(Symbol.Warning24, file, problem, null);
            ((FrameworkElement)warning.Children[0]).SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
            rows.Add(warning);
        }
        if (rows.Count == 0)
            rows.Add(Row(Symbol.PuzzlePiece24, "还没有自己的插件", "可以从模板新建：用网址模板接入任何网站的搜索，用 PowerShell 或 JavaScript 脚本处理选中的文字", null));

        var create = ActionButton("新建插件", Symbol.Add24, () => { }, WpfUi.ControlAppearance.Primary);
        create.Click += (_, _) => TemplateMenu(create);
        var import = ActionButton("导入…", Symbol.ArrowImport24, ImportPlugins);
        import.Margin = new Thickness(8, 0, 0, 0);
        var folder = ActionButton("打开插件文件夹", Symbol.FolderOpen24, () => OpenFolder(store.Directory));
        var bar = new DockPanel { Margin = new Thickness(16, 12, 16, 12), LastChildFill = false };
        var left = Horizontal(create, import);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(folder, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(folder);
        rows.Add(bar);
        page.Children.Add(Group(null, rows));
        var note = Note("每个插件是插件文件夹里的一个 JSON 文件，可以直接编辑、拷给别人，和 macOS 版的 Pop 通用。插件和内置功能一样，可以放到圆盘上，也可以在「全部功能」里搜到。");
        note.Margin = new Thickness(2, 8, 0, 0);
        page.Children.Add(note);
    }

    /// 「新建插件」的模板菜单
    private void TemplateMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var template in PluginTemplates.All)
        {
            var item = new MenuItem
            {
                Header = template.Title,
                Icon = new WpfUi.SymbolIcon { Symbol = Icons.Parse(PluginGlyphs.Resolve(template.Manifest), Symbol.PuzzlePiece24), FontSize = 16 },
            };
            item.Click += (_, _) => EditPlugin(template.Manifest with { Id = PluginManifest.MakeId() }, isNew: true);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// 插件那一行的「更多」菜单：导出、在文件夹中显示、删除
    private void PluginMenu(PluginManifest manifest, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string title, Symbol icon, Action action)
        {
            var item = new MenuItem { Header = title, Icon = new WpfUi.SymbolIcon { Symbol = icon, FontSize = 16 } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("导出…", Symbol.ArrowExport24, () => ExportPlugin(manifest));
        Add("在文件夹中显示", Symbol.FolderOpen24, () => RevealPlugin(manifest));
        menu.Items.Add(new Separator());
        Add("删除", Symbol.Delete24, async () => await DeletePlugin(manifest));
        menu.IsOpen = true;
    }

    private void EditPlugin(PluginManifest manifest, bool isNew)
    {
        if (PluginEditorWindow.Edit(this, app, manifest, isNew) is { } saved && isNew)
        {
            // 新建的插件默认打开；圆盘上有空的格子就放上去
            Update(x => x.DisabledActions.RemoveAll(d => string.Equals(d, saved.Id, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void ImportPlugins()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Pop 插件 (*.json)|*.json", Multiselect = true, Title = "导入插件" };
        if (dialog.ShowDialog(this) != true) return;
        var problems = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var imported = app.Plugins.ImportFile(path);
                Log.Info($"插件：导入了 {imported.Id}");
            }
            catch (PluginStoreException e)
            {
                problems.Add($"{Path.GetFileName(path)}：{e.Message}");
            }
        }
        if (problems.Count > 0) _ = ShowMessage("有的插件没有导入", string.Join("\n", problems));
    }

    private void ExportPlugin(PluginManifest manifest)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Pop 插件 (*.json)|*.json",
            FileName = $"{string.Concat(manifest.DisplayName.Split(Path.GetInvalidFileNameChars()))}.json",
            Title = "导出插件",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PluginStore.Export(manifest, dialog.FileName);
        }
        catch (PluginStoreException e)
        {
            _ = ShowMessage("没有导出", e.Message);
        }
    }

    private void RevealPlugin(PluginManifest manifest)
    {
        if (app.Plugins.FilePath(manifest.Id) is not { } path) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error("打开插件文件夹失败", e);
        }
    }

    private async Task DeletePlugin(PluginManifest manifest)
    {
        var confirm = new WpfUi.MessageBox
        {
            Title = $"删除「{manifest.DisplayName}」？",
            Content = "插件文件会被删除，不能恢复。想留着的话可以先导出。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = Theme.TextFont,
        };
        if (await confirm.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;
        try
        {
            app.Plugins.Delete(manifest.Id);
            Update(x => x.DisabledActions.RemoveAll(d => string.Equals(d, manifest.Id, StringComparison.OrdinalIgnoreCase)));
            Log.Info($"插件：删除了 {manifest.Id}");
        }
        catch (PluginStoreException e)
        {
            await ShowMessage("没有删除", e.Message);
        }
    }

    private async Task ShowMessage(string title, string text)
    {
        var box = new WpfUi.MessageBox
        {
            Title = title,
            Content = text,
            CloseButtonText = "好",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = Theme.TextFont,
        };
        await box.ShowDialogAsync();
    }

    // ── 翻译 ────────────────────────────────────────────

    private void TranslatePage(StackPanel page)
    {
        var s = Settings;
        page.Children.Add(Note("选中文字后在圆盘上选「翻译」，译文显示在卡片里，可以复制或者替换原文，不用离开当前的 App。"));
        page.Children.Add(Section("翻译服务"));

        var engines = new (TranslationEngine Engine, string Title)[]
        {
            (TranslationEngine.Bing, "必应翻译（免费，不用设置）"),
            (TranslationEngine.Azure, "Microsoft Translator（自己的 Key）"),
            (TranslationEngine.Browser, "在浏览器里打开必应翻译"),
        };
        var engine = new ComboBox { MinWidth = 260 };
        foreach (var (_, title) in engines) engine.Items.Add(title);
        engine.SelectedIndex = Math.Max(0, Array.FindIndex(engines, e => e.Engine == s.TranslationEngine));
        var azure = new StackPanel();
        void ShowAzure() => azure.Visibility = Settings.TranslationEngine == TranslationEngine.Azure ? Visibility.Visible : Visibility.Collapsed;
        engine.SelectionChanged += (_, _) =>
        {
            if (engine.SelectedIndex < 0) return;
            var chosen = engines[engine.SelectedIndex].Engine;
            Update(x => x.TranslationEngine = chosen);
            ShowAzure();
        };
        page.Children.Add(Card(Symbol.Translate24, "用哪个翻译服务", "必应翻译直接就能用；有 Azure 账号的话可以用自己的翻译资源", engine));

        // Microsoft Translator 的 Key 和区域
        var hasKey = !string.IsNullOrEmpty(SecretStore.Get(SecretStore.AzureTranslatorKey));
        var key = new WpfUi.PasswordBox { PlaceholderText = hasKey ? "已保存。输入新的 Key 可以替换" : "粘贴 Key", Width = 260 };
        var keyStatus = Label(hasKey ? "Key 已保存" : "还没有保存 Key", Theme.Caption, "TextFillColorSecondaryBrush");
        var save = ActionButton("保存", null, () =>
        {
            var value = key.Password.Trim();
            if (value.Length == 0) return;
            try
            {
                SecretStore.Set(SecretStore.AzureTranslatorKey, value);
                key.Password = "";
                key.PlaceholderText = "已保存。输入新的 Key 可以替换";
                keyStatus.Text = "Key 已保存";
                Log.Info("保存了 Microsoft Translator 的 Key");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                keyStatus.Text = $"保存失败：{e.Message}";
                Log.Error("保存 Key 失败", e);
            }
        });
        save.Margin = new Thickness(8, 0, 0, 0);
        var keyControl = new StackPanel();
        keyControl.Children.Add(Horizontal(key, save));
        keyStatus.Margin = new Thickness(2, 6, 0, 0);
        keyControl.Children.Add(keyStatus);
        var region = new WpfUi.TextBox { Text = s.AzureTranslatorRegion, PlaceholderText = "例如 eastasia", Width = 260 };
        region.LostFocus += (_, _) => Update(x => x.AzureTranslatorRegion = region.Text.Trim());
        azure.Children.Add(Group(null,
        [
            Row(Symbol.Key24, "Key", "在 Azure 门户创建「翻译」资源，在「密钥和终结点」里复制。Key 加密保存在这台电脑上，不写进设置文件", keyControl),
            Row(Symbol.Globe24, "区域", "翻译资源所在的区域；创建的是全局资源的话留空", region),
        ]));
        ShowAzure();
        page.Children.Add(azure);

        page.Children.Add(Section("语言"));
        var target = new ComboBox { MinWidth = 260 };
        target.Items.Add("自动（中文译成英语，其他译成简体中文）");
        foreach (var (_, name) in Languages.Targets) target.Items.Add(name);
        var targetIndex = Languages.Targets.ToList().FindIndex(t => string.Equals(t.Code, s.TranslateTarget, StringComparison.OrdinalIgnoreCase));
        target.SelectedIndex = targetIndex + 1;
        target.SelectionChanged += (_, _) =>
        {
            var code = target.SelectedIndex <= 0 ? "auto" : Languages.Targets[target.SelectedIndex - 1].Code;
            Update(x => x.TranslateTarget = code);
        };
        page.Children.Add(Card(Symbol.LocalLanguage24, "译成", "选中的文字本来就是这种语言时，中文和英语互译", target));

        var foreign = Toggle(s.DirectKinds.Contains("foreign", StringComparer.OrdinalIgnoreCase), v => Update(x =>
        {
            x.DirectKinds.RemoveAll(k => string.Equals(k, "foreign", StringComparison.OrdinalIgnoreCase));
            if (v) x.DirectKinds.Add("foreign");
        }));
        foreign.IsEnabled = s.DirectResults && s.IsEnabled(Actions.Translate.Id);
        var foreignNote = !s.IsEnabled(Actions.Translate.Id) ? "「翻译」功能已关闭，可以在「功能」里打开"
            : !s.DirectResults ? "要先打开「通用」里的「直接出结果」"
            : "长按时不弹圆盘，直接显示译文。想对外文用圆盘上的其他功能时关掉它";
        page.Children.Add(Card(Symbol.Flash24, "选中外文时直接翻译", foreignNote, foreign));

        page.Children.Add(Section("试一试"));
        page.Children.Add(TryTranslation());
    }

    private Border TryTranslation()
    {
        var input = new WpfUi.TextBox { Text = "Select any text, then press and hold the right mouse button.", TextWrapping = TextWrapping.Wrap };
        var output = Label("", Theme.BodyLarge, "TextFillColorPrimaryBrush");
        output.Margin = new Thickness(2, 14, 0, 0);
        output.Visibility = Visibility.Collapsed;
        var caption = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        caption.Margin = new Thickness(2, 4, 0, 0);
        caption.Visibility = Visibility.Collapsed;
        WpfUi.Button? go = null;
        go = ActionButton("翻译", Symbol.Translate24, async () =>
        {
            var text = input.Text.Trim();
            if (text.Length == 0 || go is null) return;
            var s = Settings;
            var to = Languages.TargetFor(text, s.TranslateTarget);
            var engine = s.TranslationEngine == TranslationEngine.Browser ? TranslationEngine.Bing : s.TranslationEngine;
            go.IsEnabled = false;
            output.Visibility = Visibility.Visible;
            caption.Visibility = Visibility.Collapsed;
            output.Text = "正在翻译…";
            try
            {
                http ??= new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var result = await new Translator(http).TranslateAsync(text, to, engine, SecretStore.Get(SecretStore.AzureTranslatorKey), s.AzureTranslatorRegion);
                output.Text = result.Text;
                caption.Text = $"{Languages.Name(result.From)} → {Languages.Name(result.To)} · {result.Provider}";
                caption.Visibility = Visibility.Visible;
            }
            catch (TranslationException e)
            {
                output.Text = e.Message;
            }
            catch (TaskCanceledException)
            {
                output.Text = "翻译服务没有响应，请稍后再试";
            }
            finally
            {
                go.IsEnabled = true;
            }
        }, WpfUi.ControlAppearance.Primary);
        go.Margin = new Thickness(8, 0, 0, 0);
        go.VerticalAlignment = VerticalAlignment.Top;
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(input);
        Grid.SetColumn(go, 1);
        line.Children.Add(go);
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(line);
        stack.Children.Add(output);
        stack.Children.Add(caption);
        return CardBorder(stack);
    }

    // ── 剪贴板 ──────────────────────────────────────────

    private void ClipboardPage(StackPanel page)
    {
        var s = Settings;
        page.Children.Add(Section("剪贴板历史", first: true));
        page.Children.Add(Card(Symbol.History24, "记录剪贴板历史", "复制过的文字、图片和文件都记下来，只存在这台电脑上",
            Toggle(s.ClipboardHistory, v => Update(x => x.ClipboardHistory = v))));
        page.Children.Add(Card(Symbol.Keyboard24, "打开剪贴板历史",
            app.HistoryHotKey is null ? "快捷键被别的程序占用了，可以从托盘面板或「全部功能」里打开" : "在任意位置按下这组快捷键，打字搜索，回车粘贴",
            Keys(app.HistoryHotKey)));

        int[] dayChoices = [1, 7, 30, 90, 365];
        var days = new ComboBox { MinWidth = 140 };
        foreach (var d in dayChoices) days.Items.Add($"{d} 天");
        days.SelectedIndex = Math.Max(0, Array.IndexOf(dayChoices, s.ClipboardRetentionDays));
        days.SelectionChanged += (_, _) =>
        {
            if (days.SelectedIndex >= 0) Update(x => x.ClipboardRetentionDays = dayChoices[days.SelectedIndex]);
        };
        page.Children.Add(Card(Symbol.Clock24, "保存时间", "超过这个时间的记录自动删除，固定的除外", days));

        var max = new WpfUi.NumberBox
        {
            Value = s.ClipboardMaxItems,
            Minimum = 10,
            Maximum = 100_000,
            SmallChange = 100,
            LargeChange = 1000,
            MaxDecimalPlaces = 0,
            Width = 160,
            SpinButtonPlacementMode = WpfUi.NumberBoxSpinButtonPlacementMode.Inline,
        };
        max.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v) Update(x => x.ClipboardMaxItems = (int)Math.Clamp(v, 10, 100_000));
        };
        page.Children.Add(Card(Symbol.Stack24, "最多保存", "超过这个条数时删掉最早的，固定的不算在内", max));

        page.Children.Add(Section("隐私"));
        var excluded = new WpfUi.TextBox
        {
            Text = string.Join(Environment.NewLine, s.ClipboardExcludedApps),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 96,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "一行一个程序名",
            Margin = new Thickness(56, 0, 16, 16),
        };
        excluded.LostFocus += (_, _) => Update(x => x.ClipboardExcludedApps = excluded.Text
            .Split(['\r', '\n', ',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
        var privacy = new StackPanel();
        privacy.Children.Add(Row(Symbol.ShieldLock24, "不记录这些 App 复制的内容", "一行一个程序名，不带 .exe。密码管理器标了「不要记录」的内容本来就不会记", null));
        privacy.Children.Add(excluded);
        page.Children.Add(CardBorder(privacy));

        page.Children.Add(Section("已保存"));
        var (count, bytes) = app.ClipboardStatistics();
        var clear = ActionButton("清空", Symbol.Delete24, async () =>
        {
            var confirm = new WpfUi.MessageBox
            {
                Title = "清空剪贴板历史？",
                Content = "固定的记录会保留，其他记录删除后不能恢复。",
                PrimaryButtonText = "清空",
                CloseButtonText = "取消",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = Theme.TextFont,
            };
            if (await confirm.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;
            app.ClearClipboardHistory();
            ShowPage(animate: false);
        });
        clear.IsEnabled = count > 0;
        page.Children.Add(Card(Symbol.Database24, $"{count} 条记录", $"占用 {bytes / 1024.0 / 1024.0:0.0} MB 空间", clear));
    }

    // ── 关于 ────────────────────────────────────────────

    private void AboutPage(StackPanel page)
    {
        var s = Settings;
        var hero = new Grid { Margin = new Thickness(20) };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = Icons.AppIcon(64);
        logo.Margin = new Thickness(0, 0, 20, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        hero.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Label("Pop", Theme.Subtitle, "TextFillColorPrimaryBrush", FontWeights.SemiBold, Theme.DisplayFont));
        names.Children.Add(Label($"版本 {Updater.CurrentVersion}", Theme.Body, "TextFillColorSecondaryBrush"));
        var status = app.Updater.Available is { } available ? $"可以更新到 {available.Version}" : app.Updater.Status ?? updateMessage;
        if (status is not null)
        {
            var line = Label(status, Theme.Caption, app.Updater.Available is null ? "TextFillColorTertiaryBrush" : "AccentTextFillColorPrimaryBrush");
            line.Margin = new Thickness(0, 4, 0, 0);
            names.Children.Add(line);
        }
        Grid.SetColumn(names, 1);
        hero.Children.Add(names);
        WpfUi.Button button;
        if (app.Updater.Available is { } release)
        {
            button = ActionButton($"更新到 {release.Version}", Symbol.ArrowDownload24, async () => await app.InstallUpdateAsync(release), WpfUi.ControlAppearance.Primary);
            button.IsEnabled = app.Updater.Status is not { } busy || !busy.StartsWith("正在", StringComparison.Ordinal);
        }
        else
        {
            button = ActionButton("检查更新", Symbol.ArrowSync24, async () =>
            {
                updateMessage = "正在检查…";
                ShowPage(animate: false);
                try
                {
                    var found = await app.Updater.CheckAsync(userInitiated: true);
                    updateMessage = found is null ? $"{Updater.CurrentVersion} 已是最新版本" : null;
                }
                catch (UpdateException e)
                {
                    updateMessage = e.Message;
                }
                ShowPage(animate: false);
            });
            button.IsEnabled = updateMessage != "正在检查…";
        }
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 2);
        hero.Children.Add(button);
        page.Children.Add(CardBorder(hero));

        page.Children.Add(Section("更新"));
        page.Children.Add(Card(Symbol.ArrowSync24, "自动检查更新", "有新版本时托盘图标右下角会出现一个小圆点，点一下就能更新",
            Toggle(s.CheckForUpdates, v => Update(x => x.CheckForUpdates = v))));
        page.Children.Add(Card(Symbol.Beaker24, "接收测试版", "提前用上还在测试的新功能，可能不太稳定",
            Toggle(s.IncludePrerelease, v => Update(x => x.IncludePrerelease = v))));

        page.Children.Add(Section("帮助"));
        page.Children.Add(Group(null,
        [
            Row(Symbol.DocumentText24, "日志", "遇到问题时可以把日志发给开发者", ActionButton("打开文件夹", Symbol.FolderOpen24, () => OpenFolder(Log.Directory))),
            Row(Symbol.Settings24, "设置文件", Paths.Settings, ActionButton("打开文件夹", Symbol.FolderOpen24, () => OpenFolder(Paths.RoamingData))),
            Row(Symbol.Globe24, "项目主页", "反馈问题、查看更新日志", ActionButton("打开", Symbol.Open24, () => OpenUrl(HomePage))),
        ]));

        page.Children.Add(Section("开源许可"));
        page.Children.Add(Group(null,
        [
            Row(null, "Noto Sans CJK", "界面里的中文字体（常用字子集），SIL Open Font License 1.1", ActionButton("查看许可", null, ShowFontLicense), indent: 2),
            Row(null, "WPF-UI 和 Fluent System Icons", "界面控件和图标，MIT 许可", null, indent: 2),
            Row(null, "Microsoft.Data.Sqlite 和 SQLitePCLRaw", "剪贴板历史的数据库，MIT 和 Apache 2.0 许可", null, indent: 2),
            Row(null, "Interop.UIAutomationClient", "读取其他 App 里选中的文字，MIT 许可", null, indent: 2),
            Row(null, "Jint 和 Acornima", "运行 JavaScript 插件，BSD 许可", null, indent: 2),
        ]));
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"打开 {path} 失败", e);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error($"打开 {url} 失败", e);
        }
    }

    /// 字体的许可证随程序一起分发：写到本地文件夹里再用记事本打开
    private static void ShowFontLicense()
    {
        try
        {
            var folder = Path.Combine(Paths.LocalData, "licenses");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Noto Sans CJK - OFL.txt");
            using (var source = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Fonts/LICENSE-OFL.txt")).Stream)
            using (var target = File.Create(path))
                source.CopyTo(target);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("打开字体许可失败", e);
        }
    }
}
