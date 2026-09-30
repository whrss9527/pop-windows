using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pop.Core;

/// 插件的动作类型
public enum PluginActionType
{
    /// 打开网址模板
    Url,
    /// 运行脚本（Windows 上默认用 Windows PowerShell，见 PluginShell）
    Shell,
    /// 运行 JavaScript
    JavaScript,
    /// 运行快捷指令（只有 macOS 版能用）
    Shortcut,
    /// 把选中的文字和指令发给 AI
    Ai,
}

/// 脚本输出的去向。打开网址的插件没有输出
public enum PluginOutput
{
    /// 显示在结果卡片里
    Card,
    /// 复制到剪贴板
    Copy,
    /// 替换选中的文字
    Replace,
    /// 轻提示
    Toast,
    /// 什么都不显示
    None,
}

/// Shell 脚本用哪个程序运行（Windows 版才有的设置，macOS 版忽略它，一律用 zsh）
public enum PluginShell
{
    /// Windows 自带的 Windows PowerShell 5.1（powershell.exe）
    PowerShell,
    /// 另外安装的 PowerShell 7（pwsh.exe）
    Pwsh,
    /// 命令提示符（cmd.exe）
    Cmd,
}

/// 插件文件里的写法和设置界面上的名称。插件文件的写法和 macOS 版一致，区分大小写
public static class PluginNames
{
    public static string Name(PluginActionType type) => type switch
    {
        PluginActionType.Shell => "shell",
        PluginActionType.JavaScript => "javascript",
        PluginActionType.Shortcut => "shortcut",
        PluginActionType.Ai => "ai",
        _ => "url",
    };

    public static string Title(PluginActionType type) => type switch
    {
        PluginActionType.Shell => "Shell 脚本",
        PluginActionType.JavaScript => "JavaScript",
        PluginActionType.Shortcut => "快捷指令",
        PluginActionType.Ai => "AI 指令",
        _ => "打开网址",
    };

    public static PluginActionType? ParseActionType(string? name) => name switch
    {
        "url" => PluginActionType.Url,
        "shell" => PluginActionType.Shell,
        "javascript" => PluginActionType.JavaScript,
        "shortcut" => PluginActionType.Shortcut,
        "ai" => PluginActionType.Ai,
        _ => null,
    };

    public static string Name(PluginOutput output) => output switch
    {
        PluginOutput.Copy => "copy",
        PluginOutput.Replace => "replace",
        PluginOutput.Toast => "toast",
        PluginOutput.None => "none",
        _ => "card",
    };

    public static string Title(PluginOutput output) => output switch
    {
        PluginOutput.Copy => "复制到剪贴板",
        PluginOutput.Replace => "替换选中的文字",
        PluginOutput.Toast => "轻提示",
        PluginOutput.None => "不显示",
        _ => "显示结果卡片",
    };

    public static PluginOutput? ParseOutput(string? name) => name switch
    {
        "card" => PluginOutput.Card,
        "copy" => PluginOutput.Copy,
        "replace" => PluginOutput.Replace,
        "toast" => PluginOutput.Toast,
        "none" => PluginOutput.None,
        _ => null,
    };

    public static string Name(PluginShell shell) => shell switch
    {
        PluginShell.Pwsh => "pwsh",
        PluginShell.Cmd => "cmd",
        _ => "powershell",
    };

    public static string Title(PluginShell shell) => shell switch
    {
        PluginShell.Pwsh => "PowerShell 7",
        PluginShell.Cmd => "命令提示符",
        _ => "Windows PowerShell",
    };

    /// 这是 Windows 版自己加的字段，手写时大小写随意
    public static PluginShell? ParseShell(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "powershell" => PluginShell.PowerShell,
        "pwsh" => PluginShell.Pwsh,
        "cmd" => PluginShell.Cmd,
        _ => null,
    };
}

/// 插件能处理的内容类型。插件文件里写的是 macOS 版的类型名，这里换成 Windows 版的 ContentKind
public static class PluginKinds
{
    /// macOS 版认识的全部类型，按它设置界面里的顺序
    public static readonly IReadOnlyList<string> All =
    [
        "text", "chineseText", "foreignText", "url", "email", "math", "timestamp", "json",
        "files", "image", "word", "color", "number", "dateTime", "imageFile", "measurement",
    ];

