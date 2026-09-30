using System.Runtime.InteropServices;
using static Pop.Native;

namespace Pop;

/// 模拟鼠标和按键。发出去的事件都带上标记，回到 Pop 自己的钩子时直接放行
internal static class InputInjector
{
    /// "POP!"
    public static readonly IntPtr Marker = new(0x504F5021);

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;

    public static void RightDown() => Send(Mouse(MOUSEEVENTF_RIGHTDOWN));

    public static void RightClick() => Send(Mouse(MOUSEEVENTF_RIGHTDOWN), Mouse(MOUSEEVENTF_RIGHTUP));

    /// Ctrl + 某个键。用户按着的 Shift、Alt、Win 先临时松开，不然会变成别的快捷键
    public static void CtrlChord(ushort key)
    {
        var held = new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN }
            .Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)
            .ToArray();
        var inputs = new List<INPUT>();
        inputs.AddRange(held.Select(vk => Key(vk, up: true)));
        inputs.Add(Key(VK_CONTROL, up: false));
        inputs.Add(Key(key, up: false));
        inputs.Add(Key(key, up: true));
        inputs.Add(Key(VK_CONTROL, up: true));
        Send([.. inputs]);
    }

    private static INPUT Mouse(uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = Marker } },
    };

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Marker } },
    };

    private static void Send(params INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) Log.Error($"SendInput 只发出了 {sent}/{inputs.Length} 个事件，错误码 {Marshal.GetLastWin32Error()}");
    }
}
