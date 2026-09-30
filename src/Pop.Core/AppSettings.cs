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

    /// 选中算式、带单位的数值、颜色、时间戳时直接出结果，不弹圆盘
    public bool DirectResults { get; set; } = true;

    /// 圆盘上每一格的功能 ID，正上方开始顺时针；4–8 格
    public List<string> RingSlots { get; set; } = [.. RingItems.DefaultIds];

    /// 直接出结果的内容类型：math、measurement、color、timestamp、datetime、number
    public List<string> DirectKinds { get; set; } = [.. Pop.Core.DirectResults.DefaultKindNames];

    /// 记录剪贴板历史
    public bool ClipboardHistory { get; set; } = true;

    /// 剪贴板历史保存多少天
    public int ClipboardRetentionDays { get; set; } = 30;

    /// 剪贴板历史最多保存多少条（固定的不算）
    public int ClipboardMaxItems { get; set; } = 500;

    /// 这些 App（程序名，不带 .exe）复制的内容不记进剪贴板历史
    public List<string> ClipboardExcludedApps { get; set; } = ["KeePass", "KeePassXC", "1Password", "Bitwarden"];

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
                    case "directresults" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.DirectResults = p.Value.GetBoolean();
                        break;
                    case "clipboardhistory" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.ClipboardHistory = p.Value.GetBoolean();
                        break;
                    case "clipboardretentiondays" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var days):
                        settings.ClipboardRetentionDays = Math.Clamp(days, 1, 3650);
                        break;
                    case "clipboardmaxitems" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var max):
                        settings.ClipboardMaxItems = Math.Clamp(max, 10, 100_000);
                        break;
                    case "clipboardexcludedapps" when p.Value.ValueKind == JsonValueKind.Array:
                        settings.ClipboardExcludedApps = Strings(p.Value);
                        break;
                    case "ringslots" when p.Value.ValueKind == JsonValueKind.Array:
                        var slots = Strings(p.Value);
                        if (slots.Count is >= RingItems.MinSlots and <= RingItems.MaxSlots) settings.RingSlots = slots;
                        break;
                    case "directkinds" when p.Value.ValueKind == JsonValueKind.Array:
                        settings.DirectKinds = Strings(p.Value).Where(k => Pop.Core.DirectResults.KindNamed(k) is not null).ToList();
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

    private static List<string> Strings(JsonElement array) =>
        array.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!.Trim())
            .Where(v => v.Length > 0)
            .ToList();

    /// 直接出结果的内容类型（关掉「直接出结果」时为 None）
    public ContentKind DirectKindFlags =>
        !DirectResults ? ContentKind.None : DirectKinds.Select(Pop.Core.DirectResults.KindNamed).Aggregate(ContentKind.None, (all, k) => all | (k ?? ContentKind.None));

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
