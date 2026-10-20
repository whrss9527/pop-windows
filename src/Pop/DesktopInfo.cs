using System.ComponentModel;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace Pop;

/// 直接读取物理像素屏幕和鼠标设置，避免为少量系统信息载入 WinForms。
internal static class DesktopInfo
{
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Native.RECT bounds, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

    public static int DoubleClickTime => (int)GetDoubleClickTime();
    public static double DoubleClickTolerance => GetSystemMetrics(36) / 2d;
    public static Drawing.Size SmallIconSize => new(Math.Max(16, GetSystemMetrics(49)), Math.Max(16, GetSystemMetrics(50)));
    public static Drawing.Rectangle VirtualScreen => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
    public static Drawing.Rectangle MonitorAt(int x, int y)
    {
        var (bounds, _, _) = Native.MonitorDetailsAt(x, y);
        return Drawing.Rectangle.FromLTRB((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom);
    }
    public static IReadOnlyList<Drawing.Rectangle> Monitors()
    {
        var result = new List<Drawing.Rectangle>();
        bool Collect(IntPtr monitor, IntPtr dc, ref Native.RECT bounds, IntPtr data)
        {
            if (bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                result.Add(Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            return true;
        }
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Collect, IntPtr.Zero) || result.Count == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取显示器信息");
        return result;
    }
}
