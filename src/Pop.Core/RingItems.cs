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

    /// 打开链接、邮箱或者路径，放在「搜索」那一格
    public static readonly RingItem Open = new("open", "打开", "\uE8A7", true, ActionOutput.None);

    /// 按选中的内容调整圆盘：选中链接、邮箱或路径时，「搜索」换成「打开」
    public static IReadOnlyList<RingItem> For(ClassifiedContent content)
    {
        if (!content.Has(ContentKind.Url) && !content.Has(ContentKind.Email) && !content.Has(ContentKind.Files)) return Default;
        return Default.Select(i => i.Id == "search" ? Open : i).ToList();
    }

    /// 有没有选中文字时这一格能不能用
    public static bool IsAvailable(RingItem item, string? selection) =>
        !item.NeedsText || !string.IsNullOrEmpty(selection);
}
