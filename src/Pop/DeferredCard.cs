using System.Diagnostics;
using Pop.Core;

namespace Pop;

/// 卡片展示的唯一入口：新结果或关闭使旧异步任务失效，即使服务忽略取消也不能回写。
internal sealed class DeferredCard : IDisposable
{
    private readonly ResultCard card;
    private readonly Action<bool> setOpen;
    private CancellationTokenSource? pending;
    private long generation;
    private bool disposed;

    public DeferredCard(ResultCard card, Action<bool> setOpen)
    {
        this.card = card; this.setOpen = setOpen;
        card.Dismissed += OnDismissed;
    }

    public void Show(int x, int y, CardContent content)
    {
        Invalidate();
        if (!disposed) Present(x, y, content);
    }

    public void Toast(int x, int y, string message)
    {
        Invalidate();
        if (disposed) return;
        setOpen(false); card.ShowToast(x, y, message);
    }

    public void CopyFailure(int x, int y, string title, string text, string? instruction)
    {
        Invalidate();
        if (disposed) return;
        setOpen(true); card.ShowCopyFailure(x, y, title, text, instruction);
    }

    public void Dismiss()
    {
        Invalidate(); setOpen(false);
        if (!disposed) card.Dismiss();
    }

    public async Task RunAsync(int x, int y, CardContent? loading, Func<CancellationToken, Task<CardContent>> work)
    {
        Invalidate();
        if (disposed) return;
        var current = generation;
        using var run = new CancellationTokenSource();
        pending = run;
        try
        {
            if (loading is not null) Present(x, y, loading);
            var result = await work(run.Token);
            if (!disposed && current == generation && !run.IsCancellationRequested) Present(x, y, result);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception) when (disposed || current != generation) { }
        finally
        {
            if (ReferenceEquals(pending, run)) pending = null;
        }
    }

    private void Present(int x, int y, CardContent content)
    {
        var watch = Stopwatch.StartNew();
        setOpen(true); card.ShowResult(x, y, content);
        if (watch.ElapsedMilliseconds >= 200) Log.Info($"显示卡片用了 {watch.ElapsedMilliseconds} ms");
    }
    private void OnDismissed() { Invalidate(); setOpen(false); }
    private void Invalidate()
    {
        generation++;
        var previous = pending; pending = null;
        previous?.Cancel(); // 请求的取消源由正在等待的 RunAsync 在结束时释放
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Invalidate(); setOpen(false);
        card.Dismissed -= OnDismissed;
    }
}
