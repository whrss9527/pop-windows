namespace Pop.Core;

/// 钩子对这次鼠标事件的处理：放行给目标 App，还是扣下
public enum HookVerdict { Pass, Swallow }

/// 钩子处理完之后还要做的事
public enum PressAction
{
    None,
    /// 右键按下被扣住，开始计时
    StartTimer,
    /// 按住拖动超过阈值：补发「按下」，之后的拖动照常交给 App
    ReplayDown,
    /// 计时内松开：按原顺序补发「按下 + 松开」，系统右键菜单照常弹出
    ReplayClick,
    /// 这次按压归 Pop，指针移动了（圆盘跟着高亮）
    Moved,
    /// 这次按压归 Pop，右键松开了（执行圆盘上的一格或者关闭）
    Release,
}

public enum PressPhase
{
    /// 没有按着右键，或者这次按压已经交给了 App
    Idle,
    /// 右键按下被扣住，正在计时
    Pending,
    /// 计时到了，这次按压归 Pop
    Owned,
    /// 圆盘被键盘关掉了，等右键松开时把「松开」也扣下
    Discarding,
}

public readonly record struct PressDecision(HookVerdict Verdict, PressAction Action)
{
    public static readonly PressDecision PassThrough = new(HookVerdict.Pass, PressAction.None);
}

/// 长按右键的判定：
/// 按下 → 扣住并计时；计时内松开 → 补发按下 + 松开；拖动超过阈值 → 补发按下并放行拖动；计时到 → 归 Pop。
/// 只保存状态、不碰系统接口，钩子线程和计时器线程都会调用，所以方法都加了锁。
public sealed class PressTracker
{
    private readonly object gate = new();
    private double originX, originY;
    private int token;

    public int HoldMilliseconds { get; set; } = 250;
    public double DragThreshold { get; set; } = 6;
    public PressPhase Phase { get; private set; } = PressPhase.Idle;

    /// 当前计时对应的编号，计时器回调时带回来，旧的计时到了不算数
    public int Token { get { lock (gate) return token; } }

    public (double X, double Y) Origin { get { lock (gate) return (originX, originY); } }

    public PressDecision OnDown(double x, double y)
    {
        lock (gate)
        {
            if (Phase != PressPhase.Idle) return PressDecision.PassThrough;
            Phase = PressPhase.Pending;
            originX = x;
            originY = y;
            token++;
            return new(HookVerdict.Swallow, PressAction.StartTimer);
        }
    }

    public PressDecision OnMove(double x, double y)
    {
        lock (gate)
        {
            switch (Phase)
            {
                case PressPhase.Pending:
                    var dx = x - originX;
                    var dy = y - originY;
                    if (dx * dx + dy * dy <= DragThreshold * DragThreshold) return PressDecision.PassThrough;
                    Phase = PressPhase.Idle;
                    return new(HookVerdict.Pass, PressAction.ReplayDown);
                case PressPhase.Owned:
                    return new(HookVerdict.Pass, PressAction.Moved);
                default:
                    return PressDecision.PassThrough;
            }
        }
    }

    public PressDecision OnUp()
    {
        lock (gate)
        {
            switch (Phase)
            {
                case PressPhase.Pending:
                    Phase = PressPhase.Idle;
                    return new(HookVerdict.Swallow, PressAction.ReplayClick);
                case PressPhase.Owned:
                    Phase = PressPhase.Idle;
                    return new(HookVerdict.Swallow, PressAction.Release);
                case PressPhase.Discarding:
                    Phase = PressPhase.Idle;
                    return new(HookVerdict.Swallow, PressAction.None);
                default:
                    return PressDecision.PassThrough;
            }
        }
    }

    /// 计时到了。返回 true 表示这次按压归 Pop
    public bool OnTimer(int timerToken)
    {
        lock (gate)
        {
            if (Phase != PressPhase.Pending || timerToken != token) return false;
            Phase = PressPhase.Owned;
            return true;
        }
    }

    /// 圆盘在右键还按着的时候被关掉了（Esc、数字键）：之后的「松开」直接扣下
    public void Discard()
    {
        lock (gate)
        {
            if (Phase == PressPhase.Owned) Phase = PressPhase.Discarding;
        }
    }

    /// 关掉长按功能时调用：正在计时的按压当作没发生过
    public void Reset()
    {
        lock (gate)
        {
            Phase = PressPhase.Idle;
            token++;
        }
    }
}
