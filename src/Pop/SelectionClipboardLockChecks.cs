using System.Windows.Interop;

namespace Pop;

/// 用独立 STA 所有者在锁等待期间改写内容，确认判断发生在成功取得锁之后。
internal static class SelectionClipboardLockChecks
{
    public static async Task VerifyAsync(IntPtr target)
    {
        using var access = new ClipboardAccess("Pop 选区锁检查");
        access.SetText("guard-lock-original", temporary: true);
        var saved = access.Save()!;
        using (var held = await HeldClipboard.StartAsync(replace: true))
        {
            var before = ClipboardAccess.SequenceNumber;
            var guardCalls = 0;
            held.Continue();
            if (access.TryReadAndRestore(saved, before, () => { guardCalls++; return true; }, out _) || guardCalls != 1)
                throw new InvalidOperationException("锁等待检查未取得访问权，或覆盖了等待期间的新序列");
            if (access.GetText() != "guard-lock-new") throw new InvalidOperationException("等待锁期间的新内容被恢复覆盖");
        }

        using (var held = await HeldClipboard.StartAsync(replace: false))
        {
            using var denied = new ClipboardAccess("Pop 无法快照检查", openAttempts: 1);
            var sends = 0;
            using var reader = new SelectionReader(denied, () => 0, () => { sends++; return true; });
            if (await reader.ReadCopyForChecksAsync(TimeSpan.FromMilliseconds(30), target) is not null || sends != 0)
                throw new InvalidOperationException("未取得原快照时仍发送了复制");
        }
    }

    private sealed class HeldClipboard : IDisposable
    {
        private readonly ManualResetEventSlim proceed = new(), finish = new();
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread thread;

        private HeldClipboard(bool replace)
        {
            thread = new Thread(() =>
            {
                try
                {
                    using var writer = new ClipboardAccess("Pop 锁检查所有者");
                    if (!writer.SetText("guard-lock-held", temporary: true) || !Native.OpenClipboard(Native.GetClipboardOwner()))
                        throw new InvalidOperationException("无法准备实际剪贴板锁");
                    try
                    {
                        ready.TrySetResult();
                        proceed.Wait(TimeSpan.FromSeconds(3));
                        if (replace)
                        {
                            Thread.Sleep(40);
                            if (!Native.EmptyClipboard() || !ClipboardAccess.RenderText("guard-lock-new"))
                                throw new InvalidOperationException("锁内替换哨兵失败");
                        }
                    }
                    finally { Native.CloseClipboard(); }
                    finish.Wait(TimeSpan.FromSeconds(3));
                }
                catch (Exception e) { ready.TrySetException(e); }
            }) { IsBackground = true, Name = "Pop 选区锁回归" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }

        public static async Task<HeldClipboard> StartAsync(bool replace)
        {
            var held = new HeldClipboard(replace);
            try { await held.ready.Task.WaitAsync(TimeSpan.FromSeconds(3)); return held; }
            catch { held.Dispose(); throw; }
        }
        public void Continue() => proceed.Set();
        public void Dispose()
        {
            proceed.Set(); finish.Set();
            if (!thread.Join(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("锁检查线程没有结束");
            proceed.Dispose(); finish.Dispose();
        }
    }
}
