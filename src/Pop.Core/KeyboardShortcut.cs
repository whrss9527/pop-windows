using System.Text.Json.Serialization;

namespace Pop.Core;

/// 与 RegisterHotKey 相同的修饰键位，纯逻辑可在任何平台验证配置。
public sealed record KeyboardShortcut(uint Modifiers, uint Key)
{
    public const string RingTarget = "$ring";
    // Shift 单独使用会拦截正常输入；必须同时包含 Ctrl、Alt 或 Win。
    [JsonIgnore]
    public bool IsValid => Modifiers > 0 && (Modifiers & ~15u) == 0 && (Modifiers & 11u) != 0
        && (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 or 0x20 or 0xC0);
    [JsonIgnore]
    public string Display => string.Join("+", new[]
    {
        (Modifiers & 8) != 0 ? "Win" : null, (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 4) != 0 ? "Shift" : null, KeyName(Key),
    }.OfType<string>());

    public static string KeyName(uint key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x70 + 1}",
        0x20 => "Space", 0xC0 => "`", _ => "?",
    };

    public static readonly IReadOnlyDictionary<string, KeyboardShortcut> Defaults = new Dictionary<string, KeyboardShortcut>
    {
        [RingTarget] = new(9, 0x20), ["clipboard"] = new(9, 0x56), ["ocr"] = new(9, 0x4F),
        ["pin"] = new(9, 0x50), ["annotate"] = new(9, 0x41), ["colorPicker"] = new(9, 0x43),
    };

    public static Dictionary<string, KeyboardShortcut> Effective(IReadOnlyDictionary<string, KeyboardShortcut?> overrides)
    {
        var result = new Dictionary<string, KeyboardShortcut>(Defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, shortcut) in overrides)
        {
            if (shortcut is null) result.Remove(id);
            else if (shortcut.IsValid) result[id] = shortcut;
        }
        return result;
    }
}
