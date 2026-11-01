namespace Pop.Core;

/// 交给监听线程的不可变记录策略；只规范化匹配快照，不改写用户配置。
public sealed class ClipboardRecordingPolicy
{
    private readonly HashSet<string> excluded;
    public bool Enabled { get; }

    public ClipboardRecordingPolicy(bool enabled, IEnumerable<string>? excludedApplications = null)
    {
        Enabled = enabled;
        excluded = new HashSet<string>((excludedApplications ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(AppSettings.ApplicationName).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    private ClipboardRecordingPolicy(bool enabled, HashSet<string> excluded) { Enabled = enabled; this.excluded = excluded; }

    public bool Allows(string? source) => Enabled && (string.IsNullOrWhiteSpace(source) || !excluded.Contains(AppSettings.ApplicationName(source)));
    public ClipboardRecordingPolicy WithEnabled(bool enabled) => Enabled == enabled ? this : new(enabled, excluded);
    public bool EquivalentTo(ClipboardRecordingPolicy? other) => other is not null && Enabled == other.Enabled && excluded.SetEquals(other.excluded);
}
