using System.Security.Cryptography;
using System.Text;
using Pop.Core;

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

    internal sealed record ReadResult(string? Value, string? Error);

    public static string? Get(string name) => Read(name).Value;

    public static string? GetTranslationKey(TranslationEngine engine)
    {
        if (engine != TranslationEngine.Azure) return null;
        var saved = Read(AzureTranslatorKey);
        if (saved.Error is not null)
            throw new TranslationException("无法读取已保存的翻译 Key，请在设置 → 翻译中检查并重试保存");
        return saved.Value;
    }

    internal static ReadResult Read(string name)
    {
        try
        {
            var encrypted = new EncryptedSecretFile(FilePath).Read(name);
            if (encrypted is null) return new(null, null);
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
            return new(new UTF8Encoding(false, true).GetString(bytes), null);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            Log.Error($"读取保存的 {name} 失败", e);
            return new(null, e.Message);
        }
    }

    public static void Set(string name, string? value)
    {
        var encrypted = string.IsNullOrEmpty(value) ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        // 由界面处理失败；只有写入成功才能清空输入并显示已保存。
        new EncryptedSecretFile(FilePath).Set(name, encrypted);
    }

    public const string AzureTranslatorKey = "azureTranslatorKey";
}
