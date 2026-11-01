using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 结果卡片（带复制、替换原文按钮）和轻提示（一会儿自己消失）。毛玻璃底，出现在指针旁边，从指针所在的角长出来
internal sealed partial class ResultCard : OverlayWindow
{
    private const double MinCardWidth = 280;
    private const double MaxCardWidth = 440;
    /// 第一次弹出时按这个大小截下背景，之后内容变了（翻译结果回来了）也够用
    private const double MaxCardHeight = 560;

    /// 正文和结果行最多这么高，再多就滚动
    private const double MaxBodyHeight = 400;

    private readonly Grid root = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer autoClose = new();
    private Frost.Shot? shot;
    private (int X, int Y) anchor;
    private bool toast;

    /// 点了「复制」按钮或者某一行
    public event Func<string, bool>? CopyRequested;

    private WpfUi.Button? copyButton;
    private bool copyPreviewTruncated;
    private bool primaryCopyFailed;
    public bool CopyFailed { get; private set; }

    public bool CopyPrimary()
    {
        if (PrimaryText is not { Length: > 0 } text || !TryCopy(text)) return false;
        Dismiss();
        return true;
    }

    private bool TryCopy(string text)
    {
        var copied = CopyRequested?.Invoke(text) == true;
        SetCopyState(copied);
        return copied;
    }

    private void SetCopyState(bool copied)
    {
        CopyFailed = !copied;
        if (reading is null || string.Equals(PrimaryText, shownContent?.PrimaryText, StringComparison.Ordinal))
            primaryCopyFailed = CopyFailed;
        UpdateCopyButton();
    }

    private void UpdateCopyButton()
    {
        if (copyButton is not null)
        {
            copyButton.Content = !CopyFailed ? reading is null ? "复制" : "复制全文" : "重试复制";
            copyButton.ToolTip = !CopyFailed ? reading is null ? "复制主要结果（Enter），包含全部内容" : "复制当前阅读项的全文（Enter），包含所有页" : copyPreviewTruncated
                ? "尚未复制。点击或按 Enter 重试，会复制完整结果"
                : "剪贴板暂时不可写，尚未复制；再次点击或按 Enter 重试";
        }
    }

    public void ShowCopyFailure(int x, int y, string title, string text, string? instruction = null)
    {
        var truncated = text.Length > ResultPreview.BodyCharacters;
        var message = instruction ?? "剪贴板暂时不可写，完整结果已保留。";
        if (truncated) message += "预览已简略显示，可查看全文；重试会复制全部内容。";
        ShowResult(x, y, new CardContent(title, [], Body: text, Caption: "未复制 · 可重试", Source: message));
        PrimaryText = text; copyPreviewTruncated = truncated;
        SetCopyState(false);
    }

    /// 点了「替换原文」
    public event Action<string>? ReplaceRequested;

    /// 点了底部的链接（在浏览器中打开）
    public event Action<string>? LinkRequested;

    /// 卡片开始收起（按钮、Esc、点别处、轻提示到时间）
    public event Action? Dismissed;

    public ResultCard() : base(clickThrough: false)
    {
        Title = "Pop 结果";
        root.RenderTransform = scale;
        Content = root;
        autoClose.Tick += (_, _) =>
        {
            autoClose.Stop();
            Dismiss();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            PrimaryText = null; copyButton = null; copyPreviewTruncated = false; CopyFailed = false; primaryCopyFailed = false;
            shownContent = null; reading = null; cardScroller = null; readingTitle = "";
            root.Children.Clear(); shot = null;
        };
    }

    public bool IsOpen => IsVisible && root.Opacity > 0;

    /// 当前卡片「复制」按钮对应的文字（回车键用）
    public string? PrimaryText { get; private set; }

    public void ShowCard(int x, int y, string title, string body) => ShowResult(x, y, CardContent.Text(title, body));

