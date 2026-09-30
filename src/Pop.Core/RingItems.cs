namespace Pop.Core;

/// 功能执行完之后要做的事
public enum ActionEffect
{
    /// 什么都不做
    None,
    /// 结果显示在卡片上
    Card,
    /// 结果换掉原来选中的文字
    Replace,
    /// 复制到剪贴板，并给个轻提示
    Copy,
    /// 打开网址或者文件
    Open,
    /// 只显示一句轻提示
    Toast,
    /// 打开「全部功能」列表
    ShowAll,
}

public sealed record ActionResult(ActionEffect Effect, string? Text = null, CardContent? Card = null)
{
    public static ActionResult ShowCard(CardContent card) => new(ActionEffect.Card, Card: card);
    public static ActionResult Replace(string text) => new(ActionEffect.Replace, text);
    public static ActionResult Copy(string text) => new(ActionEffect.Copy, text);
    public static ActionResult Open(string target) => new(ActionEffect.Open, target);
    public static ActionResult Toast(string message) => new(ActionEffect.Toast, message);
}

/// 一个功能：圆盘上的一格，也是「全部功能」列表里的一项
/// <param name="Id">功能 ID</param>
/// <param name="Title">显示的名称</param>
/// <param name="Glyph">Segoe Fluent Icons / Segoe MDL2 Assets 里的图标字符</param>
/// <param name="Keywords">搜索用的关键字：拼音、拼音首字母、英文</param>
/// <param name="Requires">适用的内容类型，满足其中之一就能用；None 表示不需要选中内容</param>
/// <param name="Run">执行；返回 null 表示这段内容用不了这个功能</param>
/// <param name="Accepts">在内容类型之外再细看一下内容（比如能不能按行拆开）</param>
public sealed record PopAction(
    string Id,
    string Title,
    string Glyph,
    string Keywords,
    ContentKind Requires,
    Func<ClassifiedContent, ActionResult?> Run,
    Func<ClassifiedContent, bool>? Accepts = null)
{
    public bool IsAvailable(ClassifiedContent content) =>
        Requires == ContentKind.None || ((content.Kinds & Requires) != 0 && (Accepts?.Invoke(content) ?? true));

    /// 搜索框里的文字能不能匹配到这个功能（名称、拼音、英文，不分大小写，忽略空格）
    public bool Matches(string query)
    {
        var q = new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        if (q.Length == 0) return true;
        var haystack = (Title + " " + Keywords).ToLowerInvariant();
        return haystack.Contains(q, StringComparison.Ordinal) || haystack.Replace(" ", "").Contains(q, StringComparison.Ordinal);
    }
}

/// 所有功能
public static partial class Actions
{
    public static readonly PopAction Copy = new("copy", "复制", "", "fz fuzhi copy", ContentKind.Text,
        c => ActionResult.Copy(c.Text));

    public static readonly PopAction Search = new("search", "搜索", "", "ss sousuo search bing", ContentKind.Text,
        c => ActionResult.Open(TextActions.SearchUrl(c.Text)));

    public static readonly PopAction Open = new("open", "打开", "", "dk dakai open link url", ContentKind.Url | ContentKind.Email | ContentKind.Files,
        c => (c.Url ?? c.Path) is { } target ? ActionResult.Open(target) : null);

    public static readonly PopAction Translate = new("translate", "翻译", "", "fy fanyi translate", ContentKind.Text,
        c => ActionResult.Open(TextActions.TranslateUrl(c.Text)));

    public static readonly PopAction Upper = new("upper", "大写", "", "dx daxie upper uppercase", ContentKind.Text,
        c => ActionResult.Replace(TextActions.ToUpper(c.Text)));

    public static readonly PopAction Lower = new("lower", "小写", "", "xx xiaoxie lower lowercase", ContentKind.Text,
        c => ActionResult.Replace(TextActions.ToLower(c.Text)));

    public static readonly PopAction Count = new("count", "字数", "", "zs zishu tj tongji count words", ContentKind.Text,
        c => ActionResult.ShowCard(CardContent.Text("字数统计", TextActions.Describe(TextActions.Count(c.Text)))));

    public static readonly PopAction All = new("all", "全部功能", "", "qb quanbu gn gongneng all more", ContentKind.None,
        _ => new ActionResult(ActionEffect.ShowAll));

    public static readonly PopAction Calculate = new("calc", "计算", "", "js jisuan calc math", ContentKind.Math,
        c => CardFor(c, ContentKind.Math));

    public static readonly PopAction Units = new("unit", "单位换算", "", "dwhs danwei huansuan unit convert", ContentKind.Measurement,
        c => CardFor(c, ContentKind.Measurement));

    public static readonly PopAction Color = new("color", "颜色转换", "", "ys yanse color hex rgb hsl", ContentKind.Color,
        c => CardFor(c, ContentKind.Color));

    public static readonly PopAction Time = new("time", "时间转换", "", "sj shijian time date timestamp sjc", ContentKind.Timestamp | ContentKind.DateTime,
        c => CardFor(c, ContentKind.Timestamp) ?? CardFor(c, ContentKind.DateTime));

    public static readonly PopAction Number = new("number", "数字转换", "", "sz shuzi jz jinzhi number hex binary rmb dx", ContentKind.Number,
        c => CardFor(c, ContentKind.Number));

    private static ActionResult? CardFor(ClassifiedContent content, ContentKind kind) =>
        DirectResults.Card(content, kind) is { } card ? ActionResult.ShowCard(card) : null;

    private static readonly Lazy<IReadOnlyList<PopAction>> list = new(BuildList);

    /// 「全部功能」列表里的顺序。第一次用到时才构建：功能分在几个文件里，静态字段的初始化顺序不能依赖
    public static IReadOnlyList<PopAction> List => list.Value;

    private static IReadOnlyList<PopAction> BuildList()
    {
        var list = new List<PopAction> { Copy, Search, Open, Translate, Calculate, Units, Color, Time, Number, Count, Upper, Lower };
        list.AddRange(TextTools());
        return list;
    }

    public static PopAction? Find(string id) => List.FirstOrDefault(a => a.Id == id) ?? (id == All.Id ? All : null);

    /// 搜索结果：能用的排在前面，同一组里保持原来的顺序
    public static IReadOnlyList<PopAction> Filter(string query, ClassifiedContent content) =>
        List.Where(a => a.Matches(query))
            .Select((a, i) => (a, i))
            .OrderBy(x => x.a.IsAvailable(content) ? 0 : 1)
            .ThenBy(x => x.i)
            .Select(x => x.a)
            .ToList();
}

public static class RingItems
{
    /// 默认的圆盘：正上方开始顺时针
    public static readonly IReadOnlyList<PopAction> Default =
        [Actions.Copy, Actions.Search, Actions.Translate, Actions.Upper, Actions.Count, Actions.All];

    /// 按选中的内容调整圆盘：选中链接、邮箱或路径时，「搜索」换成「打开」
    public static IReadOnlyList<PopAction> For(ClassifiedContent content)
    {
        if (!Actions.Open.IsAvailable(content)) return Default;
        return Default.Select(i => i.Id == Actions.Search.Id ? Actions.Open : i).ToList();
    }
}
