using System.Text;

namespace Pop.Core;

public enum ToolStateFilter { All, Enabled, Disabled }

public sealed record ToolSettingsSelection(IReadOnlyList<PopAction> Items, int Total, int Enabled,
    int MatchingTotal, IReadOnlyList<string> EnableIds);

/// 功能设置的只读筛选与显式批量范围；不把插件或未匹配功能混进操作。
public static class ToolSettingsCatalog
{
    public static IReadOnlyList<PopAction> Entries { get; } = [.. Actions.BuiltIn, Actions.All];

    public static bool IsEnabled(AppSettings settings, PopAction action) =>
        !Actions.CanDisable(action) || settings.IsEnabled(action.Id);

    public static ToolSettingsSelection Select(AppSettings settings, string? query, ToolStateFilter filter = ToolStateFilter.All)
    {
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        var search = query ?? "";
        try { search = search.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { /* 不完整的粘贴字符仍可搜索，不改写输入或设置。 */ }
        var matching = Entries.Where(a => a.Matches(search)).ToArray();
        var items = matching.Where(a => filter == ToolStateFilter.All
            || IsEnabled(settings, a) == (filter == ToolStateFilter.Enabled)).ToArray();
        var enable = items.Where(a => Actions.CanDisable(a) && !IsEnabled(settings, a)).Select(a => a.Id).ToArray();
        return new(items, Entries.Count, Entries.Count(a => IsEnabled(settings, a)), matching.Length, enable);
    }

    public static int EnableVisible(AppSettings settings, string? query, ToolStateFilter filter = ToolStateFilter.All)
    {
        var ids = Select(settings, query, filter).EnableIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return 0;
        settings.DisabledActions.RemoveAll(ids.Contains);
        return ids.Count;
    }
}
