using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pop.Core;

/// 设置，存成 %APPDATA%\Pop\settings.json。字段缺了或者写错都用默认值
public sealed class AppSettings
{
    /// 长按右键唤起 Pop
    public bool Enabled { get; set; } = true;

    /// 按住多久算长按（毫秒）
    public int HoldMilliseconds { get; set; } = 250;

    /// 自动检查更新
    public bool CheckForUpdates { get; set; } = true;

    /// 接收测试版
    public bool IncludePrerelease { get; set; }

    public const int MinHold = 150;
    public const int MaxHold = 1000;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return FromJson(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static AppSettings FromJson(string json)
    {
        var settings = new AppSettings();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return settings;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "enabled" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.Enabled = p.Value.GetBoolean();
                        break;
                    case "holdmilliseconds" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var hold):
                        settings.HoldMilliseconds = Math.Clamp(hold, MinHold, MaxHold);
                        break;
                    case "checkforupdates" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.CheckForUpdates = p.Value.GetBoolean();
                        break;
                    case "includeprerelease" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.IncludePrerelease = p.Value.GetBoolean();
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }
        return settings;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
