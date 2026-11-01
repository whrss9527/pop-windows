using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 剪贴板历史：不抢焦点，键盘输入由全局键盘钩子转过来。
/// 打字搜索（图片里识别出的文字也能搜），↑↓ 选择，回车粘贴，Ctrl+1–9 直接粘贴第几条，Delete 删除，Ctrl+P 固定
internal sealed class ClipboardHistoryWindow : SearchOverlay<ClipboardItem>
{
    private ClipboardStore? store;
    private readonly ClipboardThumbnailCache thumbnails = new();
    private int thumbnailGeneration;
    private static readonly ClipboardKind?[] Kinds = [null, ClipboardKind.Text, ClipboardKind.Image, ClipboardKind.Files];
    private ClipboardKind? kind;
    private bool pinnedOnly;
    private readonly List<(Border Shell, TextBlock Label, ClipboardKind? Kind, bool Pins)> filters = [];

    /// 要粘贴这一条
    public event Action<ClipboardItem>? Chosen;

    public ClipboardHistoryWindow() : base("Pop 剪贴板历史")
    {
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            thumbnailGeneration++; thumbnails.Clear(); store = null;
        };
    }

    protected override double PanelWidth => 420;
    protected override double ListHeight => 7 * 54;
    protected override string Placeholder => "搜索剪贴板历史";
    protected override IReadOnlyList<(string Keys, string Action)> Hints =>
        [("Enter", "粘贴"), ("Ctrl 1–9", "直接粘贴"), ("Del", "删除"), ("Ctrl P", "固定"), ("Tab", "分类"), ("Ctrl Shift P", "只看固定")];

    public void ShowAt(ClipboardStore history, int x, int y)
    {
        thumbnailGeneration++; thumbnails.Clear();
        kind = null; pinnedOnly = false;
        store = history;
        Open(x, y);
        Log.Info("剪贴板历史已显示");
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
        var items = store?.Items(text, 200, kind, pinnedOnly) ?? [];
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
        if (item.Kind == ClipboardKind.Image && store?.ImagePath(item) is { } path)
        {
            var preview = new Border
            {
                Width = 44,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                BorderBrush = theme.Brush(theme.Divider),
                BorderThickness = new Thickness(1),
                Background = theme.Brush(theme.Hover),
                Child = Icons.Make("Image24", 16, theme.Brush(theme.SecondaryText)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ((FrameworkElement)preview.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)preview.Child).VerticalAlignment = VerticalAlignment.Center;
            _ = SetThumbnailAsync(preview, path, thumbnailGeneration);
            leading = preview;
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
        line.ToolTip = item.Kind == ClipboardKind.Text && item.Text.Length > 60 ? TextPreview.SingleLine(item.Text, 600) : null;
        return line;
    }

    protected override string EmptyText(string text)
    {
        if (store?.OpenError is not null) return "无法读取剪贴板历史，请在设置的「关于」页查看日志。";
        if (text.Length > 0) return $"当前分类中没有找到「{text}」。可切换到「全部」并关闭「只看固定」继续查找。";
        if (pinnedOnly) return "当前分类没有固定内容。关闭「只看固定」后选择记录，按 Ctrl+P 固定。";
        return kind is null ? "还没有记录。复制过的文字、图片和文件会出现在这里" : $"还没有{ClipboardHistory.Name(kind.Value)}记录。选择「全部」查看其他类型。";
    }

    public void CycleFilter(int delta)
    {
        var index = (Array.IndexOf(Kinds, kind) + delta % Kinds.Length + Kinds.Length) % Kinds.Length;
        SetFilter(Kinds[index], pinnedOnly);
    }

    public void TogglePinnedFilter() => SetFilter(kind, !pinnedOnly);

    internal void SetFilter(ClipboardKind? selectedKind, bool onlyPinned)
    {
        kind = selectedKind; pinnedOnly = onlyPinned;
        UpdateFilters(); Reload();
    }

    internal ClipboardItem? SelectedHistoryItem => SelectedItem;
    internal bool FiltersAreClear => kind is null && !pinnedOnly;

    protected override FrameworkElement FilterControls()
    {
        filters.Clear();
        var panel = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        var pins = FilterButton("只看固定", null, true);
        DockPanel.SetDock(pins, Dock.Right); panel.Children.Add(pins);
        foreach (var value in Kinds)
            panel.Children.Add(FilterButton(value is { } k ? ClipboardHistory.Name(k) : "全部", value, false));
        UpdateFilters();
        return panel;
    }

    private Border FilterButton(string title, ClipboardKind? value, bool pins)
    {
        var label = new TextBlock { Text = title, FontSize = Theme.Caption, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var shell = new Border
        {
            Child = label, MinWidth = 48, Height = 30, Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(Theme.ControlRadius), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = pins ? "Ctrl+Shift+P 切换只看固定，可与文字、图片、文件分类组合" : "Tab 切换分类，Shift+Tab 返回；保留搜索词",
        };
        System.Windows.Automation.AutomationProperties.SetName(shell, title);
        shell.MouseLeftButtonUp += (_, e) => { if (pins) TogglePinnedFilter(); else SetFilter(value, pinnedOnly); e.Handled = true; };
        filters.Add((shell, label, value, pins));
        return shell;
    }

    private void UpdateFilters()
    {
        foreach (var (shell, label, value, pins) in filters)
        {
            var active = pins ? pinnedOnly : kind == value;
            shell.Background = Palette.Brush(active ? Palette.AccentSoft : Palette.Hover);
            shell.BorderBrush = Palette.Brush(active ? Palette.Accent : Palette.Divider);
            label.Foreground = Palette.Brush(active ? Palette.Accent : Palette.SecondaryText);
            System.Windows.Automation.AutomationProperties.SetItemStatus(shell, active ? "已选中" : "未选中");
        }
    }

    protected override void OnChosen(ClipboardItem item) => Chosen?.Invoke(item);

    private static string Preview(ClipboardItem item) => item.Kind switch
    {
        ClipboardKind.Files => FilePreview(item.Text),
        ClipboardKind.Image => string.IsNullOrWhiteSpace(item.RecognizedText) ? "图片" : TextPreview.SingleLine(item.RecognizedText, 80),
        _ => TextPreview.SingleLine(item.Text, 120),
    };

    private static string FilePreview(string text)
    {
        var summary = FileListSummary.Parse(text);
        var names = summary.FirstPaths.Select(p => Path.GetFileName(p.TrimEnd('\\')));
        var preview = summary.Count == 1 ? names.First() : $"{summary.Count} 个文件：{string.Join("、", names)}";
        return TextPreview.Truncate(preview, 120);
    }

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

    private static string Ago(DateTimeOffset time, DateTimeOffset now)
    {
        var span = now - time;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        return time.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SetThumbnailAsync(Border preview, string path, int generation)
    {
        try
        {
            var image = await thumbnails.GetAsync(path);
            if (generation != thumbnailGeneration || image is null) return;
            preview.Child = null;
            preview.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception e)
        {
            Log.Error("载入剪贴板缩略图失败", e);
        }
    }
}
