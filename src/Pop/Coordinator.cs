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
    private readonly ActionListWindow actionList = new();
    private readonly ClipboardHistoryWindow historyWindow = new();
    private readonly ClipboardHistory history;
    private readonly RegionSelector selector = new();
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

    private sealed class Session(int x, int y, Task<Selection> selection)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public Task<Selection> Selection { get; } = selection;
        public IReadOnlyList<PopAction> Items { get; set; } = [];
        public bool RingShown { get; set; }
        /// 圆盘还没弹出来右键就松开了
        public bool ReleasedEarly { get; set; }
        public bool Finished { get; set; }
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
        hook.Moved += (x, y) => dispatcher.BeginInvoke(() => OnMoved(x, y));
        hook.Released += (x, y) => dispatcher.BeginInvoke(() => OnReleased(x, y));
        hook.OtherButtonDown += (x, y) =>
        {
            if (cardOpen) dispatcher.BeginInvoke(() => { if (!card.ContainsPhysical(x, y)) CloseCard(); });
            if (listOpen) dispatcher.BeginInvoke(() => { if (!actionList.ContainsPhysical(x, y)) CloseList(); });
            if (historyOpen) dispatcher.BeginInvoke(() => { if (!historyWindow.ContainsPhysical(x, y)) CloseHistory(); });
        };
        hook.KeyFilter = FilterKey;
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
        ring.Close();
        card.Close();
        actionList.Close();
        historyWindow.Close();
        paster.Dispose();
        clipboard.Dispose();
    }

    private async void OnTriggered(int x, int y)
    {
        CloseCard();
        CloseList();
        CloseHistory();
        Log.Info($"长按 ({x}, {y})");
        var selection = ReadSelectionAsync();
        var s = new Session(x, y, selection);
        session = s;

        await Task.WhenAny(selection, Task.Delay(DirectWait));
        if (session != s || s.Finished) return;
        ClassifiedContent? content = null;
        if (selection.IsCompleted)
        {
            content = ContentClassifier.Classify(selection.Result.Text);
            if (DirectResults.TranslatesDirectly(content, settings().DirectKindFlags))
            {
                s.Finished = true;
                Log.Info("直达结果：翻译");
                _ = TranslateAsync(content.Text, x, y);
                return;
            }
            if (DirectCard(content) is { } direct)
            {
                s.Finished = true;
                Log.Info($"直达结果：{direct.Title}");
                ShowResult(x, y, direct);
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

    private CardContent? DirectCard(ClassifiedContent content) =>
        DirectResults.For(content, settings().DirectKindFlags);

    private IReadOnlyList<PopAction> Ring => RingItems.Build(settings().RingSlots, settings().IsEnabled);

    private void ShowRing(Session s, ClassifiedContent? content)
    {
        var watch = Stopwatch.StartNew();
        s.Items = content is null ? Ring : RingItems.For(Ring, content);
        s.RingShown = true;
        ringCount = s.Items.Count;
        ringOpen = true;
        ring.ShowAt(s.X, s.Y, s.Items);
        if (content is not null) ring.SetContent(content, s.Items);
        LogIfSlow("弹出圆盘", watch);
    }

    private void UpdateRing(Session s, ClassifiedContent content)
    {
        s.Items = RingItems.For(Ring, content);
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

    /// 「全部功能」列表打开时，按键都交给列表；按着 Ctrl、Alt、Win 的组合键照常交给系统，同时关掉列表
    private bool FilterListKey(int vk)
    {
        const int VK_BACK = 0x08, VK_UP = 0x26, VK_DOWN = 0x28;
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C))
        {
            dispatcher.BeginInvoke(CloseList);
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
                dispatcher.BeginInvoke(actionList.Backspace);
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

    private (ClassifiedContent Content, int X, int Y)? lastList;

    private async Task Execute(PopAction action, ClassifiedContent content, int x, int y)
    {
        if (action.Id == Actions.All.Id)
        {
            Log.Info("打开全部功能");
            ShowList(content, x, y);
            return;
        }
        await Run(action, content, x, y);
    }

    private void ShowList(ClassifiedContent content, int x, int y)
    {
        lastList = (content, x, y);
        CloseCard();
        actionList.ShowFor(content, x, y, settings().IsEnabled);
        listOpen = true;
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
    public async Task CaptureTextAsync()
    {
        if (await SelectRegionAsync() is not { } region) return;
        await RecognizeAsync(region.Png, region.Area.Left + region.Area.Width / 2, region.Area.Top + region.Area.Height / 2);
    }

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
    public async Task CapturePinAsync()
    {
        if (await SelectRegionAsync() is not { } region) return;
        var scale = Native.MonitorAt(region.Area.Left, region.Area.Top).Scale;
        new PinWindow(region.Png, region.Area.Left, region.Area.Top, scale).Show();
        Log.Info($"贴图：{region.Area.Width}×{region.Area.Height}，现在有 {PinWindow.Count} 张");
    }

    /// 打开剪贴板历史；不给位置时放在指针旁边
    public void ShowHistory(int? x = null, int? y = null)
    {
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
            dispatcher.BeginInvoke(CloseHistory);
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
