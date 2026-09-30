using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

/// 新建和编辑插件：名称、图标、动作（网址、Shell 脚本、JavaScript）、结果的去向、什么时候可用，可以当场试运行。
/// 插件文件的格式和 macOS 版一样
internal sealed class PluginEditorWindow : WpfUi.FluentWindow
{
    /// 可以选的图标
    private static readonly string[] Glyphs =
    [
        "PuzzlePiece24", "Globe24", "Search24", "Book24", "Library24", "Translate24", "Code24", "Braces24",
        "WindowConsole20", "ArrowSort24", "ArrowSwap24", "TextBulletListLtr24", "TextQuote24", "TextT24", "Edit24", "Link24",
        "Mail24", "Copy24", "Clipboard24", "Document24", "Note24", "Folder24", "Calendar24", "Clock24",
        "Color24", "Image24", "QrCode24", "Calculator24", "NumberSymbol24", "Map24", "Location24", "Cart24",
        "Chat24", "ArrowReply24", "Send24", "Share24", "Sparkle24", "Wand24", "Lightbulb24", "Star24",
        "Heart24", "Tag24", "Bookmark24", "Key24", "LockClosed24", "Wrench24", "Settings24", "Rocket24",
    ];

    private static readonly (PluginActionType Type, string Title)[] Types =
    [
        (PluginActionType.Url, "打开网址"),
        (PluginActionType.Shell, "Shell 脚本"),
        (PluginActionType.JavaScript, "JavaScript"),
    ];

    private readonly App app;
    private readonly PluginManifest original;
    private readonly bool isNew;

    private readonly WpfUi.TextBox name = new() { PlaceholderText = "比如：GitHub 搜索", Width = 300 };
    private readonly WpfUi.TextBox summary = new() { PlaceholderText = "可选：一句话说明它做什么", Width = 300 };
    private readonly WrapPanel glyphs = new() { Margin = new Thickness(16, 0, 16, 14) };
    private string glyph;

    private readonly ComboBox type = new() { MinWidth = 200 };
    private readonly WpfUi.TextBox url = new() { PlaceholderText = "https://example.com/search?q={text}" };
    private readonly WpfUi.TextBox script = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        MinHeight = 180,
        MaxHeight = 320,
        FontFamily = Theme.MonoFont,
        FontSize = 13,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly ComboBox shell = new() { MinWidth = 200 };
    private readonly ComboBox output = new() { MinWidth = 200 };
    private readonly WpfUi.NumberBox timeout = new()
    {
        Minimum = PluginAction.MinTimeout,
        Maximum = PluginAction.MaxTimeout,
        SmallChange = 5,
        MaxDecimalPlaces = 0,
        Width = 150,
        SpinButtonPlacementMode = WpfUi.NumberBoxSpinButtonPlacementMode.Inline,
    };
    private readonly StackPanel actionArea = new();
    private readonly TextBlock actionHelp = Note("");

    private readonly Dictionary<string, CheckBox> kinds = [];
    private readonly WpfUi.TextBox pattern = new() { PlaceholderText = "可选，比如 ^[A-Z]{2,}$", Width = 300 };

