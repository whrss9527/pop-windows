using System.Diagnostics;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 把各部分串起来：长按 → 弹出圆盘并读取选中内容 → 指针划向一格 → 松开执行
internal sealed partial class Coordinator : IDisposable
{
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;

    /// 选中的文字默认不写进日志；自动化测试时用 POP_LOG_SELECTION=1 打开
    private static readonly bool LogSelection = Environment.GetEnvironmentVariable("POP_LOG_SELECTION") == "1";

    private readonly Dispatcher dispatcher;
    private readonly InputHook hook;
    private readonly ClipboardAccess clipboard = new();
    private readonly SelectionReader reader;
    private readonly Paster paster;
    private readonly RingWindow ring = new();
    private readonly ResultCard card = new();
    private readonly ActionListWindow actionList = new();
    private readonly ClipboardHistoryWindow historyWindow = new();
    private readonly ClipboardHistory history;
    private readonly RegionSelector selector = new();
    private readonly ColorPicker colorPicker = new();
    private bool screenToolBusy;
    private readonly Func<AppSettings> settings;
    private Session? session;

    // 钩子线程读这几个值决定要不要扣下按键
    private volatile bool ringOpen;
    private volatile bool cardOpen;
    private volatile bool listOpen;
    private volatile bool historyOpen;
    private volatile int ringCount;

    /// 长按成立后最多等这么久读取选中内容：选中的是算式、单位、颜色这些时直接出结果，不用先弹圆盘再换成卡片
    private static readonly TimeSpan DirectWait = TimeSpan.FromMilliseconds(250);

