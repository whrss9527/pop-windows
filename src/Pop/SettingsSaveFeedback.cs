using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using WpfUi = Wpf.Ui.Controls;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private TextBlock? saveStatusText;
    private FrameworkElement? retrySave;
    private Border? recoveryPanel;
    private TextBlock? recoveryTitle, recoveryMessage;
    private WpfUi.Button? reloadSettings, recoveryFolder;

    private UIElement SaveStatus()
    {
        var status = new StackPanel { Margin = new Thickness(264, 8, 24, 12) };
        var recovery = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };
        reloadSettings = ActionButton("重新读取", null, async () => await ReloadSettingsFromFile());
        recoveryFolder = ActionButton("打开配置目录", null, () => OpenFolder(Path.GetDirectoryName(app.SettingsRecoveryPath ?? app.SettingsFilePath)!));
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(reloadSettings); actions.Children.Add(recoveryFolder);
        reloadSettings.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(actions, Dock.Right); recovery.Children.Add(actions);
        var explanation = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        recoveryTitle = Label("", Theme.Body, "TextFillColorPrimaryBrush", FontWeights.SemiBold);
        recoveryMessage = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        recoveryMessage.Margin = new Thickness(0, 4, 0, 0);
        explanation.Children.Add(recoveryTitle); explanation.Children.Add(recoveryMessage); recovery.Children.Add(explanation);
        recoveryPanel = CardBorder(recovery); status.Children.Add(recoveryPanel);
        AutomationProperties.SetName(recoveryPanel, "设置文件恢复提示");
        AutomationProperties.SetLiveSetting(recoveryMessage, AutomationLiveSetting.Polite);

        var row = new DockPanel { LastChildFill = true };
        status.Children.Add(row);
        retrySave = ActionButton("重试保存", Symbol.Save24, () => app.RetrySaveSettings());
        retrySave.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(retrySave, Dock.Right); row.Children.Add(retrySave);
        saveStatusText = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        saveStatusText.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(saveStatusText, "设置保存状态");
        AutomationProperties.SetLiveSetting(saveStatusText, AutomationLiveSetting.Polite);
        row.Children.Add(saveStatusText);
        OnSettingsSaveStateChanged();
        return status;
    }

    private void OnSettingsSaveStateChanged()
    {
        ShowSaveState(app.SettingsSaveError, app.SettingsHaveBeenSaved, app.SettingsSavePending);
        if (recoveryPanel is null || recoveryTitle is null || recoveryMessage is null || reloadSettings is null || recoveryFolder is null) return;
        recoveryPanel.Visibility = app.SettingsRecoveryMessage is null ? Visibility.Collapsed : Visibility.Visible;
        recoveryTitle.Text = app.SettingsReadProblem ? "原设置需要处理" : "已保留原文件副本";
        recoveryMessage.Text = app.SettingsRecoveryMessage ?? "";
        recoveryPanel.ToolTip = app.SettingsReadDetail ?? app.SettingsRecoveryPath;
        reloadSettings.Visibility = app.SettingsReadProblem ? Visibility.Visible : Visibility.Collapsed;
        recoveryFolder.Content = app.SettingsRecoveryPath is null ? "打开配置目录" : "打开副本目录";
    }

    private async Task ReloadSettingsFromFile()
    {
        if (app.SettingsSavePending || app.SettingsSaveError is not null)
        {
            var confirm = new WpfUi.MessageBox { Owner = this, Title = "重新读取设置？", FontFamily = Theme.TextFont,
                Content = "读取成功后会替换当前尚未保存的改动；原文件不会被修改。", PrimaryButtonText = "重新读取", CloseButtonText = "取消" };
            if (await confirm.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;
        }
        if (!app.ReloadSettings())
            await ShowMessage("暂时无法重新读取", "文件仍不可用，当前改动已保留。可修复文件后再读取，或保存当前配置；保存前会先保留原文件副本。\n" + app.SettingsSaveError);
    }

    internal bool RecoveryNoticeIsVisible => recoveryPanel is { IsVisible: true, ActualHeight: > 0 }
        && recoveryPanel.TranslatePoint(new Point(), this).Y + recoveryPanel.ActualHeight <= ActualHeight - 8;

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
