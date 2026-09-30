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

    private void Finish(Drawing.Rectangle area) => pending?.TrySetResult(area);

    private sealed class SelectorWindow : OverlayWindow
    {
        private readonly RegionSelector owner;
        private readonly Drawing.Rectangle monitor;
        private readonly double scale;
        private readonly Canvas canvas = new();
        private readonly Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)) };
        private readonly Rectangle border = new() { Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x7B, 0xF6)), StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
        private readonly TextBlock size = new() { Foreground = Brushes.White, FontSize = 12, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed };
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

            // 这块显示器对应的那一部分截图
            var crop = new CroppedBitmap(screen, new Int32Rect(monitor.Left - virtualScreen.Left, monitor.Top - virtualScreen.Top, monitor.Width, monitor.Height));
            canvas.Children.Add(new Image { Source = crop, Width = Width, Height = Height, Stretch = Stretch.Fill });
            shade.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
            canvas.Children.Add(shade);
            canvas.Children.Add(border);
            canvas.Children.Add(size);
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

        private void Draw(Point a, Point b)
        {
            var r = new Rect(a, b);
            shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
            border.Visibility = Visibility.Visible;
            border.Width = r.Width;
            border.Height = r.Height;
            Canvas.SetLeft(border, r.Left);
            Canvas.SetTop(border, r.Top);
            size.Visibility = Visibility.Visible;
            size.Text = $"{(int)Math.Round(r.Width * scale)} × {(int)Math.Round(r.Height * scale)}";
            Canvas.SetLeft(size, r.Left);
            Canvas.SetTop(size, Math.Max(0, r.Top - 24));
        }
    }
}
