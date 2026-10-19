using Pop.Core;

namespace Pop;

internal sealed partial class Coordinator
{
    private readonly SelectionToolbar toolbar = new();
    private (Selection Selection, IntPtr Target, int X, int Y)? toolbarSelection;
    private volatile bool waitingForToolbar;
    private long toolbarVersion;

    private void SetUpSelectionToolbar()
    {
        hook.SelectionRequested += (x, y, target, version) => dispatcher.BeginInvoke(async () => await TryShowToolbar(x, y, target, version));
        hook.PointerActivity += (x, y, scroll) =>
        {
            if (toolbar.IsOpen) dispatcher.BeginInvoke(() =>
            {
                if (scroll || !toolbar.ContainsPhysical(x, y)) toolbar.Dismiss();
            });
        };
        toolbar.Chosen += async id =>
        {
            if (toolbarSelection is not { } last || Native.GetForegroundWindow() != last.Target
                || !settings().IsEnabled(id) || Actions.Find(id) is not { } action) return;
            Log.Info($"执行 {id}（选择工具条）");
            await Execute(action, ContentClassifier.Classify(last.Selection.Text), last.X, last.Y);
        };
        toolbar.MoreRequested += () =>
        {
            if (toolbarSelection is not { } last || Native.GetForegroundWindow() != last.Target) return;
            var content = ContentClassifier.Classify(last.Selection.Text);
            var s = new Session(last.X, last.Y, Task.FromResult(last.Selection), last.Selection.Process) { WaitingForClick = true };
            session = s; ShowRing(s, content); ring.SetClickable(true);
        };
    }

    public void SettingsUpdated()
    {
        toolbarVersion++;
        toolbar.Dismiss();
    }

    private async Task TryShowToolbar(int x, int y, IntPtr target, long activity)
    {
        bool CanShow() => hook.ActivityVersion == activity && settings().Enabled && settings().SelectionToolbar
            && Native.GetForegroundWindow() == target && !screenToolBusy && !selector.IsOpen && !colorPicker.IsOpen
            && !ringOpen && !cardOpen && !listOpen && !historyOpen;
        if (waitingForToolbar || !CanShow()) return;
        var version = toolbarVersion;
        waitingForToolbar = true;
        try
        {
            await Task.Delay(90); // 等目标 App 完成双击或拖选处理。
            if (!CanShow() || version != toolbarVersion) return;
            var selection = await reader.ReadPassiveAsync(target);
            if (!CanShow() || version != toolbarVersion || string.IsNullOrWhiteSpace(selection.Text)
                || settings().TriggerExcludedApps.Select(AppSettings.ApplicationName).Contains(AppSettings.ApplicationName(selection.Process))) return;
            var (_, scale) = Native.MonitorAt(x, y);
            // 没有选区位置时不冒险展示旧选区，拖动窗口和文件不应唤起文字工具。
            if (selection.Bounds is not { } bounds || !SelectionToolbarLogic.IsNear(bounds, x, y, scale)) return;
            var content = ContentClassifier.Classify(selection.Text);
            var ringItems = RingItems.For(RingItems.Build(settings().SlotsFor(selection.Process), settings().IsEnabled), content);
            var items = SelectionToolbarLogic.Items(ringItems, content);
            toolbarSelection = (selection, target, x, y);
            CloseCard(); CloseList(); CloseHistory();
            toolbar.ShowAt(x, y, items, bounds, target);
            Log.Info($"选择工具条：{items.Count} 个功能，{selection.Text.Length} 个字符");
        }
        catch (Exception e) { Log.Error("读取选择工具条内容失败", e); }
        finally { waitingForToolbar = false; }
    }
}
