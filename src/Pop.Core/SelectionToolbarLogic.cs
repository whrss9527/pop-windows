namespace Pop.Core;

/// 选择工具条沿用圆盘顺序，只展示能处理当前文字的功能。
public static class SelectionToolbarLogic
{
    public const int MaxItems = 5;

    public static IReadOnlyList<PopAction> Items(IEnumerable<PopAction> ring, ClassifiedContent content) =>
        ring.Where(a => content.Has(ContentKind.Text) && (a.Requires == ContentKind.None
            ? a.Category == Actions.PluginCategory && (a.Accepts?.Invoke(content) ?? true) : a.IsAvailable(content)))
            .DistinctBy(a => a.Id).Take(MaxItems).ToList();

    public static bool IsNear(Rect bounds, double x, double y, double scale = 1) =>
        x >= bounds.Left - 60 * scale && x <= bounds.Right + 60 * scale
        && y >= bounds.Top - 60 * scale && y <= bounds.Bottom + 60 * scale;

    /// Windows 屏幕坐标向下增长，短选区上方优先，多行选区跟随松开的位置。
    public static Rect Frame(double width, double height, Rect? selection, double x, double y, Rect screen, double scale = 1)
    {
        var target = selection is { } r && r.Bottom > r.Top && r.Bottom - r.Top <= 80 * scale
            ? r : new Rect(x, y - 10 * scale, x, y + 10 * scale);
        var top = target.Top - 6 * scale - height;
        if (top < screen.Top) top = target.Bottom + 6 * scale;
        var left = Math.Clamp((target.Left + target.Right - width) / 2, screen.Left + 4 * scale, Math.Max(screen.Left + 4 * scale, screen.Right - width - 4 * scale));
        top = Math.Clamp(top, screen.Top + 4 * scale, Math.Max(screen.Top + 4 * scale, screen.Bottom - height - 4 * scale));
        return new(left, top, left + width, top + height);
    }
}

/// 只观察左键，不接管点击。单击不读取选区，双击、三击或拖选才尝试读取。
public sealed class SelectionGestureTracker
{
    private double downX, downY, lastX, lastY;
    private long lastDown = long.MinValue;
    private int clicks;
    private bool pressed;

    public void Down(double x, double y, long time, int doubleClickMilliseconds, double clickTolerance)
    {
        clicks = lastDown != long.MinValue && time - lastDown <= doubleClickMilliseconds
            && Math.Abs(x - lastX) <= clickTolerance && Math.Abs(y - lastY) <= clickTolerance ? clicks % 3 + 1 : 1;
        lastDown = time; lastX = downX = x; lastY = downY = y; pressed = true;
    }

    public bool Up(double x, double y, double scale)
    {
        if (!pressed) return false;
        pressed = false;
        var dx = x - downX; var dy = y - downY;
        return clicks is 2 or 3 || dx * dx + dy * dy >= 64 * scale * scale;
    }

    public void Reset() { pressed = false; clicks = 0; lastDown = long.MinValue; }
}
