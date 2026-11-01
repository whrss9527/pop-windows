namespace Pop.Core;

/// 读发布信息、下载文件。地址可以是网址，也可以是本机文件（测试时用假的发布）
public sealed class UpdateClient(HttpClient http, ReleasePackage package = ReleasePackage.Portable)
{
    public const string DefaultFeed = "https://api.github.com/repos/whrss9527/pop-windows/releases?per_page=20";

    public async Task<string> GetStringAsync(string location, CancellationToken ct)
    {
        if (LocalPath(location) is { } path) return await File.ReadAllTextAsync(path, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task DownloadAsync(string location, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (LocalPath(location) is { } path)
        {
            await using var local = File.OpenRead(path);
            await CopyDownloadAsync(local, destination, local.Length, progress, ct);
            return;
        }
        using var response = await http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await CopyDownloadAsync(source, destination, total, progress, ct);
    }

    private static async Task CopyDownloadAsync(Stream source, string destination, long? total, IProgress<double>? progress, CancellationToken ct)
    {
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            if (total > 0) progress?.Report((double)received / total.Value);
        }
    }

    public async Task<ReleaseInfo?> CheckAsync(string feed, SemVersion current, bool includePrerelease, CancellationToken ct)
    {
        var json = await GetStringAsync(feed, ct);
        return ReleaseFeed.PickUpdate(ReleaseFeed.Parse(json, package), current, includePrerelease);
    }

    /// 下载安装包和校验和，校验后解压，返回新 Pop.exe 的路径
    public async Task<string> DownloadAndPrepareAsync(ReleaseInfo release, string workDirectory, IProgress<double>? progress, CancellationToken ct,
        IProgress<UpdatePreparationProgress>? preparationProgress = null)
    {
        Directory.CreateDirectory(workDirectory);
        var zip = Path.Combine(workDirectory, release.PackageName);
        var checksums = await GetStringAsync(release.ChecksumsUrl, ct);
        preparationProgress?.Report(new(UpdatePreparationStage.Downloading));
        await DownloadAsync(release.PackageUrl, zip, new InlineProgress<double>(fraction =>
        {
            progress?.Report(fraction);
            preparationProgress?.Report(new(UpdatePreparationStage.Downloading, fraction));
        }), ct);
        ct.ThrowIfCancellationRequested();
        preparationProgress?.Report(new(UpdatePreparationStage.Preparing));
        // 哈希和解压不占用调用方的界面线程。
        return await Task.Run(() => UpdatePackage.Prepare(zip, release.PackageName, checksums, Path.Combine(workDirectory, "staging")), ct);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// file:///C:/x 或者 C:\x 这样的本机路径
    private static string? LocalPath(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : null;
        return Path.IsPathRooted(location) ? location : null;
    }
}
