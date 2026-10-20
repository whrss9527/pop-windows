using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfUi = Wpf.Ui.Controls;
using Pop.Core;

namespace Pop;

/// 带搜索框的浮窗（「全部功能」和剪贴板历史）：毛玻璃底，上面是搜索框，中间是分组的列表，下面是按键提示。
/// 不抢焦点，键盘输入由全局键盘钩子转过来
internal abstract class SearchOverlay<T> : OverlayWindow where T : class
{
    /// 列表里的一项：要么是分组标题，要么是一条可以选的内容
    protected sealed record Entry(string? Header, T? Item, bool Enabled = true);

    private readonly Grid root = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly TextBlock query = new();
    private readonly StackPanel rows = new();
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    private readonly List<(Entry Entry, FrameworkElement Element, int Number)> painted = [];
    private readonly List<int> selectable = [];
    private List<Entry> entries = [];
    private int selected = -1;
    private FrameworkElement? layout;
    private Frost.Shot? backdrop;
    private Pop.Core.Rect workArea;
    private double monitorScale = 1, panelWidth;
    private int anchorX, anchorY;

    protected Theme Palette { get; private set; } = Theme.Current();
    protected string SearchText { get; private set; } = "";

    protected SearchOverlay(string title) : base(clickThrough: false)
    {
        Title = title;
        root.RenderTransform = scale;
        Content = root;
        scroller.Content = rows;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            // 浮窗长期复用；隐藏时拆开视觉树，释放上一批文本、图片和毛玻璃屏幕快照。
            rows.Children.Clear(); painted.Clear(); entries.Clear(); selectable.Clear(); selected = -1;
            root.Children.Clear(); query.Text = ""; SearchText = "";
            layout = null; backdrop = null;
            if (query.Parent is Panel queryParent) queryParent.Children.Remove(query);
            if (scroller.Parent is Panel scrollerParent) scrollerParent.Children.Remove(scroller);
        };
    }

    /// 内容区域的宽和列表区域的高（DIP）
    protected abstract double PanelWidth { get; }
    protected abstract double ListHeight { get; }
    protected abstract string Placeholder { get; }
    protected abstract IReadOnlyList<(string Keys, string Action)> Hints { get; }

    /// 按搜索词列出要显示的内容
    protected abstract IEnumerable<Entry> Query(string text);

    /// 画一行
    protected abstract FrameworkElement Row(T item, bool selected, bool enabled, int index);

    /// 没有内容时显示的话
    protected abstract string EmptyText(string text);

    /// 选中了一项（回车或者鼠标点）
    protected abstract void OnChosen(T item);

    public bool IsOpen => IsVisible && root.Opacity > 0;

    protected T? SelectedItem => selected >= 0 && selected < entries.Count ? entries[selected].Item : null;

    protected void Open(int x, int y)
    {
        Palette = Theme.Current();
        SearchText = "";
        anchorX = x; anchorY = y;
        (workArea, monitorScale) = WorkAreaAt(x, y);
        panelWidth = Math.Min(PanelWidth, Math.Max(1, workArea.Width / monitorScale - 2 * Theme.ShadowPad));
        var maxHeight = Math.Max(1, workArea.Height / monitorScale - 2 * Theme.ShadowPad);
        root.Children.Clear();
        layout = Layout();
        scroller.Height = 0;
        layout.Measure(new Size(panelWidth, double.PositiveInfinity));
        var chrome = Math.Ceiling(layout.DesiredSize.Height);
        scroller.Height = double.NaN;
        scroller.MaxHeight = Math.Min(ListHeight, Math.Max(0, maxHeight - chrome));
        layout.MaxHeight = maxHeight;
        var fullHeight = Math.Min(maxHeight, chrome + scroller.MaxHeight);
        var (left, top, w, h) = BoundsFor(fullHeight);
        // 一次捕获可展开到的完整范围；搜索、删除后调整尺寸时复用，不把窗口自己截进背景。
        backdrop = Frost.Capture(left, top, w, h, monitorScale);
        Refresh();
        MarkShowing();
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        Show();
        Dispatcher.BeginInvoke(() => { if (IsVisible) Reflow(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        scale.CenterX = Width / 2;
        scale.CenterY = Theme.ShadowPad;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, Ms(180)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, Ms(180)) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(130)));
    }

    protected virtual (Pop.Core.Rect WorkArea, double Scale) WorkAreaAt(int x, int y) => Native.MonitorAt(x, y);

    private (int Left, int Top, int Width, int Height) BoundsFor(double height)
    {
        var pad = (int)Math.Ceiling(Theme.ShadowPad * monitorScale);
        var width = (int)Math.Ceiling(panelWidth * monitorScale);
        var h = (int)Math.Ceiling(height * monitorScale);
        var left = (int)Math.Clamp(anchorX - width / 2d, workArea.Left + pad, Math.Max(workArea.Left + pad, workArea.Right - width - pad));
        var top = (int)Math.Clamp(anchorY - 28 * monitorScale, workArea.Top + pad, Math.Max(workArea.Top + pad, workArea.Bottom - h - pad));
        return (left, top, width, h);
    }

    private void Reflow()
    {
        if (layout is null) return;
        if (layout.Parent is Panel old) old.Children.Remove(layout);
        layout.Measure(new Size(panelWidth, double.PositiveInfinity));
        var height = Math.Ceiling(layout.DesiredSize.Height);
        var (left, top, width, h) = BoundsFor(height);
        root.Children.Clear();
        var panel = Frost.Panel(backdrop, left, top, panelWidth, height, Theme.OverlayRadius, Palette, layout);
        panel.Margin = new Thickness(Theme.ShadowPad); root.Children.Add(panel);
        Width = panelWidth + 2 * Theme.ShadowPad; Height = height + 2 * Theme.ShadowPad;
        var pad = (int)Math.Ceiling(Theme.ShadowPad * monitorScale);
        PlacePhysical(left - pad, top - pad, width + 2 * pad, h + 2 * pad);
        EnsureSelectedVisible();
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        FadeOutAndHide(root, 100);
    }

    public void Type(char c)
    {
        Paste(c.ToString());
    }

    public void Paste(string text) { SearchText = SearchQuery.Append(SearchText, text); Refresh(); }

    public void Backspace()
    {
        if (SearchText.Length == 0) return;
        SearchText = SearchQuery.Backspace(SearchText);
        Refresh();
    }

    public void Move(int delta)
    {
        if (selectable.Count == 0) return;
        var position = selectable.BinarySearch(selected);
        if (position < 0) position = 0;
        else
        {
            var next = (position + (long)delta) % selectable.Count;
            position = (int)(next < 0 ? next + selectable.Count : next);
        }
        Select(selectable[position]);
    }

    public void Choose()
    {
        if (SelectedItem is { } item && entries[selected].Enabled)
        {
            Dismiss();
            OnChosen(item);
            return;
        }
        Log.Info($"{Title}：回车时没有能选的项（搜索词 {SearchText.Length} 个字）");
    }

    /// 第 n 个可以选的（Ctrl+数字）
    public void ChooseNth(int n)
    {
        if (n < 0 || n >= selectable.Count) return;
        var item = entries[selectable[n]].Item!;
        Dismiss();
        OnChosen(item);
    }

    /// 内容变了（删除、固定），保持选中同一项
    protected void Reload(T? keep = null)
    {
        var current = keep ?? SelectedItem;
        Refresh();
        if (current is not null && entries.FindIndex(e => ReferenceEquals(e.Item, current) || Equals(e.Item, current)) is var index and >= 0)
            Select(index);
    }

    private FrameworkElement Layout()
    {
        var theme = Palette;
        var search = new DockPanel { LastChildFill = true };
        var icon = Icons.Make(WpfUi.SymbolRegular.Search20, 16, theme.Brush(theme.TertiaryText));
        icon.Margin = new Thickness(2, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        search.Children.Add(icon);
        query.FontSize = Theme.Body;
        query.VerticalAlignment = VerticalAlignment.Center;
        query.TextTrimming = TextTrimming.CharacterEllipsis;
        if (query.Parent is Panel oldQuery) oldQuery.Children.Remove(query);
        search.Children.Add(query);
        var searchBox = new Border
        {
            Child = search,
            Height = 40,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(8),
            Background = theme.Brush(theme.Field),
            BorderBrush = theme.Brush(theme.FieldStroke),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var hints = new WrapPanel { Margin = new Thickness(4, 8, 4, 0) };
        foreach (var (keys, action) in Hints)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(Keycap(keys, theme));
            item.Children.Add(new TextBlock { Text = action, FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = theme.Brush(theme.TertiaryText) });
            hints.Children.Add(item);
        }

        var layout = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(hints, Dock.Bottom);
        layout.Children.Add(searchBox);
        layout.Children.Add(hints);
        if (scroller.Parent is Panel oldScroller) oldScroller.Children.Remove(scroller);
        layout.Children.Add(scroller);
        return layout;
    }

    /// 按键提示的小方块
    public static Border Keycap(string text, Theme theme) => new()
    {
        Child = new TextBlock { Text = text, FontSize = 10.5, Foreground = theme.Brush(theme.SecondaryText) },
        Padding = new Thickness(5, 1, 5, 1),
        CornerRadius = new CornerRadius(4),
        Background = theme.Brush(theme.Hover),
        BorderBrush = theme.Brush(theme.Divider),
        BorderThickness = new Thickness(1, 1, 1, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Refresh()
    {
        var theme = Palette;
        query.Text = SearchText.Length == 0 ? Placeholder : SearchText + "▏";
        query.Foreground = theme.Brush(SearchText.Length == 0 ? theme.TertiaryText : theme.Text);
        entries = Query(SearchText).ToList();
        selectable.Clear();
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].Item is not null && entries[i].Enabled) selectable.Add(i);
        selected = selectable.Count > 0 ? selectable[0] : -1;
        Paint();
        Reflow();
    }

    private void Select(int index)
    {
        if (selected == index) return;
        var previous = selected; selected = index;
        UpdateRow(previous); UpdateRow(selected); EnsureSelectedVisible();
    }

    // 方向键只更新旧、新选中的两行，保留其他行、图片和事件处理器。
    private void UpdateRow(int index)
    {
        if (index < 0 || index >= painted.Count) return;
        var (entry, element, number) = painted[index];
        if (entry.Item is null || element is not Border { Child: Grid grid } row) return;
        var active = index == selected;
        var content = Row(entry.Item, active, entry.Enabled, number);
        // WPF 的 VisualCollection 不允许直接覆盖仍有视觉父项的槽位。
        grid.Children.RemoveAt(0); grid.Children.Insert(0, content);
        grid.Children[1].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        row.Background = active ? Palette.Brush(Palette.Selected)
            : row.IsMouseOver ? Palette.Brush(Palette.Hover) : Brushes.Transparent;
    }

    private void Paint()
    {
        var theme = Palette;
        rows.Children.Clear();
        painted.Clear();
        if (!entries.Any(e => e.Item is not null))
        {
            rows.Children.Add(new TextBlock
            {
                Text = EmptyText(SearchText),
                FontSize = Theme.Body,
                Margin = new Thickness(12, 16, 12, 16),
                TextWrapping = TextWrapping.Wrap,
                Foreground = theme.Brush(theme.TertiaryText),
            });
            return;
        }
        var number = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            FrameworkElement element;
            var rowNumber = -1;
            if (entry.Item is null)
            {
                element = new TextBlock
                {
                    Text = entry.Header,
                    FontSize = Theme.Caption,
                    FontWeight = FontWeights.Medium,
                    Foreground = theme.Brush(theme.TertiaryText),
                    Margin = new Thickness(12, i == 0 ? 4 : 12, 0, 4),
                };
            }
            else
            {
                var isSelected = i == selected;
                rowNumber = entry.Enabled ? number++ : -1;
                var content = Row(entry.Item, isSelected, entry.Enabled, rowNumber);
                var grid = new Grid();
                grid.Children.Add(content);
                // 选中的那一行：浅底色 + 左边一道主题色竖条
                grid.Children.Add(new Border
                {
                    Width = 3,
                    Height = 16,
                    CornerRadius = new CornerRadius(1.5),
                    Background = theme.Brush(theme.Accent),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(-8, 0, 0, 0),
                    Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed,
                });
                var row = new Border
                {
                    Child = grid,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(0, 1, 0, 1),
                    CornerRadius = new CornerRadius(Theme.ControlRadius),
                    Background = isSelected ? theme.Brush(theme.Selected) : Brushes.Transparent,
                    Opacity = entry.Enabled ? 1 : 0.42,
                    Cursor = entry.Enabled ? System.Windows.Input.Cursors.Hand : null,
                };
                var index = i;
                if (entry.Enabled)
                {
                    var hover = theme.Brush(theme.Hover);
                    row.MouseEnter += (_, _) => { if (index != selected) row.Background = hover; };
                    row.MouseLeave += (_, _) => { if (index != selected) row.Background = Brushes.Transparent; };
                    row.MouseLeftButtonUp += (_, _) =>
                    {
                        selected = index;
                        Choose();
                    };
                }
                element = row;
            }
            rows.Children.Add(element);
            painted.Add((entry, element, rowNumber));
        }
        EnsureSelectedVisible();
    }

    private void EnsureSelectedVisible()
    {
        if (selected >= 0 && selected < painted.Count)
        {
            // 让选中的那一行留在可见范围里
            rows.UpdateLayout();
            var viewport = scroller.ViewportHeight;
            if (viewport <= 0 || double.IsInfinity(viewport)) return;
            var element = painted[selected].Element;
            var top = element.TranslatePoint(new Point(0, 0), rows).Y;
            var bottom = top + element.ActualHeight;
            if (top < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(Math.Max(0, top - 24));
            else if (bottom > scroller.VerticalOffset + viewport) scroller.ScrollToVerticalOffset(bottom - viewport + 4);
        }
    }
}
