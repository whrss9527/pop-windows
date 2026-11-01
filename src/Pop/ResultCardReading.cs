using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;

namespace Pop;

internal sealed partial class ResultCard
{
    private CardContent? shownContent;
    private TextPages? reading;
    private string readingTitle = "";
    private int resultsPage;
    private double readingReturnOffset;
    private volatile bool readingActionsEnabled;
    private ScrollViewer? cardScroller;
    internal double? VerificationHeightLimit { get; set; }
    public bool IsReading => readingActionsEnabled && Volatile.Read(ref reading) is not null;
    internal int? ReadingPage => reading?.Index;
    internal double BodyScrollOffset => cardScroller?.VerticalOffset ?? 0;
    internal bool HasRetainedReadingContent => reading is not null || shownContent is not null;

    private WpfUi.Button ReadButton(CardContent content, string text, string title, string id, Theme theme)
    {
        var button = Subtle(ActionButton(ResultPreview.Text(title, 18) + " · 查看全文", WpfUi.ControlAppearance.Transparent, theme), theme);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(-8, 2, 0, 4);
        button.FontSize = Theme.Caption;
        button.ToolTip = "在卡片中逐页阅读；复制和替换仍保留完整内容";
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) =>
        {
            if (!readingActionsEnabled || !ReferenceEquals(content, shownContent) || content.Loading) return;
            readingReturnOffset = cardScroller?.VerticalOffset ?? 0;
            CopyFailed = string.Equals(text, shownContent.PrimaryText, StringComparison.Ordinal) && primaryCopyFailed;
            reading = new TextPages(text);
            readingTitle = ResultPreview.Text(title, 80);
            RefreshReading();
        };
        return button;
    }

    private UIElement ReadingNavigation(Theme theme)
    {
        var previous = Subtle(ActionButton("上一页", WpfUi.ControlAppearance.Transparent, theme), theme);
        var next = Subtle(ActionButton("下一页", WpfUi.ControlAppearance.Transparent, theme), theme);
        previous.IsEnabled = reading!.HasPrevious; next.IsEnabled = reading.HasNext;
        previous.ToolTip = "上一页（PageUp）"; next.ToolTip = "下一页（PageDown）";
        previous.Click += (_, _) => MoveReadingPage(-1); next.Click += (_, _) => MoveReadingPage(1);
        System.Windows.Automation.AutomationProperties.SetAutomationId(previous, "card-read-previous");
        System.Windows.Automation.AutomationProperties.SetAutomationId(next, "card-read-next");
        var bar = new DockPanel { Margin = new Thickness(-8, 6, 0, 0) };
        DockPanel.SetDock(previous, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        bar.Children.Add(previous); bar.Children.Add(next);
        var progress = new TextBlock { Text = reading.HasNext ? "后面还有内容" : "已到末尾", FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.TertiaryText), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        bar.Children.Add(progress);
        return bar;
    }

    public bool MoveReadingPage(int direction)
    {
        if (!readingActionsEnabled || reading is null) return false;
        var moved = direction < 0 ? reading.Previous() : direction > 0 && reading.Next();
        if (moved) RefreshReading();
        return moved;
    }

    private void ReturnFromReading()
    {
        if (!readingActionsEnabled || reading is null) return;
        var offset = readingReturnOffset;
        CopyFailed = primaryCopyFailed;
        reading = null; readingTitle = ""; RefreshReading();
        cardScroller?.UpdateLayout();
        cardScroller?.ScrollToVerticalOffset(offset);
    }

    private void RefreshReading()
    {
        if (!readingActionsEnabled || !IsVisible || shownContent is null) return;
        var theme = Theme.Current();
        var display = shownContent;
        PrimaryText = reading?.FullText ?? shownContent.PrimaryText;
        copyPreviewTruncated = reading is not null || PrimaryText.Length > ResultPreview.BodyCharacters;
        if (reading is not null)
            display = new CardContent(shownContent.Title, [], Body: reading.Current.Text, Monospace: shownContent.Monospace,
                Caption: $"第 {reading.Index + 1} 页", Source: "正在阅读：" + readingTitle
                    + (reading.Current.SplitOversizedElement ? " · 过长字符组已分段显示，复制保留全文" : ""), Icon: shownContent.Icon);
        copyButton = null;
        Present(anchor.X, anchor.Y, Card(display, theme), theme, Theme.OverlayRadius, updating: true);
        UpdateCopyButton();
    }

    private UIElement ResultNavigation(CardContent content, int first, int count, Theme theme)
    {
        var previous = Subtle(ActionButton("上一组", WpfUi.ControlAppearance.Transparent, theme), theme);
        var next = Subtle(ActionButton("下一组", WpfUi.ControlAppearance.Transparent, theme), theme);
        previous.IsEnabled = first > 0; next.IsEnabled = first + count < content.Lines.Count;
        void Move(int direction)
        {
            if (!readingActionsEnabled || !ReferenceEquals(content, shownContent)) return;
            var lastPage = Math.Max(0, (content.Lines.Count - 1) / ResultPreview.LinesPerPage);
            resultsPage = Math.Clamp(resultsPage + direction, 0, lastPage);
            RefreshReading();
        }
        previous.Click += (_, _) => Move(-1); next.Click += (_, _) => Move(1);
        System.Windows.Automation.AutomationProperties.SetAutomationId(previous, "card-results-previous");
        System.Windows.Automation.AutomationProperties.SetAutomationId(next, "card-results-next");
        var bar = new DockPanel { Margin = new Thickness(-8, 6, 0, 6) };
        DockPanel.SetDock(previous, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        bar.Children.Add(previous); bar.Children.Add(next);
        bar.Children.Add(new TextBlock { Text = $"{first + 1}–{first + count} / {content.Lines.Count} 项", FontSize = Theme.Caption,
            Foreground = theme.Brush(theme.TertiaryText), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        return bar;
    }
}
