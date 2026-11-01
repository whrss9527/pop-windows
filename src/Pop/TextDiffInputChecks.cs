using Pop.Core;

namespace Pop;

internal sealed partial class TextDiffWindow
{
    internal static Task VerifyInputBoundaryAsync(App app, Action<string, TextDiffWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("文字对比边界检查仅用于演示回归");
        // 不显示超大文本窗口：只检查真实构造入口有没有先把超限全文交给编辑器。
        var window = new TextDiffWindow(new string('x', TextDiff.MaximumCharacters + 1), "保留的对照");
        try
        {
            if (window.before.Text.Length != 0 || window.compare.IsEnabled)
                throw new InvalidOperationException("超限初始全文被载入编辑器，或仍允许把未载入内容当作空白比较");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    }
}
