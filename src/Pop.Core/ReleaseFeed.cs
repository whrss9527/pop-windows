using System.Text.Json;

namespace Pop.Core;

/// GitHub Releases 里的一个版本
public sealed record ReleaseInfo(
    SemVersion Version,
    string Tag,
    string Notes,
    bool Prerelease,
    string PackageName,
    string PackageUrl,
    string ChecksumsUrl);

/// 读 GitHub Releases 接口（/repos/{owner}/{repo}/releases）返回的列表，挑出可以更新的版本
public static class ReleaseFeed
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    /// 发布包的文件名：Pop-0.1.0-win-x64.zip
    public static string PackageName(SemVersion version) => $"Pop-{version}-win-x64.zip";

    /// 解析失败的条目直接跳过，不影响其他版本
    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (GetBool(item, "draft")) continue;
            var tag = GetString(item, "tag_name");
            if (!SemVersion.TryParse(tag, out var version)) continue;
            var package = PackageName(version);
            string? packageUrl = null, checksumsUrl = null;
            if (item.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (string.IsNullOrEmpty(url)) continue;
                    if (string.Equals(name, package, StringComparison.OrdinalIgnoreCase)) packageUrl = url;
                    else if (string.Equals(name, ChecksumsAssetName, StringComparison.OrdinalIgnoreCase)) checksumsUrl = url;
                }
            }
            // 没有安装包或者校验和的版本不能更新
            if (packageUrl is null || checksumsUrl is null) continue;
            result.Add(new ReleaseInfo(version, tag!, GetString(item, "body") ?? "", GetBool(item, "prerelease") || version.IsPrerelease,
                package, packageUrl, checksumsUrl));
        }
        return result;
    }

    /// 比当前版本新的里面最新的一个；includePrerelease 为 false 时不看测试版
    public static ReleaseInfo? PickUpdate(IEnumerable<ReleaseInfo> releases, SemVersion current, bool includePrerelease) =>
        releases
            .Where(r => includePrerelease || !r.Prerelease)
            .Where(r => r.Version > current)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
