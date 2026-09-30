namespace Pop.Core;

/// 结果卡片要显示的内容
/// <param name="Title">卡片标题（「计算」「单位换算」）</param>
/// <param name="Lines">一行一个结果，点一行复制这一行的值</param>
/// <param name="Body">没有分行结果时显示的一段文字</param>
/// <param name="Replacement">「替换原文」用的文字；为 null 时不显示这个按钮</param>
/// <param name="Swatch">颜色结果的色块（#RRGGBB）</param>
public sealed record CardContent(
    string Title,
    IReadOnlyList<ResultLine> Lines,
    string? Body = null,
    string? Replacement = null,
    string? Swatch = null)
{
    /// 「复制」按钮复制的内容：有替换文字时复制它，否则第一行的值，否则整段文字
    public string PrimaryText => Replacement ?? (Lines.Count > 0 ? Lines[0].Value : Body ?? "");

    public static CardContent Text(string title, string body) => new(title, [], body);
}
