using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
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
        try
        {
            using var g = Drawing.Graphics.FromImage(bitmap);
            g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, Drawing.CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static byte[] Png(Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// 直接复制 BGRA 像素，避免仅为屏幕预览编码、解码整张 PNG。返回值不再依赖原位图。
    public static BitmapSource ToBitmapSource(Drawing.Bitmap bitmap)
    {
        var area = new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            BitmapSource source;
            if (data.Stride > 0)
                source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null,
                    data.Scan0, checked(data.Stride * bitmap.Height), data.Stride);
            else
            {
                // 反向扫描线不能交给 WPF；逐行复制，同时去掉行尾填充。
                var stride = checked(bitmap.Width * 4);
                var pixels = new byte[checked(stride * bitmap.Height)];
                for (var y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), pixels, y * stride, stride);
                source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            }
            source.Freeze(); return source;
        }
        finally { bitmap.UnlockBits(data); }
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
