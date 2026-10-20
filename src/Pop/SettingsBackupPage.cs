using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private void BackupPage(StackPanel page)
    {
        page.Children.Add(Note("把圆盘、应用布局、唤起方式、选择工具条、直达规则、快捷键、常用短语、翻译和剪贴板设置，以及自己写的 JSON 插件保存到一个文件，方便换机或恢复。"));
        page.Children.Add(Card(Symbol.Save24, "导出设置与插件", "不包含翻译密钥、剪贴板历史和开机启动状态。插件脚本和网址会完整保留",
            ActionButton("导出…", null, ExportBackup)));
        page.Children.Add(Card(Symbol.FolderOpen24, "导入设置与插件", "先预览，再覆盖当前设置；同 ID 的插件会更新，其他插件保留。导入不会运行脚本",
            ActionButton("导入…", null, async () => await ImportBackup())));
        page.Children.Add(Card(Symbol.ArrowReset24, "导入前的自动备份", "每次导入前保存当前设置及插件，需要时可以重新导入",
            ActionButton("打开文件夹", null, () =>
            {
                var path = Path.Combine(Paths.RoamingData, "Backups"); Directory.CreateDirectory(path); OpenFolder(path);
            })));
        page.Children.Add(Note("设置备份用于 Windows 版之间迁移。macOS 的自定义 JSON 插件可在「功能 → 我的插件」单独导入；依赖平台的脚本需要调整。"));
    }

    private void ExportBackup()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出 Pop 设置", Filter = "Pop 设置备份 (*.json)|*.json", FileName = $"Pop 设置 {DateTime.Now:yyyy-MM-dd}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var json = new SettingsBackup(Settings, app.Plugins.Manifests, Updater.CurrentVersion.ToString()).ToJson();
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, dialog.FileName, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _ = ShowMessage("已导出", "设置与插件已保存。文件包含完整的自定义脚本和网址，请只分享给你信任的人。");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _ = ShowMessage("没有导出", e.Message); }
    }

    private async Task ImportBackup()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入 Pop 设置", Filter = "Pop 设置备份 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > SettingsBackup.MaximumBytes) throw new InvalidDataException("备份文件超过 16 MB");
            var backup = SettingsBackup.FromJson(File.ReadAllText(dialog.FileName));
            var confirm = new WpfUi.MessageBox
            {
                Owner = this, Title = "导入设置与插件？", PrimaryButtonText = "导入", CloseButtonText = "取消",
                Content = $"将覆盖当前圆盘、唤起、直达规则、快捷键、常用短语（{backup.Settings.Snippets.Count} 条）、翻译和剪贴板设置，并导入 {backup.Plugins.Count} 个插件。同 ID 的已有插件会更新，其他插件保留。\n\n导入前会自动备份。导入不会运行脚本；使用导入的插件前，请在功能页检查脚本内容。",
            };
            if (await confirm.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;
            var recovery = app.ImportBackup(backup);
            await ShowMessage("已导入", $"设置已生效，导入前的备份保存在：\n{recovery}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PluginStoreException or AggregateException)
        { await ShowMessage("没有导入", e.Message); }
    }
}
