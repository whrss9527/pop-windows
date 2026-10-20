using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Pop;

/// 演示/CI 专用：验证像素及资源生命周期，记录同一输入的新旧转换开销，不以易波动的时间阈值判定通过。
internal static class PerformanceChecks
{
    public static IReadOnlyList<string> Run()
    {
        VerifySnapshot();
        var lines = new List<string> { "screen-snapshot=ok" };
        foreach (var (width, height) in new[] { (1920, 1080), (3840, 2160) })
        {
            using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x / 8 + y / 16); pixels[i + 1] = (byte)(x / 16 + y / 8);
                pixels[i + 2] = (byte)(x / 32 ^ y / 32); pixels[i + 3] = 255;
            }
            var data = bitmap.LockBits(new(0, 0, width, height), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            BitmapSource Old() => ScreenCapture.ToBitmapSource(ScreenCapture.Png(bitmap));
            BitmapSource Direct() => ScreenCapture.ToBitmapSource(bitmap);
            var old = Old(); var direct = Direct();
            var converted = old.Format == PixelFormats.Bgra32 ? old : new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
            var oldPixels = new byte[pixels.Length]; var directPixels = new byte[pixels.Length];
            converted.CopyPixels(oldPixels, width * 4, 0); direct.CopyPixels(directPixels, width * 4, 0);
            if (!pixels.AsSpan().SequenceEqual(oldPixels) || !pixels.AsSpan().SequenceEqual(directPixels))
                throw new InvalidOperationException("屏幕快照转换改变像素");
            var png = Measure(Old); var bgra = Measure(Direct);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"performance: screen={width}x{height} png-ms={png.Milliseconds:0.###} direct-ms={bgra.Milliseconds:0.###} png-managed-bytes={png.Bytes} direct-managed-bytes={bgra.Bytes}"));
        }
        var sample = string.Concat(Enumerable.Repeat("剪贴板 preview line\r\n", 100_000));
        string Legacy()
        {
            var flat = string.Join(" ", sample.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            return flat.Length > 120 ? flat[..120] + "…" : flat;
        }
        string Bounded() => TextPreview.SingleLine(sample, 120);
        if (Legacy() != Bounded()) throw new InvalidOperationException("文本预览改变了显示内容");
        var split = Measure(Legacy); var prefix = Measure(Bounded);
        lines.Add("text-preview=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: preview-chars={sample.Length} split-ms={split.Milliseconds:0.###} prefix-ms={prefix.Milliseconds:0.###} split-managed-bytes={split.Bytes} prefix-managed-bytes={prefix.Bytes}"));
        return lines;
    }

    public static void VerifyNavigation(int x, int y)
    {
        var probe = new NavigationProbe();
        try
        {
            probe.OpenAt(x, y); var initial = probe.RowsCreated;
            if (probe.SelectedId != "probe-0") throw new InvalidOperationException("列表没有选择第一项");
            probe.Move(1);
            if (probe.SelectedId != "probe-2") throw new InvalidOperationException("列表没有跳过禁用项");
            probe.Move(-1);
            if (probe.SelectedId != "probe-0") throw new InvalidOperationException("列表反向选择错误");
            probe.Move(-1);
            if (probe.SelectedId != "probe-99") throw new InvalidOperationException("列表环绕选择错误");
            if (initial != 100 || probe.RowsCreated != initial + 6)
                throw new InvalidOperationException("列表选择时重建了无关行");
            probe.Hide();
            if (probe.SelectedId is not null || probe.Content is not Grid { Children.Count: 0 })
                throw new InvalidOperationException("隐藏后仍保留列表和背景");
            probe.OpenAt(x, y);
            if (probe.SelectedId != "probe-0" || probe.RowsCreated != initial + 106)
                throw new InvalidOperationException("隐藏后重新打开列表失败");
        }
        finally { probe.Close(); }
    }

    private sealed class NavigationProbe : SearchOverlay<PopAction>
    {
        public int RowsCreated { get; private set; }
        public string? SelectedId => SelectedItem?.Id;
        public NavigationProbe() : base("Pop 列表性能检查") { }
        public void OpenAt(int x, int y) => Open(x, y);
        protected override double PanelWidth => 380;
        protected override double ListHeight => 380;
        protected override string Placeholder => "性能检查";
        protected override IReadOnlyList<(string Keys, string Action)> Hints => [];
        protected override IEnumerable<Entry> Query(string text)
        {
            yield return new Entry("分组", null);
            for (var i = 0; i < 100; i++)
                yield return new Entry(null, new PopAction("probe-" + i, "项目 " + i, "", "", ContentKind.None, _ => null), i != 1);
        }
        protected override FrameworkElement Row(PopAction item, bool selected, bool enabled, int index)
        { RowsCreated++; return new TextBlock { Text = item.Title, Height = 36 }; }
        protected override string EmptyText(string text) => "";
        protected override void OnChosen(PopAction item) { }
    }

    private static (double Milliseconds, long Bytes) Measure<T>(Func<T> convert)
    {
        var times = new List<double>(); var allocations = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            var image = convert(); watch.Stop();
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes); times.Add(watch.Elapsed.TotalMilliseconds);
            GC.KeepAlive(image);
        }
        times.Sort(); allocations.Sort(); return (times[1], allocations[1]);
    }

    private static void VerifySnapshot()
    {
        BitmapSource snapshot;
        using (var bitmap = new Drawing.Bitmap(2, 2, Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(0, 0, Drawing.Color.FromArgb(128, 11, 22, 33));
            snapshot = ScreenCapture.ToBitmapSource(bitmap);
            bitmap.SetPixel(0, 0, Drawing.Color.White);
        }
        var bytes = new byte[16]; snapshot.CopyPixels(bytes, 8, 0);
        if (!snapshot.IsFrozen || !bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 33, 22, 11, 128 }))
            throw new InvalidOperationException("快照依赖原位图或改变透明度");
        var memory = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, 0, memory, 16);
            using var inverted = new Drawing.Bitmap(2, 2, -8, Drawing.Imaging.PixelFormat.Format32bppArgb, IntPtr.Add(memory, 8));
            var expected = inverted.GetPixel(0, 0);
            var image = ScreenCapture.ToBitmapSource(inverted); image.CopyPixels(bytes, 8, 0);
            if (bytes[0] != expected.B || bytes[1] != expected.G || bytes[2] != expected.R)
                throw new InvalidOperationException("反向扫描线转换方向错误");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
