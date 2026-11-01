namespace Pop.Core;

/// 关闭自动清理时不删除已有记录；恢复配置时也必须保留这一选择。
public readonly record struct ClipboardRetentionPolicy(bool Enabled, int Days, int Maximum)
{
    public static ClipboardRetentionPolicy For(AppSettings settings) =>
        new(settings.ClipboardAutomaticCleanup, settings.ClipboardRetentionDays, settings.ClipboardMaxItems);

    public int Cleanup(ClipboardStore store, DateTimeOffset now) => Enabled ? store.Cleanup(Days, Maximum, now) : 0;
}
