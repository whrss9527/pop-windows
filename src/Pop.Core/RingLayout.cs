namespace Pop.Core;

/// 边缘用朝屏幕内侧展开的弧形菜单，唤起点和鼠标位置保持不动。
public sealed record RingLayout(double StartAngle, double Span)
{
    public bool IsArc => Span < 360;
    public int VisibleLimit => Span <= 90 ? 3 : Span <= 180 ? 5 : RingItems.MaxSlots;
    public double Angle(int index, int count) => IsArc ? StartAngle + Span * (index + 0.5) / count : RingGeometry.SectorAngle(index, count);

    public int? HitTest(double dx, double dy, int count, double deadRadius)
    {
        if (!IsArc) return RingGeometry.HitTest(dx, dy, count, deadRadius);
        if (count <= 0 || dx * dx + dy * dy < deadRadius * deadRadius) return null;
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        var offset = ((angle - StartAngle) % 360 + 360) % 360;
        return offset < Span ? Math.Min(count - 1, (int)(offset / Span * count)) : null;
    }

    public static RingLayout At(Rect work, double x, double y, double radius)
    {
        var left = x - work.Left < radius; var right = work.Right - x < radius;
        var top = y - work.Top < radius; var bottom = work.Bottom - y < radius;
        // 极小工作区无法容纳任何标准圆盘，使用完整菜单并由窗口裁剪。
        if (left && right || top && bottom) return new(0, 360);
        var (start, span) = (left, right, top, bottom) switch
        {
            (true, _, true, _) => (90d, 90d),
            (_, true, true, _) => (180d, 90d),
            (true, _, _, true) => (0d, 90d),
            (_, true, _, true) => (270d, 90d),
            (true, _, _, _) => (0d, 180d),
            (_, true, _, _) => (180d, 180d),
            (_, _, true, _) => (90d, 180d),
            (_, _, _, true) => (270d, 180d),
            _ => (0d, 360d),
        };
        return span == 360 ? new(start, span) : new(start + 6, span - 12);
    }
}
