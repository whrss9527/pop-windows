namespace Pop.Core;

/// 读发布信息、下载文件。地址可以是网址，也可以是本机文件（测试时用假的发布）
public sealed class UpdateClient(HttpClient http)
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
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (LocalPath(location) is { } path)
        {
            File.Copy(path, destination, overwrite: true);
            progress?.Report(1);
            return;
        }
        using var response = await http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
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
        return ReleaseFeed.PickUpdate(ReleaseFeed.Parse(json), current, includePrerelease);
    }

    /// 下载安装包和校验和，校验后解压，返回新 Pop.exe 的路径
    public async Task<string> DownloadAndPrepareAsync(ReleaseInfo release, string workDirectory, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(workDirectory);
        var zip = Path.Combine(workDirectory, release.PackageName);
        var checksums = await GetStringAsync(release.ChecksumsUrl, ct);
        await DownloadAsync(release.PackageUrl, zip, progress, ct);
        return UpdatePackage.Prepare(zip, release.PackageName, checksums, Path.Combine(workDirectory, "staging"));
    }

    /// file:///C:/x 或者 C:\x 这样的本机路径
    private static string? LocalPath(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : null;
        return Path.IsPathRooted(location) ? location : null;
    }
}
