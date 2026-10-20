namespace Pop.Core;

public enum AnnotationTool { Arrow, Rectangle, Ellipse, Pen, Text, Mosaic, Counter }
public readonly record struct ImagePoint(double X, double Y);

/// 标注坐标使用原图像素；缩放只影响显示，不改变导出尺寸。
public sealed record AnnotationStroke(AnnotationTool Tool, IReadOnlyList<ImagePoint> Points,
    string Color, double Width, string Text = "", int Counter = 0)
{
    public ImagePoint Start => Points.Count > 0 ? Points[0] : default;
    public ImagePoint End => Points.Count > 0 ? Points[^1] : default;
    public Rect Bounds => new(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
    public bool IsMeaningful => Tool switch
    {
        AnnotationTool.Text => !string.IsNullOrWhiteSpace(Text),
        AnnotationTool.Counter => true,
        AnnotationTool.Pen => Points.Count > 1 && Points.Any(p => p != Start),
        AnnotationTool.Arrow => Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2) >= 16,
        _ => Bounds.Width >= 3 && Bounds.Height >= 3,
    };
}

public sealed class AnnotationDocument
{
    private readonly List<AnnotationStroke> strokes = [];
    private readonly Stack<AnnotationStroke> undone = new();
    public IReadOnlyList<AnnotationStroke> Strokes => strokes.AsReadOnly();
    public bool CanUndo => strokes.Count > 0;
    public bool CanRedo => undone.Count > 0;
    public int NextCounter => strokes.Count(s => s.Tool == AnnotationTool.Counter) + 1;

    public void Add(AnnotationStroke stroke)
    {
        if (!stroke.IsMeaningful) return;
        // 保存快照，不依赖鼠标拖动时还在修改的点列表。
        strokes.Add(stroke with { Points = stroke.Points.ToArray() });
        undone.Clear();
    }
    public void Undo() { if (CanUndo) { undone.Push(strokes[^1]); strokes.RemoveAt(strokes.Count - 1); } }
    public void Redo() { if (CanRedo) strokes.Add(undone.Pop()); }
}
