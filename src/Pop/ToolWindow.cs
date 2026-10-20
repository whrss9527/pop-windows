using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Pop;

/// 需要输入文字的工具窗口共用标题栏、Mica、字号和工作区约束。
internal class ToolWindow : Wpf.Ui.Controls.FluentWindow
{
    protected Grid Body { get; } = new() { Margin = new Thickness(22, 12, 22, 22) };
    public void BodyContent(UIElement content) { Body.Children.Clear(); Body.Children.Add(content); }
    public ToolWindow(string title, double width = 680, double height = 500, bool closeOnEscape = true)
    {
        Title = title; Width = width; Height = height; MinWidth = Math.Min(520, width); MinHeight = Math.Min(340, height);
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
        WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Pop.ico"));
        FontFamily = Theme.TextFont; FontSize = Theme.Body;
        var theme = Theme.Current(); Background = theme.Brush(theme.Tint); Foreground = theme.Brush(theme.Text);
        Theme.ApplyTextOptions(this); SettingsUi.FitToWorkArea(this);
        var shell = new DockPanel();
        var titleBar = new Wpf.Ui.Controls.TitleBar { Title = title,
            Icon = new Wpf.Ui.Controls.ImageIcon { Source = Icons.AppIcon(16).Source, Width = 16, Height = 16 } };
        DockPanel.SetDock(titleBar, Dock.Top); shell.Children.Add(titleBar); shell.Children.Add(Body); Content = shell;
        if (closeOnEscape) PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.OriginalSource is not ComboBox { IsDropDownOpen: true }) { e.Handled = true; Close(); }
        };
    }
}
