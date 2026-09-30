using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pop.Core;

namespace Pop;

/// 剪贴板历史：和圆盘一样不抢焦点，键盘输入由全局键盘钩子转过来。
/// 打字搜索（图片里识别出的文字也能搜），↑↓ 选择，回车粘贴，Ctrl+1–9 直接粘贴第几条，Delete 删除，Ctrl+P 固定
internal sealed class ClipboardHistoryWindow : OverlayWindow
{
    private const double ListWidth = 380;
    private const double RowHeight = 40;
    private const int VisibleRows = 9;

    private readonly Border root = new();
    private readonly TextBlock query = new();
    private readonly StackPanel rows = new();
    private readonly ScrollViewer scroller = new();
    private readonly TextBlock empty = new();
    private ClipboardStore? store;
    private IReadOnlyList<ClipboardItem> items = [];
    private string text = "";
    private int selected;
    private Theme theme = Theme.Current();

    /// 要粘贴这一条
    public event Action<ClipboardItem>? Chosen;

    public ClipboardHistoryWindow() : base(clickThrough: false)
    {
        Title = "Pop 剪贴板历史";
        Content = root;
        scroller.Content = rows;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroller.Focusable = false;
    }

    public bool IsOpen => IsVisible && root.Opacity > 0;

    public void ShowAt(ClipboardStore history, int x, int y)
    {
        store = history;
        text = "";
        theme = Theme.Current();

        query.FontFamily = Theme.TextFont;
        query.FontSize = 14;
        if (query.Parent is Border oldQuery) oldQuery.Child = null;
        var searchBox = new Border
        {
            Child = query,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(theme.Segment),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var hint = new TextBlock
        {
            Text = "回车粘贴　Ctrl+1–9 直接粘贴　Delete 删除　Ctrl+P 固定　Esc 关闭",
            FontFamily = Theme.TextFont,
            FontSize = 11,
            Margin = new Thickness(4, 6, 4, 0),
            Foreground = new SolidColorBrush(theme.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
        };
        empty.FontFamily = Theme.TextFont;
        empty.FontSize = 13;
        empty.Margin = new Thickness(8, 12, 8, 12);
        empty.Foreground = new SolidColorBrush(theme.SecondaryText);

        var panel = new DockPanel { Width = ListWidth };
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(searchBox);
        panel.Children.Add(hint);
        if (scroller.Parent is Panel oldPanel) oldPanel.Children.Remove(scroller);
        scroller.Height = VisibleRows * RowHeight;
        panel.Children.Add(scroller);

        root.Child = panel;
        root.Padding = new Thickness(10);
        root.Margin = new Thickness(12);
        root.CornerRadius = new CornerRadius(12);
        root.Background = new SolidColorBrush(theme.Surface);
        root.BorderBrush = new SolidColorBrush(theme.SurfaceBorder);
        root.BorderThickness = new Thickness(1);
        root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = theme.Dark ? 0.5 : 0.2, Direction = 270 };

        Refresh();

        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (work, scale) = Native.MonitorAt(x, y);
        var w = (int)Math.Ceiling(root.DesiredSize.Width * scale);
        var h = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        var left = (int)Math.Clamp(x - w / 2.0, work.Left, Math.Max(work.Left, work.Right - w));
        var top = (int)Math.Clamp(y - 30 * scale, work.Top, Math.Max(work.Top, work.Bottom - h));
        Width = root.DesiredSize.Width;
        Height = root.DesiredSize.Height;
        MarkShowing();
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 1;
        PlacePhysical(left, top, w, h);
        Show();
        Dispatcher.BeginInvoke(() => PlacePhysical(left, top, w, h), System.Windows.Threading.DispatcherPriority.Loaded);
        Log.Info($"剪贴板历史已显示，{items.Count} 条");
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        FadeOutAndHide(root, 90);
    }

    public void Type(char c)
    {
        text += c;
        Refresh();
    }

    public void Backspace()
    {
        if (text.Length == 0) return;
        text = text[..^1];
        Refresh();
    }

    public void Move(int delta)
    {
        if (items.Count == 0) return;
        selected = (selected + delta + items.Count) % items.Count;
        Paint();
    }

    public void Choose() => ChooseAt(selected);

    /// Ctrl+数字：直接粘贴第几条（从 0 开始）
    public void ChooseAt(int index)
    {
        if (index < 0 || index >= items.Count) return;
        var item = items[index];
        Dismiss();
        Chosen?.Invoke(item);
    }

    public void DeleteSelected()
    {
        if (store is null || selected < 0 || selected >= items.Count) return;
        store.Delete(items[selected].Id);
        var keep = selected;
        Refresh();
        selected = Math.Min(keep, items.Count - 1);
        Paint();
    }

    public void TogglePin()
    {
        if (store is null || selected < 0 || selected >= items.Count) return;
        var item = items[selected];
        store.SetPinned(!item.Pinned, item.Id);
        Refresh();
        selected = Math.Max(0, items.ToList().FindIndex(i => i.Id == item.Id));
        Paint();
    }

    private void Refresh()
    {
        query.Text = text.Length == 0 ? "搜索剪贴板历史" : text + "▏";
        query.Foreground = new SolidColorBrush(text.Length == 0 ? theme.SecondaryText : theme.Text);
        items = store?.Items(text, 200) ?? [];
        selected = items.Count > 0 ? 0 : -1;
        empty.Text = text.Length == 0 ? "还没有记录。复制过的文字、图片和文件会出现在这里" : "没有找到";
        Paint();
    }

    private void Paint()
    {
        rows.Children.Clear();
        if (items.Count == 0)
        {
            if (empty.Parent is Panel p) p.Children.Remove(empty);
            rows.Children.Add(empty);
            return;
        }
        var now = DateTimeOffset.Now;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var isSelected = i == selected;
            var foreground = new SolidColorBrush(isSelected ? theme.AccentText : theme.Text);
            var secondary = new SolidColorBrush(isSelected ? theme.AccentText : theme.SecondaryText);

            var line = new DockPanel { LastChildFill = true };
            // 右边：固定标记（图标字体）+ 时间和 Ctrl+数字 序号（文字字体）
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (item.Pinned)
                right.Children.Add(new TextBlock { Text = "\uE718", FontFamily = Theme.IconFont, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = secondary });
            right.Children.Add(new TextBlock
            {
                Text = i < 9 ? $"{Ago(item.UsedAt, now)}　Ctrl+{i + 1}" : Ago(item.UsedAt, now),
                FontFamily = Theme.TextFont,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = secondary,
            });
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);

            FrameworkElement icon = new TextBlock
            {
                Text = item.Kind switch { ClipboardKind.Image => "", ClipboardKind.Files => "", _ => "" },
                FontFamily = Theme.IconFont,
                FontSize = 15,
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = secondary,
            };
            if (item.Kind == ClipboardKind.Image && store?.ImagePath(item) is { } path && Thumbnail(path) is { } thumbnail)
                icon = new Image { Source = thumbnail, Width = 44, Height = 32, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);

            line.Children.Add(new TextBlock
            {
                Text = Preview(item),
                FontFamily = Theme.TextFont,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = foreground,
            });

            var row = new Border
            {
                Child = line,
                Height = RowHeight,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? new SolidColorBrush(theme.Accent) : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = item.Kind == ClipboardKind.Text && item.Text.Length > 60 ? Shorten(item.Text, 600) : null,
            };
            var index = i;
            row.MouseLeftButtonUp += (_, _) => ChooseAt(index);
            rows.Children.Add(row);
        }
        if (selected >= 0)
        {
            var offset = selected * RowHeight;
            if (offset < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(offset);
            else if (offset + RowHeight > scroller.VerticalOffset + scroller.Height) scroller.ScrollToVerticalOffset(offset + RowHeight - scroller.Height);
        }
    }

    private static string Preview(ClipboardItem item) => item.Kind switch
    {
        ClipboardKind.Files => item.FilePaths.Count == 1 ? Path.GetFileName(item.FilePaths[0].TrimEnd('\\')) : $"{item.FilePaths.Count} 个文件：{string.Join("、", item.FilePaths.Take(3).Select(p => Path.GetFileName(p.TrimEnd('\\'))))}",
        ClipboardKind.Image => string.IsNullOrWhiteSpace(item.RecognizedText) ? "图片" : "图片：" + Shorten(item.RecognizedText, 60),
        _ => Shorten(item.Text, 120),
    };

    private static string Shorten(string text, int max)
    {
        var flat = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length > max ? flat[..max] + "…" : flat;
    }

    private static string Ago(DateTimeOffset time, DateTimeOffset now)
    {
        var span = now - time;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        return time.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static BitmapImage? Thumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 64;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
