using System.ComponentModel;
using Pop.Core;

namespace Pop.Core.Tests;

public sealed class PluginRunnerTests : IDisposable
{
    private readonly TempFolder scripts = new();

    public void Dispose() => scripts.Dispose();

    private static readonly PluginRunner Runner = new();

    private static Task<PluginRunResult> Run(PluginAction action, string text, params string[] files) =>
        Runner.ExecuteAsync(action, PluginInput.Of(text, files));

    private static PluginAction JavaScript(string script, double timeout = PluginAction.DefaultTimeout) =>
        new() { Type = PluginActionType.JavaScript, Script = script, Timeout = timeout };

    // MARK: JavaScript

    [Fact]
    public async Task JavaScriptRunsLikeOnTheMac()
    {
        Assert.Equal(PluginRunResult.Success("POP"), await Run(JavaScript("function run(input) { return input.toUpperCase() }"), "pop"));
        Assert.Equal(PluginRunResult.Success("cba"), await Run(JavaScript("input.split('').reverse().join('')"), "abc"));
        Assert.Equal(PluginRunResult.Success("{\n  \"count\": 2\n}"), await Run(JavaScript("function run(input, files) { return { count: files.length } }"), "", "a", "b"));
        Assert.Equal(PluginRunResult.Success("hello 1"), await Run(JavaScript("console.log('hello', 1)"), ""));
        // 有返回值时不用 console.log 的内容；末尾的换行去掉
        Assert.Equal(PluginRunResult.Success("result"), await Run(JavaScript("console.log('debug'); 'result\\n\\n'"), ""));
        Assert.Equal(PluginRunResult.Success("[\n  1,\n  \"x\"\n]"), await Run(JavaScript("[1, 'x']"), ""));
        Assert.Equal(PluginRunResult.Success("42"), await Run(JavaScript("function run() { return 42 }"), ""));
        Assert.Equal(PluginRunResult.Success(""), await Run(JavaScript("var nothing = 1"), ""));
        Assert.Equal(PluginRunResult.Success("/a|/b"), await Run(JavaScript("files.join('|')"), "", "/a", "/b"));
    }

    [Fact]
    public async Task MacJavaScriptTemplatesWork()
    {
        var reverse = PluginTemplates.All.Single(t => t.Id == "js-reverse").Manifest.Action;
        Assert.Equal(PluginRunResult.Success("🎉界世好你"), await Run(reverse, "你好世界🎉"));
        var keys = PluginTemplates.All.Single(t => t.Id == "js-json-keys").Manifest.Action;
        Assert.Equal(PluginRunResult.Success("a\nb"), await Run(keys, "{\"a\": 1, \"b\": [2]}"));
    }

    [Fact]
    public async Task JavaScriptErrors()
    {
        var thrown = await Run(JavaScript("function run() {\n  throw new Error('boom')\n}"), "x");
        Assert.Equal("第 2 行：Error: boom", thrown.Error);

        var syntax = await Run(JavaScript("function ("), "x");
        Assert.False(syntax.IsSuccess);
        Assert.StartsWith("第 1 行：SyntaxError", syntax.Error);

        // 抛出的不是错误对象时只有内容，和 macOS 版一样
        Assert.Equal("plain", (await Run(JavaScript("throw 'plain'"), "x")).Error);

        var type = await Run(JavaScript("null.x"), "x");
        Assert.StartsWith("第 1 行：TypeError", type.Error);
    }

