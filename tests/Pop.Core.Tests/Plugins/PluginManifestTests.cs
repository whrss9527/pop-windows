using Pop.Core;

namespace Pop.Core.Tests;

public class PluginManifestTests
{
    private static readonly DateTimeOffset September28 = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    /// macOS 版写出的 GitHub 搜索插件，一个字节都不差
    private static readonly string MacGitHubFile = string.Join("\n",
        "{",
        "  \"action\" : {",
        "    \"template\" : \"https://github.com/search?q={text}\",",
        "    \"type\" : \"url\"",
        "  },",
        "  \"format\" : 1,",
        "  \"id\" : \"user-github\",",
        "  \"match\" : {",
        "    \"kinds\" : [",
        "      \"text\"",
        "    ]",
        "  },",
        "  \"modifiedAt\" : \"2026-09-28T08:00:00Z\",",
        "  \"name\" : \"GitHub 搜索\",",
        "  \"output\" : \"none\",",
        "  \"summary\" : \"在 GitHub 上搜索选中的文字\",",
        "  \"symbol\" : \"magnifyingglass\"",
        "}");

    private static PluginManifest GitHub() => new()
    {
        Id = "user-github",
        Name = "GitHub 搜索",
        Symbol = "magnifyingglass",
        Summary = "在 GitHub 上搜索选中的文字",
        Match = new PluginMatch { Kinds = ["text"] },
        Action = new PluginAction { Type = PluginActionType.Url, Template = "https://github.com/search?q={text}" },
        Output = PluginOutput.None,
        ModifiedAt = September28,
    };

    [Fact]
    public void LenientDecoding()
    {
        const string json = "{\"id\": \"user-x\", \"name\": \"测试\", \"match\": {\"kinds\": [\"text\", \"fromTheFuture\", \"url\"], \"pattern\": 5}, " +
                            "\"action\": {\"type\": \"teleport\", \"timeout\": 99999}, \"output\": \"hologram\"}";
        var manifest = PluginManifest.FromJson(json)!;
        Assert.Equal("user-x", manifest.Id);
        Assert.Equal(["text", "url"], manifest.Match.Kinds);
        Assert.Null(manifest.Match.Pattern);
        Assert.Equal(PluginActionType.Url, manifest.Action.Type);
        Assert.Equal(PluginAction.MaxTimeout, manifest.Action.Timeout);
        Assert.Equal(PluginOutput.Card, manifest.Output);
        Assert.Equal(PluginManifest.DefaultSymbol, manifest.Symbol);
    }

    [Fact]
    public void MissingAndWrongFieldsFallBackToDefaults()
    {
        var empty = PluginManifest.FromJson("{}")!;
        Assert.Equal("", empty.Id);
        Assert.Equal("", empty.Name);
        Assert.Equal(PluginManifest.DefaultSymbol, empty.Symbol);
        Assert.Null(empty.Glyph);
        Assert.Equal(["text"], empty.Match.Kinds);
        Assert.Equal(PluginActionType.Url, empty.Action.Type);
        Assert.Equal("", empty.Action.Template);
        Assert.Equal(PluginAction.DefaultTimeout, empty.Action.Timeout);
        Assert.Equal(PluginShell.PowerShell, empty.Action.Shell);
        Assert.Equal(PluginOutput.Card, empty.Output);
        Assert.Equal(PluginManifest.DistantPast, empty.ModifiedAt);
        Assert.Equal(PluginManifest.CurrentFormat, empty.Format);

        // 不是 JSON 对象的整个作废
        Assert.Null(PluginManifest.FromJson("not json"));
        Assert.Null(PluginManifest.FromJson("[]"));
        Assert.Null(PluginManifest.FromJson("5"));
        Assert.Null(PluginManifest.FromJson("{\"id\": \"a\",}"));

        // 类型数组里有一项不是字符串时整个数组作废；空数组表示随时可用；重复的去掉
        Assert.Equal(["text"], Kinds("[\"url\", 5]"));
        Assert.Equal(["text"], Kinds("\"url\""));
        Assert.Equal(["text"], Kinds("null"));
        Assert.Empty(Kinds("[]"));
        Assert.Equal(["url", "text"], Kinds("[\"url\", \"url\", \"text\"]"));

        var wrong = PluginManifest.FromJson("{\"id\": 5, \"name\": [\"x\"], \"symbol\": null, \"match\": \"text\", \"action\": [], " +
                                            "\"output\": \"Replace\", \"modifiedAt\": \"yesterday\", \"format\": \"1\", \"glyph\": 7}")!;
        Assert.Equal("", wrong.Id);
        Assert.Equal("", wrong.Name);
        Assert.Equal(PluginManifest.DefaultSymbol, wrong.Symbol);
        Assert.Equal(new PluginMatch(), wrong.Match);
        Assert.Equal(new PluginAction(), wrong.Action);
        // 取值区分大小写，和 macOS 版一样
        Assert.Equal(PluginOutput.Card, wrong.Output);
        Assert.Equal(PluginManifest.DistantPast, wrong.ModifiedAt);
        Assert.Equal(PluginManifest.CurrentFormat, wrong.Format);
        Assert.Null(wrong.Glyph);

        var numbers = PluginManifest.FromJson("{\"format\": 2.0, \"match\": {\"minLength\": 3, \"maxLength\": \"9\"}, " +
                                              "\"action\": {\"type\": \"shell\", \"timeout\": \"20\"}}")!;
        Assert.Equal(2, numbers.Format);
        Assert.Equal(3, numbers.Match.MinLength);
        Assert.Null(numbers.Match.MaxLength);
        Assert.Equal(PluginActionType.Shell, numbers.Action.Type);
        Assert.Equal(PluginAction.DefaultTimeout, numbers.Action.Timeout);
        Assert.Equal(PluginAction.MinTimeout, PluginManifest.FromJson("{\"action\": {\"timeout\": 0.2}}")!.Action.Timeout);

        // 同名的键以最后一个为准，键区分大小写
        var duplicate = PluginManifest.FromJson("{\"name\": \"旧\", \"name\": \"新\", \"Name\": \"忽略\", \"type\": \"shell\"}")!;
        Assert.Equal("新", duplicate.Name);
        Assert.Equal(PluginActionType.Url, duplicate.Action.Type);
    }

