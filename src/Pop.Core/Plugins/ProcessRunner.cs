using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Pop.Core;

/// 一次子进程运行
/// <param name="FileName">程序；不带路径时在 PATH 里找</param>
/// <param name="Arguments">参数，逐个给出，不用自己加引号</param>
/// <param name="StandardInput">写进标准输入的文字（UTF-8，写完关闭）；null 表示标准输入直接结束</param>
/// <param name="Environment">在当前环境变量之外再加的环境变量</param>
/// <param name="Timeout">超过这么久就结束整个进程树</param>
/// <param name="WorkingDirectory">工作目录；null 表示沿用当前目录</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout,
    string? WorkingDirectory = null);

/// 子进程的结果：退出码、标准输出和错误输出（按 UTF-8 解码）、有没有超时
public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// 运行子进程。插件运行器通过它启动 Shell，测试里可以换成假的
public interface IProcessRunner
{
    /// 程序启动不了（找不到等）时抛出异常；超时不抛异常，结果的 TimedOut 为 true；
    /// cancellationToken 取消时结束进程并抛出 OperationCanceledException
    Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

/// 真正启动子进程：不弹出控制台窗口，超时后结束整个进程树
public sealed class SystemProcessRunner : IProcessRunner
{
    public static readonly SystemProcessRunner Instance = new();

    /// 标准输出和错误输出各最多保留这么多字节，多的读出来丢掉（脚本不停打印时不把内存吃光）
    public const int MaxOutputBytes = 8 * 1024 * 1024;

    /// 进程结束后最多再等多久输出管道关闭：脚本放到后台的子进程可能一直占着它
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(1);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(request.FileName, CommandLine.Join(request.Arguments))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrEmpty(request.WorkingDirectory)) info.WorkingDirectory = request.WorkingDirectory;
        foreach (var (key, value) in request.Environment) info.Environment[key] = value;

        var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        var stdout = new OutputBuffer();
        var stderr = new OutputBuffer();
        var readers = Task.WhenAll(stdout.ReadAsync(process.StandardOutput.BaseStream), stderr.ReadAsync(process.StandardError.BaseStream));
        _ = FeedAsync(process.StandardInput.BaseStream, request.StandardInput);

        var timedOut = false;
        var canceled = false;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(Clamp(request.Timeout));
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = cancellationToken.IsCancellationRequested;
                timedOut = !canceled;
                Kill(process);
                await WaitQuietlyAsync(process.WaitForExitAsync(), DrainTimeout).ConfigureAwait(false);
            }
        }

        // 管道还被占着时不再等，已经读到的就是结果；读的任务等管道关闭后自己收尾
        await WaitQuietlyAsync(readers, DrainTimeout).ConfigureAwait(false);
        var exitCode = process.HasExited ? process.ExitCode : -1;
        // 用过的标准输入输出由读写它们的任务关闭，这里只释放进程句柄
        process.Dispose();

        if (canceled) throw new OperationCanceledException(cancellationToken);
        return new ProcessOutput(exitCode, stdout.Text(), stderr.Text(), timedOut);
    }

    private static TimeSpan Clamp(TimeSpan timeout) =>
        timeout <= TimeSpan.Zero ? TimeSpan.Zero : timeout > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : timeout;

    private static async Task FeedAsync(Stream input, string? text)
    {
        try
        {
            if (text is not null)
            {
                await input.WriteAsync(Utf8.GetBytes(text)).ConfigureAwait(false);
                await input.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // 子进程没读完标准输入就退出了
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                input.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 已经退出了
        }
    }

    /// 等到 task 完成或者超时；返回是否完成
    private static async Task<bool> WaitQuietlyAsync(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task) return false;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        return true;
    }

    /// 收集一路输出；读的线程和取结果的线程不同，用锁保护
    private sealed class OutputBuffer
    {
        private readonly object gate = new();
        private readonly MemoryStream bytes = new();

        public async Task ReadAsync(Stream stream)
        {
            var chunk = new byte[16 * 1024];
            try
            {
                int read;
                while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
                {
                    lock (gate)
                    {
                        var room = MaxOutputBytes - (int)bytes.Length;
                        if (room > 0) bytes.Write(chunk, 0, Math.Min(read, room));
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
            finally
            {
                stream.Dispose();
            }
        }

        public string Text()
        {
            byte[] data;
            lock (gate) data = bytes.ToArray();
            var text = Utf8.GetString(data);
            return text.StartsWith('\uFEFF') ? text[1..] : text;
        }
    }
}

/// 把参数拼成一条命令行（CreateProcess 用的格式）
public static class CommandLine
{
    /// 按 Windows C 运行库的规则加引号和反斜杠。除了空白和引号，含 cmd 特殊字符（与号、竖线、尖括号、圆括号、^、% 等）的参数也加上引号，
    /// 这样同一条命令行交给 cmd.exe 时这些字符不会被当成命令分隔符
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '(' or ')' or '^' or '%' or '!' or ',' or ';' or '='))
            return argument;
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // 引号前面的反斜杠要加倍，引号本身再转义一次
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }
        // 结尾的反斜杠后面紧跟着收尾的引号，也要加倍
        quoted.Append('\\', backslashes * 2);
        return quoted.Append('"').ToString();
    }
}
