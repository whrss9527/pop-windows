namespace Pop;

/// 日志写在 %LOCALAPPDATA%\Pop\logs\pop.log，超过 1 MB 时把旧的改名成 pop.1.log
internal static class Log
{
    private static readonly object Gate = new();

    public static string Directory => Path.Combine(Paths.LocalData, "logs");
    public static string FilePath => Path.Combine(Directory, "pop.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Environment.ProcessId}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(FilePath, Path.Combine(Directory, "pop.1.log"), overwrite: true);
                File.AppendAllText(FilePath, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

internal static class Paths
{
    /// 设置：%APPDATA%\Pop
    public static string RoamingData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pop");

    /// 日志、更新下载：%LOCALAPPDATA%\Pop
    public static string LocalData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pop");

    public static string Settings => Path.Combine(RoamingData, "settings.json");

    public static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pop.exe");
}