    private sealed class Session(int x, int y, Task<Selection> selection, string application = "")
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public Task<Selection> Selection { get; } = selection;
        public string Application { get; } = application;
        public IReadOnlyList<PopAction> Items { get; set; } = [];
        public bool RingShown { get; set; }
        /// 圆盘还没弹出来右键就松开了
        public bool ReleasedEarly { get; set; }
        public bool Finished { get; set; }
        public bool WaitingForClick { get; set; }
        public int? KeyboardIndex { get; set; }
        public IReadOnlyList<PopAction> OriginalItems { get; set; } = [];
    }

    private readonly Func<PluginStore> plugins;
    private readonly PluginRunner pluginRunner;

    /// 正在运行的插件：关掉「正在运行」的卡片时取消
    private CancellationTokenSource? pluginRun;

    public Coordinator(Dispatcher dispatcher, InputHook hook, Func<AppSettings> settings, ClipboardHistory history, Func<PluginStore> plugins, PluginRunner pluginRunner)
    {
        this.dispatcher = dispatcher;
        this.hook = hook;
        this.settings = settings;
        this.history = history;
        this.plugins = plugins;
        this.pluginRunner = pluginRunner;
        reader = new SelectionReader(clipboard);
        reader.CopyStarting += () => history.Monitor.IgnoreChangesFor(SelectionReader.CopyIgnoreWindow);
        PinWindow.RecognizeRequested += async (png, px, py) => await RecognizeAsync(png, px, py);
        historyWindow.Chosen += async item =>
        {
            historyOpen = false;
            await PasteFromHistory(item);
        };
        paster = new Paster();

        hook.Triggered += (x, y) => dispatcher.BeginInvoke(() => OnTriggered(x, y));
        hook.ClickTriggered += (x, y) => dispatcher.BeginInvoke(async () => await OpenFromClickAsync(x, y));
        hook.Moved += (x, y) => dispatcher.BeginInvoke(() => OnMoved(x, y));
        hook.Released += (x, y) => dispatcher.BeginInvoke(() => OnReleased(x, y));
        hook.OtherButtonDown += (x, y) =>
        {
            if (ringOpen) dispatcher.BeginInvoke(async () =>
            {
                if (session is { Finished: false, WaitingForClick: true } s && !ring.ContainsPhysical(x, y)) await Finish(s, null);
            });
            if (cardOpen) dispatcher.BeginInvoke(() => { if (!card.ContainsPhysical(x, y)) CloseCard(); });
            if (listOpen) dispatcher.BeginInvoke(() => { if (!actionList.ContainsPhysical(x, y)) CloseList(); });
            if (historyOpen) dispatcher.BeginInvoke(() => { if (!historyWindow.ContainsPhysical(x, y)) CloseHistory(); });
        };
        hook.KeyFilter = FilterKey;
        SetUpSelectionToolbar();
        actionList.BackRequested += ReturnToRing;
        ring.Clicked += async index =>
        {
            if (session is { Finished: false, WaitingForClick: true } s) await Finish(s, index);
        };
        card.CopyRequested += text => clipboard.SetText(text, temporary: false);
        card.LinkRequested += Open;
        card.ReplaceRequested += async text => await Replace(text);
        card.Dismissed += () =>
        {
            cardOpen = false;
            pluginRun?.Cancel();
        };
        actionList.Chosen += async action =>
        {
            listOpen = false;
            if (lastList is not { } last) return;
            Log.Info($"执行 {action.Id}（全部功能）");
            await Run(action, last.Content, last.X, last.Y);
        };
    }

    /// 启动后界面空闲时调用：先把第一次显示卡片要载入的东西载入好
    public void WarmUp()
    {
        var watch = Stopwatch.StartNew();
        card.WarmUp();
        Log.Info($"卡片准备好了，用时 {watch.ElapsedMilliseconds} ms");
    }

    public void Dispose()
    {
        toolbar.Close();
        selector.Cancel();
        colorPicker.Cancel();
        ring.Close();
        card.Close();
        actionList.Close();
        historyWindow.Close();
        paster.Dispose();
        clipboard.Dispose();
    }

    private async void OnTriggered(int x, int y)
    {
        if (!hook.AllowsMouseTrigger) { hook.DiscardCurrentPress(); return; }
        toolbar.Dismiss();
        if (session is { Finished: false } previous) await Finish(previous, null);
        CloseCard();
        CloseList();
        CloseHistory();
        Log.Info($"长按 ({x}, {y})");
        var selection = ReadSelectionAsync();
        var s = new Session(x, y, selection, Native.ProcessNameOf(Native.GetForegroundWindow()));
        session = s;

        await Task.WhenAny(selection, Task.Delay(DirectWait));
        if (session != s || s.Finished) return;
        ClassifiedContent? content = null;
        if (selection.IsCompleted)
        {
            content = ContentClassifier.Classify(selection.Result.Text);
            if (settings().DirectActionFor(content) is { } direct)
            {
                s.Finished = true;
                Log.Info($"直达结果：{direct.Title}");
                await Run(direct, content, x, y);
                return;
            }
        }
        if (s.ReleasedEarly)
        {
            // 还没看到圆盘就松开了（Pop 忙的时候也会这样），当作没按
            s.Finished = true;
            Log.Info("圆盘弹出前就松开了，不执行");
            return;
        }

        ShowRing(s, content);
        if (content is not null) return;
        var result = await selection;
        if (session == s && !s.Finished) UpdateRing(s, ContentClassifier.Classify(result.Text));
    }

    private IReadOnlyList<PopAction> RingFor(Session s) => RingItems.Build(settings().SlotsFor(s.Application), settings().IsEnabled);

    /// 快捷键唤起时没有右键按压，直接进入点击选择模式。
    public async void ShowRingFromShortcut()
    {
        toolbar.Dismiss();
        if (screenToolBusy || selector.IsOpen || colorPicker.IsOpen) return;
        if (session is { Finished: false } previous) { await Finish(previous, null); return; }
        CloseCard(); CloseList(); CloseHistory();
        Native.GetCursorPos(out var cursor);
        var target = Native.GetForegroundWindow();
        // 组合键还按着时不能模拟 Ctrl+C，否则 Alt/Win 会把按键变成别的快捷键。
        if (!await WaitForModifiersAsync() || Native.GetForegroundWindow() != target) return;
        var s = new Session(cursor.X, cursor.Y, ReadSelectionAsync(), Native.ProcessNameOf(target)) { WaitingForClick = true };
        session = s;
        ShowRing(s, null);
        ring.SetClickable(true);
        var selection = await s.Selection;
        if (!s.Finished) UpdateRing(s, ContentClassifier.Classify(selection.Text));
    }

    private async Task OpenFromClickAsync(int x, int y)
    {
        toolbar.Dismiss();
        if (!hook.AllowsMouseTrigger) return;
        if (screenToolBusy || selector.IsOpen || colorPicker.IsOpen) return;
        var target = Native.GetForegroundWindow();
        if (!await WaitForModifiersAsync() || Native.GetForegroundWindow() != target) return;
        if (session is { Finished: false } previous) { await Finish(previous, null); return; }
        CloseCard(); CloseList(); CloseHistory();
        var s = new Session(x, y, ReadSelectionAsync(), Native.ProcessNameOf(target)) { WaitingForClick = true };
        session = s;
        await Task.WhenAny(s.Selection, Task.Delay(DirectWait));
        if (session != s || s.Finished) return;
        if (!s.Selection.IsCompleted)
        {
            ShowRing(s, null); ring.SetClickable(true);
            var late = await s.Selection;
            if (session == s && !s.Finished) UpdateRing(s, ContentClassifier.Classify(late.Text));
            return;
        }
        var selection = await s.Selection;
        if (session != s || s.Finished) return;
        var content = ContentClassifier.Classify(selection.Text);
        if (settings().DirectActionFor(content) is { } direct)
        {
            s.Finished = true;
            await Run(direct, content, x, y);
            return;
        }
        ShowRing(s, content); ring.SetClickable(true);
    }

    private static async Task<bool> WaitForModifiersAsync()
    {
        for (var n = 0; n < 100; n++)
        {
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(key => (Native.GetAsyncKeyState(key) & 0x8000) == 0)) return true;
            await Task.Delay(15);
        }
        return false;
    }

    public async void RunActionFromShortcut(string id)
    {
        toolbar.Dismiss();
        if (screenToolBusy || selector.IsOpen || colorPicker.IsOpen || !settings().IsEnabled(id) || Actions.Find(id) is not { } action) return;
        var target = Native.GetForegroundWindow();
        if (!await WaitForModifiersAsync() || Native.GetForegroundWindow() != target) return;
        if (session is { Finished: false } s) await Finish(s, null);
        CloseCard(); CloseList(); CloseHistory();
        Native.GetCursorPos(out var cursor);
        var needsSelection = action.Requires != ContentKind.None || action.Id == Actions.All.Id || action.Category == Actions.PluginCategory;
        var content = needsSelection ? ContentClassifier.Classify((await ReadSelectionAsync()).Text) : ClassifiedContent.Empty;
        if (Native.GetForegroundWindow() != target) return;
        if (action.Id == Actions.All.Id) ShowList(content, cursor.X, cursor.Y);
        else await Execute(action, content, cursor.X, cursor.Y);
    }

    private void ShowRing(Session s, ClassifiedContent? content, IReadOnlyList<PopAction>? originalItems = null)
    {
        var watch = Stopwatch.StartNew();
        s.Items = originalItems ?? (content is null ? RingFor(s) : RingItems.For(RingFor(s), content));
        s.OriginalItems = s.Items;
        s.RingShown = true;
        ringCount = s.Items.Count;
        ringOpen = true;
        ring.ShowAt(s.X, s.Y, s.Items);
        if (content is not null) ring.SetContent(content, s.OriginalItems);
        s.Items = ring.Items;
        ringCount = s.Items.Count;
        LogIfSlow("弹出圆盘", watch);
    }

    private void UpdateRing(Session s, ClassifiedContent content)
    {
        s.Items = RingItems.For(RingFor(s), content);
        s.OriginalItems = s.Items;
        ring.SetContent(content, s.Items);
        s.Items = ring.Items;
        ringCount = s.Items.Count;
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
        s.KeyboardIndex = null;
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
        var index = s.KeyboardIndex ?? HitTest(s, x, y);
        if (RingNavigation.KeepAfterRelease(s.WaitingForClick, settings().KeepRingOpen, index))
        {
            s.WaitingForClick = true;
            ring.SetClickable(true);
            return;
        }
        await Finish(s, index);
    }

    private int? HitTest(Session s, int x, int y) =>
        ring.Layout.HitTest(x - s.X, y - s.Y, s.Items.Count, RingWindow.DeadRadius * ring.Scale);

    /// 钩子线程上调用，只看状态、立刻返回，真正的事交给界面线程
    private bool FilterKey(int vk)
    {
        if (toolbar.IsOpen)
        {
            dispatcher.BeginInvoke(toolbar.Dismiss);
            if (vk == VK_ESCAPE) return true;
        }
        if (colorPicker.IsOpen)
        {
            if (vk != VK_ESCAPE) return false;
            dispatcher.BeginInvoke(colorPicker.Cancel);
            return true;
        }
        if (selector.IsOpen)
        {
            if (vk != VK_ESCAPE) return false;
            dispatcher.BeginInvoke(selector.Cancel);
            return true;
        }
        if (listOpen) return FilterListKey(vk);
        if (historyOpen) return FilterHistoryKey(vk);
        if (ringOpen)
        {
            static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
            if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C))
            {
                if (vk is 0x11 or 0x12 or 0x5B or 0x5C or 0xA2 or 0xA3 or 0xA4 or 0xA5) return false;
                hook.DiscardCurrentPress();
                dispatcher.BeginInvoke(async () => { if (session is { Finished: false } s) await Finish(s, null); });
                return false;
            }
            if (vk == VK_ESCAPE)
            {
                hook.DiscardCurrentPress();
                dispatcher.BeginInvoke(async () => { if (session is { Finished: false } s) await Finish(s, null); });
                return true;
            }
            if (vk is 0x25 or 0x26 or 0x27 or 0x28)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (session is not { Finished: false } s) return;
                    s.KeyboardIndex = RingNavigation.Move(s.KeyboardIndex ?? ring.Highlighted, vk is 0x25 or 0x26 ? -1 : 1, s.Items.Count);
                    ring.Highlight(s.KeyboardIndex);
                });
                return true;
            }
            if (vk == VK_RETURN)
            {
                hook.DiscardCurrentPress();
                dispatcher.BeginInvoke(async () => { if (session is { Finished: false } s) await Finish(s, s.KeyboardIndex ?? ring.Highlighted); });
                return true;
            }
            if (RingNavigation.NumberIndex(vk, ringCount) is { } index)
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

    /// 「全部功能」列表打开时，按键都交给列表；按着 Ctrl、Alt、Win 的组合键照常交给系统，同时关掉列表
    private bool FilterListKey(int vk)
    {
        const int VK_BACK = 0x08, VK_UP = 0x26, VK_DOWN = 0x28;
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C))
        {
            var held = HeldModifiers();
            dispatcher.BeginInvoke(() =>
            {
                Log.Info($"全部功能列表：按着 {held} 按了别的键，关闭");
                CloseList();
            });
            return false;
        }
        var typed = TypedChar(vk);
        switch (vk)
        {
            case VK_ESCAPE:
                dispatcher.BeginInvoke(CloseList);
                return true;
            case VK_RETURN:
                dispatcher.BeginInvoke(actionList.Choose);
                return true;
            case VK_UP:
                dispatcher.BeginInvoke(() => actionList.Move(-1));
                return true;
            case VK_DOWN:
                dispatcher.BeginInvoke(() => actionList.Move(1));
                return true;
            case VK_BACK:
                dispatcher.BeginInvoke(() => { if (actionList.CanReturnToRing) ReturnToRing(); else actionList.Backspace(); });
                return true;
            case 0x25:
                dispatcher.BeginInvoke(() => { if (actionList.CanReturnToRing) ReturnToRing(); });
                return true;
        }
        if (typed is { } c)
        {
            dispatcher.BeginInvoke(() => actionList.Type(c));
            return true;
        }
        // Shift、Tab 之类的键不处理，也不交给原来的 App
        return vk is not (0x10 or 0xA0 or 0xA1 or 0x14);
    }

    private async Task Finish(Session s, int? index)
    {
        if (s.Finished) return;
        s.Finished = true;
        ringOpen = false;
        ring.Dismiss();
        ring.SetClickable(false);
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

    private (ClassifiedContent Content, int X, int Y)? lastList;

    private async Task Execute(PopAction action, ClassifiedContent content, int x, int y)
    {
        if (action.Id == Actions.All.Id)
        {
            Log.Info("打开全部功能");
            ShowList(content, x, y, true);
            return;
        }
        await Run(action, content, x, y);
    }

    private void ShowList(ClassifiedContent content, int x, int y, bool fromRing = false)
    {
        lastList = (content, x, y);
        CloseCard();
        actionList.ShowFor(content, x, y, settings().IsEnabled, fromRing,
            fromRing && ring.Layout.IsArc ? session?.OriginalItems : null);
        listOpen = true;
    }

    private void ReturnToRing()
    {
        if (lastList is not { } last || session is not { } previous) return;
        CloseList();
        var s = new Session(previous.X, previous.Y, previous.Selection, previous.Application) { WaitingForClick = true };
        session = s;
        ShowRing(s, last.Content, previous.OriginalItems);
        ring.SetClickable(true);
    }

    /// 框选一块屏幕区域，截下来（物理像素）；取消时返回 null
    private async Task<(byte[] Png, System.Drawing.Rectangle Area)?> SelectRegionAsync()
    {
        if (selector.IsOpen) return null;
        CloseCard();
        CloseList();
        CloseHistory();
        // 等圆盘和卡片淡出，不要被截进去
        await Task.Delay(150);
        var selected = await selector.SelectAsync();
        if (selected is not { } s)
        {
            Log.Info("框选区域：取消");
            return null;
        }
        using (s.Screen)
        {
            var relative = new System.Drawing.Rectangle(s.Area.X - s.ScreenBounds.X, s.Area.Y - s.ScreenBounds.Y, s.Area.Width, s.Area.Height);
            relative.Intersect(new System.Drawing.Rectangle(0, 0, s.Screen.Width, s.Screen.Height));
            if (relative.Width < 1 || relative.Height < 1) return null;
            using var crop = s.Screen.Clone(relative, s.Screen.PixelFormat);
            Log.Info($"框选区域：{relative.Width}×{relative.Height}");
            return (ScreenCapture.Png(crop), s.Area);
        }
    }

    /// 截图识字：框选区域，识别文字，结果显示在卡片上
    public Task CaptureTextAsync() => ScreenToolAsync(async () =>
    {
        if (await SelectRegionAsync() is not { } region) return;
        await RecognizeAsync(region.Png, region.Area.Left + region.Area.Width / 2, region.Area.Top + region.Area.Height / 2);
    });

    private async Task RecognizeAsync(byte[] png, int x, int y)
    {
        try
        {
            var text = await TextRecognizer.RecognizeAsync(png);
            Log.Info($"识别文字：识别出 {text.Length} 个字符{(LogSelection ? $"，内容「{Abbreviate(text)}」" : "")}");
            ShowResult(x, y, text.Length == 0
                ? CardContent.Text("识别文字", "没有识别出文字")
                : new CardContent("识别文字", [], Body: text));
        }
        catch (TextRecognizer.UnavailableException e)
        {
            Log.Info($"识别文字：{e.Message}");
            ShowResult(x, y, CardContent.Text("识别文字", e.Message));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            Log.Error("识别文字失败", e);
            ShowToast(x, y, "识别失败，详情见日志");
        }
    }

    /// 截图贴图：框选区域，贴在所有窗口最前面（放在原来的位置）
    public Task CapturePinAsync() => ScreenToolAsync(async () =>
    {
        if (await SelectRegionAsync() is not { } region) return;
        var scale = Native.MonitorAt(region.Area.Left, region.Area.Top).Scale;
        new PinWindow(region.Png, region.Area.Left, region.Area.Top, scale).Show();
        Log.Info($"贴图：{region.Area.Width}×{region.Area.Height}，现在有 {PinWindow.Count} 张");
    });

    public Task CaptureAnnotateAsync() => ScreenToolAsync(async () =>
    {
        if (await SelectRegionAsync() is not { } region) return;
        new AnnotationWindow(region.Png, region.Area.Left, region.Area.Top).Show();
    });

    public Task PickColorAsync() => ScreenToolAsync(async () =>
    {
        CloseCard(); CloseList(); CloseHistory();
        await Task.Delay(150);
        if (await colorPicker.PickAsync() is not { } picked) return;
        ShowResult(picked.X, picked.Y, new CardContent("屏幕取色", picked.Color.Rows(), Swatch: picked.Color.HexString,
            Monospace: true, Caption: "来自屏幕原始像素 · 点击一行复制，回车复制 HEX"));
    });

    public Task MeasureScreenAsync() => ScreenToolAsync(async () =>
    {
        if (await SelectRegionAsync() is not { } region) return;
        var area = region.Area;
        ShowResult(area.Left, area.Top, new CardContent("屏幕标尺",
        [
            new("尺寸", $"{area.Width} × {area.Height} px"),
            new("宽度", $"{area.Width} px"), new("高度", $"{area.Height} px"),
            new("对角线", $"{Math.Sqrt((double)area.Width * area.Width + (double)area.Height * area.Height):0.##} px"),
        ], Caption: "物理像素，不受 Windows 显示缩放影响", Icon: "Ruler24"));
    });

    private async Task ScreenToolAsync(Func<Task> operation)
    {
        toolbar.Dismiss(); toolbarVersion++;
        if (screenToolBusy || selector.IsOpen || colorPicker.IsOpen) return;
        screenToolBusy = true;
        hook.Enabled = false;
        try { await operation(); }
        catch (Exception e)
        {
            Log.Error("屏幕工具失败", e);
            Native.GetCursorPos(out var cursor);
            ShowToast(cursor.X, cursor.Y, "屏幕工具无法启动，详情见日志");
        }
        finally { screenToolBusy = false; hook.Enabled = settings().Enabled; }
    }

    /// 打开剪贴板历史；不给位置时放在指针旁边
    public void ShowHistory(int? x = null, int? y = null)
    {
        toolbar.Dismiss(); toolbarVersion++;
        if (x is null || y is null)
        {
            Native.GetCursorPos(out var cursor);
            (x, y) = (cursor.X, cursor.Y);
        }
        CloseCard();
        CloseList();
        historyWindow.ShowAt(history.Store, x.Value, y.Value);
        historyOpen = true;
    }

    private void CloseHistory()
    {
        historyOpen = false;
        historyWindow.Dismiss();
    }

    /// 把这一条放回剪贴板，再粘贴到当前 App
    private async Task PasteFromHistory(ClipboardItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case ClipboardKind.Text:
                    clipboard.SetText(item.Text, temporary: false);
                    break;
                case ClipboardKind.Files:
                    var files = new System.Collections.Specialized.StringCollection();
                    files.AddRange(item.FilePaths.ToArray());
                    System.Windows.Clipboard.SetFileDropList(files);
                    break;
                case ClipboardKind.Image when history.Store.ImagePath(item) is { } path:
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    System.Windows.Clipboard.SetImage(image);
                    break;
                default:
                    return;
            }
            history.Store.MarkUsed(item.Id, DateTimeOffset.Now);
            await Task.Delay(60);
            InputInjector.CtrlChord(0x56); // V
            Log.Info($"剪贴板历史：粘贴一条{ClipboardHistory.Name(item.Kind)}");
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException or IOException or NotSupportedException)
        {
            Log.Error("从剪贴板历史粘贴失败", e);
        }
    }

    /// 剪贴板历史打开时的按键：打字搜索，↑↓ 选择，回车粘贴，Ctrl+1–9 直接粘贴，Delete 删除，Ctrl+P 固定
    private bool FilterHistoryKey(int vk)
    {
        const int VK_BACK = 0x08, VK_UP = 0x26, VK_DOWN = 0x28, VK_DELETE = 0x2E;
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        if (Down(0x11) && !Down(0x12))
        {
            if (vk is >= 0x31 and <= 0x39)
            {
                var index = vk - 0x31;
                dispatcher.BeginInvoke(() => historyWindow.ChooseNth(index));
                return true;
            }
            if (vk == 0x50) // P
            {
                dispatcher.BeginInvoke(historyWindow.TogglePin);
                return true;
            }
        }
        if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C))
        {
            if (vk is 0x11 or 0xA2 or 0xA3) return false; // 只按下了 Ctrl
            var held = HeldModifiers();
            dispatcher.BeginInvoke(() =>
            {
                Log.Info($"剪贴板历史：按着 {held} 按了别的键，关闭");
                CloseHistory();
            });
            return false;
        }
        switch (vk)
        {
            case VK_ESCAPE:
                dispatcher.BeginInvoke(CloseHistory);
                return true;
            case VK_RETURN:
                dispatcher.BeginInvoke(historyWindow.Choose);
                return true;
            case VK_UP:
                dispatcher.BeginInvoke(() => historyWindow.Move(-1));
                return true;
            case VK_DOWN:
                dispatcher.BeginInvoke(() => historyWindow.Move(1));
                return true;
            case VK_BACK:
                dispatcher.BeginInvoke(historyWindow.Backspace);
                return true;
            case VK_DELETE:
                dispatcher.BeginInvoke(historyWindow.DeleteSelected);
                return true;
        }
        if (TypedChar(vk) is { } c)
        {
            dispatcher.BeginInvoke(() => historyWindow.Type(c));
            return true;
        }
        return vk is not (0x10 or 0xA0 or 0xA1 or 0x14);
    }

    /// 现在按着的 Ctrl、Alt、Win（写日志用；在钩子线程上只取状态，日志交给界面线程写）
    private static string HeldModifiers()
    {
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        var names = new List<string>();
        if (Down(0x11)) names.Add("Ctrl");
        if (Down(0x12)) names.Add("Alt");
        if (Down(0x5B) || Down(0x5C)) names.Add("Win");
        return string.Join("+", names);
    }

    /// 搜索框能输入的字符：字母、数字、空格、减号、点
    private static char? TypedChar(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (char)('a' + vk - 0x41),
        >= 0x30 and <= 0x39 => (char)('0' + vk - 0x30),
        >= 0x60 and <= 0x69 => (char)('0' + vk - 0x60),
        0x20 => ' ',
        0xBD => '-',
        0xBE => '.',
        _ => null,
    };

    private void CloseList()
    {
        listOpen = false;
        actionList.Dismiss();
    }

    private async Task Run(PopAction action, ClassifiedContent content, int x, int y)
    {
        if (!action.IsAvailable(content))
        {
            ShowToast(x, y, content.IsEmpty ? "没有读到选中的文字" : "选中的内容用不了这个功能");
            return;
        }
        try
        {
            await Handle(action.Run(content), action, content, x, y);
        }
        catch (Exception e)
        {
            Log.Error($"执行 {action.Id} 失败", e);
            ShowToast(x, y, "出错了，详情见日志");
        }
    }

    /// 按功能返回的结果做事：显示卡片、替换原文、复制、打开……
    private async Task Handle(ActionResult? result, PopAction action, ClassifiedContent content, int x, int y)
    {
        switch (result?.Effect)
        {
            case null:
                ShowToast(x, y, "选中的内容用不了这个功能");
                break;
            case ActionEffect.Card when result.Card is { } resultCard:
                // 卡片没有自己的图标时用这个功能的图标
                ShowResult(x, y, resultCard.Icon is null && resultCard.Swatch is null ? resultCard with { Icon = action.Glyph } : resultCard);
                break;
            case ActionEffect.Replace when result.Text is { } replacement:
                await Replace(replacement);
                break;
            case ActionEffect.Copy when result.Text is { } copied:
                clipboard.SetText(copied, temporary: false);
                ShowToast(x, y, "已复制");
                break;
            case ActionEffect.Translate when result.Text is { } source:
                await TranslateAsync(source, x, y);
                break;
            case ActionEffect.Open when result.Text is { } target:
                Open(target);
                break;
            case ActionEffect.Toast when result.Text is { } message:
                ShowToast(x, y, message);
                break;
            case ActionEffect.ShowAll:
                ShowList(content, x, y);
                break;
            case ActionEffect.ShowHistory:
                ShowHistory(x, y);
                break;
            case ActionEffect.CaptureText:
                await CaptureTextAsync();
                break;
            case ActionEffect.CapturePin:
                await CapturePinAsync();
                break;
            case ActionEffect.CaptureAnnotate:
                await CaptureAnnotateAsync();
                break;
            case ActionEffect.PickColor:
                await PickColorAsync();
                break;
            case ActionEffect.MeasureScreen:
                await MeasureScreenAsync();
                break;
            case ActionEffect.ShowSettings:
                ((App)System.Windows.Application.Current).ShowSettings();
                break;
            case ActionEffect.RunPlugin when result.PluginId is { } pluginId:
                await RunPluginAsync(pluginId, action, content, x, y);
                break;
        }
    }

    /// 运行用户的插件。脚本可能要跑一会儿：300 毫秒还没结束就先显示「正在运行」，关掉卡片就停止脚本
    private async Task RunPluginAsync(string id, PopAction action, ClassifiedContent content, int x, int y)
    {
        if (plugins().Find(id) is not { } manifest)
        {
            ShowToast(x, y, "找不到这个插件，可能已经删掉了");
            return;
        }
        pluginRun?.Cancel();
        using var run = new CancellationTokenSource();
        pluginRun = run;
        var watch = Stopwatch.StartNew();
        var task = pluginRunner.RunAsync(manifest, content, run.Token);
        if (await Task.WhenAny(task, Task.Delay(300)) != task)
            ShowResult(x, y, new CardContent(manifest.DisplayName, [], Body: "正在运行…", Icon: action.Glyph, Loading: true));
        ActionResult? result;
        try
        {
            result = await task;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"插件 {id}：取消了");
            return;
        }
        finally
        {
            if (ReferenceEquals(pluginRun, run)) pluginRun = null;
        }
        Log.Info($"插件 {id}（{PluginNames.Name(manifest.Action.Type)}）：{result?.Effect.ToString() ?? "内容用不了"}，用时 {watch.ElapsedMilliseconds} ms");
        if (run.IsCancellationRequested) return;
        // 「正在运行」的卡片：结果不是卡片或者轻提示时先关掉它（替换原文要回到原来的 App）
        if (cardOpen && result?.Effect is ActionEffect.Replace or ActionEffect.Open or ActionEffect.None) CloseCard();
        await Handle(result, action, content, x, y);
    }

    private readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private Translator? translator;
    private int translation;

    /// 在卡片里翻译：先显示「正在翻译」，结果回来后原地换成译文
    private async Task TranslateAsync(string text, int x, int y)
    {
        var s = settings();
        var to = Languages.TargetFor(text, s.TranslateTarget);
        if (s.TranslationEngine == TranslationEngine.Browser)
        {
            Open(Translator.BrowserUrl(text, to));
            return;
        }
        var id = ++translation;
        ShowResult(x, y, TranslationCards.Loading(text, to));
        translator ??= new Translator(http);
        var watch = Stopwatch.StartNew();
        CardContent result;
        try
        {
            var translated = await translator.TranslateAsync(text, to, s.TranslationEngine,
                SecretStore.Get(SecretStore.AzureTranslatorKey), s.AzureTranslatorRegion);
            Log.Info($"翻译完成：{translated.Provider}（{translated.Via}），{translated.From} → {translated.To}，{translated.Text.Length} 个字符，用时 {watch.ElapsedMilliseconds} ms{(LogSelection ? $"，译文「{Abbreviate(translated.Text)}」" : "")}");
            result = TranslationCards.Result(text, translated);
        }
        catch (TranslationException e)
        {
            Log.Info($"翻译失败：{e.Message}{(e.InnerException is { } inner ? $"（{inner.GetType().Name}: {inner.Message}）" : "")}");
            result = TranslationCards.Failed(text, to, e.Message);
        }
        catch (TaskCanceledException)
        {
            Log.Info("翻译失败：超时");
            result = TranslationCards.Failed(text, to, "翻译服务没有响应，请稍后再试");
        }
        // 用户已经关掉了卡片或者又开始了别的操作
        if (id != translation || !card.IsOpen) return;
        ShowResult(x, y, result);
    }

    private void ShowResult(int x, int y, CardContent content)
    {
        var watch = Stopwatch.StartNew();
        card.ShowResult(x, y, content);
        cardOpen = true;
        LogIfSlow("显示卡片", watch);
    }

    /// 界面线程被占住太久时记一笔：这期间长按、松开都在排队，圆盘和卡片也不会动
    private static void LogIfSlow(string what, Stopwatch watch)
    {
        if (watch.ElapsedMilliseconds >= 200) Log.Info($"{what}用了 {watch.ElapsedMilliseconds} ms");
    }

    private async Task Replace(string text)
    {
        Log.Info("替换原文");
        var result = await paster.PasteAsync(text);
        var others = result.OtherReaders.Count > 0 ? $"（{string.Join("、", result.OtherReaders.Distinct())} 也读了剪贴板）" : "";
        Log.Info(result.Outcome switch
        {
            Paster.Outcome.Pasted => $"粘贴完成：目标 App 过了 {result.Milliseconds} ms 来读，原来的剪贴板已恢复{others}",
            Paster.Outcome.NotRead => $"目标 App {Paster.Deadline.TotalSeconds:0} 秒内没有来读剪贴板，原来的内容已恢复{others}",
            Paster.Outcome.Replaced => "粘贴期间剪贴板里放进了别的内容，不再恢复原来的",
            _ => "替换原文失败：打不开剪贴板",
        });
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
