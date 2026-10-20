using System.Text;
using System.Text.Json;

namespace Pop.Core;

/// Windows 设置和 JSON 插件的可迁移副本，不包含凭据、剪贴板历史或开机启动状态。
public sealed record SettingsBackup(AppSettings Settings, IReadOnlyList<PluginManifest> Plugins, string? AppVersion = null)
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "pop-settings"); writer.WriteNumber("version", 1);
            writer.WriteString("platform", "windows"); writer.WriteString("exportedAt", DateTimeOffset.UtcNow);
            if (AppVersion is not null) writer.WriteString("appVersion", AppVersion);
            writer.WritePropertyName("settings");
            using (var settings = JsonDocument.Parse(Settings.ToJson())) settings.RootElement.WriteTo(writer);
            writer.WriteStartArray("plugins");
            foreach (var plugin in Plugins)
            {
                using var document = JsonDocument.Parse(plugin.ToJson());
                document.RootElement.WriteTo(writer);
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (stream.Length > MaximumBytes) throw new InvalidDataException("设置和插件超过 16 MB，请减少内容后再导出备份");
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static SettingsBackup FromJson(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("设置备份超过 16 MB");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != "pop-settings")
                throw new InvalidDataException("这不是 Pop 导出的设置备份");
            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1)
                throw new InvalidDataException("不支持这个备份版本，请更新 Pop 后重试");
            if (!root.TryGetProperty("platform", out var platform) || platform.GetString() != "windows")
                throw new InvalidDataException("请选择 Windows 版导出的设置备份；macOS 的自定义 JSON 插件可在功能页单独导入");
            if (!root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("备份缺少设置或插件列表");
            var manifests = new List<PluginManifest>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in plugins.EnumerateArray())
            {
                var manifest = PluginManifest.FromJson(item.GetRawText());
                if (manifest is null || !PluginManifest.IsUsableId(manifest.Id, Actions.IsBuiltIn)
                    || manifest.ValidationError() is not null || !ids.Add(manifest.Id))
                    throw new InvalidDataException("备份中有无效、重复或与内置功能重名的插件，尚未修改任何设置");
                manifests.Add(manifest);
            }
            return new(AppSettings.FromJson(settings.GetRawText()), manifests);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("读不了这个备份：文件格式不正确或已经损坏", e);
        }
    }

    /// 全部验证通过才保存。插件同 ID 覆盖，其他已有插件保留，设置保存失败时回滚插件。
    public void Restore(string settingsPath, PluginStore store) =>
        store.ImportBatch(Plugins, () => Settings.Save(settingsPath));
}
