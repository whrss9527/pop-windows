namespace Pop.Core;

public enum TriggerMode { LongPressRight, ModifierRightClick, MiddleClick, Disabled }
public enum TriggerModifier { Alt, Control, Shift, Windows }
public enum TriggerButton { Right, Middle }

public readonly record struct ClickTriggerDecision(bool Swallow, bool Activate = false);

/// 只接管已配置的点击，按下和松开成对消费；设置中途变化时不向原 App 泄漏半个点击。
public sealed class ClickTriggerTracker
{
    private TriggerButton? owned;

    public ClickTriggerDecision Down(TriggerButton button, TriggerMode mode, bool modifierMatches, bool enabled)
    {
        if (owned is { } current) return new(current == button);
        if (!enabled) return default;
        var matches = mode == TriggerMode.MiddleClick && button == TriggerButton.Middle
            || mode == TriggerMode.ModifierRightClick && button == TriggerButton.Right && modifierMatches;
        if (!matches) return default;
        owned = button;
        return new(true);
    }

    public ClickTriggerDecision Up(TriggerButton button, bool enabled)
    {
        if (owned != button) return default;
        owned = null;
        return new(true, enabled);
    }
}
