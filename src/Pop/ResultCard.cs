using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pop.Core;

namespace Pop;

/// 结果卡片（带复制按钮）和轻提示（一会儿自己消失）。出现在指针旁边，从指针所在的角长出来
internal sealed class ResultCard : OverlayWindow
{
    private readonly Border root = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer autoClose = new();

    /// 点了「复制」按钮或者某一行
    public event Action<string>? CopyRequested;

    /// 点了「替换原文」
    public event Action<string>? ReplaceRequested;

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
        PrimaryText = content.PrimaryText;
        var theme = Theme.Current();
        var panel = new StackPanel { MaxWidth = 400, MinWidth = 220 };

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var close = SmallButton("\uE711", theme, "关闭");
        close.Click += (_, _) => Dismiss();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        if (content.Swatch is { } swatch && TryColor(swatch) is { } color)
        {
            var chip = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(theme.SurfaceBorder),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(chip, Dock.Left);
            header.Children.Add(chip);
        }
        header.Children.Add(new TextBlock
        {
            Text = content.Title,
            FontFamily = Theme.TextFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(theme.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(header);

        if (!string.IsNullOrEmpty(content.Body))
        {
            panel.Children.Add(new TextBlock
            {
                Text = content.Body,
                FontFamily = Theme.TextFont,
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(theme.Text),
            });
        }

        foreach (var line in content.Lines) panel.Children.Add(LineRow(line, theme));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (content.Replacement is { } replacement)
        {
            var replace = ActionButton("替换原文", theme, primary: false);
            replace.Click += (_, _) =>
            {
                Dismiss();
                ReplaceRequested?.Invoke(replacement);
            };
            buttons.Children.Add(replace);
        }
        var copy = ActionButton("复制", theme, primary: true);
        copy.Click += (_, _) =>
        {
            CopyRequested?.Invoke(content.PrimaryText);
            Dismiss();
        };
        buttons.Children.Add(copy);
        panel.Children.Add(buttons);

        Present(x, y, panel, theme, cornerRadius: 12, padding: new Thickness(16, 12, 12, 14));
        autoClose.Stop();
    }

    /// 一行结果：左边说明，右边值；鼠标移上去变色，点一下复制这一行
    private UIElement LineRow(ResultLine line, Theme theme)
    {
        var grid = new Grid { Margin = new Thickness(-6, 0, -6, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = line.Label,
            FontFamily = Theme.TextFont,
            FontSize = 12,
            Foreground = new SolidColorBrush(theme.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var value = new TextBlock
        {
            Text = line.Value,
            FontFamily = Theme.TextFont,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "点一下复制",
        };
        var hover = new SolidColorBrush(theme.Segment);
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            CopyRequested?.Invoke(line.Value);
            label.Text = "已复制";
            label.Foreground = new SolidColorBrush(theme.Accent);
        };
        return row;
    }

    private static Button ActionButton(string text, Theme theme, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(14, 4, 14, 4),
        Margin = new Thickness(8, 0, 0, 0),
        Focusable = false,
        FontFamily = Theme.TextFont,
        Background = primary ? new SolidColorBrush(theme.Accent) : new SolidColorBrush(theme.Segment),
        Foreground = primary ? new SolidColorBrush(theme.AccentText) : new SolidColorBrush(theme.Text),
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand,
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

    public void ShowToast(int x, int y, string message)
    {
        var theme = Theme.Current();
        var label = new TextBlock
        {
            Text = message,
            FontFamily = Theme.TextFont,
            FontSize = 13,
            Foreground = new SolidColorBrush(theme.Text),
        };
        Present(x, y, label, theme, cornerRadius: 16, padding: new Thickness(14, 7, 14, 7));
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

    private void Present(int x, int y, UIElement content, Theme theme, double cornerRadius, Thickness padding)
    {
        root.Child = content;
        root.Padding = padding;
        root.CornerRadius = new CornerRadius(cornerRadius);
        root.Background = new SolidColorBrush(theme.Surface);
        root.BorderBrush = new SolidColorBrush(theme.SurfaceBorder);
        root.BorderThickness = new Thickness(1);
        root.Margin = new Thickness(12);
        root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = theme.Dark ? 0.5 : 0.2, Direction = 270 };

        var (work, monitorScale) = Native.MonitorAt(x, y);
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = (int)Math.Ceiling(root.DesiredSize.Width * monitorScale);
        var height = (int)Math.Ceiling(root.DesiredSize.Height * monitorScale);

        // 默认在指针右下方；放不下就翻到左边或上边，卡片从离指针最近的那个角长出来
        var offset = (int)(4 * monitorScale);
        var right = x + offset + width <= work.Right;
        var below = y + offset + height <= work.Bottom;
        var left = right ? x + offset : x - offset - width;
        var top = below ? y + offset : y - offset - height;
        left = (int)Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = (int)Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));

        Width = root.DesiredSize.Width;
        Height = root.DesiredSize.Height;
        MarkShowing();
        PlacePhysical(left, top, width, height);
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        Show();
        Dispatcher.BeginInvoke(() => PlacePhysical(left, top, width, height), System.Windows.Threading.DispatcherPriority.Loaded);

        scale.CenterX = right ? 0 : root.DesiredSize.Width;
        scale.CenterY = below ? 0 : root.DesiredSize.Height;
        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, Ms(200)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, Ms(200)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(130)));
    }

    private static Button SmallButton(string glyph, Theme theme, string tooltip) => new()
    {
        Content = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 10, Foreground = new SolidColorBrush(theme.SecondaryText) },
        ToolTip = tooltip,
        Width = 22,
        Height = 22,
        Focusable = false,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand,
    };
}
