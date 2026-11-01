namespace Pop.Core;

public readonly record struct WindowFit(Rect Bounds, double MinWidth, double MinHeight);

/// 普通窗口在显示器工作区中的物理像素边界；最小尺寸以 DIP 保存，跨屏后可恢复。
public static class WindowPlacement
{
    public static WindowFit Fit(Rect current, Rect work, double scale, double minWidth, double minHeight)
    {
        if (!Valid(current) || !Valid(work) || !double.IsFinite(scale) || scale <= 0
            || !double.IsFinite(minWidth) || minWidth < 0 || !double.IsFinite(minHeight) || minHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(current), "窗口、工作区、缩放和最小尺寸必须有效");

        // 已经位于工作区内的贴边、分屏窗口保持原位置，不强制添加外边距。
        var availableWidth = work.Width;
        var availableHeight = work.Height;
        var width = Math.Min(availableWidth, Math.Max(current.Width, Math.Ceiling(minWidth * scale)));
        var height = Math.Min(availableHeight, Math.Max(current.Height, Math.Ceiling(minHeight * scale)));
        var left = Math.Clamp(current.Left, work.Left, work.Right - width);
        var top = Math.Clamp(current.Top, work.Top, work.Bottom - height);
        return new(new Rect(left, top, left + width, top + height),
            Math.Min(minWidth, availableWidth / scale), Math.Min(minHeight, availableHeight / scale));
    }

    /// GetWindowRect 包含不可见的缩放边框；允许这些边框越过工作区，避免破坏系统贴边布局。
    public static Rect OuterWorkArea(Rect work, Rect outer, Rect visible)
    {
        if (!Valid(visible) || visible.Left < outer.Left || visible.Top < outer.Top
            || visible.Right > outer.Right || visible.Bottom > outer.Bottom) return work;
        return new(work.Left - (visible.Left - outer.Left), work.Top - (visible.Top - outer.Top),
            work.Right + (outer.Right - visible.Right), work.Bottom + (outer.Bottom - visible.Bottom));
    }

    private static bool Valid(Rect rect) => double.IsFinite(rect.Left) && double.IsFinite(rect.Top)
        && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom) && rect.Width >= 1 && rect.Height >= 1;
}
