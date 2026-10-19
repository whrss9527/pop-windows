namespace Pop.Core;

/// 直达规则按列表顺序匹配；停用、内容不适用或已卸载的功能不拦住后续规则。
public sealed record DirectRule(string Kind, string ActionId, bool Enabled)
{
    public static List<DirectRule> FromLegacy(IEnumerable<string> enabledKinds) =>
        DirectResults.Kinds.Select(k => new DirectRule(k.Name, DirectResults.ActionFor(k.Kind),
            enabledKinds.Contains(k.Name, StringComparer.OrdinalIgnoreCase))).ToList();

    public static List<DirectRule> Normalize(IEnumerable<DirectRule> rules)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DirectRule>();
        foreach (var rule in rules)
        {
            var name = DirectResults.Kinds.FirstOrDefault(k => string.Equals(k.Name, rule.Kind, StringComparison.OrdinalIgnoreCase)).Name;
            if (name is null || !seen.Add(name)) continue;
            result.Add(rule with { Kind = name });
        }
        result.AddRange(FromLegacy([]).Where(r => seen.Add(r.Kind)));
        return result;
    }

    public static PopAction? Resolve(IEnumerable<DirectRule> rules, ClassifiedContent content, Func<string, bool> enabled)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || DirectResults.KindNamed(rule.Kind) is not { } kind || !content.Has(kind) || !enabled(rule.ActionId)) continue;
            if (Actions.Find(rule.ActionId) is { } action && (action.Requires != ContentKind.None || action.Category == Actions.PluginCategory)
                && (action.Requires == ContentKind.None ? action.Accepts?.Invoke(content) ?? true : action.IsAvailable(content))) return action;
        }
        return null;
    }
}
