using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pop.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Pop;

/// 冻结整个虚拟屏幕，在原图物理像素上取色，不会取到放大镜本身。
internal sealed class ColorPicker
{
    private readonly List<PickerWindow> windows = [];
    private TaskCompletionSource<(ColorValue Color, int X, int Y)?>? pending;
    private BitmapSource? frozen;
    private Drawing.Rectangle bounds;
    private int cursorX, cursorY;
    public bool IsOpen => pending is not null;
    public void Cancel() => pending?.TrySetResult(null);

    public async Task<(ColorValue Color, int X, int Y)?> PickAsync()
    {
        if (IsOpen) return null;
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            bounds = ScreenCapture.VirtualScreen;
            BitmapSource source;
            using (var captured = ScreenCapture.Capture(bounds))
                source = ScreenCapture.ToBitmapSource(captured);
            frozen = source;
            Native.GetCursorPos(out var cursor); (cursorX, cursorY) = (cursor.X, cursor.Y);
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var window = new PickerWindow(this, source, bounds, screen.Bounds);
                windows.Add(window); window.Open();
            }
            return await pending.Task;
        }
        finally
        {
            foreach (var window in windows) window.Close();
            windows.Clear(); pending = null; frozen = null;
        }
    }

    /// 演示：显示真正的放大镜和颜色读数，不写剪贴板。
    public void Preview(int x, int y)
    {
        if (!bounds.Contains(x, y)) return;
        (cursorX, cursorY) = (x, y);
        foreach (var window in windows) window.Preview(x, y);
    }

    public void MovePixel(int dx, int dy)
    {
        if (!IsOpen) return;
        var x = Math.Clamp(cursorX + dx, bounds.Left, bounds.Right - 1);
        var y = Math.Clamp(cursorY + dy, bounds.Top, bounds.Bottom - 1);
        var screen = Forms.Screen.FromPoint(new Drawing.Point(x, y)).Bounds;
        x = Math.Clamp(x, screen.Left, screen.Right - 1); y = Math.Clamp(y, screen.Top, screen.Bottom - 1);
        Native.SetCursorPos(x, y); Preview(x, y);
    }

    public void Confirm()
    {
        if (frozen is null || !IsOpen) return;
        var pixel = new byte[4];
        frozen.CopyPixels(new Int32Rect(cursorX - bounds.Left, cursorY - bounds.Top, 1, 1), pixel, 4, 0);
        pending?.TrySetResult((new ColorValue(pixel[2], pixel[1], pixel[0]), cursorX, cursorY));
    }

    private sealed class PickerWindow : OverlayWindow
    {
        private readonly ColorPicker owner;
        private readonly BitmapSource source;
        private readonly Drawing.Rectangle bounds, monitor;
        private readonly double scale;
        private readonly Canvas canvas = new();
        private readonly Border lens;
        private readonly Image enlarged = new() { Width = 121, Height = 121, Stretch = Stretch.Fill };
        private readonly TextBlock value = new() { FontFamily = Theme.MonoFont, Foreground = Brushes.White, FontSize = Theme.Body };
        private readonly Border swatch = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(4) };

        public PickerWindow(ColorPicker owner, BitmapSource source, Drawing.Rectangle bounds, Drawing.Rectangle monitor) : base(false)
        {
            this.owner = owner; this.source = source; this.bounds = bounds; this.monitor = monitor;
            scale = Native.MonitorAt(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2).Scale;
            Width = monitor.Width / scale; Height = monitor.Height / scale;
            Title = "Pop 屏幕取色"; Cursor = Cursors.Cross;
            var crop = new CroppedBitmap(source, new Int32Rect(monitor.Left - bounds.Left, monitor.Top - bounds.Top, monitor.Width, monitor.Height));
            canvas.Children.Add(new Image { Source = crop, Width = Width, Height = Height, Stretch = Stretch.Fill });
            var box = new StackPanel();
            var magnifier = new Grid { Width = 121, Height = 121, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(enlarged, BitmapScalingMode.NearestNeighbor);
            magnifier.Children.Add(enlarged);
            magnifier.Children.Add(new System.Windows.Shapes.Rectangle { Width = 11, Height = 11, Stroke = Brushes.White, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(magnifier);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 5) };
            row.Children.Add(swatch); row.Children.Add(value); box.Children.Add(row);
            box.Children.Add(new TextBlock { Text = "方向键 1 px · Shift 10 px · Enter 选色\n点击选色 · 右键 / Esc 取消", Foreground = Brushes.LightGray, FontSize = Theme.Caption });
            lens = new Border { Child = box, Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromArgb(240, 28, 28, 30)), CornerRadius = new CornerRadius(10), IsHitTestVisible = false };
            canvas.Children.Add(lens); Content = canvas;
            MouseMove += (_, e) => { var p = e.GetPosition(canvas); owner.Preview(monitor.Left + (int)Math.Floor(p.X * scale), monitor.Top + (int)Math.Floor(p.Y * scale)); };
            MouseLeftButtonUp += (_, e) =>
            {
                var p = e.GetPosition(canvas);
                var x = monitor.Left + (int)Math.Floor(p.X * scale);
                var y = monitor.Top + (int)Math.Floor(p.Y * scale);
                owner.Preview(x, y); owner.Confirm();
            };
            MouseRightButtonUp += (_, _) => owner.Cancel();
        }
        private ColorValue Sample(int x, int y)
        {
            var pixel = new byte[4];
            var sourceX = Math.Clamp(x - bounds.Left, 0, source.PixelWidth - 1);
            var sourceY = Math.Clamp(y - bounds.Top, 0, source.PixelHeight - 1);
            // 截屏 PNG 解码为 BGRA32，显式转换避免格式不同导致通道读错。
            var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(new Int32Rect(sourceX, sourceY, 1, 1), pixel, 4, 0);
            return new(pixel[2], pixel[1], pixel[0]);
        }
        public void Preview(int x, int y)
        {
            if (!monitor.Contains(x, y)) { lens.Visibility = Visibility.Hidden; return; }
            lens.Visibility = Visibility.Visible;
            var color = Sample(x, y);
            value.Text = color.HexString;
            swatch.Background = new SolidColorBrush(Color.FromRgb((byte)color.Red, (byte)color.Green, (byte)color.Blue));
            var w = Math.Min(11, source.PixelWidth); var h = Math.Min(11, source.PixelHeight);
            enlarged.Source = new CroppedBitmap(source, new Int32Rect(Math.Clamp(x - bounds.Left - w / 2, 0, source.PixelWidth - w),
                Math.Clamp(y - bounds.Top - h / 2, 0, source.PixelHeight - h), w, h));
            lens.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var px = (x - monitor.Left) / scale; var py = (y - monitor.Top) / scale;
            Canvas.SetLeft(lens, Math.Clamp(px + 24 + lens.DesiredSize.Width < Width ? px + 24 : px - lens.DesiredSize.Width - 24, 0, Math.Max(0, Width - lens.DesiredSize.Width)));
            Canvas.SetTop(lens, Math.Clamp(py + 24 + lens.DesiredSize.Height < Height ? py + 24 : py - lens.DesiredSize.Height - 24, 0, Math.Max(0, Height - lens.DesiredSize.Height)));
        }
        public void Open()
        {
            PlacePhysical(monitor.Left, monitor.Top, monitor.Width, monitor.Height); Show();
            Dispatcher.BeginInvoke(() => PlacePhysical(monitor.Left, monitor.Top, monitor.Width, monitor.Height), System.Windows.Threading.DispatcherPriority.Loaded);
            Native.GetCursorPos(out var p); Preview(p.X, p.Y);
        }
    }
}
