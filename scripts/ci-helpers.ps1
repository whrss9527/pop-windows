# CI 脚本共用的函数：模拟鼠标键盘、截图、等待文件内容
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ('PopCi.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace PopCi {
    public static class Native {
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

        // 记事本的编辑区：老版本是 Edit，新版本是 RichEditD2DPT
        public static string ReadEditText(IntPtr top) {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(top, (h, _) => {
                var sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                var cls = sb.ToString();
                if (cls == "Edit" || cls.StartsWith("RichEdit")) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return null;
            int len = (int)SendMessage(found, 0x000E, IntPtr.Zero, IntPtr.Zero);
            var text = new StringBuilder(len + 1);
            SendMessage(found, 0x000D, (IntPtr)(len + 1), text);
            return text.ToString();
        }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
'@
}
[PopCi.Native]::SetProcessDPIAware() | Out-Null

function Invoke-RightDown { [PopCi.Native]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero) }
function Invoke-RightUp { [PopCi.Native]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero) }

function Invoke-Key([byte]$vk, [switch]$Ctrl) {
    if ($Ctrl) { [PopCi.Native]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }
    [PopCi.Native]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [PopCi.Native]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    if ($Ctrl) { [PopCi.Native]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 80
}

# 一小步一小步移动指针，钩子能收到连续的移动事件
function Move-Pointer([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY, [int]$Steps = 12) {
    for ($i = 1; $i -le $Steps; $i++) {
        $x = [int]($FromX + ($ToX - $FromX) * $i / $Steps)
        $y = [int]($FromY + ($ToY - $FromY) * $i / $Steps)
        [PopCi.Native]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 15
    }
}

# 前台窗口切换有限制：直接切不过去时先按一下 Alt 再切换。
# 已经在前台时不能按 Alt，不然会激活记事本的菜单栏
function Set-Foreground([IntPtr]$Hwnd) {
    if ([PopCi.Native]::GetForegroundWindow() -eq $Hwnd) { return }
    [PopCi.Native]::ShowWindow($Hwnd, 9) | Out-Null
    [PopCi.Native]::SetForegroundWindow($Hwnd) | Out-Null
    Start-Sleep -Milliseconds 200
    for ($i = 0; $i -lt 10; $i++) {
        if ([PopCi.Native]::GetForegroundWindow() -eq $Hwnd) { return }
        [PopCi.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [PopCi.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [PopCi.Native]::SetForegroundWindow($Hwnd) | Out-Null
        Start-Sleep -Milliseconds 200
        if ([PopCi.Native]::GetForegroundWindow() -eq $Hwnd) { return }
    }
    throw "没法把窗口切到前台"
}

function Save-Screenshot([string]$Path) {
    $w = [PopCi.Native]::GetSystemMetrics(78)
    $h = [PopCi.Native]::GetSystemMetrics(79)
    $x = [PopCi.Native]::GetSystemMetrics(76)
    $y = [PopCi.Native]::GetSystemMetrics(77)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "截图：$Path"
}

function Wait-FileContains([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            $text = Get-Content $Path -Raw -Encoding UTF8
            if ($text -match $Pattern) { return $text }
        }
        Start-Sleep -Milliseconds 250
    }
    $content = if (Test-Path $Path) { Get-Content $Path -Raw -Encoding UTF8 } else { '（文件不存在）' }
    throw "等了 ${TimeoutSeconds} 秒，$Path 里还是没有 /$Pattern/。内容：`n$content"
}

function Get-PopLog {
    $path = Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log'
    if (Test-Path $path) { Get-Content $path -Raw -Encoding UTF8 } else { '' }
}

function Get-FileProductVersion([string]$Path) {
    ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion -split '\+')[0]
}
