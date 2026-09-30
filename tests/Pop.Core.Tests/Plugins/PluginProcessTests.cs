using System.ComponentModel;
using System.Diagnostics;
using Pop.Core;

namespace Pop.Core.Tests;

/// 真的启动子进程的测试：/bin/sh 在 Linux、macOS 上跑；PowerShell 7 在装了 pwsh 的地方跑；
/// Windows PowerShell 和 cmd 只在 Windows（CI）上跑
public sealed class PluginProcessTests : IDisposable
{
    private readonly TempFolder folder = new();

    public void Dispose() => folder.Dispose();

    private static ProcessRequest Sh(string script, string? input = null, IReadOnlyDictionary<string, string>? environment = null, double seconds = 10) =>
        new("/bin/sh", ["-c", script], input, environment ?? new Dictionary<string, string>(), TimeSpan.FromSeconds(seconds));

    // MARK: /bin/sh

    [UnixShellFact]
    public async Task RunsAShellWithUtf8InputAndEnvironment()
    {
        var environment = new Dictionary<string, string> { ["POP_TEXT"] = "环境", ["POP_FILES"] = "/a\n/b" };
        var output = await SystemProcessRunner.Instance.RunAsync(Sh("tr a-z A-Z; printf '|%s|%s' \"$POP_TEXT\" \"$POP_FILES\"", "pop 你好🎉\n", environment));
        Assert.Equal(new ProcessOutput(0, "POP 你好🎉\n|环境|/a\n/b", "", false), output);

        var failed = await SystemProcessRunner.Instance.RunAsync(Sh("echo out; echo oops >&2; exit 3"));
        Assert.Equal(new ProcessOutput(3, "out\n", "oops\n", false), failed);

        // 不给标准输入时，读标准输入马上结束
        Assert.Equal("0", (await SystemProcessRunner.Instance.RunAsync(Sh("wc -c | tr -d ' \\n'"))).StandardOutput);
        // 子进程不读标准输入就退出也没关系
        Assert.Equal(0, (await SystemProcessRunner.Instance.RunAsync(Sh("exit 0", new string('x', 1024 * 1024)))).ExitCode);
    }

