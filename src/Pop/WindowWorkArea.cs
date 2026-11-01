using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 在 WPF 完成缩放之后适配工作区；移动过程中不纠正位置，关闭时解除所有注册。
internal sealed class WindowWorkArea
{
    private static readonly ConditionalWeakTable<Window, WindowWorkArea> Attached = new();
    private readonly Window window;
    private HwndSource? source;
    private DispatcherOperation? pending;
    private bool loaded, moving, applying, closed;
    private double minWidth, minHeight;
    private Func<(Pop.Core.Rect Work, double Scale)>? demoWorkArea;

    private WindowWorkArea(Window window)
    {
        this.window = window;
        window.SourceInitialized += OnSourceInitialized;
        window.Loaded += OnLoaded;
        window.StateChanged += OnStateChanged;
        window.Closed += OnClosed;
        AttachSource();
        if (window.IsLoaded) OnLoaded(window, new RoutedEventArgs());
    }

    public static void Attach(Window window)
    {
        window.Dispatcher.VerifyAccess();
        if (!Attached.TryGetValue(window, out _)) Attached.Add(window, new WindowWorkArea(window));
    }

    /// 只在独立演示进程中注入受控工作区，不修改 Windows 的显示配置。
    internal static void UseWorkAreaForDemo(Window window, Func<(Pop.Core.Rect Work, double Scale)> read)
    {
        if (Application.Current is not App { IsDemo: true }) throw new InvalidOperationException("工作区替代仅用于演示回归");
        Attach(window);
        Attached.GetValue(window, _ => throw new InvalidOperationException()).demoWorkArea = read;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => AttachSource();
    private void AttachSource()
    {
        if (source is not null || closed) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        source = HwndSource.FromHwnd(handle);
        if (source is null) return;
        source.AddHook(Hook);
        source.Disposed += OnSourceDisposed;
    }

    private void OnSourceDisposed(object? sender, EventArgs e) => source = null;
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded || closed) return;
        loaded = true;
        // 构造窗口时设置的最终约束，而不是第一次缩小后的约束。
        minWidth = window.MinWidth; minHeight = window.MinHeight;
        // 首次加载立即适配，保证后续 BringIntoView 使用实际视口；跨屏等后续消息再延后处理。
        AttachSource(); Fit();
    }
    private void OnStateChanged(object? sender, EventArgs e) => QueueFit();

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0231) moving = true; // WM_ENTERSIZEMOVE
        else if (message == 0x0232) { moving = false; QueueFit(); } // WM_EXITSIZEMOVE
        else if (!applying && (message is 0x0047 or 0x02e0 or 0x007e // 位置、DPI、显示配置变化
            || message == 0x001a && wParam.ToInt64() == 0x002f)) // SPI_SETWORKAREA
            QueueFit();
        return IntPtr.Zero; // 继续由 WPF 处理原消息和 DPI 缩放
    }

    private void QueueFit()
    {
        if (closed || !loaded || pending?.Status == DispatcherOperationStatus.Pending) return;
        pending = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            pending = null;
            Fit();
        });
    }

    private void Fit()
    {
        if (closed || moving || applying || window.WindowState != WindowState.Normal || source is null || !window.IsVisible) return;
        var hwnd = source.Handle;
        if (!Native.GetWindowRect(hwnd, out var rect)) return;
        var (work, scale) = demoWorkArea?.Invoke() ?? ReadWorkArea(hwnd, rect);
        if (work.Width < 1 || work.Height < 1 || !double.IsFinite(scale) || scale <= 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
        var current = new Pop.Core.Rect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (Native.DwmGetWindowAttribute(hwnd, 9, out var visible, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) == 0)
            work = WindowPlacement.OuterWorkArea(work, current, new Pop.Core.Rect(visible.Left, visible.Top, visible.Right, visible.Bottom));
        var fit = WindowPlacement.Fit(current, work, scale, minWidth, minHeight);
        applying = true;
        try
        {
            if (window.MinWidth != fit.MinWidth) window.MinWidth = fit.MinWidth;
            if (window.MinHeight != fit.MinHeight) window.MinHeight = fit.MinHeight;
            if (fit.Bounds == current) return;
            var bounds = fit.Bounds;
            Native.SetWindowPos(hwnd, IntPtr.Zero, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        finally { applying = false; }
    }

    private static (Pop.Core.Rect Work, double Scale) ReadWorkArea(IntPtr hwnd, Native.RECT rect)
    {
        var (work, scale) = Native.MonitorAt((int)(((long)rect.Left + rect.Right) / 2), (int)(((long)rect.Top + rect.Bottom) / 2));
        var dpi = Native.GetDpiForWindow(hwnd);
        return (work, dpi > 0 ? dpi / 96d : scale);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        pending?.Abort(); pending = null;
        window.SourceInitialized -= OnSourceInitialized;
        window.Loaded -= OnLoaded;
        window.StateChanged -= OnStateChanged;
        window.Closed -= OnClosed;
        if (source is not null) { source.RemoveHook(Hook); source.Disposed -= OnSourceDisposed; source = null; }
        demoWorkArea = null;
        Attached.Remove(window);
    }
}