    private readonly WpfUi.TextBox testInput = new() { Text = "Hello Pop", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
    private readonly TextBlock testResult = new() { FontFamily = Theme.MonoFont, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock testWarning = Label("", Theme.Caption, "SystemFillColorCautionBrush");
    private readonly WpfUi.ProgressRing testProgress = new() { IsIndeterminate = true, Width = 16, Height = 16, Margin = new Thickness(10, 0, 0, 0), Visibility = Visibility.Collapsed };

    private readonly TextBlock error = Label("", Theme.Body, "SystemFillColorCriticalBrush");
    private CancellationTokenSource? testRun;

    /// 保存下来的插件；取消时是 null
    public PluginManifest? Saved { get; private set; }

    public PluginEditorWindow(App app, PluginManifest manifest, bool isNew)
    {
        this.app = app;
        original = manifest;
        this.isNew = isNew;
        glyph = PluginGlyphs.Resolve(manifest);
        Title = isNew ? "新建插件" : $"编辑「{manifest.DisplayName}」";
        Width = 720;
        Height = 780;
        MinWidth = 600;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WpfUi.WindowBackdropType.Mica;
        WindowCornerPreference = WpfUi.WindowCornerPreference.Round;
        ShowInTaskbar = false;
        FontFamily = Theme.TextFont;
        FontSize = Theme.Body;
        Theme.ApplyTextOptions(this);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Pop.ico"));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new WpfUi.TitleBar
        {
            Title = Title,
            Icon = new WpfUi.ImageIcon { Source = Icons.AppIcon(16).Source, Width = 16, Height = 16 },
            ShowMinimize = false,
            ShowMaximize = false,
        });
        var page = new StackPanel { Margin = new Thickness(28, 4, 28, 24) };
        var scroller = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        var footer = Footer();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        Fill(manifest);
        page.Children.Add(Section("基本信息", first: true));
        page.Children.Add(Basics());
        page.Children.Add(Section("动作"));
        page.Children.Add(ActionCard());
        page.Children.Add(actionHelp);
        actionHelp.Margin = new Thickness(2, 8, 0, 0);
        page.Children.Add(Section("什么时候可用"));
        page.Children.Add(MatchCard());
        var matchHelp = Note("勾选能处理的内容类型，满足其中之一就行；一个都不勾表示随时可用（不需要选中内容）。填了正则的话，选中的文字还要能匹配它。");
        matchHelp.Margin = new Thickness(2, 8, 0, 0);
        page.Children.Add(matchHelp);
        page.Children.Add(Section("试运行"));
        page.Children.Add(TestCard());
        ShowType();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) => testRun?.Cancel();
        Loaded += (_, _) => name.Focus();
    }

    // ── 界面 ────────────────────────────────────────────

