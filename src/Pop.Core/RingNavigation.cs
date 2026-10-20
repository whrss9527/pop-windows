namespace Pop.Core;

/// 圆盘的键盘选择，数字 0 对应第十格，其余格子用方向键访问。
public static class RingNavigation
{
    /// 点击模式始终等待点击；长按模式仅在圆心松开并启用保持时等待点击。
    public static bool KeepAfterRelease(bool clickMode, bool keepOnCenter, int? selected) => clickMode || keepOnCenter && selected is null;

    public static int? NumberIndex(int virtualKey, int count)
    {
        var index = virtualKey switch
        {
            >= 0x31 and <= 0x39 => virtualKey - 0x31,
            >= 0x61 and <= 0x69 => virtualKey - 0x61,
            0x30 or 0x60 => 9,
            _ => -1,
        };
        return index >= 0 && index < count ? index : null;
    }

    public static int Move(int? current, int direction, int count) =>
        count <= 0 ? 0 : current is null ? (direction < 0 ? count - 1 : 0)
            : ((current.Value + direction) % count + count) % count;
}
