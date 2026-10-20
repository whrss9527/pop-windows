using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Pop;

/// 演示/CI 专用：验证像素及资源生命周期，记录同一输入的新旧转换开销，不以易波动的时间阈值判定通过。
internal static class PerformanceChecks
{
    public static IReadOnlyList<string> Run()
    {
        VerifySnapshot();
        var lines = new List<string> { "screen-snapshot=ok" };
        foreach (var (width, height) in new[] { (1920, 1080), (3840, 2160) })
        {
            using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x / 8 + y / 16); pixels[i + 1] = (byte)(x / 16 + y / 8);
                pixels[i + 2] = (byte)(x / 32 ^ y / 32); pixels[i + 3] = 255;
            }
            var data = bitmap.LockBits(new(0, 0, width, height), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            BitmapSource Old() => ScreenCapture.ToBitmapSource(ScreenCapture.Png(bitmap));
            BitmapSource Direct() => ScreenCapture.ToBitmapSource(bitmap);
            var old = Old(); var direct = Direct();
            var converted = old.Format == PixelFormats.Bgra32 ? old : new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
            var oldPixels = new byte[pixels.Length]; var directPixels = new byte[pixels.Length];
            converted.CopyPixels(oldPixels, width * 4, 0); direct.CopyPixels(directPixels, width * 4, 0);
            if (!pixels.AsSpan().SequenceEqual(oldPixels) || !pixels.AsSpan().SequenceEqual(directPixels))
                throw new InvalidOperationException("屏幕快照转换改变像素");
            var png = Measure(Old); var bgra = Measure(Direct);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"performance: screen={width}x{height} png-ms={png.Milliseconds:0.###} direct-ms={bgra.Milliseconds:0.###} png-managed-bytes={png.Bytes} direct-managed-bytes={bgra.Bytes}"));
        }
        var sample = string.Concat(Enumerable.Repeat("剪贴板 preview line\r\n", 100_000));
        string Legacy()
        {
            var flat = string.Join(" ", sample.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            return flat.Length > 120 ? flat[..120] + "…" : flat;
        }
        string Bounded() => TextPreview.SingleLine(sample, 120);
        if (Legacy() != Bounded()) throw new InvalidOperationException("文本预览改变了显示内容");
        var split = Measure(Legacy); var prefix = Measure(Bounded);
        lines.Add("text-preview=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: preview-chars={sample.Length} split-ms={split.Milliseconds:0.###} prefix-ms={prefix.Milliseconds:0.###} split-managed-bytes={split.Bytes} prefix-managed-bytes={prefix.Bytes}"));
        var files = string.Join('\n', Enumerable.Range(0, 10_000).Select(i => $@"C:\文档\文件-{i}.txt"));
        int OldFiles()
        {
            if (files.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1) return 1;
            var count = files.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var preview = files.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray();
            GC.KeepAlive(preview); return count;
        }
        var summary = FileListSummary.Parse(files);
        if (summary.Count != OldFiles() || !summary.FirstPaths.SequenceEqual(files.Split('\n').Take(3)))
            throw new InvalidOperationException("文件预览改变了计数或顺序");
        var oldFiles = Measure(OldFiles); var newFiles = Measure(() => FileListSummary.Parse(files));
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: file-count={summary.Count} split-ms={oldFiles.Milliseconds:0.###} summary-ms={newFiles.Milliseconds:0.###} split-managed-bytes={oldFiles.Bytes} summary-managed-bytes={newFiles.Bytes}"));
        var snippet = new Snippet("performance", "", string.Concat(Enumerable.Repeat("标题\r\n正文内容\n", 10_000)));
        bool OldSearch()
        {
            var first = snippet.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First();
            return (first + "\n" + snippet.Text).Contains("not present", StringComparison.OrdinalIgnoreCase);
        }
        if (OldSearch() != snippet.Matches("not present")) throw new InvalidOperationException("短语搜索改变了结果");
        var concat = Measure(OldSearch); var search = Measure(() => snippet.Matches("not present"));
        var marked = snippet.Text.Replace("\r", "").Replace("\n", " ↵ ");
        if (snippet.DisplayPreview != marked[..160] + "…") throw new InvalidOperationException("短语预览改变了换行标记");
        var replace = Measure(() => snippet.Text.Replace("\r", "").Replace("\n", " ↵ "));
        var previewPrefix = Measure(() => snippet.DisplayPreview);
        lines.Add("list-content=ok files=10000 snippets=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: snippet-chars={snippet.Text.Length} concat-ms={concat.Milliseconds:0.###} search-ms={search.Milliseconds:0.###} concat-managed-bytes={concat.Bytes} search-managed-bytes={search.Bytes} replace-managed-bytes={replace.Bytes} preview-managed-bytes={previewPrefix.Bytes}"));
        var pluginText = ContentClassifier.Classify(new string('a', 300_000));
        var match = new PluginMatch { Kinds = [], MaxLength = 12 };
        bool OldLength() => new StringInfo(pluginText.Text).LengthInTextElements <= 12;
        bool BoundedLength() => PluginMatcher.Matches(match, pluginText);
        if (OldLength() != BoundedLength()) throw new InvalidOperationException("插件长度判断改变了结果");
        var whole = Measure(OldLength); var bounded = Measure(BoundedLength);
        lines.Add("plugin-match=ok graphemes=ok bounded=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: plugin-match-chars={pluginText.Text.Length} whole-ms={whole.Milliseconds:0.###} bounded-ms={bounded.Milliseconds:0.###} whole-managed-bytes={whole.Bytes} bounded-managed-bytes={bounded.Bytes}"));
        VerifyPluginOutputAndHash(lines);
        return lines;
    }

    private static void VerifyPluginOutputAndHash(List<string> lines)
    {
        var output = string.Concat(Enumerable.Repeat("first line\n", 100_000));
        var toast = new PluginManifest { Output = PluginOutput.Toast };
        ActionResult OldToast()
        {
            var first = output.Split(['\n', '\r', '\u000B', '\u000C', '\u0085', '\u2028', '\u2029'], StringSplitOptions.RemoveEmptyEntries).First();
            var elements = StringInfo.ParseCombiningCharacters(first);
            return ActionResult.Toast(elements.Length <= 60 ? first : first[..elements[60]]);
        }
        ActionResult NewToast() => PluginRunner.Present(output, toast, false);
        var input = PluginInput.Of(new string('a', 2_000_000));
        IReadOnlyDictionary<string, string> OldEnvironment() => new Dictionary<string, string>
        {
            ["POP_TEXT"] = input.Text[..StringInfo.ParseCombiningCharacters(input.Text)[PluginRunner.MaxEnvironmentLength]],
            ["POP_FILES"] = "", ["POP_KINDS"] = "",
        };
        IReadOnlyDictionary<string, string> NewEnvironment() => PluginRunner.Environment(input);
        var error = new ProcessOutput(1, "", input.Text, false);
        PluginRunResult OldError() => PluginRunResult.Failure(error.StandardError[..StringInfo.ParseCombiningCharacters(error.StandardError)[500]]);
        PluginRunResult NewError() => PluginRunner.ScriptOutput(error);
        if (OldToast() != NewToast() || OldEnvironment()["POP_TEXT"] != NewEnvironment()["POP_TEXT"] || OldError() != NewError())
            throw new InvalidOperationException("插件轻提示、错误输出或环境变量改变了内容");
        var oldToast = Measure(OldToast); var newToast = Measure(NewToast);
        var oldEnvironment = Measure(OldEnvironment); var newEnvironment = Measure(NewEnvironment);
        var oldError = Measure(OldError); var newError = Measure(NewError);
        lines.Add("plugin-output=ok toast=ok environment=ok error=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: plugin-toast-chars={output.Length} split-managed-bytes={oldToast.Bytes} prefix-managed-bytes={newToast.Bytes}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: plugin-environment-chars={input.Text.Length} whole-managed-bytes={oldEnvironment.Bytes} bounded-managed-bytes={newEnvironment.Bytes}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: plugin-error-chars={error.StandardError.Length} whole-managed-bytes={oldError.Bytes} bounded-managed-bytes={newError.Bytes}"));
        var capture = new ClipboardCapture(ClipboardKind.Text, new string('中', 500_000));
        string OldHash() => "text:" + Digests.Hex(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(capture.Text)));
        string NewHash() => capture.Hash;
        if (OldHash() != NewHash()) throw new InvalidOperationException("剪贴板去重摘要改变了格式");
        var oldHash = Measure(OldHash); var newHash = Measure(NewHash);
        lines.Add("clipboard-hash=ok utf8=ok bounded=ok");
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"performance: clipboard-hash-chars={capture.Text.Length} whole-ms={oldHash.Milliseconds:0.###} chunked-ms={newHash.Milliseconds:0.###} whole-managed-bytes={oldHash.Bytes} chunked-managed-bytes={newHash.Bytes}"));
    }

    public static void VerifyNavigation(int x, int y)
    {
        var probe = new NavigationProbe();
        try
        {
            probe.OpenAt(x, y); var initial = probe.RowsCreated;
            if (probe.SelectedId != "probe-0") throw new InvalidOperationException("列表没有选择第一项");
            probe.Move(1);
            if (probe.SelectedId != "probe-2") throw new InvalidOperationException("列表没有跳过禁用项");
            probe.Move(-1);
            if (probe.SelectedId != "probe-0") throw new InvalidOperationException("列表反向选择错误");
            probe.Move(-1);
            if (probe.SelectedId != "probe-99") throw new InvalidOperationException("列表环绕选择错误");
            if (initial != 100 || probe.RowsCreated != initial + 6)
                throw new InvalidOperationException("列表选择时重建了无关行");
            probe.Hide();
            if (probe.SelectedId is not null || probe.Content is not Grid { Children.Count: 0 })
                throw new InvalidOperationException("隐藏后仍保留列表和背景");
            probe.OpenAt(x, y);
            if (probe.SelectedId != "probe-0" || probe.RowsCreated != initial + 106)
                throw new InvalidOperationException("隐藏后重新打开列表失败");
            probe.Paste("1");
            if (probe.SelectedId != "probe-10") throw new InvalidOperationException("过滤后没有跳过禁用项");
            probe.Move(-1);
            if (probe.SelectedId != "probe-19") throw new InvalidOperationException("过滤后环绕选择错误");
            probe.Move(int.MaxValue); probe.Move(-int.MaxValue);
            if (probe.SelectedId != "probe-19") throw new InvalidOperationException("大步长选择溢出");
            probe.ChooseNth(0);
            if (probe.ChosenId != "probe-10") throw new InvalidOperationException("快捷直选没有使用过滤后的可选项");
            probe.OpenAt(x, y); probe.Paste("missing");
            probe.Move(1); probe.ChooseNth(0);
            if (probe.SelectedId is not null || probe.ChosenId != "probe-10") throw new InvalidOperationException("空列表选择错误");
        }
        finally { probe.Close(); }
    }

    public static async Task VerifySizingAsync(int x, int y)
    {
        var (work, _) = Native.MonitorAt(x, y);
        var area = new Pop.Core.Rect(work.Left + 20, work.Top + 20, Math.Min(work.Right, work.Left + 440), Math.Min(work.Bottom, work.Top + 320));
        var probe = new NavigationProbe { OverrideWorkArea = area };
        try
        {
            probe.OpenAt(x, y);
            var fullHeight = probe.Height;
            Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(probe).Handle, out var rect);
            if (rect.Left < area.Left - 1 || rect.Top < area.Top - 1 || rect.Right > area.Right + 1 || rect.Bottom > area.Bottom + 1)
                throw new InvalidOperationException("列表超出了小工作区");
            probe.Paste("99");
            if (probe.Height >= fullHeight - 30 || probe.SelectedId != "probe-99")
                throw new InvalidOperationException($"少量搜索结果没有收缩列表：{fullHeight:0.##} → {probe.Height:0.##}，选中 {probe.SelectedId}");
            probe.Backspace();
            if (probe.Height < fullHeight - 1)
                throw new InvalidOperationException($"增加搜索结果后列表没有恢复高度：{fullHeight:0.##} → {probe.Height:0.##}");
            probe.Move(-1);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            ScrollViewer? Find(System.Windows.DependencyObject parent)
            {
                if (parent is ScrollViewer viewer) return viewer;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    if (Find(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
                return null;
            }
            if (probe.SelectedId != "probe-99" || Find(probe)?.VerticalOffset is not > 0)
                throw new InvalidOperationException("小窗口内键盘选择没有滚动到可见项");
        }
        finally { probe.Close(); }
    }

    private sealed class NavigationProbe : SearchOverlay<PopAction>
    {
        public int RowsCreated { get; private set; }
        public string? SelectedId => SelectedItem?.Id;
        public string? ChosenId { get; private set; }
        public Pop.Core.Rect? OverrideWorkArea { get; init; }
        protected override (Pop.Core.Rect WorkArea, double Scale) WorkAreaAt(int x, int y)
        {
            var (work, scale) = base.WorkAreaAt(x, y);
            return (OverrideWorkArea ?? work, scale);
        }
        public NavigationProbe() : base("Pop 列表性能检查") { }
        public void OpenAt(int x, int y) => Open(x, y);
        protected override double PanelWidth => 380;
        protected override double ListHeight => 380;
        protected override string Placeholder => "性能检查";
        protected override IReadOnlyList<(string Keys, string Action)> Hints => [];
        protected override IEnumerable<Entry> Query(string text)
        {
            yield return new Entry("分组", null);
            for (var i = 0; i < 100; i++)
                if (text.Length == 0 || i.ToString(CultureInfo.InvariantCulture).StartsWith(text, StringComparison.Ordinal))
                    yield return new Entry(null, new PopAction("probe-" + i, "项目 " + i, "", "", ContentKind.None, _ => null), i != 1);
        }
        protected override FrameworkElement Row(PopAction item, bool selected, bool enabled, int index)
        { RowsCreated++; return new TextBlock { Text = item.Title, Height = 36 }; }
        protected override string EmptyText(string text) => "";
        protected override void OnChosen(PopAction item) => ChosenId = item.Id;
    }

    private static (double Milliseconds, long Bytes) Measure<T>(Func<T> convert)
    {
        var times = new List<double>(); var allocations = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            var image = convert(); watch.Stop();
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes); times.Add(watch.Elapsed.TotalMilliseconds);
            GC.KeepAlive(image);
        }
        times.Sort(); allocations.Sort(); return (times[1], allocations[1]);
    }

    private static void VerifySnapshot()
    {
        BitmapSource snapshot;
        using (var bitmap = new Drawing.Bitmap(2, 2, Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(0, 0, Drawing.Color.FromArgb(128, 11, 22, 33));
            snapshot = ScreenCapture.ToBitmapSource(bitmap);
            bitmap.SetPixel(0, 0, Drawing.Color.White);
        }
        var bytes = new byte[16]; snapshot.CopyPixels(bytes, 8, 0);
        if (!snapshot.IsFrozen || !bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 33, 22, 11, 128 }))
            throw new InvalidOperationException("快照依赖原位图或改变透明度");
        var memory = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, 0, memory, 16);
            using var inverted = new Drawing.Bitmap(2, 2, -8, Drawing.Imaging.PixelFormat.Format32bppArgb, IntPtr.Add(memory, 8));
            var expected = inverted.GetPixel(0, 0);
            var image = ScreenCapture.ToBitmapSource(inverted); image.CopyPixels(bytes, 8, 0);
            if (bytes[0] != expected.B || bytes[1] != expected.G || bytes[2] != expected.R)
                throw new InvalidOperationException("反向扫描线转换方向错误");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
