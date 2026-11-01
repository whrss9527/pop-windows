using System.Windows.Threading;
using Pop.Core;

namespace Pop;

internal sealed partial class Coordinator
{
    /// 经过真实协调器和插件运行器，只替换子进程等待；不安装全局钩子或使用正式历史目录。
    internal static async Task VerifyPluginLifetimeAsync(App app, int x, int y, Action<string, ResultCard> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("插件运行归属检查仅用于演示回归");
        var directory = Path.Combine(Path.GetTempPath(), "pop-plugin-lifetime-" + Guid.NewGuid().ToString("N"));
        var preferences = new AppSettings { ClipboardHistory = false, ClipboardAutomaticCleanup = false };
        using var store = new PluginStore(Path.Combine(directory, "plugins"));
        using var history = new ClipboardHistory(() => preferences, Path.Combine(directory, "history"));
        using var hook = new InputHook();
        var process = new PendingPluginProcess();
        using var coordinator = new Coordinator(Dispatcher.CurrentDispatcher, hook, () => preferences,
            history, () => store, new PluginRunner(process, Path.Combine(directory, "scripts")));
        var manifest = new PluginManifest
        {
            Name = "等待中的插件",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = "controlled-fixture" },
            Output = PluginOutput.Card,
        };
        store.Save(manifest);
        var action = PluginRunner.ToAction(manifest);
        var content = ContentClassifier.Classify("用于检查插件等待的示例文字");
        var pending = coordinator.Run(action, content, x, y);
        try
        {
            if (!process.Token.CanBeCanceled) throw new InvalidOperationException("插件进程未收到取消令牌");
            coordinator.CloseCard(); // 尚未显示加载卡片，也必须取消。
            await Task.Delay(450);
            if (!process.Token.IsCancellationRequested || coordinator.cardOpen || coordinator.card.IsOpen)
            {
                shot("plugin-close-problem", coordinator.card);
                throw new InvalidOperationException("加载卡片出现前关闭插件，旧任务仍显示加载状态或没有取消进程");
            }
        }
        finally
        {
            process.Reply.TrySetResult(new ProcessOutput(0, "迟到的插件结果", "", false));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class PendingPluginProcess : IProcessRunner
    {
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<ProcessOutput> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Reply.Task; // 故意不服从取消，仍必须丢弃迟到结果。
        }
    }
}
