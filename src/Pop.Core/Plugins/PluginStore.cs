using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 保存、导入插件失败，Message 是给用户看的原因
public sealed class PluginStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// 读一次插件文件夹的结果
/// <param name="Manifests">读到的插件，按名称排好</param>
/// <param name="Files">插件 ID → 所在文件（手动放进来的文件名不一定是 ID）</param>
/// <param name="Errors">读不了的文件（文件名 → 原因），设置里提示用户</param>
public sealed record PluginLoadResult(
    IReadOnlyList<PluginManifest> Manifests,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, string> Errors);

/// 用户插件的存储：每个插件是插件文件夹（App 用 %APPDATA%\Pop\Plugins）里的一个 JSON 文件，
/// 可以直接用文本编辑器修改、拷贝给别人，和 macOS 版的插件文件通用。
/// 可以在多个线程上使用；Changed 在做出修改的线程上触发（监视文件夹时是线程池线程），界面要自己切回界面线程
public sealed class PluginStore : IDisposable
{
    /// 文件夹有变化后等这么久再重新载入：编辑器保存文件往往是好几步操作，合并成一次
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object gate = new();
    private readonly Func<string, bool> isReserved;
    private IReadOnlyList<PluginManifest> manifests = [];
    private IReadOnlyDictionary<string, string> loadErrors = new Dictionary<string, string>();
    private Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private Timer? reloadTimer;
    private bool disposed;

    /// <param name="directory">插件文件夹，不存在时自动创建</param>
    /// <param name="isReserved">内置功能的 ID，插件不能用；默认是 Actions 里的全部功能</param>
    public PluginStore(string directory, Func<string, bool>? isReserved = null)
    {
        Directory = directory;
        this.isReserved = isReserved ?? IsBuiltinId;
        TryCreateDirectory();
        Apply(ReadAll(directory, this.isReserved));
    }

    public string Directory { get; }

    /// 插件列表（按名称排好的快照）
    public IReadOnlyList<PluginManifest> Manifests
    {
        get
        {
            lock (gate) return manifests;
        }
    }

    /// 读不了的文件（文件名 → 原因）
    public IReadOnlyDictionary<string, string> LoadErrors
    {
        get
        {
            lock (gate) return loadErrors;
        }
    }

    /// 插件列表或者读取错误变了：保存、删除、导入，或者重新载入时发现文件夹里的文件变了
    public event EventHandler? Changed;

    /// 和 Windows 版内置功能重名的 ID（插件自己的 ID 不算）
    public static bool IsBuiltinId(string id) => Actions.IsBuiltIn(id);

