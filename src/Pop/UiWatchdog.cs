using System.Diagnostics;
using System.Windows.Threading;

namespace Pop;

/// 界面线程卡住时记日志：后台线程每半秒往界面线程排一个空操作，超过两秒还没轮到就记一笔，恢复后记卡了多久。
/// 界面线程卡住的这段时间里，长按、松开和按键都在排队，圆盘和卡片也不会动
internal sealed class UiWatchdog : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(2);

    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource stop = new();

    public UiWatchdog(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        new Thread(Run) { IsBackground = true, Name = "Pop 界面线程看门狗" }.Start();
    }

    public void Dispose() => stop.Cancel();

    private void Run()
    {
        using var answered = new ManualResetEventSlim();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                answered.Reset();
                var watch = Stopwatch.StartNew();
                dispatcher.BeginInvoke(() => answered.Set());
                if (!answered.Wait(Threshold, stop.Token))
                {
                    Log.Info($"界面线程超过 {Threshold.TotalSeconds:0} 秒没有响应");
                    answered.Wait(stop.Token);
                    Log.Info($"界面线程恢复响应，卡了 {watch.ElapsedMilliseconds} ms");
                }
                stop.Token.WaitHandle.WaitOne(Interval);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
