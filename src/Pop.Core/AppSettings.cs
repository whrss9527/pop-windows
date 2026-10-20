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
    public TriggerMode TriggerMode { get; set; } = TriggerMode.LongPressRight;
    public TriggerModifier TriggerModifier { get; set; } = TriggerModifier.Alt;
    /// 拖选或双击选词后显示工具条；不支持辅助功能的 App 不会尝试模拟复制。
    public bool SelectionToolbar { get; set; }
    /// 这些程序保留原来的鼠标操作，快捷键仍然可用。
    public List<string> TriggerExcludedApps { get; set; } = [];
    public List<Snippet> Snippets { get; set; } = [.. Snippet.Examples];

    /// 程序名（不带 .exe）到独立圆盘的映射，未配置的应用继承默认圆盘。
    public Dictionary<string, List<string>> AppRings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// null 保留旧版 directKinds 的含义，首次编辑规则时才迁移。
    public List<DirectRule>? Rules { get; set; }
    /// null 值表示不使用这个快捷键，缺少条目表示使用默认值。
    public Dictionary<string, KeyboardShortcut?> ActionShortcuts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// 选中算式、带单位的数值、颜色、时间戳时直接出结果，不弹圆盘
    public bool DirectResults { get; set; } = true;

    /// 圆盘上每一格的功能 ID，正上方开始顺时针；4–12 格
    public List<string> RingSlots { get; set; } = [.. RingItems.DefaultIds];

    /// 松开右键后保持圆盘，点击执行；默认保持按住划选的操作方式。
    public bool KeepRingOpen { get; set; }

    /// 直接出结果的内容类型：math、measurement、color、timestamp、datetime、number
    public List<string> DirectKinds { get; set; } = [.. Pop.Core.DirectResults.DefaultKindNames];

    /// 停用的功能（ID）：不出现在圆盘和「全部功能」里，也不直接出结果
    public List<string> DisabledActions { get; set; } = [];

    /// 翻译服务
    public TranslationEngine TranslationEngine { get; set; } = TranslationEngine.Bing;

    /// 翻译成哪种语言；auto 表示中文翻成英语、其他翻成简体中文
    public string TranslateTarget { get; set; } = "auto";

    /// Microsoft Translator 的区域（Key 存在 Windows 的凭据里，不在这个文件里）
    public string AzureTranslatorRegion { get; set; } = "";

    /// 功能是不是启用了
    public bool IsEnabled(string actionId) => !DisabledActions.Contains(actionId, StringComparer.OrdinalIgnoreCase);

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
        Converters = { new JsonStringEnumConverter() },
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
                    case "snippets" when p.Value.ValueKind == JsonValueKind.Array:
                        var snippets = new List<Snippet>();
                        foreach (var item in p.Value.EnumerateArray())
                            try
                            {
                                var snippet = item.Deserialize<Snippet>(Options);
                                if (snippet is not null) snippets.Add(snippet);
                            }
                            catch (JsonException) { }
                        settings.Snippets = Snippet.Normalize(snippets);
                        break;
                    case "selectiontoolbar" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.SelectionToolbar = p.Value.GetBoolean();
                        break;
                    case "triggerexcludedapps" when p.Value.ValueKind == JsonValueKind.Array:
                        settings.TriggerExcludedApps = Strings(p.Value).Select(ApplicationName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "enabled" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.Enabled = p.Value.GetBoolean();
                        break;
                    case "holdmilliseconds" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var hold):
                        settings.HoldMilliseconds = Math.Clamp(hold, MinHold, MaxHold);
                        break;
                    case "triggermode" when p.Value.ValueKind == JsonValueKind.String && Enum.TryParse<TriggerMode>(p.Value.GetString(), true, out var mode) && Enum.IsDefined(mode):
                        settings.TriggerMode = mode;
                        break;
                    case "triggermodifier" when p.Value.ValueKind == JsonValueKind.String && Enum.TryParse<TriggerModifier>(p.Value.GetString(), true, out var modifier) && Enum.IsDefined(modifier):
                        settings.TriggerModifier = modifier;
                        break;
                    case "apprings" when p.Value.ValueKind == JsonValueKind.Object:
                        foreach (var app in p.Value.EnumerateObject())
                        {
                            var name = ApplicationName(app.Name);
                            if (name.Length == 0 || app.Value.ValueKind != JsonValueKind.Array) continue;
                            var appSlots = Strings(app.Value);
                            if (appSlots.Count is >= RingItems.MinSlots and <= RingItems.MaxSlots) settings.AppRings[name] = appSlots;
                        }
                        break;
                    case "rules" when p.Value.ValueKind == JsonValueKind.Array:
                        var rules = new List<DirectRule>();
                        foreach (var item in p.Value.EnumerateArray())
                        {
                            try
                            {
                                var rule = item.Deserialize<DirectRule>(Options);
                                if (rule is { Kind: not null, ActionId: not null }) rules.Add(rule);
                            }
                            catch (JsonException) { }
                        }
                        settings.Rules = DirectRule.Normalize(rules);
                        break;
                    case "actionshortcuts" when p.Value.ValueKind == JsonValueKind.Object:
                        foreach (var shortcut in p.Value.EnumerateObject())
                        {
                            if (shortcut.Value.ValueKind == JsonValueKind.Null) { settings.ActionShortcuts[shortcut.Name] = null; continue; }
                            try
                            {
                                var binding = shortcut.Value.Deserialize<KeyboardShortcut>(Options);
                                if (binding is { IsValid: true }) settings.ActionShortcuts[shortcut.Name] = binding;
                            }
                            catch (JsonException) { }
                        }
                        break;
                    case "directresults" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.DirectResults = p.Value.GetBoolean();
                        break;
                    case "keepringopen" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        settings.KeepRingOpen = p.Value.GetBoolean();
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
                    case "disabledactions" when p.Value.ValueKind == JsonValueKind.Array:
                        settings.DisabledActions = Strings(p.Value);
                        break;
                    case "translationengine" when p.Value.ValueKind == JsonValueKind.String && Enum.TryParse<TranslationEngine>(p.Value.GetString(), true, out var engine):
                        settings.TranslationEngine = engine;
                        break;
                    case "translatetarget" when p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Trim().Length > 0:
                        settings.TranslateTarget = p.Value.GetString()!.Trim();
                        break;
                    case "azuretranslatorregion" when p.Value.ValueKind == JsonValueKind.String:
                        settings.AzureTranslatorRegion = p.Value.GetString()!.Trim();
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

    public static string ApplicationName(string name)
    {
        var trimmed = name.Trim();
        var basename = trimmed.Split(['/', '\\']).Last();
        return (basename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? basename[..^4] : basename).Trim().ToLowerInvariant();
    }

    public IReadOnlyList<string> SlotsFor(string application) =>
        AppRings.TryGetValue(ApplicationName(application), out var slots) ? slots : RingSlots;

    [JsonIgnore]
    public IReadOnlyList<DirectRule> EffectiveRules => Rules is null ? DirectRule.FromLegacy(DirectKinds) : DirectRule.Normalize(Rules);

    public PopAction? DirectActionFor(ClassifiedContent content) =>
        DirectResults ? DirectRule.Resolve(EffectiveRules, content, IsEnabled) : null;

    /// 直接出结果的内容类型（关掉「直接出结果」时为 None）
    public ContentKind DirectKindFlags =>
        !DirectResults ? ContentKind.None : EffectiveRules.Where(r => r.Enabled && IsEnabled(r.ActionId))
            .Select(r => Pop.Core.DirectResults.KindNamed(r.Kind))
            .OfType<ContentKind>()
            .Aggregate(ContentKind.None, (all, k) => all | k);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
