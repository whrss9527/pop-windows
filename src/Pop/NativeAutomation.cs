using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace Pop;

/// Windows 原生的 UI Automation 客户端，在 MTA 线程上调用
internal static class NativeAutomation
{
    private const int TextPatternId = 10014;

    /// 当前有键盘焦点的元素支持文字接口时返回 (true, 选中的文字)，不支持返回 (false, null)
    public static (bool Supported, string? Text, Pop.Core.Rect? Bounds) ReadFocusedSelection(bool withBounds = false)
    {
        var automation = Create();
        IUIAutomationElement? element = null;
        IUIAutomationTextPattern? pattern = null;
        IUIAutomationTextRangeArray? ranges = null;
        try
        {
            element = automation.GetFocusedElement();
            if (element is null || element.CurrentIsPassword != 0) return (false, null, null);
            pattern = element.GetCurrentPattern(TextPatternId) as IUIAutomationTextPattern;
            if (pattern is null) return (false, null, null);
            ranges = pattern.GetSelection();
            if (ranges is null) return (true, "", null);
            var parts = new List<string>();
            Pop.Core.Rect? bounds = null;
            for (var i = 0; i < ranges.Length; i++)
            {
                var range = ranges.GetElement(i);
                try
                {
                    var text = range.GetText(-1);
                    if (!string.IsNullOrEmpty(text)) parts.Add(text);
                    if (withBounds && !string.IsNullOrEmpty(text) && range.GetBoundingRectangles() is double[] rectangles)
                        for (var n = 0; n + 3 < rectangles.Length; n += 4)
                        {
                            var x = rectangles[n]; var y = rectangles[n + 1];
                            var w = rectangles[n + 2]; var h = rectangles[n + 3];
                            if (!double.IsFinite(x + y + w + h) || w <= 0 || h <= 0) continue;
                            var r = new Pop.Core.Rect(x, y, x + w, y + h);
                            bounds = bounds is { } b ? new(Math.Min(b.Left, r.Left), Math.Min(b.Top, r.Top), Math.Max(b.Right, r.Right), Math.Max(b.Bottom, r.Bottom)) : r;
                        }
                }
                finally
                {
                    Release(range);
                }
            }
            return (true, string.Join("\n", parts), bounds);
        }
        finally
        {
            Release(ranges);
            Release(pattern);
            Release(element);
            Release(automation);
        }
    }

    /// 第一次用 UI Automation 要加载组件、建立连接，慢的机器上要好几百毫秒，超过读取的时限就只能改用复制。
    /// 启动后先在后台用一次（只取元素，不读内容）
    public static void WarmUp()
    {
        var thread = new Thread(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var automation = Create();
                try
                {
                    Release(automation.GetRootElement());
                    Release(automation.GetFocusedElement());
                }
                finally
                {
                    Release(automation);
                }
                Log.Info($"UI Automation 准备好了，用时 {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                Log.Info($"UI Automation 预热失败：{e.GetType().Name} {e.Message}");
            }
        })
        {
            IsBackground = true,
            Name = "Pop 预热 UI Automation",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private static IUIAutomation Create()
    {
        IUIAutomation automation;
        try
        {
            automation = new CUIAutomation8();
        }
        catch (COMException)
        {
            // Windows 7 以前没有 CUIAutomation8
            automation = new CUIAutomation();
        }
        if (automation is IUIAutomation2 withTimeouts)
        {
            // 目标 App 没有响应时别一直等（单位毫秒）
            withTimeouts.ConnectionTimeout = 500;
            withTimeouts.TransactionTimeout = 800;
        }
        return automation;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}
