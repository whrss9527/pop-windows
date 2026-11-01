using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private TextBlock? saveStatusText;
    private FrameworkElement? retrySave;

    private UIElement SaveStatus()
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(264, 8, 24, 12) };
        retrySave = ActionButton("重试保存", Symbol.Save24, () => app.RetrySaveSettings());
        retrySave.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(retrySave, Dock.Right); row.Children.Add(retrySave);
        saveStatusText = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        saveStatusText.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(saveStatusText, "设置保存状态");
        AutomationProperties.SetLiveSetting(saveStatusText, AutomationLiveSetting.Polite);
        row.Children.Add(saveStatusText);
        OnSettingsSaveStateChanged();
        return row;
    }

    private void OnSettingsSaveStateChanged() => ShowSaveState(app.SettingsSaveError, app.SettingsHaveBeenSaved, app.SettingsSavePending);

    private void ShowSaveState(string? error, bool saved, bool pending = false)
    {
        if (saveStatusText is null || retrySave is null) return;
        saveStatusText.Text = pending ? "待保存 · 改动已立即生效" : error is null ? saved ? "已保存 · 改动会自动保存" : "改动会自动保存"
            : "尚未保存 · 改动仅在本次运行生效，请重试";
        saveStatusText.ToolTip = error;
        saveStatusText.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        retrySave.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void PreviewSaveFailure()
    {
        if (app.IsDemo) ShowSaveState("演示：设置目录暂时不可写", false);
    }

    internal void PreviewSavePending(bool show)
    {
        if (!app.IsDemo) return;
        if (show) ShowSaveState(null, false, pending: true);
        else OnSettingsSaveStateChanged();
    }
}