    private void Fill(PluginManifest manifest)
    {
        name.Text = manifest.Name;
        summary.Text = manifest.Summary;
        foreach (var (t, title) in Types) type.Items.Add(title);
        // macOS 版的插件可能是快捷指令、AI 指令，保留原样，只是在 Windows 上运行不了
        if (manifest.Action.Type is PluginActionType.Shortcut or PluginActionType.Ai)
            type.Items.Add($"{PluginNames.Title(manifest.Action.Type)}（Windows 上还不能运行）");
        type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, t => t.Type == manifest.Action.Type) is var i && i >= 0 ? i : Types.Length);
        type.SelectionChanged += (_, _) => ShowType();
        url.Text = manifest.Action.Template;
        script.Text = manifest.Action.Script;
        foreach (var s in Enum.GetValues<PluginShell>()) shell.Items.Add(PluginNames.Title(s));
        shell.SelectedIndex = (int)manifest.Action.Shell;
        foreach (var o in new[] { PluginOutput.Card, PluginOutput.Copy, PluginOutput.Replace, PluginOutput.Toast }) output.Items.Add(PluginNames.Title(o));
        output.SelectedIndex = manifest.Output switch { PluginOutput.Copy => 1, PluginOutput.Replace => 2, PluginOutput.Toast => 3, _ => 0 };
        timeout.Value = PluginAction.ClampTimeout(manifest.Action.Timeout);
        pattern.Text = manifest.Match.Pattern ?? "";
    }

    private UIElement Basics()
    {
        var stack = new StackPanel();
        stack.Children.Add(Row(null, "名称", null, name));
        stack.Children.Add(Divider());
        var iconRow = new StackPanel();
        iconRow.Children.Add(Row(null, "图标", "显示在圆盘和列表里", null));
        FillGlyphs();
        iconRow.Children.Add(glyphs);
        stack.Children.Add(iconRow);
        stack.Children.Add(Divider());
        stack.Children.Add(Row(null, "说明", null, summary));
        return CardBorder(stack);
    }

    private void FillGlyphs()
    {
        glyphs.Children.Clear();
        var choices = Glyphs.Contains(glyph) ? Glyphs : [glyph, .. Glyphs];
        foreach (var name in choices)
        {
            var selected = name == glyph;
            var icon = new WpfUi.SymbolIcon { Symbol = Icons.Parse(name, Symbol.PuzzlePiece24), FontSize = 18, Filled = selected };
            var button = new WpfUi.Button
            {
                Icon = icon,
                Width = 38,
                Height = 38,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = name,
                Appearance = selected ? WpfUi.ControlAppearance.Primary : WpfUi.ControlAppearance.Secondary,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                glyph = name;
                FillGlyphs();
            };
            glyphs.Children.Add(button);
        }
    }

    private UIElement ActionCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(Row(null, "类型", null, type));
        stack.Children.Add(Divider());
        stack.Children.Add(actionArea);
        return CardBorder(stack);
    }

    private PluginActionType SelectedType =>
        type.SelectedIndex >= 0 && type.SelectedIndex < Types.Length ? Types[type.SelectedIndex].Type : original.Action.Type;

    /// 按动作类型换掉下面的输入框和说明
    private void ShowType()
    {
        foreach (var child in new FrameworkElement[] { url, script, shell, output, timeout })
            if (child.Parent is Panel parent) parent.Children.Remove(child);
        actionArea.Children.Clear();
        var kind = SelectedType;
        switch (kind)
        {
            case PluginActionType.Url:
                url.Margin = new Thickness(16, 14, 16, 16);
                actionArea.Children.Add(url);
                actionHelp.Text = "{text} 会换成编码后的选中文字，{raw} 换成原文。可以用任何网址，也可以用 App 的链接（比如 bingmaps:?q={text}）。";
                break;
            case PluginActionType.Shell or PluginActionType.JavaScript:
                if (kind == PluginActionType.Shell)
                {
                    actionArea.Children.Add(Row(null, "用什么运行", "Windows PowerShell 每台电脑都有；PowerShell 7 要另外安装", shell));
                    actionArea.Children.Add(Divider());
                }
                script.Margin = new Thickness(16, 14, 16, 14);
                actionArea.Children.Add(script);
                actionArea.Children.Add(Divider());
                actionArea.Children.Add(Row(null, "结果", "脚本的输出怎么用", output));
                actionArea.Children.Add(Divider());
                actionArea.Children.Add(Row(null, "最长运行（秒）", "超过这个时间就停止脚本", timeout));
                actionHelp.Text = kind == PluginActionType.Shell
                    ? "选中的文字从标准输入传进来（PowerShell 里用 $input 读），也可以读环境变量 POP_TEXT；选中文件时 POP_FILES 是每行一个路径。标准输出就是结果，退出码不为 0 时显示错误输出。"
                    : "定义 function run(input, files) 并返回结果（返回对象会自动转成 JSON），也可以直接写一个表达式。脚本在隔离的环境里运行，不能访问网络和文件。";
                break;
            default:
                actionArea.Children.Add(Row(null, "这个插件来自 macOS 版", $"{PluginNames.Title(kind)}在 Windows 上还不能运行，保存时原样保留", null));
                actionHelp.Text = "";
                break;
        }
        actionHelp.Visibility = actionHelp.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement MatchCard()
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(16, 14, 16, 8) };
        foreach (var kind in PluginKinds.All)
        {
            var supported = PluginKinds.IsSupported(kind);
            var box = new CheckBox
            {
                Content = PluginKinds.Title(kind),
                IsChecked = original.Match.Kinds.Contains(kind),
                IsEnabled = supported,
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = supported ? null : "Windows 版还识别不了这种内容",
            };
            kinds[kind] = box;
            grid.Children.Add(box);
        }
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Divider());
        stack.Children.Add(Row(null, "正则", "选中的文字要能匹配它", pattern));
        return CardBorder(stack);
    }

    private UIElement TestCard()
    {
        var run = ActionButton("运行", Symbol.Play24, async () => await RunTestAsync(), WpfUi.ControlAppearance.Primary);
        run.Margin = new Thickness(8, 0, 0, 0);
        run.VerticalAlignment = VerticalAlignment.Top;
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(testInput);
        Grid.SetColumn(run, 1);
        line.Children.Add(run);
        testProgress.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(testProgress, 2);
        line.Children.Add(testProgress);
        testWarning.Margin = new Thickness(2, 8, 0, 0);
        testWarning.Visibility = Visibility.Collapsed;
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(line);
        stack.Children.Add(testWarning);
        stack.Children.Add(testResult);
        return CardBorder(stack);
    }

    private UIElement Footer()
    {
        var bar = new Grid();
        bar.SetResourceReference(Panel.BackgroundProperty, "LayerFillColorDefaultBrush");
        var line = Divider();
        line.VerticalAlignment = VerticalAlignment.Top;
        bar.Children.Add(line);
        var row = new DockPanel { Margin = new Thickness(28, 16, 28, 16), LastChildFill = true };
        var cancel = ActionButton("取消", null, Close);
        var save = ActionButton(isNew ? "添加" : "保存", null, Save, WpfUi.ControlAppearance.Primary);
        save.Margin = new Thickness(8, 0, 0, 0);
        var buttons = Horizontal(cancel, save);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        error.VerticalAlignment = VerticalAlignment.Center;
        error.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(error);
        bar.Children.Add(row);
        return bar;
    }

    // ── 保存和试运行 ────────────────────────────────────

    /// 按界面上填的内容组成插件（还没整理）
    private PluginManifest Collect()
    {
        var kind = SelectedType;
        return original with
        {
            Name = name.Text,
            Summary = summary.Text,
            Glyph = glyph,
            Action = original.Action with
            {
                Type = kind,
                Template = kind == PluginActionType.Url ? url.Text : original.Action.Template,
                Script = kind is PluginActionType.Shell or PluginActionType.JavaScript ? script.Text : original.Action.Script,
                Shell = shell.SelectedIndex >= 0 ? (PluginShell)shell.SelectedIndex : original.Action.Shell,
                Timeout = timeout.Value ?? PluginAction.DefaultTimeout,
            },
            Output = kind == PluginActionType.Url ? PluginOutput.None : output.SelectedIndex switch
            {
                1 => PluginOutput.Copy,
                2 => PluginOutput.Replace,
                3 => PluginOutput.Toast,
                _ => PluginOutput.Card,
            },
            Match = original.Match with
            {
                Kinds = [.. PluginKinds.All.Where(k => kinds.TryGetValue(k, out var box) && box.IsChecked == true)],
                Pattern = pattern.Text,
            },
        };
    }

    private void Save()
    {
        var manifest = Collect().Normalized();
        if (manifest.ValidationError() is { } problem)
        {
            error.Text = problem;
            return;
        }
        try
        {
            Saved = app.Plugins.Save(manifest);
            Log.Info($"插件：{(isNew ? "添加" : "保存")}了 {Saved.Id}（{PluginNames.Name(Saved.Action.Type)}）");
            DialogResult = true;
        }
        catch (PluginStoreException e)
        {
            error.Text = e.Message;
        }
    }

    /// 演示模式：点一下「运行」
    public void RunTest() => _ = RunTestAsync();

    private async Task RunTestAsync()
    {
        var manifest = Collect().Normalized();
        testResult.Visibility = Visibility.Visible;
        if (manifest.ValidationError() is { } problem)
        {
            ShowTestResult(problem, failed: true);
            return;
        }
        var content = ContentClassifier.Classify(testInput.Text);
        testWarning.Text = "这段内容不满足「什么时候可用」，在圆盘里不会出现";
        testWarning.Visibility = PluginMatcher.Matches(manifest, content) ? Visibility.Collapsed : Visibility.Visible;
        testRun?.Cancel();
        using var run = new CancellationTokenSource();
        testRun = run;
        testProgress.Visibility = Visibility.Visible;
        testResult.Text = "";
        try
        {
            var result = await app.PluginRunner.ExecuteAsync(manifest.Action, PluginInput.From(content), run.Token);
            if (run.IsCancellationRequested) return;
            if (result.Error is { } failure) ShowTestResult(failure, failed: true);
            else ShowTestResult(result.Output.Length == 0 ? "（没有输出）" : result.Output, failed: false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            testProgress.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(testRun, run)) testRun = null;
        }
    }

    private void ShowTestResult(string text, bool failed)
    {
        testResult.Text = text;
        testResult.SetResourceReference(TextBlock.ForegroundProperty, failed ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
    }

    /// 打开编辑器（设置窗口是它的主窗口），保存了返回插件
    public static PluginManifest? Edit(Window owner, App app, PluginManifest manifest, bool isNew)
    {
        var editor = new PluginEditorWindow(app, manifest, isNew) { Owner = owner };
        return editor.ShowDialog() == true ? editor.Saved : null;
    }
}
