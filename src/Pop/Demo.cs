using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Pop.Core;
using Drawing = System.Drawing;

namespace Pop;

/// 演示模式（Pop.exe --demo-shots 文件夹）：在一张示例文档前面把圆盘、结果卡片、列表、托盘面板、
/// 设置窗口等依次显示出来，每个截一张图存到文件夹里，然后退出。CI 用它检查界面，不装钩子、不改设置。
/// 文件名是 light-xxx.png 或 dark-xxx.png；--demo-scenes ring,cards 只拍其中几组
internal static class Demo
{
    public static async Task RunAsync(App app, string outDir, string? only)
    {
        Directory.CreateDirectory(outDir);
        var wanted = only?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Want(string group) => wanted is null || wanted.Contains(group);
        var prefix = Theme.SystemIsDark() ? "dark" : "light";
        var (bounds, work, _) = Native.MonitorDetailsAt(1, 1);
        var screen = new Drawing.Rectangle((int)bounds.Left, (int)bounds.Top, (int)(bounds.Right - bounds.Left), (int)(bounds.Bottom - bounds.Top));
        Log.Info($"演示模式：{prefix}，屏幕 {screen.Width}×{screen.Height}，截图存到 {outDir}");

        var backdrop = new Backdrop();
        backdrop.ShowOn(work);
        await Wait(900);
        var (x, y) = backdrop.SelectionPoint();

        void Shot(string name, Drawing.Rectangle? area = null)
        {
            var rect = area ?? screen;
            rect.Intersect(screen);
            using var bitmap = ScreenCapture.Capture(rect);
            File.WriteAllBytes(Path.Combine(outDir, $"{prefix}-{name}.png"), ScreenCapture.Png(bitmap));
            SmokeTest.Report($"demo-shot={prefix}-{name}");
        }

        async Task Scene(string name, Func<Task> run)
        {
            try
            {
                await run();
            }
            catch (Exception e)
            {
                Log.Error($"演示「{name}」出错", e);
                SmokeTest.Report($"demo-error={name}: {e.GetType().Name} {e.Message}");
            }
        }

        if (Want("performance"))
        {
            await Scene("performance", async () =>
            {
                foreach (var line in await Task.Run(PerformanceChecks.Run)) SmokeTest.Report(line);
                PerformanceChecks.VerifyNavigation(x, y); SmokeTest.Report("list-navigation=ok rows=100 updated=6 moves=3");
                SmokeTest.Report("list-selection=ok filtered=ok shortcuts=ok empty=ok");
                SmokeTest.Report("overlay-release=ok reopen=ok");
                await ClipboardPerformanceChecks.VerifyAsync();
                SmokeTest.Report("clipboard-thumbnails=ok bounded=256 pixels=96 workers=2 cancellation=ok");
            });
            // 独立报告，某一项失败时仍检查其余原生接口与真实界面。
            await Scene("native-tray", async () =>
            {
                await NativeExperienceChecks.VerifyAsync(); SmokeTest.Report("native-tray=ok keyboard=ok restart=ok dispose=ok monitors=ok");
            });
            await Scene("list-sizing", async () =>
            {
                await PerformanceChecks.VerifySizingAsync(x, y); SmokeTest.Report("list-sizing=ok compact=ok workarea=ok scroll=ok");
            });
            await Scene("plugin-editor", async () =>
            {
                await PluginExperienceChecks.VerifyAsync(app); SmokeTest.Report("plugin-editor=ok restart=ok stop=ok copy-full=ok close=ok length=ok pending-numbers=ok");
            });
        }

        var text = ContentClassifier.Classify("Pop makes selected text useful");
        if (Want("icon-compatibility"))
            await Scene("icon-compatibility", () =>
            {
                IconExperienceChecks.Verify();
                SmokeTest.Report("icons-compatible=ok pages=ok legacy=ok fallback=ok");
                return Task.CompletedTask;
            });
        if (Want("clipboard-ownership"))
            await Scene("clipboard-ownership", () =>
            {
                ClipboardOwnershipChecks.Verify();
                SmokeTest.Report("clipboard-ownership=ok restore=ok replacement=preserved fallback=preserved");
                return Task.CompletedTask;
            });

        if (Want("clipboard-recording"))
            await Scene("clipboard-recording", async () =>
            {
                await ClipboardRecordingChecks.VerifyAsync();
                SmokeTest.Report("clipboard-recording=ok disabled=unread queued=cancelled queued-result=cancelled rapid-toggle=fresh reenabled=complete");
            });

        if (Want("selection-copy"))
            await Scene("selection-copy", async () =>
            {
                await SelectionCopyChecks.VerifyAsync(app);
                SmokeTest.Report("selection-copy=ok real-copy=ok normal=restored late=restored user-copy=preserved menu=preserved history=recorded owner=checked foreign-process=preserved lock=checked snapshot=required newer-read=cancelled dispose=cancelled");
            });

        if (Want("snippets"))
            await Scene("snippets", async () =>
            {
                var snippets = new SnippetWindow();
                snippets.ShowAt([.. Snippet.Examples, new Snippet("example-long", "多行模板", string.Concat(Enumerable.Repeat("第一段内容\r\n下一段内容与说明\n", 1000)))], x, y);
                await Wait(400); Shot("snippets"); snippets.Close(); await Wait(150);
            });
        if (Want("utilities"))
            await Scene("utilities", async () =>
            {
                var diff = new TextDiffWindow("标题\n功能已完成\n下一步：检查", "标题\n功能已完善\n新增：回归验证\n下一步：检查");
                diff.Show(); await Wait(600); Shot("text-diff"); diff.Close(); await Wait(150);
                using var awake = new KeepAwakeController(); awake.ShowWindow();
                await Wait(400); Shot("keep-awake");
            });

        if (Want("toolbar"))
            await Scene("toolbar", async () =>
            {
                var toolbar = new SelectionToolbar();
                toolbar.ShowAt(x, y, SelectionToolbarLogic.Items(RingItems.Default, text), new Pop.Core.Rect(x - 140, y - 16, x + 140, y + 4));
                await Wait(500); Shot("selection-toolbar"); toolbar.Dismiss(); toolbar.Close(); await Wait(200);
            });

        if (Want("ring"))
            await Scene("ring", async () =>
            {
                var ring = new RingWindow();
                var items = RingItems.For(RingItems.Default, text);
                ring.ShowAt(x, y, items);
                ring.SetContent(text, items);
                await Wait(500);
                Shot("ring");
                ring.Highlight(2);
                await Wait(400);
                Shot("ring-highlight");
                var link = ContentClassifier.Classify("https://github.com/whrss9527/pop-windows");
                var linkItems = RingItems.For(RingItems.Default, link);
                ring.SetContent(link, linkItems);
                ring.Highlight(1);
                await Wait(400);
                Shot("ring-link");
                ring.Dismiss();
                await Wait(300);
                ring.Close();
            });

        if (Want("cards"))
            await Scene("cards", async () =>
            {
                var original = "Select any text, then press and hold the right mouse button. Pop shows what you can do with it.";
                var cards = new List<(string Name, CardContent Content)>
                {
                    ("card-translate", TranslationCards.Result(original, new TranslationResult("选中任意文字，然后按住鼠标右键。Pop 会列出可以对它做的事。", "en", "zh-Hans", "必应翻译"))),
                    ("card-translating", TranslationCards.Loading(original, "zh-Hans")),
                    ("card-math", DirectResults.For(ContentClassifier.Classify("128*3+7"))!),
                    ("card-unit", DirectResults.For(ContentClassifier.Classify("5 km"))!),
                    ("card-color", DirectResults.For(ContentClassifier.Classify("#FF8800"))!),
                    ("card-time", DirectResults.For(ContentClassifier.Classify("1735660800"))!),
                    ("card-count", new CardContent("字数统计", TextActions.Lines(TextActions.Count("Pop 让选中的文字更有用。Select, hold, done."))) { Icon = Actions.Count.Glyph }),
                };
                if (Actions.Find("codec") is { } codecAction && codecAction.Run(ContentClassifier.Classify("hello pop world")) is { Card: { } codec })
                    cards.Add(("card-codec", codec with { Icon = codec.Icon ?? codecAction.Glyph }));
                // 内容很长的卡片：正文区域滚动
                var json = "{\"name\":\"Pop\",\"version\":\"0.7.0\",\"features\":[\"ring\",\"translate\",\"clipboard\",\"ocr\",\"pin\"],\"settings\":{\"holdMilliseconds\":250,\"directResults\":true,\"ringSlots\":[\"copy\",\"search\",\"translate\",\"upper\",\"count\",\"all\"],\"clipboard\":{\"retentionDays\":30,\"maxItems\":500,\"excludedApps\":[\"KeePass\",\"1Password\",\"Bitwarden\"]}},\"platforms\":[{\"os\":\"Windows 10\",\"minimum\":\"1809\"},{\"os\":\"Windows 11\",\"minimum\":\"21H2\"}]}";
                if (Actions.Find("json") is { } jsonAction && jsonAction.Run(ContentClassifier.Classify(json)) is { Card: { } formatted })
                    cards.Add(("card-long", formatted with { Icon = formatted.Icon ?? jsonAction.Glyph }));
                if (JwtDecoder.Card("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJQb3AiLCJpc3MiOiJodHRwczovL2V4YW1wbGUuY29tIiwiZXhwIjoxODAwMDAwMDAwfQ.signature") is { } jwt)
                    cards.Add(("card-jwt", jwt));
                foreach (var (name, content) in cards)
                {
                    var card = new ResultCard();
                    card.ShowResult(x, y, content);
                    await Wait(500);
                    Shot(name);
                    card.Dismiss();
                    await Wait(250);
                    card.Close();
                }
                var toast = new ResultCard();
                toast.ShowToast(x, y, "已复制");
                await Wait(350);
                Shot("toast");
                await Wait(1600);
                toast.Close();
            });

        if (Want("list"))
            await Scene("list", async () =>
            {
                var list = new ActionListWindow();
                list.ShowFor(text, x, y, _ => true);
                await Wait(500);
                Shot("list");
                foreach (var c in "ba") list.Type(c);
                await Wait(300);
                Shot("list-search");
                list.Dismiss();
                await Wait(250);
                list.Close();
            });

        if (Want("history"))
            await Scene("history", async () =>
            {
                var folder = Path.Combine(Path.GetTempPath(), $"pop-demo-{Guid.NewGuid():N}");
                try
                {
                    using var store = new ClipboardStore(folder);
                    var now = DateTimeOffset.Now;
                    using (var picture = ScreenCapture.Capture(new Drawing.Rectangle(screen.Left + screen.Width / 4, screen.Top + screen.Height / 5, 320, 200)))
                        store.Add(ClipboardCapture.Image(ScreenCapture.Png(picture), "mspaint"), now.AddMinutes(-50));
                    store.Add(ClipboardCapture.Files(new[] { @"C:\Users\Pop\Documents\季度报告.docx", @"C:\Users\Pop\Pictures\封面.png" }
                        .Concat(Enumerable.Range(3, 9998).Select(i => $@"C:\文档\附件-{i}.txt")), "explorer"), now.AddMinutes(-35));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "https://github.com/whrss9527/pop-windows", SourceApp: "chrome"), now.AddMinutes(-20));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "会议改到周四下午三点，地点不变。", SourceApp: "WeChat"), now.AddMinutes(-9));
                    var pinned = store.Add(new ClipboardCapture(ClipboardKind.Text, "hello@example.com", SourceApp: "Outlook"), now.AddMinutes(-3));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "Select any text, then press and hold the right mouse button.", SourceApp: "notepad"), now.AddMinutes(-1));
                    if (pinned is { } id) store.SetPinned(true, id);
                    var image = store.Items(kind: ClipboardKind.Image).Single();
                    store.SetRecognizedText("项目示例图片", image.Id);
                    var history = new ClipboardHistoryWindow();
                    history.ShowAt(store, x, y);
                    await Wait(500);
                    Shot("history");
                    history.SetFilter(ClipboardKind.Image, false); history.Type('图');
                    await Wait(300); Shot("history-images");
                    if (history.SelectedHistoryItem?.Id != image.Id) throw new InvalidOperationException("图片分类或识别文字搜索不正确");
                    history.TogglePinnedFilter(); await Wait(200); Shot("history-filter-empty");
                    if (history.SelectedHistoryItem is not null) throw new InvalidOperationException("空分类仍能选择其他类型的记录");
                    store.SetPinned(true, image.Id); history.SetFilter(ClipboardKind.Image, true);
                    if (history.SelectedHistoryItem?.Id != image.Id) throw new InvalidOperationException("分类与固定筛选不能组合");
                    await Wait(200); Shot("history-pinned-image");
                    history.TogglePin();
                    if (history.SelectedHistoryItem is not null) throw new InvalidOperationException("取消固定后没有移出筛选结果");
                    history.Backspace(); history.SetFilter(null, true);
                    if (history.SelectedHistoryItem?.Id != pinned) throw new InvalidOperationException("只看固定混入未固定记录");
                    await Wait(200); Shot("history-pinned");
                    ClipboardItem? chosen = null; history.Chosen += item => chosen = item;
                    history.ChooseNth(0);
                    if (chosen?.Id != pinned) throw new InvalidOperationException("快捷粘贴未使用筛选后的序号");
                    await Wait(250); history.ShowAt(store, x, y);
                    if (!history.FiltersAreClear) throw new InvalidOperationException("重新打开没有恢复全部分类");
                    history.CycleFilter(-1);
                    if (history.SelectedHistoryItem?.Kind != ClipboardKind.Files) throw new InvalidOperationException("分类反向切换不正确");
                    history.CycleFilter(1);
                    if (!history.FiltersAreClear) throw new InvalidOperationException("分类循环没有回到全部");
                    SmokeTest.Report("history-filters=ok search=ok pinned=ok empty=ok shortcuts=ok reopen=ok");
                    history.Dismiss();
                    await Wait(250);
                    history.Close();
                }
                finally
                {
                    try
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                    catch (IOException)
                    {
                    }
                }
            });

        if (Want("flyout"))
            await Scene("flyout", async () =>
            {
                var flyout = new TrayFlyout(app);
                flyout.ShowNear((int)work.Right - 160, (int)Math.Max(work.Bottom, bounds.Bottom - 20));
                await Wait(500);
                Shot("flyout");
                flyout.Close();
                await Wait(250);
            });

        if (Want("settings"))
            await Scene("settings", async () =>
            {
                SettingsWindow? window = null;
                foreach (var (id, _, _) in SettingsWindow.Pages)
                {
                    if (window is null) window = app.ShowSettings(id);
                    else window.Navigate(id);
                    await Wait(900);
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var r);
                    Shot($"settings-{id}", new Drawing.Rectangle(r.Left - 16, r.Top - 16, r.Right - r.Left + 32, r.Bottom - r.Top + 32));
                }
                // 独立应用圆盘和修饰键设置，验证有配置时的布局，演示结束恢复默认。
                app.UpdateSettings(s =>
                {
                    s.AppRings["notepad"] = ["copy", "ocr", "pin", "all"];
                    s.TriggerMode = TriggerMode.ModifierRightClick;
                    s.TriggerModifier = TriggerModifier.Control;
                    s.ActionShortcuts["upper"] = new(6, 0x55);
                    s.SelectionToolbar = true;
                    s.TriggerExcludedApps = ["Code", "mstsc"];
                });
                if (window is not null)
                {
                    window.Navigate("triggers"); await Wait(600); Shot("settings-triggers-modifier");
                    window.EditApplicationRing("notepad"); await Wait(600); Shot("settings-ring-application");
                    window.Navigate("shortcuts"); await Wait(600); Shot("settings-shortcuts-configured");
                }
                app.UpdateSettings(s => { s.AppRings.Clear(); s.ActionShortcuts.Clear(); s.TriggerMode = TriggerMode.LongPressRight; s.TriggerModifier = TriggerModifier.Alt; s.SelectionToolbar = false; s.TriggerExcludedApps.Clear(); });
                // 插件编辑器：一个脚本插件、一个网址插件，脚本插件先试运行一下
                if (window is not null)
                    foreach (var manifest in app.Plugins.Manifests.OrderBy(m => m.Action.Type == PluginActionType.Url ? 1 : 0).Take(2))
                    {
                        var editor = new PluginEditorWindow(app, manifest, isNew: false) { Owner = window };
                        editor.Show();
                        await Wait(700);
                        if (manifest.Action.Type != PluginActionType.Url)
                        {
                            editor.RunTest();
                            await Wait(900);
                        }
                        Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(editor).Handle, out var e);
                        Shot($"plugin-editor-{PluginNames.Name(manifest.Action.Type)}", new Drawing.Rectangle(e.Left - 16, e.Top - 16, e.Right - e.Left + 32, e.Bottom - e.Top + 32));
                        editor.Close();
                        await Wait(250);
                    }
                if (window is not null)
                {
                    var constrained = new PluginManifest { Name = "长度条件", Match = new() { MinLength = 2, MaxLength = 8 },
                        Action = new() { Type = PluginActionType.JavaScript, Script = "input" } };
                    var editor = new PluginEditorWindow(app, constrained, isNew: false) { Owner = window, TestInput = "👩‍💻" };
                    editor.Show(); await Wait(500); await editor.RunTestAsync(); await Wait(400);
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(editor).Handle, out var e);
                    Shot("plugin-editor-match", new Drawing.Rectangle(e.Left - 16, e.Top - 16, e.Right - e.Left + 32, e.Bottom - e.Top + 32));
                    editor.Close(); await Wait(150);
                    var snippetEditor = window.PreviewSnippetEditor(); await Wait(500); Shot("snippet-editor"); snippetEditor.Close();
                }
                window?.Close();
                await Wait(250);
            });

        if (Want("pin"))
            await Scene("pin", async () =>
            {
                var area = new Drawing.Rectangle(x - 200, y - 150, 360, 180);
                area.Intersect(screen);
                using var picture = ScreenCapture.Capture(area);
                var pin = new PinWindow(ScreenCapture.Png(picture), area.Left + 120, area.Top + 220, Native.MonitorAt(x, y).Scale);
                pin.Show();
                await Wait(500);
                Shot("pin");
                pin.Close();
                await Wait(200);
            });

        if (Want("region"))
            await Scene("region", async () =>
            {
                var selector = new RegionSelector();
                var selecting = selector.SelectAsync();
                await Wait(500);
                Shot("region");
                selector.Preview(new Drawing.Rectangle(x - 260, y - 90, 420, 180));
                await Wait(200);
                Shot("region-drag");
                selector.Cancel();
                await selecting;
            });

        if (Want("annotate"))
            await Scene("annotate", async () =>
            {
                using var picture = ScreenCapture.Capture(new Drawing.Rectangle(x - 200, y - 80, 480, 260));
                var editor = new AnnotationWindow(ScreenCapture.Png(picture), x, y);
                editor.Preview(); editor.Show();
                await Wait(500); Shot("annotate");
                editor.ApplyFrame(new(AnnotationBackground.Sky, 32, 12));
                await Wait(300); Shot("annotate-background"); editor.Close();
                // 验证真正的渲染输出：尺寸不变、线条落在预期像素、未标注处保留原图。
                var pixels = Enumerable.Repeat((byte)255, 100 * 60 * 4).ToArray();
                var image = System.Windows.Media.Imaging.BitmapSource.Create(100, 60, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
                var strokes = new[]
                {
                    new AnnotationStroke(AnnotationTool.Rectangle, [new(20, 10), new(80, 50)], "#FF0000", 4),
                    new AnnotationStroke(AnnotationTool.Mosaic, [new(0, 0), new(3, 3)], "#FF0000", 8),
                };
                var output = AnnotationRenderer.Render(image, strokes);
                var restored = ScreenCapture.ToBitmapSource(AnnotationRenderer.Png(output));
                if (restored.PixelWidth != 100 || restored.PixelHeight != 60) throw new InvalidOperationException("标注导出尺寸不一致");
                var actual = new byte[pixels.Length]; output.CopyPixels(actual, 400, 0);
                if (actual[(30 * 100 + 20) * 4 + 2] < 240 || actual[(30 * 100 + 20) * 4 + 1] > 100)
                    throw new InvalidOperationException("标注线条未画在原图坐标");
                if (actual[(30 * 100 + 50) * 4 + 1] < 240) throw new InvalidOperationException("标注改变了未绘制区域");
                SmokeTest.Report("annotation-render=ok");
                var framed = AnnotationRenderer.Render(image, strokes, new(AnnotationBackground.Sky, 16, 8));
                var framedPng = ScreenCapture.ToBitmapSource(AnnotationRenderer.Png(framed));
                if (framedPng.PixelWidth != 132 || framedPng.PixelHeight != 92) throw new InvalidOperationException("美化导出尺寸不一致");
                var framedPixels = new byte[132 * 92 * 4]; framed.CopyPixels(framedPixels, 132 * 4, 0);
                if (framedPixels[(46 * 132 + 36) * 4 + 2] < 240 || framedPixels[(46 * 132 + 36) * 4 + 1] > 100)
                    throw new InvalidOperationException("美化后标注坐标偏移");
                if (framedPixels[(46 * 132 + 66) * 4 + 1] < 240 || framedPixels[3] != 255 || framedPixels[1] > 230)
                    throw new InvalidOperationException("美化背景覆盖原图或未绘制");
                SmokeTest.Report("annotation-frame=ok");
            });

        if (Want("picker"))
            await Scene("picker", async () =>
            {
                var picker = new ColorPicker();
                var picking = picker.PickAsync();
                picker.Preview(x, y); await Wait(300); Shot("picker");
                picker.Cancel(); await picking;
            });

        if (Want("ruler"))
            await Scene("ruler", async () =>
            {
                var ruler = new RegionSelector(); var measuring = ruler.MeasureAsync();
                ruler.PreviewAuto(x + 180, y - 80); await Wait(300); Shot("ruler-auto"); ruler.Cancel(); await measuring;
            });

        if (Want("ring"))
            await Scene("ring-layouts", async () =>
            {
                var ring = new RingWindow();
                var twelve = RingItems.Build(Actions.BuiltIn.Take(11).Select(a => a.Id).Append("all"));
                ring.ShowAt(x, y, twelve);
                ring.SetContent(ContentClassifier.Classify("测试内容"), twelve);
                await Wait(400); Shot("ring-twelve"); ring.Dismiss(); await Wait(160);
                ring.ShowAt((int)work.Left + 4, (int)work.Top + 4, twelve);
                await Wait(400); Shot("ring-corner");
                if (!ring.Layout.IsArc || ring.Items.Count != 3 || ring.Items[^1].Id != Actions.All.Id)
                    throw new InvalidOperationException("角落菜单没有保留更多入口");
                ring.Close();
            });

        if (Want("feedback"))
            await Scene("feedback", async () =>
            {
                var card = new ResultCard();
                const string original = "保存失败或剪贴板忙时，保留结果供重试。";
                Func<string, bool> busy = _ => false;
                card.CopyRequested += busy;
                card.ShowCard(x, y, "复制反馈", original);
                await Wait(350);
                if (card.CopyPrimary() || !card.CopyFailed || !card.IsOpen || card.PrimaryText != original)
                    throw new InvalidOperationException("复制失败后没有保留卡片和原结果");
                await Wait(180); Shot("card-copy-failed");
                card.CopyRequested -= busy;
                string? copied = null;
                card.CopyRequested += value => { copied = value; return true; };
                if (!card.CopyPrimary() || card.CopyFailed || copied != original)
                    throw new InvalidOperationException("复制重试没有返回原结果");
                await Wait(180);
                card.ShowToast(x, y, "示例轻提示");
                if (card.PrimaryText is not null || card.CopyPrimary())
                    throw new InvalidOperationException("轻提示不应保留上一张卡片的可复制内容");
                card.ShowCopyFailure(x, y, "复制未完成", original);
                if (!card.CopyFailed || card.PrimaryText != original) throw new InvalidOperationException("复制失败恢复卡片没有保留原内容");
                await Wait(250); Shot("copy-action-recovery");
                if (!card.CopyPrimary() || copied != original) throw new InvalidOperationException("普通复制恢复入口无法重试");
                var large = new string('x', 2_000_000);
                card.ShowCopyFailure(x, y, "长结果复制恢复", large);
                if (card.PrimaryText != large || LongestRenderedText(card) > 10_000)
                    throw new InvalidOperationException("长结果没有保留完整复制值，或界面载入了整段正文");
                await Wait(250);
                if (!card.IsOpen || LongestRenderedText(card) == 0)
                    throw new InvalidOperationException($"快速重开长结果后卡片不可见：visible={card.IsVisible} open={card.IsOpen}");
                Shot("copy-action-large-recovery");
                if (!card.CopyPrimary() || copied != large) throw new InvalidOperationException("长结果重试只复制了预览");
                var hiddenDeadline = DateTime.UtcNow.AddSeconds(5);
                while (card.IsVisible)
                {
                    if (DateTime.UtcNow > hiddenDeadline) throw new InvalidOperationException("复制成功后卡片没有关闭");
                    await Task.Delay(50);
                }
                if (card.PrimaryText is not null || LongestRenderedText(card) > 0)
                    throw new InvalidOperationException($"关闭卡片后仍保留上一张结果的显示缓存：primary={card.PrimaryText?.Length ?? 0} rendered={LongestRenderedText(card)}");
                SmokeTest.Report("copy-feedback-large=ok full=2000000 preview=bounded hidden=cleared");
                card.Close();
                SmokeTest.Report("copy-feedback=ok");

                var window = app.ShowSettings("general");
                window.PreviewSaveFailure(); await Wait(400);
                Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                Shot("settings-save-failed", new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                app.RetrySaveSettings(); await Wait(300);
                if (app.SettingsSaveError is not null || !app.SettingsHaveBeenSaved)
                    throw new InvalidOperationException("保存重试没有恢复成功状态");
                Shot("settings-save-retried", new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                window.Close();
                SmokeTest.Report("settings-save-feedback=ok");
            });

        if (Want("settings-search"))
            await Scene("settings-search", async () =>
            {
                if (SettingsCatalog.Entries.Any(entry => !SettingsWindow.Pages.Any(page => page.Id == entry.Page)))
                    throw new InvalidOperationException("搜索索引包含不存在的设置页");
                var window = app.ShowSettings("general");
                window.PreviewSettingsSearch("剪贴板"); await Wait(350);
                Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                Shot("settings-search", new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                if (!window.OpenFirstSearchResult() || window.CurrentPage != "clipboard")
                    throw new InvalidOperationException("设置搜索没有跳转到匹配页面");
                window.PreviewSettingsSearch("no-setting-matches-this-query"); await Wait(250);
                if (window.OpenFirstSearchResult()) throw new InvalidOperationException("空结果不能打开其他设置");
                Shot("settings-search-empty", new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                window.PreviewSettingsSearch("");
                if (window.CurrentPage != "clipboard") throw new InvalidOperationException("清空搜索改变了原页面");
                window.Close(); SmokeTest.Report("settings-search=ok");
            });

        if (Want("settings-targets"))
            await Scene("settings-targets", async () =>
            {
                await SettingsSearchExperienceChecks.VerifyAsync(app, (name, window) =>
                {
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    Shot(name, new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                });
                SmokeTest.Report($"settings-targets=ok entries={SettingsCatalog.Entries.Count} short-window=ok keyboard=ok readonly=ok conditional=ok expand=ok editor=available nav=visible cancel=ok highlight=expired offset=restored");
            });

        if (Want("result-reading"))
            await Scene("result-reading", async () =>
            {
                await ResultReadingChecks.VerifyAsync(app, x, y, (name, card) =>
                {
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(card).Handle, out var rect);
                    Shot(name, new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                });
                SmokeTest.Report("result-reading=ok bounded=ok full-copy=ok full-replace=ok source=ok rows=ok paging=ok small-workarea=ok loading=cancelled close=released focus=preserved");
            });

        if (Want("tools-settings"))
            await Scene("tools-settings", async () =>
            {
                await ToolSettingsChecks.VerifyAsync(app, (name, window) =>
                {
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    Shot(name, new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                });
                SmokeTest.Report("tools-settings=ok counter=live bulk=live filter=readonly scope=matched focus=preserved scroll=preserved locked=on esc=clear");
            });

        if (Want("window-workarea"))
            await Scene("window-workarea", async () =>
            {
                await WindowWorkAreaChecks.VerifyAsync(app, (name, window) =>
                {
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    Shot(name, new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                });
                SmokeTest.Report("window-workarea=ok moved=fit moving=free minimum=restored foreground=preserved normal=only closed=cancelled settings=unchanged");
            });

        if (Want("usage-guide"))
            await Scene("usage-guide", async () =>
            {
                var before = app.Settings.ToJson();
                var enabled = app.Settings.Enabled;
                var window = app.ShowSettings("general");
                var guide = window.PreviewUsageGuide();
                try
                {
                    await Wait(400);
                    if (guide.FindName("GuideFooterTip") is not TextBlock footer || !footer.IsVisible || footer.ActualHeight <= 0
                        || footer.TranslatePoint(new Point(), guide).Y + footer.ActualHeight > guide.ActualHeight - 12)
                        throw new InvalidOperationException("使用指南底部提示被裁切");
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(guide).Handle, out var rect);
                    Shot("usage-guide", new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                    if (app.Settings.ToJson() != before) throw new InvalidOperationException("打开使用指南改动了设置");
                    app.UpdateSettings(s => s.Enabled = false);
                    if (guide.FindName("GuideTriggerHint") is not TextBlock hint || !hint.Text.Contains("已暂停"))
                        throw new InvalidOperationException("使用指南没有更新当前鼠标暂停状态");
                    await Wait(200); Shot("usage-guide-paused");
                    app.UpdateSettings(s => s.Enabled = enabled);
                    if (guide.FindName("GuideOpenClipboard") is not Wpf.Ui.Controls.Button open)
                        throw new InvalidOperationException("指南缺少剪贴板设置入口");
                    open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (guide.IsVisible || window.CurrentPage != "clipboard" || app.Settings.ToJson() != before)
                        throw new InvalidOperationException("指南跳转未关闭窗口、未到达设置页或修改了配置");
                    SmokeTest.Report("usage-guide=ok readonly=ok live-state=ok navigation=ok");
                }
                finally
                {
                    if (guide.IsVisible) guide.Close();
                    app.UpdateSettings(s => s.Enabled = enabled); app.FlushPendingSettings();
                    if (window.IsVisible) window.Close();
                }
            });

        if (Want("backup-preview"))
            await Scene("backup-preview", async () =>
            {
                var before = app.Settings.ToJson();
                var settings = AppSettings.FromJson(before);
                settings.HoldMilliseconds = settings.HoldMilliseconds == 350 ? 400 : 350;
                settings.ClipboardRetentionDays = 90;
                settings.TranslateTarget = "ja";
                settings.Snippets = [new("preview-example", "会议回复", "收到，我会准时参加会议。")];
                var plugin = PluginTemplates.All.First().Manifest with { Id = "preview-only", Name = "项目搜索" };
                var backup = new SettingsBackup(settings, [plugin], "0.13.0");
                var window = app.ShowSettings("backup");
                var dialog = window.CreateBackupPreview(backup);
                var showing = dialog.ShowDialogAsync(showAsDialog: false); await Wait(450);
                if (!SettingsWindow.BackupScopeIsVisible(dialog)) throw new InvalidOperationException("备份导入范围说明被裁切");
                Shot("backup-preview"); dialog.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Close); await showing;
                var identical = window.CreateBackupPreview(new(app.Settings, app.Plugins.Manifests));
                var showingIdentical = identical.ShowDialogAsync(showAsDialog: false); await Wait(300);
                if (!SettingsWindow.BackupScopeIsVisible(identical)) throw new InvalidOperationException("相同配置预览中的导入范围说明被裁切");
                Shot("backup-preview-unchanged"); identical.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Close); await showingIdentical;
                if (app.Settings.ToJson() != before || app.Plugins.Manifests.Any(p => p.Id == "preview-only"))
                    throw new InvalidOperationException("取消导入预览修改了当前设置或插件");
                window.Close(); SmokeTest.Report("backup-preview=ok cancel=ok unchanged=ok");
            });

        if (Want("settings-recovery"))
            await Scene("settings-recovery", async () =>
            {
                var original = AppSettings.FromJson(app.Settings.ToJson());
                var directory = Path.Combine(Path.GetTempPath(), $"pop-settings-recovery-{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "settings.json");
                var blockedRecovery = Path.Combine(directory, "Recovery");
                SettingsWindow? window = null;
                try
                {
                    const string raw = "{原来的配置还没有修复";
                    File.WriteAllText(path, raw);
                    app.PreviewSettingsFile(path);
                    var recoveryFlyout = new TrayFlyout(app);
                    try
                    {
                        recoveryFlyout.ShowNear((int)work.Right - 160, (int)Math.Max(work.Bottom, bounds.Bottom - 20));
                        await Wait(450);
                        if (!recoveryFlyout.RecoveryNoticeIsVisible) throw new InvalidOperationException("托盘中缺少设置恢复入口");
                        Shot("settings-recovery-tray");
                    }
                    finally { recoveryFlyout.Close(); }
                    await Wait(200);
                    window = app.ShowSettings("general"); await Wait(450);
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    var area = new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32);
                    if (!app.SettingsReadProblem || app.Settings.Enabled || app.Settings.ClipboardHistory || app.Settings.ClipboardAutomaticCleanup || !window.RecoveryNoticeIsVisible
                        || File.ReadAllText(path) != raw)
                        throw new InvalidOperationException("设置异常未准确显示、提示被裁切或原文件已修改");
                    Shot("settings-recovery-needed", area);
                    File.WriteAllText(blockedRecovery, "阻止创建副本目录");
                    app.UpdateSettings(s => s.HoldMilliseconds = 600);
                    if (app.RetrySaveSettings() || File.ReadAllText(path) != raw || !app.SettingsReadProblem)
                        throw new InvalidOperationException("保留副本失败时覆盖了原设置");
                    await Wait(200); Shot("settings-recovery-blocked", area);
                    File.Delete(blockedRecovery);
                    app.UpdateSettings(s => s.HoldMilliseconds = 600);
                    const string stillBroken = "{重新读取时仍未修复";
                    File.WriteAllText(path, stillBroken);
                    if (app.ReloadSettings() || app.SettingsSavePending || app.Settings.HoldMilliseconds != 600)
                        throw new InvalidOperationException("重新读取失败后没有保留改动或停止旧的排队写入");
                    await Wait(500);
                    if (File.ReadAllText(path) != stillBroken) throw new InvalidOperationException("重新读取失败后擅自保存了临时设置");
                    if (!app.RetrySaveSettings() || app.SettingsReadProblem || app.SettingsRecoveryPath is not { } preserved
                        || File.ReadAllText(preserved) != stillBroken || AppSettings.Load(path).HoldMilliseconds != 600 || AppSettings.Load(path).ClipboardAutomaticCleanup)
                        throw new InvalidOperationException("重试没有先保留原文件再保存当前设置");
                    await Wait(200);
                    if (!window.RecoveryNoticeIsVisible) throw new InvalidOperationException("保留副本后的说明不可见");
                    Shot("settings-recovery-saved", area);

                    File.WriteAllText(path, "second broken file"); app.PreviewSettingsFile(path);
                    var imported = new AppSettings { HoldMilliseconds = 750, Enabled = false, ClipboardHistory = false };
                    var bundle = app.ImportBackup(new SettingsBackup(imported, []));
                    if (app.SettingsReadProblem || !File.Exists(bundle) || app.SettingsRecoveryPath is not { } importedOriginal
                        || File.ReadAllText(importedOriginal) != "second broken file" || AppSettings.Load(path).HoldMilliseconds != 750)
                        throw new InvalidOperationException("导入设置绕过了原文件保留");

                    File.WriteAllText(path, "third broken file"); app.PreviewSettingsFile(path);
                    var repaired = new AppSettings { HoldMilliseconds = 700, Enabled = false, ClipboardHistory = false }.ToJson();
                    File.WriteAllText(path, repaired); app.UpdateSettings(s => s.HoldMilliseconds = 500);
                    if (app.RetrySaveSettings() || File.ReadAllText(path) != repaired)
                        throw new InvalidOperationException("临时配置覆盖了外部已修复的文件");
                    await Wait(200); Shot("settings-recovery-conflict", area);
                    if (!app.ReloadSettings() || app.SettingsReadProblem || app.SettingsSavePending
                        || app.Settings.HoldMilliseconds != 700 || File.ReadAllText(path) != repaired)
                        throw new InvalidOperationException("重新读取没有加载修复后的设置");
                    File.WriteAllText(path, "fourth broken file"); app.PreviewSettingsFile(path);
                    app.UpdateSettings(s => s.HoldMilliseconds = 550);
                    var current = app.Settings.ToJson(); File.Delete(path);
                    if (app.ReloadSettings() || File.Exists(path) || app.Settings.ToJson() != current || app.SettingsSavePending)
                        throw new InvalidOperationException("原文件消失时重新读取丢弃了当前改动或擅自新建文件");
                    await Wait(200); Shot("settings-recovery-missing", area);
                    if (!app.RetrySaveSettings() || AppSettings.Load(path).HoldMilliseconds != 550)
                        throw new InvalidOperationException("原文件消失后的明确保存未生成当前配置");
                    SmokeTest.Report("settings-recovery=ok preserve=ok retry=ok import=ok conflict=ok reload=ok missing=preserved");
                }
                finally
                {
                    app.PreviewSettingsFile(null, original);
                    if (window?.IsVisible == true) window.Close();
                    var target = Path.GetFullPath(directory);
                    var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), target);
                    if (!string.IsNullOrEmpty(Path.GetDirectoryName(relative)) || !relative.StartsWith("pop-settings-recovery-", StringComparison.Ordinal))
                        throw new InvalidOperationException("设置恢复检查目录超出临时范围");
                    Directory.Delete(target, recursive: true);
                }
            });

        if (Want("startup-feedback"))
            await Scene("startup-feedback", async () =>
            {
                await StartupExperienceChecks.VerifyAsync(app, (name, window) =>
                {
                    Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var rect);
                    Shot(name, new Drawing.Rectangle(rect.Left - 16, rect.Top - 16, rect.Right - rect.Left + 32, rect.Bottom - rect.Top + 32));
                });
                SmokeTest.Report("startup-feedback=ok confirmed=ok denied=actual unknown=disabled reload=ok mismatch=detected");
            });

        if (Want("settings-batching"))
            await Scene("settings-batching", async () =>
            {
                var original = app.Settings.HoldMilliseconds;
                var completed = 0;
                void Saved() { if (!app.SettingsSavePending && app.SettingsHaveBeenSaved && app.SettingsSaveError is null) completed++; }
                app.FlushPendingSettings();
                app.SettingsSaveStateChanged += Saved;
                var window = app.ShowSettings("general");
                try
                {
                    await Wait(450);
                    for (var n = 0; n < 10; n++) app.UpdateSettings(s => s.HoldMilliseconds = 200 + n * 40);
                    if (!app.SettingsSavePending || app.SettingsHaveBeenSaved || completed != 0 || app.Settings.HoldMilliseconds != 560)
                        throw new InvalidOperationException("连续修改没有立即生效或提前写入");
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (app.SettingsSavePending)
                    {
                        if (DateTime.UtcNow > deadline) throw new InvalidOperationException("待保存设置没有自动保存");
                        await Task.Delay(50);
                    }
                    if (completed != 1 || !app.SettingsHaveBeenSaved) throw new InvalidOperationException("连续修改没有合并成一次保存");
                    // 实际保存与计数已核对；单独定格短暂提示供明暗截图，不延长真实保存调度。
                    window.PreviewSavePending(true); await Wait(250); Shot("settings-pending-save");
                    window.PreviewSavePending(false);
                    app.UpdateSettings(s => s.HoldMilliseconds = 600);
                    if (!app.RetrySaveSettings() || app.SettingsSavePending) throw new InvalidOperationException("手动保存未清除待保存任务");
                    await Task.Delay(450);
                    if (completed != 2) throw new InvalidOperationException("手动保存后仍有重复排队写入");
                    app.UpdateSettings(s => s.HoldMilliseconds = 640); window.Close();
                    if (app.SettingsSavePending || completed != 3 || !app.SettingsHaveBeenSaved)
                        throw new InvalidOperationException("关闭设置未保存最新修改");
                    SmokeTest.Report("settings-batching=ok burst=10 writes=1 retry=ok close-flush=ok");
                }
                finally
                {
                    app.SettingsSaveStateChanged -= Saved;
                    app.UpdateSettings(s => s.HoldMilliseconds = original); app.FlushPendingSettings();
                    if (window.IsVisible) window.Close();
                }
            });

        backdrop.Close();
        SmokeTest.Report("demo-done");
    }

    /// 演示用的插件：临时文件夹里放几个模板插件
    public static string PluginFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pop-demo-plugins-{Guid.NewGuid():N}");
        using var store = new PluginStore(folder);
        foreach (var id in new[] { "github", "js-reverse", "shell-sort" })
            if (PluginTemplates.All.FirstOrDefault(t => t.Id == id) is { } template)
                store.Save(template.Manifest with { Id = PluginManifest.MakeId() });
        return folder;
    }

    private static Task Wait(int milliseconds) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds * Motion.Scale));

    private static int LongestRenderedText(DependencyObject element)
    {
        var maximum = element is System.Windows.Controls.TextBlock text ? text.Text.Length : 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            maximum = Math.Max(maximum, LongestRenderedText(VisualTreeHelper.GetChild(element, index)));
        return maximum;
    }

    /// 演示用的背景：一张彩色的桌面上摆着一页文档，其中一句是选中的样子
    private sealed class Backdrop : Window
    {
        private readonly Border selection;

        public Backdrop()
        {
            var dark = Theme.SystemIsDark();
            Title = "Pop 演示";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = Theme.TextFont;
            Theme.ApplyTextOptions(this);
            Background = new LinearGradientBrush(
                [
                    new GradientStop(dark ? Color.FromRgb(0x1B, 0x2A, 0x5C) : Color.FromRgb(0x5B, 0x8D, 0xEF), 0),
                    new GradientStop(dark ? Color.FromRgb(0x3F, 0x1D, 0x5C) : Color.FromRgb(0xA7, 0x8B, 0xFA), 0.55),
                    new GradientStop(dark ? Color.FromRgb(0x5C, 0x2A, 0x14) : Color.FromRgb(0xFD, 0xBA, 0x74), 1),
                ], new Point(0, 0), new Point(1, 1));

            var paper = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White;
            var ink = dark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x1F, 0x1F, 0x1F);
            var muted = dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x6B, 0x6B, 0x6B);
            var document = new StackPanel { Margin = new Thickness(56, 44, 56, 44) };
            document.Children.Add(new TextBlock { Text = "产品周报 · 第 39 周", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), Margin = new Thickness(0, 0, 0, 6) });
            document.Children.Add(new TextBlock { Text = "2026 年 9 月 30 日 · 设计组", FontSize = 13, Foreground = new SolidColorBrush(muted), Margin = new Thickness(0, 0, 0, 22) });
            document.Children.Add(Paragraph("这周完成了新版界面：圆盘、结果卡片和设置窗口都换成了新的样式，深色模式下也做了单独的配色。", ink));
            var line = new TextBlock { FontSize = 16, Foreground = new SolidColorBrush(ink), Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
            line.Inlines.Add(new Run("海外用户的反馈里最常见的一句是 "));
            selection = new Border
            {
                Background = new SolidColorBrush(dark ? Color.FromRgb(0x26, 0x4F, 0x78) : Color.FromRgb(0xCC, 0xE4, 0xFF)),
                Child = new TextBlock { Text = "Pop makes selected text useful", FontSize = 16, Foreground = new SolidColorBrush(ink) },
            };
            line.Inlines.Add(new InlineUIContainer(selection) { BaselineAlignment = BaselineAlignment.Bottom });
            line.Inlines.Add(new Run("，下周会整理成文档。"));
            document.Children.Add(line);
            document.Children.Add(Paragraph("剪贴板历史的搜索速度提升了一倍；截图识字支持了竖排文字。下一步是自定义插件和更多的翻译服务。", ink));
            document.Children.Add(Paragraph("预算：128*3+7 万元，折合 #FF8800 色的那一版海报。", ink));

            Content = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Width = 720,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = new SolidColorBrush(paper),
                        CornerRadius = new CornerRadius(10),
                        Child = document,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.25 },
                    },
                },
            };
        }

        private static TextBlock Paragraph(string text, Color ink) => new()
        {
            Text = text,
            FontSize = 16,
            LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ink),
            Margin = new Thickness(0, 0, 0, 14),
        };

        /// 铺满工作区（物理像素）
        public void ShowOn(Pop.Core.Rect area)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Show();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, IntPtr.Zero, (int)area.Left, (int)area.Top, (int)(area.Right - area.Left), (int)(area.Bottom - area.Top), Native.SWP_NOACTIVATE);
        }

        /// 「选中的文字」的中心（物理像素）
        public (int X, int Y) SelectionPoint()
        {
            UpdateLayout();
            var center = selection.PointToScreen(new Point(selection.ActualWidth / 2, selection.ActualHeight / 2));
            return ((int)center.X, (int)center.Y);
        }
    }
}
