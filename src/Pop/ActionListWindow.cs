using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pop.Core;

namespace Pop;

/// 「全部功能」列表：可以搜索（名称、拼音首字母、英文），方向键选择，回车执行，Esc 或点别处关闭。
/// 要接收键盘输入，所以这个窗口会拿到焦点；执行前把焦点还给原来的 App
internal sealed class ActionListWindow : Window
{
    private readonly TextBox search = new();
    private readonly ListBox list = new();
    private readonly TextBlock hint = new();
    private ClassifiedContent content = ClassifiedContent.Empty;
    private IntPtr previousForeground;
    private bool choosing;
    /// 列表真正拿到焦点之后才在失去焦点时关闭：显示的那一刻系统可能先激活又立刻收回
    private bool armed;

    /// 选了一个功能；参数是功能和原来前台的窗口
    public event Action<PopAction, IntPtr>? Chosen;

    public ActionListWindow()
    {
        Title = "Pop 全部功能";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = 340;
        Height = 420;
        new WindowInteropHelper(this).EnsureHandle();
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW);

        search.FontFamily = Theme.TextFont;
        search.FontSize = 14;
        search.Padding = new Thickness(8, 6, 8, 6);
        search.BorderThickness = new Thickness(0);
        search.TextChanged += (_, _) => Refresh();
        search.PreviewKeyDown += OnKey;

        list.BorderThickness = new Thickness(0);
        list.Background = Brushes.Transparent;
        list.Focusable = false;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.MouseLeftButtonUp += (_, _) => Choose();

        hint.FontFamily = Theme.TextFont;
        hint.FontSize = 11;
        hint.Margin = new Thickness(10, 4, 10, 0);
        hint.Text = "↑↓ 选择　回车执行　Esc 关闭";

        Deactivated += (_, _) =>
        {
            var foreground = Native.GetForegroundWindow();
            Log.Info($"全部功能列表失去焦点，现在的前台窗口是 {Native.WindowClass(foreground)}");
            if (armed && !choosing) Hide();
        };
        Activated += (_, _) => armed = IsVisible;
    }

    public void ShowFor(ClassifiedContent selected, int x, int y, IntPtr foreground)
    {
        content = selected;
        previousForeground = foreground;
        choosing = false;
        armed = false;
        var theme = Theme.Current();

        var panel = new DockPanel();
        var searchBox = new Border
        {
            Child = search,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(theme.Segment),
            Margin = new Thickness(0, 0, 0, 6),
        };
        search.Background = Brushes.Transparent;
        search.Foreground = new SolidColorBrush(theme.Text);
        search.CaretBrush = new SolidColorBrush(theme.Text);
        DockPanel.SetDock(searchBox, Dock.Top);
        panel.Children.Add(searchBox);
        hint.Foreground = new SolidColorBrush(theme.SecondaryText);
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(list);

        if (search.Parent is Border oldBox) oldBox.Child = null;
        if (list.Parent is Panel oldList) oldList.Children.Remove(list);
        if (hint.Parent is Panel oldHint) oldHint.Children.Remove(hint);
        searchBox.Child = search;
        Content = new Border
        {
            Child = panel,
            Padding = new Thickness(10),
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(theme.Surface),
            BorderBrush = new SolidColorBrush(theme.SurfaceBorder),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = theme.Dark ? 0.5 : 0.2, Direction = 270 },
        };

        search.Text = "";
        Refresh();

        // 放在指针旁边，放不下就往里挪（物理像素）
        var (work, scale) = Native.MonitorAt(x, y);
        var w = (int)(Width * scale);
        var h = (int)(Height * scale);
        var left = (int)Math.Clamp(x - w / 2.0, work.Left, Math.Max(work.Left, work.Right - w));
        var top = (int)Math.Clamp(y - 40 * scale, work.Top, Math.Max(work.Top, work.Bottom - h));
        Show();
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, left, top, w, h, 0);
        Native.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        search.Focus();
        Keyboard.Focus(search);
        var active = Native.GetForegroundWindow() == new WindowInteropHelper(this).Handle;
        Log.Info($"全部功能列表已显示，{(active ? "拿到了焦点" : $"没拿到焦点（前台是 {Native.WindowClass(Native.GetForegroundWindow())}）")}，{list.Items.Count} 项");
    }

    private void Refresh()
    {
        var theme = Theme.Current();
        list.Items.Clear();
        foreach (var action in Actions.Filter(search.Text, content))
        {
            var available = action.IsAvailable(content);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 2, 3), Tag = action };
            row.Children.Add(new TextBlock
            {
                Text = action.Glyph,
                FontFamily = Theme.IconFont,
                FontSize = 16,
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(available ? theme.Text : theme.DisabledText),
            });
            row.Children.Add(new TextBlock
            {
                Text = action.Title,
                FontFamily = Theme.TextFont,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(available ? theme.Text : theme.DisabledText),
            });
            list.Items.Add(new ListBoxItem { Content = row, Tag = action, IsEnabled = available });
        }
        list.SelectedIndex = list.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.IsEnabled);
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) Log.Info($"全部功能列表：{e.Key}，搜索「{search.Text}」");
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                RestoreForeground();
                e.Handled = true;
                break;
            case Key.Enter:
                Choose();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var items = list.Items.Cast<ListBoxItem>().ToList();
        var index = list.SelectedIndex;
        for (var i = 0; i < items.Count; i++)
        {
            index = (index + delta + items.Count) % items.Count;
            if (items[index].IsEnabled) break;
        }
        list.SelectedIndex = index;
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
    }

    private void Choose()
    {
        if (list.SelectedItem is not ListBoxItem { IsEnabled: true, Tag: PopAction action }) return;
        choosing = true;
        Hide();
        RestoreForeground();
        Chosen?.Invoke(action, previousForeground);
    }

    /// 把焦点还给打开列表之前的 App（替换原文要粘贴到那里）
    private void RestoreForeground()
    {
        if (previousForeground != IntPtr.Zero && Native.IsWindow(previousForeground))
            Native.ForceForeground(previousForeground);
    }
}
