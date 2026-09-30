using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Pop.Core;
using Path = System.Windows.Shapes.Path;

namespace Pop;

/// 圆形功能菜单：从指针处弹开，高亮沿着圆环滑到指针所指的那一格
internal sealed class RingWindow : OverlayWindow
{
    public const double Size = 300;
    private const double Center = Size / 2;
    private const double OuterRadius = 138;
    private const double InnerRadius = 56;
    private const double GapDegrees = 1.2;
    /// 圆心这一圈里松开就是关闭（DIP）
    public const double DeadRadius = 34;

    private readonly Grid root = new() { Width = Size, Height = Size };
    private readonly Canvas canvas = new() { Width = Size, Height = Size };
    private readonly ScaleTransform scale = new(1, 1, Center, Center);
    private readonly Path highlight = new();
    private readonly RotateTransform highlightRotation = new(0, Center, Center);
    private readonly TextBlock status = new();
    private readonly List<(TextBlock Glyph, TextBlock Title)> labels = [];
    private IReadOnlyList<PopAction> items = [];
    private ClassifiedContent? content;
    private int? highlighted;
    private double highlightAngle;
    private Theme theme = Theme.Current();

    public RingWindow() : base(clickThrough: true)
    {
        Width = Size;
        Height = Size;
        Title = "Pop 圆盘";
        root.RenderTransform = scale;
        root.Children.Add(canvas);
        Content = root;
    }

    public IReadOnlyList<PopAction> Items => items;

    /// 物理像素下的缩放比例（显示时所在显示器）
    public double Scale { get; private set; } = 1;

