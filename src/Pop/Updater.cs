using System.Diagnostics;
using System.Net.Http;
using Pop.Core;

namespace Pop;

/// 一键更新：读 GitHub Releases，下载安装包，比对 SHA-256、检查代码签名，确认无误再替换 Pop.exe 并重新启动
internal sealed class Updater : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly HttpClient http;
    private readonly UpdateClient client;
    private readonly Func<AppSettings> settings;
    private System.Windows.Threading.DispatcherTimer? timer;
    private bool busy;

    public Updater(Func<AppSettings> settings)
    {
        this.settings = settings;
        http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Pop-Windows/{CurrentVersion}");
        client = new UpdateClient(http);
    }

    public static SemVersion CurrentVersion { get; } = ReadCurrentVersion();

    /// 测试时可以用 POP_UPDATE_FEED 指向本机的假发布
    public static string Feed => Environment.GetEnvironmentVariable("POP_UPDATE_FEED") is { Length: > 0 } feed ? feed : UpdateClient.DefaultFeed;

    public ReleaseInfo? Available { get; private set; }

    /// 检查结果变了（发现新版本、开始下载、失败）
    public event Action? Changed;

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
        timer?.Stop();
        http.Dispose();
    }

    public async Task<ReleaseInfo?> CheckAsync(bool userInitiated)
    {
        if (busy) return Available;
        busy = true;
        try
        {
            Available = await client.CheckAsync(Feed, CurrentVersion, settings().IncludePrerelease, CancellationToken.None);
            Status = Available is null ? null : $"发现新版本 {Available.Version}";
            Log.Info(Available is null ? $"检查更新：{CurrentVersion} 已是最新" : $"检查更新：发现 {Available.Version}");
            return Available;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error("检查更新失败", e);
            if (userInitiated) throw new UpdateException($"检查更新失败：{e.Message}", e);
            return null;
        }
        finally
        {
            busy = false;
            Changed?.Invoke();
        }
    }

    /// 下载、校验、替换。成功后启动新版本，返回 true，调用方负责退出当前进程
    public async Task<bool> InstallAsync(ReleaseInfo release, IProgress<double>? progress)
    {
        if (busy) return false;
        busy = true;
        Status = $"正在下载 {release.Version}…";
        Changed?.Invoke();
        try
        {
            var current = Paths.Executable;
            var directory = Path.GetDirectoryName(current)!;
            if (!ExecutableSwap.CanWrite(directory))
                throw new UpdateException($"Pop 所在的文件夹不能写入（{directory}），请把 Pop.exe 放到自己的文件夹里（例如 %LOCALAPPDATA%\\Programs\\Pop）再更新");

            var work = Path.Combine(Paths.LocalData, "updates", release.Version.ToString());
            var newExe = await client.DownloadAndPrepareAsync(release, work, progress, CancellationToken.None);

            var fileVersion = FileVersionInfo.GetVersionInfo(newExe).ProductVersion;
            if (!SemVersion.TryParse(fileVersion, out var actual) || !actual.Equals(release.Version))
                throw new UpdateException($"安装包里的版本（{fileVersion}）和发布的版本（{release.Version}）不一致，已放弃更新");

            if (Signature.CheckUpdate(current, newExe) is { } refusal) throw new UpdateException(refusal);

            ExecutableSwap.Swap(current, newExe);
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
            Status = null;
            throw e as UpdateException ?? new UpdateException($"更新失败：{e.Message}", e);
        }
        finally
        {
            busy = false;
            Changed?.Invoke();
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
