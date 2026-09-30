using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Pop.Core;

/// 剪贴板内容的种类，存进数据库时用小写名字
public enum ClipboardKind
{
    Text,
    Image,
    Files,
}

/// 剪贴板历史里的一条记录
public sealed record ClipboardItem
{
    public long Id { get; init; }
    public ClipboardKind Kind { get; init; }

    /// 文字内容；文件是每行一个完整路径；图片为空
    public string Text { get; init; } = "";

    /// 图片文件名（在 Images 文件夹里）
    public string? ImageName { get; init; }

    /// 复制时前台程序的名字
    public string? SourceApp { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// 最后一次复制或粘贴的时间，列表按它排序
    public DateTimeOffset UsedAt { get; init; }

    public bool Pinned { get; init; }
    public long ByteSize { get; init; }

    /// 图片里识别出的文字（搜索时一起找）；还没识别过是 null，识别过但没有文字是空字符串
    public string? RecognizedText { get; init; }

    /// 文件记录里的完整路径，其他种类为空
    public IReadOnlyList<string> FilePaths =>
        Kind == ClipboardKind.Files
            ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];
}

/// 一次复制的内容，还没写进数据库。图片是 PNG 数据
public sealed record ClipboardCapture(ClipboardKind Kind, string Text, byte[]? ImagePng = null, string? SourceApp = null)
{
    /// 复制了一组文件：每行一个完整路径
    public static ClipboardCapture Files(IEnumerable<string> paths, string? sourceApp = null) =>
        new(ClipboardKind.Files, string.Join('\n', paths), null, sourceApp);

    /// 复制了一张图片（PNG 数据）
    public static ClipboardCapture Image(byte[] png, string? sourceApp = null) =>
        new(ClipboardKind.Image, "", png, sourceApp);

    /// 去重用：内容相同的复制只保留一条
    public string Hash => Kind switch
    {
        ClipboardKind.Image => "image:" + Sha256(ImagePng ?? []),
        ClipboardKind.Files => "files:" + Sha256(Encoding.UTF8.GetBytes(Text)),
        _ => "text:" + Sha256(Encoding.UTF8.GetBytes(Text)),
    };

    public long ByteSize => Kind == ClipboardKind.Image ? ImagePng?.Length ?? 0 : Encoding.UTF8.GetByteCount(Text);

    private static string Sha256(byte[] data) => Digests.Hex(SHA256.HashData(data));
}

/// 剪贴板历史数据库：目录下的 history.sqlite（WAL 模式），图片单独存成 PNG 放在旁边的 Images 文件夹里。
/// 只存在本机。所有操作都加锁，可以从任意线程调用。
public sealed class ClipboardStore : IDisposable
{
    private const string Columns = "id, kind, text, image_name, source_app, created_at, used_at, pinned, byte_size, recognized_text";

    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public string Directory { get; }
    public string ImagesDirectory { get; }
    public string DatabasePath { get; }

    /// 打不开数据库时的原因；这时所有读写都不做事
    public string? OpenError { get; private set; }

    /// 默认位置：%LOCALAPPDATA%\Pop\Clipboard
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pop", "Clipboard");

