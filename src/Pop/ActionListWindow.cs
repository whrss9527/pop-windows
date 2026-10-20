using System.Windows;
using System.Windows.Controls;
using Pop.Core;

namespace Pop;

/// 「全部功能」列表：不抢焦点，原来的 App 一直在前台（替换原文直接粘贴回去）。
/// 键盘输入由全局键盘钩子转过来：字母、数字改搜索词（名称的拼音首字母、英文），方向键选择，回车执行，Esc 关闭
internal sealed class ActionListWindow : SearchOverlay<PopAction>
{
    private ClassifiedContent content = ClassifiedContent.Empty;
    private ActionAvailability availability = new(ClassifiedContent.Empty);
    private Func<string, bool> enabled = _ => true;
    private bool canReturn;
    private IReadOnlyList<PopAction>? originalRing;
    public bool CanReturnToRing => canReturn && SearchText.Length == 0;
    public event Action? BackRequested;
    private static readonly PopAction Back = new("backToRing", "返回圆盘", "ArrowLeft24", "fh fanhui back", ContentKind.None, _ => null,
        Summary: "返回刚才的菜单 · ← 或 Backspace");

    /// 选了一个功能（回车或者鼠标点）
    public event Action<PopAction>? Chosen;

    public ActionListWindow() : base("Pop 全部功能")
    {
    }

    protected override double PanelWidth => 380;
    protected override double ListHeight => 10 * 38;
    protected override string Placeholder => "搜索：拼音 / 英文 · Ctrl+V 粘贴中文";
    protected override IReadOnlyList<(string Keys, string Action)> Hints => [("↑↓", "选择"), ("Enter", "执行"), ("Esc", "关闭")];

    public void ShowFor(ClassifiedContent selectedContent, int x, int y, Func<string, bool> isEnabled,
        bool canReturnToRing = false, IReadOnlyList<PopAction>? ringActions = null)
    {
        canReturn = canReturnToRing;
        originalRing = ringActions;
        content = selectedContent;
        availability = new(selectedContent);
        enabled = isEnabled;
        Open(x, y);
        Log.Info("全部功能列表已显示");
    }

    protected override IEnumerable<Entry> Query(string text)
    {
        var actions = originalRing is { } original
            ? original.Where(a => a.Matches(text)).ToList()
            : Actions.Filter(text, content, enabled, availability.IsAvailable);
        if (canReturn && text.Length == 0) yield return new Entry(null, Back);
        if (text.Length > 0)
        {
            foreach (var action in actions) yield return new Entry(null, action, availability.IsAvailable(action));
            yield break;
        }
        // 没有搜索词时：能用的按分组列出，用不了的放在最后
        var available = actions.Where(a => availability.IsAvailable(a)).ToList();
        foreach (var category in Actions.Categories)
        {
            var group = available.Where(a => a.Category == category).ToList();
            if (group.Count == 0) continue;
            yield return new Entry(category, null);
            foreach (var action in group) yield return new Entry(null, action);
        }
        var unavailable = actions.Where(a => !availability.IsAvailable(a)).ToList();
        if (unavailable.Count == 0) yield break;
        yield return new Entry("当前内容用不了", null);
        foreach (var action in unavailable) yield return new Entry(null, action, false);
    }

    protected override FrameworkElement Row(PopAction action, bool selected, bool isEnabled, int index)
    {
        var theme = Palette;
        var line = new DockPanel { Height = 36, LastChildFill = true };
        var icon = Icons.Make(action.Glyph, 18, theme.Brush(selected ? theme.Accent : theme.Text));
        icon.Width = 28;
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        var title = new TextBlock
        {
            Text = action.Title,
            FontSize = Theme.Body,
            FontWeight = selected ? FontWeights.Medium : FontWeights.Normal,
            Foreground = theme.Brush(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(title, Dock.Left);
        line.Children.Add(title);
        line.Children.Add(new TextBlock
        {
            Text = action.Summary,
            FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.TertiaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 0, 0),
        });
        return line;
    }

    protected override string EmptyText(string text) => $"没有找到「{text}」";

    protected override void OnChosen(PopAction action)
    {
        if (action == Back) { BackRequested?.Invoke(); return; }
        Log.Info($"全部功能列表：选中 {action.Id}，搜索「{SearchText}」");
        Chosen?.Invoke(action);
    }
}
