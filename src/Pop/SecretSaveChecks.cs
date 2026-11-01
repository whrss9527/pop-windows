using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

internal static class SecretSaveChecks
{
    public static async Task VerifyAsync(App app, Action<string, SettingsWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("凭据保存检查仅用于演示回归");
        var preferences = app.Settings.ToJson();
        var engine = app.Settings.TranslationEngine;
        var directory = Path.Combine(Path.GetTempPath(), "pop-secret-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "secrets.json");
        using var scope = SecretStore.UseFileForDemo(file);
        const string original = "ci-original-placeholder";
        const string replacement = "ci-replacement-placeholder";
        SecretStore.Set(SecretStore.AzureTranslatorKey, original);
        SecretStore.Set("futureCredential", original);
        var before = File.ReadAllBytes(file);
        app.UpdateSettings(s => s.TranslationEngine = TranslationEngine.Azure);
        var window = app.ShowSettings("translate");
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var key = Find<WpfUi.PasswordBox>("translation-key");
            var save = Find<WpfUi.Button>("translation-key-save");
            var status = Find<TextBlock>("translation-key-status");
            key.Password = replacement;
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await InvokeSave();
                if (key.Password != replacement || !status.Text.Contains("保存失败", StringComparison.Ordinal))
                {
                    shot("secret-save-problem", window);
                    throw new InvalidOperationException("凭据写入失败后清空了输入或错误显示保存成功");
                }
                if (!File.ReadAllBytes(file).SequenceEqual(before)) throw new InvalidOperationException("保存失败改写了原凭据");
                await Task.Delay(200); shot("secret-save-failed", window);
            }
            await InvokeSave();
            if (key.Password.Length != 0 || status.Text != "Key 已保存" || SecretStore.Get(SecretStore.AzureTranslatorKey) != replacement)
                throw new InvalidOperationException("解除占用后重试未真正保存凭据");
            if (File.ReadAllText(file).Contains(replacement, StringComparison.Ordinal)) throw new InvalidOperationException("凭据以明文写入磁盘");
            if (SecretStore.Get("futureCredential") != original) throw new InvalidOperationException("保存当前 Key 覆盖了其他凭据");
            if (Directory.EnumerateFiles(directory, "*.tmp").Any()) throw new InvalidOperationException("保存失败遗留了本次临时文件");
            await Task.Delay(200); shot("secret-save-saved", window);

            window.Close();
            var damaged = System.Text.Encoding.UTF8.GetBytes("{\"azureTranslatorKey\": invalid-json");
            File.WriteAllBytes(file, damaged);
            window = app.ShowSettings("translate");
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            key = Find<WpfUi.PasswordBox>("translation-key");
            save = Find<WpfUi.Button>("translation-key-save");
            status = Find<TextBlock>("translation-key-status");
            if (!status.Text.Contains("无法读取", StringComparison.Ordinal)) throw new InvalidOperationException("损坏凭据被错误显示为尚未保存");
            await Task.Delay(200); shot("secret-read-unavailable", window);
            key.Password = replacement;
            await InvokeSave();
            if (key.Password != replacement || !status.Text.Contains("保存失败", StringComparison.Ordinal)
                || !File.ReadAllBytes(file).SequenceEqual(damaged))
                throw new InvalidOperationException("损坏凭据保存时丢失输入或改写了原文件");

            async Task InvokeSave()
            {
                if (new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                    throw new InvalidOperationException("凭据保存按钮不可调用");
                invoke.Invoke(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            }
            T Find<T>(string id) where T : DependencyObject
            {
                var pending = new Stack<DependencyObject>(); pending.Push(window);
                while (pending.TryPop(out var element))
                {
                    if (element is T found && AutomationProperties.GetAutomationId(element) == id) return found;
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
                        pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
                }
                throw new InvalidOperationException("找不到凭据保存控件：" + id);
            }
        }
        finally { window.Close(); app.UpdateSettings(s => s.TranslationEngine = engine); }
        if (app.Settings.ToJson() != preferences) throw new InvalidOperationException("凭据保存检查改写了偏好设置");
    }
}