    public ClipboardStore(string directory)
    {
        Directory = directory;
        ImagesDirectory = Path.Combine(directory, "Images");
        DatabasePath = Path.Combine(directory, "history.sqlite");
        lock (_gate)
        {
            Open();
        }
    }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _connection is not null;
            }
        }
    }

    // 读写

    /// 记录一次复制。和已有记录内容相同时只更新时间（挪到最前面），来源为空时保留原来的。返回记录 ID，失败返回 null
    public long? Add(ClipboardCapture capture, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_connection is null)
            {
                return null;
            }
            var hash = capture.Hash;
            var time = ToSeconds(now);
            var existing = Scalar("SELECT id FROM items WHERE hash = $hash", ("$hash", hash));
            if (existing is long id)
            {
                Run("UPDATE items SET used_at = $used, source_app = COALESCE($source, source_app) WHERE id = $id",
                    ("$used", time), ("$source", capture.SourceApp), ("$id", id));
                return id;
            }

            string? imageName = null;
            if (capture.Kind == ClipboardKind.Image)
            {
                if (capture.ImagePng is null)
                {
                    return null;
                }
                // 文件名取内容的哈希：同一张图只存一份
                var name = hash["image:".Length..] + ".png";
                if (!WriteAtomically(Path.Combine(ImagesDirectory, name), capture.ImagePng))
                {
                    return null;
                }
                imageName = name;
            }

            try
            {
                using var command = Command(
                    "INSERT INTO items (kind, text, image_name, hash, source_app, created_at, used_at, pinned, byte_size) " +
                    "VALUES ($kind, $text, $image, $hash, $source, $created, $used, 0, $size); SELECT last_insert_rowid();",
                    ("$kind", KindName(capture.Kind)), ("$text", capture.Text), ("$image", imageName), ("$hash", hash),
                    ("$source", capture.SourceApp), ("$created", time), ("$used", time), ("$size", capture.ByteSize));
                return command.ExecuteScalar() is long inserted ? inserted : null;
            }
            catch (SqliteException)
            {
                return null;
            }
        }
    }

    /// 固定的排在最前，其余按最近使用排序。search 不为空时按文字搜索，图片按里面识别出的文字搜索（英文字母不分大小写）
    public IReadOnlyList<ClipboardItem> Items(string search = "", int limit = 200)
    {
        lock (_gate)
        {
            var keyword = search.Trim();
            var sql = "SELECT " + Columns + " FROM items";
            var parameters = new List<(string, object?)>();
            if (keyword.Length > 0)
            {
                sql += " WHERE text LIKE $pattern ESCAPE '\\' OR recognized_text LIKE $pattern ESCAPE '\\'";
                parameters.Add(("$pattern", "%" + EscapeLike(keyword) + "%"));
            }
            sql += " ORDER BY pinned DESC, used_at DESC LIMIT $limit";
            parameters.Add(("$limit", Math.Max(limit, 0)));
            return Query(sql, [.. parameters]);
        }
    }

    public ClipboardItem? Item(long id)
    {
        lock (_gate)
        {
            return Query("SELECT " + Columns + " FROM items WHERE id = $id", ("$id", id)).FirstOrDefault();
        }
    }

    /// 记下图片里识别出的文字；没有文字时存空字符串，下次不再识别
    public void SetRecognizedText(string text, long id)
    {
        lock (_gate)
        {
            Run("UPDATE items SET recognized_text = $text WHERE id = $id", ("$text", text), ("$id", id));
        }
    }

    /// 还没识别过文字的图片，最近用过的在前
    public IReadOnlyList<ClipboardItem> ImagesWithoutRecognizedText(int limit)
    {
        lock (_gate)
        {
            return Query("SELECT " + Columns + " FROM items WHERE kind = 'image' AND recognized_text IS NULL ORDER BY used_at DESC LIMIT $limit",
                ("$limit", Math.Max(limit, 0)));
        }
    }

    public void SetPinned(bool pinned, long id)
    {
        lock (_gate)
        {
            Run("UPDATE items SET pinned = $pinned WHERE id = $id", ("$pinned", pinned ? 1 : 0), ("$id", id));
        }
    }

    public void MarkUsed(long id, DateTimeOffset now)
    {
        lock (_gate)
        {
            Run("UPDATE items SET used_at = $used WHERE id = $id", ("$used", ToSeconds(now)), ("$id", id));
        }
    }

    public void Delete(long id)
    {
        lock (_gate)
        {
            RemoveRows("SELECT id, image_name FROM items WHERE id = $id", ("$id", id));
        }
    }

    /// 清空历史；keepPinned 为 true 时保留固定的记录
    public void Clear(bool keepPinned)
    {
        lock (_gate)
        {
            RemoveRows(keepPinned ? "SELECT id, image_name FROM items WHERE pinned = 0" : "SELECT id, image_name FROM items");
            Run("PRAGMA wal_checkpoint(TRUNCATE)");
            RemoveOrphanImages();
        }
    }

    /// 删除超过保存天数、或者超出条数上限的记录（固定的记录不受影响）。retentionDays 为 0 表示不按时间清理。返回删除条数
    public int Cleanup(int retentionDays, int maxItems, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cutoff = retentionDays > 0 ? ToSeconds(now) - retentionDays * 86_400.0 : double.MinValue;
            var removed = RemoveRows(
                "SELECT id, image_name FROM items WHERE pinned = 0 AND (used_at < $cutoff OR id NOT IN (" +
                "SELECT id FROM items WHERE pinned = 0 ORDER BY used_at DESC LIMIT $max))",
                ("$cutoff", cutoff), ("$max", Math.Max(maxItems, 0)));
            RemoveOrphanImages();
            return removed;
        }
    }

    /// 记录条数和内容总字节数
    public (int Count, long Bytes) Statistics()
    {
        lock (_gate)
        {
            var rows = Read("SELECT COUNT(*), COALESCE(SUM(byte_size), 0) FROM items", reader => (reader.GetInt32(0), reader.GetInt64(1)));
            return rows.Count > 0 ? rows[0] : (0, 0);
        }
    }

    /// 图片记录对应的 PNG 文件完整路径，其他种类返回 null
    public string? ImagePath(ClipboardItem item) =>
        item.ImageName is { } name ? Path.Combine(ImagesDirectory, name) : null;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_connection is null)
            {
                return;
            }
            // 关掉连接并清空连接池，数据库文件不再被占用，目录可以删除
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
            _connection = null;
        }
    }

    /// 转义 LIKE 里的通配符，转义字符是反斜杠
    public static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    // SQLite

    private void Open()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ImagesDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            OpenError = "无法创建文件夹：" + error.Message;
            return;
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // 只用一个长期打开的连接，不放进连接池，关掉后文件立即释放
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            _connection = connection;
            Run("PRAGMA journal_mode = WAL");
            Run("PRAGMA synchronous = NORMAL");
            Run("CREATE TABLE IF NOT EXISTS items (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "kind TEXT NOT NULL, " +
                "text TEXT NOT NULL DEFAULT '', " +
                "image_name TEXT, " +
                "hash TEXT NOT NULL UNIQUE, " +
                "source_app TEXT, " +
                "created_at REAL NOT NULL, " +
                "used_at REAL NOT NULL, " +
                "pinned INTEGER NOT NULL DEFAULT 0, " +
                "byte_size INTEGER NOT NULL DEFAULT 0)");
            Run("CREATE INDEX IF NOT EXISTS items_order ON items (pinned DESC, used_at DESC)");
            // 第 2 版：加一列图片里识别出的文字
            if (Scalar("PRAGMA user_version") is long version && version < 2)
            {
                Run("ALTER TABLE items ADD COLUMN recognized_text TEXT");
                Run("PRAGMA user_version = 2");
            }
        }
        catch (SqliteException error)
        {
            OpenError = error.Message;
            connection.Dispose();
            _connection = null;
        }
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection!.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    /// 执行一条语句，失败返回 false。只能在锁里调用
    private bool Run(string sql, params (string Name, object? Value)[] parameters)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = Command(sql, parameters);
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        if (_connection is null)
        {
            return null;
        }
        try
        {
            using var command = Command(sql, parameters);
            return command.ExecuteScalar();
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// 逐行读出结果，失败时返回已经读到的。只能在锁里调用
    private List<T> Read<T>(string sql, Func<SqliteDataReader, T> row, params (string Name, object? Value)[] parameters)
    {
        var results = new List<T>();
        if (_connection is null)
        {
            return results;
        }
        try
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(row(reader));
            }
        }
        catch (SqliteException)
        {
        }
        return results;
    }

    private List<ClipboardItem> Query(string sql, params (string Name, object? Value)[] parameters) =>
        Read(sql, ItemFrom, parameters).OfType<ClipboardItem>().ToList();

    /// 删除查询出来的记录和对应的图片文件，返回删除条数。只能在锁里调用
    private int RemoveRows(string sql, params (string Name, object? Value)[] parameters)
    {
        var victims = Read(sql, reader => (Id: reader.GetInt64(0), ImageName: reader.IsDBNull(1) ? null : reader.GetString(1)), parameters);
        if (victims.Count == 0 || _connection is null)
        {
            return 0;
        }
        try
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var victim in victims)
            {
                using var command = Command("DELETE FROM items WHERE id = $id", ("$id", victim.Id));
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (SqliteException)
        {
            return 0;
        }
        foreach (var victim in victims)
        {
            if (victim.ImageName is { } name)
            {
                TryDelete(Path.Combine(ImagesDirectory, name));
            }
        }
        return victims.Count;
    }

    /// 删掉数据库里没有引用的图片（比如写完图片还没来得及记录就退出了）。只能在锁里调用
    private void RemoveOrphanImages()
    {
        if (_connection is null)
        {
            return;
        }
        var referenced = Read("SELECT image_name FROM items WHERE image_name IS NOT NULL", reader => reader.GetString(0))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(ImagesDirectory, "*.png");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var file in files)
        {
            if (!referenced.Contains(Path.GetFileName(file)))
            {
                TryDelete(file);
            }
        }
    }

    private static bool WriteAtomically(string path, byte[] data)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ClipboardItem? ItemFrom(SqliteDataReader reader)
    {
        if (ParseKind(reader.IsDBNull(1) ? null : reader.GetString(1)) is not { } kind)
        {
            return null;
        }
        return new ClipboardItem
        {
            Id = reader.GetInt64(0),
            Kind = kind,
            Text = reader.IsDBNull(2) ? "" : reader.GetString(2),
            ImageName = reader.IsDBNull(3) ? null : reader.GetString(3),
            SourceApp = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = FromSeconds(reader.GetDouble(5)),
            UsedAt = FromSeconds(reader.GetDouble(6)),
            Pinned = reader.GetInt64(7) != 0,
            ByteSize = reader.GetInt64(8),
            RecognizedText = reader.IsDBNull(9) ? null : reader.GetString(9),
        };
    }

    private static string KindName(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Image => "image",
        ClipboardKind.Files => "files",
        _ => "text",
    };

    private static ClipboardKind? ParseKind(string? name) => name switch
    {
        "text" => ClipboardKind.Text,
        "image" => ClipboardKind.Image,
        "files" => ClipboardKind.Files,
        _ => null,
    };

    /// 时间存成从 1970 年起的秒数（小数），精确到 100 纳秒
    private static double ToSeconds(DateTimeOffset time) =>
        (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (double)TimeSpan.TicksPerSecond;

    private static DateTimeOffset FromSeconds(double seconds) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));
}