    /// 对应的 ContentKind。image（图片）和 imageFile（图片文件）Windows 版还识别不了，返回 None
    public static ContentKind Flag(string name) => name switch
    {
        "text" => ContentKind.Text,
        "chineseText" => ContentKind.ChineseText,
        "foreignText" => ContentKind.ForeignText,
        "url" => ContentKind.Url,
        "email" => ContentKind.Email,
        "math" => ContentKind.Math,
        "timestamp" => ContentKind.Timestamp,
        "json" => ContentKind.Json,
        "files" => ContentKind.Files,
        "word" => ContentKind.Word,
        "color" => ContentKind.Color,
        "number" => ContentKind.Number,
        "dateTime" => ContentKind.DateTime,
        "measurement" => ContentKind.Measurement,
        _ => ContentKind.None,
    };

    public static bool IsKnown(string name) => All.Contains(name);

    /// Windows 版能识别这种内容
    public static bool IsSupported(string name) => Flag(name) != ContentKind.None;

    public static ContentKind Flags(IEnumerable<string> names) =>
        names.Aggregate(ContentKind.None, (all, name) => all | Flag(name));

    /// 内容具备的类型，按名称排好（传给脚本的 POP_KINDS）
    public static IReadOnlyList<string> Names(ContentKind kinds) =>
        All.Where(name => Flag(name) is var flag && flag != ContentKind.None && (kinds & flag) == flag)
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string Title(string name) => name switch
    {
        "text" => "文本",
        "chineseText" => "中文",
        "foreignText" => "外文",
        "url" => "链接",
        "email" => "邮箱",
        "math" => "算式",
        "timestamp" => "时间戳",
        "json" => "JSON",
        "files" => "文件",
        "image" => "图片",
        "word" => "单个词",
        "color" => "颜色",
        "number" => "数字",
        "dateTime" => "日期时间",
        "imageFile" => "图片文件",
        "measurement" => "带单位的数值",
        _ => name,
    };
}

/// 什么样的内容能用这个插件
public sealed record PluginMatch
{
    /// 能处理的内容类型（macOS 版的类型名），满足其中之一就行；为空表示随时可用（不需要选中内容）
    public IReadOnlyList<string> Kinds { get; init; } = ["text"];

    /// 选中的文字（或文件路径）还要匹配这个正则
    public string? Pattern { get; init; }

    /// 最少几个字（按用户看到的字符数）
    public int? MinLength { get; init; }

    /// 最多几个字
    public int? MaxLength { get; init; }

    public bool Equals(PluginMatch? other) =>
        other is not null && Kinds.SequenceEqual(other.Kinds) && Pattern == other.Pattern &&
        MinLength == other.MinLength && MaxLength == other.MaxLength;

    public override int GetHashCode() => HashCode.Combine(Kinds.Count, Pattern, MinLength, MaxLength);
}

/// 插件要做的事
public sealed record PluginAction
{
    public const double DefaultTimeout = 15;
    public const double MinTimeout = 1;
    public const double MaxTimeout = 300;

    public PluginActionType Type { get; init; } = PluginActionType.Url;

    /// url：网址模板，{text} 换成编码后的选中文字，{raw} 换成原文
    public string Template { get; init; } = "";

    /// shell / javascript：脚本内容
    public string Script { get; init; } = "";

    /// shortcut：快捷指令名称
    public string Shortcut { get; init; } = "";

    /// ai：给 AI 的指令，{text} 换成选中的文字
    public string Prompt { get; init; } = "";

    /// 最长运行时间（秒）
    public double Timeout { get; init; } = DefaultTimeout;

    /// shell：用哪个程序运行脚本（Windows 版才有）
    public PluginShell Shell { get; init; } = PluginShell.PowerShell;

    /// 限制在 1–300 秒；不是有限的数时用默认值
    public static double ClampTimeout(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinTimeout, MaxTimeout) : DefaultTimeout;
}

/// 用户插件的描述文件。每个插件是插件文件夹里的一个 JSON 文件，格式和 macOS 版相同，两边可以互相拷贝：
///
///     {
///       "action" : { "template" : "https://github.com/search?q={text}", "type" : "url" },
///       "id" : "user-3f9a1c2b",
///       "match" : { "kinds" : [ "text" ] },
///       "name" : "GitHub 搜索",
///       "output" : "none",
///       ...
///     }
///
/// 所有字段都宽松读取：缺字段、类型不对、不认识的值都回退到默认值，新旧版本之间交换插件不会整体失败。
/// Windows 版多了两个可选字段：glyph（图标名）和 action.shell（用哪个程序运行 Shell 脚本），macOS 版会忽略它们
public sealed record PluginManifest
{
    public const int CurrentFormat = 1;
    public const string DefaultSymbol = "puzzlepiece.extension";

