using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Pop.Core;
using Path = System.Windows.Shapes.Path;

namespace Pop;

/// 圆形功能菜单：毛玻璃圆盘从指针处弹开，主题色的高亮沿着圆环滑到指针所指的那一格
internal sealed class RingWindow : OverlayWindow
{
    /// 圆盘直径（DIP）
    public const double Diameter = 300;
    private const double Pad = Theme.ShadowPad;
    private const double Size = Diameter + 2 * Pad;
    private const double Center = Size / 2;
    private const double OuterRadius = Diameter / 2;
    private const double InnerRadius = 54;
    private const double LabelRadius = 99;
    /// 圆心这一圈里松开就是关闭（DIP）
    public const double DeadRadius = 34;

    private readonly Grid root = new() { Width = Size, Height = Size };
    private readonly Canvas canvas = new() { Width = Size, Height = Size };
    private readonly ScaleTransform scale = new(1, 1, Center, Center);
    private readonly Path highlight = new() { StrokeLineJoin = PenLineJoin.Round, StrokeThickness = 10 };
    private readonly RotateTransform highlightRotation = new(0, Center, Center);
    private readonly TextBlock status = new();
    private readonly List<(FrameworkElement Box, Wpf.Ui.Controls.SymbolIcon Icon, TextBlock Title)> labels = [];
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

        var (work, monitorScale) = Native.MonitorAt(x, y);
        Scale = monitorScale;
        var (cx, cy) = RingGeometry.ClampCenter(work, x, y, OuterRadius * Scale);
        var side = (int)Math.Round(Size * Scale);
        var left = (int)Math.Round(cx - side / 2.0);
        var top = (int)Math.Round(cy - side / 2.0);
        // 圆盘后面那块屏幕，给毛玻璃用
        var diameter = (int)Math.Round(Diameter * Scale);
        var discLeft = (int)Math.Round(cx - diameter / 2.0);
        var discTop = (int)Math.Round(cy - diameter / 2.0);
        var shot = Frost.Capture(discLeft, discTop, diameter, diameter, Scale);
        Build(shot, discLeft, discTop);
        PlacePhysical(left, top, side, side);

        MarkShowing();
        root.BeginAnimation(OpacityProperty, null);
        root.Opacity = 0;
        scale.ScaleX = scale.ScaleY = 0.82;
        Show();
        // 移到另一个缩放比例的显示器时 WPF 会按系统建议的位置重新摆一次，这里再放回来
        Dispatcher.BeginInvoke(() => PlacePhysical(left, top, side, side), System.Windows.Threading.DispatcherPriority.Loaded);