    [UnixShellFact]
    public async Task KillsTheProcessTreeOnTimeout()
    {
        var watch = Stopwatch.StartNew();
        var output = await SystemProcessRunner.Instance.RunAsync(Sh("echo started; sleep 20 & wait", seconds: 1));
        Assert.True(output.TimedOut);
        Assert.Equal("started\n", output.StandardOutput);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [UnixShellFact]
    public async Task DoesNotWaitForBackgroundChildren()
    {
        var watch = Stopwatch.StartNew();
        var output = await SystemProcessRunner.Instance.RunAsync(Sh("echo hi; sleep 5 &"));
        Assert.Equal(0, output.ExitCode);
        Assert.False(output.TimedOut);
        Assert.Equal("hi\n", output.StandardOutput);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), watch.Elapsed.ToString());
    }

    [UnixShellFact]
    public async Task CancellationStopsTheProcess()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SystemProcessRunner.Instance.RunAsync(Sh("sleep 20", seconds: 60), cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [UnixShellFact]
    public async Task CapsHugeOutput()
    {
        var output = await SystemProcessRunner.Instance.RunAsync(Sh("head -c 20000000 /dev/zero | tr '\\0' 'x'", seconds: 30));
        Assert.Equal(0, output.ExitCode);
        Assert.Equal(SystemProcessRunner.MaxOutputBytes, output.StandardOutput.Length);
    }

    [Fact]
    public async Task ReportsProgramsThatDoNotExist()
    {
        var request = new ProcessRequest(Path.Combine(folder.Path, "no-such-program"), [], null, new Dictionary<string, string>(), TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<Win32Exception>(() => SystemProcessRunner.Instance.RunAsync(request));

        if (OperatingSystem.IsWindows()) return;
        // 这里没有 cmd.exe：报告启动不了，脚本的临时文件也删掉了
        var runner = new PluginRunner(scriptDirectory: folder.Path);
        var missing = await runner.ExecuteAsync(new PluginAction { Type = PluginActionType.Shell, Script = "x", Shell = PluginShell.Cmd }, PluginInput.Of("x"));
        Assert.StartsWith("无法运行 命令提示符：", missing.Error);
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    // MARK: PowerShell

    private async Task<PluginRunResult> RunShell(PluginShell shell, string script, string text, double timeout = 60, string? directory = null, params string[] files)
    {
        var runner = new PluginRunner(scriptDirectory: directory ?? folder.Path);
        var result = await runner.ExecuteAsync(new PluginAction { Type = PluginActionType.Shell, Script = script, Shell = shell, Timeout = timeout }, PluginInput.Of(text, files));
        // Windows 上的输出是 CRLF 换行
        return result with { Output = result.Output.Replace("\r\n", "\n", StringComparison.Ordinal) };
    }

    private async Task PowerShellBehavesLikeAScriptFile(PluginShell shell)
    {
        var sort = PluginTemplates.All.Single(t => t.Id == "shell-sort").Manifest.Action.Script;
        Assert.Equal(PluginRunResult.Success("a\nb"), await RunShell(shell, sort, "b\na\nb\n"));

        // 选中的文字按 UTF-8 传进去、读出来
        Assert.Equal(PluginRunResult.Success("你好 POP🎉\n第二行"), await RunShell(shell, "[Console]::In.ReadToEnd().ToUpperInvariant()", "你好 pop🎉\n第二行"));
        Assert.Equal(PluginRunResult.Success("3 行：甲|乙|"), await RunShell(shell, "$lines = @($input)\n\"$($lines.Count) 行：$($lines -join '|')\"", "甲\n乙\n\n"));
        Assert.Equal(PluginRunResult.Success("环境 🎉|C:\\a.txt"), await RunShell(shell, "\"$env:POP_TEXT|$env:POP_FILES\"", "环境 🎉", files: "C:\\a.txt"));
        var count = PluginTemplates.All.Single(t => t.Id == "shell-say").Manifest.Action.Script;
        Assert.Equal(PluginRunResult.Success("4"), await RunShell(shell, count, "你好🎉a"));

        // 退出码和 -File 运行脚本时一样
        Assert.Equal(PluginRunResult.Failure("脚本退出码 3"), await RunShell(shell, "'before'\nexit 3\n'after'", "x"));
        Assert.Equal(PluginRunResult.Failure("第 2 行：boom"), await RunShell(shell, "'partial'\nthrow 'boom'", "x"));
        Assert.Equal(PluginRunResult.Success("still here"), await RunShell(shell, "Write-Error 'soft'\n'still here'", "x"));
        var syntax = await RunShell(shell, "'fine'\nif (1 {", "x");
        Assert.StartsWith("第 2 行：", syntax.Error);

        // 脚本原样交给 PowerShell，param、using 这些只能写在开头的语句照常能用
        if (shell == PluginShell.Pwsh)
            Assert.Equal(PluginRunResult.Success("ok"), await RunShell(shell, "using namespace System.Text\nparam()\n[StringBuilder]::new('ok').ToString()", "x"));

        // 临时文件夹的路径里有空格和单引号
        var odd = Path.Combine(folder.Path, "it's a dir");
        Assert.Equal(PluginRunResult.Success("odd"), await RunShell(shell, "'odd'", "x", directory: odd));
        Assert.Empty(Directory.GetFiles(odd));

        var watch = Stopwatch.StartNew();
        Assert.Equal(PluginRunResult.Failure("运行超时"), await RunShell(shell, "Start-Sleep -Seconds 30", "x", timeout: 2));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), watch.Elapsed.ToString());
    }

    [PwshFact]
    public Task PowerShell7() => PowerShellBehavesLikeAScriptFile(PluginShell.Pwsh);

    [WindowsFact]
    public Task WindowsPowerShell() => PowerShellBehavesLikeAScriptFile(PluginShell.PowerShell);

    // MARK: cmd

    [WindowsFact]
    public async Task CommandPrompt()
    {
        Assert.Equal(PluginRunResult.Success("hello 你好"), await RunShell(PluginShell.Cmd, "echo %POP_TEXT%", "hello 你好"));
        Assert.Equal(PluginRunResult.Success("line1\nline2"), await RunShell(PluginShell.Cmd, "findstr \"^\"", "line1\nline2\n"));
        Assert.Equal(PluginRunResult.Success("第一\n第二"), await RunShell(PluginShell.Cmd, "echo 第一\necho 第二", "x"));
        Assert.Equal(PluginRunResult.Failure("脚本退出码 3"), await RunShell(PluginShell.Cmd, "exit /b 3", "x"));
        Assert.Equal(PluginRunResult.Failure("oops"), await RunShell(PluginShell.Cmd, "echo oops 1>&2\nexit /b 2", "x"));
        var odd = Path.Combine(folder.Path, "Tom & Jerry (a)");
        Assert.Equal(PluginRunResult.Success("odd"), await RunShell(PluginShell.Cmd, "echo odd", "x", directory: odd));
    }
}
