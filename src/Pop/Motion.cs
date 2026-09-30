using System.Globalization;
using System.Windows;

namespace Pop;

/// 动画时长。POP_ANIMATION_SCALE=6 把所有动画放慢 6 倍（截图看中间帧时用）
internal static class Motion
{
    public static readonly double Scale =
        double.TryParse(Environment.GetEnvironmentVariable("POP_ANIMATION_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 1;

    public static Duration Ms(double milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds * Scale));
}