        var spring = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, Ms(260)) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, Ms(260)) { EasingFunction = spring });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(150)));
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, Ms(120)) { EasingFunction = new QuadraticEase() });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, Ms(120)) { EasingFunction = new QuadraticEase() });
        FadeOutAndHide(root, 120);
    }

    /// 选中的内容读到了：更新中间的预览，用不了的格子变淡；链接之类的内容会换掉某些格子
    public void SetContent(ClassifiedContent selected, IReadOnlyList<PopAction> ringItems)
    {
        content = selected;
        if (!ReferenceEquals(ringItems, items) && !ringItems.SequenceEqual(items))
        {
            items = ringItems;
            var keep = highlighted;
            highlighted = null;
            RebuildLabels();
            Highlight(keep);
        }
        status.Text = selected.IsEmpty ? "没有选中文字" : CenterText(selected);
        status.Foreground = theme.Brush(selected.IsEmpty ? theme.TertiaryText : theme.SecondaryText);
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
        if (index is { } i && i < items.Count)
        {
            var available = content is null || items[i].IsAvailable(content);
            var fill = theme.Brush(available ? theme.Accent : theme.DisabledText);
            highlight.Fill = fill;
            highlight.Stroke = fill;
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
                    new DoubleAnimation(target, Ms(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            highlightAngle = target;
            highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(100)));
        }
        else
        {
            highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(100)));
        }
        RefreshLabels();
    }

    private static string Preview(string text)
    {
        var oneLine = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length > 36 ? oneLine[..36] + "…" : oneLine;
    }

    private void Build(Frost.Shot? shot, int discLeft, int discTop)
    {
        canvas.Children.Clear();

        // 阴影：画在一个不透明的圆上
        var shadow = new Ellipse
        {
            Width = Diameter,
            Height = Diameter,
            Fill = theme.Brush(theme.Tint),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = theme.ShadowOpacity, RenderingBias = RenderingBias.Quality },
        };
        Place(shadow, Pad, Pad);

        // 毛玻璃底
        var disc = new EllipseGeometry(new Point(OuterRadius, OuterRadius), OuterRadius, OuterRadius);
        disc.Freeze();
        Place(Frost.Layer(shot, discLeft, discTop, Diameter, Diameter, disc, theme), Pad, Pad);

        // 外描边和内侧高光
        Place(new Ellipse { Width = Diameter, Height = Diameter, Stroke = theme.Brush(theme.Stroke), StrokeThickness = 1 }, Pad, Pad);
        Place(new Ellipse
        {
            Width = Diameter - 2,
            Height = Diameter - 2,
            Stroke = new LinearGradientBrush(theme.InnerStroke, Colors.Transparent, 90),
            StrokeThickness = 1,
        }, Pad + 1, Pad + 1);

        // 格子之间的分隔线
        var count = Math.Max(items.Count, 1);
        var step = 360.0 / count;
        for (var i = 0; i < count; i++)
        {
            var angle = (i + 0.5) * step;
            var (x1, y1) = At(angle, InnerRadius + 10);
            var (x2, y2) = At(angle, OuterRadius - 16);
            canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = theme.Brush(theme.Divider), StrokeThickness = 1 });
        }

        // 高亮：主题色的一段圆环，和圆盘边缘、分隔线之间留出一样宽的空隙；描边加粗让四个角变圆
        highlight.Data = Segment(step, OuterRadius - HighlightInset - HighlightCorner, InnerRadius + HighlightInset + HighlightCorner, HighlightGap + HighlightCorner);
        highlight.RenderTransform = highlightRotation;
        highlight.BeginAnimation(OpacityProperty, null);
        highlight.Opacity = 0;
        canvas.Children.Add(highlight);

        // 中间的圆：显示选中内容的预览
        var centerDisc = new Ellipse
        {
            Width = InnerRadius * 2,
            Height = InnerRadius * 2,
            Fill = theme.Brush(theme.CenterFill),
            Stroke = theme.Brush(theme.Divider),
            StrokeThickness = 1,
        };
        Place(centerDisc, Center - InnerRadius, Center - InnerRadius);

        status.Text = "读取中…";
        status.FontFamily = Theme.TextFont;
        status.FontSize = Theme.Caption;
        status.LineHeight = 17;
        status.TextAlignment = TextAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Width = InnerRadius * 2 - 24;
        status.MaxHeight = 52;
        status.Foreground = theme.Brush(theme.TertiaryText);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.VerticalAlignment = VerticalAlignment.Center;
        if (status.Parent is Panel old) old.Children.Remove(status);
        var statusBox = new Grid { Width = InnerRadius * 2, Height = InnerRadius * 2 };
        statusBox.Children.Add(status);
        Place(statusBox, Center - InnerRadius, Center - InnerRadius);

        RebuildLabels();
    }

    private void RebuildLabels()
    {
        foreach (var (box, _, _) in labels) canvas.Children.Remove(box);
        labels.Clear();
        var count = items.Count;
        for (var i = 0; i < count; i++)
        {
            var icon = Icons.Make(items[i].Glyph, 22, theme.Brush(theme.Text));
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var title = new TextBlock
            {
                Text = items[i].Title,
                FontFamily = Theme.TextFont,
                FontSize = Theme.Caption,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 76,
            };
            var panel = new StackPanel { Width = 80, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(icon);
            panel.Children.Add(title);
            var box = new Grid { Width = 80, Height = 56 };
            box.Children.Add(panel);
            var (x, y) = At(RingGeometry.SectorAngle(i, count), LabelRadius);
            Place(box, x - 40, y - 28);
            labels.Add((box, icon, title));
        }
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < labels.Count && i < items.Count; i++)
        {
            var available = content is null || items[i].IsAvailable(content);
            var active = i == highlighted;
            var brush = theme.Brush(active ? theme.AccentText : theme.Text);
            labels[i].Icon.Foreground = brush;
            labels[i].Title.Foreground = brush;
            labels[i].Box.Opacity = active || available ? 1 : 0.38;
        }
    }

    private void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static (double X, double Y) At(double degrees, double radius)
    {
        var rad = degrees * Math.PI / 180;
        return (Center + radius * Math.Sin(rad), Center - radius * Math.Cos(rad));
    }

    /// 高亮和圆盘边缘、中间圆之间的空隙，和相邻格子分界线之间的空隙（DIP）
    private const double HighlightInset = 6;
    private const double HighlightGap = 3;
    /// 高亮四个角的圆角（描边宽度的一半）
    private const double HighlightCorner = 5;

    /// 正上方那一格的高亮形状：两条直边和这一格的分界线平行、离开 gap，所以空隙内外一样宽
    private static Geometry Segment(double step, double outer, double inner, double gap)
    {
        var half = step / 2;
        var outerMargin = Math.Asin(Math.Min(1, gap / outer)) * 180 / Math.PI;
        var innerMargin = Math.Asin(Math.Min(1, gap / inner)) * 180 / Math.PI;
        Point P(double degrees, double r)
        {
            var (x, y) = At(degrees, r);
            return new Point(x, y);
        }
        var from = -half;
        var to = half;
        var large = step - 2 * outerMargin > 180;
        var figure = new PathFigure { StartPoint = P(from + outerMargin, outer), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(P(to - outerMargin, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(to - innerMargin, inner), true));
        figure.Segments.Add(new ArcSegment(P(from + innerMargin, inner), new Size(inner, inner), 0, step - 2 * innerMargin > 180, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
