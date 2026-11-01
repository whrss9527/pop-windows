using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

internal sealed partial class SettingsWindow
{
    internal static async Task VerifyUpdateFeedbackAsync(App app, Action<string, System.Windows.Window> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("更新反馈检查仅用于演示回归");
        var preferences = app.Settings.ToJson();
        var window = app.ShowSettings("about");
        var directory = Path.Combine(Path.GetTempPath(), "pop-update-feedback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var package = ReleaseFeed.PackageName(new SemVersion(0, 24, 0), Updater.Package);
        var zipPath = Path.Combine(directory, package);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Pop.exe").Open())) writer.Write("prepared-local-fixture");
            using var padding = zip.CreateEntry("sample.bin", CompressionLevel.NoCompression).Open();
            padding.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(200_000));
        }
        var bytes = File.ReadAllBytes(zipPath);
        var sums = $"{Checksums.Sha256File(zipPath)}  {package}";
        var release = new ReleaseInfo(new SemVersion(0, 24, 0), "v0.24.0", "", false,
            package, "https://update.invalid/package.zip", "https://update.invalid/SHA256SUMS.txt");
        var prepared = false;
        void OnChanged() { if (app.Updater.IsPreparingPackage) prepared = true; }
        app.Updater.Changed += OnChanged;
        try
        {
            foreach (var known in new[] { true, false })
            {
                prepared = false;
                using var handler = new WaitingDownloadHandler(bytes, sums, known);
                using var transport = new HttpClient(handler);
                var status = window.updateStatus;
                var button = window.updateAction;
                var bar = window.updateProgress;
                if (!window.nav["about"].Button.Focus()) throw new InvalidOperationException("无法建立更新检查的键盘焦点");
                window.scroller.ScrollToVerticalOffset(80);
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                var offset = window.scroller.VerticalOffset;
                var focus = Keyboard.FocusedElement;
                var task = app.Updater.PrepareForDemoAsync(transport, release,
                    Path.Combine(directory, known ? "known" : "unknown"));
                string? result = null;
                try
                {
                    await handler.Stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    if (known && app.Updater.DownloadFraction is not (> 0 and < 1))
                        throw new InvalidOperationException("已知大小的慢速下载没有显示真实进度");
                    if (!known && app.Updater.DownloadFraction is not null)
                        throw new InvalidOperationException("未知大小的下载伪造了百分比");
                    if (!ReferenceEquals(status, window.updateStatus) || !ReferenceEquals(button, window.updateAction)
                        || !ReferenceEquals(bar, window.updateProgress) || Keyboard.FocusedElement != focus
                        || Math.Abs(window.scroller.VerticalOffset - offset) > 1)
                        throw new InvalidOperationException("进度更新重建控件或打断了焦点和滚动位置");
                    if (bar is null || !bar.IsVisible || bar.IsIndeterminate == known || button?.IsEnabled != false
                        || status?.Text.Contains("正在下载", StringComparison.Ordinal) != true)
                        throw new InvalidOperationException("更新进度、等待状态或重复操作保护显示不正确");
                    window.scroller.ScrollToTop();
                    await Task.Delay(200);
                    shot(known ? "update-download-progress" : "update-download-unknown", window);
                    if (known)
                    {
                        var (_, work, _) = Native.MonitorDetailsAt(1, 1);
                        var flyout = new TrayFlyout(app);
                        try
                        {
                            flyout.ShowNear((int)work.Right - 24, (int)work.Bottom + 5);
                            await Task.Delay(250);
                            if (FindIn<System.Windows.Controls.Primitives.ButtonBase>(flyout, "tray-update-install").IsEnabled
                                || FindIn<System.Windows.Controls.Primitives.ButtonBase>(flyout, "tray-check-update").IsEnabled
                                || !FindIn<System.Windows.Controls.TextBlock>(flyout, "tray-update-status").Text.Contains("正在下载", StringComparison.Ordinal))
                                throw new InvalidOperationException("托盘未展示真实更新阶段或仍允许重复更新");
                            shot("update-tray-busy", flyout);
                        }
                        finally { flyout.Close(); window.Activate(); }
                    }
                }
                finally
                {
                    handler.Stream.Release.TrySetResult(true);
                    result = await task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                if (result is null || File.ReadAllText(result) != "prepared-local-fixture" || !prepared)
                    throw new InvalidOperationException("真实下载、校验准备路径没有完成或没有报告准备阶段");
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                if (app.Updater.IsBusy || window.updateProgress?.IsVisible == true || window.updateAction?.IsEnabled != true)
                    throw new InvalidOperationException("准备结束后更新入口没有恢复");
            }
        }
        finally
        {
            app.Updater.Changed -= OnChanged;
            window.Close();
        }
        if (app.Settings.ToJson() != preferences) throw new InvalidOperationException("更新反馈检查改写了偏好设置");

        static T FindIn<T>(System.Windows.DependencyObject root, string id) where T : System.Windows.DependencyObject
        {
            var pending = new Stack<System.Windows.DependencyObject>(); pending.Push(root);
            while (pending.TryPop(out var element))
            {
                if (element is T found && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == id) return found;
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
                    pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
            }
            throw new InvalidOperationException("找不到更新控件：" + id);
        }
    }

    private sealed class WaitingDownloadHandler(byte[] bytes, string sums, bool known) : HttpMessageHandler
    {
        public WaitingDownloadStream Stream { get; } = new(bytes);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content;
            if (request.RequestUri?.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal) == true)
                content = new StringContent(sums);
            else
            {
                content = new StreamContent(Stream);
                if (known) content.Headers.ContentLength = bytes.Length;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class WaitingDownloadStream(byte[] bytes) : Stream
    {
        private int offset;
        public TaskCompletionSource<bool> Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (offset > 0 && !Release.Task.IsCompleted)
            {
                Waiting.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            var count = Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsMemory(offset, count).CopyTo(buffer); offset += count; return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
