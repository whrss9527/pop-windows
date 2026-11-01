using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pop.Core;

namespace Pop;

/// 在真实设置窗口内验证定位、短窗口滚动、键盘接续及只读导航。
internal static class SettingsSearchExperienceChecks
{
    public static async Task VerifyAsync(App app, Action<string, SettingsWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("设置定位检查仅用于演示回归");
        var before = app.Settings.ToJson();
        var mode = app.Settings.TriggerMode;
        var originalExcluded = app.Settings.ClipboardExcludedApps;
        var pending = app.SettingsSavePending;
        var window = new SettingsWindow(app) { Height = 520 };
        window.Show();
        try
        {
            await Task.Delay(250);
            foreach (var entry in SettingsCatalog.Entries)
            {
                await Open(entry.Id);
                AssertReadOnly();
            }

            window.PreviewSettingsSearch("剪贴板");
            await FocusSearch();
            SendKey(Key.Down);
            if (window.FocusedSearchResult != 0) throw new InvalidOperationException("下键未选择首项");
            SendKey(Key.Down);
            if (window.FocusedSearchResult != 1) throw new InvalidOperationException("下键未选择下一项");
            SendKey(Key.Up); SendKey(Key.Up);
            if (!window.SettingsSearchFocused) throw new InvalidOperationException("上键未回到搜索框");
            SendKey(Key.Enter);
            if (!await window.PendingSearchNavigation) throw new InvalidOperationException("回车未定位搜索结果");
            await Task.Delay(45); AssertVisible(SettingsCatalog.Search("剪贴板")[0].Id);

            await Open("privacy");
            shot("settings-search-privacy", window);
            if (Keyboard.FocusedElement is not UIElement focused || !focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))
                || Keyboard.FocusedElement is not TextBox)
                throw new InvalidOperationException("定位后 Tab 未进入隐私编辑项");
            window.PreviewSettingsSearch("每个应用的独立圆盘");
            AssertReadOnly();
            if (!window.OpenFirstSearchResult() || !await window.PendingSearchNavigation)
                throw new InvalidOperationException("应用圆盘搜索未完成");
            await Task.Delay(60);
            if (window.SearchTargetPosition().Height < 100)
                throw new InvalidOperationException("定位应用圆盘未展开管理区");
            AssertVisible("app-ring");
            shot("settings-search-app-ring", window);

            app.Settings.TriggerMode = TriggerMode.MiddleClick;
            await Open("trigger");
            if (app.Settings.TriggerMode != TriggerMode.MiddleClick)
                throw new InvalidOperationException("搜索长按时间改变了唤起方式");
            shot("settings-search-trigger-condition", window);
            app.Settings.TriggerMode = mode;
            AssertReadOnly();

            // 老配置中的扩展名、顺序与重复项，未经编辑时不应因浏览而被规范化。
            app.Settings.ClipboardExcludedApps = ["Legacy.exe", "Legacy", "KeePass"];
            var legacy = app.Settings.ToJson();
            await Open("privacy");
            if (Keyboard.FocusedElement is not UIElement legacyFocus || !legacyFocus.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))
                || Keyboard.FocusedElement is not TextBox)
                throw new InvalidOperationException("无法聚焦旧隐私排除项");
            await FocusSearch();
            if (app.Settings.ToJson() != legacy || app.SettingsSavePending != pending)
                throw new InvalidOperationException("未编辑的旧排除项被浏览操作更改");
            app.Settings.ClipboardExcludedApps = originalExcluded;
            AssertReadOnly();

            // 同一轮布局等待期间再搜索，旧任务不得把焦点抢回旧页面。
            window.PreviewSettingsSearch("不记录哪些应用");
            window.OpenFirstSearchResult();
            var old = window.PendingSearchNavigation;
            await Open("plugins");
            if (await old || window.CurrentSearchTarget != "plugins")
                throw new InvalidOperationException("旧定位任务未取消");

            await Task.Delay(2300);
            var position = window.SearchTargetPosition();
            if (position.Highlighted || !position.Focused || window.CurrentSearchTarget != "plugins")
                throw new InvalidOperationException("定位强调层未及时消退或消退时改变了焦点");

            window.Navigate("clipboard");
            window.PreviewSettingsScroll(260); await Task.Delay(60);
            var offset = window.SettingsScrollOffset;
            window.PreviewSettingsSearch("不存在的设置 no-setting-matches");
            if (window.OpenFirstSearchResult()) throw new InvalidOperationException("无匹配时仍可打开设置");
            await FocusSearch();
            SendKey(Key.Escape); await Task.Delay(60);
            if (window.CurrentPage != "clipboard" || Math.Abs(window.SettingsScrollOffset - offset) > 1 || window.CurrentSearchTarget is not null)
                throw new InvalidOperationException("清空搜索未恢复原滚动位置或遗留强调层");
            AssertReadOnly();

            window.PreviewSettingsSearch("不记录哪些应用"); window.OpenFirstSearchResult();
            var closing = window.PendingSearchNavigation;
            window.Close();
            if (await closing || window.CurrentSearchTarget is not null)
                throw new InvalidOperationException("关闭窗口后旧定位仍在继续");
            AssertReadOnly();
        }
        finally { app.Settings.TriggerMode = mode; app.Settings.ClipboardExcludedApps = originalExcluded; window.Close(); }

        async Task Open(string id)
        {
            var entry = SettingsCatalog.Entries.Single(e => e.Id == id);
            window.PreviewSettingsSearch(entry.Title);
            if (!window.OpenFirstSearchResult() || !await window.PendingSearchNavigation)
                throw new InvalidOperationException($"未能从搜索定位设置：{id}");
            await Task.Delay(45);
            AssertVisible(id);
        }
        void AssertVisible(string id)
        {
            var position = window.SearchTargetPosition();
            if (window.CurrentSearchTarget != id || position.Height <= 0 || !double.IsFinite(position.Top)
                || position.Top < -1 || position.Top + Math.Min(64, position.Height) > position.Viewport + 1
                || !position.Focused || !position.Highlighted)
                throw new InvalidOperationException($"设置定位不可见或未聚焦：{id} {position}");
        }
        void AssertReadOnly()
        {
            if (app.Settings.ToJson() != before || app.SettingsSavePending != pending)
                throw new InvalidOperationException("搜索或键盘定位改变了设置或产生了待保存更改");
        }
        void SendKey(Key key)
        {
            if (Keyboard.FocusedElement is not UIElement focused) throw new InvalidOperationException("设置窗口没有键盘焦点");
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            focused.RaiseEvent(e);
            if (!e.Handled) throw new InvalidOperationException($"设置搜索未处理按键：{key}");
        }
        async Task FocusSearch()
        {
            window.Activate();
            if (!InputInjector.CtrlChord(0x46)) throw new InvalidOperationException("无法发送设置搜索快捷键");
            var deadline = Environment.TickCount64 + 2500;
            while (!window.SettingsSearchFocused && Environment.TickCount64 < deadline) await Task.Delay(25);
            if (!window.SettingsSearchFocused) throw new InvalidOperationException("Ctrl+F 未聚焦设置搜索");
        }
    }
}
