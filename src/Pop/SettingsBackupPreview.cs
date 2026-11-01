using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    internal WpfUi.MessageBox CreateBackupPreview(SettingsBackup backup)
    {
        var preview = BackupPreview.Compare(backup, Settings, app.Plugins.Manifests);
        var content = new DockPanel { LastChildFill = true, MaxHeight = Math.Max(200, Math.Min(420, SystemParameters.WorkArea.Height - 260)) };
        var metadata = Note(backup.AppVersion is { Length: > 0 } version
            ? $"备份来自 Pop {TextPreview.SingleLine(version, 48)} · 导入前自动备份当前配置"
            : "导入前自动备份当前配置");
        DockPanel.SetDock(metadata, Dock.Top); content.Children.Add(metadata);
        var body = new StackPanel();
        body.Children.Add(Label(preview.HasChanges ? $"{preview.Settings.Count} 组设置变化 · 新增 {preview.AddedPlugins} 个插件 · 更新 {preview.UpdatedPlugins} 个插件" : "配置内容相同，无需重复导入", Theme.Body, "TextFillColorPrimaryBrush", FontWeights.SemiBold));
        foreach (var change in preview.Settings)
        {
            var row = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Label(change.Title, Theme.Body, "TextFillColorPrimaryBrush", FontWeights.SemiBold));
            row.Children.Add(Wrapped("当前：" + change.Before, "TextFillColorSecondaryBrush"));
            row.Children.Add(Wrapped("导入：" + change.After, "TextFillColorPrimaryBrush"));
            body.Children.Add(row);
        }
        var changedPlugins = preview.Plugins.Where(p => p.Kind != BackupPluginChangeKind.Unchanged).ToArray();
        if (changedPlugins.Length > 0)
        {
            var heading = Label("插件变化", Theme.Body, "TextFillColorPrimaryBrush", FontWeights.SemiBold);
            heading.Margin = new Thickness(0, 16, 0, 4); body.Children.Add(heading);
            string Description(BackupPluginChange plugin) => $"{(plugin.Kind == BackupPluginChangeKind.Added ? "新增" : "更新")} · {TextPreview.SingleLine(plugin.Name, 64)}（{TextPreview.SingleLine(plugin.Id, 64)}）";
            if (changedPlugins.Length <= 20)
                foreach (var plugin in changedPlugins) body.Children.Add(Wrapped(Description(plugin), "TextFillColorPrimaryBrush"));
            else
                body.Children.Add(new WpfUi.TextBox { Text = string.Join("\n", changedPlugins.Select(Description)), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = Theme.Caption });
        }
        body.Children.Add(Note($"保留 {preview.PreservedPlugins} 个备份之外的已有插件，{preview.UnchangedPlugins} 个同内容插件保持原样。"));
        var footer = Wrapped("凭据、剪贴板历史和开机启动状态不变。导入不会运行脚本，使用前请在功能页检查插件内容。", "TextFillColorSecondaryBrush");
        footer.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        content.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var dialog = new WpfUi.MessageBox { Owner = this, Title = "预览导入变化", PrimaryButtonText = preview.HasChanges ? "确认导入" : "仍然导入", CloseButtonText = "取消", Content = content, Width = 620, MaxWidth = 660, FontFamily = Theme.TextFont, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        FitToWorkArea(dialog);
        return dialog;
    }

    private static TextBlock Wrapped(string text, string resource)
    {
        var label = Label(text, Theme.Caption, resource);
        label.TextWrapping = TextWrapping.Wrap; label.Margin = new Thickness(0, 3, 0, 0);
        return label;
    }

    internal static bool BackupScopeIsVisible(WpfUi.MessageBox dialog)
    {
        if (dialog.Content is not DockPanel content) return false;
        var footer = content.Children.OfType<TextBlock>().FirstOrDefault(child => DockPanel.GetDock(child) == Dock.Bottom);
        if (footer is null || !footer.IsVisible || footer.ActualHeight <= 0) return false;
        var inContent = footer.TranslatePoint(new Point(), content);
        var inDialog = footer.TranslatePoint(new Point(), dialog);
        return inContent.Y >= 0 && inContent.Y + footer.ActualHeight <= content.ActualHeight + 1
            && inDialog.Y + footer.ActualHeight < dialog.ActualHeight - 48;
    }
}
