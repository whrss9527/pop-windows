using System.Text;
using System.Text.Json;

namespace Pop.Core;

/// 只保存已加密的字符串；读取失败时拒绝覆盖，更新一个凭据时保留其他键。
public sealed class EncryptedSecretFile(string path)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string? Read(string name) => Load().GetValueOrDefault(name);

    public void Set(string name, string? encrypted)
    {
        var values = Load();
        if (encrypted is null) values.Remove(name);
        else values[name] = encrypted;
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(values));
    }

    private Dictionary<string, string> Load()
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        try
        {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(StrictUtf8.GetString(bytes.AsSpan(offset)));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("凭据需要是对象");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Value.ValueKind != JsonValueKind.String || !values.TryAdd(property.Name, property.Value.GetString()!))
                    throw new JsonException("凭据值无效或名称重复");
            return values;
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException)
        {
            throw new IOException("无法读取已保存的凭据，原文件未修改。请检查文件或从备份恢复后重试。", e);
        }
    }
}
