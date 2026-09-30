using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pop.Core;

namespace Pop;

/// 「全部功能」列表：和圆盘一样不抢焦点，原来的 App 一直在前台（替换原文直接粘贴回去）。
/// 键盘输入由全局键盘钩子转过来：字母、数字改搜索词（名称的拼音首字母、英文），方向键选择，回车执行，Esc 关闭
internal sealed class ActionListWindow : OverlayWindow
{
    private const double ListWidth = 320;
    private const int VisibleRows = 10;

    private readonly Border root = new();
    private readonly TextBlock query = new();
    private readonly StackPanel rows = new();
    private readonly ScrollViewer scroller = new();
    private ClassifiedContent content = ClassifiedContent.Empty;
    private IReadOnlyList<PopAction> results = [];
    private string text = "";
    private int selected;
    private Theme theme = Theme.Current();

    /// 选了一个功能（回车或者鼠标点）
    public event Action<PopAction>? Chosen;

    public ActionListWindow() : base(clickThrough: false)
    {
        Title = "Pop 全部功能";
        Content = root;
        scroller.Content = rows;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scroller.Focusable = false;
    }

    public bool IsOpen => IsVisible && root.Opacity > 0;

    public void ShowFor(ClassifiedContent selectedContent, int x, int y)
    {
        content = selectedContent;
        text = "";
        theme = Theme.Current();

        var searchBox = new Border
        {
            Child = query,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(theme.Segment),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
        };
        query.FontFamily = Theme.TextFont;
        query.FontSize = 14;
        if (query.Parent is Border old) old.Child = null;
        searchBox.Child = query;

        var hint = new TextBlock
        {
            Text = "输入拼音首字母或英文搜索　↑↓ 选择　回车执行　Esc 关闭",
            FontFamily = Theme.TextFont,
            FontSize = 11,
            Margin = new Thickness(4, 6, 4, 0),
            Foreground = new SolidColorBrush(theme.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new DockPanel { Width = ListWidth };
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(searchBox);
        panel.Children.Add(hint);
        if (scroller.Parent is Panel oldPanel) oldPanel.Children.Remove(scroller);
        scroller.Height = VisibleRows * 30;
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
        Log.Info($"全部功能列表已显示，{results.Count} 项");
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
        if (results.Count == 0) return;
        var index = selected;
        for (var i = 0; i < results.Count; i++)
        {
            index = (index + delta + results.Count) % results.Count;
            if (results[index].IsAvailable(content)) break;
        }
        selected = index;
        Paint();
    }

    public void Choose()
    {
        if (selected < 0 || selected >= results.Count || !results[selected].IsAvailable(content)) return;
        var action = results[selected];
        Log.Info($"全部功能列表：选中 {action.Id}，搜索「{text}」");
        Dismiss();
        Chosen?.Invoke(action);
    }

    private void Refresh()
    {
        query.Text = text.Length == 0 ? "搜索功能" : text + "▏";
        query.Foreground = new SolidColorBrush(text.Length == 0 ? theme.SecondaryText : theme.Text);
        results = Actions.Filter(text, content);
        selected = results.ToList().FindIndex(a => a.IsAvailable(content));
        Paint();
    }

    private void Paint()
    {
        rows.Children.Clear();
        for (var i = 0; i < results.Count; i++)
        {
            var action = results[i];
            var available = action.IsAvailable(content);
            var isSelected = i == selected;
            var foreground = new SolidColorBrush(isSelected ? theme.AccentText : available ? theme.Text : theme.DisabledText);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = action.Glyph, FontFamily = Theme.IconFont, FontSize = 15, Width = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = foreground });
            line.Children.Add(new TextBlock { Text = action.Title, FontFamily = Theme.TextFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = foreground });
            var row = new Border
            {
                Child = line,
                Height = 30,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? new SolidColorBrush(theme.Accent) : Brushes.Transparent,
                Cursor = available ? System.Windows.Input.Cursors.Hand : null,
            };
            var index = i;
            if (available)
            {
                row.MouseLeftButtonUp += (_, _) =>
                {
                    selected = index;
                    Choose();
                };
            }
            rows.Children.Add(row);
        }
        if (selected >= 0)
        {
            var offset = selected * 30.0;
            if (offset < scroller.VerticalOffset) scroller.ScrollToVerticalOffset(offset);
            else if (offset + 30 > scroller.VerticalOffset + scroller.Height) scroller.ScrollToVerticalOffset(offset + 30 - scroller.Height);
        }
    }
}
