namespace Pop.Core;

/// 一次唤起固定一份选区，昂贵的内容匹配每个功能只运行一次。重新唤起时创建新缓存。
public sealed class ActionAvailability(ClassifiedContent content)
{
    private readonly Dictionary<PopAction, bool> results = new(ReferenceEqualityComparer.Instance);
    public bool IsAvailable(PopAction action)
    {
        if (results.TryGetValue(action, out var value)) return value;
        return results[action] = action.IsAvailable(content);
    }
}