    private static IReadOnlyList<string> Kinds(string json) => PluginManifest.FromJson("{\"match\": {\"kinds\": " + json + "}}")!.Match.Kinds;

    [Fact]
    public void FileRoundTripAndCompactActionEncoding()
    {
        var manifest = new PluginManifest
        {
            Name = "排序",
            Match = new PluginMatch { Kinds = ["text"], Pattern = "\\n" },
            Action = new PluginAction { Type = PluginActionType.Shell, Template = "leftover", Script = "sort -u", Timeout = 20 },
            Output = PluginOutput.Replace,
        }.Normalized() with { ModifiedAt = PluginManifest.Timestamp() };
        var json = manifest.ToJson();
        Assert.Contains("\"script\"", json);
        Assert.Contains("sort -u", json);
        Assert.DoesNotContain("template", json);
        Assert.Equal(manifest, PluginManifest.FromJson(json));
    }

    [Fact]
    public void WritesTheSameBytesAsTheMac()
    {
        Assert.Equal(MacGitHubFile, GitHub().ToJson());

        var script = "echo \"a\tb\"\r\n" + (char)1 + "done";
        var shell = new PluginManifest
        {
            Id = "user-sh",
            Name = "引号 \"和\" 斜杠 / 反斜杠 \\",
            Symbol = "terminal",
            Match = new PluginMatch { Kinds = [], Pattern = "^\\d+/?$", MinLength = 2 },
            Action = new PluginAction { Type = PluginActionType.Shell, Script = script, Timeout = 20.5 },
            Output = PluginOutput.Replace,
            ModifiedAt = new DateTimeOffset(2026, 1, 2, 11, 4, 5, TimeSpan.FromHours(8)),
        };
        var expected = string.Join("\n",
            "{",
            "  \"action\" : {",
            "    \"script\" : \"echo \\\"a\\tb\\\"\\r\\n\\u0001done\",",
            "    \"timeout\" : 20.5,",
            "    \"type\" : \"shell\"",
            "  },",
            "  \"format\" : 1,",
            "  \"id\" : \"user-sh\",",
            "  \"match\" : {",
            "    \"kinds\" : [",
            "",
            "    ],",
            "    \"minLength\" : 2,",
            "    \"pattern\" : \"^\\\\d+/?$\"",
            "  },",
            "  \"modifiedAt\" : \"2026-01-02T03:04:05Z\",",
            "  \"name\" : \"引号 \\\"和\\\" 斜杠 / 反斜杠 \\\\\",",
            "  \"output\" : \"replace\",",
            "  \"summary\" : \"\",",
            "  \"symbol\" : \"terminal\"",
            "}");
        Assert.Equal(expected, shell.ToJson());
        Assert.Equal(shell, PluginManifest.FromJson(expected));

        // 每种动作只写自己用得到的字段
        Assert.Contains("\"prompt\" : \"改写：{text}\"", new PluginManifest { Action = new PluginAction { Type = PluginActionType.Ai, Prompt = "改写：{text}" } }.ToJson());
        var shortcut = new PluginManifest { Action = new PluginAction { Type = PluginActionType.Shortcut, Shortcut = "我的快捷指令", Timeout = 60 } }.ToJson();
        Assert.Contains("\"shortcut\" : \"我的快捷指令\",\n    \"timeout\" : 60,\n    \"type\" : \"shortcut\"", shortcut);
    }

