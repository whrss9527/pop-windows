using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pop;

/// 贴图：截下来的图钉在所有窗口最前面。拖动移动，滚轮缩放，按住 Alt 滚动调透明度；
/// 双击或 Esc 关闭，Ctrl+C 复制；右键菜单里可以识别文字、存到「下载」
internal sealed class PinWindow : Window
{
    private static readonly List<PinWindow> Open = [];

    private readonly byte[] png;
    private readonly BitmapSource image;
    private readonly double baseWidth;
    private readonly double baseHeight;
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
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = baseWidth + 2;
        Height = baseHeight + 2;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x3B, 0x7B, 0xF6));
        BorderThickness = new Thickness(1);
        Content = new Image { Source = image, Stretch = Stretch.Fill };
        ContextMenu = Menu();

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
            else
                Zoom(e.Delta > 0 ? 1.1 : 1 / 1.1);
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
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        };
        Closed += (_, _) => Open.Remove(this);
        Open.Add(this);
    }

    public static int Count => Open.Count;

    public static void CloseAll()
    {
        foreach (var pin in Open.ToList()) pin.Close();
    }

    private void Zoom(double factor)
    {
        zoom = Math.Clamp(zoom * factor, 0.2, 5);
        Width = baseWidth * zoom + 2;
        Height = baseHeight * zoom + 2;
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("复制", CopyImage);
        Add("识别文字", () =>
        {
            Native.GetCursorPos(out var p);
            RecognizeRequested?.Invoke(png, p.X, p.Y);
        });
        Add("存到「下载」", Save);
        Add("原始大小", () => Zoom(1 / zoom));
        menu.Items.Add(new Separator());
        Add("关闭", Close);
        Add("关闭所有贴图", CloseAll);
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
