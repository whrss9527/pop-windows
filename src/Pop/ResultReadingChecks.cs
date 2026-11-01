using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Pop.Core;

namespace Pop;

/// 在真实结果卡片中验证有限排版、全文操作、翻页与关闭后的任务失效。
internal static class ResultReadingChecks
{
    public static async Task VerifyAsync(App app, int x, int y, Action<string, ResultCard> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("长结果检查仅用于演示回归");
        const string record = "    {\"订单\": \"ORD-20261102\", \"状态\": \"待发货\", \"仓库\": \"上海\", \"金额\": 128.50, \"备注\": \"周末送达 👩‍💻\"}";
        var full = "{\r\n  \"记录\": [\r\n" + string.Concat(Enumerable.Repeat(record + ",\r\n", 2_000_000 / record.Length + 1)) + record + "\r\n  ]\r\n}";
        var source = "原文不同：" + full;
        string? copied = null, replaced = null;
        var allowCopy = false;
        var foreground = Native.GetForegroundWindow();
        var card = new ResultCard { VerificationHeightLimit = 330 };
        card.CopyRequested += text => { copied = text; return allowCopy; };
        card.ReplaceRequested += text => replaced = text;
        try
        {
            card.ShowResult(x, y, new CardContent("长结果阅读", [], Body: full, Replacement: full, Source: source, Monospace: true));
            await Task.Delay(250);
            AssertBounded(5500); AssertFooter("card-copy-result"); AssertFooter("card-replace-result");
            shot("result-long-preview", card);
            await Click("card-read-body");
            if (!card.IsReading || card.PrimaryText != full || card.ReadingPage != 0) throw new InvalidOperationException("正文全文入口未保留完整内容");
            await Click("card-read-next");
            if (card.ReadingPage != 1) throw new InvalidOperationException("长正文未翻到下一页");
            AssertBounded(5500); AssertFooter("card-copy-result");
            if (card.CopyPrimary() || !card.CopyFailed || copied != full || !card.IsOpen)
                throw new InvalidOperationException("分页中的复制失败未保留全文或错误关闭");
            await Task.Delay(350);
            if (!Find<ButtonBase>("card-read-previous").IsEnabled
                || !Tree(card).OfType<TextBlock>().Any(b => b.Text == "第 2 页")
                || Find<ButtonBase>("card-copy-result").Content?.ToString() != "重试复制")
                throw new InvalidOperationException("阅读页码、上一页或失败反馈未反映当前状态");
            shot("result-long-reading", card);
            await Click("card-read-back");
            if (card.IsReading || card.PrimaryText != full) throw new InvalidOperationException("返回结果后主要内容改变");
            await Click("card-read-source");
            if (card.PrimaryText != source) throw new InvalidOperationException("原文入口错误使用了正文");
            card.CopyPrimary();
            if (copied != source) throw new InvalidOperationException("原文分页未复制原文全文");
            await Click("card-read-back");
            await Click("card-replace-result");
            if (replaced != full) throw new InvalidOperationException("返回后替换内容被截短");
            await Task.Delay(220);
            if (card.PrimaryText is not null || card.HasRetainedReadingContent) throw new InvalidOperationException("隐藏后仍保留长结果引用");

            var last = full + "最后一组";
            var lines = Enumerable.Range(0, 30).Select(i => new ResultLine($"项目 {i}", i == 25 ? last : full)).ToList();
            card.ShowResult(x, y, new CardContent("多项转换结果", lines, Replacement: full, Monospace: true));
            await Task.Delay(180);
            AssertBounded(35_000);
            if (Tree(card).Count(e => AutomationProperties.GetAutomationId(e).StartsWith("card-result-line-", StringComparison.Ordinal)) != 12)
                throw new InvalidOperationException("多项结果未限制一次排版数量");
            await Click("card-results-next"); await Click("card-results-next");
            var row = Find<Border>("card-result-line-25"); row.BringIntoView(); await Task.Delay(45);
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            if (copied != last) throw new InvalidOperationException("后续结果行没有复制自己的全文");
            if (card.CopyFailed || Find<ButtonBase>("card-copy-result").Content?.ToString() != "复制")
                throw new InvalidOperationException("行内失败把主要结果误标为重试其他内容");
            var readLine = Find<ButtonBase>("card-read-line-25"); readLine.BringIntoView(); await Task.Delay(60);
            var returnOffset = card.BodyScrollOffset;
            await Click("card-read-line-25");
            if (card.PrimaryText != last) throw new InvalidOperationException("后续结果行阅读了其他项");
            if (card.CopyPrimary() || !card.CopyFailed || copied != last) throw new InvalidOperationException("阅读项重试未对应当前全文");
            await Click("card-read-back");
            if (Find<Border>("card-result-line-25") is null || card.PrimaryText != full) throw new InvalidOperationException("返回阅读后未保留结果分组");
            if (card.CopyFailed || Find<ButtonBase>("card-copy-result").Content?.ToString() != "复制")
                throw new InvalidOperationException("返回主要结果后仍显示其他阅读项的失败状态");
            await Task.Delay(350);
            if (Math.Abs(card.BodyScrollOffset - returnOffset) > 1) throw new InvalidOperationException("返回阅读后未恢复原滚动位置");
            shot("result-many-items", card);

            var shorter = string.Concat(Enumerable.Repeat("分页阅读\r\n", 1250));
            card.ShowResult(x, y, CardContent.Text("末页检查", shorter)); await Task.Delay(100);
            await Click("card-read-body");
            while (card.MoveReadingPage(1)) { }
            if (card.ReadingPage < 1 || Find<ButtonBase>("card-read-next").IsEnabled) throw new InvalidOperationException("末页仍可向后翻页");
            if (!card.MoveReadingPage(-1)) throw new InvalidOperationException("末页不能返回上一页");
            allowCopy = true;
            if (!card.CopyPrimary() || copied != shorter) throw new InvalidOperationException("分页复制成功时只复制了当前页");
            if (card.MoveReadingPage(1)) throw new InvalidOperationException("收起后的旧翻页任务重新打开卡片");
            await Task.Delay(200);
            if (card.HasRetainedReadingContent) throw new InvalidOperationException("全文复制后仍保留阅读状态");

            card.ShowResult(x, y, new CardContent("复制恢复", [], Body: full)); await Task.Delay(100);
            await Click("card-read-body");
            card.ShowResult(x, y, new CardContent("正在加载", [], Body: "正在处理…", Loading: true));
            if (card.IsReading || card.PrimaryText is not null || card.MoveReadingPage(1)) throw new InvalidOperationException("新加载状态未取消旧阅读任务");
            card.ShowCard(x, y, "新结果", "最新完整内容");
            if (card.PrimaryText != "最新完整内容" || card.IsReading) throw new InvalidOperationException("新结果复用了旧的阅读内容");
            if (Native.GetForegroundWindow() != foreground) throw new InvalidOperationException("分页阅读抢走了原应用焦点");
        }
        finally { card.Close(); }

        void AssertBounded(int total)
        {
            var blocks = Tree(card).OfType<TextBlock>().ToList();
            if (blocks.Any(b => b.Text.Length > ResultPreview.BodyCharacters + 1) || blocks.Sum(b => b.Text.Length) > total)
                throw new InvalidOperationException("实际卡片仍将完整长文本交给界面排版");
            if (card.ActualHeight > 330 + 2 * Theme.ShadowPad + 1) throw new InvalidOperationException("长结果没有适应矮工作区");
        }
        void AssertFooter(string id)
        {
            var button = Find<ButtonBase>(id);
            var position = button.TranslatePoint(new Point(), card);
            if (!button.IsVisible || position.Y < 0 || position.Y + button.ActualHeight > card.ActualHeight)
                throw new InvalidOperationException("长结果把底部操作挤出了卡片");
        }
        T Find<T>(string id) where T : DependencyObject => Tree(card).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
        async Task Click(string id)
        {
            var button = Find<ButtonBase>(id);
            if (!button.IsEnabled) throw new InvalidOperationException("不可用的阅读按钮：" + id);
            button.BringIntoView(); await Task.Delay(25);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(70);
        }
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject element)
    {
        yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
}
