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
    /// 打开剪贴板历史
    ShowHistory,
    /// 框选屏幕区域识别文字
    CaptureText,
    /// 框选屏幕区域贴到最前面
    CapturePin,
    /// 翻译 Text，结果显示在卡片上（要联网，由界面异步完成）
    Translate,
    /// 在后台运行自定义插件（Text 是插件 ID）：调用 PluginRunner.RunAsync 得到真正要做的事
    RunPlugin,
}

public sealed record ActionResult(ActionEffect Effect, string? Text = null, CardContent? Card = null)
{
    public static ActionResult ShowCard(CardContent card) => new(ActionEffect.Card, Card: card);
    public static ActionResult Replace(string text) => new(ActionEffect.Replace, text);
    public static ActionResult Copy(string text) => new(ActionEffect.Copy, text);
    public static ActionResult Open(string target) => new(ActionEffect.Open, target);
    public static ActionResult Toast(string message) => new(ActionEffect.Toast, message);
    public static ActionResult RunPlugin(string pluginId) => new(ActionEffect.RunPlugin, pluginId);

    /// 要在后台运行的插件 ID（只有 RunPlugin 有）
    public string? PluginId => Effect == ActionEffect.RunPlugin ? Text : null;
}

/// 一个功能：圆盘上的一格，也是「全部功能」列表里的一项
/// <param name="Id">功能 ID</param>
/// <param name="Title">显示的名称</param>
/// <param name="Glyph">图标名（Fluent System Icons 的名字，比如 Copy24）</param>
/// <param name="Keywords">搜索用的关键字：拼音、拼音首字母、英文</param>
/// <param name="Requires">适用的内容类型，满足其中之一就能用；None 表示不需要选中内容</param>
/// <param name="Run">执行；返回 null 表示这段内容用不了这个功能</param>
/// <param name="Accepts">在内容类型之外再细看一下内容（比如能不能按行拆开）</param>
/// <param name="Category">在设置「功能」页里的分组</param>
/// <param name="Summary">一句话说明，显示在设置里</param>
public sealed record PopAction(
    string Id,
    string Title,
    string Glyph,
    string Keywords,
    ContentKind Requires,
    Func<ClassifiedContent, ActionResult?> Run,
    Func<ClassifiedContent, bool>? Accepts = null,
    string Category = "其他",
    string Summary = "")
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
    public const string Common = "常用";
    public const string TextCategory = "文字";
    public const string Convert = "转换";
    public const string Developer = "开发";
    public const string Screen = "屏幕";
    public const string PluginCategory = "插件";

    /// 设置「功能」页里分组的顺序
    public static readonly IReadOnlyList<string> Categories = [Common, TextCategory, Convert, Developer, Screen, PluginCategory];

    public static readonly PopAction Copy = new("copy", "复制", "Copy24", "fz fuzhi copy", ContentKind.Text,
        c => ActionResult.Copy(c.Text), Category: Common, Summary: "复制选中的文字");

    public static readonly PopAction Search = new("search", "搜索", "Search24", "ss sousuo search bing", ContentKind.Text,
        c => ActionResult.Open(TextActions.SearchUrl(c.Text)), Category: Common, Summary: "用必应搜索选中的文字");

    public static readonly PopAction Open = new("open", "打开", "Open24", "dk dakai open link url", ContentKind.Url | ContentKind.Email | ContentKind.Files,
        c => (c.Url ?? c.Path) is { } target ? ActionResult.Open(target) : null,
        Category: Common, Summary: "打开选中的链接、邮箱或本机路径；选中这些内容时圆盘上的「搜索」会换成它");

    public static readonly PopAction Translate = new("translate", "翻译", "Translate24", "fy fanyi translate", ContentKind.Text,
        c => new ActionResult(ActionEffect.Translate, c.Text), Category: Common, Summary: "在卡片里显示译文，可以复制或替换原文");

    public static readonly PopAction Upper = new("upper", "大写", "TextCaseUppercase24", "dx daxie upper uppercase", ContentKind.Text,
        c => ActionResult.Replace(TextActions.ToUpper(c.Text)), Category: TextCategory, Summary: "转成大写并替换原文");

    public static readonly PopAction Lower = new("lower", "小写", "TextCaseLowercase24", "xx xiaoxie lower lowercase", ContentKind.Text,
        c => ActionResult.Replace(TextActions.ToLower(c.Text)), Category: TextCategory, Summary: "转成小写并替换原文");

    public static readonly PopAction Count = new("count", "字数", "TextWordCount24", "zs zishu tj tongji count words", ContentKind.Text,
        c => ActionResult.ShowCard(new CardContent("字数统计", TextActions.Lines(TextActions.Count(c.Text)))),
        Category: TextCategory, Summary: "字符、汉字、单词和行数");

    public static readonly PopAction All = new("all", "全部功能", "Apps24", "qb quanbu gn gongneng all more", ContentKind.None,
        _ => new ActionResult(ActionEffect.ShowAll), Category: Common, Summary: "打开功能列表，打字搜索");

    public static readonly PopAction ClipboardHistory = new("clipboard", "剪贴板历史", "ClipboardTextLtr24", "jtb jianqieban ls lishi clipboard history paste", ContentKind.None,
        _ => new ActionResult(ActionEffect.ShowHistory), Category: Common, Summary: "复制过的文字、图片和文件，搜索后粘贴");

    public static readonly PopAction ScreenText = new("ocr", "截图识字", "ScanText24", "jtsz jietu shizi ocr screen text sb shibie", ContentKind.None,
        _ => new ActionResult(ActionEffect.CaptureText), Category: Screen, Summary: "框选屏幕区域，离线识别里面的文字");

    public static readonly PopAction ScreenPin = new("pin", "截图贴图", "Pin24", "jttt jietu tietu pin screenshot jt", ContentKind.None,
        _ => new ActionResult(ActionEffect.CapturePin), Category: Screen, Summary: "框选的区域贴在所有窗口最前面");

    public static readonly PopAction Calculate = new("calc", "计算", "Calculator24", "js jisuan calc math", ContentKind.Math,
        c => CardFor(c, ContentKind.Math), Category: Convert, Summary: "算出算式的结果，可以替换原文");

    public static readonly PopAction Units = new("unit", "单位换算", "Ruler24", "dwhs danwei huansuan unit convert", ContentKind.Measurement,
        c => CardFor(c, ContentKind.Measurement), Category: Convert, Summary: "长度、重量、温度、面积、数据大小等，认市制单位");

    public static readonly PopAction Color = new("color", "颜色转换", "Color24", "ys yanse color hex rgb hsl", ContentKind.Color,
        c => CardFor(c, ContentKind.Color), Category: Convert, Summary: "HEX、RGB、HSL 互相转换，附带对比度");

    public static readonly PopAction Time = new("time", "时间转换", "Clock24", "sj shijian time date timestamp sjc", ContentKind.Timestamp | ContentKind.DateTime,
        c => CardFor(c, ContentKind.Timestamp) ?? CardFor(c, ContentKind.DateTime), Category: Convert, Summary: "时间戳和日期，附带农历、第几周和各城市时间");

    public static readonly PopAction Number = new("number", "数字转换", "NumberSymbol24", "sz shuzi jz jinzhi number hex binary rmb dx", ContentKind.Number,
        c => CardFor(c, ContentKind.Number), Category: Convert, Summary: "进制、千分位、英文和中文读法、人民币大写");

    private static ActionResult? CardFor(ClassifiedContent content, ContentKind kind) =>
        DirectResults.Card(content, kind) is { } card ? ActionResult.ShowCard(card) : null;

    private static readonly Lazy<IReadOnlyList<PopAction>> list = new(BuildList);
    private static volatile IReadOnlyList<PopAction>? withPlugins;

    /// 「全部功能」列表里的顺序：内置功能，后面是用户的插件。
    /// 内置功能第一次用到时才构建：功能分在几个文件里，静态字段的初始化顺序不能依赖
    public static IReadOnlyList<PopAction> List => withPlugins ?? list.Value;

    /// 只有内置功能
    public static IReadOnlyList<PopAction> BuiltIn => list.Value;

    /// 用户的插件（插件文件夹变了时 App 调用 SetPlugins 更新）
    public static IReadOnlyList<PopAction> Plugins => withPlugins is { } all ? all.Skip(list.Value.Count).ToList() : [];

    public static void SetPlugins(IReadOnlyList<PopAction> plugins) =>
        withPlugins = plugins.Count == 0 ? null : [.. list.Value, .. plugins];

    /// 是不是内置功能的 ID（插件不能用）
    public static bool IsBuiltIn(string id) => id == All.Id || list.Value.Any(a => a.Id == id);

    private static IReadOnlyList<PopAction> BuildList()
    {
        var list = new List<PopAction> { Copy, Search, Open, Translate, ClipboardHistory, ScreenText, ScreenPin, Calculate, Units, Color, Time, Number, Count, Upper, Lower };
        list.AddRange(TextTools());
        return list;
    }

    public static PopAction? Find(string id) =>
        List.FirstOrDefault(a => a.Id == id) ?? (id == All.Id ? All : null)
        ?? Plugins.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// 能不能停用：「全部功能」一直都在
    public static bool CanDisable(PopAction action) => action.Id != All.Id;

    /// 搜索结果：只列启用的功能，能用的排在前面，同一组里保持原来的顺序
    public static IReadOnlyList<PopAction> Filter(string query, ClassifiedContent content, Func<string, bool>? enabled = null) =>
        List.Where(a => enabled?.Invoke(a.Id) ?? true)
            .Where(a => a.Matches(query))
            .Select((a, i) => (a, i))
            .OrderBy(x => x.a.IsAvailable(content) ? 0 : 1)
            .ThenBy(x => x.i)
            .Select(x => x.a)
            .ToList();
}

