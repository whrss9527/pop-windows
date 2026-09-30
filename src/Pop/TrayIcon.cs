using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Pop;

/// 通知区域（托盘）图标：左键或右键点一下弹出 Pop 的面板。发现新版本时图标右下角多一个小圆点，不弹窗打断
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon = new();
    private readonly Drawing.Icon normalIcon;
    private readonly Drawing.Icon badgeIcon;
    private readonly App app;
    private TrayFlyout? flyout;
    private long flyoutClosedAt;

    public TrayIcon(App app)
    {
        this.app = app;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Pop.ico")).Stream)
            normalIcon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        badgeIcon = WithBadge(normalIcon);
        icon.Icon = normalIcon;
        icon.Text = $"Pop {Updater.CurrentVersion}";
        icon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right) ToggleFlyout();
        };
        icon.Visible = true;
    }

    public void Dispose()
    {
        flyout?.Close();
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

    /// 打开面板；面板开着时点图标就关掉
    public void ToggleFlyout()
    {
        if (flyout is not null)
        {
            flyout.CloseAnimated();
            return;
        }
        // 面板开着时点图标：面板先因为失去焦点关掉了，这一下不要又把它打开
        if (Environment.TickCount64 - flyoutClosedAt < 300) return;
        Native.GetCursorPos(out var p);
        ShowFlyout(p.X, p.Y);
    }

    public TrayFlyout ShowFlyout(int x, int y)
    {
        flyout?.Close();
        var panel = new TrayFlyout(app);
        panel.Closed += (_, _) =>
        {
            flyoutClosedAt = Environment.TickCount64;
            if (ReferenceEquals(flyout, panel)) flyout = null;
        };
        flyout = panel;
        panel.ShowNear(x, y);
        return panel;
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
