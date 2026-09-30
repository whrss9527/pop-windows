namespace Pop.Core;

/// 卡片底部的一个链接按钮（比如「在浏览器中打开」）
public sealed record CardLink(string Title, string Url);

/// 结果卡片要显示的内容
/// <param name="Title">卡片标题（「计算」「单位换算」）</param>
/// <param name="Lines">一行一个结果，点一行复制这一行的值</param>
/// <param name="Body">没有分行结果时显示的一段文字</param>
/// <param name="Replacement">「替换原文」用的文字；为 null 时不显示这个按钮</param>
/// <param name="Swatch">颜色结果的色块（#RRGGBB）</param>
/// <param name="Monospace">值用等宽字体显示（哈希、编码、JSON）</param>
/// <param name="Caption">底部的一行小字（「英语 → 简体中文 · 必应翻译」）</param>
/// <param name="Source">原文，显示在正文上面（翻译卡片用）</param>
/// <param name="Links">底部的链接按钮</param>
/// <param name="Icon">标题前的图标名（Fluent System Icons）</param>
/// <param name="Loading">还在加载（翻译中），显示进度</param>
public sealed record CardContent(
    string Title,
    IReadOnlyList<ResultLine> Lines,
    string? Body = null,
    string? Replacement = null,
    string? Swatch = null,
    bool Monospace = false,
    string? Caption = null,
    string? Source = null,
    IReadOnlyList<CardLink>? Links = null,
    string? Icon = null,
    bool Loading = false)
{
    /// 「复制」按钮复制的内容：有替换文字时复制它，否则第一行的值，否则整段文字
    public string PrimaryText => Replacement ?? (Lines.Count > 0 ? Lines[0].Value : Body ?? "");

    public static CardContent Text(string title, string body) => new(title, [], body);
}
