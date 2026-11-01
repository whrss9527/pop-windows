using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private WpfUi.ToggleSwitch? startupToggle;
    private TextBlock? startupMessage;
    private DockPanel? startupFeedback;
    internal StartupPreferenceState StartupStatus { get; private set; } = new(null, null);

    private UIElement StartupCard()
    {
        startupToggle = new WpfUi.ToggleSwitch { OnContent = "开", OffContent = "关", LabelPosition = WpfUi.ElementPlacement.Left };
        AutomationProperties.SetName(startupToggle, "登录 Windows 时启动 Pop");
        startupToggle.Click += (_, _) =>
        {
            if (startupToggle.IsEnabled) ShowStartupState(startupPreference.Set(startupToggle.IsChecked == true));
        };
        var content = new StackPanel();
        content.Children.Add(Row(Symbol.Rocket24, "开机时启动", "登录 Windows 后在后台运行", startupToggle));
        startupFeedback = new DockPanel { Margin = new Thickness(56, 0, 16, 12) };
        var retry = ActionButton("重新读取", null, () => ShowStartupState(startupPreference.Read()));
        DockPanel.SetDock(retry, Dock.Right); startupFeedback.Children.Add(retry);
        startupMessage = Label("", Theme.Caption, "TextFillColorPrimaryBrush");
        startupMessage.Margin = new Thickness(0, 0, 12, 0); startupMessage.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(startupMessage, "开机启动状态说明");
        AutomationProperties.SetLiveSetting(startupMessage, AutomationLiveSetting.Polite);
        startupFeedback.Children.Add(startupMessage); content.Children.Add(startupFeedback);
        ShowStartupState(startupPreference.Read());
        return CardBorder(content);
    }

    private void ShowStartupState(StartupPreferenceState state)
    {
        StartupStatus = state;
        if (startupToggle is null || startupMessage is null || startupFeedback is null) return;
        startupToggle.IsChecked = state.Enabled;
        startupToggle.OffContent = state.Enabled is null ? "未知" : "关";
        startupToggle.IsEnabled = state.Enabled.HasValue;
        startupToggle.ToolTip = state.Enabled switch { true => "Windows 启动项已开启", false => "Windows 启动项已关闭", _ => "暂时无法确认 Windows 的实际状态" };
        startupFeedback.Visibility = state.Error is null ? Visibility.Collapsed : Visibility.Visible;
        startupMessage.Text = state.Enabled is null ? "无法确认实际状态，请重新读取。" : "操作未完成，开关已按 Windows 当前状态显示。";
        startupMessage.ToolTip = state.Error;
        if (state.Error is not null) Log.Info(state.Error);
    }

    internal bool StartupCanToggle => startupToggle?.IsEnabled == true;
    internal bool StartupErrorIsVisible => startupFeedback?.IsVisible == true;
    internal void PreviewStartupChange(bool enabled)
    {
        if (!app.IsDemo || !StartupCanToggle) throw new InvalidOperationException("启动项预览不可操作");
        startupToggle!.IsChecked = enabled;
        startupToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
    internal void PreviewStartupReload()
    {
        if (!app.IsDemo) throw new InvalidOperationException("启动项预览只用于演示回归");
        ShowStartupState(startupPreference.Read());
    }
}
