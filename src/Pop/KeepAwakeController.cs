using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pop;

/// 请求绑定在界面线程；停止、定时结束、退出时都在同一线程释放，退出进程后系统也会释放。
internal sealed class KeepAwakeController : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private KeepAwakeWindow? window;
    private long started;
    private TimeSpan? duration;
    private TimeSpan? Remaining => duration is { } limit ? limit - Stopwatch.GetElapsedTime(started) : null;
    public bool IsActive { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    public KeepAwakeController()
    {
        timer.Tick += (_, _) =>
        {
            if (Remaining is { } remaining && remaining <= TimeSpan.Zero) Stop();
            Changed?.Invoke();
        };
    }
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint state);
    public bool Start(int? minutes)
    {
        if (minutes is not (null or 30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (SetThreadExecutionState(0x80000003) == 0)
        { Error = "Windows 未接受保持唤醒请求，请重试"; Changed?.Invoke(); return false; }
        IsActive = true; started = Stopwatch.GetTimestamp();
        duration = minutes is { } value ? TimeSpan.FromMinutes(value) : null;
        EndsAt = duration is { } limit ? DateTimeOffset.UtcNow + limit : null;
        Error = null; timer.Start(); Changed?.Invoke(); Log.Info($"保持唤醒：{minutes?.ToString() ?? "手动结束"}"); return true;
    }
    public void Stop()
    {
        if (IsActive && SetThreadExecutionState(0x80000000) == 0)
        { Error = "暂时无法释放保持唤醒请求，请重试或退出 Pop"; Changed?.Invoke(); return; }
        IsActive = false; EndsAt = null; duration = null; Error = null; timer.Stop(); Changed?.Invoke(); Log.Info("保持唤醒：已停止");
    }
    public void ShowWindow()
    {
        if (window is { IsLoaded: true }) { window.Activate(); return; }
        window = new KeepAwakeWindow(this); window.Closed += (_, _) => window = null; window.Show();
    }
    public void Dispose() { timer.Stop(); if (IsActive) Stop(); window?.Close(); }
    public string Status => !IsActive ? "当前未保持唤醒" : EndsAt is { } end
        ? $"保持唤醒到 {end.ToLocalTime():HH:mm} · 还剩 {Math.Max(1, Math.Ceiling((Remaining ?? TimeSpan.Zero).TotalMinutes)):0} 分钟"
        : "持续保持唤醒，直到手动停止或退出 Pop";

    private sealed class KeepAwakeWindow : ToolWindow
    {
        public KeepAwakeWindow(KeepAwakeController controller) : base("Pop 保持唤醒", 640, 370)
        {
            var panel = new StackPanel();
            var status = SettingsUi.Label("", Theme.Subtitle, "TextFillColorPrimaryBrush");
            status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(status);
            panel.Children.Add(SettingsUi.Note("临时防止屏幕和电脑自动休眠。关闭这个窗口会继续保持；定时结束、手动停止或退出 Pop 后恢复系统设置。"));
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 10) };
            foreach (var (minutes, title) in new (int?, string)[] { (30, "30 分钟"), (60, "1 小时"), (120, "2 小时"), (null, "直到手动停止") })
            {
                var button = SettingsUi.ActionButton(title, null, () => controller.Start(minutes));
                button.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(button);
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "awake-" + (minutes?.ToString() ?? "continuous"));
            }
            panel.Children.Add(buttons);
            var stop = SettingsUi.ActionButton("停止保持唤醒", null, controller.Stop);
            System.Windows.Automation.AutomationProperties.SetAutomationId(stop, "awake-stop"); panel.Children.Add(stop);
            var error = SettingsUi.Label("", Theme.Caption, "TextFillColorSecondaryBrush"); error.TextWrapping = TextWrapping.Wrap; panel.Children.Add(error);
            void Refresh() { status.Text = controller.Status; stop.IsEnabled = controller.IsActive; error.Text = controller.Error ?? ""; }
            controller.Changed += Refresh; Closed += (_, _) => controller.Changed -= Refresh;
            Body.Children.Add(panel); Refresh();
        }
    }
}
