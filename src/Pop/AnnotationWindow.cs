using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Pop.Core;
using Rect = System.Windows.Rect;

namespace Pop;

/// 截图标注：工具快捷键和 macOS 一致，原图坐标绘制，复制、保存、贴图共用导出。
internal sealed class AnnotationWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AnnotationDocument document = new();
    private readonly BitmapSource image;
    private readonly Surface surface;
    private readonly TextBlock status = new() { Margin = new Thickness(12, 4, 12, 8) };
    private readonly Dictionary<AnnotationTool, ToggleButton> tools = [];
    private readonly int originX, originY;
    private AnnotationTool tool = AnnotationTool.Arrow;
    private string color = "#ED3833";
    private double lineWidth = 4;
    private List<ImagePoint>? points;
    private TextBox? textEditor;
    private ImagePoint textAnchor;

    public AnnotationWindow(byte[] png, int x, int y)
    {
        (originX, originY) = (x, y);
        image = ScreenCapture.ToBitmapSource(png);
        Title = "Pop 截图标注";
        Width = Math.Clamp(image.PixelWidth + 40, 760, 1200);
        Height = Math.Clamp(image.PixelHeight + 190, 460, 850);
        MinWidth = 640; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.TextFont;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
        var theme = Theme.Current();
        Background = theme.Brush(theme.Tint);
        Foreground = theme.Brush(theme.Text);
        Theme.ApplyTextOptions(this);
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        foreach (var (kind, label) in new[]
        {
            (AnnotationTool.Arrow, "箭头 A"), (AnnotationTool.Rectangle, "方框 R"), (AnnotationTool.Ellipse, "椭圆 O"),
            (AnnotationTool.Pen, "画笔 P"), (AnnotationTool.Text, "文字 T"), (AnnotationTool.Mosaic, "马赛克 M"), (AnnotationTool.Counter, "序号 N"),
        })
        {
            var button = new ToggleButton { Content = label, Margin = new Thickness(2), Padding = new Thickness(9, 6, 9, 6), IsChecked = kind == tool };
            button.Click += (_, _) => Choose(kind);
            tools.Add(kind, button); toolbar.Children.Add(button);
        }
        var palette = new ComboBox { Width = 96, Margin = new Thickness(8, 2, 2, 2) };
        var colors = new[] { ("红色", "#ED3833"), ("橙色", "#FF9400"), ("黄色", "#FFD60A"), ("绿色", "#33C759"), ("蓝色", "#007AFF"), ("黑色", "#000000"), ("白色", "#FFFFFF") };
        foreach (var (name, _) in colors) palette.Items.Add(name);
        palette.SelectedIndex = 0;
        palette.SelectionChanged += (_, _) => color = colors[palette.SelectedIndex].Item2;
        toolbar.Children.Add(palette);
        var width = new ComboBox { Width = 90, Margin = new Thickness(2) };
        foreach (var n in new[] { 2, 4, 6, 8 }) width.Items.Add($"{n} 像素");
        width.SelectedIndex = 1;
        width.SelectionChanged += (_, _) => lineWidth = new[] { 2, 4, 6, 8 }[width.SelectedIndex];
        toolbar.Children.Add(width);
        var actions = new WrapPanel { Margin = new Thickness(10, 0, 10, 6), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        void Add(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(12, 7, 12, 7) };
            button.Click += (_, _) => action(); actions.Children.Add(button);
        }
        Add("撤销 Ctrl+Z", () => { CommitText(); document.Undo(); Refresh(); });
        Add("重做 Ctrl+Y", () => { CommitText(); document.Redo(); Refresh(); });
        Add("复制 Ctrl+C", Copy);
        Add("保存 Ctrl+S", Save);
        Add("贴图", Pin);
        Add("关闭 Esc", Close);
        status.Text = $"{image.PixelWidth} × {image.PixelHeight} 像素 · 拖动绘制，文字和序号点击放置";
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        surface = new Surface(image, document) { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Transparent, Cursor = Cursors.Cross, ClipToBounds = true };
        var view = new Viewbox { Stretch = Stretch.Uniform, Child = surface, Margin = new Thickness(14), StretchDirection = StretchDirection.DownOnly };
        root.Children.Add(new Border { Background = theme.Brush(theme.CenterFill), Child = view, Margin = new Thickness(10, 0, 10, 10), CornerRadius = new CornerRadius(8) });
        Content = root;
        surface.MouseLeftButtonDown += (_, e) =>
        {
            CommitText();
            var p = Point(e);
            if (tool == AnnotationTool.Text) { EditText(p); return; }
            if (tool == AnnotationTool.Counter)
            {
                document.Add(new(tool, [p], color, lineWidth, Counter: document.NextCounter)); Refresh(); return;
            }
            points = [p]; surface.CaptureMouse(); UpdateCurrent();
        };
        surface.MouseMove += (_, e) =>
        {
            if (points is null) return;
            var p = Point(e);
            if (tool == AnnotationTool.Pen) points.Add(p);
            else if (points.Count == 1) points.Add(p);
            else points[1] = p;
            UpdateCurrent();
        };
        surface.MouseLeftButtonUp += (_, _) => EndStroke();
        surface.LostMouseCapture += (_, _) => EndStroke();
        PreviewKeyDown += OnKey;
    }

    private ImagePoint Point(MouseEventArgs e)
    {
        var p = e.GetPosition(surface);
        return new(Math.Clamp(p.X, 0, image.PixelWidth), Math.Clamp(p.Y, 0, image.PixelHeight));
    }
    private void UpdateCurrent() { surface.Current = points is null ? null : new(tool, points, color, lineWidth); Refresh(); }
    private void EndStroke()
    {
        if (points is null) return;
        document.Add(new(tool, points, color, lineWidth));
        points = null; surface.Current = null;
        surface.ReleaseMouseCapture(); Refresh();
    }
    private void Refresh() => surface.InvalidateVisual();
    private void Choose(AnnotationTool selected)
    {
        CommitText(); EndStroke(); tool = selected;
        foreach (var (kind, button) in tools) button.IsChecked = kind == tool;
    }
    private void EditText(ImagePoint p)
    {
        textAnchor = p;
        textEditor = new TextBox { MinWidth = 100, MaxWidth = Math.Max(100, image.PixelWidth - p.X), FontSize = Math.Max(16, lineWidth * 5) };
        Canvas.SetLeft(textEditor, p.X); Canvas.SetTop(textEditor, p.Y);
        surface.Children.Add(textEditor);
        textEditor.Focus();
        textEditor.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitText(); e.Handled = true; } };
    }
    private void CommitText()
    {
        if (textEditor is null) return;
        document.Add(new(AnnotationTool.Text, [textAnchor], color, lineWidth, Text: textEditor.Text));
        surface.Children.Remove(textEditor); textEditor = null; Refresh();
    }
    private BitmapSource Export() { CommitText(); EndStroke(); return AnnotationRenderer.Render(image, document.Strokes); }
    private void Copy()
    {
        try { Clipboard.SetImage(Export()); status.Text = "已复制标注后的图片"; }
        catch (System.Runtime.InteropServices.COMException e) { Log.Error("复制标注失败", e); status.Text = "剪贴板忙，请重试"; }
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"Pop 标注 {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png", DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, AnnotationRenderer.Png(Export())); status.Text = "图片已保存"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("保存标注失败", e); status.Text = "保存失败，请检查文件夹权限"; }
    }
    private void Pin()
    {
        var png = AnnotationRenderer.Png(Export());
        new PinWindow(png, originX, originY, Native.MonitorAt(originX, originY).Scale).Show();
        Close();
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (textEditor?.IsKeyboardFocusWithin == true)
        {
            if (e.Key == Key.Escape) { surface.Children.Remove(textEditor); textEditor = null; e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z: document.Undo(); Refresh(); break;
                case Key.Y: document.Redo(); Refresh(); break;
                case Key.C: Copy(); break;
                case Key.S: Save(); break;
                default: return;
            }
        }
        else if (e.Key == Key.Escape) Close();
        else
        {
            var selected = e.Key switch { Key.A => AnnotationTool.Arrow, Key.R => AnnotationTool.Rectangle, Key.O => AnnotationTool.Ellipse, Key.P => AnnotationTool.Pen,
                Key.T => AnnotationTool.Text, Key.M => AnnotationTool.Mosaic, Key.N => AnnotationTool.Counter, _ => (AnnotationTool?)null };
            if (selected is not { } kind) return;
            Choose(kind);
        }
        e.Handled = true;
    }

    /// 演示模式也使用真正的标注模型和导出路径。
    public void Preview()
    {
        document.Add(new(AnnotationTool.Rectangle, [new(40, 35), new(image.PixelWidth - 40, image.PixelHeight - 35)], "#007AFF", 4));
        document.Add(new(AnnotationTool.Arrow, [new(60, image.PixelHeight - 50), new(image.PixelWidth / 2, image.PixelHeight / 2)], "#ED3833", 4));
        document.Add(new(AnnotationTool.Text, [new(65, 50)], "#ED3833", 4, Text: "重点在这里"));
        document.Add(new(AnnotationTool.Counter, [new(image.PixelWidth - 70, 60)], "#ED3833", 4, Counter: 1));
        Refresh();
    }

    private sealed class Surface(BitmapSource image, AnnotationDocument document) : Canvas
    {
        public AnnotationStroke? Current { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            foreach (var stroke in document.Strokes) AnnotationRenderer.Draw(dc, image, stroke);
            if (Current is { } current) AnnotationRenderer.Draw(dc, image, current);
        }
    }
}
