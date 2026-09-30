using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 交给插件的输入
/// <param name="Text">选中的文字</param>
/// <param name="Files">选中的是本机上的路径时，展开后的路径</param>
/// <param name="Kinds">内容类型（macOS 版的类型名，排好序）</param>
public sealed record PluginInput(string Text, IReadOnlyList<string> Files, IReadOnlyList<string> Kinds)
{
    public static PluginInput Of(string text, params string[] files) => new(text, files, []);

    public static PluginInput From(ClassifiedContent content) =>
        new(content.Text.Length > 0 ? content.Text : content.Path ?? "",
            content.Path is { } path ? [path] : [],
            PluginKinds.Names(content.Kinds));
}

/// 插件运行的结果：成功时是输出（去掉了末尾的换行），失败时 Error 是原因
public sealed record PluginRunResult(string Output, string? Error)
{
    public bool IsSuccess => Error is null;

    public static PluginRunResult Success(string output) => new(output, null);

    public static PluginRunResult Failure(string error) => new("", error);
}

/// 运行用户插件：网址模板、Shell 脚本（PowerShell 或 cmd）、JavaScript。快捷指令只有 macOS 能用，AI 指令还没接上
public sealed class PluginRunner
{
    /// 环境变量 POP_TEXT 最长这么多个字符（Windows 上一个环境变量最多 32767 个字符），完整的文字从标准输入读
    public const int MaxEnvironmentLength = 32_000;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IProcessRunner processes;
    private readonly string scriptDirectory;
    private readonly JavaScriptLimits javaScriptLimits;

    /// <param name="processes">启动 Shell 用的；默认真的启动子进程</param>
    /// <param name="scriptDirectory">Shell 脚本的临时文件放在哪里；默认是系统的临时文件夹</param>
    /// <param name="javaScriptLimits">JavaScript 的内存、数组和栈的限制</param>
    public PluginRunner(IProcessRunner? processes = null, string? scriptDirectory = null, JavaScriptLimits? javaScriptLimits = null)
    {
        this.processes = processes ?? SystemProcessRunner.Instance;
        this.scriptDirectory = scriptDirectory ?? Path.GetTempPath();
        this.javaScriptLimits = javaScriptLimits ?? JavaScriptLimits.Default;
    }

    // MARK: 圆盘和列表

    /// 圆盘和「全部功能」列表里的一项。脚本可能要跑好几秒，所以 Run 不直接运行插件，
    /// 只返回 ActionEffect.RunPlugin（带着插件 ID），App 收到后在后台调用 RunAsync
    public static PopAction ToAction(PluginManifest manifest) =>
        new(manifest.Id,
            manifest.DisplayName,
            PluginGlyphs.Resolve(manifest),
            Keywords(manifest),
            PluginMatcher.RequiredKinds(manifest.Match),
            content => PluginMatcher.Matches(manifest, content) ? ActionResult.RunPlugin(manifest.Id) : null,
            content => PluginMatcher.Matches(manifest, content),
            Category: Actions.PluginCategory,
            Summary: manifest.DisplaySummary);

    /// 一组插件对应的功能：跳过 ID 无效、和内置功能重名或者重复的
    public static IReadOnlyList<PopAction> ToActions(IEnumerable<PluginManifest> manifests, Func<string, bool>? isReserved = null)
    {
        isReserved ??= PluginStore.IsBuiltinId;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return manifests.Where(m => PluginManifest.IsUsableId(m.Id, isReserved) && seen.Add(m.Id)).Select(ToAction).ToList();
    }

    /// 搜索用的关键字：ID、说明、动作类型，以及「插件」
    private static string Keywords(PluginManifest manifest) =>
        string.Join(' ', new[] { manifest.Id, manifest.Summary, PluginNames.Title(manifest.Action.Type), "cj chajian plugin" }.Where(s => s.Length > 0));

    // MARK: 运行

    /// 运行插件，得到 App 要做的事。内容用不了这个插件时返回 null；运行失败时是一句轻提示；
    /// cancellationToken 取消时结束脚本并抛出 OperationCanceledException
    public async Task<ActionResult?> RunAsync(PluginManifest manifest, ClassifiedContent content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PluginMatcher.Matches(manifest, content)) return null;
        var name = manifest.DisplayName;
        var input = PluginInput.From(content);
        switch (manifest.Action.Type)
        {
            case PluginActionType.Url:
                return PluginTemplates.ExpandUrl(manifest.Action.Template, input.Text) is { } url
                    ? ActionResult.Open(url)
                    : ActionResult.Toast($"「{name}」的网址模板无效");
            case PluginActionType.Shortcut:
                return ActionResult.Toast($"「{name}」用的是快捷指令，只能在 macOS 上运行");
            case PluginActionType.Ai:
                return ActionResult.Toast($"还没有设置 AI 接口，暂时用不了「{name}」");
        }
        var result = await ExecuteAsync(manifest.Action, input, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error
            ? ActionResult.Toast($"「{name}」运行失败：{error}")
            : Present(result.Output, manifest, canReplace: !content.IsEmpty);
    }

