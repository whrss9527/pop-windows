using System.Diagnostics;
using System.Net.Http;
using Pop.Core;

namespace Pop;

/// 一键更新：读 GitHub Releases，下载安装包，比对 SHA-256、检查代码签名，确认无误再替换 Pop.exe 并重新启动
internal sealed class Updater : IDisposable
{
#if POP_LIGHTWEIGHT
    public const ReleasePackage Package = ReleasePackage.Lightweight;
#else
    public const ReleasePackage Package = ReleasePackage.Portable;
#endif
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly HttpClient http;
    private readonly UpdateClient client;
    private readonly Func<AppSettings> settings;
    private System.Windows.Threading.DispatcherTimer? timer;
    private bool busy;
    private bool checking;
    private bool preparing;
    private long operation;
    public bool IsBusy => busy;
    public bool IsChecking => checking;
    public double? DownloadFraction { get; private set; }
    public bool IsPreparingPackage => preparing;

    public Updater(Func<AppSettings> settings)
    {
        this.settings = settings;
        http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Pop-Windows/{CurrentVersion}");
        // 更新继续使用当前包型，避免轻量版下次更新又下载整套运行时。
        client = new UpdateClient(http, Package);
    }

    public static SemVersion CurrentVersion { get; } = ReadCurrentVersion();

    /// 测试时可以用 POP_UPDATE_FEED 指向本机的假发布
    public static string Feed => Environment.GetEnvironmentVariable("POP_UPDATE_FEED") is { Length: > 0 } feed ? feed : UpdateClient.DefaultFeed;

    public ReleaseInfo? Available { get; private set; }

    /// 检查结果变了（发现新版本、开始下载、失败）
    public event Action? Changed;
    /// 下载百分比只更新已有视图，避免每个数据块重建设置页或托盘。
    public event Action? ProgressChanged;

    public string? Status { get; private set; }