    [Fact]
    public void MacFilesRoundTripUnchanged()
    {
        var manifest = PluginManifest.FromJson(MacGitHubFile)!;
        Assert.Equal(GitHub(), manifest);
        Assert.Equal(MacGitHubFile, manifest.ToJson());
        Assert.Equal(MacGitHubFile, manifest.Normalized().ToJson());
    }

    [Fact]
    public void WindowsFieldsAreOptional()
    {
        // 默认值不写出来，macOS 版的文件保持原样
        var plain = new PluginManifest { Id = "user-a", Action = new PluginAction { Type = PluginActionType.Shell, Script = "Get-Date" } }.ToJson();
        Assert.DoesNotContain("glyph", plain);
        Assert.DoesNotContain("\"shell\" :", plain);

        var windows = new PluginManifest
        {
            Id = "user-b",
            Glyph = "Search",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = "echo %POP_TEXT%", Shell = PluginShell.Cmd },
        };
        var json = windows.ToJson();
        Assert.Contains("\"format\" : 1,\n  \"glyph\" : \"Search\",\n  \"id\" : \"user-b\"", json);
        Assert.Contains("\"script\" : \"echo %POP_TEXT%\",\n    \"shell\" : \"cmd\",\n    \"timeout\" : 15", json);
        Assert.Equal(windows, PluginManifest.FromJson(json));

        // 只有 Shell 脚本才有 shell
        var javaScript = new PluginManifest { Action = new PluginAction { Type = PluginActionType.JavaScript, Script = "input", Shell = PluginShell.Pwsh } };
        Assert.DoesNotContain("\"shell\" :", javaScript.ToJson());