    /// 没有修改时间（从没保存过）
    public static readonly DateTimeOffset DistantPast = DateTimeOffset.MinValue;

    public int Format { get; init; } = CurrentFormat;

    /// 插件 ID，也是文件名。新建的插件自动生成一个
    public string Id { get; init; } = MakeId();

    public string Name { get; init; } = "";

    /// macOS 版用的 SF Symbol 图标名
    public string Symbol { get; init; } = DefaultSymbol;

    /// Windows 版的图标：Fluent System Icons 的图标名（Search24、Globe24……，不写尺寸时用 24），见 PluginGlyphs；
    /// 为空时按 Symbol 找一个相近的图标
    public string? Glyph { get; init; }

    public string Summary { get; init; } = "";

    public PluginMatch Match { get; init; } = new();

    public PluginAction Action { get; init; } = new();

    public PluginOutput Output { get; init; } = PluginOutput.Card;

    /// 最后修改时间，精确到秒
    public DateTimeOffset ModifiedAt { get; init; } = DistantPast;

    /// 圆盘和列表里显示的名称
    public string DisplayName => Name.Trim().Length == 0 ? "未命名插件" : Name;

    /// 列表里显示的说明：没写说明时显示动作类型
    public string DisplaySummary => Summary.Length == 0 ? PluginNames.Title(Action.Type) : Summary;

    // MARK: ID

    public static string MakeId() => "user-" + Guid.NewGuid().ToString("N")[..10];

