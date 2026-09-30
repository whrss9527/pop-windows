using System.IO.Compression;
using Pop.Core;

namespace Pop.Core.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("v0.9.0", "1.0.0")]
    [InlineData("0.2.0-beta.1", "0.2.0")]
    [InlineData("0.2.0-beta.1", "0.2.0-beta.2")]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.10")]
    [InlineData("0.2.0-alpha", "0.2.0-beta")]
    [InlineData("0.2.0-beta", "0.2.0-beta.1")]
    public void Ordering(string older, string newer)
    {
        Assert.True(SemVersion.Parse(older) < SemVersion.Parse(newer));
        Assert.True(SemVersion.Parse(newer) > SemVersion.Parse(older));
    }

    [Theory]
    [InlineData("0.1.0.0", "0.1.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    public void Normalizes(string text, string expected)
    {
        Assert.Equal(expected, SemVersion.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.x.0")]
    [InlineData("1.0.0-")]
    [InlineData("-1.0.0")]
    public void RejectsGarbage(string text)
    {
        Assert.False(SemVersion.TryParse(text, out _));
    }
}

public class ReleaseFeedTests
{
    private static string Release(string tag, bool prerelease = false, bool draft = false, bool withAssets = true)
    {
        var v = tag.TrimStart('v');
        var assets = withAssets
            ? $$"""[{"name":"Pop-{{v}}-win-x64.zip","browser_download_url":"https://x/{{v}}.zip"},{"name":"SHA256SUMS.txt","browser_download_url":"https://x/{{v}}.sums"}]"""
            : "[]";
        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"body":"说明","assets":{{assets}}}""";
    }

    [Fact]
    public void PicksNewestStable()
    {
        var json = $"[{Release("v0.3.0-beta.1", prerelease: true)},{Release("v0.2.0")},{Release("v0.1.1")},{Release("v0.1.0")}]";
        var releases = ReleaseFeed.Parse(json);
        var pick = ReleaseFeed.PickUpdate(releases, SemVersion.Parse("0.1.0"), includePrerelease: false);
        Assert.NotNull(pick);
        Assert.Equal("0.2.0", pick.Version.ToString());
        Assert.Equal("https://x/0.2.0.zip", pick.PackageUrl);
        Assert.Equal("https://x/0.2.0.sums", pick.ChecksumsUrl);
        Assert.Equal("Pop-0.2.0-win-x64.zip", pick.PackageName);
        Assert.Equal("说明", pick.Notes);
    }

    [Fact]
    public void PrereleaseWhenOptedIn()
    {
        var json = $"[{Release("v0.3.0-beta.1", prerelease: true)},{Release("v0.2.0")}]";
        var pick = ReleaseFeed.PickUpdate(ReleaseFeed.Parse(json), SemVersion.Parse("0.2.0"), includePrerelease: true);
        Assert.Equal("0.3.0-beta.1", pick?.Version.ToString());
        Assert.Null(ReleaseFeed.PickUpdate(ReleaseFeed.Parse(json), SemVersion.Parse("0.2.0"), includePrerelease: false));
    }

    [Fact]
    public void SkipsDraftsAndReleasesWithoutAssets()
    {
        var json = $"[{Release("v0.4.0", draft: true)},{Release("v0.3.0", withAssets: false)},{Release("nightly")},{Release("v0.2.0")}]";
        var releases = ReleaseFeed.Parse(json);
        Assert.Single(releases);
        Assert.Equal("0.2.0", releases[0].Version.ToString());
    }

    [Fact]
    public void NoUpdateWhenCurrentIsNewest()
    {
        var json = $"[{Release("v0.1.0")}]";
        Assert.Null(ReleaseFeed.PickUpdate(ReleaseFeed.Parse(json), SemVersion.Parse("0.1.0"), true));
        Assert.Empty(ReleaseFeed.Parse("{\"message\":\"rate limited\"}"));
    }
}

public sealed class UpdatePackageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "pop-tests-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string MakeZip(string name, params (string Entry, string Content)[] files)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public void ChecksumParsing()
    {
        var hash = new string('a', 64);
        var sums = Checksums.Parse($"{hash}  Pop-0.1.0-win-x64.zip\r\n{new string('B', 64)} *other.zip\n# 注释\nbroken line\n");
        Assert.Equal(2, sums.Count);
        Assert.True(Checksums.Matches(sums, "Pop-0.1.0-win-x64.zip", hash.ToUpperInvariant()));
        Assert.Equal(new string('b', 64), sums["other.zip"]);
        Assert.False(Checksums.Matches(sums, "missing.zip", hash));
    }

    [Fact]
    public void PreparesVerifiedPackage()
    {
        var zip = MakeZip("Pop-0.2.0-win-x64.zip", ("Pop.exe", "new"), ("README.txt", "hi"));
        var sums = $"{Checksums.Sha256File(zip)}  Pop-0.2.0-win-x64.zip\n";
        var exe = UpdatePackage.Prepare(zip, "Pop-0.2.0-win-x64.zip", sums, Path.Combine(dir, "staging"));
        Assert.Equal("new", File.ReadAllText(exe));
    }

    [Fact]
    public void RejectsWrongChecksum()
    {
        var zip = MakeZip("Pop-0.2.0-win-x64.zip", ("Pop.exe", "new"));
        var sums = $"{new string('0', 64)}  Pop-0.2.0-win-x64.zip\n";
        var e = Assert.Throws<UpdateException>(() => UpdatePackage.Prepare(zip, "Pop-0.2.0-win-x64.zip", sums, Path.Combine(dir, "staging")));
        Assert.Contains("校验和不对", e.Message);
        Assert.False(Directory.Exists(Path.Combine(dir, "staging")));
    }

    [Fact]
    public void RejectsMissingChecksumAndMissingExe()
    {
        var zip = MakeZip("Pop-0.2.0-win-x64.zip", ("other.exe", "x"));
        Assert.Throws<UpdateException>(() => UpdatePackage.Prepare(zip, "Pop-0.2.0-win-x64.zip", "", Path.Combine(dir, "s1")));
        var sums = $"{Checksums.Sha256File(zip)}  Pop-0.2.0-win-x64.zip\n";
        var e = Assert.Throws<UpdateException>(() => UpdatePackage.Prepare(zip, "Pop-0.2.0-win-x64.zip", sums, Path.Combine(dir, "s2")));
        Assert.Contains("Pop.exe", e.Message);
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        var zip = MakeZip("Pop-0.2.0-win-x64.zip", ("../evil.txt", "x"), ("Pop.exe", "new"));
        var sums = $"{Checksums.Sha256File(zip)}  Pop-0.2.0-win-x64.zip\n";
        Assert.Throws<UpdateException>(() => UpdatePackage.Prepare(zip, "Pop-0.2.0-win-x64.zip", sums, Path.Combine(dir, "staging")));
        Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
    }

    [Fact]
    public void SwapReplacesAndKeepsOld()
    {
        var current = Path.Combine(dir, "Pop.exe");
        var fresh = Path.Combine(dir, "new.exe");
        File.WriteAllText(current, "old");
        File.WriteAllText(fresh, "new");
        File.WriteAllText(ExecutableSwap.OldPath(current), "older leftover");
        ExecutableSwap.Swap(current, fresh);
        Assert.Equal("new", File.ReadAllText(current));
        Assert.Equal("old", File.ReadAllText(ExecutableSwap.OldPath(current)));
        Assert.True(ExecutableSwap.CleanUp(current));
        Assert.False(File.Exists(ExecutableSwap.OldPath(current)));
    }

    [Fact]
    public void SwapRollsBackWhenNewFileIsMissing()
    {
        var current = Path.Combine(dir, "Pop.exe");
        File.WriteAllText(current, "old");
        Assert.Throws<UpdateException>(() => ExecutableSwap.Swap(current, Path.Combine(dir, "missing.exe")));
        Assert.Equal("old", File.ReadAllText(current));
        Assert.False(File.Exists(ExecutableSwap.OldPath(current)));
    }

    [Fact]
    public async Task ClientReadsLocalFeed()
    {
        var zip = MakeZip("Pop-0.2.0-win-x64.zip", ("Pop.exe", "new"));
        var sumsPath = Path.Combine(dir, "SHA256SUMS.txt");
        File.WriteAllText(sumsPath, $"{Checksums.Sha256File(zip)}  Pop-0.2.0-win-x64.zip\n");
        var feed = Path.Combine(dir, "feed.json");
        var zipUrl = new Uri(zip).AbsoluteUri;
        var sumsUrl = new Uri(sumsPath).AbsoluteUri;
        File.WriteAllText(feed, $$"""[{"tag_name":"v0.2.0","assets":[{"name":"Pop-0.2.0-win-x64.zip","browser_download_url":"{{zipUrl}}"},{"name":"SHA256SUMS.txt","browser_download_url":"{{sumsUrl}}"}]}]""");

        using var http = new HttpClient();
        var client = new UpdateClient(http);
        var release = await client.CheckAsync(feed, SemVersion.Parse("0.1.0"), false, CancellationToken.None);
        Assert.NotNull(release);
        var exe = await client.DownloadAndPrepareAsync(release, Path.Combine(dir, "work"), null, CancellationToken.None);
        Assert.Equal("new", File.ReadAllText(exe));
    }
}
