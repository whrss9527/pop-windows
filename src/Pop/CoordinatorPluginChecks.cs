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
        var tasks = new List<Task>();
        try
        {
            var closing = Start();
            coordinator.CloseCard(); // 尚未显示加载卡片，也必须取消。
            await Task.Delay(450);
            if (!closing.Call.Token.IsCancellationRequested || coordinator.cardOpen || coordinator.card.IsOpen)
            {
                shot("plugin-close-problem", coordinator.card);
                throw new InvalidOperationException("加载卡片出现前关闭插件，旧任务仍显示加载状态或没有取消进程");
            }
            await Complete(closing, "关闭后的迟到结果");
            await AssertClosed();

            var escaping = Start();
            if (!coordinator.FilterKey(VK_ESCAPE)) throw new InvalidOperationException("插件等待时没有接收 Esc");
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await Task.Delay(450);
            if (!escaping.Call.Token.IsCancellationRequested) throw new InvalidOperationException("Esc 未取消尚未显示的插件");
            await Complete(escaping, "Esc 后的迟到结果");
            await AssertClosed();

            var replaced = Start();
            await coordinator.Execute(Actions.Count, content, x, y); // 圆盘和选择工具条共同使用的真实入口。
            var current = Actions.Count.Run(content)?.Card?.PrimaryText;
            if (string.IsNullOrEmpty(current)) throw new InvalidOperationException("字数统计没有产生可核对结果");
            await Task.Delay(450); // 已超过旧插件的加载阈值，当前卡片也不能变成加载状态。
            AssertPrimary(current);
            if (!replaced.Call.Token.IsCancellationRequested) throw new InvalidOperationException("切换功能未取消旧插件");
            await Complete(replaced, "不得覆盖字数统计");
            AssertPrimary(current);
            await Task.Delay(200); shot("plugin-current-action", coordinator.card);

            var staleFailure = Start();
            await coordinator.Run(Actions.Count, content, x, y); // 全部功能列表使用 Run 入口。
            await Complete(staleFailure, "", error: "迟到的旧插件错误");
            AssertPrimary(current);

            var first = Start();
            var second = Start();
            await Complete(second, "只保留最近一次插件的结果。", waitAfter: 200);
            await Complete(first, "旧插件的迟到结果");
            if (!first.Call.Token.IsCancellationRequested) throw new InvalidOperationException("新插件未取消旧插件");
            AssertPrimary("只保留最近一次插件的结果。");
            await Task.Delay(200); shot("plugin-latest-result", coordinator.card);

            var loading = Start();
            await Task.Delay(450);
            if (!coordinator.cardOpen || !coordinator.card.IsOpen || loading.Call.Token.IsCancellationRequested)
                throw new InvalidOperationException("当前插件的正常加载卡片没有出现，或取消了自己的请求");
            await Task.Delay(200); shot("plugin-loading", coordinator.card);
            coordinator.card.Dismiss(); // 实际卡片关闭事件也要停止插件。
            if (!loading.Call.Token.IsCancellationRequested) throw new InvalidOperationException("关闭可见卡片未取消插件");
            await Complete(loading, "关闭加载卡片后的迟到结果");
            await AssertClosed();

            var failing = Start();
            await Complete(failing, "", error: "当前插件的受控错误");
            // 轻提示会在 1.3 秒后收起；在处理完成时核对，额外延时可能被慢机器拖过提示寿命。
            var failureText = TextOf(coordinator.card);
            if (!coordinator.card.IsVisible || !failureText.Contains("当前插件的受控错误", StringComparison.Ordinal))
            {
                shot("plugin-current-error-problem", coordinator.card);
                throw new InvalidOperationException($"当前插件的真实失败未反馈，visible={coordinator.card.IsVisible} text={failureText}");
            }
            coordinator.CloseCard(); await AssertClosed();

            var oldTranslation = new TaskCompletionSource<CardContent>(TaskCreationOptions.RunContinuationsAsynchronously);
            var translationToken = CancellationToken.None;
            var translating = coordinator.deferredCard.RunAsync(x, y, null, cancellation =>
            {
                translationToken = cancellation;
                return oldTranslation.Task;
            });
            tasks.Add(translating);
            try
            {
                var nextPlugin = Start();
                if (!translationToken.IsCancellationRequested) throw new InvalidOperationException("新插件未取消旧翻译或识别请求");
                await Complete(nextPlugin, "新的插件结果优先。");
            }
            finally { oldTranslation.TrySetResult(CardContent.Text("过期翻译", "不可覆盖插件")); }
            await translating.WaitAsync(TimeSpan.FromSeconds(5));
            AssertPrimary("新的插件结果优先。");

            var exiting = Start();
            coordinator.Dispose();
            await Task.Delay(450);
            if (!exiting.Call.Token.IsCancellationRequested) throw new InvalidOperationException("退出未取消等待中的插件进程");
            await Complete(exiting, "退出后不可显示");
            if (coordinator.cardOpen || coordinator.card.IsVisible || coordinator.pluginPending)
                throw new InvalidOperationException("退出后插件仍重新打开结果或留下等待状态");

            (Task Task, PendingPluginCall Call) Start()
            {
                var count = process.Calls.Count;
                var task = coordinator.Run(action, content, x, y);
                tasks.Add(task);
                if (process.Calls.Count != count + 1 || !process.Calls[^1].Token.CanBeCanceled)
                    throw new InvalidOperationException("插件进程未收到取消令牌");
                return (task, process.Calls[^1]);
            }
            async Task Complete((Task Task, PendingPluginCall Call) request, string output, string error = "", int waitAfter = 0)
            {
                request.Call.Reply.TrySetResult(new ProcessOutput(error.Length == 0 ? 0 : 1, output, error, false));
                await request.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (waitAfter > 0) await Task.Delay(waitAfter);
            }
            void AssertPrimary(string expected)
            {
                if (!coordinator.cardOpen || coordinator.card.PrimaryText != expected)
                    throw new InvalidOperationException("旧插件覆盖了当前操作的结果卡片");
            }
            async Task AssertClosed()
            {
                await Task.Delay(200);
                if (coordinator.cardOpen || coordinator.card.IsOpen || coordinator.pluginPending)
                    throw new InvalidOperationException("取消后旧插件重新打开了卡片或没有结束等待");
            }
        }
        finally
        {
            coordinator.CloseCard();
            foreach (var call in process.Calls) call.Reply.TrySetResult(new ProcessOutput(0, "清理等待", "", false));
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        }

        static string TextOf(System.Windows.DependencyObject root)
        {
            if (root is System.Windows.Controls.TextBlock text) return text.Text;
            return string.Join(" ", Enumerable.Range(0, System.Windows.Media.VisualTreeHelper.GetChildrenCount(root))
                .Select(i => TextOf(System.Windows.Media.VisualTreeHelper.GetChild(root, i))));
        }
    }

    private sealed record PendingPluginCall(CancellationToken Token)
    {
        public TaskCompletionSource<ProcessOutput> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingPluginProcess : IProcessRunner
    {
        public List<PendingPluginCall> Calls { get; } = [];
        public Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            var call = new PendingPluginCall(cancellationToken);
            Calls.Add(call);
            return call.Reply.Task; // 故意不服从取消，仍必须丢弃迟到结果。
        }
    }
}
