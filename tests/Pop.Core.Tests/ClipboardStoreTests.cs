using System.Text;
using Microsoft.Data.Sqlite;
using Pop.Core;

namespace Pop.Core.Tests;

public sealed class ClipboardStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pop-clipboard-" + Guid.NewGuid().ToString("N"));
    private readonly List<ClipboardStore> _stores = [];
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ClipboardStore Open()
    {
        var store = new ClipboardStore(_directory);
        _stores.Add(store);
        return store;
    }

    private static ClipboardCapture Text(string value, string? source = null) => new(ClipboardKind.Text, value, null, source);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public void AddDeduplicateAndSearch()
    {
        var store = Open();
        Assert.True(store.IsAvailable);
        Assert.Null(store.OpenError);

        var first = store.Add(Text("hello world", "notepad.exe"), Start);
        var second = store.Add(Text("second"), Start.AddSeconds(10));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);

        // 同样的内容再复制一次：不新增，只把它挪到最前面，来源保持原来的
        Assert.Equal(first, store.Add(Text("hello world"), Start.AddSeconds(20)));
        var items = store.Items();
        Assert.Equal(["hello world", "second"], items.Select(item => item.Text));
        Assert.Equal("notepad.exe", items[0].SourceApp);
        Assert.Equal(Start, items[0].CreatedAt);
        Assert.Equal(Start.AddSeconds(20), items[0].UsedAt);

        Assert.Equal(["hello world"], store.Items("WORLD").Select(item => item.Text));
        Assert.Equal(["hello world"], store.Items("  world ").Select(item => item.Text));
        Assert.Empty(store.Items("100%"));
        Assert.Empty(store.Items("hello_world"));
        Assert.Single(store.Items(limit: 1));
        Assert.Empty(store.Items(limit: -1));
        var (count, bytes) = store.Statistics();
        Assert.Equal(2, count);
        Assert.Equal(Encoding.UTF8.GetByteCount("hello world") + Encoding.UTF8.GetByteCount("second"), bytes);

        // 新的来源会覆盖旧的
        store.Add(Text("second", "code.exe"), Start.AddSeconds(30));
        Assert.Equal("code.exe", store.Items()[0].SourceApp);

        // 重新打开数据库，记录还在
        var ids = store.Items().Select(item => item.Id).ToList();
        store.Dispose();
        Assert.False(store.IsAvailable);
        Assert.Empty(store.Items());
        Assert.Equal(ids, Open().Items().Select(item => item.Id));
    }

    [Fact]
    public void KeepsSubSecondTimes()
    {
        var store = Open();
        var time = new DateTimeOffset(2026, 9, 30, 8, 30, 15, 123, TimeSpan.FromHours(8));
        var id = store.Add(Text("中文内容"), time)!.Value;
        var item = store.Item(id)!;
        Assert.Equal(time, item.CreatedAt);
        Assert.Equal(Encoding.UTF8.GetByteCount("中文内容"), item.ByteSize);
        Assert.Equal(["中文内容"], store.Items("中文").Select(entry => entry.Text));

        store.MarkUsed(id, time.AddMinutes(5));
        Assert.Equal(time.AddMinutes(5), store.Item(id)!.UsedAt);
        Assert.Null(store.Item(id + 100));
    }

    [Fact]
    public void MarkUsedMovesToTheTop()
    {
        var store = Open();
        var older = store.Add(Text("older"), Start)!.Value;
        store.Add(Text("newer"), Start.AddSeconds(1));
        Assert.Equal(["newer", "older"], store.Items().Select(item => item.Text));
        store.MarkUsed(older, Start.AddSeconds(2));
        Assert.Equal(["older", "newer"], store.Items().Select(item => item.Text));
        // 固定的排在最前
        store.SetPinned(true, store.Items()[1].Id);
        Assert.Equal(["newer", "older"], store.Items().Select(item => item.Text));
        Assert.True(store.Items()[0].Pinned);
        store.SetPinned(false, store.Items()[0].Id);
        Assert.Equal(["older", "newer"], store.Items().Select(item => item.Text));
    }

    [Fact]
    public void CleanupKeepsPinnedItems()
    {
        var store = Open();
        var start = DateTimeOffset.FromUnixTimeSeconds(2_000_000);
        for (var index = 0; index < 5; index++)
        {
            store.Add(Text("item " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)), start.AddSeconds(index));
        }
        var oldest = store.Items()[^1];
        Assert.Equal("item 0", oldest.Text);
        store.SetPinned(true, oldest.Id);

        // 最多保留 2 条（固定的不算）
        Assert.Equal(2, store.Cleanup(retentionDays: 0, maxItems: 2, now: start.AddSeconds(10)));
        Assert.Equal(["item 0", "item 4", "item 3"], store.Items().Select(item => item.Text));

        // 超过 1 天的删掉，固定的保留
        Assert.Equal(2, store.Cleanup(retentionDays: 1, maxItems: 100, now: start.AddDays(2)));
        Assert.Equal(["item 0"], store.Items().Select(item => item.Text));

        store.Clear(keepPinned: true);
        Assert.Equal(["item 0"], store.Items().Select(item => item.Text));
        store.Clear(keepPinned: false);
        Assert.Empty(store.Items());
        Assert.Equal((0, 0L), store.Statistics());
    }

    [Fact]
    public void ImagesAndFiles()
    {
        var store = Open();
        var imageId = store.Add(ClipboardCapture.Image(Png), Start)!.Value;
        var image = store.Item(imageId)!;
        Assert.Equal(ClipboardKind.Image, image.Kind);
        var imagePath = store.ImagePath(image)!;
        Assert.Equal(store.ImagesDirectory, Path.GetDirectoryName(imagePath));
        Assert.Equal(Png, File.ReadAllBytes(imagePath));
        Assert.Equal(Png.Length, image.ByteSize);
        Assert.Empty(image.FilePaths);
        // 同一张图不会存两份
        Assert.Equal(imageId, store.Add(ClipboardCapture.Image([.. Png]), Start.AddSeconds(1)));
        Assert.Single(Directory.GetFiles(store.ImagesDirectory));
        // 没有图片数据的不记录
        Assert.Null(store.Add(new ClipboardCapture(ClipboardKind.Image, ""), Start));

        store.Delete(imageId);
        Assert.Null(store.Item(imageId));
        Assert.False(File.Exists(imagePath));

        var files = ClipboardCapture.Files([@"C:\Users\pop\a.txt", @"C:\Users\pop\b c.txt"]);
        Assert.Equal("C:\\Users\\pop\\a.txt\nC:\\Users\\pop\\b c.txt", files.Text);
        var fileId = store.Add(files, Start)!.Value;
        var item = store.Item(fileId)!;
        Assert.Equal(ClipboardKind.Files, item.Kind);
        Assert.Equal([@"C:\Users\pop\a.txt", @"C:\Users\pop\b c.txt"], item.FilePaths);
        Assert.Null(store.ImagePath(item));
    }

    [Fact]
    public void RemovesOrphanImages()
    {
        var store = Open();
        var id = store.Add(ClipboardCapture.Image(Png), Start)!.Value;
        var kept = store.ImagePath(store.Item(id)!)!;
        var orphan = Path.Combine(store.ImagesDirectory, "orphan.png");
        File.WriteAllBytes(orphan, Png);

        store.Cleanup(retentionDays: 0, maxItems: 100, now: Start);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(kept));

        store.Clear(keepPinned: false);
        Assert.Empty(Directory.GetFiles(store.ImagesDirectory));
    }

    [Fact]
    public void CaptureHashDistinguishesKinds()
    {
        var asText = new ClipboardCapture(ClipboardKind.Text, "/tmp/a");
        var asFiles = new ClipboardCapture(ClipboardKind.Files, "/tmp/a");
        Assert.NotEqual(asText.Hash, asFiles.Hash);
        Assert.Equal(asText.Hash, new ClipboardCapture(ClipboardKind.Text, "/tmp/a", null, "other").Hash);
        Assert.StartsWith("text:", asText.Hash, StringComparison.Ordinal);
        Assert.Equal("image:" + Digests.Hex(System.Security.Cryptography.SHA256.HashData(Png)), ClipboardCapture.Image(Png).Hash);
        Assert.Equal("50\\%\\_\\\\", ClipboardStore.EscapeLike("50%_\\"));
    }

    [Fact]
    public void ImagesAreFoundByTheirText()
    {
        var store = Open();
        var id = store.Add(ClipboardCapture.Image(Png), Start)!.Value;
        store.Add(Text("plain"), Start.AddSeconds(1));
        Assert.Equal([id], store.ImagesWithoutRecognizedText(10).Select(item => item.Id));
        Assert.Empty(store.Items("hello"));
        Assert.Null(store.Item(id)!.RecognizedText);

        // 记下识别出的文字后，搜索能找到这张图
        store.SetRecognizedText("HELLO POP 2026", id);
        Assert.Empty(store.ImagesWithoutRecognizedText(10));
        Assert.Equal([id], store.Items("hello").Select(item => item.Id));
        Assert.Equal("HELLO POP 2026", store.Item(id)!.RecognizedText);

        // 识别过但没有文字：存空字符串，不再列为待识别
        store.SetRecognizedText("", id);
        Assert.Equal("", store.Item(id)!.RecognizedText);
        Assert.Empty(store.ImagesWithoutRecognizedText(10));
        Assert.Empty(store.Items("hello"));
    }

    [Fact]
    public void MigratesTheFirstSchema()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "history.sqlite");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, text TEXT NOT NULL DEFAULT '', " +
                "image_name TEXT, hash TEXT NOT NULL UNIQUE, source_app TEXT, created_at REAL NOT NULL, used_at REAL NOT NULL, " +
                "pinned INTEGER NOT NULL DEFAULT 0, byte_size INTEGER NOT NULL DEFAULT 0); " +
                "INSERT INTO items (kind, text, hash, created_at, used_at) VALUES ('text', '旧的记录', 'text:old', 1, 1); " +
                "PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }

        var store = Open();
        Assert.Equal(["旧的记录"], store.Items().Select(item => item.Text));
        Assert.Null(store.Items()[0].RecognizedText);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1), store.Items()[0].CreatedAt);
        Assert.Single(store.Items("旧的"));
        store.Dispose();

        // 再打开不会重复迁移
        Assert.Single(Open().Items());
    }

    [Fact]
    public void ReportsWhenTheFolderCannotBeCreated()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "file");
        File.WriteAllText(blocker, "x");
        using var store = new ClipboardStore(blocker);
        Assert.False(store.IsAvailable);
        Assert.NotNull(store.OpenError);
        Assert.Null(store.Add(Text("x"), Start));
        Assert.Empty(store.Items());
        Assert.Equal((0, 0L), store.Statistics());
    }

    [Fact]
    public void WritesFromSeveralThreads()
    {
        var store = Open();
        Parallel.For(0, 200, index =>
        {
            var text = "entry " + (index % 50).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.NotNull(store.Add(Text(text), Start.AddSeconds(index)));
            store.Items(text, 5);
        });
        Assert.Equal(50, store.Statistics().Count);
    }
}
