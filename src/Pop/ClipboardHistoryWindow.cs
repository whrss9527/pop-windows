using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 剪贴板历史：不抢焦点，键盘输入由全局键盘钩子转过来。
/// 打字搜索（图片里识别出的文字也能搜），↑↓ 选择，回车粘贴，Ctrl+1–9 直接粘贴第几条，Delete 删除，Ctrl+P 固定
internal sealed class ClipboardHistoryWindow : SearchOverlay<ClipboardItem>
{
    private ClipboardStore? store;

    /// 要粘贴这一条
    public event Action<ClipboardItem>? Chosen;

    public ClipboardHistoryWindow() : base("Pop 剪贴板历史")
    {
    }

    protected override double PanelWidth => 420;
    protected override double ListHeight => 7 * 54;
    protected override string Placeholder => "搜索剪贴板历史";
    protected override IReadOnlyList<(string Keys, string Action)> Hints =>
        [("Enter", "粘贴"), ("Ctrl 1–9", "直接粘贴"), ("Del", "删除"), ("Ctrl P", "固定"), ("Esc", "关闭")];

    public void ShowAt(ClipboardStore history, int x, int y)
    {
        store = history;
        Open(x, y);
        Log.Info($"剪贴板历史已显示，{history.Items("", 200).Count} 条");
    }

    public void DeleteSelected()
    {
        if (store is null || SelectedItem is not { } item) return;
        store.Delete(item.Id);
        Reload();
    }

    public void TogglePin()
    {
        if (store is null || SelectedItem is not { } item) return;
        store.SetPinned(!item.Pinned, item.Id);
        Reload(store.Item(item.Id));
    }

    protected override IEnumerable<Entry> Query(string text)
    {
        var items = store?.Items(text, 200) ?? [];
        var pinned = items.Where(i => i.Pinned).ToList();
        var recent = items.Where(i => !i.Pinned).ToList();
        if (pinned.Count > 0)
        {
            yield return new Entry("已固定", null);
            foreach (var item in pinned) yield return new Entry(null, item);
        }
        if (recent.Count > 0)
        {
            if (pinned.Count > 0) yield return new Entry("最近", null);
            foreach (var item in recent) yield return new Entry(null, item);
        }
    }

    protected override FrameworkElement Row(ClipboardItem item, bool selected, bool enabled, int index)
    {
        var theme = Palette;
        var line = new DockPanel { Height = 52, LastChildFill = true };

        // 右边：Ctrl+数字
        if (index is >= 0 and < 9)
        {
            var keycap = Keycap($"Ctrl {index + 1}", theme);
            keycap.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(keycap, Dock.Right);
            line.Children.Add(keycap);
        }

        // 左边：缩略图或者图标
        FrameworkElement leading;
        if (item.Kind == ClipboardKind.Image && store?.ImagePath(item) is { } path && Thumbnail(path) is { } thumbnail)
        {
            leading = new Border
            {
                Width = 44,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                BorderBrush = theme.Brush(theme.Divider),
                BorderThickness = new Thickness(1),
                Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill },
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            var glyph = item.Kind switch { ClipboardKind.Image => "Image24", ClipboardKind.Files => "Folder24", _ => "TextAlignLeft24" };
            var badge = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = theme.Brush(theme.Hover),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Icons.Make(glyph, 16, theme.Brush(theme.SecondaryText)),
            };
            ((FrameworkElement)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)badge.Child).VerticalAlignment = VerticalAlignment.Center;
            leading = badge;
        }
        DockPanel.SetDock(leading, Dock.Left);
        line.Children.Add(leading);

        // 中间：两行，内容预览和时间来源
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = Preview(item),
            FontSize = Theme.Body,
            Foreground = theme.Brush(theme.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        if (item.Pinned)
        {
            var pin = Icons.Make(WpfUi.SymbolRegular.Pin12, 11, theme.Brush(theme.Accent), filled: true);
            pin.Margin = new Thickness(0, 0, 4, 0);
            pin.VerticalAlignment = VerticalAlignment.Center;
            meta.Children.Add(pin);
        }
        meta.Children.Add(new TextBlock
        {
            Text = Meta(item),
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.TertiaryText),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(meta);
        line.Children.Add(text);
        line.ToolTip = item.Kind == ClipboardKind.Text && item.Text.Length > 60 ? Shorten(item.Text, 600) : null;
        return line;
    }

    protected override string EmptyText(string text) =>
        text.Length == 0 ? "还没有记录。复制过的文字、图片和文件会出现在这里" : $"没有找到「{text}」";

    protected override void OnChosen(ClipboardItem item) => Chosen?.Invoke(item);

    private static string Preview(ClipboardItem item) => item.Kind switch
    {
        ClipboardKind.Files => item.FilePaths.Count == 1
            ? Path.GetFileName(item.FilePaths[0].TrimEnd('\\'))
            : $"{item.FilePaths.Count} 个文件：{string.Join("、", item.FilePaths.Take(3).Select(p => Path.GetFileName(p.TrimEnd('\\'))))}",
        ClipboardKind.Image => string.IsNullOrWhiteSpace(item.RecognizedText) ? "图片" : Shorten(item.RecognizedText, 80),
        _ => Shorten(item.Text, 120),
    };

    private static string Meta(ClipboardItem item)
    {
        var parts = new List<string> { Ago(item.UsedAt, DateTimeOffset.Now) };
        parts.Add(item.Kind switch
        {
            ClipboardKind.Image => "图片",
            ClipboardKind.Files => "文件",
            _ => $"{item.Text.Length} 字",
        });
        if (!string.IsNullOrEmpty(item.SourceApp)) parts.Add(item.SourceApp);
        return string.Join(" · ", parts);
    }

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