    public void ShowAt(int x, int y, IReadOnlyList<PopAction> ringItems)
    {
        items = ringItems;
        content = null;
        highlighted = null;
        theme = Theme.Current();
        Build();

        var (work, monitorScale) = Native.MonitorAt(x, y);
        Scale = monitorScale;
        var radius = Size / 2 * Scale;
        var (cx, cy) = RingGeometry.ClampCenter(work, x, y, radius);
        var side = (int)Math.Round(Size * Scale);
        var left = (int)Math.Round(cx - side / 2.0);
        var top = (int)Math.Round(cy - side / 2.0);
        PlacePhysical(left, top, side, side);

        MarkShowing();
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        scale.ScaleX = scale.ScaleY = 0.6;
        Show();
        // 移到另一个缩放比例的显示器时 WPF 会按系统建议的位置重新摆一次，这里再放回来
        Dispatcher.BeginInvoke(() => PlacePhysical(left, top, side, side), System.Windows.Threading.DispatcherPriority.Loaded);

        var spring = new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, Ms(240)) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, Ms(240)) { EasingFunction = spring });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(140)));
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        FadeOutAndHide(root, 110);
    }

    /// 选中的内容读到了：更新中间的预览，读不到文字的格子变灰；链接之类的内容会换掉某些格子
    public void SetContent(ClassifiedContent selected, IReadOnlyList<PopAction> ringItems)
    {
        content = selected;
        if (!ReferenceEquals(ringItems, items) && !ringItems.SequenceEqual(items))
        {
            items = ringItems;
            var keep = highlighted;
            highlighted = null;
            Build();
            Highlight(keep);
        }
        status.Text = selected.IsEmpty ? "没有选中文字" : CenterText(selected);
        status.Foreground = new SolidColorBrush(selected.IsEmpty ? theme.SecondaryText : theme.Text);
        RefreshLabels();
    }

    /// 普通文字显示开头一段，链接、算式这些显示类型
    private static string CenterText(ClassifiedContent content) =>
        content.Kinds is ContentKind.Text or (ContentKind.Text | ContentKind.ChineseText) or (ContentKind.Text | ContentKind.ForeignText)
            ? Preview(content.Text)
            : content.Summary;

    public void Highlight(int? index)
    {
        if (index == highlighted) return;
        highlighted = index;
        if (index is { } i)
        {
            var available = content is null || items[i].IsAvailable(content);
            highlight.Fill = new SolidColorBrush(available ? theme.Accent : theme.SecondaryText);
            var target = RingGeometry.NextHighlightAngle(highlightAngle, i, items.Count);
            if (highlight.Opacity < 0.01)
            {
                // 从圆心出来时直接出现在那一格，不从上一次的位置滑过来
                highlightRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                highlightRotation.Angle = target;
            }
            else
            {
                highlightRotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(target, Ms(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            highlightAngle = target;
            highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(90)));
        }
        else
        {
            highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(90)));
        }
        RefreshLabels();
    }

    private static string Preview(string text)
    {
        var oneLine = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length > 40 ? oneLine[..40] + "…" : oneLine;
    }

    private void Build()
    {
        canvas.Children.Clear();
        labels.Clear();

        var surface = new Ellipse
        {
            Width = OuterRadius * 2 + 8,
            Height = OuterRadius * 2 + 8,
            Fill = new SolidColorBrush(theme.Surface),
            Stroke = new SolidColorBrush(theme.SurfaceBorder),
            StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = theme.Dark ? 0.5 : 0.22, Direction = 270 },
        };
        Canvas.SetLeft(surface, Center - OuterRadius - 4);
        Canvas.SetTop(surface, Center - OuterRadius - 4);
        canvas.Children.Add(surface);

        var count = items.Count;
        var step = 360.0 / Math.Max(count, 1);
        for (var i = 0; i < count; i++)
        {
            var angle = RingGeometry.SectorAngle(i, count);
            canvas.Children.Add(new Path
            {
                Data = Wedge(angle - step / 2 + GapDegrees, angle + step / 2 - GapDegrees, OuterRadius - 4, InnerRadius + 4),
                Fill = new SolidColorBrush(theme.Segment),
            });
        }

        highlight.Data = Wedge(-step / 2 + GapDegrees, step / 2 - GapDegrees, OuterRadius - 4, InnerRadius + 4);
        highlight.RenderTransform = highlightRotation;
        highlight.BeginAnimation(OpacityProperty, null);
        highlight.Opacity = 0;
        canvas.Children.Add(highlight);

        var labelRadius = (OuterRadius + InnerRadius) / 2;
        for (var i = 0; i < count; i++)
        {
            var rad = RingGeometry.SectorAngle(i, count) * Math.PI / 180;
            var glyph = new TextBlock
            {
                Text = items[i].Glyph,
                FontFamily = Theme.IconFont,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var title = new TextBlock
            {
                Text = items[i].Title,
                FontFamily = Theme.TextFont,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var panel = new StackPanel { Width = 64, Height = 48, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(glyph);
            panel.Children.Add(title);
            Canvas.SetLeft(panel, Center + labelRadius * Math.Sin(rad) - 32);
            Canvas.SetTop(panel, Center - labelRadius * Math.Cos(rad) - 24);
            canvas.Children.Add(panel);
            labels.Add((glyph, title));
        }

        // 上一次显示时 status 放在旧的容器里，要先拿出来才能放进新的
        if (status.Parent is Panel oldParent) oldParent.Children.Remove(status);
        status.Text = "读取中…";
        status.FontFamily = Theme.TextFont;
        status.FontSize = 11;
        status.TextAlignment = TextAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Width = InnerRadius * 2 - 22;
        status.MaxHeight = 46;
        status.Foreground = new SolidColorBrush(theme.SecondaryText);
        var statusBox = new Grid { Width = InnerRadius * 2, Height = InnerRadius * 2 };
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.VerticalAlignment = VerticalAlignment.Center;
        statusBox.Children.Add(status);
        Canvas.SetLeft(statusBox, Center - InnerRadius);
        Canvas.SetTop(statusBox, Center - InnerRadius);
        canvas.Children.Add(statusBox);

        RefreshLabels();
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < labels.Count; i++)
        {
            var available = content is null || items[i].IsAvailable(content);
            var color = i == highlighted ? theme.AccentText : available ? theme.Text : theme.DisabledText;
            var brush = new SolidColorBrush(color);
            labels[i].Glyph.Foreground = brush;
            labels[i].Title.Foreground = brush;
        }
    }

    /// 圆环上的一段，角度 0 朝上、顺时针
    private static Geometry Wedge(double fromDegrees, double toDegrees, double outer, double inner)
    {
        Point At(double degrees, double r)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(Center + r * Math.Sin(rad), Center - r * Math.Cos(rad));
        }
        var large = toDegrees - fromDegrees > 180;
        var figure = new PathFigure { StartPoint = At(fromDegrees, outer), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(toDegrees, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(toDegrees, inner), true));
        figure.Segments.Add(new ArcSegment(At(fromDegrees, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
