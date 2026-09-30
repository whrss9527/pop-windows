using Pop.Core;

namespace Pop.Core.Tests;

public class PluginTemplatesTests
{
    [Fact]
    public void UrlTemplate()
    {
        Assert.Equal("https://example.com/s?q=C%2B%2B%20%E4%BD%A0%E5%A5%BD", PluginTemplates.ExpandUrl("https://example.com/s?q={text}", "C++ 你好"));
        Assert.Null(PluginTemplates.ExpandUrl("not a url {text}", "C++ 你好"));
    }

    [Fact]
    public void RawPlaceholderAndEncoding()
    {
        // 只有字母、数字和 -._~ 不编码
        Assert.Equal("https://a.com/?q=a-b._~%2F%26%23%3F%3D", PluginTemplates.ExpandUrl("https://a.com/?q={text}", "a-b._~/&#?="));
        Assert.Equal("https://a.com/path/x/y", PluginTemplates.ExpandUrl("https://a.com/{raw}", "path/x/y"));
        Assert.Equal("https://a.com/?q=a%20b", PluginTemplates.ExpandUrl("  https://a.com/?q={raw}  ", "a b"));
        Assert.Equal("mailto:someone@example.com", PluginTemplates.ExpandUrl("mailto:{raw}", "someone@example.com"));
        Assert.Equal("https://a.com/?a=x&b=x", PluginTemplates.ExpandUrl("https://a.com/?a={text}&b={raw}", "x"));
        Assert.Null(PluginTemplates.ExpandUrl("{raw}", "example.com"));
    }

    [Fact]
    public void UrlsNeedAScheme()
    {
        Assert.NotNull(PluginTemplates.ParseUrl("https://example.com"));
        Assert.NotNull(PluginTemplates.ParseUrl("bingmaps:?q=x"));
        Assert.Null(PluginTemplates.ParseUrl("/search?q=x"));
        Assert.Null(PluginTemplates.ParseUrl("example.com/search"));
        Assert.Null(PluginTemplates.ParseUrl(""));
    }

    [Fact]
    public void PromptExpansion()
    {
        Assert.Equal("翻译：hello", PluginTemplates.ExpandPrompt("  翻译：{text} ", "hello"));
        Assert.Equal("原文 hello", PluginTemplates.ExpandPrompt("原文 {raw}", "hello"));
        Assert.Equal("总结一下\n\nhello", PluginTemplates.ExpandPrompt("总结一下", "hello"));
        Assert.Equal("hello", PluginTemplates.ExpandPrompt("  ", "hello"));
    }

    [Fact]
    public void NewPluginTemplates()
    {
        var templates = PluginTemplates.All;
        Assert.Equal(templates.Count, templates.Select(t => t.Id).Distinct().Count());
        Assert.All(templates, template => Assert.Null(template.Manifest.ValidationError()));
        // 每次取都是新的插件 ID
        Assert.NotEqual(PluginTemplates.All[0].Manifest.Id, PluginTemplates.All[0].Manifest.Id);
        // 快捷指令只有 macOS 能用
        Assert.DoesNotContain(templates, t => t.Manifest.Action.Type == PluginActionType.Shortcut);
        // Shell 示例是 PowerShell 写的
        var shells = templates.Where(t => t.Manifest.Action.Type == PluginActionType.Shell).ToList();
        Assert.Equal(2, shells.Count);
        Assert.All(shells, t => Assert.Equal(PluginShell.PowerShell, t.Manifest.Action.Shell));
        Assert.Contains("Sort-Object", shells[0].Manifest.Action.Script);
        Assert.Contains("$env:POP_TEXT", shells[1].Manifest.Action.Script);
        Assert.Equal(["json"], templates.Single(t => t.Id == "js-json-keys").Manifest.Match.Kinds);
        Assert.StartsWith("https://www.bing.com/maps", templates.Single(t => t.Id == "maps").Manifest.Action.Template);
    }
}

public class PluginMatcherTests
{
    private static ClassifiedContent Text(string text) => ContentClassifier.Classify(text);

    private static PluginManifest With(PluginMatch match) => new() { Name = "x", Match = match };

    [Fact]
    public void ManifestPluginInfo()
    {
        var manifest = new PluginManifest
        {
            Id = "user-digits",
            Name = "数字",
            Match = new PluginMatch { Kinds = ["text"], Pattern = "^[0-9]+$" },
            Action = new PluginAction { Type = PluginActionType.JavaScript, Script = "input" },
        };
        Assert.Equal("JavaScript", manifest.DisplaySummary);
        Assert.True(PluginMatcher.Matches(manifest, Text("2024")));
        Assert.False(PluginMatcher.Matches(manifest, Text("abc")));

        // 没有勾选任何类型：随时可用
        var anytime = With(new PluginMatch { Kinds = [] });
        Assert.True(PluginMatcher.Matches(anytime, ClassifiedContent.Empty));
        Assert.Equal(ContentKind.None, PluginMatcher.RequiredKinds(anytime.Match));
        Assert.True(PluginRunner.ToAction(anytime).IsAvailable(ClassifiedContent.Empty));
    }

