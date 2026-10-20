using System.Windows;
using System.Windows.Controls;
using Pop.Core;

namespace Pop;

internal sealed class SnippetWindow : SearchOverlay<Snippet>
{
    private IReadOnlyList<Snippet> snippets = [];
    public event Action<Snippet>? Chosen;
    public SnippetWindow() : base("Pop 常用短语") { }
    protected override double PanelWidth => 420;
    protected override double ListHeight => 7 * 58;
    protected override string Placeholder => "搜索短语 · Ctrl+V 粘贴搜索词";
    protected override IReadOnlyList<(string Keys, string Action)> Hints => [("↑↓", "选择"), ("Enter", "粘贴"), ("Ctrl 1–9", "直选"), ("Esc", "关闭")];
    public void ShowAt(IReadOnlyList<Snippet> items, int x, int y) { snippets = items; Open(x, y); }
    protected override IEnumerable<Entry> Query(string query) => snippets.Where(s => s.Matches(query)).Select(s => new Entry(null, s));
    protected override string EmptyText(string query) => snippets.Count == 0 ? "还没有短语，请在设置 → 常用短语中添加" : "没有找到匹配的短语";
    protected override void OnChosen(Snippet item) => Chosen?.Invoke(item);
    protected override FrameworkElement Row(Snippet item, bool selected, bool enabled, int index)
    {
        var panel = new StackPanel { Margin = new Thickness(3, 7, 3, 7) };
        panel.Children.Add(new TextBlock { Text = item.DisplayTitle, FontSize = Theme.Body, Foreground = Palette.Brush(selected ? Palette.Accent : Palette.Text), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = item.Text.Replace("\r", "").Replace("\n", " ↵ "), Margin = new Thickness(0, 4, 0, 0),
            FontSize = Theme.Caption, Foreground = Palette.Brush(Palette.SecondaryText), TextTrimming = TextTrimming.CharacterEllipsis });
        return panel;
    }
}