    /// ID 同时是文件名：1–64 个字母、数字、点、横线或下划线，不能以点开头，也不能是 Windows 的设备名（CON、NUL、COM1……）
    public static bool IsValidId(string? id)
    {
        if (id is null || id.Length is < 1 or > 64 || id[0] == '.') return false;
        foreach (var c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) return false;
        }
        return !IsDeviceName(id.Split('.')[0]);
    }

    /// ID 合法，并且不和内置功能重名
    public static bool IsUsableId(string? id, Func<string, bool> isReserved) => IsValidId(id) && !isReserved(id!);

    private static bool IsDeviceName(string stem) =>
        stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" ||
        (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsAsciiDigit(stem[3]));

    /// 当前时间，精确到秒（文件里只存到秒，这样写进去再读出来仍然相等）
    public static DateTimeOffset Timestamp(DateTimeOffset? now = null) =>
        DateTimeOffset.FromUnixTimeSeconds((now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());

    // MARK: 校验与整理

    /// 保存前整理：去掉首尾空白、清掉当前类型用不到的字段、空字符串变成 null
    public PluginManifest Normalized()
    {
        var pattern = (Match.Pattern ?? "").Trim();
        var symbol = Symbol.Trim();
        var timeout = PluginAction.ClampTimeout(Action.Timeout);
        var action = Action.Type switch
        {
            PluginActionType.Shell => new PluginAction { Type = PluginActionType.Shell, Script = Action.Script, Timeout = timeout, Shell = Action.Shell },
            PluginActionType.JavaScript => new PluginAction { Type = PluginActionType.JavaScript, Script = Action.Script, Timeout = timeout },
            PluginActionType.Shortcut => new PluginAction { Type = PluginActionType.Shortcut, Shortcut = Action.Shortcut.Trim(), Timeout = timeout },
            PluginActionType.Ai => new PluginAction { Type = PluginActionType.Ai, Prompt = Action.Prompt.Trim() },
            _ => new PluginAction { Type = PluginActionType.Url, Template = Action.Template.Trim() },
        };
        return this with
        {
            Format = CurrentFormat,
            Name = Name.Trim(),
            Symbol = symbol.Length == 0 ? DefaultSymbol : symbol,
            Glyph = string.IsNullOrWhiteSpace(Glyph) ? null : Glyph.Trim(),
            Summary = Summary.Trim(),
            Match = Match with
            {
                Pattern = pattern.Length == 0 ? null : pattern,
                MinLength = Match.MinLength > 0 ? Match.MinLength : null,
                MaxLength = Match.MaxLength > 0 ? Match.MaxLength : null,
            },
            Action = action,
            // 打开网址没有输出
            Output = action.Type == PluginActionType.Url ? PluginOutput.None : Output,
        };
    }

    /// 需要用户修正的问题；null 表示可以保存
    public string? ValidationError()
    {
        if (Name.Trim().Length == 0) return "请填写名称";
        if (!string.IsNullOrEmpty(Match.Pattern) && !PluginMatcher.IsValidPattern(Match.Pattern)) return "正则表达式有误";
        switch (Action.Type)
        {
            case PluginActionType.Url:
                var template = Action.Template.Trim();
                if (template.Length == 0) return "请填写网址";
                var sample = template.Replace("{text}", "test", StringComparison.Ordinal).Replace("{raw}", "test", StringComparison.Ordinal);
                if (PluginTemplates.ParseUrl(sample) is null) return "网址需要以 https:// 之类的协议开头";
                break;
            case PluginActionType.Shell or PluginActionType.JavaScript:
                if (Action.Script.Trim().Length == 0) return "请填写脚本";
                break;
            case PluginActionType.Shortcut:
                if (Action.Shortcut.Trim().Length == 0) return "请填写快捷指令名称";
                break;
            case PluginActionType.Ai:
                if (Action.Prompt.Trim().Length == 0) return "请填写给 AI 的指令";
                break;
        }
        return null;
    }

    // MARK: 读取

    /// 读插件文件。不是 JSON 对象时返回 null；字段缺了、写错了都用默认值
    public static PluginManifest? FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Read(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PluginManifest Read(JsonElement root)
    {
        var fields = Fields(root);
        return new PluginManifest
        {
            Format = Integer(fields, "format") ?? CurrentFormat,
            Id = Text(fields, "id") ?? "",
            Name = Text(fields, "name") ?? "",
            Symbol = Text(fields, "symbol") ?? DefaultSymbol,
            Glyph = Text(fields, "glyph"),
            Summary = Text(fields, "summary") ?? "",
            Match = fields.TryGetValue("match", out var match) && match.ValueKind == JsonValueKind.Object ? ReadMatch(match) : new PluginMatch(),
            Action = fields.TryGetValue("action", out var action) && action.ValueKind == JsonValueKind.Object ? ReadAction(action) : new PluginAction(),
            Output = PluginNames.ParseOutput(Text(fields, "output")) ?? PluginOutput.Card,
            ModifiedAt = Date(Text(fields, "modifiedAt")) ?? DistantPast,
        };
    }

    private static PluginMatch ReadMatch(JsonElement element)
    {
        var fields = Fields(element);
        return new PluginMatch
        {
            Kinds = ReadKinds(fields),
            Pattern = Text(fields, "pattern"),
            MinLength = Integer(fields, "minLength"),
            MaxLength = Integer(fields, "maxLength"),
        };
    }

    /// 缺了或者不是字符串数组时是 ["text"]（和 macOS 版一样整个数组作废）；不认识的类型（更新的版本加的）跳过
    private static IReadOnlyList<string> ReadKinds(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("kinds", out var array) || array.ValueKind != JsonValueKind.Array) return ["text"];
        if (array.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)) return ["text"];
        var kinds = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            var name = item.GetString()!;
            if (PluginKinds.IsKnown(name) && !kinds.Contains(name)) kinds.Add(name);
        }
        return kinds;
    }

    private static PluginAction ReadAction(JsonElement element)
    {
        var fields = Fields(element);
        return new PluginAction
        {
            Type = PluginNames.ParseActionType(Text(fields, "type")) ?? PluginActionType.Url,
            Template = Text(fields, "template") ?? "",
            Script = Text(fields, "script") ?? "",
            Shortcut = Text(fields, "shortcut") ?? "",
            Prompt = Text(fields, "prompt") ?? "",
            Timeout = PluginAction.ClampTimeout(Number(fields, "timeout") ?? PluginAction.DefaultTimeout),
            Shell = PluginNames.ParseShell(Text(fields, "shell")) ?? PluginShell.PowerShell,
        };
    }

    /// 同名的键以最后一个为准
    private static Dictionary<string, JsonElement> Fields(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) fields[property.Name] = property.Value;
        return fields;
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(Dictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    /// 整数；3.0 这样正好是整数的小数也算
    private static int? Integer(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        if (value.TryGetInt32(out var integer)) return integer;
        return value.TryGetDouble(out var number) && number == Math.Floor(number) && number is >= int.MinValue and <= int.MaxValue ? (int)number : null;
    }

    private static DateTimeOffset? Date(string? text)
    {
        if (text is null || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;
        return DateTimeOffset.FromUnixTimeSeconds(date.ToUnixTimeSeconds());
    }

    // MARK: 写出

    /// 写成插件文件：键按字母排序、两个空格缩进、"键" : 值，和 macOS 版写出的文件逐字节相同。
    /// action 里只写当前类型用得到的字段；Windows 版的 glyph、shell 只在不是默认值时才写
    public string ToJson()
    {
        var root = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["action"] = ActionFields(),
            ["format"] = Format,
            ["id"] = Id,
            ["match"] = MatchFields(),
            ["modifiedAt"] = ModifiedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["name"] = Name,
            ["output"] = PluginNames.Name(Output),
            ["summary"] = Summary,
            ["symbol"] = Symbol,
        };
        if (!string.IsNullOrEmpty(Glyph)) root["glyph"] = Glyph;
        var output = new StringBuilder();
        AppleJson.Write(output, root, 0);
        return output.ToString();
    }

    private SortedDictionary<string, object> ActionFields()
    {
        var fields = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["type"] = PluginNames.Name(Action.Type) };
        switch (Action.Type)
        {
            case PluginActionType.Url:
                fields["template"] = Action.Template;
                break;
            case PluginActionType.Shell or PluginActionType.JavaScript:
                fields["script"] = Action.Script;
                fields["timeout"] = Action.Timeout;
                if (Action.Type == PluginActionType.Shell && Action.Shell != PluginShell.PowerShell) fields["shell"] = PluginNames.Name(Action.Shell);
                break;
            case PluginActionType.Shortcut:
                fields["shortcut"] = Action.Shortcut;
                fields["timeout"] = Action.Timeout;
                break;
            case PluginActionType.Ai:
                fields["prompt"] = Action.Prompt;
                break;
        }
        return fields;
    }

    private SortedDictionary<string, object> MatchFields()
    {
        var fields = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["kinds"] = Match.Kinds };
        if (Match.Pattern is not null) fields["pattern"] = Match.Pattern;
        if (Match.MinLength is { } min) fields["minLength"] = min;
        if (Match.MaxLength is { } max) fields["maxLength"] = max;
        return fields;
    }
}

