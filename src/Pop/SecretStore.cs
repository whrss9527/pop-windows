using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pop;

/// 保存 Key 这类机密：用 Windows 的数据保护接口（DPAPI）加密，只有当前用户在这台电脑上能解开。
/// 存在 %APPDATA%\Pop\secrets.json，不放进 settings.json
internal static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("io.github.whrss9527.pop");
    private static string? fileForDemo;
    private static string FilePath => fileForDemo ?? Path.Combine(Paths.RoamingData, "secrets.json");

    internal static IDisposable UseFileForDemo(string path)
    {
        if (System.Windows.Application.Current is not App { IsDemo: true })
            throw new InvalidOperationException("临时凭据路径仅用于演示回归");
        var full = Path.GetFullPath(path);
        var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("演示凭据必须使用临时目录");
        var previous = fileForDemo; fileForDemo = full;
        return new DemoFileScope(() => fileForDemo = previous);
    }

    private sealed class DemoFileScope(Action restore) : IDisposable
    {
        private Action? restorePath = restore;
        public void Dispose() => Interlocked.Exchange(ref restorePath, null)?.Invoke();
    }

    public static string? Get(string name)
    {
        try
        {
            var all = Load();
            if (!all.TryGetValue(name, out var encrypted)) return null;
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IOException or JsonException)
        {
            Log.Error($"读取保存的 {name} 失败", e);
            return null;
        }
    }

    public static void Set(string name, string? value)
    {
        try
        {
            var all = Load();
            if (string.IsNullOrEmpty(value)) all.Remove(name);
            else all[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"保存 {name} 失败", e);
        }
    }

    private static Dictionary<string, string> Load() =>
        File.Exists(FilePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? []
            : [];

    public const string AzureTranslatorKey = "azureTranslatorKey";
}
