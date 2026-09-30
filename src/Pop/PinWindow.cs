using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 贴图：截下来的图钉在所有窗口最前面。拖动移动，滚轮缩放，按住 Alt 滚动调透明度；
/// 双击或 Esc 关闭，Ctrl+C 复制；右键菜单里可以识别文字、存到「下载」
internal sealed class PinWindow : Window
{
    private static readonly List<PinWindow> Open = [];

    /// 图片四周留给阴影的空白（DIP）
    private const double Pad = 16;

    private readonly byte[] png;
    private readonly BitmapSource image;
    private readonly double baseWidth;
    private readonly double baseHeight;
    private readonly Border frame;
    private readonly Border badge;
    private readonly TextBlock badgeText = new() { Foreground = Brushes.White, FontSize = Theme.Caption };
    private double zoom = 1;

    /// 右键菜单里点了「识别文字」
    public static event Action<byte[], int, int>? RecognizeRequested;

    public PinWindow(byte[] png, int x, int y, double scale)
    {
        this.png = png;
        image = ScreenCapture.ToBitmapSource(png);
        baseWidth = image.PixelWidth / scale;
        baseHeight = image.PixelHeight / scale;
        Title = "Pop 贴图";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        UseLayoutRounding = true;
        FontFamily = Theme.TextFont;
        Theme.ApplyTextOptions(this);
        var theme = Theme.Current();

        // 图片圆角裁剪，外面一圈细描边，阴影画在单独的一层上
        var picture = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = picture,
            BorderBrush = theme.Brush(theme.Stroke),
            BorderThickness = new Thickness(1),
        };
        frame.Loaded += (_, _) => ClipFrame();
        frame.SizeChanged += (_, _) => ClipFrame();
        var shadow = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = theme.Dark ? 0.55 : 0.28 },
        };
        badge = new Border
        {
            Child = badgeText,
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1C, 0x1C, 0x1E)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 3),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        var content = new Grid { Margin = new Thickness(Pad) };
        content.Children.Add(shadow);
        content.Children.Add(frame);
        content.Children.Add(badge);
        Content = content;
        Width = baseWidth + 2 * Pad;
        Height = baseHeight + 2 * Pad;
        ContextMenu = Menu();

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
                Flash($"不透明度 {Opacity * 100:0}%");
            }
            else
            {
                Zoom(e.Delta > 0 ? 1.1 : 1 / 1.1);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopyImage();
        };
        Loaded += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
            var pad = (int)Math.Round(Pad * scale);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x - pad, y - pad, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        };
        Closed += (_, _) => Open.Remove(this);
        Open.Add(this);
    }

    private void ClipFrame() =>
        frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 8, 8);

    /// 右下角短暂显示缩放比例、不透明度
    private void Flash(string text)
    {
        badgeText.Text = text;
        var show = new DoubleAnimationUsingKeyFrames();
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        badge.BeginAnimation(OpacityProperty, show);
    }

    public static int Count => Open.Count;

    public static void CloseAll()
    {
        foreach (var pin in Open.ToList()) pin.Close();
    }

    private void Zoom(double factor)
    {
        zoom = Math.Clamp(zoom * factor, 0.2, 5);
        Width = baseWidth * zoom + 2 * Pad;
        Height = baseHeight * zoom + 2 * Pad;
        Flash($"{zoom * 100:0}%");
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        void Add(string title, WpfUi.SymbolRegular icon, Action action, string? keys = null)
        {
            var item = new MenuItem { Header = title, Icon = new WpfUi.SymbolIcon { Symbol = icon, FontSize = 16 }, InputGestureText = keys ?? "" };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("复制", WpfUi.SymbolRegular.Copy24, CopyImage, "Ctrl+C");
        Add("识别文字", WpfUi.SymbolRegular.ScanText24, () =>
        {
            Native.GetCursorPos(out var p);
            RecognizeRequested?.Invoke(png, p.X, p.Y);
        });
        Add("存到「下载」", WpfUi.SymbolRegular.ArrowDownload24, Save);
        Add("原始大小", WpfUi.SymbolRegular.ZoomFit24, () => Zoom(1 / zoom));
        menu.Items.Add(new Separator());
        Add("关闭", WpfUi.SymbolRegular.Dismiss24, Close, "Esc");
        Add("关闭所有贴图", WpfUi.SymbolRegular.DismissSquareMultiple20, CloseAll);
        return menu;
    }

    private void CopyImage()
    {
        try
        {
            Clipboard.SetImage(image);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            Log.Error("复制贴图失败", e);
        }
    }

    private void Save()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        var path = Path.Combine(downloads, $"Pop 贴图 {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        File.WriteAllBytes(path, png);
        Log.Info($"贴图存到了 {path}");
    }
}
