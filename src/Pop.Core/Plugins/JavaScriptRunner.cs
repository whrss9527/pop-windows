using System.Text.RegularExpressions;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace Pop.Core;

/// JavaScript 插件的运行限制（运行时间看插件的 timeout）
/// <param name="HeapBytes">运行期间托管堆最多增长多少，防止失控的脚本把 Pop 的内存吃光</param>
/// <param name="MaxArraySize">一个数组最多多少项</param>
/// <param name="StackBytes">运行脚本的线程的栈大小，决定递归能有多深（再深会得到 RangeError，不会让 Pop 崩溃）</param>
public sealed record JavaScriptLimits(long HeapBytes = 512L * 1024 * 1024, uint MaxArraySize = 16 * 1024 * 1024, int StackBytes = 16 * 1024 * 1024)
{
    public static readonly JavaScriptLimits Default = new();
}

/// 用 Jint 运行插件脚本，约定和 macOS 版相同：定义 function run(input, files) 并返回结果；
/// 也可以不定义 run，直接写表达式（最后一个表达式的值就是结果）。返回对象时转成两个空格缩进的 JSON，
/// 没有返回值时把 console.log 的内容当作结果
public static partial class JavaScriptRunner
{
    /// 在运行用户脚本之前定义的 console 和结果转换
    private const string Helpers =
        "var console = { log: function () { var parts = []; for (var i = 0; i < arguments.length; i++) { parts.push(String(arguments[i])); } __popLog(parts.join(' ')); } };\n" +
        "function __popStringify(value) {\n" +
        "  if (value === undefined || value === null) return '';\n" +
        "  if (typeof value === 'string') return value;\n" +
        "  if (typeof value === 'object') {\n" +
        "    try { return JSON.stringify(value, null, 2); } catch (e) { return String(value); }\n" +
        "  }\n" +
        "  return String(value);\n" +
        "}\n";

    /// 在单独的线程上运行（栈大小见 JavaScriptLimits）。超时返回失败；cancellationToken 取消时抛出 OperationCanceledException
    public static Task<PluginRunResult> RunAsync(string source, PluginInput input, TimeSpan timeout, CancellationToken cancellationToken = default, JavaScriptLimits? limits = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= JavaScriptLimits.Default;
        var completion = new TaskCompletionSource<PluginRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Run(source, input, timeout, cancellationToken, limits));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception e)
            {
                completion.TrySetResult(PluginRunResult.Failure(e.Message));
            }
        }, limits.StackBytes)
        {
            IsBackground = true,
            Name = "Pop 插件脚本",
        };
        thread.Start();
        return WithFallbackAsync(completion.Task, timeout, cancellationToken);
    }

    /// 兜底：解释器卡在一次很长的内置调用里、没机会检查时间时，也不让调用方一直等下去
    private static async Task<PluginRunResult> WithFallbackAsync(Task<PluginRunResult> run, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var fallback = Task.Delay(timeout + TimeSpan.FromSeconds(1), cancellationToken);
        if (await Task.WhenAny(run, fallback).ConfigureAwait(false) == run) return await run.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return PluginRunResult.Failure("运行超时");
    }

    /// 在当前线程上运行
    public static PluginRunResult Run(string source, PluginInput input, TimeSpan timeout, CancellationToken cancellationToken, JavaScriptLimits limits)
    {
        // 整次运行共用一个时间预算：定义函数、调用 run、转换结果都算在里面
        var deadline = new OperationDeadlineConstraint();
        var heap = new HeapConstraint(limits.HeapBytes);
        var logs = new List<string>();
        using var engine = new Engine(options =>
        {
            options.Constraint(deadline);
            options.Constraint(heap);
            options.Constraints.StackOverflowGuard = true;
            options.Constraints.RegexTimeout = timeout;
            options.Constraints.MaxArraySize = limits.MaxArraySize;
        });
        deadline.Begin(timeout, cancellationToken);
        heap.Begin();
        try
        {
            engine.SetValue("__popLog", new Action<string>(logs.Add));
            engine.SetValue("input", input.Text);
            var files = new JsArray(engine, input.Files.Select(file => (JsValue)new JsString(file)).ToArray());
            engine.SetValue("files", files);
            engine.Execute(Helpers);

            var completion = engine.Evaluate(source);
            var value = engine.Evaluate("typeof run === 'function'").AsBoolean()
                ? engine.Invoke("run", input.Text, files)
                : completion;
            var text = engine.Invoke("__popStringify", value).ToString();
            if (text.Length == 0 && logs.Count > 0) text = string.Join("\n", logs);
            return PluginRunResult.Success(PluginRunner.TrimTrailingNewlines(text));
        }
        catch (JavaScriptException e)
        {
            return PluginRunResult.Failure(Describe(e));
        }
        catch (Exception e) when (e is TimeoutException or RegexMatchTimeoutException)
        {
            return PluginRunResult.Failure("运行超时");
        }
        catch (Exception e) when (e is MemoryLimitExceededException or HeapLimitException or OutOfMemoryException)
        {
            return PluginRunResult.Failure("脚本占用的内存太多");
        }
        catch (InsufficientExecutionStackException)
        {
            return PluginRunResult.Failure("脚本嵌套太深");
        }
        catch (JintException e)
        {
            return PluginRunResult.Failure(e.Message);
        }
        finally
        {
            deadline.End();
        }
    }

    /// 「第 3 行：TypeError: ……」。抛出的不是错误对象（比如字符串）时只有内容
    private static string Describe(JavaScriptException exception)
    {
        var message = LocationSuffix().Replace(exception.Error.ToString(), "");
        var line = exception.Location.Start.Line;
        return exception.Error is ObjectInstance && line > 0 ? $"第 {line.ToString(System.Globalization.CultureInfo.InvariantCulture)} 行：{message}" : message;
    }

    /// 语法错误的消息末尾自带的位置，比如「(anonymous:1:10)」，行号已经写在前面了
    [GeneratedRegex(@"\s*\([^()]*:\d+:\d+\)$")]
    private static partial Regex LocationSuffix();

    /// 托管堆比开始时多出太多就停下。只在这个线程每分配 16 MB 后看一次堆，平时几乎没有开销；
    /// 超过上限时先完整回收一次垃圾再下结论，只是分配得多、但没有留着不放的脚本不会被误伤
    private sealed class HeapConstraint(long limit) : Constraint
    {
        private const long ProbeInterval = 16 * 1024 * 1024;
        private long baseline;
        private long lastProbe;

        /// 只读取外部状态，隔几条语句检查一次也不影响结果，解释器的快速循环可以保持开启
        public override bool IsAmortizable => true;

        public void Begin()
        {
            baseline = GC.GetTotalMemory(forceFullCollection: false);
            lastProbe = GC.GetAllocatedBytesForCurrentThread();
        }

        public override void Reset()
        {
            // 预算覆盖整次运行，不随每次进入解释器重置
        }

        public override void Check()
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            if (allocated - lastProbe < ProbeInterval) return;
            lastProbe = allocated;
            if (GC.GetTotalMemory(forceFullCollection: false) - baseline <= limit) return;
            if (GC.GetTotalMemory(forceFullCollection: true) - baseline > limit) throw new HeapLimitException();
        }
    }

    private sealed class HeapLimitException : Exception;
}
