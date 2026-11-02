using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

internal sealed partial class TextDiffWindow
{
    internal static async Task VerifyInputBoundaryAsync(App app, Action<string, TextDiffWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("文字对比边界检查仅用于演示回归");
        // 不显示超大文本窗口：只检查真实构造入口有没有先把超限全文交给编辑器。
        var window = new TextDiffWindow(new string('x', TextDiff.MaximumCharacters + 1), "保留的对照");
        try
        {
            if (window.before.Text.Length != 0 || window.compare.IsEnabled)
                throw new InvalidOperationException("超限初始全文被载入编辑器，或仍允许把未载入内容当作空白比较");
            window.Show(); await Task.Delay(200);
            if (!window.status.Text.Contains("尚未载入", StringComparison.Ordinal) || window.after.Text != "保留的对照")
                throw new InvalidOperationException("初始超限没有说明，或丢失了可用的一侧");
            shot("diff-input-initial-limit", window);

            window.after.Text = "只修改对照";
            window.ignore.IsChecked = true;
            if (window.compare.IsEnabled) throw new InvalidOperationException("另一侧或空白规则变化错误解除了未载入限制");
            window.before.Text = "已补充原文";
            await Compare();
            if (!window.output.Text.Contains("已补充原文", StringComparison.Ordinal)
                || !window.output.Text.Contains("只修改对照", StringComparison.Ordinal))
                throw new InvalidOperationException("补充有效输入后未能重新比较完整内容");
            await Task.Delay(200); shot("diff-input-recovered", window);

            var retained = window.before.Text;
            var result = window.output.Text;
            var large = new DataObject(DataFormats.UnicodeText, new string('p', TextDiff.MaximumCharacters));
            var rejected = new DataObjectPastingEventArgs(large, false, DataFormats.UnicodeText);
            window.before.RaiseEvent(rejected); // 检查实际粘贴事件的取消，不执行系统剪贴板粘贴。
            if (!rejected.CommandCancelled || window.before.Text != retained || window.output.Text != result
                || !window.status.Text.Contains("原输入已保留", StringComparison.Ordinal))
                throw new InvalidOperationException("超限粘贴事件没有取消或破坏了原输入和结果");
            await Task.Delay(200); shot("diff-input-paste-limit", window);
            var allowed = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "短内容"), false, DataFormats.UnicodeText);
            window.before.RaiseEvent(allowed);
            if (allowed.CommandCancelled) throw new InvalidOperationException("正常粘贴事件被错误取消");

            window.before.Text = "修改后的输入";
            if (window.output.Text.Length != 0 || window.copy.IsEnabled)
                throw new InvalidOperationException("输入已修改但旧差异仍显示或可复制");
            await Task.Delay(200); shot("diff-input-stale-cleared", window);
            await Compare();
            window.ignore.IsChecked = false;
            if (window.output.Text.Length != 0 || window.copy.IsEnabled)
                throw new InvalidOperationException("比较规则变化未清除旧差异");

            async Task Compare()
            {
                if (!window.compare.IsEnabled || new ButtonAutomationPeer(window.compare).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                    throw new InvalidOperationException("有效输入的比较按钮不可调用");
                invoke.Invoke();
                var watch = Stopwatch.StartNew();
                while (!window.copy.IsEnabled && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(25);
                if (!window.copy.IsEnabled || window.output.Text.Length == 0) throw new InvalidOperationException("真实比较入口没有提交新结果");
            }
        }
        finally { window.Close(); }

        var lines = new TextDiffWindow(string.Join('\n', Enumerable.Repeat("x", TextDiff.MaximumLines + 1)), "行数检查");
        try
        {
            if (lines.before.Text.Length != 0 || lines.compare.IsEnabled || !lines.status.Text.Contains("20000", StringComparison.Ordinal))
                throw new InvalidOperationException("超限初始行数没有在编辑器前拒绝");
        }
        finally { lines.Close(); }
    }
}
