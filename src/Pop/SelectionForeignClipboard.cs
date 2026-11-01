using System.Diagnostics;

namespace Pop;

/// 单独进程提供即时文字；只用于独立演示回归，避免把同进程窗口当作跨进程验证。
internal sealed class SelectionForeignClipboard : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pop-selection-proof-" + Guid.NewGuid().ToString("N"));
    private Process? process;

    public static async Task<SelectionForeignClipboard> StartAsync()
    {
        var fixture = new SelectionForeignClipboard();
        Directory.CreateDirectory(fixture.directory);
        try
        {
            var script = """
                $ErrorActionPreference='Stop'
                [IO.File]::WriteAllText('PHASE_FILE','powershell-started')
                Add-Type -TypeDefinition @'
                using System;
                using System.Runtime.InteropServices;
                public static class PopCopyProofOwner {
                    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
                    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr window);
                    [DllImport("user32.dll")] static extern bool CloseClipboard();
                    [DllImport("user32.dll")] static extern bool EmptyClipboard();
                    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
                    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr length);
                    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr data);
                    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr data);
                    static IntPtr window;
                    public static void Create() {
                        window=CreateWindowEx(0,"STATIC","Pop copy proof",0,0,0,0,0,new IntPtr(-3),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
                        if(window==IntPtr.Zero) throw new Exception("fixture window failed");
                    }
                    public static void Write() {
                        bool opened=false;
                        for(int i=0;i<20&&!opened;i++) { opened=OpenClipboard(window); if(!opened) System.Threading.Thread.Sleep(15); }
                        if(!opened) throw new Exception("fixture clipboard blocked");
                        try {
                            if(!EmptyClipboard()) throw new Exception("fixture empty failed");
                            byte[] bytes=System.Text.Encoding.Unicode.GetBytes("guard-foreign\0");
                            IntPtr memory=GlobalAlloc(0x42,new UIntPtr((uint)bytes.Length));
                            IntPtr pointer=GlobalLock(memory);
                            if(pointer==IntPtr.Zero) throw new Exception("fixture memory failed");
                            Marshal.Copy(bytes,0,pointer,bytes.Length); GlobalUnlock(memory);
                            if(SetClipboardData(13,memory)==IntPtr.Zero) throw new Exception("fixture set failed");
                        } finally { CloseClipboard(); }
                    }
                }
                '@
                [IO.File]::WriteAllText('PHASE_FILE','compiled')
                [PopCopyProofOwner]::Create()
                [IO.File]::WriteAllText('READY_FILE','ok')
                $watch=[Diagnostics.Stopwatch]::StartNew()
                while (-not [IO.File]::Exists('GO_FILE') -and $watch.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 10 }
                [PopCopyProofOwner]::Write()
                Start-Sleep -Seconds 15
                """;
            script = script.Replace("READY_FILE", Path.Combine(fixture.directory, "ready").Replace("'", "''"))
                .Replace("GO_FILE", Path.Combine(fixture.directory, "go").Replace("'", "''"))
                .Replace("PHASE_FILE", Path.Combine(fixture.directory, "phase").Replace("'", "''"));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-STA", "-Command", script }) start.ArgumentList.Add(argument);
            fixture.process = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立所有者进程");
            var watch = Stopwatch.StartNew();
            // 首次 PowerShell / 编译器加载在 CI 冷机上可能超过六秒；这是准备期限，不改变复制响应时限。
            while (!File.Exists(Path.Combine(fixture.directory, "ready")) && !fixture.process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(20);
            if (!File.Exists(Path.Combine(fixture.directory, "ready")))
            {
                var phase = File.Exists(Path.Combine(fixture.directory, "phase")) ? File.ReadAllText(Path.Combine(fixture.directory, "phase")) : "脚本尚未开始";
                throw new InvalidOperationException("独立所有者未准备好：" + phase + "，" + (fixture.process.HasExited ? await fixture.process.StandardError.ReadToEndAsync() : "启动超时"));
            }
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    public void Write() => File.WriteAllText(Path.Combine(directory, "go"), "go");
    public void Dispose()
    {
        if (process is not null)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); }
            process.Dispose();
        }
        var target = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(Path.GetFullPath(Path.GetTempPath()), target);
        if (!string.IsNullOrEmpty(Path.GetDirectoryName(relative)) || !relative.StartsWith("pop-selection-proof-", StringComparison.Ordinal)) throw new InvalidOperationException("独立所有者检查目录超出临时范围");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