    public void ShowResult(int x, int y, CardContent content)
    {
        // 新结果可能紧接着上一次复制的收起动画，先阻止旧动画隐藏新内容。
        autoClose.Stop();
        MarkShowing();
        CopyFailed = false;
        primaryCopyFailed = false;
        copyPreviewTruncated = false;
        copyButton = null;
        readingActionsEnabled = true;
        shownContent = content; reading = null; readingTitle = ""; resultsPage = 0;
        PrimaryText = content.Loading ? null : content.PrimaryText;
        copyPreviewTruncated = PrimaryText?.Length > ResultPreview.BodyCharacters;
        var theme = Theme.Current();
        var updating = IsOpen && !toast && anchor == (x, y);
        toast = false;
        Present(x, y, Card(content, theme), theme, Theme.OverlayRadius, updating);
        autoClose.Stop();
    }

    public void ShowToast(int x, int y, string message)
    {
        autoClose.Stop();
        MarkShowing();
        PrimaryText = null;
        readingActionsEnabled = false;
        shownContent = null; reading = null; cardScroller = null; readingTitle = "";
        copyButton = null;
        CopyFailed = false;
        primaryCopyFailed = false;
        copyPreviewTruncated = false;
        var theme = Theme.Current();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 9, 16, 9) };
        var icon = Icons.Make(Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20, 16, theme.Brush(theme.Accent), filled: true);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = Theme.Body,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = theme.Brush(theme.Text),
        });
        toast = true;
        Present(x, y, row, theme, 19, updating: false);
        autoClose.Interval = TimeSpan.FromMilliseconds(1300 * AnimationScale);
        autoClose.Start();
    }

    /// 第一次显示卡片要载入 WPF-UI 按钮、滚动区域的样式和模板，要半秒左右，这期间长按、松开都在排队。
    /// 启动后界面空闲时先在这个（还没显示的）窗口里搭一张卡片、量一下尺寸，把它们载入好
    public void WarmUp()
    {
        if (IsVisible) return;
        var theme = Theme.Current();
        var sample = new CardContent("字数", [new ResultLine("字符", "15"), new ResultLine("单词", "3")],
            Replacement: "HELLO POP WORLD", Caption: "示例", Links: [new CardLink("在浏览器中打开", "https://example.com")], Icon: "Copy24");
        root.Children.Add(Frost.Panel(null, 0, 0, MinCardWidth, 160, Theme.OverlayRadius, theme, Card(sample, theme)));
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new System.Windows.Rect(root.DesiredSize));
        root.Children.Clear();
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        readingActionsEnabled = false;
        autoClose.Stop();
        Dismissed?.Invoke();
        FadeOutAndHide(root, 120);
    }

    private FrameworkElement Card(CardContent content, Theme theme)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 12, 14), MinWidth = MinCardWidth - 28, MaxWidth = MaxCardWidth - 28 };
        // 测量时尚未接到窗口上，也要使用实际字体，避免接入后换行变高、底部按钮被裁掉。
        System.Windows.Documents.TextElement.SetFontFamily(panel, Theme.TextFont);

        // 标题行：图标（或色块）、标题、关闭按钮
        var header = new DockPanel { LastChildFill = true };
        var close = Subtle(new WpfUi.Button
        {
            Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.Dismiss16, FontSize = 12 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            ToolTip = "关闭（Esc）",
        }, theme);
        close.Click += (_, _) => Dismiss();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        // 说明文字（「英语 → 简体中文 · 必应翻译」）放在标题行右边
        if (!string.IsNullOrEmpty(content.Caption))
        {
            var caption = new TextBlock
            {
                Text = ResultPreview.Text(content.Caption, 120),
                FontSize = 11,
                Foreground = theme.Brush(theme.TertiaryText),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 160,
                Margin = new Thickness(12, 0, 6, 0),
            };
            DockPanel.SetDock(caption, Dock.Right);
            header.Children.Add(caption);
        }
        if (content.Swatch is { } swatch && TryColor(swatch) is { } color)
        {
            var chip = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(color),
                BorderBrush = theme.Brush(theme.Stroke),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(chip, Dock.Left);
            header.Children.Add(chip);
        }
        else if (content.Icon is { } iconName)
        {
            var icon = Icons.Make(iconName, 16, theme.Brush(theme.Accent));
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
        }
        header.Children.Add(new TextBlock
        {
            Text = ResultPreview.Text(content.Title, 120),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = Theme.Caption,
            FontWeight = FontWeights.Medium,
            Foreground = theme.Brush(theme.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(header);
        if (reading is not null) panel.Children.Add(ReadingNavigation(theme));

        // 原文（翻译卡片）
        if (!string.IsNullOrEmpty(content.Source))
        {
            panel.Children.Add(new TextBlock
            {
                Text = ResultPreview.Text(content.Source, ResultPreview.SourceCharacters),
                FontSize = Theme.Caption,
                LineHeight = 18,
                Foreground = theme.Brush(theme.TertiaryText),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                Margin = new Thickness(0, 6, 4, 0),
            });
            if (reading is null && !content.Loading && ResultPreview.SourceNeedsReader(content.Source))
                panel.Children.Add(ReadButton(content, content.Source, "原文", "card-read-source", theme));
        }

        // 正文和结果行放在一个能滚动的区域里，内容很长时卡片不会超出屏幕
        var scrollContent = new StackPanel { Margin = new Thickness(8, 0, 4, 0) };
        var scroller = new ScrollViewer
        {
            Content = scrollContent,
            MaxHeight = reading is not null ? 320 : content.Source is { } source && ResultPreview.SourceNeedsReader(source) ? 368 : MaxBodyHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false,
            Margin = new Thickness(-8, 0, -4, 0),
        };
        cardScroller = scroller;
        panel.Children.Add(scroller);

        if (content.Loading)
        {
            var loading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
            loading.Children.Add(new WpfUi.ProgressRing { IsIndeterminate = true, Width = 18, Height = 18, Progress = 30 });
            loading.Children.Add(new TextBlock
            {
                Text = ResultPreview.Text(content.Body ?? "", 240),
                FontSize = Theme.Body,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = theme.Brush(theme.SecondaryText),
            });
            scrollContent.Children.Add(loading);
        }
        else if (!string.IsNullOrEmpty(content.Body))
        {
            var prominent = content.Lines.Count == 0;
            if (reading is null && content.Body.Length > ResultPreview.BodyCharacters)
                scrollContent.Children.Add(ReadButton(content, content.Body, "正文", "card-read-body", theme));
            scrollContent.Children.Add(new TextBlock
            {
                Text = ResultPreview.Text(content.Body, ResultPreview.BodyCharacters),
                // 等宽字体只给代码一样的正文（格式化好的 JSON）；结果行上面的说明文字用正文字体
                FontFamily = content.Monospace && prominent ? Theme.MonoFont : Theme.TextFont,
                FontSize = prominent ? Theme.BodyLarge : Theme.Body,
                LineHeight = prominent ? 25 : 21,
                TextWrapping = TextWrapping.Wrap,
                Foreground = theme.Brush(prominent ? theme.Text : theme.SecondaryText),
                Margin = new Thickness(0, content.Source is null ? 8 : 8, 4, content.Lines.Count > 0 ? 6 : 0),
            });
        }

        var (first, count) = ResultPreview.Lines(content.Lines.Count, resultsPage);
        if (content.Lines.Count > ResultPreview.LinesPerPage) scrollContent.Children.Add(ResultNavigation(content, first, count, theme));
        for (var i = first; i < first + count; i++)
        {
            var line = content.Lines[i];
            if (ResultPreview.LineNeedsReader(line.Value))
                scrollContent.Children.Add(ReadButton(content, line.Value, line.Label, $"card-read-line-{i}", theme));
            var row = LineRow(line, theme, content.Monospace);
            System.Windows.Automation.AutomationProperties.SetAutomationId(row, $"card-result-line-{i}");
            scrollContent.Children.Add(row);
        }

        // 底部：左边是链接，右边是替换原文、复制
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 14, 0, 0) };
        if (reading is not null)
        {
            var back = Subtle(ActionButton("返回结果", WpfUi.ControlAppearance.Transparent, theme), theme);
            back.Margin = new Thickness(-8, 0, 0, 0);
            back.Click += (_, _) => ReturnFromReading();
            System.Windows.Automation.AutomationProperties.SetAutomationId(back, "card-read-back");
            DockPanel.SetDock(back, Dock.Left); footer.Children.Add(back);
        }
        foreach (var link in content.Links ?? [])
        {
            var button = Subtle(ActionButton(link.Title, WpfUi.ControlAppearance.Transparent, theme), theme);
            button.Foreground = theme.Brush(theme.Accent);
            button.Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.Open16, FontSize = 13 };
            button.Margin = new Thickness(-8, 0, 0, 0);
            button.Padding = new Thickness(8, 0, 10, 0);
            button.Click += (_, _) =>
            {
                Dismiss();
                LinkRequested?.Invoke(link.Url);
            };
            DockPanel.SetDock(button, Dock.Left);
            footer.Children.Add(button);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (!content.Loading && content.Replacement is { } replacement)
        {
            var replace = ActionButton("替换原文", WpfUi.ControlAppearance.Secondary, theme);
            System.Windows.Automation.AutomationProperties.SetAutomationId(replace, "card-replace-result");
            replace.Click += (_, _) =>
            {
                Dismiss();
                ReplaceRequested?.Invoke(replacement);
            };
            buttons.Children.Add(replace);
        }
        if (!content.Loading && content.PrimaryText.Length > 0)
        {
            var copy = ActionButton(reading is null ? "复制" : "复制全文", WpfUi.ControlAppearance.Primary, theme);
            copy.Width = reading is null ? 88 : 100;
            copy.ToolTip = reading is null ? "复制主要结果（Enter），包含全部内容" : "复制当前阅读项的全文（Enter），包含所有页";
            copyButton = copy;
            System.Windows.Automation.AutomationProperties.SetAutomationId(copy, "card-copy-result");
            copy.Click += (_, _) => CopyPrimary();
            buttons.Children.Add(copy);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        if (footer.Children.Count > 1 || buttons.Children.Count > 0) panel.Children.Add(footer);
        return panel;
    }

    /// 一行结果：左边说明，右边值；鼠标移上去变色，点一下复制这一行
    private FrameworkElement LineRow(ResultLine line, Theme theme, bool monospace)
    {
        // 多行的值（格式化的 JSON 之类）：说明放在上面，值占满整行
        var preview = ResultPreview.Text(line.Value, ResultPreview.LineCharacters);
        var stacked = preview.Contains('\n') || line.Value.Length > ResultPreview.LineCharacters;
        var grid = new Grid();
        if (stacked)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var label = new TextBlock
        {
            Text = ResultPreview.Text(line.Label, 100),
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.SecondaryText),
            VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, stacked ? 4 : 0),
        };
        var value = new TextBlock
        {
            Text = preview,
            FontFamily = monospace ? Theme.MonoFont : Theme.TextFont,
            FontSize = monospace ? 13 : Theme.Body,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = ResultPreview.LineNeedsReader(line.Value) ? 140 : double.PositiveInfinity,
            Foreground = theme.Brush(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (stacked) Grid.SetRow(value, 1);
        else Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(-8, 0, -4, 0),
            CornerRadius = new CornerRadius(Theme.ControlRadius),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "点一下复制",
        };
        var hover = theme.Brush(theme.Hover);
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            // 行内反馈只属于这一项，不能把底部主要结果的按钮改成重试其他内容。
            var copied = CopyRequested?.Invoke(line.Value) == true;
            label.Text = copied ? "已复制 ✓" : "未复制，请重试";
            label.Foreground = theme.Brush(copied ? theme.Accent : theme.SecondaryText);
        };
        return row;
    }

    private static WpfUi.Button ActionButton(string text, WpfUi.ControlAppearance appearance, Theme theme) => new()
    {
        Content = text,
        Appearance = appearance,
        Height = 30,
        MinWidth = 64,
        Padding = new Thickness(14, 0, 14, 0),
        Margin = new Thickness(8, 0, 0, 0),
        FontSize = 13,
        Focusable = false,
        Cursor = Cursors.Hand,
    };

    /// 没有底色和边框的按钮：鼠标移上去才显示浅色的底
    private static WpfUi.Button Subtle(WpfUi.Button button, Theme theme)
    {
        button.Appearance = WpfUi.ControlAppearance.Transparent;
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.MouseOverBackground = theme.Brush(theme.Hover);
        button.MouseOverBorderBrush = Brushes.Transparent;
        button.PressedBackground = theme.Brush(theme.Selected);
        button.PressedBorderBrush = Brushes.Transparent;
        return button;
    }

    private static Color? TryColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// 摆到指针旁边：默认在右下方，放不下就翻到左边或上边；卡片从离指针最近的那个角长出来
    private void Present(int x, int y, FrameworkElement body, Theme theme, double radius, bool updating)
    {
        MarkShowing();
        root.BeginAnimation(OpacityProperty, null);
        var (work, monitorScale) = Native.MonitorAt(x, y);
        body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var availableHeight = Math.Min(MaxCardHeight, (work.Bottom - work.Top) / monitorScale - 2 * Theme.ShadowPad - 12);
        if (VerificationHeightLimit is { } verification) availableHeight = Math.Min(availableHeight, verification);
        if (cardScroller is not null && body.DesiredSize.Height > availableHeight)
        {
            cardScroller.MaxHeight = Math.Max(48, cardScroller.MaxHeight - (body.DesiredSize.Height - availableHeight));
            body.InvalidateMeasure();
            body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        var width = Math.Ceiling(body.DesiredSize.Width);
        var height = Math.Ceiling(body.DesiredSize.Height);
        var w = (int)Math.Ceiling(width * monitorScale);
        var h = (int)Math.Ceiling(height * monitorScale);
        var gap = (int)(6 * monitorScale);
        var right = x + gap + w <= work.Right;
        var below = y + gap + h <= work.Bottom;
        var left = right ? x + gap : x - gap - w;
        var top = below ? y + gap : y - gap - h;
        left = (int)Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
        top = (int)Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - h));

        if (!updating || shot is null)
        {
            // 按最大尺寸截下背景，内容变高了也够用
            var maxW = (int)Math.Ceiling(MaxCardWidth * monitorScale);
            var maxH = (int)Math.Ceiling(MaxCardHeight * monitorScale);
            var shotLeft = right ? left : left + w - maxW;
            var shotTop = below ? top : top + h - maxH;
            shot = Frost.Capture(shotLeft, shotTop, maxW, maxH, monitorScale);
        }
        anchor = (x, y);

        var panel = Frost.Panel(shot, left, top, width, height, radius, theme, body);
        panel.Margin = new Thickness(Theme.ShadowPad);
        root.Children.Clear();
        root.Children.Add(panel);

        var pad = (int)Math.Round(Theme.ShadowPad * monitorScale);
        Width = width + 2 * Theme.ShadowPad;
        Height = height + 2 * Theme.ShadowPad;
        MarkShowing();
        PlacePhysical(left - pad, top - pad, w + 2 * pad, h + 2 * pad);
        if (updating && IsVisible)
        {
            root.BeginAnimation(OpacityProperty, null);
            root.Opacity = 1;
            return;
        }
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        Show();
        Dispatcher.BeginInvoke(() => PlacePhysical(left - pad, top - pad, w + 2 * pad, h + 2 * pad), System.Windows.Threading.DispatcherPriority.Loaded);

        scale.CenterX = Theme.ShadowPad + (right ? 0 : width);
        scale.CenterY = Theme.ShadowPad + (below ? 0 : height);
        var ease = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, Ms(220)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, Ms(220)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(140)));
    }
}
