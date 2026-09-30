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

        backdrop.Close();
        SmokeTest.Report("demo-done");
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
