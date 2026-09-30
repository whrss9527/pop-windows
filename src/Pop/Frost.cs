using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Pop;

/// 毛玻璃：浮窗弹出前先截下它后面的那块屏幕，模糊之后当底，再叠一层半透明的底色。
/// 透明窗口用不了系统的亚克力效果，这样做在 Windows 10 和 11 上都一样。
/// 截图在弹出前就缩小、模糊好（几毫秒）。不用 WPF 的 BlurEffect：它每一帧都要重新模糊整张图，
/// 没有显卡加速时（虚拟机、远程桌面）一次就要几百毫秒，分层窗口第一次显示时界面线程还得等它画完
internal static class Frost
{
    /// 模糊半径（DIP），和 WPF 的 BlurEffect 的半径一样：高斯模糊的标准差大约是它的三分之一
    public const double BlurRadius = 42;

    /// 模糊前先缩小到一个像素大约这么多 DIP：模糊得这么厉害，缩小了看不出来，要算的像素少十几倍
    private const double DipsPerPixel = 4;

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
            return new Shot(Blurred(bitmap, scale), wanted.Left, wanted.Top, wanted.Width, wanted.Height, scale);
        }
        catch (Exception e) when (e is ExternalException or ArgumentException or InvalidOperationException)
        {
            // 安全桌面、锁屏时截不到，退回不透明的底色
            return null;
        }
    }

    /// 缩小、模糊好的截图，显示时再拉伸回原来的大小
    private static BitmapSource Blurred(Drawing.Bitmap bitmap, double scale)
    {
        var factor = DipsPerPixel * scale;
        var width = Math.Max(1, (int)Math.Ceiling(bitmap.Width / factor));
        var height = Math.Max(1, (int)Math.Ceiling(bitmap.Height / factor));
        using var small = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(small))
        using (var attributes = new Drawing.Imaging.ImageAttributes())
        {
            // 缩小时取周围像素的平均；边上按镜像取，不会混进黑边
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(Drawing.Drawing2D.WrapMode.TileFlipXY);
            g.DrawImage(bitmap, new Drawing.Rectangle(0, 0, width, height), 0, 0, bitmap.Width, bitmap.Height, Drawing.GraphicsUnit.Pixel, attributes);
        }
        var data = small.LockBits(new Drawing.Rectangle(0, 0, width, height), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        var stride = data.Stride;
        var pixels = new byte[stride * height];
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            small.UnlockBits(data);
        }
        Pop.Core.FastBlur.Gaussian(pixels, width, height, stride, BlurRadius / 3 / DipsPerPixel);
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return source;
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
            };
            // 已经模糊好的小图，放大时平滑插值
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
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
