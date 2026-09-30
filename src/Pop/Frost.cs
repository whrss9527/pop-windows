using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Pop;

/// 毛玻璃：浮窗弹出前先截下它后面的那块屏幕，模糊之后当底，再叠一层半透明的底色。
/// 透明窗口用不了系统的亚克力效果，这样做在 Windows 10 和 11 上都一样
internal static class Frost
{
    /// 模糊半径（DIP）
    public const double BlurRadius = 42;

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    /// 截下的屏幕：Left/Top/Width/Height 是物理像素的绝对位置
    public sealed record Shot(BitmapSource Image, int Left, int Top, int Width, int Height, double Scale);

    /// 截下一块屏幕（物理像素），四周再多一圈（模糊需要）；截到屏幕外的部分去掉。截不到返回 null
    public static Shot? Capture(int left, int top, int width, int height, double scale)
    {
        try
        {
            var margin = (int)Math.Ceiling(BlurRadius * scale);
            var wanted = new Drawing.Rectangle(left - margin, top - margin, width + 2 * margin, height + 2 * margin);
            wanted.Intersect(ScreenCapture.VirtualScreen);
            if (wanted.Width < 1 || wanted.Height < 1) return null;
            using var bitmap = ScreenCapture.Capture(wanted);
            var handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return new Shot(source, wanted.Left, wanted.Top, wanted.Width, wanted.Height, scale);
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        catch (Exception e) when (e is ExternalException or ArgumentException or InvalidOperationException)
        {
            // 安全桌面、锁屏时截不到，退回不透明的底色
            return null;
        }
    }

    /// 毛玻璃底：按 shape 裁剪，大小是内容区域（DIP）；contentLeft/Top 是内容区域左上角的物理像素位置
    public static Grid Layer(Shot? shot, int contentLeft, int contentTop, double width, double height, Geometry shape, Theme theme)
    {
        var grid = new Grid { Width = width, Height = height, Clip = shape, IsHitTestVisible = false };
        if (shot is not null)
        {
            var canvas = new Canvas { Width = width, Height = height };
            var image = new Image
            {
                Source = shot.Image,
                Width = shot.Width / shot.Scale,
                Height = shot.Height / shot.Scale,
                Stretch = Stretch.Fill,
                Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
            Canvas.SetLeft(image, (shot.Left - contentLeft) / shot.Scale);
            Canvas.SetTop(image, (shot.Top - contentTop) / shot.Scale);
            canvas.Children.Add(image);
            grid.Children.Add(canvas);
        }
        grid.Children.Add(new Rectangle
        {
            Fill = theme.Brush(theme.Tint),
            Opacity = shot is null ? 0.97 : theme.TintOpacity,
        });
        return grid;
    }

    /// 圆角矩形形状（给 Layer 和描边用）
    public static Geometry RoundedRect(double width, double height, double radius)
    {
        var geometry = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        geometry.Freeze();
        return geometry;
    }

    /// 完整的毛玻璃面板：阴影 + 模糊底 + 外描边 + 内侧高光，内容放在 Content 里
    public static Grid Panel(Shot? shot, int contentLeft, int contentTop, double width, double height, double radius, Theme theme, UIElement content)
    {
        var shape = RoundedRect(width, height, radius);
        var panel = new Grid { Width = width, Height = height };
        // 阴影单独画在一个不透明的形状上，模糊底被裁剪了带不出阴影
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = theme.Brush(theme.Tint),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = theme.ShadowOpacity, RenderingBias = RenderingBias.Quality },
            IsHitTestVisible = false,
        });
        panel.Children.Add(Layer(shot, contentLeft, contentTop, width, height, shape, theme));
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderBrush = theme.Brush(theme.Stroke),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius - 1),
            Margin = new Thickness(1),
            BorderBrush = new LinearGradientBrush(theme.InnerStroke, Colors.Transparent, 90),
            BorderThickness = new Thickness(1, 1, 1, 0),
            IsHitTestVisible = false,
        });
        panel.Children.Add(content);
        return panel;
    }
}
