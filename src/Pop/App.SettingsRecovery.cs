using Pop.Core;

namespace Pop;

internal sealed partial class App
{
    private SettingsDocument? settingsDocument;
    public string SettingsFilePath => settingsDocument?.FilePath ?? Paths.Settings;
    public bool SettingsReadProblem => settingsDocument?.HasLoadIssue == true;
    public string? SettingsRecoveryPath => settingsDocument?.RecoveryPath;
    public string? SettingsReadDetail => settingsDocument?.ReadError;
    public string? SettingsRecoveryMessage => SettingsReadProblem
        ? "当前使用临时配置；保存前会先保留原文件副本。读取不会改动原文件。"
        : SettingsRecoveryPath is not null ? "原文件副本已保留，可从目录中查看或修复。" : null;

    /// 成功才替换内存偏好；仍不可读时保留用户当前改动，并记录本次重新读取的文件版本。
    public bool ReloadSettings()
    {
        if (IsDemo && settingsDocument is null) return false;
        // 重新读取期间不让旧的排队写入接着覆盖；失败后由用户明确重试保存。
        settingsSaveTimer?.Stop(); SettingsSavePending = false;
        var candidate = SettingsDocument.Open(SettingsFilePath);
        settingsDocument = candidate;
        if (candidate.State == SettingsReadState.Missing)
        {
            SettingsHaveBeenSaved = false;
            SettingsSaveError = "没有找到原设置，已保留当前改动；保存将创建新的设置文件";
            SettingsSaveStateChanged?.Invoke();
            return false;
        }
        if (candidate.HasLoadIssue)
        {
            SettingsSaveError = candidate.ReadError;
            SettingsHaveBeenSaved = false;
            SettingsSaveStateChanged?.Invoke();
            return false;
        }
        Settings = candidate.Settings; SettingsSaveError = null;
        SettingsHaveBeenSaved = candidate.State == SettingsReadState.Loaded;
        ApplySettings(); SettingsSaveStateChanged?.Invoke();
        return true;
    }

    /// 只在原生演示回归中接入独立临时文件，不能触碰用户的真实设置。
    internal void PreviewSettingsFile(string? path, AppSettings? restore = null)
    {
        if (!IsDemo) throw new InvalidOperationException("设置文件预览仅用于演示");
        if (path is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), directory);
            if (!string.IsNullOrEmpty(Path.GetDirectoryName(relative)) || !relative.StartsWith("pop-settings-recovery-", StringComparison.Ordinal))
                throw new InvalidOperationException("设置文件预览必须使用独立临时目录");
        }
        settingsSaveTimer?.Stop(); SettingsSavePending = false;
        settingsDocument = path is null ? null : SettingsDocument.Open(path);
        Settings = settingsDocument?.Settings ?? restore ?? new AppSettings();
        SettingsSaveError = null; SettingsHaveBeenSaved = settingsDocument?.State == SettingsReadState.Loaded;
        ApplySettings(); SettingsSaveStateChanged?.Invoke();
    }
}