    public PluginManifest? Find(string id)
    {
        lock (gate) return manifests.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// 插件所在的文件
    public string? FilePath(string id)
    {
        lock (gate) return files.TryGetValue(id, out var path) ? path : null;
    }

    // MARK: 读取

    /// 读插件文件夹里的全部 *.json（隐藏文件和点开头的文件除外）。读不了、ID 无效或者重复的文件记到 Errors 里跳过；
    /// 手写的文件可以不写 ID 和名称，用文件名代替
    public static PluginLoadResult ReadAll(string directory, Func<string, bool> isReserved)
    {
        var loaded = new List<PluginManifest>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in JsonFiles(directory))
        {
            var fileName = Path.GetFileName(path);
            PluginManifest? manifest;
            try
            {
                manifest = PluginManifest.FromJson(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                manifest = null;
            }
            if (manifest is null)
            {
                errors[fileName] = "不是有效的插件文件";
                continue;
            }
            var baseName = Path.GetFileNameWithoutExtension(path);
            if (manifest.Id.Length == 0) manifest = manifest with { Id = baseName };
            if (manifest.Name.Trim().Length == 0) manifest = manifest with { Name = baseName };
            if (!PluginManifest.IsUsableId(manifest.Id, isReserved))
            {
                errors[fileName] = $"插件 ID「{manifest.Id}」无效，只能包含字母、数字、点、横线和下划线，也不能和内置功能重名";
                continue;
            }
            // 文件名在 Windows 上不分大小写，ID 也按不分大小写查重
            if (files.TryGetValue(manifest.Id, out var existing))
            {
                errors[fileName] = $"和 {Path.GetFileName(existing)} 的插件 ID 重复";
                continue;
            }
            files[manifest.Id] = path;
            loaded.Add(manifest);
        }
        return new PluginLoadResult(Sorted(loaded), files, errors);
    }

    private static IEnumerable<string> JsonFiles(string directory)
    {
        IEnumerable<FileInfo> entries;
        try
        {
            entries = new DirectoryInfo(directory).EnumerateFiles().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return entries
            .Where(file => file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                           !file.Name.StartsWith('.') && !file.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(file => file.FullName)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// 按名称排序（不分大小写，数字按大小比较），名称相同的按 ID
    public static IReadOnlyList<PluginManifest> Sorted(IEnumerable<PluginManifest> manifests) =>
        manifests.OrderBy(m => m.Name, NaturalComparer.Instance).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// 重新读一遍文件夹（文件被外部修改、拷贝进来、删除之后）。有变化时触发 Changed 并返回 true
    public bool Reload()
    {
        bool changed;
        lock (gate)
        {
            if (disposed) return false;
            var loaded = ReadAll(Directory, isReserved);
            changed = !loaded.Manifests.SequenceEqual(manifests) || !SameErrors(loaded.Errors, loadErrors);
            Apply(loaded);
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private void Apply(PluginLoadResult loaded)
    {
        manifests = loaded.Manifests;
        loadErrors = loaded.Errors;
        files = new Dictionary<string, string>(loaded.Files, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameErrors(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

    // MARK: 修改

    /// 新建或更新插件，返回实际保存的内容（整理过格式、更新了修改时间）。内容有问题时抛出 PluginStoreException
    public PluginManifest Save(PluginManifest manifest)
    {
        if (manifest.ValidationError() is { } problem) throw new PluginStoreException(problem);
        if (!PluginManifest.IsUsableId(manifest.Id, isReserved)) throw new PluginStoreException("插件 ID 无效");
        var updated = manifest.Normalized() with { ModifiedAt = PluginManifest.Timestamp() };
        lock (gate)
        {
            var path = TargetPath(updated.Id);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                WriteAtomically(path, updated.ToJson());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new PluginStoreException("无法保存插件：" + e.Message, e);
            }
            files[updated.Id] = path;
            manifests = Sorted(manifests.Where(m => !string.Equals(m.Id, updated.Id, StringComparison.OrdinalIgnoreCase)).Append(updated));
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    /// 已有的插件写回原来的文件；新插件用「ID.json」，这个文件名已经被别的文件占着（手写的、读不了的）时换一个
    private string TargetPath(string id)
    {
        if (files.TryGetValue(id, out var existing)) return existing;
        var path = Path.Combine(Directory, id + ".json");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(Directory, id + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
        return path;
    }

    /// 删除插件和它的文件；插件不存在时返回 false
    public bool Delete(string id)
    {
        bool removed;
        lock (gate)
        {
            if (files.TryGetValue(id, out var path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new PluginStoreException("无法删除插件：" + e.Message, e);
                }
                files.Remove(id);
            }
            var remaining = manifests.Where(m => !string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            removed = remaining.Count != manifests.Count;
            manifests = remaining;
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// 导入插件（JSON 文字）。ID 和已有的插件相同时就是更新它；ID 缺了、无效或者和内置功能重名时换一个新 ID。
    /// fallbackName：插件没写名称时用的名称
    public PluginManifest Import(string json, string? fallbackName = null)
    {
        var manifest = PluginManifest.FromJson(json) ?? throw new PluginStoreException("不是有效的插件文件");
        return ImportManifest(manifest, fallbackName);
    }

    /// 导入插件文件；没写名称时用文件名
    public PluginManifest ImportFile(string path)
    {
        var fileName = Path.GetFileName(path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PluginStoreException($"无法读取「{fileName}」：{e.Message}", e);
        }
        var manifest = PluginManifest.FromJson(json) ?? throw new PluginStoreException($"「{fileName}」不是有效的插件文件");
        return ImportManifest(manifest, Path.GetFileNameWithoutExtension(path));
    }

    private PluginManifest ImportManifest(PluginManifest manifest, string? fallbackName)
    {
        if (manifest.Name.Trim().Length == 0 && !string.IsNullOrWhiteSpace(fallbackName)) manifest = manifest with { Name = fallbackName };
        if (!PluginManifest.IsUsableId(manifest.Id, isReserved)) manifest = manifest with { Id = PluginManifest.MakeId() };
        return Save(manifest);
    }

    /// 导出成插件文件（整理过格式），可以直接发给别人或者放到 macOS 版的插件文件夹里
    public static void Export(PluginManifest manifest, string path)
    {
        try
        {
            WriteAtomically(path, manifest.Normalized().ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PluginStoreException("无法导出插件：" + e.Message, e);
        }
    }

    // MARK: 监视文件夹

    /// 监视插件文件夹，文件有变化时自动重新载入（有变化时触发 Changed）。文件夹建不起来时返回 false
    public bool StartWatching()
    {
        lock (gate)
        {
            if (disposed) return false;
            if (watcher is not null) return true;
            TryCreateDirectory();
            FileSystemWatcher created;
            try
            {
                created = new FileSystemWatcher(Directory)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                };
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                return false;
            }
            reloadTimer = new Timer(_ => ReloadQuietly(), null, Timeout.Infinite, Timeout.Infinite);
            created.Created += OnDirectoryChanged;
            created.Changed += OnDirectoryChanged;
            created.Deleted += OnDirectoryChanged;
            created.Renamed += OnDirectoryChanged;
            // 事件太多、缓冲区溢出时也重新载入一次
            created.Error += (_, _) => ScheduleReload();
            created.EnableRaisingEvents = true;
            watcher = created;
            return true;
        }
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        lock (gate)
        {
            if (!disposed) reloadTimer?.Change(ReloadDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReloadQuietly()
    {
        try
        {
            Reload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 文件正被别的程序写着，下一次变化时再读
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            watcher?.Dispose();
            watcher = null;
            reloadTimer?.Dispose();
            reloadTimer = null;
        }
    }

    // MARK: 内部

    private void TryCreateDirectory()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 读的时候当作空文件夹
        }
    }

    /// 先写到同一个文件夹里的临时文件，再替换过去，写到一半断电也不会留下半个文件
    private static void WriteAtomically(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, content, Utf8);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    /// 名称排序：不分大小写，连续的数字按数值比较（「插件 2」排在「插件 10」前面）
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var startX = i;
                    var startY = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                    var digitsX = x[startX..i].TrimStart('0');
                    var digitsY = y[startY..j].TrimStart('0');
                    var order = digitsX.Length != digitsY.Length ? digitsX.Length.CompareTo(digitsY.Length) : string.CompareOrdinal(digitsX, digitsY);
                    if (order != 0) return order;
                }
                else
                {
                    var startX = i;
                    var startY = j;
                    while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;
                    var order = string.Compare(x[startX..i], y[startY..j], CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
                    if (order != 0) return order;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
