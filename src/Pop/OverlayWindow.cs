using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static Pop.Native;

namespace Pop;

/// 浮窗的基类：不抢焦点（原来的 App 一直在前台，选区不丢，模拟的按键也发给它），不出现在任务栏和 Alt+Tab 里。
/// 位置用物理像素，和钩子拿到的坐标一致。
internal abstract class OverlayWindow : Window
{
    /// 动画放慢的倍数，截图时用（POP_ANIMATION_SCALE=6）
    protected static readonly double AnimationScale =
        double.TryParse(Environment.GetEnvironmentVariable("POP_ANIMATION_SCALE"), out var s) && s > 0 ? s : 1;

    protected OverlayWindow(bool clickThrough)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        AddExStyle(hwnd, WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | (clickThrough ? WS_EX_TRANSPARENT : 0));
    }

    protected IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected static Duration Ms(double milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds * AnimationScale));

    /// 窗口左上角放到物理像素 (x, y)，大小也是物理像素
    protected void PlacePhysical(int x, int y, int width, int height) =>
        SetWindowPos(Handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);

    /// 指针所在位置（物理像素）是否在窗口里
    public bool ContainsPhysical(int x, int y) =>
        IsVisible && GetWindowRect(Handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    protected void FadeOutAndHide(UIElement root, double milliseconds)
    {
        var fade = new DoubleAnimation(0, Ms(milliseconds));
        fade.Completed += (_, _) =>
        {
            // 淡出的过程中又被显示出来了
            if (root.Opacity == 0 && !keepVisible) Hide();
        };
        keepVisible = false;
        root.BeginAnimation(OpacityProperty, fade);
    }

    private bool keepVisible;

    protected void MarkShowing() => keepVisible = true;
}
