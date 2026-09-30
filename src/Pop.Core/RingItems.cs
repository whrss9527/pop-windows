namespace Pop.Core;

/// 功能执行完之后怎么显示结果
public enum ActionOutput
{
    /// 自己处理（复制、打开网页）
    None,
    /// 结果换掉原来选中的文字
    Replace,
    /// 结果显示在卡片上
    Card,
}

/// 圆盘上的一格
public sealed record RingItem(string Id, string Title, string Glyph, bool NeedsText, ActionOutput Output);

public static class RingItems
{
    // 图标是 Segoe Fluent Icons / Segoe MDL2 Assets 里的字符
    public static readonly IReadOnlyList<RingItem> Default =
    [
        new("copy", "复制", "", true, ActionOutput.None),
        new("search", "搜索", "", true, ActionOutput.None),
        new("translate", "翻译", "", true, ActionOutput.None),
        new("upper", "大写", "", true, ActionOutput.Replace),
        new("count", "字数", "", true, ActionOutput.Card),
        new("lower", "小写", "", true, ActionOutput.Replace),
    ];

    /// 有没有选中文字时这一格能不能用
    public static bool IsAvailable(RingItem item, string? selection) =>
        !item.NeedsText || !string.IsNullOrEmpty(selection);
}
