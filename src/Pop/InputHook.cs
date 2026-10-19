using System.Runtime.InteropServices;
using Pop.Core;
using static Pop.Native;

namespace Pop;

/// 全局鼠标和键盘钩子，跑在单独的线程上。
/// 低级钩子的回调必须很快返回（系统默认最多等 1 秒左右，超时几次就会被悄悄摘掉），
/// 所以回调里只做判定，界面上的事都丢给界面线程去做。
internal sealed class InputHook : IDisposable
{
    private readonly PressTracker tracker = new();
    private readonly ClickTriggerTracker clicks = new();
    private readonly SelectionGestureTracker selectionGesture = new();
    private readonly MouseApplicationFilter applications = new();
    private IntPtr leftTarget, triggerTarget;
    private readonly int doubleClickTime = System.Windows.Forms.SystemInformation.DoubleClickTime;
    private readonly double doubleClickTolerance = System.Windows.Forms.SystemInformation.DoubleClickSize.Width / 2d;
    private double selectionScale = 1;
    private long activityVersion;
    private volatile bool selectionToolbar;
    public bool SelectionToolbar { get => selectionToolbar; set => selectionToolbar = value; }
    public long ActivityVersion => Interlocked.Read(ref activityVersion);
    public void ExcludeApplications(IEnumerable<string> names) => applications.Update(names);
    public bool AllowsMouseTrigger
    {
        get
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var process);
            return enabled && applications.Allows(foreground, process);
        }
    }
    public event Action<int, int, IntPtr, long>? SelectionRequested;
    public event Action<int, int, bool>? PointerActivity;
    private readonly LowLevelProc mouseProc;
    private readonly LowLevelProc keyboardProc;
    private Thread? thread;
    private uint threadId;
    private IntPtr mouseHook, keyboardHook;
    private Timer? holdTimer;
    private volatile bool enabled = true;
    private volatile TriggerMode triggerMode = TriggerMode.LongPressRight;
    private volatile TriggerModifier triggerModifier = TriggerModifier.Alt;
    private TriggerModifier? maskModifierRelease;

    public TriggerMode TriggerMode { get => triggerMode; set => triggerMode = value; }
    public TriggerModifier TriggerModifier { get => triggerModifier; set => triggerModifier = value; }
    /// 修饰键右键、中键完成一次点击后唤起，圆盘以点击模式工作。
    public event Action<int, int>? ClickTriggered;

    // 补发右键的请求：在钩子回调返回之后、由钩子线程的消息循环发出，保证被扣下的那个事件先处理完
    private const uint WM_REPLAY = 0x8001; // WM_APP + 1
    private const int ReplayDownKind = 1;
    private const int ReplayClickKind = 2;

    /// 长按成立，参数是按下的位置（物理像素）。在计时器线程上调用
    public event Action<int, int>? Triggered;
    /// 长按成立后指针移动。在钩子线程上调用
    public event Action<int, int>? Moved;
    /// 长按成立后右键松开。在钩子线程上调用
    public event Action<int, int>? Released;
    /// 任意位置按下左键或中键（用来关掉结果卡片）。在钩子线程上调用
    public event Action<int, int>? OtherButtonDown;
    /// 键盘按下。返回 true 表示把这个键扣下，不交给当前 App。在钩子线程上调用，必须立刻返回
    public Func<int, bool>? KeyFilter { get; set; }

    public InputHook()
    {
        mouseProc = MouseCallback;
        keyboardProc = KeyboardCallback;
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (!value) tracker.Cancel();
        }
    }

    public int HoldMilliseconds
    {
        get => tracker.HoldMilliseconds;
        set => tracker.HoldMilliseconds = value;
    }

    public bool IsInstalled => mouseHook != IntPtr.Zero && keyboardHook != IntPtr.Zero;

    /// 右键还按着、圆盘被键盘关掉时调用：之后的「松开」也扣下，不让 App 收到半个右键
    public void DiscardCurrentPress() => tracker.Discard();

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            var module = GetModuleHandle(null);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            if (mouseHook == IntPtr.Zero) Log.Error($"安装鼠标钩子失败，错误码 {Marshal.GetLastWin32Error()}");
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
            if (keyboardHook == IntPtr.Zero) Log.Error($"安装键盘钩子失败，错误码 {Marshal.GetLastWin32Error()}");
            ready.Set();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message != WM_REPLAY) continue;
                if ((int)msg.wParam == ReplayDownKind) InputInjector.RightDown();
                else if ((int)msg.wParam == ReplayClickKind) InputInjector.RightClick();
            }
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            mouseHook = keyboardHook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "Pop 输入钩子",
            Priority = ThreadPriority.Highest,
        };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        Log.Info($"输入钩子{(IsInstalled ? "已安装" : "安装失败")}");
    }

    public void Dispose()
    {
        applications.Dispose();
        holdTimer?.Dispose();
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread?.Join(TimeSpan.FromSeconds(2));
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        // Pop 自己补发的事件
        if (info.dwExtraInfo == InputInjector.Marker) return CallNextHookEx(mouseHook, nCode, wParam, lParam);

        var x = info.pt.X;
        var y = info.pt.Y;
        PressDecision decision;
        try
        {
            var message = (int)wParam;
            if (message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
            {
                Interlocked.Increment(ref activityVersion);
                PointerActivity?.Invoke(x, y, message is WM_MOUSEWHEEL or WM_MOUSEHWHEEL);
            }
            if (message == WM_LBUTTONDOWN)
            {
                var foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out var process);
                if (selectionToolbar && enabled && applications.Allows(foreground, process))
                {
                    if (leftTarget != foreground) selectionGesture.Reset();
                    leftTarget = foreground;
                    selectionScale = Math.Max(1, GetDpiForWindow(foreground) / 96d);
                    selectionGesture.Down(x, y, Environment.TickCount64, doubleClickTime, doubleClickTolerance);
                }
                else selectionGesture.Reset();
            }
            if (message == WM_LBUTTONUP)
            {
                // 只记录手势，UIA 读取在界面调度后进行。
                if (selectionGesture.Up(x, y, selectionScale) && selectionToolbar && enabled && GetForegroundWindow() == leftTarget)
                    SelectionRequested?.Invoke(x, y, leftTarget, ActivityVersion);
            }
            if (message is WM_RBUTTONUP or WM_MBUTTONUP)
            {
                var click = clicks.Up(message == WM_RBUTTONUP ? TriggerButton.Right : TriggerButton.Middle, AllowsMouseTrigger && GetForegroundWindow() == triggerTarget);
                if (click.Swallow)
                {
                    if (click.Activate) ClickTriggered?.Invoke(x, y);
                    return new IntPtr(1);
                }
            }
            if (message is WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out var foregroundProcess);
                var external = applications.Allows(foreground, foregroundProcess);
                var modifierKey = triggerModifier switch { TriggerModifier.Control => 0x11, TriggerModifier.Shift => 0x10, TriggerModifier.Windows => 0x5B, _ => 0x12 };
                var modifierDown = (GetAsyncKeyState(modifierKey) & 0x8000) != 0
                    || triggerModifier == TriggerModifier.Windows && (GetAsyncKeyState(0x5C) & 0x8000) != 0;
                var click = clicks.Down(message == WM_RBUTTONDOWN ? TriggerButton.Right : TriggerButton.Middle,
                    triggerMode, modifierDown, enabled && external);
                if (click.Swallow)
                {
                    triggerTarget = foreground;
                    if (triggerMode == TriggerMode.ModifierRightClick && triggerModifier is TriggerModifier.Alt or TriggerModifier.Windows)
                        maskModifierRelease = triggerModifier;
                    return new IntPtr(1);
                }
                if (message == WM_RBUTTONDOWN && (!external || !enabled || triggerMode != TriggerMode.LongPressRight))
                    return CallNextHookEx(mouseHook, nCode, wParam, lParam);
                if (message == WM_RBUTTONDOWN) triggerTarget = foreground;
            }
            switch ((int)wParam)
            {
                case WM_RBUTTONDOWN:
                    if (!enabled) return CallNextHookEx(mouseHook, nCode, wParam, lParam);
                    decision = tracker.OnDown(x, y);
                    break;
                case WM_MOUSEMOVE:
                    decision = tracker.OnMove(x, y);
                    break;
                case WM_RBUTTONUP:
                    decision = tracker.OnUp();
                    break;
                case WM_LBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    OtherButtonDown?.Invoke(x, y);
                    return CallNextHookEx(mouseHook, nCode, wParam, lParam);
                default:
                    return CallNextHookEx(mouseHook, nCode, wParam, lParam);
            }

            switch (decision.Action)
            {
                case PressAction.StartTimer:
                    StartHoldTimer(tracker.Token, x, y);
                    break;
                case PressAction.ReplayDown:
                    PostThreadMessage(threadId, WM_REPLAY, ReplayDownKind, IntPtr.Zero);
                    break;
                case PressAction.ReplayClick:
                    PostThreadMessage(threadId, WM_REPLAY, ReplayClickKind, IntPtr.Zero);
                    break;
                case PressAction.Moved:
                    Moved?.Invoke(x, y);
                    break;
                case PressAction.Release:
                    Released?.Invoke(x, y);
                    break;
            }
        }
        catch (Exception e)
        {
            // 钩子里抛异常会把整个进程带走，而且这时右键可能正被扣着
            Log.Error("鼠标钩子出错", e);
            tracker.Reset();
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        return decision.Verdict == HookVerdict.Swallow ? new IntPtr(1) : CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private void StartHoldTimer(int token, int x, int y)
    {
        holdTimer?.Dispose();
        holdTimer = new Timer(_ =>
        {
            if (!tracker.OnTimer(token)) return;
            if (!AllowsMouseTrigger || GetForegroundWindow() != triggerTarget) { tracker.Discard(); return; }
            try
            {
                Triggered?.Invoke(x, y);
            }
            catch (Exception e)
            {
                Log.Error("处理长按出错", e);
            }
        }, null, tracker.HoldMilliseconds, Timeout.Infinite);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is WM_KEYUP or WM_SYSKEYUP && maskModifierRelease is { } modifier)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var matches = modifier == TriggerModifier.Alt ? info.vkCode is 0x12 or 0xA4 or 0xA5 : info.vkCode is 0x5B or 0x5C;
            if (info.dwExtraInfo != InputInjector.Marker && matches)
            {
                maskModifierRelease = null;
                if (InputInjector.MaskModifierRelease((ushort)info.vkCode)) return new IntPtr(1);
            }
        }
        if (nCode >= 0 && (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN && KeyFilter is { } filter)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != InputInjector.Marker)
            {
                Interlocked.Increment(ref activityVersion);
                try
                {
                    if (filter((int)info.vkCode)) return new IntPtr(1);
                }
                catch (Exception e)
                {
                    Log.Error("键盘钩子出错", e);
                }
            }
        }
        return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }
}
