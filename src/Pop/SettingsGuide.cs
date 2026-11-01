using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private void ShowUsageGuide() => CreateUsageGuide().ShowDialog();

    private ToolWindow CreateUsageGuide()
    {
        var guide = new ToolWindow("Pop 使用指南", 720, 660)
        { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        NameScope.SetNameScope(guide, new NameScope());
        var root = new DockPanel { LastChildFill = true };
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 14, 0, 0) };
        var done = ActionButton("知道了", null, guide.Close, WpfUi.ControlAppearance.Primary);
        DockPanel.SetDock(done, Dock.Right); footer.Children.Add(done);
        var footerHint = Note("在设置中按 Ctrl+F 搜索功能，改动会自动保存。\n指南不会修改设置，可以随时重新打开。");
        footerHint.FontSize = Theme.Caption; footerHint.LineHeight = 18; footerHint.Margin = new Thickness(0, 0, 12, 0);
        guide.RegisterName("GuideFooterTip", footerHint); footer.Children.Add(footerHint);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        body.Children.Add(Label("让选中的内容直接可用", Theme.Subtitle, "TextFillColorPrimaryBrush", FontWeights.SemiBold));
        body.Children.Add(Note("从最常用的三个步骤开始，按自己的习惯调整。"));

        WpfUi.Button Go(string name, string title, string page)
        {
            var button = ActionButton(title, null, () => { guide.Close(); Navigate(page); Activate(); });
            button.Name = name;
            guide.RegisterName(name, button);
            return button;
        }
        void Step(Symbol icon, string title, TextBlock description, params UIElement[] actions)
        {
            var content = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            var heading = Horizontal(new WpfUi.SymbolIcon { Symbol = icon, FontSize = 19, Margin = new Thickness(0, 0, 10, 0) },
                Label(title, Theme.BodyLarge, "TextFillColorPrimaryBrush", FontWeights.SemiBold));
            content.Children.Add(heading);
            description.Margin = new Thickness(0, 8, 0, 12); content.Children.Add(description);
            var buttons = new WrapPanel();
            foreach (var action in actions)
            {
                if (action is FrameworkElement element) element.Margin = new Thickness(0, 0, 8, 4);
                buttons.Children.Add(action);
            }
            content.Children.Add(buttons); body.Children.Add(CardBorder(content));
        }

        var trigger = Note(""); var clipboard = Note("");
        guide.RegisterName("GuideTriggerHint", trigger);
        AutomationProperties.SetLiveSetting(trigger, AutomationLiveSetting.Polite);
        void RefreshHints()
        {
            trigger.Text = TriggerInstruction() + "\n" + (app.ShortcutText(KeyboardShortcut.RingTarget) is { } ringKey
                ? $"也可按 {ringKey} 打开圆盘，再点击或用方向键与回车选择。"
                : "圆盘快捷键暂不可用，可在快捷键页查看原因或重新设置。");
            clipboard.Text = !Settings.ClipboardHistory ? "剪贴板历史已关闭，可在剪贴板设置中按需启用。"
                : app.ShortcutText("clipboard") is { } key ? $"按 {key} 打开历史，按类型筛选、搜索或只看固定；Enter 粘贴。"
                : "剪贴板历史已开启，可从托盘面板打开，或在快捷键页设置可用组合键。";
        }
        RefreshHints(); app.SettingsChanged += RefreshHints;
        guide.Closed += (_, _) => app.SettingsChanged -= RefreshHints;
        Step(Symbol.CursorClick24, "01  选择内容，再唤起", trigger,
            Go("GuideOpenTriggers", "调整唤起", "triggers"), Go("GuideOpenShortcuts", "配置快捷键", "shortcuts"));
        Step(Symbol.Copy24, "02  处理结果", Note("选中算式、颜色等内容时可以直接显示结果，其他内容打开圆盘。结果可复制；提供「替换原文」的工具可写回原选区。复制失败时保留结果供重试。"),
            Go("GuideOpenRules", "调整直达规则", "rules"));
        Step(Symbol.ClipboardTextLtr24, "03  找回内容与调整偏好", clipboard,
            Go("GuideOpenClipboard", "剪贴板设置", "clipboard"), Go("GuideOpenBackup", "备份与迁移", "backup"));
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        guide.BodyContent(root);
        return guide;
    }

    private string TriggerInstruction()
    {
        if (!Settings.Enabled || Settings.TriggerMode == TriggerMode.Disabled) return "鼠标唤起已暂停，全局快捷键仍可使用。";
        return Settings.TriggerMode switch
        {
            TriggerMode.LongPressRight => $"当前长按右键 {Settings.HoldMilliseconds} 毫秒唤起；划向功能后松开执行。短按仍为原应用的右键菜单。",
            TriggerMode.MiddleClick => "当前点击鼠标中键唤起，再用鼠标点击或键盘选择功能。",
            _ => $"当前按住 {Settings.TriggerModifier switch { TriggerModifier.Control => "Ctrl", TriggerModifier.Windows => "Win", _ => Settings.TriggerModifier.ToString() }} 并点击右键唤起，再点击选择功能。",
        };
    }

    internal ToolWindow PreviewUsageGuide()
    {
        if (!app.IsDemo) throw new InvalidOperationException("使用指南预览仅用于演示");
        var guide = CreateUsageGuide(); guide.Show(); return guide;
    }
}