        // shell 手写时大小写随意，不认识的用默认的 Windows PowerShell
        Assert.Equal(PluginShell.Pwsh, Shell("\"PWSH\""));
        Assert.Equal(PluginShell.Cmd, Shell("\" cmd \""));
        Assert.Equal(PluginShell.PowerShell, Shell("\"bash\""));
        Assert.Equal(PluginShell.PowerShell, Shell("3"));
    }

    private static PluginShell Shell(string json) =>
        PluginManifest.FromJson("{\"action\": {\"type\": \"shell\", \"shell\": " + json + "}}")!.Action.Shell;

    [Fact]
    public void NormalizedCleansUp()
    {
        var manifest = new PluginManifest
        {
            Name = "  网页  ",
            Symbol = " ",
            Glyph = "  ",
            Match = new PluginMatch { Kinds = ["text"], Pattern = "  ", MinLength = 0 },
            Action = new PluginAction { Type = PluginActionType.Url, Template = " https://example.com/?q={text} ", Script = "unused", Shell = PluginShell.Cmd },
            Output = PluginOutput.Card,
        }.Normalized();
        Assert.Equal("网页", manifest.Name);
        Assert.Equal(PluginManifest.DefaultSymbol, manifest.Symbol);
        Assert.Null(manifest.Glyph);
        Assert.Null(manifest.Match.Pattern);
        Assert.Null(manifest.Match.MinLength);
        Assert.Equal("https://example.com/?q={text}", manifest.Action.Template);
        Assert.Equal("", manifest.Action.Script);
        Assert.Equal(PluginShell.PowerShell, manifest.Action.Shell);
        Assert.Equal(PluginOutput.None, manifest.Output);

        var shell = new PluginManifest
        {
            Name = "x",
            Glyph = " Search ",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = " dir ", Timeout = 1000, Shell = PluginShell.Cmd, Prompt = "unused" },
        }.Normalized();
        Assert.Equal("Search", shell.Glyph);
        Assert.Equal(" dir ", shell.Action.Script);
        Assert.Equal(PluginAction.MaxTimeout, shell.Action.Timeout);
        Assert.Equal(PluginShell.Cmd, shell.Action.Shell);
        Assert.Equal("", shell.Action.Prompt);

        var ai = new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Ai, Prompt = " 总结 ", Timeout = 99 } }.Normalized();
        Assert.Equal("总结", ai.Action.Prompt);
        Assert.Equal(PluginAction.DefaultTimeout, ai.Action.Timeout);
    }

    [Fact]
    public void Validation()
    {
        Assert.Equal("请填写名称", new PluginManifest { Name = " " }.ValidationError());
        Assert.NotNull(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Url, Template = "example.com/{text}" } }.ValidationError());
        Assert.NotNull(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Url, Template = "/search?q={text}" } }.ValidationError());
        Assert.Equal("正则表达式有误", new PluginManifest { Name = "x", Match = new PluginMatch { Pattern = "(" }, Action = new PluginAction { Type = PluginActionType.Url, Template = "https://a.com" } }.ValidationError());
        Assert.NotNull(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Shell, Script = "  " } }.ValidationError());
        Assert.NotNull(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Shortcut, Shortcut = " " } }.ValidationError());
        Assert.NotNull(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Ai } }.ValidationError());
        Assert.Null(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.JavaScript, Script = "input" } }.ValidationError());
        Assert.Null(new PluginManifest { Name = "x", Action = new PluginAction { Type = PluginActionType.Url, Template = "mailto:{raw}" } }.ValidationError());
        foreach (var template in PluginTemplates.All)
        {
            Assert.True(template.Manifest.ValidationError() is null, template.Title);
        }
    }

    [Fact]
    public void Ids()
    {
        Assert.True(PluginManifest.IsValidId("user-abc_1.2"));
        Assert.True(PluginManifest.IsValidId(PluginManifest.MakeId()));
        Assert.StartsWith("user-", PluginManifest.MakeId());
        Assert.Equal(15, PluginManifest.MakeId().Length);
        Assert.False(PluginManifest.IsValidId(""));
        Assert.False(PluginManifest.IsValidId(null));
        Assert.False(PluginManifest.IsValidId("../escape"));
        Assert.False(PluginManifest.IsValidId(".hidden"));
        Assert.False(PluginManifest.IsValidId("中文"));
        Assert.False(PluginManifest.IsValidId("a b"));
        Assert.True(PluginManifest.IsValidId(new string('a', 64)));
        Assert.False(PluginManifest.IsValidId(new string('a', 65)));
        Assert.NotEqual(PluginManifest.MakeId(), PluginManifest.MakeId());
        Assert.NotEqual(new PluginManifest().Id, new PluginManifest().Id);

        // Windows 的设备名不能当文件名
        foreach (var device in new[] { "con", "NUL", "aux.plugin", "COM1", "lpt9", "prn" })
        {
            Assert.False(PluginManifest.IsValidId(device), device);
        }
        foreach (var fine in new[] { "console", "com10", "com", "nul-plugin", "my.con" })
        {
            Assert.True(PluginManifest.IsValidId(fine), fine);
        }

        Assert.False(PluginManifest.IsUsableId("copy", PluginStore.IsBuiltinId));
        Assert.False(PluginManifest.IsUsableId("all", PluginStore.IsBuiltinId));
        Assert.True(PluginManifest.IsUsableId("user-copy", PluginStore.IsBuiltinId));
        Assert.False(PluginManifest.IsUsableId("mine", id => id == "mine"));
    }

    [Fact]
    public void KindNamesMapToContentKinds()
    {
        Assert.Equal(16, PluginKinds.All.Count);
        Assert.Equal(ContentKind.Text, PluginKinds.Flag("text"));
        Assert.Equal(ContentKind.ForeignText, PluginKinds.Flag("foreignText"));
        Assert.Equal(ContentKind.DateTime, PluginKinds.Flag("dateTime"));
        Assert.Equal(ContentKind.Files, PluginKinds.Flag("files"));
        Assert.Equal(ContentKind.None, PluginKinds.Flag("image"));
        Assert.Equal(ContentKind.None, PluginKinds.Flag("imageFile"));
        Assert.Equal(ContentKind.None, PluginKinds.Flag("Text"));
        Assert.False(PluginKinds.IsSupported("imageFile"));
        Assert.True(PluginKinds.IsKnown("imageFile"));
        Assert.Equal(["text", "url"], PluginKinds.Names(ContentKind.Url | ContentKind.Text));
        Assert.Equal("带单位的数值", PluginKinds.Title("measurement"));

        // ContentKind 的每一种都有对应的名字，加了新的类型时这里会提醒
        var all = Enum.GetValues<ContentKind>().Where(kind => kind != ContentKind.None).ToList();
        foreach (var kind in all)
        {
            var name = Assert.Single(PluginKinds.Names(kind));
            Assert.Equal(kind, PluginKinds.Flag(name));
        }
        Assert.Equal(all.Aggregate(ContentKind.None, (a, b) => a | b), PluginKinds.Flags(PluginKinds.All));
    }

    [Fact]
    public void TimestampsKeepWholeSeconds()
    {
        var now = new DateTimeOffset(2026, 9, 30, 10, 20, 30, 999, TimeSpan.FromHours(8));
        var stamp = PluginManifest.Timestamp(now);
        Assert.Equal(TimeSpan.Zero, stamp.Offset);
        Assert.Equal(0, stamp.Millisecond);
        Assert.Equal(now.ToUnixTimeSeconds(), stamp.ToUnixTimeSeconds());

        var saved = new PluginManifest { Id = "user-t", ModifiedAt = stamp };
        Assert.Contains("\"modifiedAt\" : \"2026-09-30T02:20:30Z\"", saved.ToJson());
        Assert.Equal(saved, PluginManifest.FromJson(saved.ToJson()));
        Assert.Contains("\"modifiedAt\" : \"0001-01-01T00:00:00Z\"", new PluginManifest().ToJson());
    }

    [Fact]
    public void DisplayNameAndSummary()
    {
        Assert.Equal("未命名插件", new PluginManifest { Name = " " }.DisplayName);
        Assert.Equal("JavaScript", new PluginManifest { Action = new PluginAction { Type = PluginActionType.JavaScript } }.DisplaySummary);
        Assert.Equal("说明", new PluginManifest { Summary = "说明" }.DisplaySummary);
        Assert.Equal("Shell 脚本", PluginNames.Title(PluginActionType.Shell));
        Assert.Equal("显示结果卡片", PluginNames.Title(PluginOutput.Card));
        Assert.Equal("PowerShell 7", PluginNames.Title(PluginShell.Pwsh));
    }
}

