using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pop.Core;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Pop;

/// 编辑器预览和导出使用同一套绘制逻辑，始终保持原图像素。
internal static class AnnotationRenderer
{
    public static BitmapSource Render(BitmapSource image, IEnumerable<AnnotationStroke> strokes, AnnotationFrame? frame = null)
    {
        frame ??= new();
        var size = frame.OutputSize(image.PixelWidth, image.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            DrawComposition(dc, image, strokes, frame);
        var bitmap = new RenderTargetBitmap(size.Width, size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static void DrawComposition(DrawingContext dc, BitmapSource image, IEnumerable<AnnotationStroke> strokes, AnnotationFrame frame, AnnotationStroke? current = null)
    {
        var size = frame.OutputSize(image.PixelWidth, image.PixelHeight);
        var inset = frame.Inset;
        var radius = frame.CornerRadius(image.PixelWidth, image.PixelHeight);
        var bounds = new Rect(inset, inset, image.PixelWidth, image.PixelHeight);
        if (frame.Background != AnnotationBackground.None)
        {
            var colors = frame.Colors;
            var gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors.Start), (Color)ColorConverter.ConvertFromString(colors.End), 45);
            dc.DrawRectangle(gradient, null, new Rect(0, 0, size.Width, size.Height));
            if (frame.Shadow)
            {
                var spread = Math.Min(16, inset / 2);
                for (var i = spread; i >= 1; i--)
                {
                    var shadow = new Rect(bounds.Left - i, bounds.Top - i + spread / 3d, bounds.Width + i * 2, bounds.Height + i * 2);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(5, 0, 0, 0)), null, shadow, radius + i, radius + i);
                }
            }
        }
        dc.PushClip(new RectangleGeometry(bounds, radius, radius));
        dc.PushTransform(new TranslateTransform(inset, inset));
        dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        foreach (var stroke in strokes) Draw(dc, image, stroke);
        if (current is not null) Draw(dc, image, current);
        dc.Pop(); dc.Pop();
    }

    public static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    internal static void Draw(DrawingContext dc, BitmapSource image, AnnotationStroke stroke)
    {
        Point P(ImagePoint p) => new(p.X, p.Y);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(stroke.Color));
        var pen = new Pen(brush, stroke.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var b = stroke.Bounds;
        var bounds = new Rect(b.Left, b.Top, b.Width, b.Height);
        switch (stroke.Tool)
        {
            case AnnotationTool.Arrow:
                dc.DrawLine(pen, P(stroke.Start), P(stroke.End));
                var angle = Math.Atan2(stroke.End.Y - stroke.Start.Y, stroke.End.X - stroke.Start.X);
                foreach (var offset in new[] { Math.PI * 5 / 6, -Math.PI * 5 / 6 })
                {
                    var length = Math.Max(12, stroke.Width * 4);
                    dc.DrawLine(pen, P(stroke.End), new Point(stroke.End.X + Math.Cos(angle + offset) * length, stroke.End.Y + Math.Sin(angle + offset) * length));
                }
                break;
            case AnnotationTool.Rectangle: dc.DrawRoundedRectangle(null, pen, bounds, stroke.Width, stroke.Width); break;
            case AnnotationTool.Ellipse: dc.DrawEllipse(null, pen, new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2); break;
            case AnnotationTool.Pen:
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(P(stroke.Start), false, false);
                    path.PolyLineTo(stroke.Points.Skip(1).Select(P).ToArray(), true, false);
                }
                dc.DrawGeometry(null, pen, geometry);
                break;
            case AnnotationTool.Text:
                dc.DrawText(Text(stroke.Text, Math.Max(16, stroke.Width * 5), brush), P(stroke.Start));
                break;
            case AnnotationTool.Counter:
                var radius = Math.Max(12, stroke.Width * 4);
                dc.DrawEllipse(brush, null, P(stroke.Start), radius, radius);
                var number = Text(stroke.Counter.ToString(CultureInfo.InvariantCulture), radius * 1.2,
                    stroke.Color is "#FFD60A" or "#FFFFFF" ? Brushes.Black : Brushes.White);
                dc.DrawText(number, new Point(stroke.Start.X - number.Width / 2, stroke.Start.Y - number.Height / 2));
                break;
            case AnnotationTool.Mosaic:
                var left = Math.Clamp((int)b.Left, 0, image.PixelWidth - 1);
                var top = Math.Clamp((int)b.Top, 0, image.PixelHeight - 1);
                var right = Math.Clamp((int)Math.Ceiling(b.Right), left + 1, image.PixelWidth);
                var bottom = Math.Clamp((int)Math.Ceiling(b.Bottom), top + 1, image.PixelHeight);
                var crop = new CroppedBitmap(image, new Int32Rect(left, top, right - left, bottom - top));
                var block = Math.Max(8, stroke.Width * 3);
                var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1, Math.Floor(crop.PixelWidth / block)) / crop.PixelWidth,
                    Math.Max(1, Math.Floor(crop.PixelHeight / block)) / crop.PixelHeight));
                var pixels = new ImageBrush(small) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(pixels, BitmapScalingMode.NearestNeighbor);
                dc.DrawRectangle(pixels, null, new Rect(left, top, right - left, bottom - top));
                break;
        }
    }

    private static FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Theme.TextFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), size, brush, 1);
}
