using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

internal sealed partial class Coordinator
{
    /// 通过真实插件和协调器显示执行失败，不使用正式配置、外部服务或系统剪贴板。
    internal static async Task VerifyNoticeFeedbackAsync(App app, int x, int y, Action<string, ResultCard> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("轻提示反馈检查仅用于演示回归");
        var directory = Path.Combine(Path.GetTempPath(), "pop-notice-feedback-" + Guid.NewGuid().ToString("N"));
        var preferences = new AppSettings { ClipboardHistory = false, ClipboardAutomaticCleanup = false };
        using var store = new PluginStore(Path.Combine(directory, "plugins"));
        using var history = new ClipboardHistory(() => preferences, Path.Combine(directory, "history"));
        using var hook = new InputHook();
        var process = new PendingPluginProcess();
        using var coordinator = new Coordinator(Dispatcher.CurrentDispatcher, hook, () => preferences,
            history, () => store, new PluginRunner(process, Path.Combine(directory, "scripts")));
        var manifest = new PluginManifest
        {
            Name = "服务连接检查",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = "controlled-fixture" },
            Output = PluginOutput.Card,
        };
        store.Save(manifest);
        Task? running = null;
        try
        {
            running = coordinator.Run(PluginRunner.ToAction(manifest), ContentClassifier.Classify("受控示例文字"), x, y);
            if (process.Calls.Count != 1) throw new InvalidOperationException("轻提示检查没有经过真实插件运行入口");
            process.Calls[0].Reply.SetResult(new ProcessOutput(1, "", "无法连接示例服务。请检查插件配置后重试。", false));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            var icon = Descendants(coordinator.card).OfType<WpfUi.SymbolIcon>().FirstOrDefault();
            if (icon is null || icon.Symbol != Icons.Parse("DismissCircle20"))
            {
                shot("notice-failure-problem", coordinator.card);
                throw new InvalidOperationException($"真实插件运行失败仍显示成功或错误含义的图标：{icon?.Symbol}");
            }
            await Task.Delay(200); shot("notice-error", coordinator.card);
        }
        finally
        {
            foreach (var call in process.Calls) call.Reply.TrySetResult(new ProcessOutput(0, "清理等待", "", false));
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.CloseCard();
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
