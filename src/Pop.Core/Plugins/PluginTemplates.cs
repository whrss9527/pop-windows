using System.Text.RegularExpressions;

namespace Pop.Core;

/// 「新建插件」菜单里的一个示例
/// <param name="Id">示例的 ID</param>
/// <param name="Title">菜单里显示的名称</param>
/// <param name="Manifest">照着改就能用的插件（每次取都是新的插件 ID）</param>
public sealed record PluginTemplate(string Id, string Title, PluginManifest Manifest);

/// 网址模板、AI 指令模板的展开，以及新建插件用的示例
public static partial class PluginTemplates
{
    /// 展开网址模板：{text} 换成编码后的文字（只保留字母、数字和 -._~），{raw} 原样替换。
    /// 展开后不是带协议的完整网址时返回 null
    public static string? ExpandUrl(string template, string text)
    {
        var filled = template
            .Replace("{text}", Encode(text), StringComparison.Ordinal)
            .Replace("{raw}", text, StringComparison.Ordinal)
            .Trim();
        return ParseUrl(filled)?.AbsoluteUri;
    }

    /// 带协议（https:、mailto:……）的完整网址；Linux 上的 /path 这类不算
    public static Uri? ParseUrl(string text) =>
        SchemePrefix().IsMatch(text) && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;

    /// 自定义 AI 指令：{text}（或 {raw}）换成选中的文字；模板里没写的话把文字接在指令后面
    public static string ExpandPrompt(string template, string text)
    {
        var trimmed = template.Trim();
        if (trimmed.Contains("{text}", StringComparison.Ordinal) || trimmed.Contains("{raw}", StringComparison.Ordinal))
            return trimmed.Replace("{text}", text, StringComparison.Ordinal).Replace("{raw}", text, StringComparison.Ordinal);
        return trimmed.Length == 0 ? text : trimmed + "\n\n" + text;
    }

    private static string Encode(string text)
    {
        try
        {
            return Uri.EscapeDataString(text);
        }
        catch (UriFormatException)
        {
            // 残缺的代理项之类编不了码的文字
            return "";
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemePrefix();

    /// 「新建插件」菜单里的示例。和 macOS 版的示例一一对应：Shell 脚本改成了 PowerShell，
    /// 在地图中查找改用必应地图；快捷指令只有 macOS 能用，不放进来
    public static IReadOnlyList<PluginTemplate> All =>
    [
        new("blank-url", "网址（空白）", new PluginManifest
        {
            Name = "新的网页插件",
            Symbol = "globe",
            Action = new PluginAction { Type = PluginActionType.Url, Template = "https://www.google.com/search?q={text}" },
            Output = PluginOutput.None,
        }),
        new("github", "网址：GitHub 搜索", new PluginManifest
        {
            Name = "GitHub 搜索",
            Symbol = "chevron.left.forwardslash.chevron.right",
            Summary = "在 GitHub 上搜索选中的文字",
            Action = new PluginAction { Type = PluginActionType.Url, Template = "https://github.com/search?q={text}&type=code" },
            Output = PluginOutput.None,
        }),
        new("wikipedia", "网址：维基百科", new PluginManifest
        {
            Name = "维基百科",
            Symbol = "book",
            Summary = "在维基百科中查找选中的词条",
            Action = new PluginAction { Type = PluginActionType.Url, Template = "https://zh.wikipedia.org/wiki/Special:Search?search={text}" },
            Output = PluginOutput.None,
        }),
        new("maps", "网址：在地图中查找", new PluginManifest
        {
            Name = "地图",
            Symbol = "map",
            Summary = "在必应地图中查找选中的地址",
            Action = new PluginAction { Type = PluginActionType.Url, Template = "https://www.bing.com/maps?q={text}" },
            Output = PluginOutput.None,
        }),
        new("shell-sort", "PowerShell：按行排序去重", new PluginManifest
        {
            Name = "排序去重",
            Symbol = "arrow.up.arrow.down",
            Summary = "把选中的多行文字排序并去掉重复行",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = "$input | Sort-Object -Unique -CaseSensitive" },
            Output = PluginOutput.Replace,
        }),
        new("shell-say", "PowerShell：环境变量示例", new PluginManifest
        {
            Name = "字数（PowerShell）",
            Symbol = "terminal",
            Summary = "演示如何读取选中的内容",
            Action = new PluginAction
            {
                Type = PluginActionType.Shell,
                Script = "# 选中的文字从标准输入传入（$input 按行读取），也可以用 $env:POP_TEXT\n" +
                         "# 选中的是文件路径时 $env:POP_FILES 是每行一个路径\n" +
                         "[System.Globalization.StringInfo]::new(\"$env:POP_TEXT\").LengthInTextElements",
            },
            Output = PluginOutput.Toast,
        }),
        new("js-reverse", "JavaScript：反转文字", new PluginManifest
        {
            Name = "反转文字",
            Symbol = "arrow.left.arrow.right",
            Summary = "把选中的文字倒过来",
            Action = new PluginAction
            {
                Type = PluginActionType.JavaScript,
                Script = "// input 是选中的文字，返回值会作为结果\nfunction run(input) {\n  return Array.from(input).reverse().join('')\n}",
            },
            Output = PluginOutput.Card,
        }),
        new("js-json-keys", "JavaScript：列出 JSON 的键", new PluginManifest
        {
            Name = "JSON 键名",
            Symbol = "list.bullet",
            Summary = "列出选中 JSON 对象的所有键",
            Match = new PluginMatch { Kinds = ["json"] },
            Action = new PluginAction
            {
                Type = PluginActionType.JavaScript,
                Script = "function run(input) {\n  return Object.keys(JSON.parse(input)).join('\\n')\n}",
            },
            Output = PluginOutput.Card,
        }),
        new("ai-formal", "AI：改写成正式的语气", new PluginManifest
        {
            Name = "正式一点",
            Symbol = "text.quote",
            Summary = "让 AI 把选中的文字改写得正式、礼貌",
            Action = new PluginAction { Type = PluginActionType.Ai, Prompt = "把下面的文字改写得更正式、礼貌，保持原来的语言和意思，只输出改写后的文字：\n\n{text}" },
            Output = PluginOutput.Card,
        }),
        new("ai-reply", "AI：帮我回复", new PluginManifest
        {
            Name = "帮我回复",
            Symbol = "arrowshape.turn.up.left",
            Summary = "让 AI 替选中的消息拟一段回复",
            Action = new PluginAction { Type = PluginActionType.Ai, Prompt = "下面是别人发给我的消息，帮我拟一段得体、简洁的回复，用消息原来的语言：\n\n{text}" },
            Output = PluginOutput.Card,
        }),
    ];
}
