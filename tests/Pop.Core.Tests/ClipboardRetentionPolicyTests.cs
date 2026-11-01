using Pop.Core;

namespace Pop.Core.Tests;

public sealed class ClipboardRetentionPolicyTests : IDisposable
{
    private readonly TempFolder folder = new();
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); folder.Dispose(); }

    [Fact]
    public void RecoveredSettingsDoNotDeleteExistingHistoryBeforeOrAfterRestart()
    {
        var path = folder.File("settings.json"); File.WriteAllText(path, "broken");
        var document = SettingsDocument.Open(path);
        using var store = new ClipboardStore(folder.File("clipboard"));
        var now = DateTimeOffset.UtcNow;
        var old = store.Add(new ClipboardCapture(ClipboardKind.Text, "需要保留的旧内容"), now.AddDays(-400))!.Value;
        var pinned = store.Add(new ClipboardCapture(ClipboardKind.Text, "固定的旧内容"), now.AddDays(-500))!.Value;
        store.SetPinned(true, pinned);
        Assert.Equal(0, ClipboardRetentionPolicy.For(document.Settings).Cleanup(store, now));
        Assert.NotNull(store.Item(old));
        document.Save(document.Settings);
        var reopened = SettingsDocument.Open(path);
        Assert.Equal(SettingsReadState.Loaded, reopened.State);
        Assert.False(reopened.Settings.ClipboardAutomaticCleanup);
        Assert.Equal(0, ClipboardRetentionPolicy.For(reopened.Settings).Cleanup(store, now));
        Assert.NotNull(store.Item(old));
        reopened.Settings.ClipboardAutomaticCleanup = true;
        Assert.Equal(1, ClipboardRetentionPolicy.For(reopened.Settings).Cleanup(store, now));
        Assert.Null(store.Item(old)); Assert.NotNull(store.Item(pinned));
    }

    [Fact]
    public void OlderSettingsKeepExistingAutomaticCleanupBehavior()
    {
        var settings = AppSettings.FromJson("{\"clipboardRetentionDays\":90,\"clipboardMaxItems\":1000}");
        Assert.True(settings.ClipboardAutomaticCleanup);
        var policy = ClipboardRetentionPolicy.For(settings);
        Assert.True(policy.Enabled); Assert.Equal(90, policy.Days); Assert.Equal(1000, policy.Maximum);
        settings.ClipboardAutomaticCleanup = false;
        Assert.False(AppSettings.FromJson(settings.ToJson()).ClipboardAutomaticCleanup);
    }
}
