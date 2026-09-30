using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace Pop;

/// Windows 原生的 UI Automation 客户端，在 MTA 线程上调用
internal static class NativeAutomation
{
    private const int TextPatternId = 10014;

    /// 当前有键盘焦点的元素支持文字接口时返回 (true, 选中的文字)，不支持返回 (false, null)
    public static (bool Supported, string? Text) ReadFocusedSelection()
    {
        var automation = Create();
        IUIAutomationElement? element = null;
        IUIAutomationTextPattern? pattern = null;
        IUIAutomationTextRangeArray? ranges = null;
        try
        {
            element = automation.GetFocusedElement();
            if (element is null) return (false, null);
            pattern = element.GetCurrentPattern(TextPatternId) as IUIAutomationTextPattern;
            if (pattern is null) return (false, null);
            ranges = pattern.GetSelection();
            if (ranges is null) return (true, "");
            var parts = new List<string>();
            for (var i = 0; i < ranges.Length; i++)
            {
                var range = ranges.GetElement(i);
                try
                {
                    var text = range.GetText(-1);
                    if (!string.IsNullOrEmpty(text)) parts.Add(text);
                }
                finally
                {
                    Release(range);
                }
            }
            return (true, string.Join("\n", parts));
        }
        finally
        {
            Release(ranges);
            Release(pattern);
            Release(element);
            Release(automation);
        }
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
