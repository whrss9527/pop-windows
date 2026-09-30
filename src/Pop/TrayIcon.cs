using System.Diagnostics;
using Pop.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Pop;

/// 通知区域（托盘）图标和菜单。发现新版本时图标右下角多一个小圆点，不弹窗打断
internal sealed class TrayIcon : IDisposable
{
    private static readonly int[] HoldChoices = [200, 250, 350, 500, 700];

    private readonly Forms.NotifyIcon icon = new();
    private readonly Drawing.Icon normalIcon;
    private readonly Drawing.Icon badgeIcon;
    private readonly App app;

    public TrayIcon(App app)
    {
        this.app = app;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Pop.ico")).Stream)
            normalIcon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        badgeIcon = WithBadge(normalIcon);
        icon.Icon = normalIcon;
        icon.Text = $"Pop {Updater.CurrentVersion}";
        icon.ContextMenuStrip = new Forms.ContextMenuStrip();
        icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowMenu();
        };
        icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) app.ShowSettings();
        };
        BuildMenu();
        icon.Visible = true;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        normalIcon.Dispose();
        badgeIcon.Dispose();
    }

    public void Notify(string title, string text, Forms.ToolTipIcon kind = Forms.ToolTipIcon.None) =>
        icon.ShowBalloonTip(5000, title, text, kind);

    public void Refresh()
    {
        var available = app.Updater.Available;
        icon.Icon = available is null ? normalIcon : badgeIcon;
        icon.Text = available is null ? $"Pop {Updater.CurrentVersion}" : $"Pop {Updater.CurrentVersion}（可以更新到 {available.Version}）";
    }

    private void ShowMenu()
    {
        // NotifyIcon 没有公开「在左键时弹出菜单」的方法，这是常用的做法
        typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(icon, null);
    }

    private void BuildMenu()
    {
        var menu = icon.ContextMenuStrip!;
        menu.Items.Clear();
        var settings = app.Settings;

        menu.Items.Add(new Forms.ToolStripMenuItem($"Pop {Updater.CurrentVersion}") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        var settingsItem = new Forms.ToolStripMenuItem("设置…") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
        settingsItem.Click += (_, _) => app.ShowSettings();
        menu.Items.Add(settingsItem);

        var enabled = new Forms.ToolStripMenuItem("长按右键唤起") { Checked = settings.Enabled };
        enabled.Click += (_, _) => app.UpdateSettings(s => s.Enabled = !s.Enabled);
        menu.Items.Add(enabled);

        var hold = new Forms.ToolStripMenuItem("长按时间");
        foreach (var ms in HoldChoices)
        {
            var item = new Forms.ToolStripMenuItem($"{ms} 毫秒") { Checked = settings.HoldMilliseconds == ms };
            item.Click += (_, _) => app.UpdateSettings(s => s.HoldMilliseconds = ms);
            hold.DropDownItems.Add(item);
        }
        menu.Items.Add(hold);

        var historyItem = new Forms.ToolStripMenuItem("剪贴板历史") { ShortcutKeyDisplayString = app.HistoryHotKey };
        historyItem.Click += (_, _) => app.ShowClipboardHistory();
        menu.Items.Add(historyItem);
        var ocr = new Forms.ToolStripMenuItem("截图识字") { ShortcutKeyDisplayString = app.OcrHotKey };
        ocr.Click += (_, _) => app.CaptureText();
        menu.Items.Add(ocr);
        var pin = new Forms.ToolStripMenuItem("截图贴图") { ShortcutKeyDisplayString = app.PinHotKey };
        pin.Click += (_, _) => app.CapturePin();
        menu.Items.Add(pin);
        if (PinWindow.Count > 0)
        {
            var closePins = new Forms.ToolStripMenuItem($"关闭所有贴图（{PinWindow.Count}）");
            closePins.Click += (_, _) => PinWindow.CloseAll();
            menu.Items.Add(closePins);
        }
        var historyMenu = new Forms.ToolStripMenuItem("剪贴板历史设置");
        var record = new Forms.ToolStripMenuItem("记录剪贴板历史") { Checked = settings.ClipboardHistory };
        record.Click += (_, _) => app.UpdateSettings(s => s.ClipboardHistory = !s.ClipboardHistory);
        historyMenu.DropDownItems.Add(record);
        foreach (var days in new[] { 1, 7, 30, 90, 365 })
        {
            var keep = new Forms.ToolStripMenuItem($"保存 {days} 天") { Checked = settings.ClipboardRetentionDays == days };
            keep.Click += (_, _) => app.UpdateSettings(s => s.ClipboardRetentionDays = days);
            historyMenu.DropDownItems.Add(keep);
        }
        historyMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        var clear = new Forms.ToolStripMenuItem("清空（保留固定的）");
        clear.Click += (_, _) => app.ClearClipboardHistory();
        historyMenu.DropDownItems.Add(clear);
        menu.Items.Add(historyMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var direct = new Forms.ToolStripMenuItem("选中算式、单位、颜色时直接出结果") { Checked = settings.DirectResults };
        direct.Click += (_, _) => app.UpdateSettings(s => s.DirectResults = !s.DirectResults);
        menu.Items.Add(direct);

        var autoStart = new Forms.ToolStripMenuItem("开机时启动") { Checked = SafeAutoStart() };
        autoStart.Click += (_, _) =>
        {
            try
            {
                AutoStart.Set(!AutoStart.IsEnabled);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Notify("Pop", $"设置开机启动失败：{e.Message}", Forms.ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());

        if (app.Updater.Available is { } release)
        {
            var install = new Forms.ToolStripMenuItem($"更新到 {release.Version}…") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
            install.Click += async (_, _) => await app.InstallUpdateAsync(release);
            menu.Items.Add(install);
        }
        else
        {
            var check = new Forms.ToolStripMenuItem(app.Updater.Status ?? "检查更新");
            check.Click += async (_, _) => await app.CheckForUpdatesAsync(userInitiated: true);
            menu.Items.Add(check);
        }

        var autoCheck = new Forms.ToolStripMenuItem("自动检查更新") { Checked = settings.CheckForUpdates };
        autoCheck.Click += (_, _) => app.UpdateSettings(s => s.CheckForUpdates = !s.CheckForUpdates);
        menu.Items.Add(autoCheck);

        var beta = new Forms.ToolStripMenuItem("接收测试版") { Checked = settings.IncludePrerelease };
        beta.Click += (_, _) => app.UpdateSettings(s => s.IncludePrerelease = !s.IncludePrerelease);
        menu.Items.Add(beta);

        var logs = new Forms.ToolStripMenuItem("打开日志文件夹");
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true });
        };
        menu.Items.Add(logs);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var quit = new Forms.ToolStripMenuItem("退出 Pop");
        quit.Click += (_, _) => app.Quit();
        menu.Items.Add(quit);
    }

    private static bool SafeAutoStart()
    {
        try
        {
            return AutoStart.IsEnabled;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private static Drawing.Icon WithBadge(Drawing.Icon source)
    {
        using var bitmap = source.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = bitmap.Width * 0.45f;
            var rect = new Drawing.RectangleF(bitmap.Width - d, bitmap.Height - d, d - 0.5f, d - 0.5f);
            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 255, 149, 0));
            using var pen = new Drawing.Pen(Drawing.Color.White, Math.Max(1, bitmap.Width / 16f));
            g.FillEllipse(brush, rect);
            g.DrawEllipse(pen, rect);
        }
        var handle = bitmap.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }
}