public class PluginGlyphsTests
{
    [Fact]
    public void ResolvesIconNamesAndMacSymbols()
    {
        // glyph 是 Fluent System Icons 的图标名，不写尺寸时用 24
        Assert.Equal("Search24", PluginGlyphs.Resolve("Search24", null));
        Assert.Equal("Search24", PluginGlyphs.Resolve(" Search ", "globe"));
        Assert.Equal("WindowConsole20", PluginGlyphs.Resolve("WindowConsole20", "globe"));
        // 不是图标名的（字符、带空格的）不用，按 symbol 找
        Assert.Equal("Globe24", PluginGlyphs.Resolve("★", "globe"));
        Assert.Equal("Globe24", PluginGlyphs.Resolve("Search Icon", "globe"));
        Assert.Equal("Globe24", PluginGlyphs.Resolve("Music_Note", "globe"));

        // 没写 glyph 时按 macOS 的 SF Symbol 找，去掉 .fill 这类后缀
        Assert.Equal("Search24", PluginGlyphs.Resolve(null, "magnifyingglass"));
        Assert.Equal("Star24", PluginGlyphs.Resolve("", "star.fill"));
        Assert.Equal("Warning24", PluginGlyphs.Resolve(null, "exclamationmark.triangle.fill"));
        Assert.Equal("Info24", PluginGlyphs.Resolve(null, "info.circle"));
        Assert.Equal(PluginGlyphs.Default, PluginGlyphs.Resolve("  ", "no.such.symbol"));
        Assert.Equal(PluginGlyphs.Default, PluginGlyphs.Resolve(null, null));
        Assert.Equal("PuzzlePiece24", PluginGlyphs.Resolve(null, PluginManifest.DefaultSymbol));
        Assert.Equal("Globe24", PluginGlyphs.Resolve(new PluginManifest { Symbol = "globe" }));
        Assert.Equal("Code24", PluginGlyphs.Resolve(new PluginManifest { Symbol = "globe", Glyph = "Code" }));

        // 都是图标名的写法：字母开头、尺寸结尾；示例插件都有自己的图标
        Assert.All(PluginGlyphs.Symbols.Values, name => Assert.Matches("^[A-Z][A-Za-z0-9]*(16|20|24|28|32|48)$", name));
        foreach (var template in PluginTemplates.All)
        {
            Assert.True(PluginGlyphs.Resolve(template.Manifest) != PluginGlyphs.Default, template.Title);
        }
    }
}
