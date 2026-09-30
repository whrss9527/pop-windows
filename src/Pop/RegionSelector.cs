using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Rect = System.Windows.Rect;
using Path = System.Windows.Shapes.Path;

namespace Pop;

/// 框选屏幕上的一块区域：先把整个屏幕定格，每个显示器盖一层半透明的遮罩，按住左键拖出一个框，松开就选定。
/// 右键或者 Esc 取消。每个显示器一个窗口，不同缩放比例的显示器上位置都准确
internal sealed class RegionSelector
{
    private readonly List<SelectorWindow> windows = [];
    private TaskCompletionSource<Drawing.Rectangle?>? pending;
    private Drawing.Bitmap? frozen;

    public bool IsOpen => pending is not null;

    /// 选好的区域（物理像素）和定格的整屏截图；取消时返回 null
    public async Task<(Drawing.Rectangle Area, Drawing.Bitmap Screen, Drawing.Rectangle ScreenBounds)?> SelectAsync()
    {
        if (pending is not null) return null;
        var bounds = ScreenCapture.VirtualScreen;
        frozen = ScreenCapture.Capture(bounds);
        var png = ScreenCapture.Png(frozen);
        var source = ScreenCapture.ToBitmapSource(png);
        pending = new TaskCompletionSource<Drawing.Rectangle?>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new SelectorWindow(this, screen.Bounds, bounds, source);
            windows.Add(window);
            window.Open();
        }
        Log.Info($"框选区域：{Forms.Screen.AllScreens.Length} 个显示器");
        var area = await pending.Task;
        foreach (var w in windows) w.Close();
        windows.Clear();
        pending = null;
        var screenshot = frozen;
        frozen = null;
        if (area is not { } chosen || chosen.Width < 4 || chosen.Height < 4)
        {
            screenshot.Dispose();
            return null;
        }
        return (chosen, screenshot, bounds);
    }

    public void Cancel() => pending?.TrySetResult(null);

    /// 演示模式：不用拖动，直接画出一个选区（物理像素）
    public void Preview(Drawing.Rectangle area)
    {
        foreach (var window in windows) window.Preview(area);
    }

    private void Finish(Drawing.Rectangle area) => pending?.TrySetResult(area);

    private sealed class SelectorWindow : OverlayWindow
    {
        private readonly RegionSelector owner;
        private readonly Drawing.Rectangle monitor;
        private readonly double scale;
        private readonly Canvas canvas = new();
        private readonly Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
        private readonly Rectangle border;
        private readonly Rectangle inner = new() { Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        private readonly TextBlock sizeText = new() { Foreground = Brushes.White, FontSize = Theme.Caption, FontFamily = Theme.MonoFont };
        private readonly Border size;
        private readonly Border hint;
        private Point? start;

        public SelectorWindow(RegionSelector owner, Drawing.Rectangle monitor, Drawing.Rectangle virtualScreen, BitmapSource screen) : base(clickThrough: false)
        {
            this.owner = owner;
            this.monitor = monitor;
            scale = Native.MonitorAt(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2).Scale;
            Title = "Pop 框选";
            Cursor = Cursors.Cross;
            Width = monitor.Width / scale;
            Height = monitor.Height / scale;
            var theme = Theme.Current();
            border = new Rectangle { Stroke = theme.Brush(theme.Dark ? theme.Accent : Color.FromRgb(0x4C, 0xC2, 0xFF)), StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            size = new Border
            {
                Child = sizeText,
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1C, 0x1C, 0x1E)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 3),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            // 顶上的提示：怎么框选、怎么取消
            var hintText = new StackPanel { Orientation = Orientation.Horizontal };
            var hintIcon = Icons.Make(Wpf.Ui.Controls.SymbolRegular.SelectObject24, 16, Brushes.White);
            hintIcon.Margin = new Thickness(0, 0, 8, 0);
            hintIcon.VerticalAlignment = VerticalAlignment.Center;
            hintText.Children.Add(hintIcon);
            hintText.Children.Add(new TextBlock { Text = "拖动鼠标框选区域", Foreground = Brushes.White, FontSize = Theme.Body, VerticalAlignment = VerticalAlignment.Center });
            hintText.Children.Add(new TextBlock { Text = "右键或 Esc 取消", Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = Theme.Caption, Margin = new Thickness(14, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            hint = new Border
            {
                Child = hintText,
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1C, 0x1C, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 9, 18, 10),
                IsHitTestVisible = false,
            };

            // 这块显示器对应的那一部分截图
            var crop = new CroppedBitmap(screen, new Int32Rect(monitor.Left - virtualScreen.Left, monitor.Top - virtualScreen.Top, monitor.Width, monitor.Height));
            canvas.Children.Add(new Image { Source = crop, Width = Width, Height = Height, Stretch = Stretch.Fill });
            shade.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
            canvas.Children.Add(shade);
            canvas.Children.Add(border);
            canvas.Children.Add(inner);
            canvas.Children.Add(size);
            canvas.Children.Add(hint);
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hint, (Width - hint.DesiredSize.Width) / 2);
            Canvas.SetTop(hint, 28);
            canvas.Background = Brushes.Transparent;
            Content = canvas;

            MouseLeftButtonDown += (_, e) =>
            {
                start = e.GetPosition(canvas);
                CaptureMouse();
            };
            MouseMove += (_, e) =>
            {
                if (start is { } s) Draw(s, e.GetPosition(canvas));
            };
            MouseLeftButtonUp += (_, e) =>
            {
                ReleaseMouseCapture();
                if (start is not { } s) return;
                start = null;
                var r = new Rect(s, e.GetPosition(canvas));
                owner.Finish(new Drawing.Rectangle(
                    monitor.Left + (int)Math.Round(r.Left * scale),
                    monitor.Top + (int)Math.Round(r.Top * scale),
                    (int)Math.Round(r.Width * scale),
                    (int)Math.Round(r.Height * scale)));
            };
            MouseRightButtonUp += (_, _) => owner.Cancel();
        }

        public void Open()
        {
            PlacePhysical(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
            Show();
            Dispatcher.BeginInvoke(() => PlacePhysical(monitor.Left, monitor.Top, monitor.Width, monitor.Height), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// 演示模式：画出一个选区（物理像素）
        public void Preview(Drawing.Rectangle area)
        {
            var local = Drawing.Rectangle.Intersect(area, monitor);
            if (local.Width <= 0 || local.Height <= 0) return;
            Draw(new Point((local.Left - monitor.Left) / scale, (local.Top - monitor.Top) / scale),
                new Point((local.Right - monitor.Left) / scale, (local.Bottom - monitor.Top) / scale));
        }

        private void Draw(Point a, Point b)
        {
            var r = new Rect(a, b);
            shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
            hint.Visibility = Visibility.Collapsed;
            border.Visibility = Visibility.Visible;
            border.Width = r.Width + 2;
            border.Height = r.Height + 2;
            Canvas.SetLeft(border, r.Left - 1);
            Canvas.SetTop(border, r.Top - 1);
            inner.Visibility = r.Width > 6 && r.Height > 6 ? Visibility.Visible : Visibility.Collapsed;
            inner.Width = Math.Max(0, r.Width - 2);
            inner.Height = Math.Max(0, r.Height - 2);
            Canvas.SetLeft(inner, r.Left + 1);
            Canvas.SetTop(inner, r.Top + 1);
            size.Visibility = Visibility.Visible;
            sizeText.Text = $"{(int)Math.Round(r.Width * scale)} × {(int)Math.Round(r.Height * scale)}";
            size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(size, Math.Min(r.Left, Width - size.DesiredSize.Width - 4));
            // 选区上面放得下就放上面，放不下放到选区里面左上角
            Canvas.SetTop(size, r.Top - size.DesiredSize.Height - 6 >= 0 ? r.Top - size.DesiredSize.Height - 6 : r.Top + 6);
        }
    }
}
