using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

/// 从真实功能开关检查衍生计数，防止只改设置而保留旧的说明与批量按钮状态。
internal static class ToolSettingsChecks
{
    public static async Task VerifyAsync(App app, Action<string, SettingsWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("功能设置检查仅用于演示回归");
        var before = app.Settings.ToJson();
        var disabled = app.Settings.DisabledActions.ToList();
        SettingsWindow? window = null;
        try
        {
            app.Settings.DisabledActions = [];
            var total = Actions.BuiltIn.Append(Actions.All).Count();
            window = new SettingsWindow(app, "actions"); window.Show(); await Task.Delay(300);
            var summary = Tree(window).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "settings-tools-summary");
            var all = Tree(window).OfType<ButtonBase>().Single(e => AutomationProperties.GetAutomationId(e) == "settings-tools-enable-all");
            var toggle = Tree(window).OfType<WpfUi.ToggleSwitch>().Single(e => AutomationProperties.GetAutomationId(e) == "settings-action-toggle-" + Actions.Json.Id);
            string Summary() => Tree(summary).OfType<TextBlock>().Single(b => b.Text.StartsWith("已打开 ", StringComparison.Ordinal)).Text;
            if (Summary() != $"已打开 {total} 个内置功能，共 {total} 个" || all.IsEnabled)
                throw new InvalidOperationException("全开基线的功能计数或批量状态不正确");
            toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(75);
            if (app.Settings.IsEnabled(Actions.Json.Id)) throw new InvalidOperationException("实际功能开关未关闭 JSON");
            var expected = $"已打开 {total - 1} 个内置功能，共 {total} 个";
            if (Summary() != expected || !all.IsEnabled)
            {
                summary.BringIntoView(); await Task.Delay(350); shot("settings-tools-counter-stale", window);
                throw new InvalidOperationException($"单项关闭后计数/批量按钮仍过时：actual={Summary()} expected={expected} bulkEnabled={all.IsEnabled}");
            }
        }
        finally
        {
            window?.Close();
            app.UpdateSettings(s => s.DisabledActions = disabled);
            app.FlushPendingSettings();
        }
        if (app.Settings.ToJson() != before) throw new InvalidOperationException("功能设置检查未恢复原配置");
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject element)
    {
        yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
}
