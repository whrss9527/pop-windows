using System.Text.Json;

namespace Pop.Core;

public sealed record BackupSettingChange(string Title, string Before, string After);
public enum BackupPluginChangeKind { Added, Updated, Unchanged }
public sealed record BackupPluginChange(string Id, string Name, BackupPluginChangeKind Kind);

/// 只比较可迁移的配置，不写入文件、运行插件或读取凭据。
public sealed record BackupPreview(IReadOnlyList<BackupSettingChange> Settings, IReadOnlyList<BackupPluginChange> Plugins, int PreservedPlugins)
{
    public int AddedPlugins => Plugins.Count(p => p.Kind == BackupPluginChangeKind.Added);
    public int UpdatedPlugins => Plugins.Count(p => p.Kind == BackupPluginChangeKind.Updated);
    public int UnchangedPlugins => Plugins.Count(p => p.Kind == BackupPluginChangeKind.Unchanged);
    public bool HasChanges => Settings.Count > 0 || AddedPlugins > 0 || UpdatedPlugins > 0;

    public static BackupPreview Compare(SettingsBackup backup, AppSettings current, IReadOnlyList<PluginManifest> existing)
    {
        var before = AppSettings.FromJson(current.ToJson());
        var after = AppSettings.FromJson(backup.Settings.ToJson());
        using var beforeJson = JsonDocument.Parse(before.ToJson());
        using var afterJson = JsonDocument.Parse(after.ToJson());
        var changes = new List<BackupSettingChange>();
        void Group(string title, string[] properties, Func<AppSettings, string> summary)
        {
            if (properties.All(p => Same(beforeJson.RootElement.GetProperty(p), afterJson.RootElement.GetProperty(p)))) return;
            var oldSummary = summary(before); var newSummary = summary(after);
            changes.Add(new(title, oldSummary, oldSummary == newSummary ? newSummary + " · 内容有更新" : newSummary));
        }
        Group("鼠标唤起", ["enabled", "holdMilliseconds", "triggerMode", "triggerModifier", "selectionToolbar", "triggerExcludedApps"],
            s => $"鼠标唤起{On(s.Enabled)} · {s.HoldMilliseconds} ms · 工具条{On(s.SelectionToolbar)} · 排除 {s.TriggerExcludedApps.Count} 个应用");
        Group("圆盘布局", ["ringSlots", "appRings", "keepRingOpen"], s => $"默认 {s.RingSlots.Count} 格 · {s.AppRings.Count} 个独立布局 · 松开后{(s.KeepRingOpen ? "保持菜单" : "执行或关闭")}");
        Group("直达规则", ["directResults", "directKinds", "rules"], s => $"直接出结果{On(s.DirectResults)} · {s.EffectiveRules.Count} 条规则");
        Group("功能快捷键", ["actionShortcuts"], s => $"{s.ActionShortcuts.Count} 项自定义绑定或停用");
        Group("功能开关", ["disabledActions"], s => $"停用 {s.DisabledActions.Count} 项功能");
        Group("常用短语", ["snippets"], s => $"{s.Snippets.Count} 条短语");
        Group("翻译偏好", ["translationEngine", "translateTarget", "azureTranslatorRegion"], s => $"{EngineName(s.TranslationEngine)} · 目标语言 {(s.TranslateTarget == "auto" ? "自动双向" : Languages.Name(s.TranslateTarget))}");
        Group("剪贴板偏好", ["clipboardHistory", "clipboardRetentionDays", "clipboardMaxItems", "clipboardExcludedApps"],
            s => $"历史记录{On(s.ClipboardHistory)} · {s.ClipboardRetentionDays} 天 · 最多 {s.ClipboardMaxItems} 条 · 排除 {s.ClipboardExcludedApps.Count} 个应用");
        Group("更新偏好", ["checkForUpdates", "includePrerelease"], s => $"自动检查{On(s.CheckForUpdates)} · 测试版{On(s.IncludePrerelease)}");

        var oldPlugins = existing.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var incoming = backup.Plugins.Select(p => new BackupPluginChange(p.Id, p.Name,
            !oldPlugins.TryGetValue(p.Id, out var old) ? BackupPluginChangeKind.Added
            : SameJson(old.ToJson(), p.ToJson()) ? BackupPluginChangeKind.Unchanged : BackupPluginChangeKind.Updated)).ToArray();
        var importedIds = backup.Plugins.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(changes, incoming, existing.Count(p => !importedIds.Contains(p.Id)));
    }

    private static string On(bool value) => value ? "开" : "关";
    private static string EngineName(TranslationEngine engine) => engine switch { TranslationEngine.Bing => "必应", TranslationEngine.Azure => "微软翻译", _ => "在浏览器打开" };

    private static bool SameJson(string left, string right)
    {
        using var a = JsonDocument.Parse(left); using var b = JsonDocument.Parse(right);
        return Same(a.RootElement, b.RootElement);
    }

    // 对象的保存顺序不影响内容；数组顺序会影响圆盘、规则和短语的行为，需要比较。
    private static bool Same(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var other) && Same(p.Value, other)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair => Same(pair.First, pair.Second)),
            JsonValueKind.String => a.GetString() == b.GetString(),
            _ => a.GetRawText() == b.GetRawText(),
        };
    }
}
