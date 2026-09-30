namespace Pop.Core;

public readonly record struct Rect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// 圆盘的几何：第 0 格在正上方，顺时针排列
public static class RingGeometry
{
    /// 指针相对按下位置偏移 (dx, dy) 时指向哪一格；还在圆心的死区里返回 null。屏幕坐标 y 轴朝下
    public static int? HitTest(double dx, double dy, int count, double deadRadius)
    {
        if (count <= 0) return null;
        if (dx * dx + dy * dy < deadRadius * deadRadius) return null;
        var sector = 2 * Math.PI / count;
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        var index = (int)Math.Floor((angle + sector / 2) / sector);
        return index % count;
    }

    /// 第 index 格中心线的角度（度），0 度朝上、顺时针
    public static double SectorAngle(int index, int count) => count <= 0 ? 0 : 360.0 * index / count;

    /// 高亮从 from 格滑到 to 格时应该转到的角度：走较短的方向，角度可以超出 0–360，动画不会绕远路
    public static double NextHighlightAngle(double current, int to, int count)
    {
        var target = SectorAngle(to, count);
        var delta = (target - current) % 360;
        if (delta > 180) delta -= 360;
        if (delta < -180) delta += 360;
        return current + delta;
    }

    /// 圆盘靠近屏幕边缘时往里挪，保证整个圆都在工作区里
    public static (double X, double Y) ClampCenter(Rect workArea, double x, double y, double radius)
    {
        double Clamp(double v, double min, double max) => min > max ? (min + max) / 2 : Math.Min(Math.Max(v, min), max);
        return (Clamp(x, workArea.Left + radius, workArea.Right - radius),
                Clamp(y, workArea.Top + radius, workArea.Bottom - radius));
    }
}
