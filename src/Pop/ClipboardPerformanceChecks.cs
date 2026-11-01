using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Pop;

/// CI 中用真实 WIC 解码器检查缩略图复用、尺寸限制、文件释放和后台取消。
internal static class ClipboardPerformanceChecks
{
    public static async Task VerifyAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pop-thumbnails-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var cache = new ClipboardThumbnailCache();
        try
        {
            var wide = Path.Combine(folder, "wide.png");
            var tall = Path.Combine(folder, "tall.png");
            WriteImage(wide, 1600, 8, Drawing.Color.Red);
            WriteImage(tall, 8, 1600, Drawing.Color.Blue);
            var first = cache.GetAsync(wide);
            if (!ReferenceEquals(first, cache.GetAsync(wide))) throw new InvalidOperationException("重复请求没有合并");
            var images = await Task.WhenAll(first, cache.GetAsync(tall));
            foreach (var image in images)
            {
                if (image is null || !image.IsFrozen || image.PixelWidth > ClipboardThumbnailCache.PixelLimit
                    || image.PixelHeight > ClipboardThumbnailCache.PixelLimit || image.PixelWidth < 1 || image.PixelHeight < 1)
                    throw new InvalidOperationException("缩略图没有冻结或超出像素上限");
            }
            if (images[0]!.PixelWidth <= images[0]!.PixelHeight || images[1]!.PixelWidth >= images[1]!.PixelHeight)
                throw new InvalidOperationException("缩略图改变了宽高方向");
            if (cache.DecodeCount != 2) throw new InvalidOperationException("同一图片重复解码");
            // 独占打开成功才说明 WIC 已经释放原图文件句柄。
            using (File.Open(wide, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            WriteImage(wide, 16, 16, Drawing.Color.Lime);
            cache.Clear();
            var changed = await cache.GetAsync(wide) ?? throw new InvalidOperationException("重新打开后没有刷新缩略图");
            var converted = new FormatConvertedBitmap(changed, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4]; converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            if (changed.PixelWidth != 16 || changed.PixelHeight != 16 || pixel[1] != 255 || pixel[2] != 0)
                throw new InvalidOperationException("缩略图仍在使用旧文件内容");

            var missing = Path.Combine(folder, "missing.png");
            if (await cache.GetAsync(missing) is not null) throw new InvalidOperationException("缺失图片未回退");
            var decodes = cache.DecodeCount;
            if (await cache.GetAsync(missing) is not null || cache.DecodeCount != decodes)
                throw new InvalidOperationException("缺失图片被重复读取");
            WriteImage(missing, 8, 8, Drawing.Color.Red);
            cache.Clear();
            if (await cache.GetAsync(missing) is null) throw new InvalidOperationException("新会话没有重试缺失图片");
            var corrupt = Path.Combine(folder, "corrupt.png");
            await File.WriteAllTextAsync(corrupt, "not a PNG");
            if (await cache.GetAsync(corrupt) is not null) throw new InvalidOperationException("损坏图片未回退");

            var sample = images[0];
            var bounded = new ClipboardThumbnailCache(_ => sample);
            try
            {
                for (var i = 0; i <= ClipboardThumbnailCache.Capacity; i++) await bounded.GetAsync(i.ToString());
                if (bounded.Count != ClipboardThumbnailCache.Capacity) throw new InvalidOperationException("缩略图缓存超过条数上限");
                decodes = bounded.DecodeCount;
                await bounded.GetAsync(ClipboardThumbnailCache.Capacity.ToString());
                if (bounded.DecodeCount != decodes) throw new InvalidOperationException("最近图片没有复用");
                await bounded.GetAsync("0");
                if (bounded.DecodeCount != decodes + 1) throw new InvalidOperationException("最旧图片没有淘汰");
            }
            finally { bounded.Clear(); }
            await VerifyCancellationAsync(sample);
        }
        finally { cache.Clear(); Directory.Delete(folder, true); }
    }

    private static async Task VerifyCancellationAsync(BitmapSource? image)
    {
        using var release = new ManualResetEventSlim();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var started = 0;
        var cache = new ClipboardThumbnailCache(_ =>
        {
            Interlocked.Increment(ref active);
            if (Interlocked.Increment(ref started) == 2) bothStarted.TrySetResult();
            try
            {
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("界面线程等待了缩略图解码");
                return image;
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var tasks = Enumerable.Range(0, 12).Select(i => cache.GetAsync(i.ToString())).ToArray();
        try
        {
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (Volatile.Read(ref active) != 2 || cache.DecodeCount != 2)
                throw new InvalidOperationException("后台图片解码并发超过上限");
            cache.Clear(); release.Set();
            if ((await Task.WhenAll(tasks)).Any(result => result is not null) || cache.Count != 0 || cache.DecodeCount != 2)
                throw new InvalidOperationException("关闭后仍有旧图片或排队任务继续解码");
        }
        finally { cache.Clear(); release.Set(); await Task.WhenAll(tasks); }
    }

    private static void WriteImage(string path, int width, int height, Drawing.Color color)
    {
        using var bitmap = new Drawing.Bitmap(width, height);
        using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.Clear(color);
        bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
    }
}
