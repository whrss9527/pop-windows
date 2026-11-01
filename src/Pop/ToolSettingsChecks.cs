using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
            toggle.BringIntoView(); toggle.Focus(); await Task.Delay(75);
            var offset = window.SettingsScrollOffset;
            toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(75);
            if (app.Settings.IsEnabled(Actions.Json.Id)) throw new InvalidOperationException("实际功能开关未关闭 JSON");
            var expected = $"已打开 {total - 1} 个内置功能，共 {total} 个";
            if (Summary() != expected || !all.IsEnabled)
            {
                summary.BringIntoView(); await Task.Delay(350); shot("settings-tools-counter-stale", window);
                throw new InvalidOperationException($"单项关闭后计数/批量按钮仍过时：actual={Summary()} expected={expected} bulkEnabled={all.IsEnabled}");
            }
            if (!ReferenceEquals(toggle, Find<WpfUi.ToggleSwitch>("settings-action-toggle-" + Actions.Json.Id))
                || !toggle.IsKeyboardFocusWithin || Math.Abs(window.SettingsScrollOffset - offset) > 1)
                throw new InvalidOperationException("单项开关更新重建了原控件或改变焦点、滚动位置");

            app.FlushPendingSettings();
            var beforeSearch = app.Settings.ToJson();
            window.PreviewSettingsSearch("JSON");
            if (!window.OpenFirstSearchResult() || !await window.PendingSearchNavigation) throw new InvalidOperationException("全局设置搜索未打开具体功能筛选");
            await Task.Delay(75);
            var query = Find<WpfUi.TextBox>("settings-tools-query");
            var filter = Find<ComboBox>("settings-tools-filter");
            if (query.Text != "JSON" || !ShownIds().Contains(Actions.Json.Id) || ShownIds().Contains(Actions.Hash.Id)
                || app.Settings.ToJson() != beforeSearch || app.SettingsSavePending)
                throw new InvalidOperationException("全局搜索没有只读带入匹配功能");
            await Task.Delay(350); shot("settings-tools-filtered", window);

            app.UpdateSettings(s => { s.DisabledActions.Add(Actions.Hash.Id); s.DisabledActions.Add("demo-unmatched-plugin"); });
            await Task.Delay(100);
            query = Find<WpfUi.TextBox>("settings-tools-query"); filter = Find<ComboBox>("settings-tools-filter");
            query.Text = "JSON 格式化"; filter.SelectedIndex = (int)ToolStateFilter.Disabled;
            await Task.Delay(75);
            if (!ShownIds().SequenceEqual([Actions.Json.Id])) throw new InvalidOperationException("关闭视图显示了未匹配项");
            var scopedEnable = Find<ButtonBase>("settings-tools-enable-all");
            if (!scopedEnable.IsEnabled || scopedEnable.Content?.ToString() != "打开匹配项") throw new InvalidOperationException("批量按钮没有说明当前匹配范围");
            await Task.Delay(350); shot("settings-tools-disabled", window);
            scopedEnable.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(75);
            if (!app.Settings.IsEnabled(Actions.Json.Id) || app.Settings.IsEnabled(Actions.Hash.Id)
                || app.Settings.IsEnabled("demo-unmatched-plugin") || ShownIds().Count != 0
                || Find<ButtonBase>("settings-tools-enable-all").IsEnabled)
                throw new InvalidOperationException("批量启用改变了未匹配项或未刷新空视图");

            app.FlushPendingSettings();
            var beforeQueries = app.Settings.ToJson();
            filter.SelectedIndex = (int)ToolStateFilter.All;
            query.Text = "gsh"; await Task.Delay(45);
            if (!ShownIds().Contains(Actions.Json.Id)) throw new InvalidOperationException("拼音未找到 JSON 功能");
            query.Text = "ＪＳＯＮ"; await Task.Delay(45);
            if (!ShownIds().Contains(Actions.Json.Id)) throw new InvalidOperationException("全角英文未找到 JSON 功能");
            query.Text = "no-matching-tool-xyz"; await Task.Delay(45);
            if (ShownIds().Count != 0 || Find<ButtonBase>("settings-tools-enable-all").IsEnabled) throw new InvalidOperationException("空匹配仍显示可操作工具");
            await Task.Delay(350); shot("settings-tools-empty", window);
            query.Focus();
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            query.RaiseEvent(escape); await Task.Delay(50);
            if (!escape.Handled || query.Text.Length != 0 || !window.IsVisible) throw new InvalidOperationException("局部搜索 Esc 没有清空而关闭了设置");

            query.Text = Actions.All.Title; await Task.Delay(45);
            var locked = Find<WpfUi.ToggleSwitch>("settings-action-toggle-" + Actions.All.Id);
            if (locked.IsEnabled || locked.IsChecked != true) throw new InvalidOperationException("始终可用的入口被显示成可关闭或已关闭");
            if (app.Settings.ToJson() != beforeQueries || app.SettingsSavePending) throw new InvalidOperationException("筛选或搜索产生了设置改动");

            IReadOnlyList<string> ShownIds() => Tree(window).Select(AutomationProperties.GetAutomationId)
                .Where(id => id.StartsWith("settings-action-toggle-", StringComparison.Ordinal)).Select(id => id["settings-action-toggle-".Length..])
                .Where(Actions.IsBuiltIn).ToArray();
            T Find<T>(string id) where T : DependencyObject => Tree(window).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
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