public static class RingItems
{
    public const int MinSlots = 4;
    public const int MaxSlots = 8;

    /// 默认的圆盘：正上方开始顺时针
    public static readonly IReadOnlyList<string> DefaultIds = ["copy", "search", "translate", "upper", "count", "all"];

    public static IReadOnlyList<PopAction> Default { get; } = Build(DefaultIds);

    /// 可以放到圆盘上的功能：全部功能列表里的，加上「全部功能」本身
    public static IReadOnlyList<PopAction> Choices => [.. Actions.List, Actions.All];

    /// 按设置里的 ID 排好圆盘；不认识的、停用了的 ID 跳过；剩下的格子不够时用默认的和其他启用的功能补齐
    public static IReadOnlyList<PopAction> Build(IEnumerable<string> ids, Func<string, bool>? enabled = null)
    {
        bool On(PopAction a) => a.Id == Actions.All.Id || (enabled?.Invoke(a.Id) ?? true);
        var wanted = ids.ToList();
        var slots = wanted.Select(Actions.Find).OfType<PopAction>().Where(On).Distinct().ToList();
        if (slots.Count >= MinSlots && slots.Count <= MaxSlots && slots.Count == wanted.Count) return slots;
        var target = Math.Clamp(wanted.Count, MinSlots, MaxSlots);
        if (slots.Count > target) return slots.Take(target).ToList();
        foreach (var candidate in DefaultIds.Select(Actions.Find).OfType<PopAction>().Concat(Actions.List))
        {
            if (slots.Count >= target) break;
            if (On(candidate) && !slots.Contains(candidate)) slots.Add(candidate);
        }
        return slots;
    }

    /// 按选中的内容调整圆盘：选中链接、邮箱或路径时，「搜索」换成「打开」（圆盘上已经有「打开」时不换）
    public static IReadOnlyList<PopAction> For(ClassifiedContent content) => For(Default, content);

    public static IReadOnlyList<PopAction> For(IReadOnlyList<PopAction> ring, ClassifiedContent content)
    {
        if (!Actions.Open.IsAvailable(content) || ring.Contains(Actions.Open)) return ring;
        return ring.Select(i => i.Id == Actions.Search.Id ? Actions.Open : i).ToList();
    }
}