    [Fact]
    public async Task JavaScriptTimeout()
    {
        var started = DateTime.UtcNow;
        var result = await Run(JavaScript("while (true) {}", timeout: 1), "x");
        Assert.Equal(PluginRunResult.Failure("运行超时"), result);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));

        // 定义函数和调用 run 共用一个时间预算
        started = DateTime.UtcNow;
        var split = await Run(JavaScript("var t = Date.now(); while (Date.now() - t < 700) {}\nfunction run() { var u = Date.now(); while (Date.now() - u < 700) {} return 'late' }", timeout: 1), "x");
        Assert.Equal(PluginRunResult.Failure("运行超时"), split);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));

        // 回溯爆炸的正则也受时间限制
        var regex = await Run(JavaScript("/^(a+)+$/.test('a'.repeat(40) + '!')", timeout: 1), "x");
        Assert.Equal(PluginRunResult.Failure("运行超时"), regex);
    }

    [Fact]
    public async Task JavaScriptCanBeCanceled()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner.ExecuteAsync(JavaScript("while (true) {}", timeout: 60), PluginInput.Of(""), cancel.Token));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunawayScriptsFailWithoutHurtingPop()
    {
        var recursion = await Run(JavaScript("function f(n) { return f(n + 1) }\nf(0)"), "x");
        Assert.Contains("Maximum call stack size exceeded", recursion.Error);
        // 正常深度的递归没问题
        Assert.Equal(PluginRunResult.Success("3000"), await Run(JavaScript("function f(n) { return n === 0 ? 0 : 1 + f(n - 1) }\nf(3000)"), "x"));

        var limited = new PluginRunner(javaScriptLimits: new JavaScriptLimits(HeapBytes: 128L * 1024 * 1024));
        var memory = await limited.ExecuteAsync(JavaScript("var a = []; while (true) a.push('x'.repeat(10000) + a.length)"), PluginInput.Of("x"));
        Assert.Equal(PluginRunResult.Failure("脚本占用的内存太多"), memory);
        var array = await Run(JavaScript("new Array(1e9).fill(0).length"), "x");
        Assert.Equal(PluginRunResult.Failure("脚本占用的内存太多"), array);

        // 只是分配得多、没有留着不放的脚本不受影响
        Assert.Equal(PluginRunResult.Success("100000"), await limited.ExecuteAsync(JavaScript("var n = 0; for (let i = 0; i < 1e5; i++) { n += ('x'.repeat(1000) + i).length > 0 ? 1 : 0 } n"), PluginInput.Of("x")));
    }

    [Fact]
    public async Task JavaScriptCannotReachDotNet()
    {
        Assert.Equal(PluginRunResult.Success("undefined undefined undefined"), await Run(JavaScript("typeof System + ' ' + typeof importNamespace + ' ' + typeof require"), ""));
    }

    // MARK: Shell（假的子进程）

    private PluginRunner FakeRunner(FakeProcessRunner processes) => new(processes, scripts.Path);

    private static PluginAction Shell(string script, PluginShell shell = PluginShell.PowerShell, double timeout = PluginAction.DefaultTimeout) =>
        new() { Type = PluginActionType.Shell, Script = script, Shell = shell, Timeout = timeout };

    [Fact]
    public async Task ShellRunsWindowsPowerShellByDefault()
    {
        var processes = new FakeProcessRunner(new ProcessOutput(0, "POP\r\n", "", false));
        var input = new PluginInput("你好 pop", ["C:\\a.txt", "C:\\b.txt"], ["files", "text"]);
        var result = await FakeRunner(processes).ExecuteAsync(Shell("$input | ForEach-Object { $_.ToUpper() }"), input);
        Assert.Equal(PluginRunResult.Success("POP"), result);

        var request = Assert.Single(processes.Requests);
        Assert.Equal("powershell.exe", request.FileName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command"], request.Arguments.Take(5));
        Assert.Equal("你好 pop", request.StandardInput);
        Assert.Equal("你好 pop", request.Environment["POP_TEXT"]);
        Assert.Equal("C:\\a.txt\nC:\\b.txt", request.Environment["POP_FILES"]);
        Assert.Equal("files,text", request.Environment["POP_KINDS"]);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), request.WorkingDirectory);

        // 脚本放在临时文件里，引导脚本按 UTF-8 读它；运行完删掉
        var (path, content) = Assert.Single(processes.Scripts);
        Assert.StartsWith(scripts.Path, path);
        Assert.EndsWith(".ps1", path);
        Assert.Equal("$input | ForEach-Object { $_.ToUpper() }", content);
        Assert.False(File.Exists(path));
        Assert.DoesNotContain("\"", request.Arguments[^1]);
        Assert.DoesNotContain("-EncodedCommand", request.Arguments);
    }

    [Fact]
    public async Task ShellCanUsePwshOrCmd()
    {
        var processes = new FakeProcessRunner(new ProcessOutput(0, "ok", "", false));
        var runner = FakeRunner(processes);
        await runner.ExecuteAsync(Shell("Get-Date", PluginShell.Pwsh), PluginInput.Of("x"));
        Assert.Equal("pwsh", processes.Requests[0].FileName);
        Assert.Equal("-Command", processes.Requests[0].Arguments[^2]);

        await runner.ExecuteAsync(Shell("echo %POP_TEXT%\nfindstr \"^\"\r\nexit /b 0", PluginShell.Cmd, timeout: 5), PluginInput.Of("x"));
        var request = processes.Requests[1];
        Assert.Equal("cmd.exe", request.FileName);
        Assert.Equal(["/d", "/c", "call"], request.Arguments.Take(3));
        var (path, content) = processes.Scripts[1];
        Assert.Equal(path, request.Arguments[^1]);
        Assert.EndsWith(".cmd", path);
        Assert.Equal("@echo off\r\nchcp 65001 >nul\r\necho %POP_TEXT%\r\nfindstr \"^\"\r\nexit /b 0\r\n", content);
        Assert.Equal("x", request.StandardInput);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }

    [Fact]
    public async Task ShellResults()
    {
        async Task<PluginRunResult> With(ProcessOutput output) =>
            await FakeRunner(new FakeProcessRunner(output)).ExecuteAsync(Shell("x"), PluginInput.Of(""));

        Assert.Equal(PluginRunResult.Success("a\nb"), await With(new ProcessOutput(0, "a\nb\n\n", "warning", false)));
        Assert.Equal(PluginRunResult.Failure("oops"), await With(new ProcessOutput(3, "partial", " oops\n", false)));
        Assert.Equal(PluginRunResult.Failure("脚本退出码 4"), await With(new ProcessOutput(4, "", "", false)));
        Assert.Equal(PluginRunResult.Failure("运行超时"), await With(new ProcessOutput(-1, "partial", "", true)));
        Assert.Equal(500, (await With(new ProcessOutput(1, "", new string('错', 800), false))).Error!.Length);

        var missing = await FakeRunner(new FakeProcessRunner(_ => throw new Win32Exception("系统找不到指定的文件。"))).ExecuteAsync(Shell("x", PluginShell.Pwsh), PluginInput.Of(""));
        Assert.Equal(PluginRunResult.Failure("无法运行 PowerShell 7：系统找不到指定的文件。"), missing);
    }

    [Fact]
    public void EnvironmentTruncatesLongText()
    {
        var text = new string('a', PluginRunner.MaxEnvironmentLength - 1) + "🎉" + "tail";
        var environment = PluginRunner.Environment(PluginInput.Of(text));
        // 表情是两个 UTF-16 字符，不能从中间切开
        Assert.Equal(new string('a', PluginRunner.MaxEnvironmentLength - 1), environment["POP_TEXT"]);
        Assert.Equal("", environment["POP_FILES"]);
        Assert.Equal("short", PluginRunner.Environment(PluginInput.Of("short"))["POP_TEXT"]);
    }

    [Fact]
    public void BootstrapQuotesTheScriptPath()
    {
        var bootstrap = ShellCommand.PowerShellBootstrap("C:\\Users\\O'Brien\\AppData\\Local\\Temp\\pop-plugin-1.ps1");
        Assert.Contains("ReadAllText('C:\\Users\\O''Brien\\AppData\\Local\\Temp\\pop-plugin-1.ps1'", bootstrap);
        Assert.Contains("ReadAllText('C:\\Users\\A’’B\\x.ps1'", ShellCommand.PowerShellBootstrap("C:\\Users\\A’B\\x.ps1"));
        Assert.DoesNotContain("\n", bootstrap);
        Assert.DoesNotContain("$input", bootstrap);
    }

    [Fact]
    public void CommandLineQuoting()
    {
        Assert.Equal("-NoProfile", CommandLine.Quote("-NoProfile"));
        Assert.Equal("\"\"", CommandLine.Quote(""));
        Assert.Equal("\"a b\"", CommandLine.Quote("a b"));
        Assert.Equal("\"C:\\Users\\Tom&Jerry\\x.cmd\"", CommandLine.Quote("C:\\Users\\Tom&Jerry\\x.cmd"));
        Assert.Equal("\"say \\\"hi\\\"\"", CommandLine.Quote("say \"hi\""));
        Assert.Equal("\"a b\\\\\"", CommandLine.Quote("a b\\"));
        Assert.Equal("\"a\\\\\\\"b\"", CommandLine.Quote("a\\\"b"));
        Assert.Equal("C:\\path\\x.ps1", CommandLine.Quote("C:\\path\\x.ps1"));
        Assert.Equal("/d /c call \"C:\\My Files\\x.cmd\"", CommandLine.Join(["/d", "/c", "call", "C:\\My Files\\x.cmd"]));
    }

    [Fact]
    public void TrimTrailingNewlines()
    {
        Assert.Equal("a\nb", PluginRunner.TrimTrailingNewlines("a\nb\n\r\n"));
        Assert.Equal("", PluginRunner.TrimTrailingNewlines("\n"));
        Assert.Equal("  a  ", PluginRunner.TrimTrailingNewlines("  a  \r\n"));
    }

    // MARK: 结果去向

    [Fact]
    public void OutputModes()
    {
        static PluginManifest With(PluginOutput output, string name = "x") => new() { Name = name, Output = output };

        Assert.Equal(ActionResult.Copy("result"), PluginRunner.Present("result", With(PluginOutput.Copy), canReplace: true));
        Assert.Equal(ActionResult.Toast("「x」没有输出"), PluginRunner.Present("", With(PluginOutput.Copy), canReplace: true));

        Assert.Equal(ActionResult.Replace("new"), PluginRunner.Present("new", With(PluginOutput.Replace), canReplace: true));
        Assert.Equal(ActionResult.Toast("没有选中文字，无法替换"), PluginRunner.Present("new", With(PluginOutput.Replace), canReplace: false));
        Assert.Equal(ActionResult.Toast("「x」没有输出，原文保持不变"), PluginRunner.Present("", With(PluginOutput.Replace), canReplace: true));

        Assert.Equal(ActionResult.Toast("first"), PluginRunner.Present("first\nsecond", With(PluginOutput.Toast), canReplace: false));
        Assert.Equal(ActionResult.Toast("first"), PluginRunner.Present("\r\n\r\nfirst\r\nsecond", With(PluginOutput.Toast), canReplace: false));
        Assert.Equal(ActionResult.Toast("已完成"), PluginRunner.Present("", With(PluginOutput.Toast), canReplace: false));
        Assert.Equal(ActionResult.Toast(new string('长', 60)), PluginRunner.Present(new string('长', 100), With(PluginOutput.Toast), canReplace: false));

        var card = PluginRunner.Present("body", With(PluginOutput.Card, "标题"), canReplace: true);
        Assert.Equal(ActionEffect.Card, card.Effect);
        Assert.Equal("标题", card.Card!.Title);
        Assert.Empty(card.Card.Lines);
        Assert.Equal("body", card.Card.Body);
        Assert.Equal("body", card.Card.Replacement);
        Assert.Equal("body", card.Card.PrimaryText);
        Assert.Null(PluginRunner.Present("body", With(PluginOutput.Card), canReplace: false).Card!.Replacement);
        Assert.Equal(ActionResult.Toast("「未命名插件」已完成"), PluginRunner.Present("", With(PluginOutput.Card, " "), canReplace: true));

        Assert.Equal(new ActionResult(ActionEffect.None), PluginRunner.Present("ignored", With(PluginOutput.None), canReplace: true));
    }

    // MARK: 圆盘和运行入口

    [Fact]
    public void ToActionDefersToRunAsync()
    {
        var manifest = new PluginManifest
        {
            Id = "user-upper",
            Name = "大写（JS）",
            Symbol = "magnifyingglass",
            Summary = "转成大写",
            Match = new PluginMatch { Kinds = ["foreignText"] },
            Action = JavaScript("input.toUpperCase()"),
        };
        var action = PluginRunner.ToAction(manifest);
        Assert.Equal("user-upper", action.Id);
        Assert.Equal("大写（JS）", action.Title);
        Assert.Equal("Search24", action.Glyph);
        Assert.Equal(ContentKind.ForeignText, action.Requires);
        Assert.True(action.Matches("cj"));
        Assert.True(action.Matches("转成"));

        var english = ContentClassifier.Classify("hello world");
        Assert.True(action.IsAvailable(english));
        var result = action.Run(english)!;
        Assert.Equal(ActionEffect.RunPlugin, result.Effect);
        Assert.Equal("user-upper", result.PluginId);
        Assert.Equal(ActionResult.RunPlugin("user-upper"), result);
        Assert.Null(ActionResult.Toast("x").PluginId);

        var chinese = ContentClassifier.Classify("你好");
        Assert.False(action.IsAvailable(chinese));
        Assert.Null(action.Run(chinese));
        Assert.Equal("PuzzlePiece24", PluginRunner.ToAction(new PluginManifest()).Glyph);
    }

    [Fact]
    public async Task RunAsyncHandlesEveryActionType()
    {
        var text = ContentClassifier.Classify("C++ 你好");
        var runner = FakeRunner(new FakeProcessRunner(new ProcessOutput(0, "from shell\n", "", false)));

        var url = new PluginManifest { Name = "搜", Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" } };
        Assert.Equal(ActionResult.Open("https://example.com/?q=C%2B%2B%20%E4%BD%A0%E5%A5%BD"), await runner.RunAsync(url, text));
        var badUrl = url with { Action = new PluginAction { Type = PluginActionType.Url, Template = "example.com/{text}" } };
        Assert.Equal(ActionResult.Toast("「搜」的网址模板无效"), await runner.RunAsync(badUrl, text));

        var shortcut = new PluginManifest { Name = "快捷", Action = new PluginAction { Type = PluginActionType.Shortcut, Shortcut = "我的快捷指令" } };
        Assert.Contains("macOS", (await runner.RunAsync(shortcut, text))!.Text);
        var ai = new PluginManifest { Name = "改写", Action = new PluginAction { Type = PluginActionType.Ai, Prompt = "改写：{text}" } };
        Assert.Contains("AI", (await runner.RunAsync(ai, text))!.Text);

        var javaScript = new PluginManifest { Name = "JS", Action = JavaScript("input.length"), Output = PluginOutput.Card };
        var card = (await runner.RunAsync(javaScript, text))!;
        Assert.Equal("6", card.Card!.Body);
        Assert.Equal("6", card.Card.Replacement);

        var shell = new PluginManifest { Name = "Shell", Action = Shell("x"), Output = PluginOutput.Replace };
        Assert.Equal(ActionResult.Replace("from shell"), await runner.RunAsync(shell, text));

        var failing = new PluginManifest { Name = "坏了", Action = JavaScript("throw 'nope'") };
        Assert.Equal(ActionResult.Toast("「坏了」运行失败：nope"), await runner.RunAsync(failing, text));

        // 内容用不了这个插件
        var jsonOnly = javaScript with { Match = new PluginMatch { Kinds = ["json"] } };
        Assert.Null(await runner.RunAsync(jsonOnly, text));

        // 随时可用的插件在没选中内容时也能跑，但不能替换
        var anytime = new PluginManifest { Name = "随时", Match = new PluginMatch { Kinds = [] }, Action = JavaScript("'hi'"), Output = PluginOutput.Replace };
        Assert.Equal(ActionResult.Toast("没有选中文字，无法替换"), await runner.RunAsync(anytime, ClassifiedContent.Empty));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(javaScript, text, canceled.Token));
    }

    [Fact]
    public async Task ExecuteReturnsTheExpandedUrlForDryRuns()
    {
        var action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" };
        Assert.Equal(PluginRunResult.Success("https://example.com/?q=a%20b"), await Run(action, "a b"));
        Assert.Equal(PluginRunResult.Failure("网址模板无效"), await Run(action with { Template = "nope {text}" }, "a"));
        Assert.False((await Run(new PluginAction { Type = PluginActionType.Shortcut, Shortcut = "x" }, "a")).IsSuccess);
        Assert.False((await Run(new PluginAction { Type = PluginActionType.Ai, Prompt = "x" }, "a")).IsSuccess);
    }
}
