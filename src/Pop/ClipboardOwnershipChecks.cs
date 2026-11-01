namespace Pop;

/// 使用真实的两个所有者窗口，检查原生剪贴板保护；只在演示回归中执行。
internal static class ClipboardOwnershipChecks
{
    public static void Verify()
    {
        using var first = new ClipboardAccess("Pop 剪贴板所有权检查一");
        using var second = new ClipboardAccess("Pop 剪贴板所有权检查二");
        var original = first.Save() ?? throw new InvalidOperationException("无法保存检查前的剪贴板");
        try
        {
            if (!first.SetText("original", temporary: true)) throw new InvalidOperationException("无法准备原始内容");
            var saved = first.Save()!;
            if (!first.SetText("temporary", temporary: true) || !first.Restore(saved, onlyIfOwned: true) || first.GetText() != "original")
                throw new InvalidOperationException("仍持有所有权时不能恢复原剪贴板");
            first.SetText("temporary", temporary: true);
            if (!second.SetText("newly copied", temporary: true)) throw new InvalidOperationException("无法准备新的复制内容");
            if (first.OwnsClipboard || first.Restore(saved, onlyIfOwned: true) || second.GetText() != "newly copied")
                throw new InvalidOperationException("恢复原内容覆盖了新的复制");
            if (first.SetText("fallback", temporary: true, onlyIfOwned: true) || second.GetText() != "newly copied")
                throw new InvalidOperationException("后备写入覆盖了新的复制");
        }
        finally
        {
            if (first.OwnsClipboard) first.Restore(original, onlyIfOwned: true);
            else if (second.OwnsClipboard) second.Restore(original, onlyIfOwned: true);
        }
    }
}
