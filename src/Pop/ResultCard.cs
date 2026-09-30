using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 结果卡片（带复制、替换原文按钮）和轻提示（一会儿自己消失）。毛玻璃底，出现在指针旁边，从指针所在的角长出来
internal sealed class ResultCard : OverlayWindow
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
    public event Action<string>? CopyRequested;

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
    }

    public bool IsOpen => IsVisible && root.Opacity > 0;

    /// 当前卡片「复制」按钮对应的文字（回车键用）
    public string? PrimaryText { get; private set; }

    public void ShowCard(int x, int y, string title, string body) => ShowResult(x, y, CardContent.Text(title, body));

    public void ShowResult(int x, int y, CardContent content)
    {
        PrimaryText = content.Loading ? null : content.PrimaryText;
        var theme = Theme.Current();
        var updating = IsOpen && !toast && anchor == (x, y);
        toast = false;
        Present(x, y, Card(content, theme), theme, Theme.OverlayRadius, updating);
        autoClose.Stop();
    }

    public void ShowToast(int x, int y, string message)
    {
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

    public void Dismiss()
    {
        if (!IsVisible) return;
        autoClose.Stop();
        Dismissed?.Invoke();
        FadeOutAndHide(root, 120);
    }

    private FrameworkElement Card(CardContent content, Theme theme)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 12, 14), MinWidth = MinCardWidth - 28, MaxWidth = MaxCardWidth - 28 };

        // 标题行：图标（或色块）、标题、关闭按钮
        var header = new DockPanel { LastChildFill = true };
        var close = new WpfUi.Button
        {
            Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.Dismiss16, FontSize = 12 },
            Appearance = WpfUi.ControlAppearance.Transparent,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            ToolTip = "关闭（Esc）",
        };
        close.Click += (_, _) => Dismiss();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
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
            Text = content.Title,
            FontSize = Theme.Caption,
            FontWeight = FontWeights.Medium,
            Foreground = theme.Brush(theme.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(header);

        // 原文（翻译卡片）
        if (!string.IsNullOrEmpty(content.Source))
        {
            panel.Children.Add(new TextBlock
            {
                Text = content.Source,
                FontSize = Theme.Caption,
                LineHeight = 18,
                Foreground = theme.Brush(theme.TertiaryText),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                Margin = new Thickness(0, 6, 4, 0),
            });
        }

        // 正文和结果行放在一个能滚动的区域里，内容很长时卡片不会超出屏幕
        var scrollContent = new StackPanel { Margin = new Thickness(8, 0, 4, 0) };
        var scroller = new ScrollViewer
        {
            Content = scrollContent,
            MaxHeight = MaxBodyHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false,
            Margin = new Thickness(-8, 0, -4, 0),
        };
        panel.Children.Add(scroller);

        if (content.Loading)
        {
            var loading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
            loading.Children.Add(new WpfUi.ProgressRing { IsIndeterminate = true, Width = 18, Height = 18, Progress = 30 });
            loading.Children.Add(new TextBlock
            {
                Text = content.Body ?? "",
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
            scrollContent.Children.Add(new TextBlock
            {
                Text = content.Body,
                FontFamily = content.Monospace && !prominent ? Theme.MonoFont : Theme.TextFont,
                FontSize = prominent ? Theme.BodyLarge : Theme.Body,
                LineHeight = prominent ? 25 : 21,
                TextWrapping = TextWrapping.Wrap,
                Foreground = theme.Brush(prominent ? theme.Text : theme.SecondaryText),
                Margin = new Thickness(0, content.Source is null ? 8 : 8, 4, content.Lines.Count > 0 ? 6 : 0),
            });
        }

        foreach (var line in content.Lines) scrollContent.Children.Add(LineRow(line, theme, content.Monospace));

        // 底部：说明文字、链接、替换原文、复制
        var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var link in content.Links ?? [])
        {
            var button = ActionButton(link.Title, WpfUi.ControlAppearance.Transparent, theme);
            button.Foreground = theme.Brush(theme.Accent);
            button.Click += (_, _) =>
            {
                Dismiss();
                LinkRequested?.Invoke(link.Url);
            };
            buttons.Children.Add(button);
        }
        if (!content.Loading && content.Replacement is { } replacement)
        {
            var replace = ActionButton("替换原文", WpfUi.ControlAppearance.Secondary, theme);
            replace.Click += (_, _) =>
            {
                Dismiss();
                ReplaceRequested?.Invoke(replacement);
            };
            buttons.Children.Add(replace);
        }
        if (!content.Loading && content.PrimaryText.Length > 0)
        {
            var copy = ActionButton("复制", WpfUi.ControlAppearance.Primary, theme);
            copy.Click += (_, _) =>
            {
                CopyRequested?.Invoke(content.PrimaryText);
                Dismiss();
            };
            buttons.Children.Add(copy);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(new TextBlock
        {
            Text = content.Caption ?? "",
            FontSize = 11,
            Foreground = theme.Brush(theme.TertiaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (buttons.Children.Count > 0 || !string.IsNullOrEmpty(content.Caption)) panel.Children.Add(footer);
        return panel;
    }

    /// 一行结果：左边说明，右边值；鼠标移上去变色，点一下复制这一行
    private FrameworkElement LineRow(ResultLine line, Theme theme, bool monospace)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = line.Label,
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var value = new TextBlock
        {
            Text = line.Value,
            FontFamily = monospace ? Theme.MonoFont : Theme.TextFont,
            FontSize = monospace ? 13 : Theme.Body,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = theme.Brush(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(value, 1);
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
            CopyRequested?.Invoke(line.Value);
            label.Text = "已复制 ✓";
            label.Foreground = theme.Brush(theme.Accent);
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
        body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Ceiling(body.DesiredSize.Width);
        var height = Math.Ceiling(body.DesiredSize.Height);
        var (work, monitorScale) = Native.MonitorAt(x, y);
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
        if (updating)
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
