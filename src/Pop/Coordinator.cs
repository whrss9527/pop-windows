using System.Diagnostics;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 把各部分串起来：长按 → 弹出圆盘并读取选中内容 → 指针划向一格 → 松开执行
internal sealed class Coordinator : IDisposable
{
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_1 = 0x31;
    private const int VK_NUMPAD1 = 0x61;

    /// 选中的文字默认不写进日志；自动化测试时用 POP_LOG_SELECTION=1 打开
    private static readonly bool LogSelection = Environment.GetEnvironmentVariable("POP_LOG_SELECTION") == "1";

    private readonly Dispatcher dispatcher;
    private readonly InputHook hook;
    private readonly ClipboardAccess clipboard = new();
    private readonly SelectionReader reader;
    private readonly Paster paster;
    private readonly RingWindow ring = new();
    private readonly ResultCard card = new();
    private readonly Func<AppSettings> settings;
    private Session? session;

    // 钩子线程读这几个值决定要不要扣下按键
    private volatile bool ringOpen;
    private volatile bool cardOpen;
    private volatile int ringCount;

    /// 长按成立后最多等这么久读取选中内容：选中的是算式、单位、颜色这些时直接出结果，不用先弹圆盘再换成卡片
    private static readonly TimeSpan DirectWait = TimeSpan.FromMilliseconds(250);

