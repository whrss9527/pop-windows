using System.Text.RegularExpressions;
using Pop.Core;

namespace Pop.Core.Tests;

/// 有 /bin/sh 时才运行（Linux、macOS 上开发时）
public sealed class UnixShellFactAttribute : FactAttribute
{
    public UnixShellFactAttribute()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh")) Skip = "没有 /bin/sh";
    }
}

/// 装了 PowerShell 7（PATH 里有 pwsh）时才运行；CI 的 Windows 上有
public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (TestPrograms.Find("pwsh") is null) Skip = "没有 pwsh";
    }
}

/// 只在 Windows 上运行（Windows PowerShell 5.1、cmd）
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "只在 Windows 上运行";
    }
}

public static class TestPrograms
{
    /// 在 PATH 里找程序
    public static string? Find(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

/// 测试用的临时文件夹，用完删掉
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pop-plugins-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// 不真的启动进程：记下请求和那一刻脚本文件的内容，返回事先定好的结果
public sealed partial class FakeProcessRunner(Func<ProcessRequest, ProcessOutput> respond) : IProcessRunner
{
    public FakeProcessRunner(ProcessOutput output) : this(_ => output)
    {
    }

    public List<ProcessRequest> Requests { get; } = [];

    /// 每次调用时脚本临时文件的路径和内容
    public List<(string Path, string Content)> Scripts { get; } = [];

    public Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var path = ScriptPath(request);
        Scripts.Add((path, System.IO.File.ReadAllText(path)));
        return Task.FromResult(respond(request));
    }

    /// cmd 的最后一个参数就是脚本；PowerShell 的脚本路径写在引导脚本的 ReadAllText('…') 里
    public static string ScriptPath(ProcessRequest request) =>
        request.FileName == "cmd.exe" ? request.Arguments[^1] : QuotedPath().Match(request.Arguments[^1]).Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);

    [GeneratedRegex(@"ReadAllText\('((?:[^']|'')*)'")]
    private static partial Regex QuotedPath();
}
