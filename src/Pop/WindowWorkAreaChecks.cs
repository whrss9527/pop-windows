using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Pop.Core;

namespace Pop;

/// 在 CI 的真实窗口上切换受控工作区；不改变宿主的显示设置，也不冒充真实双屏测试。
internal static class WindowWorkAreaChecks
{
    public static async Task VerifyAsync(App app, Action<string, Window> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("工作区检查仅用于演示回归");
        var before = app.Settings.ToJson();
        var pending = app.SettingsSavePending;
        var (_, actualWork, scale) = Native.MonitorDetailsAt(1, 1);
        var small = new Pop.Core.Rect(actualWork.Left + 24 * scale, actualWork.Top + 24 * scale,
            actualWork.Left + Math.Min(844 * scale, actualWork.Width - 24 * scale),
            actualWork.Top + Math.Min(504 * scale, actualWork.Height - 24 * scale));
        var work = actualWork;
        var reads = 0;
        var window = new SettingsWindow(app);
        var originalMinWidth = window.MinWidth; var originalMinHeight = window.MinHeight;
        WindowWorkArea.UseWorkAreaForDemo(window, () => { reads++; return (work, scale); });
        // 重复调用共用入口不能重复安装处理器。
        SettingsUi.FitToWorkArea(window);
        var handle = IntPtr.Zero;
        ToolWindow? focusWindow = null;
        try
        {
            window.Show(); handle = new WindowInteropHelper(window).Handle;
            await Task.Delay(250);
            work = small;
            // 模拟移动通知，再实际移动 HWND：移动中不应被拉回。
            SendMessage(handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
            var rect = Bounds(handle);
            if (!Native.SetWindowPos(handle, IntPtr.Zero, (int)actualWork.Left + 100, (int)actualWork.Top + 120,
                (int)rect.Width, (int)rect.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE))
                throw new InvalidOperationException("无法移动工作区测试窗口");
            var duringMove = Bounds(handle);
            await Task.Delay(150);
            if (Bounds(handle) != duringMove || Inside(VisibleBounds(handle), small))
                throw new InvalidOperationException("移动过程被拉回，或越界测试未建立");
            SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            await WaitUntil(() => Inside(VisibleBounds(handle), small));
            if (window.MinHeight >= originalMinHeight) throw new InvalidOperationException("较小工作区没有放宽原最小高度");
            AssertFooterVisible(window);
            await Task.Delay(250); shot("window-workarea-small", window);

            // 工作区变大时恢复最小约束，但保留用户当前大小，避免每次跨屏强行放大。
            work = actualWork;
            SendMessage(handle, 0x001a, new IntPtr(0x002f), IntPtr.Zero);
            await WaitUntil(() => window.MinHeight == originalMinHeight && window.MinWidth == originalMinWidth
                && Inside(VisibleBounds(handle), actualWork));
            AssertFooterVisible(window);
            await Task.Delay(250); shot("window-workarea-restored", window);

            // 自动约束后台窗口不能改变前台窗口或正在输入的控件。
            focusWindow = new ToolWindow("Pop 工作区输入检查", 600, 360);
            var editor = new TextBox { Text = "保留输入焦点", VerticalAlignment = VerticalAlignment.Center };
            focusWindow.BodyContent(editor); focusWindow.Show(); focusWindow.Activate(); editor.Focus();
            await Task.Delay(150);
            var foreground = new WindowInteropHelper(focusWindow).Handle;
            if (Native.GetForegroundWindow() != foreground || !editor.IsKeyboardFocused)
                throw new InvalidOperationException("工作区焦点检查未建立输入焦点");
            work = small;
            SendMessage(handle, 0x007e, IntPtr.Zero, IntPtr.Zero);
            await WaitUntil(() => Inside(VisibleBounds(handle), small));
            if (Native.GetForegroundWindow() != foreground || !editor.IsKeyboardFocused)
                throw new InvalidOperationException("适配后台窗口抢走了输入焦点");
            focusWindow.Close(); focusWindow = null; window.Activate();

            // 已在工作区中的贴边窗口不添加外边距、不改变用户大小。
            var aligned = Bounds(handle);
            SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(150);
            if (Bounds(handle) != aligned) throw new InvalidOperationException("可见窗口被重复改变大小或位置");

            window.WindowState = WindowState.Maximized;
            await Task.Delay(250); var maximized = Bounds(handle);
            SendMessage(handle, 0x001a, new IntPtr(0x002f), IntPtr.Zero);
            await Task.Delay(150);
            if (window.WindowState != WindowState.Maximized || Bounds(handle) != maximized)
                throw new InvalidOperationException("工作区适配改变了最大化窗口");
            window.WindowState = WindowState.Minimized;
            await Task.Delay(150); var minimized = Bounds(handle);
            SendMessage(handle, 0x007e, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(150);
            if (window.WindowState != WindowState.Minimized || Bounds(handle) != minimized)
                throw new InvalidOperationException("工作区适配改变了最小化窗口");
            window.WindowState = WindowState.Normal;
            await WaitUntil(() => Inside(VisibleBounds(handle), small));
            AssertFooterVisible(window);

            // 已排队的约束在关闭后不得继续读取窗口或工作区。
            SendMessage(handle, 0x007e, IntPtr.Zero, IntPtr.Zero);
            window.Close(); var readsAtClose = reads;
            await Task.Delay(150);
            if (reads != readsAtClose) throw new InvalidOperationException("窗口关闭后仍执行工作区适配");
            if (app.Settings.ToJson() != before || app.SettingsSavePending != pending)
                throw new InvalidOperationException("窗口移动或缩放改变了设置");
        }
        finally { focusWindow?.Close(); if (window.IsVisible) window.Close(); }
    }

    private static Pop.Core.Rect Bounds(IntPtr handle)
    {
        if (!Native.GetWindowRect(handle, out var rect)) throw new InvalidOperationException("读取测试窗口边界失败");
        return new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
    private static Pop.Core.Rect VisibleBounds(IntPtr handle)
    {
        if (Native.DwmGetWindowAttribute(handle, 9, out var rect, Marshal.SizeOf<Native.RECT>()) != 0)
            throw new InvalidOperationException("读取测试窗口可见边界失败");
        return new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
    private static bool Inside(Pop.Core.Rect bounds, Pop.Core.Rect work) => bounds.Left >= work.Left - 1 && bounds.Top >= work.Top - 1
        && bounds.Right <= work.Right + 1 && bounds.Bottom <= work.Bottom + 1 && bounds.Width > 0 && bounds.Height > 0;
    private static void AssertFooterVisible(SettingsWindow window)
    {
        var status = Tree(window).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "设置保存状态");
        var point = status.TranslatePoint(new Point(), window);
        if (!status.IsVisible || status.ActualHeight <= 0 || point.Y < 32 || point.Y + status.ActualHeight > window.ActualHeight)
            throw new InvalidOperationException("较小工作区的底部保存状态不可见");
    }
    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!ready())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("工作区窗口未达到预期边界或最小尺寸");
            await Task.Delay(25);
        }
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