/// macOS 版 JSONEncoder（prettyPrinted、sortedKeys、withoutEscapingSlashes）的输出格式：
/// 两个空格缩进，键和值之间是 " : "，空数组写成两个换行，斜杠和非 ASCII 字符不转义，控制字符写成小写的 \u00xx
internal static class AppleJson
{
    public static void Write(StringBuilder output, object value, int depth)
    {
        switch (value)
        {
            case string text:
                WriteString(output, text);
                break;
            case int integer:
                output.Append(integer.ToString(CultureInfo.InvariantCulture));
                break;
            case double number:
                // 和 Swift 的 Double.description 一样用最短的写法，整数不带 .0
                var finite = double.IsFinite(number) ? number : PluginAction.DefaultTimeout;
                output.Append(finite.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e'));
                break;
            case IReadOnlyList<string> items:
                output.Append("[\n");
                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0) output.Append(",\n");
                    Indent(output, depth + 1);
                    WriteString(output, items[i]);
                }
                output.Append('\n');
                Indent(output, depth);
                output.Append(']');
                break;
            case SortedDictionary<string, object> fields:
                output.Append("{\n");
                var first = true;
                foreach (var (key, item) in fields)
                {
                    if (!first) output.Append(",\n");
                    first = false;
                    Indent(output, depth + 1);
                    WriteString(output, key);
                    output.Append(" : ");
                    Write(output, item, depth + 1);
                }
                output.Append('\n');
                Indent(output, depth);
                output.Append('}');
                break;
            default:
                throw new ArgumentException("不支持的值：" + value.GetType().Name, nameof(value));
        }
    }

    private static void Indent(StringBuilder output, int depth) => output.Append(' ', depth * 2);

    private static void WriteString(StringBuilder output, string text)
    {
        output.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                case < ' ': output.Append("\\u00").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)); break;
                default: output.Append(c); break;
            }
        }
        output.Append('"');
    }
}
