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
