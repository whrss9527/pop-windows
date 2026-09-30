using System.Text;

namespace Pop.Core;

/// 把 Shell 脚本变成子进程命令。脚本先写进临时文件（Extension、FileContent），
/// 选中的文字按 UTF-8 从标准输入传入，也放在环境变量 POP_TEXT、POP_FILES、POP_KINDS 里
public static class ShellCommand
{
    /// cmd 脚本的开头：不回显命令；代码页换成 UTF-8，后面的中文和输出都按 UTF-8 处理
    public const string CmdPrelude = "@echo off\r\nchcp 65001 >nul\r\n";

    public static string Extension(PluginShell shell) => shell == PluginShell.Cmd ? ".cmd" : ".ps1";

    /// 临时文件的内容（按 UTF-8 不带 BOM 写入）。cmd 脚本统一换成 CRLF 换行，只有 LF 时 cmd 会读错标签
    public static string FileContent(PluginShell shell, string script)
    {
        if (shell != PluginShell.Cmd) return script;
        var body = script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);
        return CmdPrelude + body + "\r\n";
    }

    /// 运行 scriptPath 的程序和参数
    public static (string FileName, IReadOnlyList<string> Arguments) Command(PluginShell shell, string scriptPath) => shell switch
    {
        // call 开头的命令行 cmd 原样执行，路径里有空格、括号、与号也不会拆错
        PluginShell.Cmd => ("cmd.exe", ["/d", "/c", "call", scriptPath]),
        PluginShell.Pwsh => ("pwsh", PowerShellArguments(scriptPath)),
        _ => ("powershell.exe", PowerShellArguments(scriptPath)),
    };

    public static ProcessRequest Request(PluginShell shell, string scriptPath, PluginInput input, TimeSpan timeout, string? workingDirectory)
    {
        var (fileName, arguments) = Command(shell, scriptPath);
        return new ProcessRequest(fileName, arguments, input.Text, PluginRunner.Environment(input), timeout, workingDirectory);
    }

    private static IReadOnlyList<string> PowerShellArguments(string scriptPath) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", PowerShellBootstrap(scriptPath)];

    /// 交给 -Command 的一行引导脚本，Windows PowerShell 5.1 和 PowerShell 7 都能用。
    /// PowerShell 自己读标准输入时按控制台代码页解码，中文会乱，所以由引导脚本按 UTF-8 读出来，再交给用户脚本：
    /// [Console]::In 读到的是全文；脚本里用到 $input 时逐行传给它（和 -File 运行脚本时一样）。
    /// 输出也换成 UTF-8 的纯文本（不带颜色）。
    /// 退出码和 -File 运行脚本时一样：exit N 就是 N，没处理的错误是 1（错误信息写到错误输出，带上行号），其余是 0。
    /// 故意不用 -EncodedCommand：那样 PowerShell 会把错误写成 CLIXML
    public static string PowerShellBootstrap(string scriptPath) => string.Join("; ",
        "$ProgressPreference = 'SilentlyContinue'",
        "if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }",
        "$__popUtf8 = [System.Text.UTF8Encoding]::new($false)",
        "try { [Console]::OutputEncoding = $__popUtf8 } catch { }",
        "$OutputEncoding = $__popUtf8",
        "$__popText = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), $__popUtf8).ReadToEnd()",
        "[Console]::SetIn([System.IO.StringReader]::new($__popText))",
        "$__popErrors = $null",
        "$__popAst = [System.Management.Automation.Language.Parser]::ParseInput([System.IO.File]::ReadAllText(" + SingleQuoted(scriptPath) + ", $__popUtf8), [ref]$null, [ref]$__popErrors)",
        "if ($__popErrors) { foreach ($__popError in $__popErrors) { [Console]::Error.WriteLine('第 ' + $__popError.Extent.StartLineNumber + ' 行：' + $__popError.Message) }; exit 1 }",
        "try { $__popBlock = $__popAst.GetScriptBlock(); " +
            "if ($__popAst.Find({ param($node) $node -is [System.Management.Automation.Language.VariableExpressionAst] -and $node.VariablePath.UserPath -eq 'input' }, $true)) { " +
            "$__popLines = [System.Collections.Generic.List[string]]::new(); $__popReader = [System.IO.StringReader]::new($__popText); " +
            "while ($null -ne ($__popLine = $__popReader.ReadLine())) { $__popLines.Add($__popLine) }; $__popLines | & $__popBlock " +
            "} else { & $__popBlock } " +
        "} catch { $__popAt = $_.InvocationInfo.ScriptLineNumber; " +
            "[Console]::Error.WriteLine($(if ($__popAt -gt 0) { '第 ' + $__popAt + ' 行：' } else { '' }) + $_.Exception.Message); exit 1 }",
        "exit 0");

    /// PowerShell 的单引号字符串。弯单引号（‘ ’ ‚ ‛）在 PowerShell 里也算单引号，一样要重复一次
    private static string SingleQuoted(string text)
    {
        var quoted = new StringBuilder("'");
        foreach (var c in text)
        {
            quoted.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') quoted.Append(c);
        }
        return quoted.Append('\'').ToString();
    }
}
