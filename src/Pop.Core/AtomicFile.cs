using System.Text;

namespace Pop.Core;

/// 同目录写入后替换目标；失败不覆盖原文件，也不遗留本次的临时文件。
public static class AtomicFile
{
    public static void WriteAllText(string path, string text, Action? beforeReplace = null)
    {
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            beforeReplace?.Invoke();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
