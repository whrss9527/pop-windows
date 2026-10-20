using Pop.Core;

namespace Pop;

/// 让旧任务故意迟到，验证真实编辑器的进度、结果、停止及复制完整输出。
internal static class PluginExperienceChecks
{
    public static async Task VerifyAsync(App app)
    {
        var processes = new PendingProcesses();
        var manifest = new PluginManifest { Id = "user-editor-check", Name = "试运行检查",
            Action = new PluginAction { Type = PluginActionType.Shell, Script = "Write-Output test" } };
        var editor = new PluginEditorWindow(app, manifest, true, new PluginRunner(processes));
        using var clipboard = new ClipboardAccess("Pop 插件检查剪贴板");
        var saved = clipboard.Save();
        try
        {
            editor.Show();
            var first = editor.RunTestAsync();
            if (processes.Calls.Count != 1) throw new InvalidOperationException("首次试运行没有启动");
            var second = editor.RunTestAsync();
            if (processes.Calls.Count != 2 || !processes.Calls[0].Cancellation.IsCancellationRequested)
                throw new InvalidOperationException("再次运行没有取消旧任务");
            processes.Calls[0].Completion.SetResult(new(0, "old", "", false));
            await first;
            if (!editor.IsTestRunning || !editor.IsTestProgressVisible || editor.TestOutput.Length > 0)
                throw new InvalidOperationException("旧任务覆盖了新试运行的进度或结果");
            var output = new string('x', 8000);
            processes.Calls[1].Completion.SetResult(new(0, output, "", false));
            await second;
            if (editor.IsTestRunning || editor.IsTestProgressVisible || editor.TestOutput.Length > 4001 || !editor.CopyTestResult() || clipboard.GetText() != output)
                throw new InvalidOperationException("输出预览、完成状态或复制完整结果错误");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            var resultView = editor.TestResultViewport;
            System.Windows.DependencyObject? ancestor = resultView;
            do { ancestor = System.Windows.Media.VisualTreeHelper.GetParent(ancestor); }
            while (ancestor is not null && ancestor is not System.Windows.Controls.ScrollViewer);
            if (ancestor is not System.Windows.Controls.ScrollViewer page)
                throw new InvalidOperationException("找不到插件编辑器页面视口");
            var resultBounds = resultView.TransformToAncestor(page).TransformBounds(new System.Windows.Rect(resultView.RenderSize));
            if (resultBounds.Top < -1 || resultBounds.Bottom > page.ActualHeight + 1)
                throw new InvalidOperationException("试运行结果被页面边缘或保存栏遮挡");
            var third = editor.RunTestAsync(); editor.StopTest();
            if (!processes.Calls[2].Cancellation.IsCancellationRequested || editor.IsTestRunning || editor.TestOutput != "已停止")
                throw new InvalidOperationException("停止试运行没有生效");
            processes.Calls[2].Completion.SetResult(new(0, "late", "", false)); await third;
            if (editor.TestOutput != "已停止") throw new InvalidOperationException("停止后的旧结果重新出现");
            var fourth = editor.RunTestAsync(); editor.Close();
            if (!processes.Calls[3].Cancellation.IsCancellationRequested) throw new InvalidOperationException("关闭编辑器没有取消任务");
            processes.Calls[3].Completion.SetResult(new(0, "closed", "", false)); await fourth;
            if (editor.IsTestRunning || editor.TestOutput.Length > 0) throw new InvalidOperationException("关闭后仍更新编辑器");
        }
        finally
        {
            foreach (var call in processes.Calls) call.Completion.TrySetResult(new(0, "cleanup", "", false));
            editor.Close(); clipboard.Restore(saved);
        }
        await VerifyLengthRulesAsync(app);
    }

    private static async Task VerifyLengthRulesAsync(App app)
    {
        var processes = new PendingProcesses();
        var manifest = new PluginManifest { Name = "长度条件检查", Match = new() { MinLength = 2, MaxLength = 3 },
            Action = new() { Type = PluginActionType.Shell, Script = "Write-Output test" } };
        var editor = new PluginEditorWindow(app, manifest, true, new PluginRunner(processes));
        try
        {
            editor.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (editor.CurrentManifest.Match != manifest.Match) throw new InvalidOperationException("导入插件的长度条件没有显示在编辑器中");
            async Task Trial(string input)
            {
                editor.TestInput = input;
                var count = processes.Calls.Count;
                var run = editor.RunTestAsync();
                if (processes.Calls.Count != count + 1) throw new InvalidOperationException("长度条件试运行没有启动");
                processes.Calls[^1].Completion.SetResult(new(0, "ok", "", false));
                await run;
            }
            await Trial("👩‍💻");
            if (!editor.TestWarning.Contains("至少需要 2")) throw new InvalidOperationException("过短输入没有解释长度条件");
            await Trial("👩‍💻🇨🇳");
            if (editor.TestWarning.Length > 0) throw new InvalidOperationException("完整 emoji 被误算为多个可见字符");
            await Trial("1234");
            if (!editor.TestWarning.Contains("最多允许 3")) throw new InvalidOperationException("过长输入没有解释长度条件");
            editor.MaximumLengthInput.Text = "2";
            if (editor.CurrentManifest.Match.MaxLength != 2) throw new InvalidOperationException("尚未离开数字框的新内容没有进入运行和保存草稿");
            editor.MinimumLengthInput.Text = "2.0";
            await Trial("👩‍💻🇨🇳");
            if (editor.MinimumLengthInput.Value != 2 || editor.MinimumLengthInput.Text != "2" || editor.TestWarning.Length > 0)
                throw new InvalidOperationException("试运行没有提交数字框，按键抬起时可能取消新任务");
            editor.MinimumLengthInput.Text = "3";
            var previous = processes.Calls.Count;
            await editor.RunTestAsync();
            if (processes.Calls.Count != previous || editor.TestOutput != "最多字符不能少于最少字符")
                throw new InvalidOperationException("反向长度范围仍启动了脚本");
            editor.MinimumLengthInput.Clear(); editor.MaximumLengthInput.Clear();
            if (editor.CurrentManifest.Match.MinLength is not null || editor.CurrentManifest.Match.MaxLength is not null)
                throw new InvalidOperationException("长度条件清除后仍保留隐藏限制");
            editor.TestInput = "hello";
            var pending = editor.RunTestAsync();
            editor.MaximumLengthInput.Text = "2";
            if (editor.IsTestRunning || !processes.Calls[^1].Cancellation.IsCancellationRequested || editor.TestWarning.Length > 0)
                throw new InvalidOperationException("编辑长度条件没有取消旧任务和清除旧提示");
            processes.Calls[^1].Completion.SetResult(new(0, "late bounds", "", false)); await pending;
            if (editor.TestOutput == "late bounds") throw new InvalidOperationException("旧长度条件的结果重新出现");
        }
        finally
        {
            foreach (var call in processes.Calls) call.Completion.TrySetResult(new(0, "cleanup", "", false));
            editor.Close();
        }
    }

    private sealed class PendingProcesses : IProcessRunner
    {
        public List<(TaskCompletionSource<ProcessOutput> Completion, CancellationToken Cancellation)> Calls { get; } = [];
        public Task<ProcessOutput> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<ProcessOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Add((completion, cancellationToken));
            return completion.Task;
        }
    }
}