    private sealed class Session(int x, int y, Task<Selection> selection)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public Task<Selection> Selection { get; } = selection;
        public IReadOnlyList<RingItem> Items { get; set; } = RingItems.Default;
        public bool RingShown { get; set; }
        /// 圆盘还没弹出来右键就松开了
        public bool ReleasedEarly { get; set; }
        public bool Finished { get; set; }
    }

    public Coordinator(Dispatcher dispatcher, InputHook hook, Func<AppSettings> settings)
    {
        this.dispatcher = dispatcher;
        this.hook = hook;
        this.settings = settings;
        reader = new SelectionReader(clipboard);
        paster = new Paster(clipboard);

        hook.Triggered += (x, y) => dispatcher.BeginInvoke(() => OnTriggered(x, y));
        hook.Moved += (x, y) => dispatcher.BeginInvoke(() => OnMoved(x, y));
        hook.Released += (x, y) => dispatcher.BeginInvoke(() => OnReleased(x, y));
        hook.OtherButtonDown += (x, y) =>
        {
            if (cardOpen) dispatcher.BeginInvoke(() => { if (!card.ContainsPhysical(x, y)) CloseCard(); });
        };
        hook.KeyFilter = FilterKey;
        card.CopyRequested += text => clipboard.SetText(text, temporary: false);
        card.ReplaceRequested += async text => await Replace(text);
        card.Dismissed += () => cardOpen = false;
    }

    public void Dispose()
    {
        ring.Close();
        card.Close();
        clipboard.Dispose();
    }

    private async void OnTriggered(int x, int y)
    {
        CloseCard();
        Log.Info($"长按 ({x}, {y})");
        var selection = ReadSelectionAsync();
        var s = new Session(x, y, selection);
        session = s;

        await Task.WhenAny(selection, Task.Delay(DirectWait));
        if (session != s || s.Finished) return;
        if (selection.IsCompleted)
        {
            var content = ContentClassifier.Classify(selection.Result.Text);
            if (DirectCard(content) is { } direct)
            {
                s.Finished = true;
                Log.Info($"直达结果：{direct.Title}");
                ShowResult(x, y, direct);
                return;
            }
            if (s.ReleasedEarly)
            {
                // 还没看到圆盘就松开了，当作没按
                s.Finished = true;
                return;
            }
            ShowRing(s, content);
            return;
        }

        ShowRing(s, null);
        var result = await selection;
        if (session == s && !s.Finished) UpdateRing(s, ContentClassifier.Classify(result.Text));
    }

    private CardContent? DirectCard(ClassifiedContent content) =>
        settings().DirectResults ? DirectResults.For(content) : null;

    private void ShowRing(Session s, ClassifiedContent? content)
    {
        s.Items = content is null ? RingItems.Default : RingItems.For(content);
        s.RingShown = true;
        ringCount = s.Items.Count;
        ringOpen = true;
        ring.ShowAt(s.X, s.Y, s.Items);
        if (content is not null) ring.SetContent(content, s.Items);
    }

    private void UpdateRing(Session s, ClassifiedContent content)
    {
        s.Items = RingItems.For(content);
        ringCount = s.Items.Count;
        ring.SetContent(content, s.Items);
    }

    private async Task<Selection> ReadSelectionAsync()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await reader.ReadAsync();
            Log.Info($"选中内容：来源 {result.Source}，窗口 {result.WindowClass}（{result.Process}），{result.Text.Length} 个字符，用时 {watch.ElapsedMilliseconds} ms{(LogSelection ? $"，内容「{Abbreviate(result.Text)}」" : "")}");
            return result;
        }
        catch (Exception e)
        {
            Log.Error("读取选中内容失败", e);
            return new Selection("", "error", "", "");
        }
    }

    private void OnMoved(int x, int y)
    {
        if (session is not { Finished: false } s) return;
        ring.Highlight(HitTest(s, x, y));
    }

    private async void OnReleased(int x, int y)
    {
        if (session is not { Finished: false } s) return;
        if (!s.RingShown)
        {
            s.ReleasedEarly = true;
            return;
        }
        var index = HitTest(s, x, y);
        await Finish(s, index);
    }

    private int? HitTest(Session s, int x, int y) =>
        RingGeometry.HitTest(x - s.X, y - s.Y, s.Items.Count, RingWindow.DeadRadius * ring.Scale);

    /// 钩子线程上调用，只看状态、立刻返回，真正的事交给界面线程
    private bool FilterKey(int vk)
    {
        if (ringOpen)
        {
            if (vk == VK_ESCAPE)
            {
                hook.DiscardCurrentPress();
                dispatcher.BeginInvoke(async () => { if (session is { Finished: false } s) await Finish(s, null); });
                return true;
            }
            var index = vk is >= VK_1 and <= VK_1 + 8 ? vk - VK_1 : vk is >= VK_NUMPAD1 and <= VK_NUMPAD1 + 8 ? vk - VK_NUMPAD1 : -1;
            if (index >= 0 && index < ringCount)
            {
                hook.DiscardCurrentPress();
                dispatcher.BeginInvoke(async () => { if (session is { Finished: false } s) await Finish(s, index); });
                return true;
            }
            return false;
        }
        if (cardOpen && vk == VK_ESCAPE)
        {
            dispatcher.BeginInvoke(CloseCard);
            return true;
        }
        if (cardOpen && vk == VK_RETURN)
        {
            // 回车：复制卡片上的结果
            dispatcher.BeginInvoke(() =>
            {
                if (card.PrimaryText is { Length: > 0 } text) clipboard.SetText(text, temporary: false);
                Log.Info("回车复制结果");
                CloseCard();
            });
            return true;
        }
        return false;
    }

    private async Task Finish(Session s, int? index)
    {
        s.Finished = true;
        ringOpen = false;
        ring.Dismiss();
        if (index is not { } i)
        {
            Log.Info("在圆心松开，关闭");
            return;
        }
        var item = s.Items[i];
        // 还没读完的话等它读完（通常只差几十毫秒）
        var selection = await s.Selection;
        Log.Info($"执行 {item.Id}");
        await Execute(item, ContentClassifier.Classify(selection.Text), s.X, s.Y);
    }

    private async Task Execute(RingItem item, ClassifiedContent content, int x, int y)
    {
        var text = content.Text;
        if (!RingItems.IsAvailable(item, text))
        {
            ShowToast(x, y, "没有读到选中的文字");
            return;
        }
        try
        {
            switch (item.Id)
            {
                case "copy":
                    clipboard.SetText(text, temporary: false);
                    ShowToast(x, y, "已复制");
                    break;
                case "search":
                    Open(TextActions.SearchUrl(text));
                    break;
                case "open":
                    Open(content.Url ?? content.Path ?? text);
                    break;
                case "translate":
                    Open(TextActions.TranslateUrl(text));
                    break;
                case "upper":
                    await Replace(TextActions.ToUpper(text));
                    break;
                case "lower":
                    await Replace(TextActions.ToLower(text));
                    break;
                case "count":
                    ShowResult(x, y, CardContent.Text("字数统计", TextActions.Describe(TextActions.Count(text))));
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"执行 {item.Id} 失败", e);
            ShowToast(x, y, "出错了，详情见日志");
        }
    }

    private void ShowResult(int x, int y, CardContent content)
    {
        card.ShowResult(x, y, content);
        cardOpen = true;
    }

    private async Task Replace(string text)
    {
        Log.Info("替换原文");
        await paster.PasteAsync(text);
    }

    private void ShowToast(int x, int y, string message)
    {
        cardOpen = false;
        card.ShowToast(x, y, message);
    }

    private void CloseCard()
    {
        cardOpen = false;
        card.Dismiss();
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static string Abbreviate(string text)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ");
        return flat.Length > 60 ? flat[..60] + "…" : flat;
    }
}
