namespace Pop.Core;

/// 终端窗口里模拟 Ctrl+C 会结束正在运行的程序，读取选中内容时不能用这个兜底
public static class TerminalApps
{
    private static readonly HashSet<string> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass",
        "CASCADIA_HOSTING_WINDOW_CLASS",
        "PseudoConsoleWindow",
        "mintty",
        "PuTTY",
        "VirtualConsoleClass",
    };

    private static readonly HashSet<string> Processes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "alacritty", "wezterm-gui", "putty", "kitty",
        "ConEmu", "ConEmu64", "Hyper", "Tabby", "WindTerm", "MobaXterm", "Termius",
    };

    public static bool IsTerminal(string? windowClass, string? processName)
    {
        if (!string.IsNullOrEmpty(windowClass) && Classes.Contains(windowClass)) return true;
        if (string.IsNullOrEmpty(processName)) return false;
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        return Processes.Contains(name);
    }
}