    [Fact]
    public void PatternAndLengthConstraints()
    {
        var match = new PluginMatch { Kinds = ["text"], Pattern = "^[0-9]+$", MaxLength = 5 };
        Assert.True(PluginMatcher.Matches(match, Text("123")));
        Assert.False(PluginMatcher.Matches(match, Text("abc")));
        Assert.False(PluginMatcher.Matches(match, Text("1234567")));
        Assert.False(PluginMatcher.Matches(match, ClassifiedContent.Empty));

        // 长度按用户看到的字符数算
        var short3 = new PluginMatch { Kinds = ["text"], MinLength = 3, MaxLength = 3 };
        Assert.True(PluginMatcher.Matches(short3, Text("你好🎉")));
        Assert.False(PluginMatcher.Matches(short3, Text("你好")));
    }

    [Fact]
    public void KindsAreAnyOf()
    {
        var links = new PluginMatch { Kinds = ["url", "email"] };
        Assert.True(PluginMatcher.Matches(links, Text("https://example.com")));
        Assert.True(PluginMatcher.Matches(links, Text("someone@example.com")));
        Assert.False(PluginMatcher.Matches(links, Text("hello world")));
        Assert.Equal(ContentKind.Url | ContentKind.Email, PluginMatcher.RequiredKinds(links));

        var foreign = new PluginMatch { Kinds = ["foreignText"] };
        Assert.True(PluginMatcher.Matches(foreign, Text("Good morning")));
        Assert.False(PluginMatcher.Matches(foreign, Text("早上好")));
    }

    [Fact]
    public void UnsupportedKindsNeverMatch()
    {
        var images = new PluginMatch { Kinds = ["image", "imageFile"] };
        Assert.False(PluginMatcher.Matches(images, Text("hello")));
        Assert.False(PluginMatcher.Matches(images, ClassifiedContent.Empty));
        Assert.False(PluginRunner.ToAction(With(images)).IsAvailable(Text("hello")));
        Assert.False(PluginRunner.ToAction(With(images)).IsAvailable(ClassifiedContent.Empty));

        var mixed = new PluginMatch { Kinds = ["image", "text"] };
        Assert.True(PluginMatcher.Matches(mixed, Text("hello")));
    }

    [Fact]
    public void AnytimePluginWithConstraintsNeedsText()
    {
        var match = new PluginMatch { Kinds = [], Pattern = "^\\d+$" };
        Assert.False(PluginMatcher.Matches(match, ClassifiedContent.Empty));
        Assert.True(PluginMatcher.Matches(match, Text("123")));
        Assert.Equal(ContentKind.Text, PluginMatcher.RequiredKinds(match));
        var action = PluginRunner.ToAction(With(match));
        Assert.False(action.IsAvailable(ClassifiedContent.Empty));
        Assert.True(action.IsAvailable(Text("123")));
        Assert.False(action.IsAvailable(Text("abc")));
    }

    [Fact]
    public void PatternSeesFilePaths()
    {
        using var folder = new TempFolder();
        var file = folder.File("notes.txt");
        File.WriteAllText(file, "x");
        var content = Text(file);
        Assert.True(content.Has(ContentKind.Files));
        Assert.True(PluginMatcher.Matches(new PluginMatch { Kinds = ["files"], Pattern = "\\.txt$" }, content));
        Assert.False(PluginMatcher.Matches(new PluginMatch { Kinds = ["files"], Pattern = "\\.pdf$" }, content));
    }

    [Fact]
    public void BadPatternsDoNotHang()
    {
        Assert.False(PluginMatcher.IsValidPattern("("));
        Assert.True(PluginMatcher.IsValidPattern("(?i)^pop$"));
        Assert.False(PluginMatcher.Matches(new PluginMatch { Pattern = "(" }, Text("(")));
        Assert.True(PluginMatcher.Matches(new PluginMatch { Pattern = "(?i)^pop$" }, Text("POP")));

        // 回溯爆炸的正则在限定时间内放弃
        var started = DateTime.UtcNow;
        Assert.False(PluginMatcher.Matches(new PluginMatch { Pattern = "^(a+)+$" }, Text(new string('a', 40) + "!")));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
