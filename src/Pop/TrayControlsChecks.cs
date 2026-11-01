using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 用真实切换/调用辅助接口验证托盘控件，避免仅给鼠标区域补一个可访问名称。
internal static class TrayControlsChecks
{
    public static async Task VerifyAsync(App app, int x, int y, Action<string, TrayFlyout> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("托盘状态检查仅用于独立演示");
        var before = app.Settings.ToJson();
        var enabled = app.Settings.Enabled; var mode = app.Settings.TriggerMode;
        var direct = app.Settings.DirectResults; var recording = app.Settings.ClipboardHistory;
        TrayFlyout? flyout = null;
        SettingsWindow? openedSettings = null;
        try
        {
            app.UpdateSettings(s => { s.Enabled = true; s.TriggerMode = TriggerMode.LongPressRight; s.DirectResults = true; s.ClipboardHistory = true; });
            flyout = new TrayFlyout(app); flyout.ShowNear(x, y);
            await Task.Delay(250); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            foreach (var id in new[] { "mouse", "direct", "recording" }) AssertState(id, true);
            var widths = new[] { "mouse", "direct", "recording" }.Select(id => Find<Border>("tray-face-" + id).ActualWidth).ToArray();
            var historyRow = Find<Button>("tray-action-history");
            var historyShortcut = Find<TextBlock>("tray-shortcut-history");
            var shortcutRight = historyShortcut.TranslatePoint(new Point(historyShortcut.ActualWidth, 0), historyRow).X;
            if (widths.Any(width => width < 80) || widths.Max() - widths.Min() > 1
                || shortcutRight < historyRow.ActualWidth - 24 || shortcutRight > historyRow.ActualWidth + 1)
            {
                shot("tray-controls-layout-problem", flyout);
                throw new InvalidOperationException($"托盘模板未铺满操作区：tiles={string.Join(',', widths)} shortcutRight={shortcutRight} rowWidth={historyRow.ActualWidth}");
            }
            await Task.Delay(200); shot("tray-controls-enabled", flyout);

            var mouse = Toggle("mouse"); mouse.Focus();
            PeerToggle(mouse).Toggle(); await Settled();
            AssertState("mouse", false);
            if (app.Settings.Enabled || !Toggle("mouse").IsKeyboardFocused || !flyout.IsVisible)
                throw new InvalidOperationException("托盘切换后实际状态、焦点或面板保持不正确");
            if (!Toggle("mouse").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
                throw new InvalidOperationException("托盘开关不能通过 Tab 接续操作");
            if (!Toggle("direct").IsKeyboardFocused) throw new InvalidOperationException("Tab 未到达下一个托盘开关");
            PeerToggle(Toggle("direct")).Toggle(); await Settled(); AssertState("direct", false);
            Toggle("direct").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            if (!Toggle("recording").IsKeyboardFocused) throw new InvalidOperationException("Tab 未到达记录开关");
            PeerToggle(Toggle("recording")).Toggle(); await Settled(); AssertState("recording", false);
            if (app.Settings.DirectResults || app.Settings.ClipboardHistory) throw new InvalidOperationException("辅助切换没有更新实际偏好");
            if (!AutomationProperties.GetHelpText(Toggle("recording")).Contains("已有历史仍可查看", StringComparison.Ordinal))
                throw new InvalidOperationException("暂停记录没有说明历史仍可查看");
            await Task.Delay(500); await Settled();
            if (!Toggle("recording").IsKeyboardFocused) throw new InvalidOperationException("自动保存刷新后丢失了托盘开关焦点");
            await Task.Delay(200); shot("tray-controls-paused", flyout);

            // 设置模式原本禁用时，开启按钮沿用原来的长按方式回退，不留下开关与实际模式冲突。
            app.UpdateSettings(s => { s.Enabled = true; s.TriggerMode = TriggerMode.Disabled; });
            await Settled(); AssertState("mouse", false);
            Toggle("mouse").Focus(); PeerToggle(Toggle("mouse")).Toggle(); await Settled(); AssertState("mouse", true);
            if (!app.Settings.Enabled || app.Settings.TriggerMode != TriggerMode.LongPressRight)
                throw new InvalidOperationException("禁用模式下开启鼠标没有恢复可用唤起方式");

            foreach (var id in new[] { "history", "snippets", "awake", "ocr", "pin", "annotate", "color", "measure", "settings", "quit" })
            {
                var row = Find<Button>("tray-action-" + id);
                if (!row.Focusable || !row.IsTabStop || new ButtonAutomationPeer(row).GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
                    throw new InvalidOperationException($"托盘入口不能通过键盘或辅助接口调用：{id}");
            }
            var settings = Find<Button>("tray-action-settings"); settings.BringIntoView(); settings.Focus();
            ((IInvokeProvider)new ButtonAutomationPeer(settings).GetPattern(PatternInterface.Invoke)).Invoke();
            var deadline = Environment.TickCount64 + 5_000;
            while ((openedSettings = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsVisible)) is null
                && Environment.TickCount64 < deadline) await Task.Delay(20);
            if (openedSettings is null || flyout.IsVisible) throw new InvalidOperationException("调用设置入口没有关闭托盘并打开实际设置");
            openedSettings.Close(); openedSettings = null;

            flyout = new TrayFlyout(app); flyout.ShowNear(x, y, heightLimitForDemo: 360);
            await Task.Delay(250);
            var bottom = Find<Button>("tray-action-quit"); bottom.BringIntoView(); bottom.Focus(); await Settled();
            var viewport = Tree(flyout).OfType<ScrollViewer>().Single();
            var offset = viewport.VerticalOffset;
            if (offset <= 0) throw new InvalidOperationException("矮托盘滚动样例没有建立");
            app.UpdateSettings(s => s.DirectResults = !s.DirectResults);
            await Task.Delay(500); await Settled();
            bottom = Find<Button>("tray-action-quit");
            var bounds = bottom.TransformToAncestor(viewport).TransformBounds(new System.Windows.Rect(bottom.RenderSize));
            if (!bottom.IsKeyboardFocused || Math.Abs(viewport.VerticalOffset - offset) > 1
                || bounds.Top < -1 || bounds.Bottom > viewport.ActualHeight + 1)
                throw new InvalidOperationException("矮托盘刷新后焦点、滚动位置或底部入口可见性改变");
            await Task.Delay(200); shot("tray-controls-short", flyout);

            T Find<T>(string id) where T : DependencyObject => Tree(flyout).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
            ToggleButton Toggle(string id) => Find<ToggleButton>("tray-toggle-" + id);
            void AssertState(string id, bool on)
            {
                var button = Toggle(id);
                var text = Find<TextBlock>("tray-state-" + id).Text;
                if (button.IsChecked != on || text != (on ? "已开启" : "已暂停")
                    || !AutomationProperties.GetName(button).Contains(text, StringComparison.Ordinal)
                    || PeerToggle(button).ToggleState != (on ? ToggleState.On : ToggleState.Off))
                    throw new InvalidOperationException($"托盘可见状态与实际辅助状态不一致：{id}");
            }
            static IToggleProvider PeerToggle(ToggleButton button) =>
                new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle) as IToggleProvider
                ?? throw new InvalidOperationException("托盘开关未提供真实 Toggle 接口");
            static async Task Settled()
            {
                await Task.Delay(75);
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            }
        }
        finally
        {
            openedSettings?.Close(); if (flyout?.IsVisible == true) flyout.Close();
            app.UpdateSettings(s => { s.Enabled = enabled; s.TriggerMode = mode; s.DirectResults = direct; s.ClipboardHistory = recording; });
            app.FlushPendingSettings();
        }
        if (app.Settings.ToJson() != before) throw new InvalidOperationException("托盘演示没有恢复原偏好");
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
