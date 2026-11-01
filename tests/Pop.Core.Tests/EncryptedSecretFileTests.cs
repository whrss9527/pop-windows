using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public sealed class EncryptedSecretFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pop-secret-file-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "secrets.json");

    [Fact]
    public void MissingFileIsEmptyWithoutCreatingAnything()
    {
        Assert.Null(new EncryptedSecretFile(FilePath).Read("key"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void UpdatePreservesUnknownEncryptedValuesAndLegacyTemporaryFiles()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{\"key\":\"old-cipher\",\"FutureKey\":\"unknown-cipher\"}");
        File.WriteAllText(FilePath + ".tmp", "legacy-recovery");
        var file = new EncryptedSecretFile(FilePath);
        file.Set("key", "new-cipher");
        Assert.Equal("new-cipher", file.Read("key"));
        Assert.Equal("unknown-cipher", file.Read("FutureKey"));
        Assert.Equal("legacy-recovery", File.ReadAllText(FilePath + ".tmp"));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"key\":null}")]
    [InlineData("{\"key\":\"first\",\"key\":\"second\"}")]
    [InlineData("{broken-json")]
    public void InvalidDocumentIsReportedAndNeverOverwritten(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, json);
        var original = File.ReadAllBytes(FilePath);
        var file = new EncryptedSecretFile(FilePath);
        Assert.Throws<IOException>(() => file.Read("key"));
        Assert.Throws<IOException>(() => file.Set("key", "new-cipher"));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void InvalidUtf8IsPreservedByteForByte()
    {
        Directory.CreateDirectory(directory);
        byte[] original = [0x7b, 0x22, 0xff, 0x22, 0x7d];
        File.WriteAllBytes(FilePath, original);
        Assert.Throws<IOException>(() => new EncryptedSecretFile(FilePath).Set("key", "cipher"));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Utf8BomAndCaseSensitiveUnknownNamesRemainCompatible()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{\"Key\":\"first\",\"key\":\"second\"}", new UTF8Encoding(true));
        var file = new EncryptedSecretFile(FilePath);
        Assert.Equal("first", file.Read("Key"));
        file.Set("key", "updated");
        Assert.Equal("first", file.Read("Key"));
        Assert.Equal("updated", file.Read("key"));
    }

    [Fact]
    public void RemovingOneCredentialPreservesOthers()
    {
        var file = new EncryptedSecretFile(FilePath);
        file.Set("first", "cipher-a"); file.Set("second", "cipher-b");
        file.Set("first", null);
        Assert.Null(file.Read("first"));
        Assert.Equal("cipher-b", file.Read("second"));
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), target);
        if (!string.IsNullOrEmpty(Path.GetDirectoryName(relative)) || !relative.StartsWith("pop-secret-file-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试目录超出临时目录范围");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