    public void StartSchedule()
    {
        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = CheckInterval;
            if (settings().CheckForUpdates) await CheckAsync(userInitiated: false);
        };
        timer.Start();
    }

    public void Dispose()
    {
        operation++;
        timer?.Stop();
        http.Dispose();
    }

    public async Task<ReleaseInfo?> CheckAsync(bool userInitiated)
    {
        if (busy) return Available;
        busy = true;
        checking = true;
        Status = "正在检查更新…";
        Changed?.Invoke();
        try
        {
            Available = await client.CheckAsync(Feed, CurrentVersion, settings().IncludePrerelease, CancellationToken.None);
            Status = Available is null ? null : $"发现新版本 {Available.Version}";
            Log.Info(Available is null ? $"检查更新：{CurrentVersion} 已是最新" : $"检查更新：发现 {Available.Version}");
            return Available;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Status = null;
            // 断网、GitHub 限流都很常见，后台检查失败下次再试，不算错误
            if (!userInitiated)
            {
                Log.Info($"检查更新失败，稍后再试：{e.GetType().Name} {e.Message}");
                return null;
            }
            Log.Error("检查更新失败", e);
            throw new UpdateException($"检查更新失败：{e.Message}", e);
        }
        finally
        {
            busy = false;
            checking = false;
            Changed?.Invoke();
        }
    }

    /// 下载、校验、替换。成功后启动新版本，返回 true，调用方负责退出当前进程
    public async Task<bool> InstallAsync(ReleaseInfo release, IProgress<double>? progress, Func<bool>? beforeRestart = null)
    {
        if (busy) return false;
        busy = true;
        operation++;
        Status = $"正在下载 {release.Version}…";
        Changed?.Invoke();
        try
        {
            var current = Paths.Executable;
            var directory = Path.GetDirectoryName(current)!;
            if (!ExecutableSwap.CanWrite(directory))
                throw new UpdateException($"Pop 所在的文件夹不能写入（{directory}），请把 Pop.exe 放到自己的文件夹里（例如 %LOCALAPPDATA%\\Programs\\Pop）再更新");

            var work = Path.Combine(Paths.LocalData, "updates", release.Version.ToString());
            var newExe = await PrepareAsync(client, release, work, progress, CancellationToken.None);
            Status = "正在验证程序…";
            Changed?.Invoke();

            var fileVersion = FileVersionInfo.GetVersionInfo(newExe).ProductVersion;
            if (!SemVersion.TryParse(fileVersion, out var actual) || !actual.Equals(release.Version))
                throw new UpdateException($"安装包里的版本（{fileVersion}）和发布的版本（{release.Version}）不一致，已放弃更新");

            if (Signature.CheckUpdate(current, newExe) is { } refusal) throw new UpdateException(refusal);

            if (beforeRestart?.Invoke() == false)
                throw new UpdateException("设置尚未保存，请在设置窗口重试保存后再更新");

            Status = "正在安装更新…";
            Changed?.Invoke();
            await ExecutableSwap.SwapAsync(current, newExe);
            Log.Info($"已替换为 {release.Version}，重新启动");
            TryDelete(work);

            var start = new ProcessStartInfo(current) { UseShellExecute = false };
            start.ArgumentList.Add("--after-update");
            start.ArgumentList.Add(CurrentVersion.ToString());
            Process.Start(start);
            return true;
        }
        catch (Exception e) when (e is UpdateException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("更新失败", e);
            var failure = e as UpdateException ?? new UpdateException($"更新失败：{e.Message}", e);
            Status = failure.Message;
            throw failure;
        }
        finally
        {
            busy = false;
            operation++;
            DownloadFraction = null;
            preparing = false;
            Changed?.Invoke();
        }
    }

    private async Task<string> PrepareAsync(UpdateClient downloadClient, ReleaseInfo release, string work,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var current = operation;
        var acceptsProgress = true;
        DownloadFraction = null;
        preparing = false;
        var updates = new DispatcherProgress<UpdatePreparationProgress>(System.Windows.Threading.Dispatcher.CurrentDispatcher, value =>
        {
            if (!acceptsProgress || current != operation) return;
            if (value.Stage == UpdatePreparationStage.Preparing)
            {
                preparing = true; DownloadFraction = null;
                Status = "正在校验并准备安装包…";
                Changed?.Invoke();
                return;
            }
            if (preparing) return;
            var fraction = value.Fraction is { } f && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : (double?)null;
            // 只在可见百分比变化时通知界面，大文件按块下载不会反复重画。
            if (fraction is { } next && DownloadFraction is { } previous && (int)(next * 100) == (int)(previous * 100)) return;
            DownloadFraction = fraction;
            ProgressChanged?.Invoke();
        });
        try { return await downloadClient.DownloadAndPrepareAsync(release, work, progress, cancellation, updates); }
        finally { acceptsProgress = false; DownloadFraction = null; preparing = false; }
    }

    private sealed class DispatcherProgress<T>(System.Windows.Threading.Dispatcher dispatcher, Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            if (dispatcher.CheckAccess()) report(value);
            else dispatcher.BeginInvoke(() => report(value));
        }
    }

    /// 使用真实下载与校验路径检查进度，不执行替换、启动或访问正式发布。
    internal async Task<string> PrepareForDemoAsync(HttpClient transport, ReleaseInfo release, string work, CancellationToken cancellation = default)
    {
        if (System.Windows.Application.Current is not App { IsDemo: true })
            throw new InvalidOperationException("更新进度预览仅用于演示回归");
        if (busy) throw new InvalidOperationException("已有更新任务");
        var saved = (Available, Status);
        busy = true; operation++;
        Available = release; Status = $"正在下载 {release.Version}…"; Changed?.Invoke();
        try { return await PrepareAsync(new UpdateClient(transport, Package), release, work, null, cancellation); }
        finally
        {
            operation++; busy = false; preparing = false; DownloadFraction = null;
            (Available, Status) = saved; Changed?.Invoke();
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SemVersion ReadCurrentVersion()
    {
        var info = typeof(Updater).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        return SemVersion.TryParse(info, out var v) ? v : new SemVersion(0, 0, 0);
    }
}
