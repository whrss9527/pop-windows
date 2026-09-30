using System.IO;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Pop;

/// 截屏：坐标都是物理像素
internal static class ScreenCapture
{
    /// 整个虚拟屏幕（所有显示器）的范围
    public static Drawing.Rectangle VirtualScreen => Forms.SystemInformation.VirtualScreen;

    public static Drawing.Bitmap Capture(Drawing.Rectangle area)
    {
        var bitmap = new Drawing.Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, Drawing.CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static byte[] Png(Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    public static BitmapSource ToBitmapSource(byte[] png)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
