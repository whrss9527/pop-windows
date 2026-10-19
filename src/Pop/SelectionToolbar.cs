using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 不激活的选择工具条：第一下点击直接执行，八秒不用、切换应用或输入时收起。
internal sealed class SelectionToolbar : OverlayWindow
{
    private readonly Grid root = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IntPtr target;
    private long expires;
    private volatile bool open;
    public bool IsOpen => open;
    public event Action<string>? Chosen;
    public event Action? MoreRequested;

    public SelectionToolbar() : base(clickThrough: false)
    {
        Title = "Pop 选择工具条";
        Content = root;
        timer.Tick += (_, _) =>
        {
            if (Environment.TickCount64 >= expires || target != IntPtr.Zero && Native.GetForegroundWindow() != target) Dismiss();
        };
        Closed += (_, _) => timer.Stop();
    }

    public void ShowAt(int x, int y, IReadOnlyList<PopAction> items, Pop.Core.Rect? selection = null, IntPtr foreground = default)
    {
        Dismiss();
        var theme = Theme.Current();
        var (work, scale) = Native.MonitorAt(x, y);
        const double buttonWidth = 60, panelHeight = 56, pad = 16;
        var panelWidth = (items.Count + 1) * buttonWidth + 12;
        var frame = SelectionToolbarLogic.Frame(panelWidth * scale, panelHeight * scale, selection, x, y, work, scale);
        var left = (int)Math.Round(frame.Left); var top = (int)Math.Round(frame.Top);
        var shot = Frost.Capture(left, top, (int)(panelWidth * scale), (int)(panelHeight * scale), scale);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        void Add(string id, string title, string glyph, Action run)
        {
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(Icons.Make(glyph, 18, theme.Brush(theme.Text)));
            content.Children.Add(new TextBlock { Text = title, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = theme.Brush(theme.Text), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(1, 3, 1, 0) });
            var surface = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = content };
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            var button = new Button { Width = buttonWidth, Height = 48, Content = surface, ToolTip = title, Focusable = false,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter } };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "toolbar-" + id);
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.MouseEnter += (_, _) => surface.Background = theme.Brush(theme.Hover);
            button.MouseLeave += (_, _) => surface.Background = Brushes.Transparent;
            button.Click += (_, _) => { Dismiss(); run(); };
            row.Children.Add(button);
        }
        foreach (var action in items) Add(action.Id, action.Title, action.Glyph, () => Chosen?.Invoke(action.Id));
        Add("more", "更多", "MoreHorizontal24", () => MoreRequested?.Invoke());
        root.Children.Clear();
        var panel = Frost.Panel(shot, left, top, panelWidth, panelHeight, Theme.OverlayRadius, theme, row);
        panel.Margin = new Thickness(pad);
        root.Children.Add(panel);
        Width = panelWidth + pad * 2; Height = panelHeight + pad * 2;
        MarkShowing();
        Show();
        PlacePhysical(left - (int)(pad * scale), top - (int)(pad * scale), (int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale));
        target = foreground; expires = Environment.TickCount64 + 8000; open = true; timer.Start();
    }

    public void Dismiss() { open = false; timer.Stop(); Hide(); }
}