    /// 运行插件并返回输出（去掉末尾换行）。网址插件只返回展开后的网址，不会打开，设置里的「试运行」也用它
    public async Task<PluginRunResult> ExecuteAsync(PluginAction action, PluginInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = TimeSpan.FromSeconds(PluginAction.ClampTimeout(action.Timeout));
        switch (action.Type)
        {
            case PluginActionType.Url:
                return PluginTemplates.ExpandUrl(action.Template, input.Text) is { } url
                    ? PluginRunResult.Success(url)
                    : PluginRunResult.Failure("网址模板无效");
            case PluginActionType.Shell:
                return await RunShellAsync(action, input, timeout, cancellationToken).ConfigureAwait(false);
            case PluginActionType.JavaScript:
                return await JavaScriptRunner.RunAsync(action.Script, input, timeout, cancellationToken, javaScriptLimits).ConfigureAwait(false);
            case PluginActionType.Shortcut:
                return PluginRunResult.Failure("快捷指令只能在 macOS 上运行");
            default:
                return PluginRunResult.Failure("还没有设置 AI 接口");
        }
    }

    private async Task<PluginRunResult> RunShellAsync(PluginAction action, PluginInput input, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var path = Path.Combine(scriptDirectory, "pop-plugin-" + Guid.NewGuid().ToString("N") + ShellCommand.Extension(action.Shell));
        try
        {
            Directory.CreateDirectory(scriptDirectory);
            File.WriteAllText(path, ShellCommand.FileContent(action.Shell, action.Script), Utf8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PluginRunResult.Failure("无法准备脚本：" + e.Message);
        }
        try
        {
            var request = ShellCommand.Request(action.Shell, path, input, timeout, HomeDirectory());
            ProcessOutput output;
            try
            {
                output = await processes.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return PluginRunResult.Failure($"无法运行 {PluginNames.Title(action.Shell)}：{e.Message}");
            }
            return ScriptOutput(output);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 脚本启动的子进程还开着它；留在临时文件夹里也无妨
            }
        }
    }

    /// 超时、退出码不是 0 都算失败；失败时用错误输出（最多 500 个字）说明原因
    public static PluginRunResult ScriptOutput(ProcessOutput output)
    {
        if (output.TimedOut) return PluginRunResult.Failure("运行超时");
        if (output.ExitCode != 0)
        {
            var message = output.StandardError.Trim();
            return PluginRunResult.Failure(message.Length == 0
                ? "脚本退出码 " + output.ExitCode.ToString(CultureInfo.InvariantCulture)
                : Prefix(message, 500));
        }
        return PluginRunResult.Success(TrimTrailingNewlines(output.StandardOutput));
    }

    /// 传给脚本的环境变量：POP_TEXT 是选中的文字（太长时截断），POP_FILES 是每行一个路径，POP_KINDS 是逗号隔开的内容类型
    public static IReadOnlyDictionary<string, string> Environment(PluginInput input) => new Dictionary<string, string>
    {
        ["POP_TEXT"] = Truncate(input.Text, MaxEnvironmentLength),
        ["POP_FILES"] = string.Join("\n", input.Files),
        ["POP_KINDS"] = string.Join(",", input.Kinds),
    };

    private static string? HomeDirectory()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && Directory.Exists(home) ? home : null;
    }

    // MARK: 结果

    /// 按插件设置的去向处理输出。canReplace：有选中的文字可以替换
    public static ActionResult Present(string output, PluginManifest manifest, bool canReplace)
    {
        var name = manifest.DisplayName;
        switch (manifest.Output)
        {
            case PluginOutput.Card:
                return output.Length == 0
                    ? ActionResult.Toast($"「{name}」已完成")
                    : ActionResult.ShowCard(new CardContent(name, [], output, canReplace ? output : null));
            case PluginOutput.Copy:
                return output.Length == 0 ? ActionResult.Toast($"「{name}」没有输出") : ActionResult.Copy(output);
            case PluginOutput.Replace:
                if (!canReplace) return ActionResult.Toast("没有选中文字，无法替换");
                return output.Length == 0 ? ActionResult.Toast($"「{name}」没有输出，原文保持不变") : ActionResult.Replace(output);
            case PluginOutput.Toast:
                var firstLine = output.Split(NewlineCharacters, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                return ActionResult.Toast(firstLine.Length == 0 ? "已完成" : Prefix(firstLine, 60));
            default:
                return new ActionResult(ActionEffect.None);
        }
    }

    private static readonly char[] NewlineCharacters = ['\n', '\r', '\u000B', '\u000C', '\u0085', '\u2028', '\u2029'];

    /// 去掉末尾的换行（CRLF、LF 等）
    public static string TrimTrailingNewlines(string text) => text.TrimEnd(NewlineCharacters);

    /// 前 count 个字（按用户看到的字符数）
    private static string Prefix(string text, int count)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= count ? text : text[..starts[count]];
    }

    /// 最多 count 个 UTF-16 字符，不把一个字（比如表情的代理项对）切开
    private static string Truncate(string text, int count)
    {
        if (text.Length <= count) return text;
        var end = 0;
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
        {
            if (start > count) break;
            end = start;
        }
        return text[..end];
    }
}
