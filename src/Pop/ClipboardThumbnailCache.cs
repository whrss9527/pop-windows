using System.Windows.Media.Imaging;

namespace Pop;

/// 一次历史窗口会话内复用缩略图；解码在后台进行，队列和像素大小都有上限，关闭后取消并释放。
internal sealed class ClipboardThumbnailCache
{
    internal const int Capacity = 256;
    internal const int PixelLimit = 96;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> recent = new();
    private readonly SemaphoreSlim workers = new(2);
    private readonly Func<string, BitmapSource?> decode;
    private int decodeCount;

    private sealed record Entry(Task<BitmapSource?> Image, CancellationTokenSource Cancellation, LinkedListNode<string> Node);

    public int Count => entries.Count;
    public int DecodeCount => Volatile.Read(ref decodeCount);

    public ClipboardThumbnailCache(Func<string, BitmapSource?>? decode = null) => this.decode = decode ?? Decode;

    // 仅在界面线程访问缓存；后台任务只读取路径，返回冻结且不依赖文件句柄的图片。
    public Task<BitmapSource?> GetAsync(string path)
    {
        if (entries.TryGetValue(path, out var cached))
        {
            recent.Remove(cached.Node); recent.AddLast(cached.Node);
            return cached.Image;
        }
        while (entries.Count >= Capacity) Remove(recent.First!.Value);
        var cancellation = new CancellationTokenSource();
        var node = recent.AddLast(path);
        var image = LoadAsync(path, cancellation.Token);
        entries.Add(path, new Entry(image, cancellation, node));
        return image;
    }

    public void Clear()
    {
        foreach (var path in entries.Keys.ToArray()) Remove(path);
    }

    private void Remove(string path)
    {
        var entry = entries[path]; entries.Remove(path); recent.Remove(entry.Node);
        entry.Cancellation.Cancel(); entry.Cancellation.Dispose();
    }

    private async Task<BitmapSource?> LoadAsync(string path, CancellationToken cancellation)
    {
        try
        {
            return await Task.Run(async () =>
            {
                await workers.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref decodeCount);
                    var image = decode(path);
                    cancellation.ThrowIfCancellationRequested();
                    return image;
                }
                finally { workers.Release(); }
            }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or System.IO.FileFormatException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static BitmapSource Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, PixelLimit);
        else image.DecodePixelHeight = Math.Min(height, PixelLimit);
        image.StreamSource = stream;
        image.EndInit(); image.Freeze();
        return image;
    }
}
