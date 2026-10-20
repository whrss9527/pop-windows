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

        var text = ContentClassifier.Classify("Pop makes selected text useful");

        if (Want("snippets"))
            await Scene("snippets", async () =>
            {
                var snippets = new SnippetWindow(); snippets.ShowAt(Snippet.Examples, x, y);
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
                    store.Add(ClipboardCapture.Files([@"C:\Users\Pop\Documents\季度报告.docx", @"C:\Users\Pop\Pictures\封面.png"], "explorer"), now.AddMinutes(-35));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "https://github.com/whrss9527/pop-windows", SourceApp: "chrome"), now.AddMinutes(-20));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "会议改到周四下午三点，地点不变。", SourceApp: "WeChat"), now.AddMinutes(-9));
                    var pinned = store.Add(new ClipboardCapture(ClipboardKind.Text, "hello@example.com", SourceApp: "Outlook"), now.AddMinutes(-3));
                    store.Add(new ClipboardCapture(ClipboardKind.Text, "Select any text, then press and hold the right mouse button.", SourceApp: "notepad"), now.AddMinutes(-1));
                    if (pinned is { } id) store.SetPinned(true, id);
                    var history = new ClipboardHistoryWindow();
                    history.ShowAt(store, x, y);
                    await Wait(500);
                    Shot("history");
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
                var ruler = new RegionSelector(); var measuring = ruler.SelectAsync(detectEdges: true);
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
