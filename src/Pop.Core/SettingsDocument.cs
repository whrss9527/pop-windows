using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pop.Core;

public enum SettingsReadState { Missing, Loaded, Invalid, Unreadable }

public sealed class SettingsFileChangedException() : IOException("原设置文件已变化或现已可读取，请重新读取设置后再保存");

/// 区分首次使用与读取失败；异常文件先按原始字节保留，再允许保存临时配置。
public sealed class SettingsDocument
{
    private string? observedHash;
    private SettingsDocument(string path, AppSettings settings, SettingsReadState state, string? error, string? hash = null)
    { FilePath = Path.GetFullPath(path); Settings = settings; State = state; ReadError = error; observedHash = hash; }

    public string FilePath { get; }
    public AppSettings Settings { get; private set; }
    public SettingsReadState State { get; private set; }
    public string? ReadError { get; private set; }
    public string? RecoveryPath { get; private set; }
    public string RecoveryDirectory => Path.Combine(Path.GetDirectoryName(FilePath)!, "Recovery");
    public bool HasLoadIssue => State is SettingsReadState.Invalid or SettingsReadState.Unreadable;

    public static SettingsDocument Open(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (TryRead(bytes, out var settings)) return new(path, settings!, SettingsReadState.Loaded, null);
            return new(path, TemporarySettings(), SettingsReadState.Invalid, "原设置文件格式不正确", Hash(bytes));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { return new(path, new AppSettings(), SettingsReadState.Missing, null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(path, TemporarySettings(), SettingsReadState.Unreadable, $"原设置文件无法读取：{e.Message}"); }
    }

    // 无法确认旧偏好时，不默默开始鼠标触发或剪贴板记录；设置和全局快捷键仍可使用。
    private static AppSettings TemporarySettings() => new()
    { Enabled = false, ClipboardHistory = false, SelectionToolbar = false, ClipboardAutomaticCleanup = false };

    private static bool TryRead(byte[] bytes, out AppSettings? settings)
    {
        settings = null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            settings = AppSettings.FromJson(json);
            return true;
        }
        catch (JsonException) { return false; }
    }

    public void Save(AppSettings settings)
    {
        var json = settings.ToJson();
        PreserveOriginal();
        // 临时文件准备好后再核对，防止保存期间用户已修复原配置。
        AtomicFile.WriteAllText(FilePath, json, HasLoadIssue ? VerifyOriginalUnchanged : null);
        Settings = settings; State = SettingsReadState.Loaded; ReadError = null; observedHash = null;
    }

    public void PreserveOriginal()
    {
        if (!HasLoadIssue) return;
        var bytes = ReadOriginal();
        if (RecoveryPath is { } existing && File.Exists(existing) && Hash(File.ReadAllBytes(existing)) == observedHash) return;
        Directory.CreateDirectory(RecoveryDirectory);
        var recovery = Path.Combine(RecoveryDirectory, $"原设置-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        try
        {
            using (var stream = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            RecoveryPath = recovery;
        }
        catch
        {
            try { File.Delete(recovery); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private byte[] ReadOriginal()
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(FilePath); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { throw new SettingsFileChangedException(); }
        var hash = Hash(bytes);
        // 原先打不开，现在是有效配置：先重新载入，不用临时默认值覆盖。
        if (observedHash is null)
        {
            if (TryRead(bytes, out _)) throw new SettingsFileChangedException();
            observedHash = hash;
        }
        else if (hash != observedHash) throw new SettingsFileChangedException();
        return bytes;
    }

    private void VerifyOriginalUnchanged()
    {
        ReadOriginal();
        if (RecoveryPath is null || Hash(File.ReadAllBytes(RecoveryPath)) != observedHash)
            throw new IOException("原文件副本已变化，尚未覆盖原设置，请重试保存");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
