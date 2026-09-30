using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 剪贴板历史：监听剪贴板，存进本机的 SQLite，按保存天数和条数上限每小时清理一次（固定的不清理）
internal sealed class ClipboardHistory : IDisposable
{
    private readonly Func<AppSettings> settings;
    private readonly DispatcherTimer cleanupTimer = new() { Interval = TimeSpan.FromHours(1) };
    // 剪贴板里的图片在空闲时识别文字，识别出的文字也能搜
    private readonly DispatcherTimer recognizeTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool recognizing;

    public ClipboardStore Store { get; }
    public ClipboardMonitor Monitor { get; } = new();

    public ClipboardHistory(Func<AppSettings> settings)
    {
        this.settings = settings;
        Store = new ClipboardStore(ClipboardStore.DefaultDirectory);
        if (Store.OpenError is { } error) Log.Error($"打不开剪贴板历史数据库：{error}");
        // 监听线程读到内容就交过来，存储和设置都在界面线程上
        var ui = Dispatcher.CurrentDispatcher;
        Monitor.Captured += capture => ui.BeginInvoke(() => OnCaptured(capture));
        cleanupTimer.Tick += (_, _) => Cleanup();
        cleanupTimer.Start();
        Cleanup();
        recognizeTimer.Tick += async (_, _) => await RecognizePendingImages();
        recognizeTimer.Start();
    }

    public void Dispose()
    {
        cleanupTimer.Stop();
        recognizeTimer.Stop();
        Monitor.Dispose();
        Store.Dispose();
    }

    public void Cleanup()
    {
        var s = settings();
        var removed = Store.Cleanup(s.ClipboardRetentionDays, s.ClipboardMaxItems, DateTimeOffset.Now);
        if (removed > 0) Log.Info($"剪贴板历史：清理了 {removed} 条");
    }

    private async Task RecognizePendingImages()
    {
        if (recognizing) return;
        recognizing = true;
        try
        {
            foreach (var item in Store.ImagesWithoutRecognizedText(3))
            {
                if (Store.ImagePath(item) is not { } path || !File.Exists(path))
                {
                    Store.SetRecognizedText("", item.Id);
                    continue;
                }
                var text = await TextRecognizer.RecognizeAsync(await File.ReadAllBytesAsync(path));
                Store.SetRecognizedText(text, item.Id);
            }
        }
        catch (TextRecognizer.UnavailableException)
        {
            // 没有文字识别语言包，不再尝试
            recognizeTimer.Stop();
        }
        catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log.Info($"识别剪贴板图片里的文字失败：{e.Message}");
        }
        finally
        {
            recognizing = false;
        }
    }

    private void OnCaptured(ClipboardCapture capture)
    {
        var s = settings();
        if (!s.ClipboardHistory) return;
        if (capture.SourceApp is { Length: > 0 } app && s.ClipboardExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase)) return;
        if (Store.Add(capture, DateTimeOffset.Now) is not null)
            Log.Info($"剪贴板历史：记录一条{Name(capture.Kind)}{(string.IsNullOrEmpty(capture.SourceApp) ? "" : $"（来自 {capture.SourceApp}）")}");
    }

    public static string Name(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Image => "图片",
        ClipboardKind.Files => "文件",
        _ => "文字",
    };
}
