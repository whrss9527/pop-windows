using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public sealed class SettingsDocumentTests : IDisposable
{
    private readonly TempFolder folder = new();
    private string FilePath => folder.File("settings.json");
    public void Dispose() => folder.Dispose();

    [Fact]
    public void MissingFileKeepsFirstUseDefaultsWithoutCreatingFiles()
    {
        var document = SettingsDocument.Open(FilePath);
        Assert.Equal(SettingsReadState.Missing, document.State);
        Assert.True(document.Settings.Enabled); Assert.True(document.Settings.ClipboardHistory);
        Assert.False(document.HasLoadIssue); Assert.False(File.Exists(FilePath));
        Assert.False(Directory.Exists(document.RecoveryDirectory));
    }

    [Fact]
    public void ValidOlderFieldsAndBomRemainCompatible()
    {
        File.WriteAllText(FilePath, "{\"Enabled\":false,\"holdMilliseconds\":400,\"clipboardHistory\":false,\"unknown\":1}", Encoding.Unicode);
        var document = SettingsDocument.Open(FilePath);
        Assert.Equal(SettingsReadState.Loaded, document.State);
        Assert.False(document.HasLoadIssue); Assert.Null(document.ReadError);
        Assert.False(document.Settings.Enabled); Assert.False(document.Settings.ClipboardHistory);
        Assert.True(document.Settings.ClipboardAutomaticCleanup);
        Assert.Equal(400, document.Settings.HoldMilliseconds);
        document.Save(document.Settings);
        Assert.False(Directory.Exists(document.RecoveryDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"enabled\":false,")]
    public void InvalidFileIsNotModifiedAndTemporarySettingsDoNotStartRecording(string raw)
    {
        File.WriteAllText(FilePath, raw);
        var original = File.ReadAllBytes(FilePath);
        var document = SettingsDocument.Open(FilePath);
        Assert.Equal(SettingsReadState.Invalid, document.State);
        Assert.True(document.HasLoadIssue); Assert.NotNull(document.ReadError);
        Assert.False(document.Settings.Enabled); Assert.False(document.Settings.ClipboardHistory);
        Assert.False(document.Settings.ClipboardAutomaticCleanup);
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.False(Directory.Exists(document.RecoveryDirectory));
    }

    [Fact]
    public void SavePreservesExactOriginalBytesThenWritesNewSettingsOnce()
    {
        var raw = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{坏配置")).ToArray();
        File.WriteAllBytes(FilePath, raw);
        var document = SettingsDocument.Open(FilePath);
        document.Settings.HoldMilliseconds = 600;
        document.Save(document.Settings);
        Assert.False(document.HasLoadIssue); Assert.Equal(SettingsReadState.Loaded, document.State);
        Assert.NotNull(document.RecoveryPath); Assert.Equal(raw, File.ReadAllBytes(document.RecoveryPath!));
        Assert.Equal(600, AppSettings.Load(FilePath).HoldMilliseconds);
        Assert.False(SettingsDocument.Open(FilePath).Settings.ClipboardAutomaticCleanup);
        document.Settings.HoldMilliseconds = 650; document.Save(document.Settings);
        Assert.Single(Directory.GetFiles(document.RecoveryDirectory));
        Assert.Equal(raw, File.ReadAllBytes(document.RecoveryPath!));
    }

    [Fact]
    public void FailedRecoveryCopyDoesNotOverwriteOriginalAndCanRetry()
    {
        File.WriteAllText(FilePath, "broken");
        var document = SettingsDocument.Open(FilePath);
        File.WriteAllText(document.RecoveryDirectory, "目录位置被文件占用");
        var error = Record.Exception(() => document.Save(new AppSettings()));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(document.HasLoadIssue); Assert.Null(document.RecoveryPath);
        Assert.Equal("broken", File.ReadAllText(FilePath));
        File.Delete(document.RecoveryDirectory);
        document.Save(new AppSettings());
        Assert.False(document.HasLoadIssue); Assert.Equal("broken", File.ReadAllText(document.RecoveryPath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrChangedRecoveryIsRecreatedBeforeReplacingOriginal(bool changed)
    {
        File.WriteAllText(FilePath, "broken");
        var document = SettingsDocument.Open(FilePath);
        document.PreserveOriginal();
        var previous = document.RecoveryPath!;
        if (changed) File.WriteAllText(previous, "changed backup"); else File.Delete(previous);
        document.Save(new AppSettings());
        Assert.NotEqual(previous, document.RecoveryPath);
        Assert.Equal("broken", File.ReadAllText(document.RecoveryPath!));
        Assert.Equal(SettingsReadState.Loaded, document.State);
    }

    [Fact]
    public void RepairedOrChangedOriginalRequiresReloadBeforeOverwrite()
    {
        File.WriteAllText(FilePath, "broken");
        var document = SettingsDocument.Open(FilePath);
        const string repaired = "{\"holdMilliseconds\":700}";
        File.WriteAllText(FilePath, repaired);
        Assert.Throws<SettingsFileChangedException>(() => document.Save(new AppSettings()));
        Assert.Equal(repaired, File.ReadAllText(FilePath));
        Assert.Null(document.RecoveryPath);
        Assert.Equal(700, SettingsDocument.Open(FilePath).Settings.HoldMilliseconds);
    }

    [Fact]
    public void DeletedOriginalRequiresReloadInsteadOfCreatingAReplacement()
    {
        File.WriteAllText(FilePath, "broken");
        var document = SettingsDocument.Open(FilePath);
        File.Delete(FilePath);
        Assert.Throws<SettingsFileChangedException>(() => document.Save(new AppSettings()));
        Assert.False(File.Exists(FilePath));
        Assert.Equal(SettingsReadState.Missing, SettingsDocument.Open(FilePath).State);
    }

    [WindowsFact]
    public void UnreadableFileDoesNotBecomeAFirstUseFileOrGetOverwrittenWhenUnlocked()
    {
        const string raw = "{\"holdMilliseconds\":700}";
        File.WriteAllText(FilePath, raw);
        SettingsDocument document;
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            document = SettingsDocument.Open(FilePath);
            Assert.Equal(SettingsReadState.Unreadable, document.State);
            Assert.True(document.HasLoadIssue); Assert.False(document.Settings.ClipboardHistory);
            Assert.Throws<IOException>(() => document.Save(new AppSettings()));
        }
        Assert.Throws<SettingsFileChangedException>(() => document.Save(new AppSettings()));
        Assert.Equal(raw, File.ReadAllText(FilePath));
    }

    [WindowsFact]
    public void UnreadableInvalidFileCanBePreservedAfterTheLockIsReleased()
    {
        File.WriteAllText(FilePath, "broken");
        SettingsDocument document;
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            document = SettingsDocument.Open(FilePath);
        document.Save(new AppSettings());
        Assert.False(document.HasLoadIssue); Assert.Equal("broken", File.ReadAllText(document.RecoveryPath!));
    }

    [WindowsFact]
    public void FailedReplacementKeepsOriginalAndReusesVerifiedRecoveryOnRetry()
    {
        File.WriteAllText(FilePath, "broken");
        var document = SettingsDocument.Open(FilePath);
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => document.Save(new AppSettings()));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Equal("broken", File.ReadAllText(FilePath));
            Assert.NotNull(document.RecoveryPath); Assert.True(document.HasLoadIssue);
        }
        document.Save(new AppSettings());
        Assert.Single(Directory.GetFiles(document.RecoveryDirectory));
        Assert.False(document.HasLoadIssue);
    }
}
