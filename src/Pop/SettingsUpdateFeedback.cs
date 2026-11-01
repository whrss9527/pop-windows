using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private TextBlock? updateStatus;
    private ProgressBar? updateProgress;
    private WpfUi.Button? updateAction;

    private void AddUpdateFeedback(StackPanel names, Grid hero)
    {
        updateStatus = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        updateStatus.Margin = new Thickness(0, 6, 12, 0);
        updateStatus.TextWrapping = TextWrapping.Wrap;
        updateStatus.MaxHeight = 64;
        updateStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAutomationId(updateStatus, "update-status");
        names.Children.Add(updateStatus);
        updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 8, 20, 0) };
        updateProgress.SetResourceReference(ForegroundProperty, "AccentFillColorDefaultBrush");
        AutomationProperties.SetAutomationId(updateProgress, "update-progress");
        AutomationProperties.SetName(updateProgress, "更新进度");
        names.Children.Add(updateProgress);
        updateAction = ActionButton("检查更新", Symbol.ArrowSync24, async () =>
        {
            if (app.Updater.IsBusy) return;
            if (app.Updater.Available is { } release) { await app.InstallUpdateAsync(release); return; }
            updateMessage = null;
            try
            {
                var found = await app.Updater.CheckAsync(userInitiated: true);
                updateMessage = found is null ? $"{Updater.CurrentVersion} 已是最新版本" : null;
            }
            catch (Pop.Core.UpdateException e) { updateMessage = e.Message; }
            RefreshUpdateFeedback();
        });
        updateAction.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(updateAction, "update-action");
        Grid.SetColumn(updateAction, 2); hero.Children.Add(updateAction);
        RefreshUpdateFeedback();
    }

    private void RefreshUpdateFeedback()
    {
        if (updateStatus is null || updateProgress is null || updateAction is null) return;
        var updater = app.Updater;
        var status = updater.Status ?? (updater.Available is { } release ? $"可以更新到 {release.Version}" : updateMessage);
        if (updater.IsBusy && updater.DownloadFraction is { } fraction)
            status = $"{updater.Status} {Math.Floor(fraction * 100):0}%";
        updateStatus.Text = status ?? "";
        updateStatus.ToolTip = status;
        updateStatus.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        updateProgress.Visibility = updater.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        updateProgress.IsIndeterminate = updater.DownloadFraction is null;
        updateProgress.Value = updater.DownloadFraction.GetValueOrDefault() * 100;
        updateAction.IsEnabled = !updater.IsBusy;
        updateAction.Content = updater.IsBusy ? updater.Available is null ? "正在检查…" : "正在更新…"
            : updater.Available is { } available ? $"更新到 {available.Version}" : "检查更新";
        updateAction.Appearance = updater.Available is null ? WpfUi.ControlAppearance.Secondary : WpfUi.ControlAppearance.Primary;
        updateAction.Icon = new WpfUi.SymbolIcon { Symbol = updater.Available is null ? Symbol.ArrowSync24 : Symbol.ArrowDownload24, FontSize = 16 };
    }
}
