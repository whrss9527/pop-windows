using System.Runtime.InteropServices;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace Pop;

/// Windows 演示/CI：验证原生托盘回调、Explorer 恢复和物理像素显示器信息。
internal static class NativeExperienceChecks
{
    public static async Task VerifyAsync()
    {
        var monitors = DesktopInfo.Monitors();
        var union = monitors.Aggregate(Drawing.Rectangle.Union);
        if (union != DesktopInfo.VirtualScreen || DesktopInfo.DoubleClickTime <= 0 || DesktopInfo.DoubleClickTolerance <= 0)
            throw new InvalidOperationException("原生屏幕或鼠标设置信息异常");
        foreach (var monitor in monitors)
        {
            var center = new Drawing.Point(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
            if (DesktopInfo.MonitorAt(center.X, center.Y) != monitor)
                throw new InvalidOperationException("显示器的物理像素范围不一致");
        }
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Pop.ico")).Stream;
        using var icon = new Drawing.Icon(stream, DesktopInfo.SmallIconSize);
        using var tray = new NativeTrayIcon(icon, "Pop 原生托盘检查");
        await WaitUntil(() => tray.Registered);
        if (!tray.UsesVersion4) throw new InvalidOperationException("托盘未使用支持键盘的通知协议");
        var events = new List<(int X, int Y)>();
        tray.Activated += (x, y) => events.Add((x, y));
        foreach (var notification in new[] { 0x400, 0x401, 0x7b })
        {
            var coordinates = new IntPtr(unchecked((int)((uint)(ushort)-120 | ((uint)(ushort)80 << 16))));
            if (!PostMessage(tray.Handle, NativeTrayIcon.CallbackMessage, coordinates, new IntPtr((1 << 16) | notification)))
                throw new InvalidOperationException("无法投递托盘回调");
        }
        await WaitUntil(() => events.Count == 3);
        if (events.Any(p => p != (-120, 80))) throw new InvalidOperationException("托盘鼠标或键盘位置解析错误");
        var registrations = tray.Registrations;
        if (tray.TaskbarCreatedMessage == 0 || !PostMessage(tray.Handle, (int)tray.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("无法投递 Explorer 恢复通知");
        await WaitUntil(() => tray.Registrations == registrations + 1);
        tray.Update(icon, "Pop 更新后的托盘提示");
        if (!tray.Registered) throw new InvalidOperationException("托盘更新后丢失注册");
        tray.Dispose(); tray.Dispose();
        if (tray.Registered) throw new InvalidOperationException("托盘没有撤销注册");
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("System.Windows.Forms", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("仍然载入了 WinForms");
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!ready())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("原生托盘检查没有完成");
            await Task.Delay(25);
        }
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
