using Pop.Core;

namespace Pop.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pop-atomic-{Guid.NewGuid():N}");

    [Fact]
    public void ReplacesTheWholeFileAndPreservesUnicode()
    {
        var path = Path.Combine(directory, "设置.json");
        AtomicFile.WriteAllText(path, "原来的内容");
        AtomicFile.WriteAllText(path, "{\"短语\":\"你好 👨‍👩‍👧‍👦\"}");
        Assert.Equal("{\"短语\":\"你好 👨‍👩‍👧‍👦\"}", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedReplacementPreservesExistingContentAndCleansItsOwnTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(target);
        var existing = Path.Combine(target, "user.keep");
        File.WriteAllText(existing, "不能删除");
        var error = Record.Exception(() => AtomicFile.WriteAllText(target, "new"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("不能删除", File.ReadAllText(existing));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void DoesNotOverwriteUnrelatedLegacyTemporaryFiles()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path + ".tmp", "旧的恢复内容");
        new AppSettings { HoldMilliseconds = 456 }.Save(path);
        Assert.Equal(456, AppSettings.Load(path).HoldMilliseconds);
        Assert.Equal("旧的恢复内容", File.ReadAllText(path + ".tmp"));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void AcceptsRelativePaths()
    {
        var path = Path.Combine(directory, "relative.json");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        AtomicFile.WriteAllText(relative, "relative");
        Assert.Equal("relative", File.ReadAllText(path));
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), target);
        if (!string.IsNullOrEmpty(Path.GetDirectoryName(relative)) || !relative.StartsWith("pop-atomic-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试目录超出临时目录范围");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
